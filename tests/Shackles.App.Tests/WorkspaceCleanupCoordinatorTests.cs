using Shackles.App.Infrastructure;

namespace Shackles.App.Tests;

[TestClass]
public sealed class WorkspaceCleanupCoordinatorTests
{
    private static readonly string[] ExpectedVisits = ["first", "second", "third"];
    private static readonly string[] ExpectedWarnings = ["First: failed", "Second: resource remains"];
    [TestMethod]
    public async Task FailedWorkspaceDoesNotPreventRemainingCleanup()
    {
        var visited = new List<string>();
        var warnings = await WorkspaceCleanupCoordinator.CloseAsync(
        [
            ("First", () => { visited.Add("first"); throw new InvalidOperationException("failed"); }),
            ("Second", () => { visited.Add("second"); return Task.FromResult<IReadOnlyList<string>>(["resource remains"]); }),
            ("Third", () => { visited.Add("third"); return Task.FromResult<IReadOnlyList<string>>([]); })
        ]);
        CollectionAssert.AreEqual(ExpectedVisits, visited);
        CollectionAssert.AreEqual(ExpectedWarnings, warnings.ToArray());
    }

    [TestMethod]
    public async Task CleanupWaitsWithoutBlockingAndCanBeRetried()
    {
        var release = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var visits = 0;
        (string Name, Func<Task<IReadOnlyList<string>>> Close)[] steps =
        [
            ("Delayed", () => { visits++; return visits == 1 ? release.Task : Task.FromResult<IReadOnlyList<string>>([]); })
        ];
        var first = WorkspaceCleanupCoordinator.CloseAsync(steps);
        Assert.IsFalse(first.IsCompleted);
        release.SetResult(["retry required"]);
        Assert.AreEqual(1, (await first).Count);
        Assert.AreEqual(0, (await WorkspaceCleanupCoordinator.CloseAsync(steps)).Count);
        Assert.AreEqual(2, visits);
    }

    [TestMethod]
    public void DisposalFailureDoesNotSkipOtherWorkspacesAndCanBeRetried()
    {
        var firstVisits = 0;
        var secondVisits = 0;
        (string Name, Action Dispose)[] steps =
        [
            ("First", () => { if (++firstVisits == 1) throw new InvalidOperationException("temporary failure"); }),
            ("Second", () => { secondVisits++; })
        ];
        var failure = Assert.ThrowsExactly<AggregateException>(() => WorkspaceCleanupCoordinator.Dispose(steps));
        StringAssert.Contains(failure.InnerExceptions.Single().Message, "First: temporary failure");
        Assert.AreEqual(1, secondVisits);
        WorkspaceCleanupCoordinator.Dispose(steps);
        Assert.AreEqual(2, firstVisits);
        Assert.AreEqual(2, secondVisits);
    }
}
