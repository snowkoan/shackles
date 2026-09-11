using System.ComponentModel;
using System.Runtime.InteropServices;
using Shackles.Wesp.Interop;

namespace Shackles.Wesp.Internal;

internal sealed record WespAppliedExistingProcess(
    TrackedWespProcess Tracking,
    WespReferencedProcessIdentity Identity);

internal static class WespExistingProcessAttacher
{
    private const uint WaitObject0 = 0;
    private const uint WaitTimeout = 258;
    private const uint WaitFailed = uint.MaxValue;
    private const ProcessAccessRights RequiredAccess =
        ProcessAccessRights.QueryLimitedInformation |
        ProcessAccessRights.Synchronize;

    internal static WespAppliedExistingProcess Apply(
        nint client,
        ulong policyId,
        int processId,
        long expectedCreationTimeFileTimeUtc)
    {
        SafeProcessHandle? process = OpenValidatedProcess(
            processId,
            expectedCreationTimeFileTimeUtc);
        try
        {
            var identity = WespProcessTagger.TagExistingProcess(
                client,
                checked((uint)processId),
                expectedCreationTimeFileTimeUtc,
                policyId,
                $"PID {processId}");
            EnsureRunning(process, processId);

            var tracking = new TrackedWespProcess(
                process,
                processId,
                expectedCreationTimeFileTimeUtc,
                WespProcessOrigin.Attached);
            process = null;
            return new WespAppliedExistingProcess(tracking, identity);
        }
        finally
        {
            process?.Dispose();
        }
    }

    internal static SafeProcessHandle OpenValidatedProcess(
        int processId,
        long expectedCreationTimeFileTimeUtc)
    {
        var rawProcess = NativeMethods.OpenProcess(
            RequiredAccess,
            inheritHandle: false,
            checked((uint)processId));
        var openError = Marshal.GetLastWin32Error();
        var process = new SafeProcessHandle(rawProcess);
        if (process.IsInvalid)
        {
            process.Dispose();
            throw FromWin32(
                WespOperation.OpenProcess,
                openError,
                $"Windows could not open PID {processId} with " +
                "PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE.");
        }

        try
        {
            if (!NativeMethods.GetProcessTimes(
                    process,
                    out var creationTime,
                    out _,
                    out _,
                    out _))
            {
                var timeError = Marshal.GetLastWin32Error();
                throw FromWin32(
                    WespOperation.ReadProcessIdentity,
                    timeError,
                    $"Windows could not read PID {processId}'s creation time.");
            }

            ValidateWin32Identity(
                processId,
                expectedCreationTimeFileTimeUtc,
                creationTime.ToLong());
            EnsureRunning(process, processId);
            return process;
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    internal static void ValidateWin32Identity(
        int processId,
        long expectedCreationTimeFileTimeUtc,
        long actualCreationTimeFileTimeUtc)
    {
        if (actualCreationTimeFileTimeUtc == expectedCreationTimeFileTimeUtc)
        {
            return;
        }

        throw new WespException(
            WespOperation.ReadProcessIdentity,
            $"PID {processId} now belongs to a different process; applying WESP Blocking was refused.");
    }

    internal static ProcessAccessRights GetRequiredAccess() => RequiredAccess;

    private static void EnsureRunning(SafeProcessHandle process, int processId)
    {
        var waitResult = NativeMethods.WaitForSingleObject(process, 0);
        if (waitResult == WaitTimeout)
        {
            return;
        }

        if (waitResult == WaitFailed)
        {
            var waitError = Marshal.GetLastWin32Error();
            throw FromWin32(
                WespOperation.ReadProcessIdentity,
                waitError,
                $"Windows could not check whether PID {processId} is still running.");
        }

        if (waitResult == WaitObject0)
        {
            throw new WespException(
                WespOperation.TagProcess,
                $"PID {processId} exited before WESP Blocking could be applied.");
        }

        throw new WespException(
            WespOperation.ReadProcessIdentity,
            $"Windows returned an unexpected wait result (0x{waitResult:X8}) for PID {processId}.");
    }

    private static WespException FromWin32(
        WespOperation operation,
        int error,
        string detail) =>
        new(
            operation,
            $"{detail} {new Win32Exception(error).Message}",
            unchecked((int)(0x80070000u | (uint)error)));
}
