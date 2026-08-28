using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using Shackles.App.Models;
using Shackles.AppContainers;

namespace Shackles.App.Views;

public sealed partial class AppContainerWorkspaceView : UserControl, IDisposable
{
    private readonly ObservableCollection<AppContainerSandboxCard> _cards = [];
    private readonly AppContainerManager _manager;
    private AppContainerSandboxCard? _loadedCard;
    private bool _isReady;
    private bool _isBusy;
    private bool _hasPreparedInitialDisplay;
    private bool _disposed;

    public AppContainerWorkspaceView()
    {
        InitializeComponent();
        SandboxList.ItemsSource = _cards;
        _manager = new AppContainerManager();
        _isReady = true;
        UpdateEmptyStates();
    }

    public bool IsBusy => _isBusy;

    public int ActiveSandboxCount => _cards.Count(card => card.IsActive);

    public int TrackedLaunchCount => _cards
        .Where(card => card.Sandbox is not null)
        .Sum(card => card.Sandbox!.GetSnapshot().ProcessIds.Count);

    public bool CanHaveUntrackedDescendants => _cards.Any(
        card => card.IsActive && card.Draft.AllowChildren);

    public IReadOnlyList<string> ActiveSandboxNames => _cards
        .Where(card => card.IsActive)
        .Select(card => card.DisplayName)
        .ToArray();

    public void PrepareForDisplay()
    {
        if (_hasPreparedInitialDisplay)
        {
            return;
        }

        _hasPreparedInitialDisplay = true;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            SandboxEditorScrollViewer.ScrollToTop();
            SandboxSummaryScrollViewer.ScrollToTop();
            var recovery = _manager.RecoveryResult;
            if (recovery.RecoveredSessionCount > 0 || recovery.Warnings.Count > 0)
            {
                var parts = new List<string>();
                if (recovery.RecoveredSessionCount > 0)
                {
                    parts.Add(
                        $"Recovered {recovery.RecoveredSessionCount} stale sandbox " +
                        $"session{(recovery.RecoveredSessionCount == 1 ? string.Empty : "s")}.");
                }

                parts.AddRange(recovery.Warnings);
                MessageBox.Show(
                    Window.GetWindow(this),
                    string.Join(Environment.NewLine + Environment.NewLine, parts),
                    recovery.Warnings.Count == 0
                        ? "AppContainer cleanup recovered"
                        : "AppContainer cleanup needs attention",
                    MessageBoxButton.OK,
                    recovery.Warnings.Count == 0
                        ? MessageBoxImage.Information
                        : MessageBoxImage.Warning);
            }
        });
    }

    private void NewSandbox_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        var existingDraft = _cards.FirstOrDefault(card => !card.IsActive);
        if (existingDraft is not null)
        {
            SandboxList.SelectedItem = existingDraft;
            ShowNotice(
                "The existing draft is selected. Finish or discard it before starting another draft.");
            return;
        }

        var card = new AppContainerSandboxCard(GetNextSandboxName());
        _cards.Add(card);
        SandboxList.SelectedItem = card;
        UpdateEmptyStates();
        ShowNotice(
            "Draft started. No Windows profile, SID, host grant, or process exists yet.");
    }

    private string GetNextSandboxName()
    {
        var index = 1;
        var existingNames = _cards
            .Select(card => card.DisplayName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        while (existingNames.Contains($"Sandbox {index}"))
        {
            index++;
        }

        return $"Sandbox {index}";
    }

    private void SandboxList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_isReady)
        {
            return;
        }

        LoadSelectedCard();
    }

    private void LoadSelectedCard()
    {
        _loadedCard = SandboxList.SelectedItem as AppContainerSandboxCard;
        UpdateEmptyStates();
        if (_loadedCard is null)
        {
            return;
        }

        _isReady = false;
        try
        {
            _loadedCard.Refresh();
            var draft = _loadedCard.Draft;
            SandboxNameTextBox.Text = draft.Name;
            StandardIsolationRadio.IsChecked = !draft.UseLowPrivilege;
            LpacIsolationRadio.IsChecked = draft.UseLowPrivilege;
            AllowChildrenCheckBox.IsChecked = draft.AllowChildren;
            MinimalEnvironmentCheckBox.IsChecked = draft.UseMinimalEnvironment;
            ExecutablePathTextBox.Text = draft.ExecutablePath;
            ArgumentsTextBox.Text = draft.Arguments;
            WorkingDirectoryTextBox.Text = draft.WorkingDirectory;
            IncludeTargetAccessCheckBox.IsChecked = draft.IncludeTargetAccess;
            InternetClientCheckBox.IsChecked = draft.InternetClient;
            InternetServerCheckBox.IsChecked = draft.InternetServer;
            PrivateNetworkCheckBox.IsChecked = draft.PrivateNetwork;
            NetworkCredentialsCheckBox.IsChecked = draft.NetworkCredentials;
            PicturesLibraryCheckBox.IsChecked = draft.PicturesLibrary;
            VideosLibraryCheckBox.IsChecked = draft.VideosLibrary;
            MusicLibraryCheckBox.IsChecked = draft.MusicLibrary;
            RemovableStorageCheckBox.IsChecked = draft.RemovableStorage;
            FileSystemPathTextBox.Clear();
            FileSystemAccessComboBox.SelectedIndex = 0;
            FileGrantList.ItemsSource = draft.FileGrants;
            RegistryGrantList.ItemsSource = draft.RegistryGrants;
            RegistryAccessComboBox.SelectedIndex = 0;
            RegistryViewComboBox.SelectedIndex = 0;
            RegistryKeyTextBox.Clear();
        }
        finally
        {
            _isReady = true;
        }

        var active = _loadedCard.IsActive;
        PolicyControlsPanel.IsEnabled = !active;
        SharedAccessTabs.IsEnabled = !active;
        ResetDraftButton.IsEnabled = !active && !_isBusy;
        EditorStateBadgeText.Text = active ? "ACTIVE" : "DRAFT";
        SandboxIdentityText.Text = active
            ? _loadedCard.Sandbox!.Sid
            : "SID allocated on first launch";
        CreateAndLaunchButton.Content = active
            ? "_Launch in sandbox"
            : "_Create sandbox and launch";
        CloseSandboxButton.Content = active ? "_Close sandbox" : "_Discard draft";
        RefreshPreview();
    }

    private void SaveControlsToDraft()
    {
        if (_loadedCard is null)
        {
            return;
        }

        var draft = _loadedCard.Draft;
        draft.ExecutablePath = ExecutablePathTextBox.Text.Trim();
        draft.Arguments = ArgumentsTextBox.Text;
        draft.WorkingDirectory = WorkingDirectoryTextBox.Text.Trim();
        draft.IncludeTargetAccess = IncludeTargetAccessCheckBox.IsChecked == true;
        if (_loadedCard.IsActive)
        {
            return;
        }

        draft.Name = SandboxNameTextBox.Text;
        draft.UseLowPrivilege = LpacIsolationRadio.IsChecked == true;
        draft.AllowChildren = AllowChildrenCheckBox.IsChecked == true;
        draft.UseMinimalEnvironment = MinimalEnvironmentCheckBox.IsChecked == true;
        draft.InternetClient = InternetClientCheckBox.IsChecked == true;
        draft.InternetServer = InternetServerCheckBox.IsChecked == true;
        draft.PrivateNetwork = PrivateNetworkCheckBox.IsChecked == true;
        draft.NetworkCredentials = NetworkCredentialsCheckBox.IsChecked == true;
        draft.PicturesLibrary = PicturesLibraryCheckBox.IsChecked == true;
        draft.VideosLibrary = VideosLibraryCheckBox.IsChecked == true;
        draft.MusicLibrary = MusicLibraryCheckBox.IsChecked == true;
        draft.RemovableStorage = RemovableStorageCheckBox.IsChecked == true;
    }

    private void RefreshPreview()
    {
        if (!_isReady || _loadedCard is null)
        {
            return;
        }

        SaveControlsToDraft();
        _loadedCard.RefreshDraft();
        if (_loadedCard.IsActive)
        {
            _loadedCard.Refresh();
        }

        var draft = _loadedCard.Draft;
        var displayName = _loadedCard.IsActive
            ? _loadedCard.DisplayName
            : string.IsNullOrWhiteSpace(draft.Name)
                ? "Untitled sandbox"
                : draft.Name.Trim();
        SandboxHeaderNameText.Text = displayName;
        NameSummaryText.Text = displayName;
        LifetimeSummaryText.Text = "Until closed or Shackles exits";
        IsolationSummaryText.Text = draft.UseLowPrivilege
            ? "Strict (LPAC)"
            : "Standard";
        ChildrenSummaryText.Text = draft.AllowChildren
            ? "Allowed; descendants are not tracked"
            : "Blocked";

        var network = new List<string>();
        if (draft.InternetClient)
        {
            network.Add("Internet client");
        }

        if (draft.InternetServer)
        {
            network.Add("Internet client/server");
        }

        if (draft.PrivateNetwork)
        {
            network.Add("Private networks");
        }

        NetworkSummaryText.Text = network.Count == 0
            ? "None"
            : string.Join(", ", network);
        CredentialsSummaryText.Text = draft.NetworkCredentials
            ? "Windows credentials allowed on network resources"
            : "Not available to sandbox members";
        CapabilitiesSummaryText.Text = draft.CuratedCapabilityCount == 0
            ? "None"
            : $"{draft.CuratedCapabilityCount} curated";

        var explicitAclGrantCount =
            draft.FileGrants.Count + draft.RegistryGrants.Count;
        var explicitAclSummary = explicitAclGrantCount == 0
            ? "No explicit ACL grants"
            : $"{explicitAclGrantCount} temporary ACL grant" +
              (explicitAclGrantCount == 1 ? string.Empty : "s");
        HostChangesSummaryText.Text = explicitAclSummary +
            (draft.IncludeTargetAccess
                ? "; executable folder may also receive one"
                : string.Empty);

        var snapshot = _loadedCard.Snapshot;
        var processIds = snapshot?.ProcessIds ?? Array.Empty<int>();
        MemberCountSummaryText.Text = processIds.Count.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        MemberProcessList.ItemsSource = processIds
            .Select(processId => $"PID {processId}")
            .ToArray();
        CleanupSummaryText.Text =
            "When Shackles exits or you close this sandbox, Shackles terminates tracked launches, " +
            "removes tracked grants, and deletes the generated profile." +
            (draft.AllowChildren
                ? " Descendants are not tracked and may continue running."
                : string.Empty);
    }

    private void PreviewOption_Changed(object sender, RoutedEventArgs e) =>
        RefreshPreview();

    private void PreviewSelection_Changed(
        object sender,
        SelectionChangedEventArgs e) =>
        RefreshPreview();

    private void PreviewText_Changed(object sender, TextChangedEventArgs e) =>
        RefreshPreview();

    private void BrowseExecutable_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = _loadedCard?.IsActive == true
                ? $"Choose a process for {_loadedCard.DisplayName}"
                : "Choose the first process for this sandbox",
            Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        ExecutablePathTextBox.Text = dialog.FileName;

        ShowNotice(
            _loadedCard?.IsActive == true
                ? "Ready to launch another member into the selected sandbox."
                : "The executable is part of this draft. Nothing has been launched.");
    }

    private void BrowseWorkingDirectory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose the process working directory",
            Multiselect = false
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            WorkingDirectoryTextBox.Text = dialog.FolderName;
        }
    }

    private void BrowseFileGrant_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedCard is null || _loadedCard.IsActive)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Choose a file to make available to the sandbox",
            Filter = "All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            FileSystemPathTextBox.Text = dialog.FileName;
            FileSystemPathTextBox.Focus();
        }
    }

    private void BrowseFolderGrant_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedCard is null || _loadedCard.IsActive)
        {
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "Choose a folder to make available to the sandbox",
            Multiselect = false
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            FileSystemPathTextBox.Text = dialog.FolderName;
            FileSystemPathTextBox.Focus();
        }
    }

    private void AddFileSystemGrant_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedCard is null || _loadedCard.IsActive)
        {
            return;
        }

        var path = FileSystemPathTextBox.Text.Trim();
        if (path.Length == 0)
        {
            ShowNotice("Enter an existing file or folder path.");
            FileSystemPathTextBox.Focus();
            return;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception)
        {
            ShowNotice($"The host path is invalid: {exception.Message}");
            FileSystemPathTextBox.Focus();
            return;
        }

        var isDirectory = Directory.Exists(fullPath);
        if (!isDirectory && !File.Exists(fullPath))
        {
            ShowNotice("The file or folder does not exist.");
            FileSystemPathTextBox.Focus();
            return;
        }

        if (_loadedCard.Draft.FileGrants.Any(item =>
                string.Equals(
                    item.Path,
                    fullPath,
                    StringComparison.OrdinalIgnoreCase)))
        {
            ShowNotice("That host path is already in this sandbox draft.");
            return;
        }

        _loadedCard.Draft.FileGrants.Add(
            new AppContainerFileGrantDraft(
                fullPath,
                isDirectory,
                Math.Max(0, FileSystemAccessComboBox.SelectedIndex)));
        FileSystemPathTextBox.Clear();
        RefreshPreview();
        ShowNotice(
            "Pending file-system ACL grant added. Host permissions do not change until the sandbox is created.");
    }

    private void RemoveFileGrant_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedCard is null ||
            sender is not Button
            {
                Tag: AppContainerFileGrantDraft grant
            })
        {
            return;
        }

        _loadedCard.Draft.FileGrants.Remove(grant);
        RefreshPreview();
    }

    private void AddRegistryGrant_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedCard is null || _loadedCard.IsActive)
        {
            return;
        }

        var path = RegistryKeyTextBox.Text.Trim().TrimEnd(Path.DirectorySeparatorChar);
        if (path.Length == 0)
        {
            ShowNotice(
                "Enter a registry key path such as HKCU\\Software\\Contoso.");
            RegistryKeyTextBox.Focus();
            return;
        }

        if (_loadedCard.Draft.RegistryGrants.Any(item =>
                string.Equals(
                    item.Path,
                    path,
                    StringComparison.OrdinalIgnoreCase) &&
                item.ViewIndex == RegistryViewComboBox.SelectedIndex))
        {
            ShowNotice("That registry key and view are already in this draft.");
            return;
        }

        _loadedCard.Draft.RegistryGrants.Add(
            new AppContainerRegistryGrantDraft(
                path,
                Math.Max(0, RegistryAccessComboBox.SelectedIndex),
                Math.Max(0, RegistryViewComboBox.SelectedIndex)));
        RegistryKeyTextBox.Clear();
        RefreshPreview();
        ShowNotice(
            "Pending registry ACL grant added. Host permissions do not change until the sandbox is created.");
    }

    private void RemoveRegistryGrant_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedCard is null ||
            sender is not Button
            {
                Tag: AppContainerRegistryGrantDraft grant
            })
        {
            return;
        }

        _loadedCard.Draft.RegistryGrants.Remove(grant);
        RefreshPreview();
    }

    private void ResetDraft_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedCard is null || _loadedCard.IsActive || _isBusy)
        {
            return;
        }

        var name = _loadedCard.Draft.Name;
        _loadedCard.Draft.Reset(
            string.IsNullOrWhiteSpace(name) ? GetNextSandboxName() : name);
        LoadSelectedCard();
        ShowNotice("The sandbox draft has been reset. Nothing was created.");
    }

    private async void CreateAndLaunch_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedCard is null || _isBusy)
        {
            return;
        }

        SaveControlsToDraft();
        if (!ValidateLaunchDraft(_loadedCard.Draft))
        {
            return;
        }

        var card = _loadedCard;
        SetBusy(true);
        try
        {
            AppContainerLaunchResult launch;
            if (card.Sandbox is null)
            {
                var sandboxOptions = BuildSandboxOptions(card.Draft);
                var launchOptions = BuildLaunchOptions(card.Draft);
                var creation = await Task.Run(
                    () => _manager.CreateAndLaunch(
                        sandboxOptions,
                        launchOptions)).ConfigureAwait(true);
                launch = creation.FirstLaunch;
                card.Attach(creation.Sandbox);
                creation.Sandbox.Changed += SandboxChanged;
            }
            else
            {
                var launchOptions = BuildLaunchOptions(card.Draft);
                launch = await Task.Run(
                    () => card.Sandbox.Launch(launchOptions)).ConfigureAwait(true);
                card.Refresh();
            }

            if (_cards.Contains(card))
            {
                SandboxList.SelectedItem = card;
                LoadSelectedCard();
                ShowLaunchResult(
                    launch,
                    $"Launched PID {launch.ProcessId} in sandbox {card.Sandbox!.Sid}.");
            }
        }
        catch (Exception exception)
        {
            ShowNotice(
                card.IsActive
                    ? "Launch failed. The existing sandbox remains available."
                    : "Launch failed. The new profile and tracked grants were cleaned up; the draft remains editable.");
            MessageBox.Show(
                Window.GetWindow(this),
                exception.Message,
                card.IsActive
                    ? "Could not launch in sandbox"
                    : "Could not create sandbox",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
            if (_cards.Contains(card))
            {
                card.Refresh();
                RefreshPreview();
            }
        }
    }

    private async void CloseSandbox_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedCard is null || _isBusy)
        {
            return;
        }

        var card = _loadedCard;
        if (card.Sandbox is null)
        {
            RemoveCard(card);
            return;
        }

        var snapshot = card.Sandbox.GetSnapshot();
        var trackedCount = snapshot.ProcessIds.Count;
        var descendantNotice = card.Draft.AllowChildren
            ? " Descendants are not tracked and may continue running."
            : string.Empty;
        var question = trackedCount == 0
            ? $"Close '{card.DisplayName}' and remove its tracked grants and Windows profile?" +
              descendantNotice
            : $"Close '{card.DisplayName}'? This terminates {trackedCount} directly launched " +
              $"process{(trackedCount == 1 ? string.Empty : "es")}, removes tracked grants, " +
              "and deletes the Windows profile." + descendantNotice;
        var answer = MessageBox.Show(
            Window.GetWindow(this),
            question,
            "Close AppContainer sandbox",
            MessageBoxButton.YesNo,
            trackedCount == 0 ? MessageBoxImage.Question : MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var cleanup = await Task.Run(card.Sandbox.Close).ConfigureAwait(true);
            RemoveCard(card);
            if (!cleanup.Completed)
            {
                MessageBox.Show(
                    Window.GetWindow(this),
                    string.Join(Environment.NewLine + Environment.NewLine, cleanup.Warnings),
                    "Sandbox closed with incomplete cleanup",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static AppContainerSandboxOptions BuildSandboxOptions(
        AppContainerSandboxDraft draft)
    {
        var capabilities = new List<string>();
        if (draft.InternetClient)
        {
            capabilities.Add("internetClient");
        }

        if (draft.InternetServer)
        {
            capabilities.Add("internetClientServer");
        }

        if (draft.PrivateNetwork)
        {
            capabilities.Add("privateNetworkClientServer");
        }

        if (draft.NetworkCredentials)
        {
            capabilities.Add("enterpriseAuthentication");
            capabilities.Add("developmentModeNetwork");
        }

        if (draft.PicturesLibrary)
        {
            capabilities.Add("picturesLibrary");
        }

        if (draft.VideosLibrary)
        {
            capabilities.Add("videosLibrary");
        }

        if (draft.MusicLibrary)
        {
            capabilities.Add("musicLibrary");
        }

        if (draft.RemovableStorage)
        {
            capabilities.Add("removableStorage");
        }

        return new AppContainerSandboxOptions
        {
            DisplayName = draft.Name.Trim(),
            IsolationMode = draft.UseLowPrivilege
                ? AppContainerIsolationMode.LowPrivilege
                : AppContainerIsolationMode.Standard,
            RestrictChildProcessCreation = !draft.AllowChildren,
            UseMinimalEnvironment = draft.UseMinimalEnvironment,
            CapabilityNames = capabilities,
            FileSystemGrants = draft.FileGrants
                .Select(grant => new FileSystemGrant(
                    grant.Path,
                    grant.IsDirectory,
                    grant.AccessIndex == 0
                        ? FileSystemGrantAccess.ReadExecute
                        : FileSystemGrantAccess.ReadWriteDelete))
                .ToArray(),
            RegistryGrants = draft.RegistryGrants
                .Select(grant => new RegistryGrant(
                    grant.Path,
                    grant.AccessIndex == 0
                        ? RegistryGrantAccess.Read
                        : RegistryGrantAccess.ReadWrite,
                    grant.ViewIndex switch
                    {
                        1 => RegistryGrantView.Registry32,
                        2 => RegistryGrantView.Registry64,
                        _ => RegistryGrantView.Automatic
                    }))
                .ToArray()
        };
    }

    private static AppContainerLaunchOptions BuildLaunchOptions(
        AppContainerSandboxDraft draft) =>
        new(draft.ExecutablePath)
        {
            Arguments = draft.Arguments,
            WorkingDirectory = string.IsNullOrWhiteSpace(draft.WorkingDirectory)
                ? null
                : draft.WorkingDirectory,
            IncludeTargetDirectoryGrant = draft.IncludeTargetAccess
        };

    private bool ValidateLaunchDraft(AppContainerSandboxDraft draft)
    {
        if (_loadedCard?.IsActive != true &&
            string.IsNullOrWhiteSpace(draft.Name))
        {
            ShowNotice("Give the sandbox a name before creating it.");
            SandboxNameTextBox.Focus();
            return false;
        }

        if (string.IsNullOrWhiteSpace(draft.ExecutablePath))
        {
            ShowNotice("Choose an executable to launch.");
            ExecutablePathTextBox.Focus();
            return false;
        }

        try
        {
            if (!File.Exists(Path.GetFullPath(draft.ExecutablePath)))
            {
                ShowNotice("The selected executable no longer exists.");
                ExecutablePathTextBox.Focus();
                return false;
            }

            if (!string.IsNullOrWhiteSpace(draft.WorkingDirectory) &&
                !Directory.Exists(Path.GetFullPath(draft.WorkingDirectory)))
            {
                ShowNotice("The selected working directory no longer exists.");
                WorkingDirectoryTextBox.Focus();
                return false;
            }
        }
        catch (Exception exception)
        {
            ShowNotice($"The launch path is invalid: {exception.Message}");
            return false;
        }

        return true;
    }

    private void SandboxChanged(
        object? sender,
        AppContainerSandboxChangedEventArgs eventArgs)
    {
        if (_disposed || sender is not AppContainerSandbox sandbox)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(() =>
        {
            if (_disposed)
            {
                return;
            }

            var card = _cards.FirstOrDefault(item =>
                ReferenceEquals(item.Sandbox, sandbox));
            if (card is null)
            {
                return;
            }

            if (eventArgs.Closed)
            {
                var cleanup = sandbox.Close();
                RemoveCard(card);
                if (!cleanup.Completed)
                {
                    MessageBox.Show(
                        Window.GetWindow(this),
                        string.Join(
                            Environment.NewLine + Environment.NewLine,
                            cleanup.Warnings),
                        "Automatic sandbox cleanup was incomplete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

                return;
            }

            card.Refresh();
            if (ReferenceEquals(_loadedCard, card))
            {
                RefreshPreview();
            }
        });
    }

    private void ShowLaunchResult(
        AppContainerLaunchResult launch,
        string successMessage)
    {
        var message = successMessage;
        if (launch.Warnings.Count > 0)
        {
            message += " " + string.Join(" ", launch.Warnings);
        }

        ShowNotice(message);
    }

    private void RemoveCard(AppContainerSandboxCard card)
    {
        if (card.Sandbox is not null)
        {
            card.Sandbox.Changed -= SandboxChanged;
        }

        var oldIndex = _cards.IndexOf(card);
        _cards.Remove(card);
        if (_cards.Count > 0)
        {
            SandboxList.SelectedIndex = Math.Clamp(oldIndex, 0, _cards.Count - 1);
        }
        else
        {
            SandboxList.SelectedItem = null;
            _loadedCard = null;
        }

        UpdateEmptyStates();
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        OperationProgress.Visibility = busy
            ? Visibility.Visible
            : Visibility.Collapsed;
        CreateAndLaunchButton.IsEnabled = !busy;
        CloseSandboxButton.IsEnabled = !busy;
        SandboxList.IsEnabled = !busy;
        ResetDraftButton.IsEnabled =
            !busy && _loadedCard is { IsActive: false };
    }

    private void UpdateEmptyStates()
    {
        var hasSelection = SandboxList.SelectedItem is AppContainerSandboxCard;
        EmptySandboxListHint.Visibility = _cards.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        EmptyEditorState.Visibility = hasSelection
            ? Visibility.Collapsed
            : Visibility.Visible;
        SandboxEditorHost.Visibility = hasSelection
            ? Visibility.Visible
            : Visibility.Collapsed;
        ResetDraftButton.IsEnabled =
            !_isBusy && _loadedCard is { IsActive: false };
    }

    private void ShowNotice(string message)
    {
        WorkspaceNoticeText.Text = message;
        WorkspaceNotice.Visibility = Visibility.Visible;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var card in _cards)
        {
            if (card.Sandbox is not null)
            {
                card.Sandbox.Changed -= SandboxChanged;
            }
        }

        _manager.Dispose();
    }
}
