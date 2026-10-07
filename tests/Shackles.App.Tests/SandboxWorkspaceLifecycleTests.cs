using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Controls;
using System.Windows.Threading;
using Shackles.App.Controls;
using Shackles.App.Models;
using Shackles.App.Views;
using Shackles.AppContainers;
using Shackles.AppContainers.Internal;
using Shackles.ExperimentalSandboxes;
using Shackles.ExperimentalSandboxes.Internal;

namespace Shackles.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class SandboxWorkspaceLifecycleTests
{
    private static readonly Lazy<Task<Dispatcher>> UiDispatcher = new(CreateDispatcher);

    [TestMethod]
    public async Task TestHostLoadsApplicationResourcesWithoutOpeningShackles()
    {
        await OnUi(async () =>
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var application = System.Windows.Application.Current;
            Assert.IsNull(application.StartupUri, "The test host must not run the application's startup window.");
#pragma warning disable WPF0001 // Match the Fluent theme already used by App.xaml.
            Assert.AreEqual(System.Windows.ThemeMode.System, application.ThemeMode);
#pragma warning restore WPF0001
            Assert.IsFalse(application.Windows.OfType<System.Windows.Window>().Any(window => window.IsVisible),
                "The WPF component tests must keep their windows offscreen.");
            Assert.IsInstanceOfType<System.Windows.Style>(application.FindResource("ActionButtonStyle"),
                "The test host must still load the real application resources.");
        });
    }

    [TestMethod]
    public async Task ManagersAreLazyAndInitializedAwayFromTheUiThread()
    {
        var directory = Directory.CreateTempSubdirectory("Shackles-ui-init-").FullName;
        try
        {
            await OnUi(async () =>
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                var appCalls = 0;
                var experimentalCalls = 0;
                var appOnUi = true;
                var experimentalOnUi = true;
                using var release = new ManualResetEventSlim();
                var appStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var experimentalStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using var app = new AppContainerWorkspaceView(() =>
                {
                    Interlocked.Increment(ref appCalls);
                    appOnUi = dispatcher.CheckAccess();
                    appStarted.SetResult();
                    release.Wait();
                    return NewAppContainerManager(directory);
                });
                using var experimental = new ExperimentalSandboxWorkspaceView(() =>
                {
                    Interlocked.Increment(ref experimentalCalls);
                    experimentalOnUi = dispatcher.CheckAccess();
                    experimentalStarted.SetResult();
                    release.Wait();
                    return NewExperimentalManager();
                });

                Assert.AreEqual(0, appCalls);
                Assert.AreEqual(0, experimentalCalls);
                var appInitialization = app.InitializeAsync();
                var experimentalInitialization = experimental.InitializeAsync();
                try
                {
                    await Task.WhenAll(appStarted.Task, experimentalStarted.Task).WaitAsync(TimeSpan.FromSeconds(10));
                    Assert.IsTrue(app.IsBusy);
                    Assert.IsTrue(experimental.IsBusy);
                    Assert.IsFalse(Control<InstanceTabStrip>(app, "SandboxList").IsNewEnabled);
                    Assert.IsFalse(Control<InstanceTabStrip>(experimental, "SandboxList").IsNewEnabled);
                    Assert.IsFalse(Control<ScrollViewer>(app, "SandboxEditorScrollViewer").IsEnabled);
                    Assert.IsFalse(Control<ScrollViewer>(experimental, "SandboxEditorScrollViewer").IsEnabled);
                    Assert.AreSame(appInitialization, app.InitializeAsync(), "Concurrent preparation must share one initialization.");
                }
                finally { release.Set(); }

                await Task.WhenAll(appInitialization, experimentalInitialization);
                Assert.IsFalse(appOnUi);
                Assert.IsFalse(experimentalOnUi);
                Assert.AreEqual(1, appCalls);
                Assert.AreEqual(1, experimentalCalls);
                Assert.IsFalse(app.IsBusy);
                Assert.IsFalse(experimental.IsBusy);
                Assert.IsTrue(Control<InstanceTabStrip>(app, "SandboxList").IsNewEnabled);
                Assert.IsTrue(Control<InstanceTabStrip>(experimental, "SandboxList").IsNewEnabled);
            });
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task FailedInitializationCanBeRetried()
    {
        var directory = Directory.CreateTempSubdirectory("Shackles-ui-retry-").FullName;
        try
        {
            await OnUi(async () =>
            {
                var appCalls = 0;
                var experimentalCalls = 0;
                using var app = new AppContainerWorkspaceView(() => ++appCalls == 1
                    ? throw new IOException("Temporary recovery failure")
                    : NewAppContainerManager(directory));
                using var experimental = new ExperimentalSandboxWorkspaceView(() => ++experimentalCalls == 1
                    ? throw new IOException("Temporary probe failure")
                    : NewExperimentalManager());
                await Task.WhenAll(app.InitializeAsync(), experimental.InitializeAsync());
                Assert.IsFalse(app.IsBusy);
                Assert.IsFalse(experimental.IsBusy);
                Assert.IsFalse(Control<InstanceTabStrip>(app, "SandboxList").IsNewEnabled);
                Assert.IsFalse(Control<InstanceTabStrip>(experimental, "SandboxList").IsNewEnabled);

                await Task.WhenAll(app.InitializeAsync(), experimental.InitializeAsync());
                Assert.AreEqual(2, appCalls);
                Assert.AreEqual(2, experimentalCalls);
                Assert.IsTrue(Control<InstanceTabStrip>(app, "SandboxList").IsNewEnabled);
                Assert.IsTrue(Control<InstanceTabStrip>(experimental, "SandboxList").IsNewEnabled);
            });
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task AppContainerCloseRetainsIncompleteCardAndAllowsRetry()
    {
        var directory = Directory.CreateTempSubdirectory("Shackles-ui-close-").FullName;
        try
        {
            await OnUi(async () =>
            {
                var manager = NewAppContainerManager(directory);
                var attempts = 0;
                var identity = new AppContainerIdentity($"Shackles.{Guid.NewGuid():N}", "S-1-15-2-1", []);
                var journal = CleanupJournal.Create(directory, identity, "Cleanup retry");
                var sandbox = new AppContainerSandbox(identity, [], new AppContainerSandboxOptions { DisplayName = "Cleanup retry" },
                    journal, new FakeBfs(), _ => ++attempts == 1 ? "Profile is temporarily busy." : null);
                manager.TrackSandbox(sandbox);
                using var view = new AppContainerWorkspaceView(() => manager);
                await view.InitializeAsync();
                var card = new AppContainerSandboxCard(sandbox.DisplayName);
                card.Attach(sandbox);
                var list = Control<ListBox>(view, "SandboxList");
                var cards = (ObservableCollection<AppContainerSandboxCard>)list.ItemsSource;
                cards.Add(card);
                list.SelectedItem = card;

                var first = await view.CloseAllAsync();
                Assert.HasCount(1, first);
                Assert.HasCount(1, cards);
                Assert.AreEqual("CLEANUP NEEDED", card.StateBadge);
                Assert.AreEqual("_Retry cleanup", Control<Button>(view, "CloseSandboxButton").Content);
                Assert.IsFalse(Control<Button>(view, "CreateAndLaunchButton").IsEnabled);
                Assert.IsFalse(Control<ScrollViewer>(view, "SandboxEditorScrollViewer").IsEnabled);

                Assert.IsEmpty(await view.CloseAllAsync());
                Assert.IsEmpty(cards);
                Assert.IsTrue(Control<InstanceTabStrip>(view, "SandboxList").IsNewEnabled,
                    "Successful workspace cleanup must remain usable while another workspace needs a retry.");
                view.Dispose();
                Assert.AreEqual(2, attempts, "Dispose must not repeat completed cleanup.");
            });
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task ExperimentalCloseRetainsIncompleteCardAndAllowsRetry()
    {
        await OnUi(async () =>
        {
            var manager = NewExperimentalManager();
            var attempts = 0;
            var sandbox = new ExperimentalSandbox(new SandboxIdentity("Fake profile", null),
                new ExperimentalSandboxOptions { DisplayName = "Cleanup retry", UseAppContainer = false },
                () => ++attempts == 1 ? "Profile is temporarily busy." : null, profileMayExist: true);
            manager.TrackSandbox(sandbox);
            using var view = new ExperimentalSandboxWorkspaceView(() => manager);
            await view.InitializeAsync();
            var card = new ExperimentalSandboxCard(sandbox.DisplayName);
            card.Attach(sandbox);
            var list = Control<ListBox>(view, "SandboxList");
            var cards = (ObservableCollection<ExperimentalSandboxCard>)list.ItemsSource;
            cards.Add(card);
            list.SelectedItem = card;

            Assert.HasCount(1, await view.CloseAllAsync());
            Assert.HasCount(1, cards);
            Assert.AreEqual("CLEANUP NEEDED", card.StateBadge);
            Assert.AreEqual("_Retry cleanup", Control<Button>(view, "CloseSandboxButton").Content);
            Assert.IsFalse(Control<Button>(view, "CreateAndLaunchButton").IsEnabled);
            Assert.IsFalse(Control<ScrollViewer>(view, "SandboxEditorScrollViewer").IsEnabled);

            Assert.IsEmpty(await view.CloseAllAsync());
            Assert.IsEmpty(cards);
            Assert.IsTrue(Control<InstanceTabStrip>(view, "SandboxList").IsNewEnabled);
            view.Dispose();
            Assert.AreEqual(2, attempts);
        });
    }

    [TestMethod]
    public async Task FailedCreationKeepsTheOriginalCardAndItsPolicyChoices()
    {
        var directory = Directory.CreateTempSubdirectory("Shackles-ui-failed-create-").FullName;
        try
        {
            await OnUi(async () =>
            {
                var appManager = NewAppContainerManager(directory);
                var identity = new AppContainerIdentity($"Shackles.{Guid.NewGuid():N}", "S-1-15-2-1", []);
                var journal = CleanupJournal.Create(directory, identity, "Original AppContainer");
                var appAttempts = 0;
                var appSandbox = new AppContainerSandbox(identity, [], new AppContainerSandboxOptions
                {
                    DisplayName = "Original AppContainer", RestrictChildProcessCreation = false,
                    IsolationMode = AppContainerIsolationMode.LowPrivilege
                }, journal, new FakeBfs(), _ => ++appAttempts == 1 ? "Temporary profile failure" : null);
                appManager.TrackSandbox(appSandbox);
                Assert.IsFalse(appSandbox.Close().Completed);
                using var app = new AppContainerWorkspaceView(() => appManager);
                await app.InitializeAsync();
                var appCard = new AppContainerSandboxCard(appSandbox.DisplayName);
                appCard.Draft.AllowChildren = true;
                appCard.Draft.UseLowPrivilege = true;
                var appList = Control<ListBox>(app, "SandboxList");
                var appCards = (ObservableCollection<AppContainerSandboxCard>)appList.ItemsSource;
                appCards.Add(appCard);
                appList.SelectedItem = appCard;
                app.AddPendingCleanupCards(appCard);
                Assert.HasCount(1, appCards);
                Assert.AreSame(appCard, appCards.Single());
                Assert.AreSame(appSandbox, appCard.Sandbox);
                Assert.IsTrue(appCard.Draft.UseLowPrivilege);
                Assert.IsTrue(app.CanHaveUntrackedDescendants);
                Assert.AreEqual("_Retry cleanup", Control<Button>(app, "CloseSandboxButton").Content);
                Assert.IsEmpty(await app.CloseAllAsync());

                var experimentalManager = NewExperimentalManager();
                var experimentalAttempts = 0;
                var experimentalSandbox = new ExperimentalSandbox(new SandboxIdentity("Fake profile", null),
                    new ExperimentalSandboxOptions { DisplayName = "Original restricted sandbox", UseAppContainer = false },
                    () => ++experimentalAttempts == 1 ? "Temporary profile failure" : null, profileMayExist: true);
                experimentalManager.TrackSandbox(experimentalSandbox);
                Assert.IsFalse(experimentalSandbox.Close().Completed);
                using var experimental = new ExperimentalSandboxWorkspaceView(() => experimentalManager);
                await experimental.InitializeAsync();
                var experimentalCard = new ExperimentalSandboxCard(experimentalSandbox.DisplayName);
                experimentalCard.Draft.UseAppContainer = false;
                experimentalCard.Draft.IntegrityIndex = 3;
                experimentalCard.Draft.PrivateNetwork = true;
                experimentalCard.Draft.FileRules.Add(new ExperimentalSandboxFileRuleDraft("C:\\Original", 1));
                var experimentalList = Control<ListBox>(experimental, "SandboxList");
                var experimentalCards = (ObservableCollection<ExperimentalSandboxCard>)experimentalList.ItemsSource;
                experimentalCards.Add(experimentalCard);
                experimentalList.SelectedItem = experimentalCard;
                experimental.AddPendingCleanupCards(experimentalCard);
                Assert.HasCount(1, experimentalCards);
                Assert.AreSame(experimentalCard, experimentalCards.Single());
                Assert.AreSame(experimentalSandbox, experimentalCard.Sandbox);
                Assert.IsFalse(experimentalCard.Draft.UseAppContainer);
                Assert.AreEqual(3, experimentalCard.Draft.IntegrityIndex);
                Assert.IsTrue(experimentalCard.Draft.PrivateNetwork);
                Assert.HasCount(1, experimentalCard.Draft.FileRules);
                Assert.IsEmpty(await experimental.CloseAllAsync());
            });
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task SwitchingAppContainerModePreservesInactiveDraftFields()
    {
        await OnUi(() =>
        {
            using var view = new ExperimentalSandboxWorkspaceView(NewExperimentalManager);
            var card = new ExperimentalSandboxCard("Draft");
            var draft = card.Draft;
            draft.IntegrityIndex = 3;
            draft.LeastPrivilege = true;
            draft.NetworkModeIndex = 2;
            draft.ProxyUrl = "https://proxy.example:8443";
            draft.PrivateNetwork = true;
            draft.CustomCapabilities = "customCapability";
            draft.FileRules.Add(new ExperimentalSandboxFileRuleDraft("C:\\Example", 1));
            var list = Control<ListBox>(view, "SandboxList");
            ((ObservableCollection<ExperimentalSandboxCard>)list.ItemsSource).Add(card);
            list.SelectedItem = card;
            var toggle = Control<CheckBox>(view, "UseAppContainerCheckBox");

            toggle.IsChecked = false;
            var restricted = ExperimentalSandboxWorkspaceView.BuildSandboxOptions(draft);
            Assert.AreEqual(ExperimentalSandboxIntegrityLevel.Low, restricted.IntegrityLevel);
            Assert.IsFalse(restricted.LeastPrivilege);
            Assert.IsEmpty(restricted.FileSystemRules);
            Assert.IsEmpty(restricted.CapabilityNames);
            Assert.IsNull(restricted.ProxyUrl);
            toggle.IsChecked = true;

            Assert.AreEqual(3, draft.IntegrityIndex);
            Assert.IsTrue(draft.LeastPrivilege);
            Assert.AreEqual(2, draft.NetworkModeIndex);
            Assert.AreEqual("https://proxy.example:8443", draft.ProxyUrl);
            Assert.IsTrue(draft.PrivateNetwork);
            Assert.AreEqual("customCapability", draft.CustomCapabilities);
            Assert.HasCount(1, draft.FileRules);
            var restored = ExperimentalSandboxWorkspaceView.BuildSandboxOptions(draft);
            Assert.AreEqual(ExperimentalSandboxIntegrityLevel.SystemDefault, restored.IntegrityLevel);
            Assert.IsTrue(restored.LeastPrivilege);
            Assert.AreEqual(ExperimentalSandboxNetworkMode.Proxy, restored.NetworkMode);
            Assert.HasCount(1, restored.FileSystemRules);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void ExperimentalCreationInputsAreIndependentOfSubsequentDraftEdits()
    {
        var draft = new ExperimentalSandboxDraft
        {
            Name = "Original", UseAppContainer = true, PrivateNetwork = true,
            NetworkModeIndex = 2, ProxyUrl = "https://proxy.example", ExecutablePath = "original.exe", Arguments = "original args"
        };
        var rule = new ExperimentalSandboxFileRuleDraft("C:\\Original", 1);
        draft.FileRules.Add(rule);
        var options = ExperimentalSandboxWorkspaceView.BuildSandboxOptions(draft);
        var launch = ExperimentalSandboxWorkspaceView.BuildLaunchOptions(draft);
        draft.Name = "Changed";
        draft.PrivateNetwork = false;
        draft.FileRules.Clear();
        rule.AccessIndex = 2;
        draft.ProxyUrl = "https://changed.example";
        draft.ExecutablePath = "changed.exe";
        draft.Arguments = "changed args";

        Assert.AreEqual("Original", options.DisplayName);
        Assert.Contains("privateNetworkClientServer", options.CapabilityNames);
        Assert.AreEqual(ExperimentalSandboxFileAccess.ReadOnly, options.FileSystemRules.Single().Access);
        Assert.AreEqual("https://proxy.example", options.ProxyUrl);
        Assert.AreEqual("original.exe", launch.FileName);
        Assert.AreEqual("original args", launch.Arguments);
    }

    [TestMethod]
    public void AppContainerCreationInputsAreIndependentOfSubsequentDraftEdits()
    {
        var draft = new AppContainerSandboxDraft { Name = "Original", InternetClient = true, ExecutablePath = "original.exe" };
        var grant = new AppContainerFileGrantDraft("C:\\Original", true, 0);
        draft.FileGrants.Add(grant);
        var options = AppContainerWorkspaceView.BuildSandboxOptions(draft);
        var launch = AppContainerWorkspaceView.BuildLaunchOptions(draft);
        draft.Name = "Changed";
        draft.InternetClient = false;
        draft.FileGrants.Clear();
        grant.AccessIndex = 1;
        draft.ExecutablePath = "changed.exe";

        Assert.AreEqual("Original", options.DisplayName);
        Assert.Contains("internetClient", options.CapabilityNames);
        Assert.AreEqual(FileSystemGrantAccess.ReadExecute, options.FileSystemGrants.Single().Access);
        Assert.AreEqual("original.exe", launch.FileName);
    }

    private static AppContainerManager NewAppContainerManager(string directory) => new(directory, new FakeBfs());

    private static ExperimentalSandboxManager NewExperimentalManager() => new(() => new ExperimentalSandboxSupport(
        ExperimentalSandboxAvailability.Available, "Fake support", new Version(10, 0), null, true, false, null, null, []));

    private static T Control<T>(UserControl view, string name) where T : class => (T)view.FindName(name);

    private static Task<Dispatcher> CreateDispatcher()
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                // Load the real theme and styles without configuring application startup.
#pragma warning disable WPF0001 // Match the Fluent theme already used by App.xaml.
                var application = new System.Windows.Application
                {
                    ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown,
                    ThemeMode = System.Windows.ThemeMode.System
                };
#pragma warning restore WPF0001
                application.Resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary
                {
                    Source = new Uri("/Shackles;component/Resources/ApplicationStyles.xaml", UriKind.Relative)
                });
                ready.SetResult(Dispatcher.CurrentDispatcher);
                Dispatcher.Run();
            }
            catch (Exception exception) { ready.TrySetException(exception); }
        }) { IsBackground = true, Name = "Sandbox workspace test dispatcher" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task;
    }

    internal static async Task OnUi(Func<Task> action)
    {
        var dispatcher = await UiDispatcher.Value;
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = dispatcher.BeginInvoke(new Action(async () =>
        {
            try { await action(); completed.SetResult(); }
            catch (Exception exception) { completed.SetException(exception); }
        }));
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private sealed class FakeBfs : IBrokeredFileSystemConfigurator
    {
        public BrokeredFileSystemSupport Support { get; } = new(BrokeredFileSystemAvailability.Available,
            "Fake policy backend", new Version(10, 0), null, null, false, []);
        public void AddPolicy(string appContainerName, TrackedAclGrant grant) { }
        public string? TryClearPolicy(string appContainerName) => null;
    }
}
