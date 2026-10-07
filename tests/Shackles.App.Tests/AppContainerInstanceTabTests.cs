using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Shackles.App.Controls;
using Shackles.App.Models;
using Shackles.App.Views;
using Shackles.AppContainers;
using Shackles.AppContainers.Internal;

namespace Shackles.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class AppContainerInstanceTabTests
{
    [TestMethod]
    public async Task LaunchTrackingExplanationScrollsWithTheSummary()
    {
        await Run(fixture =>
        {
            fixture.Tabs.SelectedItem = fixture.AddDraft("Draft");
            fixture.Layout();
            var summary = Control<ScrollViewer>(fixture.View, "SandboxSummaryScrollViewer");
            Assert.IsTrue(summary.IsAncestorOf(Control<TextBlock>(fixture.View, "LifecycleExplanationText")));
            Assert.IsFalse(summary.IsAncestorOf(Control<Button>(fixture.View, "CreateAndLaunchButton")));
            Assert.IsFalse(summary.IsAncestorOf(Control<Button>(fixture.View, "CloseSandboxButton")));
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task PreviewEventsRefreshTheDraftHeaderOnce()
    {
        await Run(fixture =>
        {
            var card = fixture.AddDraft("Draft");
            fixture.Tabs.SelectedItem = card;
            var notices = new List<string?>();
            card.PropertyChanged += (_, args) => notices.Add(args.PropertyName);
            Control<TextBox>(fixture.View, "SandboxNameTextBox").Text = "Renamed";
            Assert.AreEqual("Renamed", card.DisplayName);
            Assert.HasCount(4, notices);

            var internet = Control<CheckBox>(fixture.View, "InternetClientCheckBox");
            notices.Clear();
            internet.IsChecked = true;
            Assert.IsTrue(card.Draft.InternetClient);
            StringAssert.Contains(card.PolicySummary, "1 network grant");
            Assert.HasCount(4, notices);
            internet.IsChecked = false;
            Assert.IsFalse(card.Draft.InternetClient);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task NewActionReusesTheExistingDraftAndItsEdits()
    {
        await Run(async fixture =>
        {
            fixture.Layout();
            var newButton = (Button)fixture.Tabs.Template.FindName("PART_NewButton", fixture.Tabs);
            newButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var draft = fixture.Cards.Single();
            Control<TextBox>(fixture.View, "SandboxNameTextBox").Text = "Edited draft";
            Control<TextBox>(fixture.View, "ExecutablePathTextBox").Text = "draft.exe";
            var active = fixture.AddActive("Existing sandbox", _ => null);
            fixture.Tabs.SelectedItem = active;

            newButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.HasCount(2, fixture.Cards);
            Assert.AreSame(draft, fixture.Tabs.SelectedItem);
            Assert.AreEqual("Edited draft", draft.DisplayName);
            Assert.AreEqual("draft.exe", draft.Draft.ExecutablePath);
            Assert.IsTrue(fixture.Tabs.IsNewEnabled);
            Assert.AreEqual("Edited draft", Control<TextBox>(fixture.View, "SandboxNameTextBox").Text);
            await Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task ClosingABackgroundDraftPreservesTheSelectedSandbox()
    {
        var confirmations = 0;
        await Run(async fixture =>
        {
            var draft = fixture.AddDraft("Draft to discard");
            var profileDeletes = 0;
            var selected = fixture.AddActive("Selected sandbox", _ => { profileDeletes++; return null; });
            fixture.Tabs.SelectedItem = selected;

            fixture.Layout();
            Assert.IsTrue(fixture.Tabs.RequestClose(draft), "The strip must route close to the clicked background card.");
            await Task.CompletedTask;

            Assert.HasCount(1, fixture.Cards);
            Assert.AreSame(selected, fixture.Tabs.SelectedItem);
            Assert.AreEqual("Selected sandbox", Control<TextBlock>(fixture.View, "SandboxHeaderNameText").Text);
            Assert.AreEqual(0, profileDeletes, "Discarding the draft must not clean the selected sandbox.");
            Assert.AreEqual(0, confirmations, "Discarding an uncreated draft does not need native cleanup confirmation.");
        }, (_, _) => { confirmations++; return true; });
    }

    [TestMethod]
    public async Task ClosingABackgroundSandboxConfirmsAndCleansThatTarget()
    {
        var question = string.Empty;
        await Run(async fixture =>
        {
            var profileDeletes = 0;
            var target = fixture.AddActive("Background sandbox", _ => { profileDeletes++; return null; });
            var selected = fixture.AddDraft("Selected draft");
            fixture.Tabs.SelectedItem = selected;

            await fixture.View.CloseSandboxAsync(target);

            StringAssert.Contains(question, "Background sandbox");
            Assert.AreEqual(1, profileDeletes);
            Assert.HasCount(1, fixture.Cards);
            Assert.AreSame(selected, fixture.Tabs.SelectedItem);
            Assert.AreEqual("Selected draft", Control<TextBox>(fixture.View, "SandboxNameTextBox").Text);
            Assert.IsEmpty(fixture.Manager.Sandboxes);
        }, (text, _) => { question = text; return true; });
    }

    [TestMethod]
    public async Task CancelingABackgroundClosePreservesBothTabsAndSelection()
    {
        await Run(async fixture =>
        {
            var profileDeletes = 0;
            var target = fixture.AddActive("Background sandbox", _ => { profileDeletes++; return null; });
            var selected = fixture.AddDraft("Selected draft");
            fixture.Tabs.SelectedItem = selected;

            await fixture.View.CloseSandboxAsync(target);

            Assert.HasCount(2, fixture.Cards);
            Assert.AreSame(selected, fixture.Tabs.SelectedItem);
            Assert.AreEqual(0, profileDeletes);
            Assert.IsFalse(target.Sandbox!.IsClosed);
        }, (_, _) => false);
    }

    [TestMethod]
    public async Task FailedBackgroundCleanupKeepsItsTabAndRetriesOnlyThatSandbox()
    {
        var reportedWarnings = new List<string>();
        await Run(async fixture =>
        {
            var attempts = 0;
            var target = fixture.AddActive("Background sandbox", _ => ++attempts == 1 ? "Profile is temporarily busy." : null);
            var selected = fixture.AddDraft("Selected draft");
            fixture.Tabs.SelectedItem = selected;

            await fixture.View.CloseSandboxAsync(target);

            Assert.HasCount(2, fixture.Cards);
            Assert.AreEqual("CLEANUP NEEDED", target.StateBadge);
            Assert.AreSame(selected, fixture.Tabs.SelectedItem);
            Assert.HasCount(1, reportedWarnings);
            Assert.AreSame(target.Sandbox, fixture.Manager.Sandboxes.Single());
            Assert.IsTrue(Control<Button>(fixture.View, "CreateAndLaunchButton").IsEnabled,
                "The selected draft remains editable after cleanup of another tab fails.");

            await fixture.View.CloseSandboxAsync(target);

            Assert.HasCount(1, fixture.Cards);
            Assert.AreSame(selected, fixture.Tabs.SelectedItem);
            Assert.AreEqual(2, attempts);
            Assert.IsEmpty(fixture.Manager.Sandboxes);
            fixture.View.Dispose();
            Assert.AreEqual(2, attempts, "Completed tab cleanup must not repeat during workspace disposal.");
        }, reportCleanupWarnings: warnings => reportedWarnings.AddRange(warnings));
    }

    [TestMethod]
    public async Task BusyCleanupDisablesTabActionsAndIgnoresAnotherClose()
    {
        await Run(async fixture =>
        {
            using var release = new ManualResetEventSlim();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var target = fixture.AddActive("Closing sandbox", _ =>
            {
                started.SetResult();
                release.Wait();
                return null;
            });
            var selected = fixture.AddDraft("Selected draft");
            fixture.Tabs.SelectedItem = selected;
            var closing = fixture.View.CloseSandboxAsync(target);
            try
            {
                await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.IsTrue(fixture.View.IsBusy);
                Assert.IsFalse(fixture.Tabs.IsEnabled);
                Assert.IsFalse(fixture.Tabs.IsNewEnabled);
                Assert.IsFalse(Control<Button>(fixture.View, "EmptyNewSandboxButton").IsEnabled);
                await fixture.View.CloseSandboxAsync(selected);
                Assert.HasCount(2, fixture.Cards);
            }
            finally { release.Set(); }

            await closing;
            Assert.IsFalse(fixture.View.IsBusy);
            Assert.IsTrue(fixture.Tabs.IsEnabled);
            Assert.IsTrue(fixture.Tabs.IsNewEnabled);
            Assert.AreSame(selected, fixture.Tabs.SelectedItem);
        });
    }

    [TestMethod]
    public async Task ClosingSelectedAndLastTabsSelectsANeighborThenShowsTheEmptyState()
    {
        await Run(async fixture =>
        {
            var target = fixture.AddActive("Selected sandbox", _ => null);
            var neighbor = fixture.AddDraft("Neighbor draft");
            fixture.Tabs.SelectedItem = target;

            await fixture.View.CloseSandboxAsync(target);
            Assert.AreSame(neighbor, fixture.Tabs.SelectedItem);
            Assert.AreEqual("Neighbor draft", Control<TextBlock>(fixture.View, "SandboxHeaderNameText").Text);

            await fixture.View.CloseSandboxAsync(neighbor);
            Assert.IsEmpty(fixture.Cards);
            Assert.IsNull(fixture.Tabs.SelectedItem);
            Assert.AreEqual(Visibility.Visible, Control<Border>(fixture.View, "EmptyEditorState").Visibility);
            Assert.IsTrue(fixture.Tabs.IsNewEnabled);
        });
    }

    [TestMethod]
    public async Task HorizontalTabsAndSelectedEditorUseTheFullWorkspaceWidth()
    {
        await Run(fixture =>
        {
            var draft = fixture.AddDraft("Layout draft");
            fixture.Tabs.SelectedItem = draft;
            fixture.Layout();
            var editor = Control<Grid>(fixture.View, "SandboxEditorHost");

            Assert.AreEqual(fixture.View.ActualWidth, fixture.Tabs.ActualWidth, 1);
            Assert.AreEqual(fixture.View.ActualWidth, editor.ActualWidth, 1);
            Assert.AreEqual(0d, editor.TranslatePoint(new Point(), fixture.View).X, 1);
            Assert.IsTrue(editor.TranslatePoint(new Point(), fixture.View).Y >
                          fixture.Tabs.TranslatePoint(new Point(), fixture.View).Y + fixture.Tabs.ActualHeight);
            Assert.IsNotNull(fixture.Tabs.ItemTemplate);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task LoadingAndInitializationFailureAreVisibleWhenNoTabIsSelected()
    {
        await SandboxWorkspaceLifecycleTests.OnUi(async () =>
        {
            using var release = new ManualResetEventSlim();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var view = new AppContainerWorkspaceView(() =>
            {
                started.SetResult();
                release.Wait();
                throw new IOException("Recovery could not read the saved session.");
            });
            var initializing = view.InitializeAsync();
            try
            {
                await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.AreEqual(Visibility.Visible, Control<Border>(view, "EmptyWorkspaceNotice").Visibility);
                StringAssert.Contains(Control<TextBlock>(view, "EmptyWorkspaceNoticeText").Text, "Loading");
                Assert.IsFalse(Control<InstanceTabStrip>(view, "SandboxList").IsNewEnabled);
                Assert.AreEqual(Visibility.Collapsed, Control<Border>(view, "WorkspaceNotice").Visibility);
            }
            finally { release.Set(); }

            await initializing;
            Assert.IsFalse(view.IsBusy);
            Assert.AreEqual(Visibility.Visible, Control<Border>(view, "EmptyWorkspaceNotice").Visibility);
            StringAssert.Contains(Control<TextBlock>(view, "EmptyWorkspaceNoticeText").Text, "Recovery could not read");
            Assert.IsFalse(Control<InstanceTabStrip>(view, "SandboxList").IsNewEnabled);
        });
    }

    [TestMethod]
    public async Task RecoveryWarningsAreVisibleBeforeTheFirstDraft()
    {
        var directory = Directory.CreateTempSubdirectory("Shackles-app-tab-notice-").FullName;
        try
        {
            var journalFileName = $"Shackles.{Guid.NewGuid():N}.json";
            File.WriteAllText(Path.Combine(directory, journalFileName), "{ invalid journal }");
            await SandboxWorkspaceLifecycleTests.OnUi(async () =>
            {
                AppContainerManager? manager = null;
                using var view = new AppContainerWorkspaceView(() => manager = new AppContainerManager(directory, new FakeBfs()));
                await view.InitializeAsync();

                Assert.IsNotNull(manager);
                Assert.HasCount(1, manager.RecoveryResult.Warnings, "The fixture must exercise the recovery-warning path.");
                Assert.AreEqual(Visibility.Visible, Control<Border>(view, "EmptyWorkspaceNotice").Visibility);
                StringAssert.Contains(Control<TextBlock>(view, "EmptyWorkspaceNoticeText").Text, journalFileName);
                Assert.AreEqual(manager.RecoveryResult.Warnings.Single(),
                    Control<TextBlock>(view, "EmptyWorkspaceNoticeText").ToolTip);
                Assert.IsTrue(Control<InstanceTabStrip>(view, "SandboxList").IsNewEnabled);
                Assert.AreEqual(Visibility.Collapsed, Control<Border>(view, "WorkspaceNotice").Visibility);
            });
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task SelectingATabMovesTheNoticeIntoItsSummaryWithoutDuplicatingIt()
    {
        await Run(async fixture =>
        {
            Assert.AreEqual(Visibility.Visible, Control<Border>(fixture.View, "EmptyWorkspaceNotice").Visibility);
            var draft = fixture.AddDraft("Selected draft");
            fixture.Tabs.SelectedItem = draft;

            Assert.AreEqual(Visibility.Collapsed, Control<Border>(fixture.View, "EmptyWorkspaceNotice").Visibility);
            Assert.AreEqual(Visibility.Visible, Control<Border>(fixture.View, "WorkspaceNotice").Visibility);
            Assert.IsFalse(string.IsNullOrWhiteSpace(Control<TextBlock>(fixture.View, "WorkspaceNoticeText").Text));

            await fixture.View.CloseSandboxAsync(draft);
            Assert.AreEqual(Visibility.Visible, Control<Border>(fixture.View, "EmptyWorkspaceNotice").Visibility);
            Assert.AreEqual(Visibility.Collapsed, Control<Border>(fixture.View, "WorkspaceNotice").Visibility);
        });
    }

    private static Task Run(
        Func<WorkspaceFixture, Task> action,
        Func<string, MessageBoxImage, bool>? confirmSandboxClose = null,
        Action<IReadOnlyList<string>>? reportCleanupWarnings = null) =>
        SandboxWorkspaceLifecycleTests.OnUi(async () =>
        {
            using var fixture = new WorkspaceFixture(confirmSandboxClose, reportCleanupWarnings);
            await fixture.View.InitializeAsync();
            await action(fixture);
        });

    private static T Control<T>(AppContainerWorkspaceView view, string name) where T : class => (T)view.FindName(name);

    private sealed class WorkspaceFixture : IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("Shackles-app-tabs-").FullName;
        private readonly FakeBfs _bfs = new();

        internal WorkspaceFixture(
            Func<string, MessageBoxImage, bool>? confirmSandboxClose,
            Action<IReadOnlyList<string>>? reportCleanupWarnings)
        {
            Manager = new AppContainerManager(_directory, _bfs);
            View = new AppContainerWorkspaceView(() => Manager,
                confirmSandboxClose ?? ((_, _) => true), reportCleanupWarnings ?? (_ => { }));
            Tabs = Control<InstanceTabStrip>(View, "SandboxList");
            Cards = (ObservableCollection<AppContainerSandboxCard>)Tabs.ItemsSource;
        }

        internal AppContainerManager Manager { get; }
        internal AppContainerWorkspaceView View { get; }
        internal InstanceTabStrip Tabs { get; }
        internal ObservableCollection<AppContainerSandboxCard> Cards { get; }

        internal void Layout()
        {
            View.ApplyTemplate();
            View.Measure(new Size(1040, 700));
            View.Arrange(new Rect(0, 0, 1040, 700));
            View.UpdateLayout();
            Tabs.ApplyTemplate();
            Tabs.UpdateLayout();
        }

        internal AppContainerSandboxCard AddDraft(string name)
        {
            var card = new AppContainerSandboxCard(name);
            Cards.Add(card);
            return card;
        }

        internal AppContainerSandboxCard AddActive(string name, Func<string, string?> deleteProfile)
        {
            var identity = new AppContainerIdentity($"Shackles.{Guid.NewGuid():N}", "S-1-15-2-1", []);
            var journal = CleanupJournal.Create(_directory, identity, name);
            var sandbox = new AppContainerSandbox(identity, [], new AppContainerSandboxOptions { DisplayName = name },
                journal, _bfs, deleteProfile);
            Manager.TrackSandbox(sandbox);
            var card = new AppContainerSandboxCard(name);
            card.Attach(sandbox);
            Cards.Add(card);
            return card;
        }

        public void Dispose()
        {
            View.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class FakeBfs : IBrokeredFileSystemConfigurator
    {
        public BrokeredFileSystemSupport Support { get; } = new(BrokeredFileSystemAvailability.Available,
            "Fake policy backend", new Version(10, 0), null, null, false, []);
        public void AddPolicy(string appContainerName, TrackedAclGrant grant) { }
        public string? TryClearPolicy(string appContainerName) => null;
    }
}
