using System.Runtime.InteropServices;
using Shackles.Wesp.Interop;

namespace Shackles.Wesp.Internal;

internal readonly record struct WespProcessWaitResult(uint Result, int ErrorCode = 0);

internal interface IWespTrackedProcessHandle : IDisposable
{
    bool IsUsable { get; }
    WespProcessWaitResult Wait(uint milliseconds);
    int? RequestTermination();
}

internal sealed class TrackedWespProcess : IDisposable
{
    private const uint WaitObject0 = 0;
    private const uint WaitTimeout = 258;
    private const uint WaitFailed = uint.MaxValue;
    private IWespTrackedProcessHandle? _process;

    internal TrackedWespProcess(
        SafeProcessHandle process,
        int processId,
        long creationTimeFileTimeUtc,
        WespProcessOrigin origin)
        : this(new NativeProcessHandle(process), processId, creationTimeFileTimeUtc, origin)
    {
    }

    internal TrackedWespProcess(IWespTrackedProcessHandle process, int processId,
        long creationTimeFileTimeUtc, WespProcessOrigin origin)
    {
        _process = process;
        ProcessId = processId;
        CreationTimeFileTimeUtc = creationTimeFileTimeUtc;
        Origin = origin;
    }

    internal int ProcessId { get; }

    internal long CreationTimeFileTimeUtc { get; }

    internal WespProcessOrigin Origin { get; }

    internal bool IsRunning => GetInfo().IsRunning;

    internal WespTrackedProcessInfo GetInfo()
    {
        var result = ReadState(0);
        return new(ProcessId, CreationTimeFileTimeUtc, result.Running, Origin)
        {
            StateError = result.Error?.Message
        };
    }

    internal WespException? RequestTermination()
    {
        if (Origin != WespProcessOrigin.Launched)
        {
            return null;
        }

        var state = ReadState(0);
        if (!state.Running && state.Error is null)
        {
            return null;
        }

        if (_process is not { IsUsable: true } process)
        {
            return state.Error;
        }

        var error = process.RequestTermination();
        return error is { } code
            ? FromWin32Error(code, $"Windows could not terminate PID {ProcessId}.")
            : null;
    }

    internal WespException? WaitForExit(uint milliseconds) =>
        Origin == WespProcessOrigin.Launched ? ReadState(milliseconds).Error : null;

    private (bool Running, WespException? Error) ReadState(uint milliseconds)
    {
        if (_process is not { IsUsable: true } process)
        {
            return (false, new WespException(WespOperation.CloseSession,
                $"The state of PID {ProcessId} is unknown because its process handle is unavailable."));
        }

        var wait = process.Wait(milliseconds);
        return wait.Result switch
        {
            WaitObject0 => (false, null),
            WaitTimeout => (true, null),
            WaitFailed => (false, FromWin32Error(wait.ErrorCode,
                $"Windows could not determine whether PID {ProcessId} has exited.")),
            _ => (false, new WespException(WespOperation.CloseSession,
                $"Windows returned unexpected wait result 0x{wait.Result:X8} for PID {ProcessId}; its state is unknown."))
        };
    }

    private static WespException FromWin32Error(int error, string detail) =>
        WespException.FromHResult(WespOperation.CloseSession,
            error <= 0 ? unchecked((int)0x80004005) : unchecked((int)(0x80070000u | (uint)error)), detail);

    public void Dispose()
    {
        _process?.Dispose();
        _process = null;
    }

    private sealed class NativeProcessHandle(SafeProcessHandle process) : IWespTrackedProcessHandle
    {
        public bool IsUsable => !process.IsClosed && !process.IsInvalid;
        public WespProcessWaitResult Wait(uint milliseconds)
        {
            var result = NativeMethods.WaitForSingleObject(process, milliseconds);
            return new(result, result == WaitFailed ? Marshal.GetLastPInvokeError() : 0);
        }

        public int? RequestTermination() => NativeMethods.TerminateProcess(process, 1)
            ? null : Marshal.GetLastPInvokeError();

        public void Dispose() => process.Dispose();
    }
}
