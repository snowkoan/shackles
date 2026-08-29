using System.ComponentModel;
using System.Runtime.InteropServices;
using Shackles.ExperimentalSandboxes.Interop;
using SafeWaitHandle = Microsoft.Win32.SafeHandles.SafeWaitHandle;

namespace Shackles.ExperimentalSandboxes.Internal;

internal sealed class TrackedSandboxProcess : IDisposable
{
    private const uint WaitObject0 = 0;
    private const uint WaitTimeout = 258;
    private const uint WaitFailed = uint.MaxValue;
    private readonly object _gate = new();
    private readonly SafeProcessHandle _process;
    private ProcessWaitHandle? _waitHandle;
    private RegisteredWaitHandle? _registeredWait;
    private Action<TrackedSandboxProcess>? _exitCallback;
    private bool _disposed;

    internal TrackedSandboxProcess(
        SafeProcessHandle process,
        ExperimentalSandboxLaunchResult result)
    {
        _process = process;
        Result = result;
    }

    internal ExperimentalSandboxLaunchResult Result { get; }

    internal int ProcessId => Result.ProcessId;

    internal bool HasExited
    {
        get
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return WaitForExitCore(0);
            }
        }
    }

    internal void StartMonitoring(Action<TrackedSandboxProcess> exitCallback)
    {
        ArgumentNullException.ThrowIfNull(exitCallback);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_registeredWait is not null)
            {
                throw new InvalidOperationException(
                    "This sandbox process is already being monitored.");
            }

            _exitCallback = exitCallback;
            _waitHandle = new ProcessWaitHandle(_process);
            _registeredWait = ThreadPool.RegisterWaitForSingleObject(
                _waitHandle,
                static (state, _) =>
                    ((TrackedSandboxProcess)state!).NotifyExited(),
                this,
                Timeout.InfiniteTimeSpan,
                executeOnlyOnce: true);
        }
    }

    internal string? TryTerminate()
    {
        lock (_gate)
        {
            if (_disposed || WaitForExitCore(0))
            {
                return null;
            }

            if (NativeMethods.TerminateProcess(_process, 1) != 0)
            {
                return null;
            }

            var error = Marshal.GetLastPInvokeError();
            return WaitForExitCore(0)
                ? null
                : $"Could not terminate directly launched PID {ProcessId}: " +
                  new Win32Exception(error).Message;
        }
    }

    internal bool WaitForExit(TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);

        var milliseconds = checked((uint)Math.Min(
            Math.Ceiling(timeout.TotalMilliseconds),
            int.MaxValue));
        lock (_gate)
        {
            return _disposed || WaitForExitCore(milliseconds);
        }
    }

    public void Dispose()
    {
        RegisteredWaitHandle? registeredWait;
        ProcessWaitHandle? waitHandle;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _exitCallback = null;
            registeredWait = _registeredWait;
            waitHandle = _waitHandle;
            _registeredWait = null;
            _waitHandle = null;
        }

        _ = registeredWait?.Unregister(null);
        waitHandle?.Dispose();
        _process.Dispose();
    }

    private bool WaitForExitCore(uint milliseconds)
    {
        var result = NativeMethods.WaitForSingleObject(_process, milliseconds);
        return result switch
        {
            WaitObject0 => true,
            WaitTimeout => false,
            WaitFailed => throw new Win32Exception(
                Marshal.GetLastPInvokeError()),
            _ => throw new InvalidOperationException(
                $"Windows returned unexpected wait result 0x{result:X8}.")
        };
    }

    private void NotifyExited()
    {
        Action<TrackedSandboxProcess>? callback;
        lock (_gate)
        {
            callback = _disposed ? null : _exitCallback;
        }

        callback?.Invoke(this);
    }

    private sealed class ProcessWaitHandle : WaitHandle
    {
        internal ProcessWaitHandle(SafeProcessHandle process)
        {
            SafeWaitHandle = new SafeWaitHandle(
                process.DangerousGetHandle(),
                ownsHandle: false);
        }
    }
}
