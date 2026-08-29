namespace Shackles.ExperimentalSandboxes.Tests;

[TestClass]
public sealed class SandboxIntegrationTests
{
    [TestMethod]
    [TestCategory("Integration")]
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
        var creation = manager.CreateAndLaunch(
            new ExperimentalSandboxOptions
            {
                DisplayName = "Integration",
                UseAppContainer = false,
                NetworkMode = ExperimentalSandboxNetworkMode.Blocked
            },
            new ExperimentalSandboxLaunchOptions(command)
            {
                Arguments = "/d /c exit 0",
                IncludeTargetDirectoryReadAccess = false,
                IncludeWorkingDirectoryWriteAccess = false
            });

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
