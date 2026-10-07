using Shackles.ExperimentalSandboxes.Internal;

namespace Shackles.ExperimentalSandboxes.Tests;

[TestClass]
public sealed class CleanupRetryTests
{
    [TestMethod]
    public void FailedProfileCleanupRemainsOwnedAndCanBeRetried()
    {
        var attempts = 0;
        using var manager = new ExperimentalSandboxManager(() => new ExperimentalSandboxSupport(
            ExperimentalSandboxAvailability.Available, "Fake support", new Version(10, 0), null, true, false, null, null, []));
        using var sandbox = new ExperimentalSandbox(new SandboxIdentity("Shackles.Experimental.Test", null),
            new ExperimentalSandboxOptions { DisplayName = "Retry cleanup", UseAppContainer = false },
            () => ++attempts == 1 ? "Profile is temporarily busy." : null, profileMayExist: true);
        manager.TrackSandbox(sandbox);

        var first = manager.CloseAll().Single();
        Assert.IsFalse(first.Completed);
        Assert.IsTrue(sandbox.IsClosed);
        Assert.IsFalse(sandbox.CleanupCompleted);
        Assert.HasCount(1, manager.Sandboxes);
        Assert.ThrowsExactly<ObjectDisposedException>(() => sandbox.Launch(new ExperimentalSandboxLaunchOptions("never-launched.exe")));

        var second = manager.CloseAll().Single();
        Assert.IsTrue(second.Completed);
        Assert.IsEmpty(manager.Sandboxes);
        Assert.IsTrue(sandbox.Close().Completed);
        Assert.AreEqual(2, attempts, "Completed cleanup must not repeat native profile deletion.");
    }

    [TestMethod]
    public void CloseAllContinuesAfterOneSandboxCleanupFailure()
    {
        using var manager = new ExperimentalSandboxManager(() => new ExperimentalSandboxSupport(
            ExperimentalSandboxAvailability.Available, "Fake support", new Version(10, 0), null, true, false, null, null, []));
        using var failed = new ExperimentalSandbox(new SandboxIdentity("Failed", null),
            new ExperimentalSandboxOptions { DisplayName = "Failed", UseAppContainer = false },
            () => throw new IOException("Simulated cleanup error"), profileMayExist: true);
        using var successful = new ExperimentalSandbox(new SandboxIdentity("Successful", null),
            new ExperimentalSandboxOptions { DisplayName = "Successful", UseAppContainer = false });
        manager.TrackSandbox(failed);
        manager.TrackSandbox(successful);

        var results = manager.CloseAll();
        Assert.HasCount(2, results);
        Assert.IsFalse(results[0].Completed);
        Assert.IsTrue(results[1].Completed);
        Assert.AreSame(failed, manager.Sandboxes.Single());
    }
}
