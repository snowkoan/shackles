using System.Windows;
using System.Windows.Controls;
using Shackles.App.Controls;
using Shackles.App.Models;
using Shackles.App.Services;
using Shackles.App.ViewModels;
using Shackles.App.Views;

namespace Shackles.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class JobInstanceTabTests
{
    [TestMethod]
    public async Task SwitchingTabsKeepsEachJobAndItsUnsavedDraft()
    {
        await OnUi(async () =>
        {
            using var main = NewMain();
            var first = await AddJob(main, "core");
            var second = await AddJob(main, "core2");
            main.SelectedJob = first;
            var window = new MainWindow(main);
            try
            {
                var tabs = PrepareTabs(window);
                var details = (JobDetailsView)window.FindName("SelectedJobDetails");
                first.Editor.ActiveProcessLimitEnabled = true;
                first.Editor.ActiveProcessLimit = "17";
                tabs.SelectedItem = second;
                Assert.AreSame(second, main.SelectedJob);
                Assert.AreSame(second, details.DataContext);
                second.Editor.ActiveProcessLimitEnabled = true;
                second.Editor.ActiveProcessLimit = "23";
                tabs.SelectedItem = first;

                Assert.AreSame(first, main.SelectedJob);
                Assert.AreSame(first, details.DataContext);
                Assert.AreEqual("17", first.Editor.ActiveProcessLimit);
                Assert.AreEqual("23", second.Editor.ActiveProcessLimit);
                Assert.IsTrue(first.Editor.IsDirty);
                Assert.IsTrue(second.Editor.IsDirty);
                Assert.HasCount(2, tabs.Items);
            }
            finally { window.Dispose(); window.Close(); }
        });
    }

    [TestMethod]
    public async Task BackgroundTabCloseUsesItsOwnJobWithoutSwitchingTheEditor()
    {
        await OnUi(async () =>
        {
            using var main = NewMain();
            var first = await AddJob(main, "Selected");
            var backgroundSession = new FakeSession("Background");
            var background = await AddJob(main, backgroundSession);
            main.SelectedJob = first;
            var window = new MainWindow(main);
            try
            {
                var tabs = PrepareTabs(window);
                first.Editor.ActiveProcessLimit = "17";
                tabs.RequestClose(background);

                Assert.AreSame(first, main.SelectedJob);
                Assert.AreSame(first, tabs.SelectedItem);
                Assert.AreSame(first, ((JobDetailsView)window.FindName("SelectedJobDetails")).DataContext);
                Assert.AreEqual("17", first.Editor.ActiveProcessLimit);
                Assert.AreSame(first, main.Jobs.Single());
                Assert.AreEqual(1, backgroundSession.DisposeCalls);
            }
            finally { window.Dispose(); window.Close(); }
        });
    }

    [TestMethod]
    [DataRow(0, 1)]
    [DataRow(1, 2)]
    [DataRow(2, 1)]
    public async Task ClosingSelectedJobChoosesTheAdjacentTab(int closedIndex, int expectedIndex)
    {
        await OnUi(async () =>
        {
            using var main = NewMain();
            var jobs = new[] { await AddJob(main, "One"), await AddJob(main, "Two"), await AddJob(main, "Three") };
            main.SelectedJob = jobs[closedIndex];

            Assert.IsTrue(main.CloseJob(jobs[closedIndex]));
            Assert.AreSame(jobs[expectedIndex], main.SelectedJob);
            Assert.HasCount(2, main.Jobs);
        });
    }

    [TestMethod]
    public async Task FailedBackgroundCloseRetainsItsTabAndTheCurrentSelection()
    {
        await OnUi(async () =>
        {
            using var main = NewMain();
            var first = await AddJob(main, "Selected");
            var attempts = 0;
            var session = new FakeSession("Retry")
            {
                DisposeAction = () => { if (++attempts == 1) throw new InvalidOperationException("temporary failure"); }
            };
            var background = await AddJob(main, session);
            main.SelectedJob = first;

            Assert.IsFalse(main.CloseJob(background));
            Assert.HasCount(2, main.Jobs);
            Assert.AreSame(first, main.SelectedJob);
            Assert.IsTrue(main.CloseJob(background));
            Assert.AreSame(first, main.SelectedJob);
            Assert.AreEqual(2, attempts);
        });
    }

    [TestMethod]
    public async Task ClosingLastJobReturnsToTheEmptyEditor()
    {
        await OnUi(async () =>
        {
            using var main = NewMain();
            var job = await AddJob(main, "Only");
            main.SelectedJob = job;
            var window = new MainWindow(main);
            try
            {
                PrepareTabs(window).RequestClose(job);
                Assert.IsNull(main.SelectedJob);
                Assert.IsFalse(main.HasSelectedJob);
                Assert.IsEmpty(main.Jobs);
                Assert.IsNull(((JobDetailsView)window.FindName("SelectedJobDetails")).DataContext);
            }
            finally { window.Dispose(); window.Close(); }
        });
    }

    private static Task OnUi(Func<Task> action) => SandboxWorkspaceLifecycleTests.OnUi(action);
    private static InstanceTabStrip PrepareTabs(MainWindow window)
    {
        var tabs = (InstanceTabStrip)window.FindName("JobList");
        tabs.ApplyTemplate();
        tabs.Measure(new Size(900, 60));
        tabs.Arrange(new Rect(0, 0, 900, 60));
        tabs.UpdateLayout();
        return tabs;
    }
    private static MainViewModel NewMain() => new(new FakeService(), () => []);
    private static Task<JobViewModel> AddJob(MainViewModel main, string name) => AddJob(main, new FakeSession(name));
    private static async Task<JobViewModel> AddJob(MainViewModel main, FakeSession session)
    {
        var job = new JobViewModel(session, main.Capabilities, main.Jobs.Count + 1);
        await job.InitializeAsync();
        main.Jobs.Add(job);
        return job;
    }

    private sealed class FakeService : IJobControlService
    {
        public JobCapabilitySet Capabilities => JobCapabilitySet.Unavailable("test");
        public ProcessIdentityCaptureResult CaptureProcessIdentity(int processId) => new(100, null);
        public IJobSession CreateJob(string? name) => throw new AssertFailedException("Switching tabs must not create a job.");
        public IJobSession OpenJob(string name) => throw new AssertFailedException("Switching tabs must not open a job.");
        public void Dispose() { }
    }

    private sealed class FakeSession(string name) : IJobSession
    {
        public string? Name => name;
        public bool CreatedNew => true;
        public bool HasOwnedNotificationDelivery => false;
        public event EventHandler<LiveJobNotificationDisplay>? NotificationReceived { add { } remove { } }
        public int DisposeCalls { get; private set; }
        public Action? DisposeAction { get; init; }
        public JobSessionSnapshot GetSnapshot() => new(RestrictionProfile.Empty, [], JobAccountingDisplay.Empty, []);
        public void ApplyRestrictions(RestrictionProfile restrictions) => throw new AssertFailedException("Tab selection must not apply restrictions.");
        public IReadOnlyList<AssignmentOutcome> AssignProcesses(IReadOnlyCollection<ProcessIdentity> processes) => throw new AssertFailedException("Tab selection must not assign processes.");
        public LaunchOutcome LaunchProcess(LaunchRequest request) => throw new AssertFailedException("Tab selection must not launch a process.");
        public void Dispose() { DisposeCalls++; DisposeAction?.Invoke(); }
    }
}
