using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Shackles.App.Controls;
using Shackles.App.Models;
using Shackles.App.Views;
using Shackles.ExperimentalSandboxes;
using Shackles.ExperimentalSandboxes.Internal;

namespace Shackles.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ExperimentalInstanceTabTests
{
    [TestMethod]
    public async Task SelectedInstanceHeaderFollowsTheCardAndExistingIdentitySummary()
    {
        await SandboxWorkspaceLifecycleTests.OnUi(async () =>
        {
            using var view = new ExperimentalSandboxWorkspaceView(NewManager);
            await view.InitializeAsync();
            var draft = AddCard(view, new ExperimentalSandboxCard("Draft header"), select: true);
            Layout(view);
            await Dispatcher.Yield(DispatcherPriority.DataBind);

            var name = Control<TextBlock>(view, "SandboxHeaderNameText");
            var badge = Control<TextBlock>(view, "EditorStateBadgeText");
            var identity = Control<TextBlock>(view, "SandboxIdentityText");
            Assert.AreEqual(draft.DisplayName, name.Text);
            Assert.AreEqual("DRAFT", badge.Text);
            Assert.AreEqual(Control<TextBlock>(view, "IdentitySummaryText").Text, identity.Text);
            Assert.AreEqual(identity.Text, identity.ToolTip);

            Control<TextBox>(view, "SandboxNameTextBox").Text = "Edited header";
            Control<CheckBox>(view, "UseAppContainerCheckBox").IsChecked = false;
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            Assert.AreEqual("Edited header", name.Text);
            Assert.AreEqual("No AppContainer SID", identity.Text);

            using var sandbox = new ExperimentalSandbox(new SandboxIdentity("Fake active profile", null),
                new ExperimentalSandboxOptions { DisplayName = "Active header", UseAppContainer = false });
            var active = new ExperimentalSandboxCard(sandbox.DisplayName);
            active.Attach(sandbox);
            AddCard(view, active, select: true);
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            Assert.AreEqual(active.DisplayName, name.Text);
            Assert.AreEqual("ACTIVE", badge.Text);
            Assert.AreEqual(Control<TextBlock>(view, "IdentitySummaryText").Text, identity.Text);
            Assert.AreEqual("Edited header", draft.Draft.Name);
        });
    }

    [TestMethod]
    public async Task PreviewEventsRefreshTextOptionsAndSelections()
    {
        await SandboxWorkspaceLifecycleTests.OnUi(async () =>
        {
            using var view = new ExperimentalSandboxWorkspaceView(NewManager);
            await view.InitializeAsync();
            var card = AddCard(view, new ExperimentalSandboxCard("Draft"), select: true);
            var notices = new List<string?>();
            card.PropertyChanged += (_, args) => notices.Add(args.PropertyName);
            Control<TextBox>(view, "SandboxNameTextBox").Text = "Renamed";
            Assert.AreEqual("Renamed", card.DisplayName);
            Assert.HasCount(4, notices);

            var win32k = Control<CheckBox>(view, "DisallowWin32kCheckBox");
            win32k.IsChecked = true;
            Assert.IsTrue(card.Draft.DisallowWin32k);
            win32k.IsChecked = false;
            Assert.IsFalse(card.Draft.DisallowWin32k);
            Control<CheckBox>(view, "UseAppContainerCheckBox").IsChecked = false;
            notices.Clear();
            Control<ComboBox>(view, "IntegrityComboBox").SelectedIndex = 3;
            Assert.AreEqual(3, card.Draft.IntegrityIndex);
            Assert.HasCount(4, notices);
            Assert.AreEqual("Low", Control<TextBlock>(view, "IntegritySummaryText").Text);
        });
    }

    [TestMethod]
    public async Task EditorUsesWorkspaceWidthAndSupportRemainsOutsideIt()
    {
        await SandboxWorkspaceLifecycleTests.OnUi(async () =>
        {
            using var view = new ExperimentalSandboxWorkspaceView(NewManager);
            await view.InitializeAsync();
            AddCard(view, new ExperimentalSandboxCard("Draft"), select: true);
            Layout(view);

            var editor = Control<Grid>(view, "SandboxEditorHost");
            Assert.IsTrue(editor.ActualWidth >= 900, $"Editor width was {editor.ActualWidth}.");
            Assert.IsInstanceOfType<InstanceTabStrip>(view.FindName("SandboxList"));
            Assert.IsNull(view.FindName("NewSandboxButton"));
            Assert.AreEqual("Ready on this Windows build", Control<TextBlock>(view, "SupportStateText").Text);
            Assert.IsFalse(editor.IsAncestorOf(Control<TextBlock>(view, "SupportStateText")));
            Assert.IsFalse(editor.IsAncestorOf(Control<TextBlock>(view, "SupportDetailText")));
            Assert.IsFalse(editor.IsAncestorOf(Control<TextBlock>(view, "FeatureStateText")));
        });
    }

    [TestMethod]
    public async Task NewRequestReusesTheUnfinishedDraftAndPreservesItsInputs()
    {
        await SandboxWorkspaceLifecycleTests.OnUi(async () =>
        {
            using var view = new ExperimentalSandboxWorkspaceView(NewManager);
            await view.InitializeAsync();
            var tabs = Control<InstanceTabStrip>(view, "SandboxList");
            tabs.RaiseEvent(new RoutedEventArgs(InstanceTabStrip.NewRequestedEvent));
            var draft = (ExperimentalSandboxCard)tabs.SelectedItem;
            Control<TextBox>(view, "SandboxNameTextBox").Text = "Unfinished policy";
            Control<TextBox>(view, "ExecutablePathTextBox").Text = "C:\\Draft\\sample.exe";
            Control<TextBox>(view, "ArgumentsTextBox").Text = "--keep-this";
            using var sandbox = new ExperimentalSandbox(new SandboxIdentity("Fake active profile", null),
                new ExperimentalSandboxOptions { DisplayName = "Active sandbox", UseAppContainer = false });
            var active = new ExperimentalSandboxCard(sandbox.DisplayName);
            active.Attach(sandbox);
            AddCard(view, active, select: true);

            tabs.RaiseEvent(new RoutedEventArgs(InstanceTabStrip.NewRequestedEvent));

            Assert.HasCount(2, Cards(view));
            Assert.AreSame(draft, tabs.SelectedItem);
            Assert.AreEqual("Unfinished policy", draft.DisplayName);
            Assert.AreEqual("C:\\Draft\\sample.exe", draft.Draft.ExecutablePath);
            Assert.AreEqual("--keep-this", draft.Draft.Arguments);
            Assert.IsTrue(tabs.IsNewEnabled);
        });
    }

    [TestMethod]
    public async Task ClosingABackgroundDraftPreservesTheSelectedEditor()
    {
        await SandboxWorkspaceLifecycleTests.OnUi(async () =>
        {
            using var view = new ExperimentalSandboxWorkspaceView(NewManager);
            await view.InitializeAsync();
            var background = AddCard(view, new ExperimentalSandboxCard("Background draft"));
            var selected = AddCard(view, new ExperimentalSandboxCard("Selected draft"), select: true);
            Control<TextBox>(view, "ExecutablePathTextBox").Text = "C:\\Selected\\sample.exe";
            Layout(view);

            Assert.IsTrue(Control<InstanceTabStrip>(view, "SandboxList").RequestClose(background));

            Assert.HasCount(1, Cards(view));
            Assert.AreSame(selected, Control<InstanceTabStrip>(view, "SandboxList").SelectedItem);
            Assert.AreEqual("Selected draft", Control<TextBox>(view, "SandboxNameTextBox").Text);
            Assert.AreEqual("C:\\Selected\\sample.exe", Control<TextBox>(view, "ExecutablePathTextBox").Text);
        });
    }

    [TestMethod]
    public async Task FailedBackgroundCleanupRetainsTheTabAndItsRetryState()
    {
        await SandboxWorkspaceLifecycleTests.OnUi(async () =>
        {
            var manager = NewManager();
            var attempts = 0;
            var sandbox = new ExperimentalSandbox(new SandboxIdentity("Fake background profile", null),
                new ExperimentalSandboxOptions { DisplayName = "Background sandbox", UseAppContainer = false },
                () => ++attempts == 1 ? "Profile is temporarily busy." : null, profileMayExist: true);
            manager.TrackSandbox(sandbox);
            using var view = new ExperimentalSandboxWorkspaceView(() => manager);
            await view.InitializeAsync();
            var background = new ExperimentalSandboxCard(sandbox.DisplayName);
            background.Attach(sandbox);
            AddCard(view, background);
            var selected = AddCard(view, new ExperimentalSandboxCard("Selected draft"), select: true);

            var firstResult = await view.CloseSandboxAsync(background);

            Assert.IsNotNull(firstResult);
            Assert.IsFalse(firstResult.Completed);
            Assert.HasCount(2, Cards(view));
            Assert.AreSame(selected, Control<InstanceTabStrip>(view, "SandboxList").SelectedItem);
            Assert.AreEqual("CLEANUP NEEDED", background.StateBadge);
            StringAssert.Contains(Control<TextBlock>(view, "WorkspaceNoticeText").Text, "Background sandbox");
            StringAssert.Contains(Control<TextBlock>(view, "WorkspaceNoticeText").Text, "Profile is temporarily busy.");
            Control<InstanceTabStrip>(view, "SandboxList").SelectedItem = background;
            Assert.AreEqual("_Retry cleanup", Control<Button>(view, "CloseSandboxButton").Content);
            Assert.IsFalse(Control<Button>(view, "CreateAndLaunchButton").IsEnabled);
            Control<InstanceTabStrip>(view, "SandboxList").SelectedItem = selected;

            var secondResult = await view.CloseSandboxAsync(background);

            Assert.IsNotNull(secondResult);
            Assert.IsTrue(secondResult.Completed);
            Assert.HasCount(1, Cards(view));
            Assert.AreSame(selected, Control<InstanceTabStrip>(view, "SandboxList").SelectedItem);
            Assert.AreEqual(2, attempts);
        });
    }

    [TestMethod]
    public async Task CleanupDisablesTabActionsAndDoesNotDiscardAnotherDraft()
    {
        await SandboxWorkspaceLifecycleTests.OnUi(async () =>
        {
            using var release = new ManualResetEventSlim();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var manager = NewManager();
            var sandbox = new ExperimentalSandbox(new SandboxIdentity("Fake busy profile", null),
                new ExperimentalSandboxOptions { DisplayName = "Closing sandbox", UseAppContainer = false },
                () => { started.SetResult(); release.Wait(); return null; }, profileMayExist: true);
            manager.TrackSandbox(sandbox);
            using var view = new ExperimentalSandboxWorkspaceView(() => manager);
            await view.InitializeAsync();
            var background = new ExperimentalSandboxCard(sandbox.DisplayName);
            background.Attach(sandbox);
            AddCard(view, background);
            var selected = AddCard(view, new ExperimentalSandboxCard("Selected draft"), select: true);

            var closing = view.CloseSandboxAsync(background);
            try
            {
                await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.IsTrue(view.IsBusy);
                Assert.IsFalse(Control<InstanceTabStrip>(view, "SandboxList").IsEnabled);
                Assert.IsFalse(Control<InstanceTabStrip>(view, "SandboxList").IsNewEnabled);
                Assert.IsFalse(Control<InstanceTabStrip>(view, "SandboxList").RequestClose(selected));
                Assert.IsNull(await view.CloseSandboxAsync(selected));
                Assert.HasCount(2, Cards(view));
                Assert.AreSame(selected, Control<InstanceTabStrip>(view, "SandboxList").SelectedItem);
            }
            finally { release.Set(); }
            await closing;
            Assert.HasCount(1, Cards(view));
            Assert.AreSame(selected, Control<InstanceTabStrip>(view, "SandboxList").SelectedItem);
            Assert.IsTrue(Control<InstanceTabStrip>(view, "SandboxList").IsNewEnabled);
        });
    }

    [TestMethod]
    public async Task UnavailableSupportKeepsNewActionsDisabledAndReasonVisible()
    {
        await SandboxWorkspaceLifecycleTests.OnUi(async () =>
        {
            using var view = new ExperimentalSandboxWorkspaceView(() => new ExperimentalSandboxManager(() => new(
                ExperimentalSandboxAvailability.FeatureDisabled, "Windows has disabled sandbox creation.",
                new Version(10, 0), null, true, false, null, 120, [])));
            await view.InitializeAsync();

            var tabs = Control<InstanceTabStrip>(view, "SandboxList");
            Assert.IsFalse(tabs.IsNewEnabled);
            Assert.AreEqual("Windows has disabled sandbox creation.", tabs.NewToolTip);
            Assert.IsFalse(Control<Button>(view, "EmptyNewSandboxButton").IsEnabled);
            Assert.AreEqual("Installed, but unavailable", Control<TextBlock>(view, "SupportStateText").Text);
            Assert.AreEqual(Visibility.Visible, Control<TextBlock>(view, "EmptySandboxListHint").Visibility);
        });
    }

    [TestMethod]
    public async Task FailedSupportCheckLeavesAnActionableHeaderVisible()
    {
        await SandboxWorkspaceLifecycleTests.OnUi(async () =>
        {
            using var view = new ExperimentalSandboxWorkspaceView(() => throw new IOException("Temporary support failure"));
            await view.InitializeAsync();

            StringAssert.Contains(Control<TextBlock>(view, "SupportStateText").Text, "Refresh support");
            Assert.AreEqual("Temporary support failure", Control<TextBlock>(view, "SupportDetailText").Text);
            Assert.IsFalse(Control<InstanceTabStrip>(view, "SandboxList").IsNewEnabled);
        });
    }

    [TestMethod]
    public async Task ExpandedLongSupportDetailsLeaveEditorSpaceAtMinimumHeight()
    {
        await SandboxWorkspaceLifecycleTests.OnUi(async () =>
        {
            var features = Enumerable.Range(1, 100).Select(index => new ExperimentalFeatureState(
                (uint)index, "Sandbox specification", ExperimentalFeatureConfigurationState.Unknown, null)).ToArray();
            using var view = new ExperimentalSandboxWorkspaceView(() => new ExperimentalSandboxManager(() => new(
                ExperimentalSandboxAvailability.Available, "Fake support", new Version(10, 0), new string('v', 4096),
                true, false, null, null, features)));
            view.FontSize = 13;
            view.FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            await view.InitializeAsync();
            AddCard(view, new ExperimentalSandboxCard("Draft"), select: true);
            var details = Control<ScrollViewer>(view, "SupportDetailsScrollViewer");
            Control<Expander>(view, "SupportDetailsExpander").IsExpanded = true;
            view.Measure(new Size(952, 500));
            view.Arrange(new Rect(0, 0, 952, 500));
            view.UpdateLayout();

            Assert.IsTrue(details.ActualHeight <= 48, $"Support details height was {details.ActualHeight}.");
            Assert.IsTrue(details.ScrollableHeight > 0, "Long diagnostics should remain readable by scrolling.");
            var editor = Control<Grid>(view, "SandboxEditorHost");
            Assert.IsTrue(editor.ActualHeight >= 150, $"Editor height was {editor.ActualHeight}.");
            var viewport = Control<ScrollViewer>(view, "SandboxEditorScrollViewer").ViewportHeight;
            var headerHeight = Control<Border>(view, "SandboxInstanceHeader").ActualHeight;
            Assert.IsTrue(viewport > 100,
                $"Editor viewport was {viewport}; selected header {headerHeight}; " +
                $"editor host {editor.ActualHeight}; support details {details.ActualHeight}.");
            var summary = Control<ScrollViewer>(view, "SandboxSummaryScrollViewer");
            Assert.IsTrue(summary.ViewportHeight > 40, $"Summary viewport was {summary.ViewportHeight}.");
            Assert.IsTrue(summary.IsAncestorOf(Control<TextBlock>(view, "LifecycleExplanationText")),
                "Closing details must scroll with the summary while actions remain pinned.");
            Assert.IsFalse(summary.IsAncestorOf(Control<Button>(view, "CreateAndLaunchButton")));
            Assert.IsFalse(summary.IsAncestorOf(Control<Button>(view, "CloseSandboxButton")));
            Assert.AreEqual("Ready on this Windows build", Control<TextBlock>(view, "SupportStateText").Text);
        });
    }

    private static ExperimentalSandboxManager NewManager() => new(() => new ExperimentalSandboxSupport(
        ExperimentalSandboxAvailability.Available, "Fake support", new Version(10, 0), null, true, false, null, null, []));

    private static ExperimentalSandboxCard AddCard(ExperimentalSandboxWorkspaceView view, ExperimentalSandboxCard card, bool select = false)
    {
        Cards(view).Add(card);
        if (select) { Control<InstanceTabStrip>(view, "SandboxList").SelectedItem = card; }
        return card;
    }

    private static ObservableCollection<ExperimentalSandboxCard> Cards(ExperimentalSandboxWorkspaceView view) =>
        (ObservableCollection<ExperimentalSandboxCard>)Control<InstanceTabStrip>(view, "SandboxList").ItemsSource;

    private static void Layout(ExperimentalSandboxWorkspaceView view)
    {
        view.Measure(new Size(952, 650));
        view.Arrange(new Rect(0, 0, 952, 650));
        view.UpdateLayout();
    }

    private static T Control<T>(ExperimentalSandboxWorkspaceView view, string name) where T : class => (T)view.FindName(name);
}
