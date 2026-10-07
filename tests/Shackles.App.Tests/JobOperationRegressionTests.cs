using Shackles.App.Models;
using Shackles.App.Services;
using Shackles.App.ViewModels;

namespace Shackles.App.Tests;

[TestClass]
public sealed class JobOperationRegressionTests
{
    [TestMethod]
    public async Task SuccessfulAssignmentSurvivesFailedSnapshot()
    {
        var session = new FakeSession { ReadSnapshot = () => throw new InvalidOperationException("snapshot unavailable") };
        using var job = CreateJob(session);
        var outcomes = await job.AssignProcessesAsync([new(42, 100)]);

        Assert.IsTrue(outcomes.Single().Succeeded);
        Assert.IsFalse(job.LastOperationFailed);
        Assert.IsTrue(job.IsSnapshotStale);
        Assert.IsFalse(job.HasSnapshot);
        Assert.IsFalse(job.CanEditDraft);
        StringAssert.Contains(job.LastOperationMessage, "Assigned 1 process.");
        StringAssert.Contains(job.LastOperationMessage, "snapshot unavailable");
        Assert.AreEqual(1, session.AssignmentCalls);
    }

    [TestMethod]
    public async Task SuccessfulLaunchSurvivesFailedSnapshotAndProcessListRefresh()
    {
        var session = new FakeSession { ReadSnapshot = () => throw new InvalidOperationException("snapshot unavailable") };
        using var job = CreateJob(session);
        using var main = new MainViewModel(new FakeService(session), () => throw new InvalidOperationException("process list unavailable"));

        var result = await main.LaunchProcessAsync(job, new("worker.exe", [], null));

        Assert.IsNotNull(result);
        Assert.AreEqual(42, result.ProcessId);
        Assert.AreEqual(1, session.LaunchCalls);
        Assert.IsFalse(main.StatusIsError);
        StringAssert.Contains(main.StatusMessage, "Launched worker");
        StringAssert.Contains(main.StatusMessage, "snapshot unavailable");
        StringAssert.Contains(main.StatusMessage, "process list unavailable");
    }

    [TestMethod]
    public async Task SuccessfulLaunchStatusSurvivesSuccessfulProcessRefresh()
    {
        var session = new FakeSession();
        using var job = CreateJob(session);
        using var main = new MainViewModel(new FakeService(session), () => []);
        Assert.IsNotNull(await main.LaunchProcessAsync(job, new("worker.exe", [], null)));
        StringAssert.Contains(main.StatusMessage, "Launched worker");
        Assert.IsFalse(main.StatusIsError);
    }

    [TestMethod]
    public async Task CreatedJobIsRetainedWhenInitialStateReadFails()
    {
        var session = new FakeSession { ReadSnapshot = () => throw new InvalidOperationException("snapshot unavailable") };
        using var main = new MainViewModel(new FakeService(session), () => []);
        var job = await main.CreateJobAsync(null);
        Assert.IsNotNull(job);
        Assert.AreSame(job, main.Jobs.Single());
        Assert.IsTrue(job.IsSnapshotStale);
        Assert.IsFalse(main.StatusIsError);
        StringAssert.Contains(main.StatusMessage, "Created");
        StringAssert.Contains(main.StatusMessage, "snapshot unavailable");
        Assert.IsFalse(main.HasPendingOperations);
    }

    [TestMethod]
    public async Task OpenedJobIsRetainedWhenInitialStateReadFails()
    {
        var session = new FakeSession { ReadSnapshot = () => throw new InvalidOperationException("snapshot unavailable") };
        using var main = new MainViewModel(new FakeService(session), () => []);
        var job = await main.OpenJobAsync("named-job");
        Assert.IsNotNull(job);
        Assert.AreSame(job, main.Jobs.Single());
        Assert.IsTrue(job.IsSnapshotStale);
        Assert.IsNull(main.LastOpenJobErrorMessage);
        StringAssert.Contains(main.StatusMessage, "Opened named job");
    }

    [TestMethod]
    public async Task PendingProcessRefreshIsTrackedForShutdown()
    {
        using var delay = new DelayedSnapshot();
        using var main = new MainViewModel(new FakeService(new FakeSession()), () => { delay.Read(); return []; });
        var operation = main.RefreshProcessesAsync();
        await delay.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try { Assert.IsTrue(main.HasPendingOperations); }
        finally { delay.Release(); }
        Assert.IsTrue(await operation);
        Assert.IsFalse(main.HasPendingOperations);
    }

    [TestMethod]
    public async Task ActualLaunchFailureRemainsAFailure()
    {
        var session = new FakeSession { LaunchError = new InvalidOperationException("launch rejected") };
        using var job = CreateJob(session);
        using var main = new MainViewModel(new FakeService(session), () => []);
        Assert.IsNull(await main.LaunchProcessAsync(job, new("worker.exe", [], null)));
        Assert.IsTrue(main.StatusIsError);
        Assert.AreEqual("launch rejected", main.StatusMessage);
        Assert.AreEqual(0, session.SnapshotCalls);
    }

    [TestMethod]
    public async Task MixedAssignmentResultsArePreservedWhenRefreshFails()
    {
        var session = new FakeSession
        {
            AssignmentResults = [new(42, "worker", true, "Assigned"), new(43, "other", false, "Access denied")],
            ReadSnapshot = () => throw new InvalidOperationException("snapshot unavailable")
        };
        using var job = CreateJob(session);
        using var main = new MainViewModel(new FakeService(session), () => []);
        var outcomes = await main.AssignProcessesAsync(job, [new(42, 100), new(43, 101)]);
        Assert.IsTrue(outcomes[0].Succeeded);
        Assert.IsFalse(outcomes[1].Succeeded);
        Assert.AreEqual("Access denied", outcomes[1].Message);
        Assert.IsTrue(main.StatusIsError);
        StringAssert.Contains(main.StatusMessage, "Assigned 1 of 2");
    }

    [TestMethod]
    public async Task RefreshPreservesEditsMadeDuringSnapshotRead()
    {
        using var delay = new DelayedSnapshot();
        var session = new FakeSession { ReadSnapshot = delay.Read };
        using var job = CreateJob(session);
        var operation = job.RefreshAsync();
        await delay.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Assert.IsTrue(job.IsBusy);
            Assert.IsFalse(job.CanEditDraft);
            job.Editor.KillOnJobClose = true;
        }
        finally { delay.Release(); }
        await operation;
        Assert.IsTrue(job.Editor.KillOnJobClose);
        Assert.IsTrue(job.Editor.IsDirty);
        Assert.IsTrue(job.CanEditDraft);
        Assert.IsFalse(job.IsSnapshotStale);
    }

    [TestMethod]
    public async Task ApplyPreservesEditsMadeDuringNativeApply()
    {
        using var delay = new DelayedSnapshot();
        var session = new FakeSession { Apply = _ => { delay.Read(); } };
        using var job = await CreateInitializedJobAsync(session);
        job.Editor.KillOnJobClose = true;
        var operation = job.ApplyAsync();
        await delay.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try { job.Editor.BreakawayAllowed = true; }
        finally { delay.Release(); }
        await operation;
        Assert.IsTrue(session.AppliedProfile!.HardLimits.KillOnJobClose);
        Assert.IsFalse(session.AppliedProfile.HardLimits.BreakawayAllowed);
        Assert.IsTrue(job.Editor.BreakawayAllowed);
        Assert.IsTrue(job.Editor.IsDirty);
        StringAssert.Contains(job.LastOperationMessage, "Newer unsaved edits were preserved");
    }

    [TestMethod]
    public async Task RevertPreservesEditsMadeDuringSnapshotRead()
    {
        using var delay = new DelayedSnapshot();
        var session = new FakeSession { ReadSnapshot = delay.Read };
        using var job = CreateJob(session);
        job.Editor.KillOnJobClose = true;
        var operation = job.RevertAsync();
        await delay.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try { job.Editor.BreakawayAllowed = true; }
        finally { delay.Release(); }
        await operation;
        Assert.IsTrue(job.Editor.BreakawayAllowed);
        Assert.IsTrue(job.Editor.IsDirty);
    }

    [TestMethod]
    public async Task ApplyWithNoInterveningEditsReloadsAndMarksClean()
    {
        var session = new FakeSession();
        using var job = await CreateInitializedJobAsync(session);
        job.Editor.KillOnJobClose = true;
        await job.ApplyAsync();
        Assert.IsTrue(job.Editor.KillOnJobClose);
        Assert.IsFalse(job.Editor.IsDirty);
        Assert.IsTrue(job.KillOnCloseConfigured);
        Assert.IsFalse(job.LastOperationFailed);
    }

    [TestMethod]
    public async Task AcceptedApplyWithFailedReadbackRetainsDraftAndCloseWarnings()
    {
        var session = new FakeSession();
        using var job = await CreateInitializedJobAsync(session);
        session.ReadSnapshot = () => throw new InvalidOperationException("readback unavailable");
        job.Editor.KillOnJobClose = true;
        await job.ApplyAsync();
        Assert.IsFalse(job.LastOperationFailed);
        Assert.IsTrue(job.Editor.IsDirty);
        Assert.IsTrue(job.IsSnapshotStale);
        Assert.IsTrue(job.KillOnCloseConfigured);
        StringAssert.Contains(job.LastOperationMessage, "Restrictions accepted by Windows");
        session.ReadSnapshot = () => Snapshot(session.AppliedProfile!);
        await job.RevertAsync();
        Assert.IsFalse(job.IsSnapshotStale);
        Assert.IsFalse(job.Editor.IsDirty);
    }

    [TestMethod]
    public async Task InvalidDraftDoesNotReachNativeApply()
    {
        var session = new FakeSession();
        using var job = await CreateInitializedJobAsync(session);
        job.Editor.ActiveProcessLimitEnabled = true;
        job.Editor.ActiveProcessLimit = "bad";
        await job.ApplyAsync();
        Assert.IsNull(session.AppliedProfile);
        Assert.IsTrue(job.LastOperationFailed);
        Assert.IsFalse(job.IsBusy);
    }

    [TestMethod]
    public async Task UnverifiedInitialStateCannotBeAppliedUntilSuccessfulRefresh()
    {
        var session = new FakeSession { ReadSnapshot = () => throw new InvalidOperationException("unavailable") };
        using var job = CreateJob(session);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(job.RefreshAsync);
        job.Editor.KillOnJobClose = true;
        Assert.IsFalse(job.ApplyCommand.CanExecute(null));
        Assert.IsFalse(job.CanEditDraft);
        await job.ApplyAsync();
        Assert.IsNull(session.AppliedProfile);
        StringAssert.Contains(job.LastOperationMessage, "Refresh the job");
        session.ReadSnapshot = () => Snapshot();
        await job.RevertAsync();
        Assert.IsTrue(job.HasSnapshot);
        Assert.IsTrue(job.CanEditDraft);
        Assert.IsFalse(job.Editor.IsDirty);
    }

    [TestMethod]
    public async Task JobCleanupRetainsFailedHandleAndContinuesThenRetries()
    {
        var attempts = 0;
        var failingSession = new FakeSession
        {
            DisposeAction = () => { if (++attempts == 1) throw new InvalidOperationException("close unavailable"); }
        };
        var goodSession = new FakeSession();
        using var main = new MainViewModel(new FakeService(failingSession), () => []);
        using var first = CreateJob(failingSession);
        using var second = CreateJob(goodSession);
        main.Jobs.Add(first);
        main.Jobs.Add(second);
        var warnings = await main.CloseAllAsync();
        Assert.AreEqual(1, warnings.Count);
        StringAssert.Contains(warnings[0], "close unavailable");
        Assert.AreSame(first, main.Jobs.Single());
        Assert.AreEqual(1, goodSession.DisposeCalls);
        Assert.AreEqual(0, (await main.CloseAllAsync()).Count);
        Assert.AreEqual(2, attempts);
        Assert.AreEqual(0, main.Jobs.Count);
    }

    [TestMethod]
    public void ManualJobCloseRetainsFailedHandleForRetry()
    {
        var attempts = 0;
        var session = new FakeSession { DisposeAction = () => { if (++attempts == 1) throw new InvalidOperationException("temporary close failure"); } };
        using var main = new MainViewModel(new FakeService(session), () => []);
        using var job = CreateJob(session);
        main.Jobs.Add(job);
        main.SelectedJob = job;
        Assert.IsFalse(main.CloseJob(job));
        Assert.AreSame(job, main.Jobs.Single());
        Assert.AreSame(job, main.SelectedJob);
        StringAssert.Contains(main.StatusMessage, "handle is retained");
        Assert.IsTrue(main.CloseJob(job));
        Assert.AreEqual(2, attempts);
        Assert.AreEqual(0, main.Jobs.Count);
    }

    [TestMethod]
    public void FailedDisposeRetainsOwnershipAndContinuesClosingOtherJobs()
    {
        var attempts = 0;
        var failing = new FakeSession { DisposeAction = () => { if (++attempts == 1) throw new InvalidOperationException("temporary close failure"); } };
        var good = new FakeSession();
        using var main = new MainViewModel(new FakeService(failing), () => []);
        using var first = CreateJob(failing);
        using var second = CreateJob(good);
        main.Jobs.Add(first);
        main.Jobs.Add(second);
        Assert.ThrowsExactly<AggregateException>(main.Dispose);
        Assert.AreSame(first, main.Jobs.Single());
        Assert.AreEqual(1, good.DisposeCalls);
        main.Dispose();
        Assert.AreEqual(2, attempts);
        Assert.AreEqual(0, main.Jobs.Count);
    }

    private static JobViewModel CreateJob(FakeSession session) => new(session, JobCapabilitySet.Unavailable("test"), 1);
    private static async Task<JobViewModel> CreateInitializedJobAsync(FakeSession session)
    {
        var job = CreateJob(session);
        await job.InitializeAsync();
        return job;
    }
    private static JobSessionSnapshot Snapshot(RestrictionProfile? profile = null) => new(profile ?? RestrictionProfile.Empty, [], JobAccountingDisplay.Empty, []);

    private sealed class DelayedSnapshot : IDisposable
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public JobSessionSnapshot Read()
        {
            Entered.TrySetResult();
            _release.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            return Snapshot();
        }
        public void Release() => _release.TrySetResult();
        public void Dispose() => Release();
    }

    private sealed class FakeSession : IJobSession
    {
        public string? Name => null;
        public bool CreatedNew => true;
        public bool HasOwnedNotificationDelivery => true;
        public event EventHandler<LiveJobNotificationDisplay>? NotificationReceived { add { } remove { } }
        public Func<JobSessionSnapshot>? ReadSnapshot { get; set; }
        public Action<RestrictionProfile>? Apply { get; init; }
        public RestrictionProfile? AppliedProfile { get; private set; }
        public IReadOnlyList<AssignmentOutcome>? AssignmentResults { get; init; }
        public Exception? LaunchError { get; init; }
        public int LaunchCalls { get; private set; }
        public int AssignmentCalls { get; private set; }
        public int SnapshotCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public Action? DisposeAction { get; init; }
        public JobSessionSnapshot GetSnapshot() { SnapshotCalls++; return ReadSnapshot?.Invoke() ?? Snapshot(AppliedProfile); }
        public void ApplyRestrictions(RestrictionProfile restrictions) { AppliedProfile = restrictions; Apply?.Invoke(restrictions); }
        public IReadOnlyList<AssignmentOutcome> AssignProcesses(IReadOnlyCollection<ProcessIdentity> processes)
        {
            AssignmentCalls++;
            return AssignmentResults ?? processes.Select(process => new AssignmentOutcome(process.ProcessId, "worker", true, "Assigned")).ToArray();
        }
        public LaunchOutcome LaunchProcess(LaunchRequest request) { LaunchCalls++; if (LaunchError is not null) throw LaunchError; return new(42, "worker"); }
        public void Dispose() { DisposeCalls++; DisposeAction?.Invoke(); }
    }

    private sealed class FakeService(FakeSession session) : IJobControlService
    {
        public JobCapabilitySet Capabilities => JobCapabilitySet.Unavailable("test");
        public ProcessIdentityCaptureResult CaptureProcessIdentity(int processId) => new(100, null);
        public IJobSession CreateJob(string? name) => session;
        public IJobSession OpenJob(string name) => session;
        public void Dispose() { }
    }
}
