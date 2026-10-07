using Shackles.ExperimentalSandboxes.Internal;

namespace Shackles.ExperimentalSandboxes.Tests;

[TestClass]
public sealed class SandboxIntegrationTests
{
    [TestMethod]
    [TestCategory("WindowsIntegration")]
    public void LaunchesProcessWhenExperimentalApiIsEnabled()
    {
        using var manager = new ExperimentalSandboxManager();
        if (!manager.Support.IsAvailable)
        {
            Assert.Inconclusive(manager.Support.Summary);
        }

        var command = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "cmd.exe");
        ExperimentalSandboxCreationResult creation;
        try
        {
            creation = manager.CreateAndLaunch(
                new ExperimentalSandboxOptions
                {
                    DisplayName = "Integration",
                    UseAppContainer = false,
                    NetworkMode = ExperimentalSandboxNetworkMode.Blocked
                },
                new ExperimentalSandboxLaunchOptions(command)
                {
                    Arguments = "/d /c exit 0",
                    WorkingDirectory = Path.GetDirectoryName(command),
                    IncludeTargetDirectoryReadAccess = false,
                    IncludeWorkingDirectoryWriteAccess = false
                });
        }
        catch (ExperimentalSandboxException exception) when (
            exception.Operation == ExperimentalSandboxOperation.CreateProcess &&
            exception.InnerException is null &&
            exception.NativeErrorCode is { } error &&
            SandboxSupportProbe.IsUnsupportedError(error))
        {
            // Exports or a capability probe can succeed on a build whose create
            // implementation still rejects this experimental launch contract.
            Assert.Inconclusive(
                $"This Windows configuration does not support the experimental sandbox launch: {exception.Message}");
            return;
        }

        Assert.IsGreaterThan(0, creation.FirstLaunch.ProcessId);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (creation.Sandbox.GetSnapshot().ProcessIds.Count > 0 &&
               DateTime.UtcNow < deadline)
        {
            Thread.Sleep(50);
        }

        Assert.IsEmpty(creation.Sandbox.GetSnapshot().ProcessIds);
        Assert.IsTrue(creation.Sandbox.Close().Completed);
    }
}
