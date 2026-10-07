namespace Shackles.Wfp.Tests;

[TestClass]
public sealed class WfpSessionCleanupTests
{
    [TestMethod]
    public void FailedEngineCloseRetainsHandleForSubsequentRetry()
    {
        var seenHandles = new List<nint>();
        var failuresRemaining = 2;
        using var session = new WfpSession(Guid.NewGuid(), (nint)123,
            engine =>
            {
                seenHandles.Add(engine);
                return failuresRemaining-- > 0 ? 5u : 0u;
            }, _ => true);

        var first = session.Close();
        Assert.IsFalse(first.DynamicSessionClosed);
        Assert.IsFalse(session.IsClosed);
        Assert.HasCount(1, first.Warnings);
        session.Dispose();
        Assert.IsFalse(session.IsClosed);
        var retry = session.Close();
        Assert.IsTrue(retry.DynamicSessionClosed);
        Assert.IsTrue(session.IsClosed);
        Assert.HasCount(3, seenHandles);
        Assert.IsTrue(seenHandles.All(handle => handle == (nint)123));

        session.Dispose();
        Assert.HasCount(3, seenHandles);
    }

    [TestMethod]
    public void ExplicitCleanupWarningDoesNotPreventConfirmedDynamicClose()
    {
        using var session = new WfpSession(Guid.NewGuid(), (nint)123, _ => 0u,
            warnings =>
            {
                warnings.Add("Explicit filter cleanup failed; dynamic close will remove policy.");
                return false;
            });

        var result = session.Close();

        Assert.IsFalse(result.ExplicitRemovalSucceeded);
        Assert.IsTrue(result.DynamicSessionClosed);
        Assert.IsTrue(session.IsClosed);
        Assert.HasCount(1, result.Warnings);
    }
}
