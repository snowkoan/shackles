namespace Shackles.JobObjects.Tests;

using Shackles.JobObjects.Interop;

[TestClass]
public sealed class ProcessIdentityCaptureTests
{
    [TestMethod]
    public void ProcessAccessMasksRemainLeastPrivilegeAndExact()
    {
        Assert.AreEqual(0x00001000U, (uint)JobObject.ProcessIdentityQueryAccess);
        Assert.AreEqual(0x00001101U, (uint)JobObject.ProcessAssignmentAccess);
    }

    [TestMethod]
    public void TryCaptureCurrentProcessReturnsStableKernelIdentity()
    {
        var first = JobObject.TryCaptureProcessIdentity(Environment.ProcessId);
        var second = JobObject.TryCaptureProcessIdentity(Environment.ProcessId);

        Assert.IsTrue(first.Succeeded, first.Error?.Message);
        Assert.IsTrue(second.Succeeded, second.Error?.Message);
        Assert.IsTrue(first.Identity.HasValue);
        Assert.IsTrue(second.Identity.HasValue);
        Assert.IsNull(first.Error);
        Assert.IsNull(second.Error);
        Assert.AreEqual(Environment.ProcessId, first.Identity.Value.ProcessId);
        Assert.IsTrue(first.Identity.Value.CreationTimeFileTimeUtc > 0);
        Assert.AreEqual(first.Identity, second.Identity);
    }

    [TestMethod]
    public void TryCaptureIdlePidReturnsActualOpenProcessFailure()
    {
        var result = JobObject.TryCaptureProcessIdentity(0);

        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(result.Identity.HasValue);
        Assert.IsNotNull(result.Error);
        Assert.AreEqual(JobOperation.OpenProcess, result.Error.Operation);
        Assert.AreEqual(87, result.Error.NativeErrorCode);
        StringAssert.Contains(result.Error.Message, "OpenProcess could not open PID 0");
        StringAssert.Contains(result.Error.Message, "PROCESS_QUERY_LIMITED_INFORMATION (0x00001000)");
        StringAssert.Contains(result.Error.Message, "OpenProcess failed (87)");
    }

    [TestMethod]
    public void IdentityQueryAccessDeniedNamesOnlyTheLimitedQueryRight()
    {
        var detail = JobObject.BuildOpenProcessFailureDetail(
            42,
            JobObject.ProcessIdentityQueryAccess,
            "Access is denied.");
        var error = new JobObjectException(JobOperation.OpenProcess, 5, detail).ToError();

        Assert.AreEqual(JobOperation.OpenProcess, error.Operation);
        Assert.AreEqual(5, error.NativeErrorCode);
        Assert.AreEqual(
            "OpenProcess failed (5): OpenProcess could not open PID 42 to read its creation time." +
            " Requested process access: PROCESS_QUERY_LIMITED_INFORMATION (0x00001000)." +
            " Native error: Access is denied.",
            error.Message);
        Assert.IsFalse(error.Message.Contains("PROCESS_SET_QUOTA", StringComparison.Ordinal));
        Assert.IsFalse(error.Message.Contains("PROCESS_TERMINATE", StringComparison.Ordinal));
    }

    [TestMethod]
    public void AssignmentOpenFailureDetailNamesCombinedSafeAssignmentAccess()
    {
        var detail = JobObject.BuildOpenProcessFailureDetail(
            42,
            JobObject.ProcessAssignmentAccess,
            "Access is denied.");

        Assert.AreEqual(
            "OpenProcess could not open PID 42 for assignment and creation-time revalidation." +
            " Requested process access: PROCESS_SET_QUOTA | PROCESS_TERMINATE | PROCESS_QUERY_LIMITED_INFORMATION (0x00001101)." +
            " PROCESS_SET_QUOTA and PROCESS_TERMINATE are required by AssignProcessToJobObject;" +
            " PROCESS_QUERY_LIMITED_INFORMATION is required to revalidate the PID's creation time before assignment." +
            " Native error: Access is denied.",
            detail);
    }

    [TestMethod]
    public void GetProcessTimesFailureDetailNamesNativeApiAndAcceptedQueryRights()
    {
        var detail = JobObject.BuildGetProcessTimesFailureDetail(
            42,
            JobObject.DescribeProcessAccess(JobObject.ProcessIdentityQueryAccess),
            "Access is denied.");

        Assert.AreEqual(
            "GetProcessTimes could not read PID 42's creation time." +
            " The process handle access is PROCESS_QUERY_LIMITED_INFORMATION (0x00001000);" +
            " GetProcessTimes requires PROCESS_QUERY_INFORMATION or PROCESS_QUERY_LIMITED_INFORMATION." +
            " Native error: Access is denied.",
            detail);
    }

    [TestMethod]
    public void NegativePidRemainsInvalidCallerInput()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => JobObject.TryCaptureProcessIdentity(-1));
    }
}
