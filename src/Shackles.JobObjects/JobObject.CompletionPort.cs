using System.Runtime.InteropServices;
using System.Threading.Channels;
using Shackles.JobObjects.Interop;

namespace Shackles.JobObjects;

public sealed partial class JobObject
{
    private const uint CompletionPollMilliseconds = 500;
    private const uint CompletionStopMessage = uint.MaxValue;
    private const int ErrorWaitTimeout = 258;
    private const int NotificationEventCapacity = 256;
    private static readonly nint InvalidHandleValue = new(-1);
    private static readonly nuint JobCompletionKey = 0x4A4F42;
    private static readonly nuint StopCompletionKey = nuint.MaxValue;

    private readonly object _notificationGate = new();
    private SafeCompletionPortHandle? _completionPort;
    private Task? _notificationLoop;
    private Channel<JobNotificationEventArgs>? _notificationEvents;
    private Task? _notificationDispatcher;
    private CancellationTokenSource? _notificationDispatcherCancellation;
    private volatile bool _stopNotificationLoop;
    private volatile bool _notificationLoopAlive;
    private volatile bool _notificationCallbacksEnabled;
    private int _notificationCallbackThreadId;
    private long _droppedNotificationEventCount;
    private JobOperationError? _lastNotificationDetachError;

    /// <summary>
    /// Raised serially for packets consumed from this object's owned completion port. Handlers should
    /// be brief. The projection queue is bounded; authoritative state remains available via snapshots.
    /// </summary>
    public event EventHandler<JobNotificationEventArgs>? NotificationReceived;

    public long DroppedNotificationEventCount => Interlocked.Read(ref _droppedNotificationEventCount);

    /// <summary>
    /// The most recent failure to detach this object's completion port. Disposal remains no-throw;
    /// callers can inspect this property afterward when a named job may outlive the handle.
    /// </summary>
    public JobOperationError? LastNotificationDetachError
    {
        get
        {
            lock (_notificationGate)
            {
                return _lastNotificationDetachError;
            }
        }
    }

    public JobNotificationDeliveryMode NotificationDeliveryMode
    {
        get
        {
            lock (_notificationGate)
            {
                return _notificationLoopAlive && _completionPort is { IsInvalid: false, IsClosed: false }
                    ? JobNotificationDeliveryMode.OwnedCompletionPort
                    : JobNotificationDeliveryMode.SampledQueryOnly;
            }
        }
    }

    /// <summary>
    /// Creates, associates, owns, and consumes a private I/O completion port. New jobs call this
    /// automatically before they are returned. Opening an existing job remains sampled-only until
    /// the caller opts in, because another owner may already be monitoring it. Disposal attempts to
    /// detach the port and records any failure in LastNotificationDetachError.
    /// </summary>
    public void EnableNotificationDelivery()
    {
        lock (_notificationGate)
        {
            ThrowIfDisposed();
            if (_notificationLoopAlive && _completionPort is { IsInvalid: false, IsClosed: false })
            {
                return;
            }

            if (_completionPort is not null)
            {
                StopNotificationDeliveryCore();
            }

            var rawPort = NativeMethods.CreateIoCompletionPort(
                InvalidHandleValue,
                existingCompletionPort: 0,
                completionKey: 0,
                numberOfConcurrentThreads: 1);
            var error = Marshal.GetLastPInvokeError();
            var port = new SafeCompletionPortHandle(rawPort);
            if (port.IsInvalid)
            {
                port.Dispose();
                throw new JobObjectException(JobOperation.CreateCompletionPort, error);
            }

            var associated = false;
            try
            {
                var association = new NativeAssociateCompletionPort
                {
                    CompletionKey = (nint)JobCompletionKey,
                    CompletionPort = rawPort
                };
                Set(JobObjectInformationClass.AssociateCompletionPortInformation, association);
                associated = true;

                EnsureNotificationDispatcherCore();
                _stopNotificationLoop = false;
                _notificationLoopAlive = true;
                _completionPort = port;
                _notificationLoop = Task.Factory.StartNew(
                    () => RunNotificationLoop(port),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
                    TaskScheduler.Default);
            }
            catch
            {
                _notificationLoopAlive = false;
                if (associated)
                {
                    DetachCompletionPortNoThrow();
                }

                port.Dispose();
                throw;
            }
        }
    }

    private void RunNotificationLoop(SafeCompletionPortHandle port)
    {
        try
        {
            while (!_stopNotificationLoop)
            {
                var succeeded = NativeMethods.GetQueuedCompletionStatus(
                    port,
                    out var message,
                    out var completionKey,
                    out var messageValue,
                    CompletionPollMilliseconds);
                var error = Marshal.GetLastPInvokeError();

                if (_stopNotificationLoop ||
                    (message == CompletionStopMessage && completionKey == StopCompletionKey))
                {
                    return;
                }

                if (succeeded == 0)
                {
                    if (error == ErrorWaitTimeout)
                    {
                        continue;
                    }

                    DispatchNotification(new JobNotificationEventArgs(
                        rawMessageCode: 0,
                        processId: null,
                        limitViolations: null,
                        new JobObjectException(JobOperation.MonitorNotifications, error).ToError(),
                        DateTimeOffset.UtcNow));
                    return;
                }

                if (completionKey != JobCompletionKey)
                {
                    continue;
                }

                JobLimitViolations? violations = null;
                JobOperationError? queryError = null;
                if (message == (uint)JobNotificationMessageKind.NotificationLimit)
                {
                    try
                    {
                        // This query acknowledges the guaranteed soft-limit packet and rearms delivery.
                        violations = GetLimitViolationsCore();
                    }
                    catch (JobObjectException exception)
                    {
                        queryError = exception.ToError();
                    }
                }

                DispatchNotification(new JobNotificationEventArgs(
                    message,
                    GetMessageProcessId(message, messageValue),
                    violations,
                    queryError,
                    DateTimeOffset.UtcNow));
            }
        }
        finally
        {
            _notificationLoopAlive = false;
        }
    }

    private void DispatchNotification(JobNotificationEventArgs arguments)
    {
        var writer = _notificationEvents?.Writer;
        if (writer is null || !writer.TryWrite(arguments))
        {
            Interlocked.Increment(ref _droppedNotificationEventCount);
        }
    }

    private async Task RunNotificationDispatcherAsync(
        ChannelReader<JobNotificationEventArgs> reader,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var arguments in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!_notificationCallbacksEnabled)
                {
                    return;
                }

                var handlers = NotificationReceived;
                if (handlers is null)
                {
                    continue;
                }

                foreach (EventHandler<JobNotificationEventArgs> handler in handlers.GetInvocationList())
                {
                    if (!_notificationCallbacksEnabled)
                    {
                        return;
                    }

                    Volatile.Write(ref _notificationCallbackThreadId, Environment.CurrentManagedThreadId);
                    try
                    {
                        if (_notificationCallbacksEnabled)
                        {
                            handler(this, arguments);
                        }
                    }
                    catch
                    {
                        // A consumer callback cannot stop the owner loop or suppress later packets.
                    }
                    finally
                    {
                        Volatile.Write(ref _notificationCallbackThreadId, 0);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            Volatile.Write(ref _notificationCallbackThreadId, 0);
        }
    }

    private void StopNotificationDeliveryCore()
    {
        var port = _completionPort;
        var loop = _notificationLoop;
        if (port is null)
        {
            return;
        }

        _stopNotificationLoop = true;
        DetachCompletionPortNoThrow();

        if (!port.IsInvalid && !port.IsClosed)
        {
            _ = NativeMethods.PostQueuedCompletionStatus(
                port,
                CompletionStopMessage,
                StopCompletionKey,
                overlapped: 0);
        }

        try
        {
            _ = loop?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }

        port.Dispose();
        _notificationLoopAlive = false;
        _notificationLoop = null;
        _completionPort = null;
    }

    private void EnsureNotificationDispatcherCore()
    {
        if (_notificationDispatcher is { IsCompleted: false } && _notificationEvents is not null)
        {
            return;
        }

        _notificationEvents = Channel.CreateBounded<JobNotificationEventArgs>(new BoundedChannelOptions(NotificationEventCapacity)
        {
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true
        });
        _notificationDispatcherCancellation = new CancellationTokenSource();
        _notificationCallbacksEnabled = true;
        _notificationDispatcher = RunNotificationDispatcherAsync(
            _notificationEvents.Reader,
            _notificationDispatcherCancellation.Token);
    }

    private (Task? Dispatcher, CancellationTokenSource? Cancellation) StopNotificationDispatcherCore()
    {
        var events = _notificationEvents;
        var dispatcher = _notificationDispatcher;
        var cancellation = _notificationDispatcherCancellation;
        if (events is null)
        {
            return (dispatcher, cancellation);
        }

        _notificationCallbacksEnabled = false;
        cancellation?.Cancel();
        events.Writer.TryComplete();
        _notificationEvents = null;
        _notificationDispatcher = null;
        _notificationDispatcherCancellation = null;
        return (dispatcher, cancellation);
    }

    private unsafe void DetachCompletionPortNoThrow()
    {
        // Windows 8+ supports detaching by sending a zeroed association. This prevents a named job
        // that outlives this handle from retaining an unconsumed port after its owner is disposed.
        var association = new NativeAssociateCompletionPort();
        if (NativeMethods.SetInformationJobObject(
            _handle,
            JobObjectInformationClass.AssociateCompletionPortInformation,
            &association,
            checked((uint)sizeof(NativeAssociateCompletionPort))) == 0)
        {
            var error = Marshal.GetLastPInvokeError();
            _lastNotificationDetachError = new JobObjectException(
                JobOperation.AssociateCompletionPort,
                error).ToError();
        }
        else
        {
            _lastNotificationDetachError = null;
        }
    }

    private static int? GetMessageProcessId(uint message, nint messageValue)
    {
        var hasProcessId = message is
            (uint)JobNotificationMessageKind.EndOfProcessTime or
            (uint)JobNotificationMessageKind.NewProcess or
            (uint)JobNotificationMessageKind.ExitProcess or
            (uint)JobNotificationMessageKind.AbnormalExitProcess or
            (uint)JobNotificationMessageKind.ProcessMemoryLimit or
            (uint)JobNotificationMessageKind.JobMemoryLimit or
            (uint)JobNotificationMessageKind.NotificationLimit;

        return hasProcessId && messageValue > 0 && (nuint)messageValue <= int.MaxValue
            ? (int)messageValue
            : null;
    }
}
