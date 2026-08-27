namespace Shackles.JobObjects.Tests;

[TestClass]
public sealed class AssignmentDiagnosticTests
{
    [TestMethod]
    public void OtherJobEvidenceExplainsNestingWithoutInventingExistingJobProperties()
    {
        var evidence = new ProcessAssignmentDiagnosticEvidence(
            ProcessInAnyJob: true,
            ProcessInTargetJob: false,
            TargetMemberCount: 2,
            TargetUiRestrictionFlags: (uint)JobUiRestrictions.ReadClipboard);

        var suffix = JobObject.BuildAssignmentDiagnosticSuffix(evidence);

        Assert.AreEqual(
            " Read-only checks show that the process is already associated with at least one other job and is not a member of this target job." +
            " The target job reports 2 active members, so it is not empty." +
            " The target job reports UI restriction flags 0x00000002." +
            " The failure is consistent with Windows rejecting a nested association: valid nesting requires a compatible hierarchy/subset and neither job may have UI limits." +
            " These probes cannot identify or inspect the process's existing job, so its exact hierarchy and limits remain unknown." +
            " Running elevated does not bypass these kernel rules." +
            " Close and relaunch the process with Shackles' Launch in job action into the intended target, or retry with a new empty target job." +
            " Launching can still fail if an inherited parent job prevents a compatible nested hierarchy.",
            suffix);
    }

    [TestMethod]
    public void NoObservedExistingJobAddsNoSpeculativeDiagnosis()
    {
        var evidence = new ProcessAssignmentDiagnosticEvidence(
            ProcessInAnyJob: false,
            ProcessInTargetJob: false,
            TargetMemberCount: 3,
            TargetUiRestrictionFlags: (uint)JobUiRestrictions.ReadClipboard);

        Assert.AreEqual(string.Empty, JobObject.BuildAssignmentDiagnosticSuffix(evidence));
    }

    [TestMethod]
    public void NonAccessDeniedErrorsNeverAcquireNestedJobDiagnosis()
    {
        var evidence = new ProcessAssignmentDiagnosticEvidence(
            ProcessInAnyJob: true,
            ProcessInTargetJob: false,
            TargetMemberCount: 3,
            TargetUiRestrictionFlags: (uint)JobUiRestrictions.ReadClipboard);

        Assert.AreEqual(
            "AssignProcess failed (87): The parameter is incorrect.",
            JobObject.BuildAssignmentFailureMessage(
                87,
                "The parameter is incorrect.",
                targetHandleCanAssign: true,
                evidence));
    }

    [TestMethod]
    public void MissingTargetAssignRightTakesPrecedenceOverNestingEvidence()
    {
        var evidence = new ProcessAssignmentDiagnosticEvidence(
            ProcessInAnyJob: true,
            ProcessInTargetJob: false,
            TargetMemberCount: 3,
            TargetUiRestrictionFlags: (uint)JobUiRestrictions.ReadClipboard);

        var message = JobObject.BuildAssignmentFailureMessage(
            5,
            "Access is denied.",
            targetHandleCanAssign: false,
            evidence);

        StringAssert.Contains(message, "does not grant JOB_OBJECT_ASSIGN_PROCESS");
        Assert.IsFalse(message.Contains("nested association", StringComparison.Ordinal));
    }
}
