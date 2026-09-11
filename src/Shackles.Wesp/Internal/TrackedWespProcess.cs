using Shackles.Wesp.Interop;

namespace Shackles.Wesp.Internal;

internal sealed class TrackedWespProcess : IDisposable
{
    private const uint WaitTimeout = 258;
    private SafeProcessHandle? _process;

    internal TrackedWespProcess(
        SafeProcessHandle process,
        int processId,
        long creationTimeFileTimeUtc,
        WespProcessOrigin origin)
    {
        _process = process;
        ProcessId = processId;
        CreationTimeFileTimeUtc = creationTimeFileTimeUtc;
        Origin = origin;
    }

    internal int ProcessId { get; }

    internal long CreationTimeFileTimeUtc { get; }

    internal WespProcessOrigin Origin { get; }

    internal bool IsRunning =>
        _process is { IsClosed: false, IsInvalid: false } process &&
        NativeMethods.WaitForSingleObject(process, 0) == WaitTimeout;

    internal WespTrackedProcessInfo GetInfo() => new(
        ProcessId,
        CreationTimeFileTimeUtc,
        IsRunning,
        Origin);

    internal void RequestTermination()
    {
        if (Origin == WespProcessOrigin.Launched &&
            _process is { IsClosed: false, IsInvalid: false } process &&
            IsRunning)
        {
            _ = NativeMethods.TerminateProcess(process, 1);
        }
    }

    internal void WaitForExit(uint milliseconds)
    {
        if (Origin == WespProcessOrigin.Launched &&
            _process is { IsClosed: false, IsInvalid: false } process)
        {
            _ = NativeMethods.WaitForSingleObject(process, milliseconds);
        }
    }

    public void Dispose()
    {
        _process?.Dispose();
        _process = null;
    }
}
