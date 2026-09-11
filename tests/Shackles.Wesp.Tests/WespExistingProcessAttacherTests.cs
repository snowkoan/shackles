using System.ComponentModel;
using System.Runtime.InteropServices;
using Shackles.Wesp.Internal;
using Shackles.Wesp.Interop;

namespace Shackles.Wesp.Tests;

[TestClass]
public sealed class WespExistingProcessAttacherTests
{
    [TestMethod]
    public void ExistingProcessUsesOnlyIdentityAndWaitAccess()
    {
        Assert.AreEqual(
            ProcessAccessRights.QueryLimitedInformation |
            ProcessAccessRights.Synchronize,
            WespExistingProcessAttacher.GetRequiredAccess());
    }

    [TestMethod]
    public void CurrentProcessCanBeOpenedWithItsStableIdentity()
    {
        var creationTime = CaptureCurrentProcessCreationTime();
        var process = WespExistingProcessAttacher.OpenValidatedProcess(
            Environment.ProcessId,
            creationTime);
        using var tracked = new TrackedWespProcess(
            process,
            Environment.ProcessId,
            creationTime,
            WespProcessOrigin.Attached);

        var info = tracked.GetInfo();

        Assert.IsTrue(info.IsRunning);
        Assert.AreEqual(WespProcessOrigin.Attached, info.Origin);
        Assert.AreEqual(Environment.ProcessId, info.ProcessId);
        Assert.AreEqual(creationTime, info.CreationTimeFileTimeUtc);
    }

    [TestMethod]
    public void StaleCreationTimeIsRejectedBeforeWespTagging()
    {
        var creationTime = CaptureCurrentProcessCreationTime();

        var exception = Assert.Throws<WespException>(() =>
            WespExistingProcessAttacher.OpenValidatedProcess(
                Environment.ProcessId,
                creationTime + 1));

        Assert.AreEqual(WespOperation.ReadProcessIdentity, exception.Operation);
        StringAssert.Contains(exception.Message, "different process");
    }

    [TestMethod]
    public void ApplyResultDistinguishesNewDuplicateAndFailedTargets()
    {
        var applied = new WespApplyProcessResult(
            1,
            10,
            WespApplyProcessStatus.Applied,
            ErrorMessage: null);
        var duplicate = applied with
        {
            Status = WespApplyProcessStatus.AlreadyApplied
        };
        var failed = applied with
        {
            Status = WespApplyProcessStatus.Failed,
            ErrorMessage = "failed"
        };

        Assert.IsTrue(applied.Succeeded);
        Assert.IsTrue(duplicate.Succeeded);
        Assert.IsFalse(failed.Succeeded);
        Assert.AreEqual("failed", failed.ErrorMessage);
    }

    private static long CaptureCurrentProcessCreationTime()
    {
        var rawProcess = NativeMethods.OpenProcess(
            WespExistingProcessAttacher.GetRequiredAccess(),
            inheritHandle: false,
            checked((uint)Environment.ProcessId));
        var openError = Marshal.GetLastWin32Error();
        using var process = new SafeProcessHandle(rawProcess);
        Assert.IsFalse(
            process.IsInvalid,
            $"OpenProcess failed: {new Win32Exception(openError).Message}");
        Assert.IsTrue(
            NativeMethods.GetProcessTimes(
                process,
                out var creationTime,
                out _,
                out _,
                out _),
            $"GetProcessTimes failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
        return creationTime.ToLong();
    }
}
