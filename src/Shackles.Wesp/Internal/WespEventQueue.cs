using System.Runtime.InteropServices;
using Shackles.Wesp.Interop;

namespace Shackles.Wesp.Internal;

internal sealed class WespEventQueue : IDisposable
{
    private const int MaximumRetainedActivities = 200;
    private const int MaximumArmedNotifications = 4;

    private readonly object _gate = new();
    private readonly Dictionary<Guid, WespRuleActivityDescriptor> _rules = [];
    private readonly List<WespActivity> _activities = [];
    private readonly List<nint> _notifications = [];
    private readonly NativeEventQueueNotificationCallback _notificationCallback;
    private readonly NativeEventQueueStateChangeCallback _stateChangeCallback;
    private readonly bool _canDecodeProcessDetails;
    private GCHandle _selfHandle;
    private nint _queue;
    private bool _connected;
    private bool _stateCallbackRegistered;
    private bool _closing;
    private bool _disposeInProgress;
    private bool _disposed;
    private string _captureStatus;

    private WespEventQueue(bool canDecodeProcessDetails)
    {
        _canDecodeProcessDetails = canDecodeProcessDetails;
        _notificationCallback = NotificationCallback;
        _stateChangeCallback = StateChangeCallback;
        _captureStatus = WithProcessDetailAvailability(
            "Listening for delayed, best-effort WESP activity.");
    }

    internal nint Handle
    {
        get
        {
            lock (_gate)
            {
                return _queue;
            }
        }
    }

    internal string CaptureStatus
    {
        get
        {
            lock (_gate)
            {
                return _captureStatus;
            }
        }
    }

    internal bool IsDisposed
    {
        get
        {
            lock (_gate)
            {
                return _disposed;
            }
        }
    }

    internal static WespEventQueue Create(nint client, bool canDecodeProcessDetails)
    {
        var instance = new WespEventQueue(canDecodeProcessDetails);
        try
        {
            instance.Initialize(client);
            return instance;
        }
        catch
        {
            instance.Dispose();
            throw;
        }
    }

    internal void RegisterRule(Guid ruleId, WespRuleActivityDescriptor descriptor)
    {
        lock (_gate)
        {
            _rules[ruleId] = descriptor;
        }
    }

    internal IReadOnlyList<WespActivity> GetActivities()
    {
        lock (_gate)
        {
            return _activities.ToArray();
        }
    }

    internal void RecordActivity(WespActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        lock (_gate)
        {
            if (_activities.Count == MaximumRetainedActivities)
            {
                _activities.RemoveAt(0);
            }

            _activities.Add(activity);
        }
    }

    private void Initialize(nint client)
    {
        var descriptor = new NativeEventQueueDescriptor
        {
            QueueType = EspEventQueueType.Async,
            Lifetime = EspEventQueueLifetime.ClientSession,
            NotificationVersion = EspEventNotificationVersion.Version1
        };
        Check(
            NativeMethods.EspCreateEventQueue(client, in descriptor, out _queue),
            "WESP could not create the activity queue.");

        _selfHandle = GCHandle.Alloc(this);
        var callbackContext = GCHandle.ToIntPtr(_selfHandle);
        Check(
            NativeMethods.EspConnectEventQueueWithCallback(
                _queue,
                maxWorkerThreadCount: 1,
                _notificationCallback,
                callbackContext),
            "WESP could not connect the activity queue.");
        _connected = true;

        Check(
            NativeMethods.EspSetEventQueueStateChangeCallback(
                _queue,
                _stateChangeCallback,
                callbackContext,
                memoryThresholdPercent: 80,
                recoveryThresholdPercent: 50),
            "WESP could not monitor the activity queue state.");
        _stateCallbackRegistered = true;

        var notificationCount = Math.Clamp(
            Environment.ProcessorCount,
            1,
            MaximumArmedNotifications);
        for (var index = 0; index < notificationCount; index++)
        {
            var notification = NativeMethods.EspAllocateEventNotification();
            if (notification == 0)
            {
                throw new WespException(
                    WespOperation.CreateEventQueue,
                    "WESP could not allocate an activity notification.");
            }

            _notifications.Add(notification);
            Check(
                NativeMethods.EspArmEventNotification(_queue, notification),
                "WESP could not arm an activity notification.");
        }
    }

    private static void NotificationCallback(nint notification, nint context)
    {
        try
        {
            if (context != 0 &&
                GCHandle.FromIntPtr(context).Target is WespEventQueue instance)
            {
                instance.HandleNotification(notification);
            }
        }
        catch
        {
            // Exceptions must never unwind through a native WESP callback.
        }
    }

    private void HandleNotification(nint notificationPointer)
    {
        try
        {
            var notification = Marshal.PtrToStructure<NativeEventNotification>(
                notificationPointer);
            if (notification.Version != EspEventNotificationVersion.Version1 ||
                notification.NotificationData == 0)
            {
                SetCaptureStatus(
                    "WESP returned an unsupported activity notification layout.");
                return;
            }

            var header = Marshal.PtrToStructure<NativeEventNotificationDataV1Header>(
                notification.NotificationData);
            if ((header.NotificationFlags & EspEventNotificationFlags.Disconnected) != 0)
            {
                // This flag describes when WESP emitted the event. It can be
                // delivered after a reconnect and is still a valid activity.
                SetCaptureStatus(
                    WithProcessDetailAvailability(
                        "Listening for WESP activity; a delayed entry was emitted while the queue was disconnected."));
            }

            WespRuleActivityDescriptor? descriptor;
            lock (_gate)
            {
                _rules.TryGetValue(header.RuleId, out descriptor);
            }

            if (descriptor is not null &&
                IsExpectedAction(header.RuleAction, descriptor.ActivityKind))
            {
                var observedAt = DateTimeOffset.Now;
                if (_canDecodeProcessDetails)
                {
                    var data = Marshal.PtrToStructure<NativeEventNotificationDataV1>(
                        notification.NotificationData);
                    RecordActivity(WespActivityDecoder.Decode(
                        in data,
                        descriptor,
                        observedAt));
                }
                else
                {
                    RecordActivity(WespActivityDecoder.DecodeWithoutProcessDetails(
                        in header,
                        descriptor,
                        observedAt));
                }
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            SetCaptureStatus(
                $"WESP activity could not be decoded: {exception.Message}");
        }
        finally
        {
            var completeResult = NativeMethods.EspCompleteEventNotification(
                notificationPointer);
            bool shouldRearm;
            nint queue;
            lock (_gate)
            {
                shouldRearm = !_closing && completeResult >= 0;
                queue = _queue;
            }

            if (shouldRearm)
            {
                var armResult = NativeMethods.EspArmEventNotification(
                    queue,
                    notificationPointer);
                if (armResult < 0)
                {
                    SetCaptureStatus(
                        "WESP activity capture stopped because a notification could not be rearmed.");
                }
            }
            else if (completeResult < 0)
            {
                SetCaptureStatus(
                    "WESP activity capture stopped because a notification could not be completed.");
            }
        }
    }

    private static void StateChangeCallback(
        EspEventQueueState state,
        nint data,
        nint context)
    {
        try
        {
            if (context == 0 ||
                GCHandle.FromIntPtr(context).Target is not WespEventQueue instance)
            {
                return;
            }

            instance.SetCaptureStatus(instance.WithProcessDetailAvailability(state switch
            {
                EspEventQueueState.Full =>
                    "The WESP activity queue is full; some entries may be missing.",
                EspEventQueueState.MemoryThresholdExceeded =>
                    "The WESP activity queue is under memory pressure; some entries may be missing.",
                EspEventQueueState.Recovered =>
                    "WESP activity capture recovered; earlier entries may be missing.",
                _ => "Listening for delayed, best-effort WESP activity."
            }));
        }
        catch
        {
            // Exceptions must never unwind through a native WESP callback.
        }
    }

    private void SetCaptureStatus(string status)
    {
        lock (_gate)
        {
            _captureStatus = status;
        }
    }

    private string WithProcessDetailAvailability(string status) =>
        _canDecodeProcessDetails
            ? status
            : status +
              " Process and target details are unavailable because this WESP DLL's notification layout has not been validated.";

    public void Dispose() => _ = Close();

    internal WespException? Close()
    {
        nint queue;
        lock (_gate)
        {
            if (_disposed)
            {
                return null;
            }

            if (_disposeInProgress)
            {
                return new WespException(
                    WespOperation.CloseSession,
                    "WESP activity cleanup is already in progress.");
            }

            _closing = true;
            _disposeInProgress = true;
            queue = _queue;
        }

        if (queue != 0 && _stateCallbackRegistered)
        {
            var removeCallbackResult =
                NativeMethods.EspRemoveEventQueueStateChangeCallback(queue);
            if (removeCallbackResult < 0)
            {
                SetCaptureStatus(
                    "WESP activity capture could not remove its state callback; its buffers remain allocated until cleanup is retried or the process exits.");
                lock (_gate)
                {
                    _disposeInProgress = false;
                }

                return WespException.FromHResult(
                    WespOperation.CloseSession,
                    removeCallbackResult,
                    "WESP could not remove the activity queue state callback.");
            }

            lock (_gate)
            {
                _stateCallbackRegistered = false;
            }
        }

        var disconnectResult = 0;
        if (_connected && queue != 0)
        {
            disconnectResult = NativeMethods.EspDisconnectEventQueue(queue);
        }

        if (disconnectResult < 0)
        {
            // Keep callbacks, their context, and armed buffers alive rather than
            // risking a native callback into released managed state.
            SetCaptureStatus(
                "WESP activity capture could not disconnect cleanly; its buffers remain allocated until cleanup is retried or the process exits.");
            lock (_gate)
            {
                _disposeInProgress = false;
            }
            return WespException.FromHResult(
                WespOperation.CloseSession,
                disconnectResult,
                "WESP could not disconnect the activity queue.");
        }

        _connected = false;
        var closeResult = 0;
        if (queue != 0)
        {
            closeResult = NativeMethods.EspCloseEventQueue(queue);
            lock (_gate)
            {
                // The preview header marks Queue _Post_invalid_, regardless of
                // the HRESULT. Never retry an operation with this handle.
                _queue = 0;
            }
        }

        foreach (var notification in _notifications)
        {
            NativeMethods.EspFreeEventNotification(notification);
        }

        _notifications.Clear();
        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }

        lock (_gate)
        {
            _disposed = true;
            _disposeInProgress = false;
        }

        if (closeResult < 0)
        {
            SetCaptureStatus(
                "WESP activity capture stopped, but WESP reported a queue-close error.");
            return WespException.FromHResult(
                WespOperation.CloseSession,
                closeResult,
                "WESP reported an error while closing the activity queue.");
        }

        SetCaptureStatus("WESP activity capture stopped.");
        return null;
    }

    private static bool IsExpectedAction(
        EspRuleAction ruleAction,
        WespActivityKind activityKind) =>
        activityKind switch
        {
            WespActivityKind.Blocked => ruleAction == EspRuleAction.Block,
            WespActivityKind.ProcessStarted => ruleAction == EspRuleAction.Notify,
            _ => false
        };

    private static void Check(int hresult, string detail)
    {
        if (hresult < 0)
        {
            throw WespException.FromHResult(
                WespOperation.CreateEventQueue,
                hresult,
                detail);
        }
    }
}
