using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Shackles.App.Models;
using Shackles.ExperimentalSandboxes;

namespace Shackles.App.Views;

public sealed partial class ExperimentalSandboxWorkspaceView : UserControl, IDisposable
{
    private readonly ObservableCollection<ExperimentalSandboxCard> _cards = [];
    private readonly ExperimentalSandboxManager _manager;
    private ExperimentalSandboxCard? _loadedCard;
    private bool _isReady;
    private bool _isBusy;
    private bool _hasPreparedInitialDisplay;
    private bool _disposed;

    public ExperimentalSandboxWorkspaceView()
    {
        InitializeComponent();
        SandboxList.ItemsSource = _cards;
        _manager = new ExperimentalSandboxManager();
        _isReady = true;
        UpdateSupportDisplay(_manager.Support);
        UpdateEmptyStates();
    }

    public bool IsBusy => _isBusy;

    public int TrackedLaunchCount => _cards
        .Where(card => card.Sandbox is not null)
        .Sum(card => card.Sandbox!.GetSnapshot().ProcessIds.Count);

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
        _ = Dispatcher.BeginInvoke(() =>
        {
            var support = _manager.RefreshSupport();
            UpdateSupportDisplay(support);
            if (!support.IsAvailable)
            {
                ShowNotice(
                    "You can design policies here, but Windows does not currently " +
                    "advertise experimental process sandbox creation.");
            }
        });
    }

    private void NewSandbox_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy || !_manager.Support.IsAvailable)
        {
            return;
        }

        var existingDraft = _cards.FirstOrDefault(card => !card.IsActive);
        if (existingDraft is not null)
        {
            SandboxList.SelectedItem = existingDraft;
            ShowNotice(
                "The existing draft is selected. Finish or discard it before " +
                "starting another draft.");
            return;
        }

        var card = new ExperimentalSandboxCard(GetNextSandboxName());
        _cards.Add(card);
        SandboxList.SelectedItem = card;
        UpdateEmptyStates();
        ShowNotice(
            "Draft started. No Windows profile, native policy, or process exists yet.");
    }

    private string GetNextSandboxName()
    {
        var index = 1;
        var names = _cards
            .Select(card => card.DisplayName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        while (names.Contains($"Process sandbox {index}"))
        {
            index++;
        }

        return $"Process sandbox {index}";
    }

    private void SandboxList_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_isReady)
        {
            LoadSelectedCard();
        }
    }

    private void LoadSelectedCard()
    {
        _loadedCard = SandboxList.SelectedItem as ExperimentalSandboxCard;
        UpdateEmptyStates();
        if (_loadedCard is null)
        {
            return;
        }

        _isReady = false;
        try
        {
            _loadedCard.Refresh();
            SyncEffectiveRulesToDraft(_loadedCard);
            var draft = _loadedCard.Draft;
            SandboxNameTextBox.Text = draft.Name;
            UseAppContainerCheckBox.IsChecked = draft.UseAppContainer;
            IntegrityComboBox.SelectedIndex = draft.IntegrityIndex;
            LeastPrivilegeCheckBox.IsChecked = draft.LeastPrivilege;
            DisallowWin32kCheckBox.IsChecked = draft.DisallowWin32k;
            BlockExternalHandlesCheckBox.IsChecked = draft.BlockExternalHandles;
            BlockClipboardReadCheckBox.IsChecked = draft.BlockClipboardRead;
            BlockClipboardWriteCheckBox.IsChecked = draft.BlockClipboardWrite;
            BlockSystemParametersCheckBox.IsChecked = draft.BlockSystemParameters;
            BlockDisplaySettingsCheckBox.IsChecked = draft.BlockDisplaySettings;
            BlockGlobalAtomsCheckBox.IsChecked = draft.BlockGlobalAtoms;
            BlockDesktopCheckBox.IsChecked = draft.BlockDesktop;
            BlockExitWindowsCheckBox.IsChecked = draft.BlockExitWindows;
            BlockImeCheckBox.IsChecked = draft.BlockIme;
            BlockInputInjectionCheckBox.IsChecked = draft.BlockInputInjection;
            NetworkModeComboBox.SelectedIndex = draft.NetworkModeIndex;
            ProxyUrlTextBox.Text = draft.ProxyUrl;
            InternetClientCheckBox.IsChecked = draft.InternetClient;
            InternetClientServerCheckBox.IsChecked = draft.InternetClientServer;
            PrivateNetworkCheckBox.IsChecked = draft.PrivateNetwork;
            RegistryReadCheckBox.IsChecked = draft.RegistryRead;
            CustomCapabilitiesTextBox.Text = draft.CustomCapabilities;
            EnvironmentModeComboBox.SelectedIndex = draft.UseMinimalEnvironment ? 1 : 0;
            ExecutablePathTextBox.Text = draft.ExecutablePath;
            ArgumentsTextBox.Text = draft.Arguments;
            WorkingDirectoryTextBox.Text = draft.WorkingDirectory;
            IncludeTargetAccessCheckBox.IsChecked = draft.IncludeTargetAccess;
            IncludeWorkingDirectoryWriteAccessCheckBox.IsChecked =
                draft.IncludeWorkingDirectoryWriteAccess;
            FileSystemRuleList.ItemsSource = draft.FileRules;
            FileSystemPathTextBox.Clear();
            FileSystemAccessComboBox.SelectedIndex = 0;
        }
        finally
        {
            _isReady = true;
        }

        var active = _loadedCard.IsActive;
        PolicyEditor.IsEnabled = !active;
        EnvironmentModeComboBox.IsEnabled = !active;
        ResetDraftButton.IsEnabled = !active && !_isBusy;
        CreateAndLaunchButton.Content = active
            ? "_Launch another process"
            : "_Create sandbox and launch";
        CloseSandboxButton.Content = active ? "_Close sandbox" : "_Discard draft";
        UpdateAppContainerDependentControls();
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
        draft.IncludeWorkingDirectoryWriteAccess =
            IncludeWorkingDirectoryWriteAccessCheckBox.IsChecked == true;
        if (_loadedCard.IsActive)
        {
            return;
        }

        draft.Name = SandboxNameTextBox.Text;
        draft.UseAppContainer = UseAppContainerCheckBox.IsChecked == true;
        draft.IntegrityIndex = Math.Max(0, IntegrityComboBox.SelectedIndex);
        draft.LeastPrivilege = LeastPrivilegeCheckBox.IsChecked == true;
        draft.DisallowWin32k = DisallowWin32kCheckBox.IsChecked == true;
        draft.BlockExternalHandles = BlockExternalHandlesCheckBox.IsChecked == true;
        draft.BlockClipboardRead = BlockClipboardReadCheckBox.IsChecked == true;
        draft.BlockClipboardWrite = BlockClipboardWriteCheckBox.IsChecked == true;
        draft.BlockSystemParameters = BlockSystemParametersCheckBox.IsChecked == true;
        draft.BlockDisplaySettings = BlockDisplaySettingsCheckBox.IsChecked == true;
        draft.BlockGlobalAtoms = BlockGlobalAtomsCheckBox.IsChecked == true;
        draft.BlockDesktop = BlockDesktopCheckBox.IsChecked == true;
        draft.BlockExitWindows = BlockExitWindowsCheckBox.IsChecked == true;
        draft.BlockIme = BlockImeCheckBox.IsChecked == true;
        draft.BlockInputInjection = BlockInputInjectionCheckBox.IsChecked == true;
        draft.NetworkModeIndex = Math.Max(0, NetworkModeComboBox.SelectedIndex);
        draft.ProxyUrl = ProxyUrlTextBox.Text.Trim();
        draft.InternetClient = InternetClientCheckBox.IsChecked == true;
        draft.InternetClientServer = InternetClientServerCheckBox.IsChecked == true;
        draft.PrivateNetwork = PrivateNetworkCheckBox.IsChecked == true;
        draft.RegistryRead = RegistryReadCheckBox.IsChecked == true;
        draft.CustomCapabilities = CustomCapabilitiesTextBox.Text;
        draft.UseMinimalEnvironment = EnvironmentModeComboBox.SelectedIndex == 1;
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
            SyncEffectiveRulesToDraft(_loadedCard);
        }

        var draft = _loadedCard.Draft;
        var snapshot = _loadedCard.Snapshot;
        NameSummaryText.Text = _loadedCard.DisplayName;
        IdentitySummaryText.Text = snapshot?.AppContainerSid ??
            (draft.UseAppContainer
                ? "Allocated with first launch"
                : "No AppContainer SID");
        IsolationSummaryText.Text = draft.UseAppContainer
            ? draft.LeastPrivilege
                ? "AppContainer + least privilege"
                : "AppContainer"
            : "Restricted process without AppContainer";
        IntegritySummaryText.Text = draft.UseAppContainer
            ? "System default (Low)"
            : IntegrityText(draft.IntegrityIndex);

        var implicitRules = 0;
        if (!_loadedCard.IsActive && draft.UseAppContainer)
        {
            implicitRules += draft.IncludeTargetAccess &&
                             !string.IsNullOrWhiteSpace(draft.ExecutablePath)
                ? 1
                : 0;
            implicitRules += draft.IncludeWorkingDirectoryWriteAccess &&
                             !string.IsNullOrWhiteSpace(draft.WorkingDirectory)
                ? 1
                : 0;
        }

        var ruleCount = snapshot?.Options.FileSystemRules.Count ??
            draft.FileRules.Count + implicitRules;
        FileSystemSummaryText.Text = draft.UseAppContainer
            ? $"{ruleCount} native path rule{(ruleCount == 1 ? string.Empty : "s")}"
            : "Unavailable without AppContainer";
        var capabilityCount = BuildCapabilityNames(draft).Length;
        NetworkSummaryText.Text = draft.UseAppContainer
            ? $"{NetworkText(draft.NetworkModeIndex)} • " +
              (capabilityCount == 1
                  ? "1 capability"
                  : $"{capabilityCount} capabilities")
            : "No AppContainer network policy";
        var uiCount = CountUiRestrictions(draft);
        UiSummaryText.Text = draft.DisallowWin32k
            ? "Win32k disabled"
            : uiCount == 0
                ? "No UI limits"
                : $"{uiCount} UI limit{(uiCount == 1 ? string.Empty : "s")}";
        var processIds = snapshot?.ProcessIds ?? Array.Empty<int>();
        MemberCountSummaryText.Text = processIds.Count.ToString(
            System.Globalization.CultureInfo.CurrentCulture);
        MemberProcessList.ItemsSource = processIds.Select(processId => $"PID {processId}").ToArray();
        LifetimeSummaryText.Text = _loadedCard.IsActive
            ? "Identity and Windows profile remain until this card closes or Shackles exits. " +
              "Every launch receives a separate OS-managed Job Object."
            : "Nothing exists in Windows until the first successful launch.";
        DenySupportText.Text = DenySupportDescription(_manager.Support);
        UpdateActionState();
    }

    private void PreviewOption_Changed(object sender, RoutedEventArgs e) =>
        RefreshPreview();

    private void PreviewSelection_Changed(
        object sender,
        SelectionChangedEventArgs e) =>
        RefreshPreview();

    private void PreviewText_Changed(object sender, TextChangedEventArgs e) =>
        RefreshPreview();

    private void AppContainerOption_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isReady || _loadedCard?.IsActive == true)
        {
            return;
        }

        if (UseAppContainerCheckBox.IsChecked == true)
        {
            IntegrityComboBox.SelectedIndex = 0;
        }
        else
        {
            LeastPrivilegeCheckBox.IsChecked = false;
            NetworkModeComboBox.SelectedIndex = 0;
            ProxyUrlTextBox.Clear();
            InternetClientCheckBox.IsChecked = false;
            InternetClientServerCheckBox.IsChecked = false;
            PrivateNetworkCheckBox.IsChecked = false;
            RegistryReadCheckBox.IsChecked = false;
            CustomCapabilitiesTextBox.Clear();
            _loadedCard?.Draft.FileRules.Clear();
        }

        UpdateAppContainerDependentControls();
        RefreshPreview();
    }

    private void NetworkMode_Changed(
        object sender,
        SelectionChangedEventArgs e)
    {
        UpdateAppContainerDependentControls();
        RefreshPreview();
    }

    private void UpdateAppContainerDependentControls()
    {
        var enabled = UseAppContainerCheckBox.IsChecked == true &&
                      _loadedCard?.IsActive != true;
        IntegrityComboBox.IsEnabled = !enabled && _loadedCard?.IsActive != true;
        LeastPrivilegeCheckBox.IsEnabled = enabled;
        FileSystemPolicyPanel.IsEnabled = enabled;
        NetworkModeComboBox.IsEnabled = enabled;
        ProxyUrlTextBox.IsEnabled = enabled && NetworkModeComboBox.SelectedIndex == 2;
        InternetClientCheckBox.IsEnabled = enabled;
        InternetClientServerCheckBox.IsEnabled = enabled;
        PrivateNetworkCheckBox.IsEnabled = enabled;
        RegistryReadCheckBox.IsEnabled = enabled;
        CustomCapabilitiesTextBox.IsEnabled = enabled;
        IncludeTargetAccessCheckBox.IsEnabled =
            UseAppContainerCheckBox.IsChecked == true;
        IncludeWorkingDirectoryWriteAccessCheckBox.IsEnabled =
            UseAppContainerCheckBox.IsChecked == true;
    }

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
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            ExecutablePathTextBox.Text = dialog.FileName;
            ShowNotice(
                _loadedCard?.IsActive == true
                    ? "The target is ready. Its directory will be added to the " +
                      "card's native read-only policy when launched."
                    : "The target is part of this draft. Nothing has been launched.");
        }
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

    private void BrowseFolderRule_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedCard is null || _loadedCard.IsActive)
        {
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "Choose a directory for the native sandbox policy",
            Multiselect = false
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            FileSystemPathTextBox.Text = dialog.FolderName;
            FileSystemPathTextBox.Focus();
        }
    }

    private void AddFileSystemRule_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedCard is null || _loadedCard.IsActive)
        {
            return;
        }

        var path = FileSystemPathTextBox.Text.Trim();
        if (path.Length == 0)
        {
            ShowNotice("Enter an existing directory path.");
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
            ShowNotice($"The path is invalid: {exception.Message}");
            return;
        }

        if (!Directory.Exists(fullPath))
        {
            ShowNotice("The directory does not exist.");
            return;
        }

        var existing = _loadedCard.Draft.FileRules.FirstOrDefault(rule =>
            string.Equals(rule.Path, fullPath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.AccessIndex = Math.Max(0, FileSystemAccessComboBox.SelectedIndex);
            FileSystemRuleList.Items.Refresh();
            ShowNotice("The existing rule was updated.");
        }
        else
        {
            _loadedCard.Draft.FileRules.Add(
                new ExperimentalSandboxFileRuleDraft(
                    fullPath,
                    Math.Max(0, FileSystemAccessComboBox.SelectedIndex)));
            ShowNotice(
                "Native path rule added to the draft. The directory ACL was not changed.");
        }

        FileSystemPathTextBox.Clear();
        RefreshPreview();
    }

    private void RemoveFileSystemRule_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedCard is null ||
            _loadedCard.IsActive ||
            sender is not Button { Tag: ExperimentalSandboxFileRuleDraft rule })
        {
            return;
        }

        _loadedCard.Draft.FileRules.Remove(rule);
        RefreshPreview();
    }

    private void ResetDraft_Click(object sender, RoutedEventArgs e)
    {
        if (_loadedCard is null || _loadedCard.IsActive || _isBusy)
        {
            return;
        }

        var name = string.IsNullOrWhiteSpace(_loadedCard.Draft.Name)
            ? GetNextSandboxName()
            : _loadedCard.Draft.Name;
        _loadedCard.Draft.Reset(name);
        LoadSelectedCard();
        ShowNotice("The sandbox draft has been reset. Nothing was created.");
    }

    private async void RefreshSupport_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var support = await Task.Run(_manager.RefreshSupport).ConfigureAwait(true);
            UpdateSupportDisplay(support);
            ShowNotice(support.Summary);
        }
        finally
        {
            SetBusy(false);
        }
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
            ExperimentalSandboxLaunchResult launch;
            if (card.Sandbox is null)
            {
                var creation = await Task.Run(() =>
                    _manager.CreateAndLaunch(
                        BuildSandboxOptions(card.Draft),
                        BuildLaunchOptions(card.Draft))).ConfigureAwait(true);
                launch = creation.FirstLaunch;
                card.Attach(creation.Sandbox);
                creation.Sandbox.Changed += SandboxChanged;
            }
            else
            {
                launch = await Task.Run(() =>
                    card.Sandbox.Launch(BuildLaunchOptions(card.Draft)))
                    .ConfigureAwait(true);
                card.Refresh();
            }

            SyncEffectiveRulesToDraft(card);
            if (_cards.Contains(card))
            {
                SandboxList.SelectedItem = card;
                LoadSelectedCard();
                ShowLaunchResult(
                    launch,
                    $"Launched PID {launch.ProcessId} with identity " +
                    $"{card.Sandbox!.Identity}.");
            }
        }
        catch (Exception exception)
        {
            ShowNotice(
                card.IsActive
                    ? "Launch failed. The existing sandbox card remains available."
                    : "Launch failed. The draft remains editable; any profile created " +
                      "during the attempt was cleaned up.");
            MessageBox.Show(
                Window.GetWindow(this),
                exception.Message,
                card.IsActive
                    ? "Could not launch in experimental sandbox"
                    : "Could not create experimental sandbox",
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
        var question = trackedCount == 0
            ? $"Close '{card.DisplayName}' and delete its Windows profile?"
            : $"Close '{card.DisplayName}'? This terminates {trackedCount} directly " +
              $"launched process{(trackedCount == 1 ? string.Empty : "es")} and " +
              "deletes its Windows profile. The API does not expose the internal " +
              "Job Objects, so descendant lifetime cannot be inspected here.";
        var answer = MessageBox.Show(
            Window.GetWindow(this),
            question,
            "Close experimental sandbox",
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
            if (_cards.Contains(card))
            {
                RemoveCard(card);
            }

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

    private static ExperimentalSandboxOptions BuildSandboxOptions(
        ExperimentalSandboxDraft draft)
    {
        var useAppContainer = draft.UseAppContainer;
        return new ExperimentalSandboxOptions
        {
            DisplayName = draft.Name.Trim(),
            UseAppContainer = useAppContainer,
            IntegrityLevel = IntegrityLevel(draft.IntegrityIndex),
            LeastPrivilege = useAppContainer && draft.LeastPrivilege,
            DisallowWin32kSystemCalls = draft.DisallowWin32k,
            UiRestrictions = BuildUiRestrictions(draft),
            NetworkMode = useAppContainer
                ? NetworkMode(draft.NetworkModeIndex)
                : ExperimentalSandboxNetworkMode.Blocked,
            ProxyUrl = useAppContainer && draft.NetworkModeIndex == 2
                ? draft.ProxyUrl
                : null,
            UseMinimalEnvironment = draft.UseMinimalEnvironment,
            CapabilityNames = useAppContainer
                ? BuildCapabilityNames(draft)
                : Array.Empty<string>(),
            FileSystemRules = useAppContainer
                ? draft.FileRules.Select(rule => new ExperimentalSandboxFileRule(
                    rule.Path,
                    FileAccess(rule.AccessIndex))).ToArray()
                : Array.Empty<ExperimentalSandboxFileRule>()
        };
    }

    private static ExperimentalSandboxLaunchOptions BuildLaunchOptions(
        ExperimentalSandboxDraft draft) =>
        new(draft.ExecutablePath)
        {
            Arguments = draft.Arguments,
            WorkingDirectory = string.IsNullOrWhiteSpace(draft.WorkingDirectory)
                ? null
                : draft.WorkingDirectory,
            IncludeTargetDirectoryReadAccess =
                draft.UseAppContainer && draft.IncludeTargetAccess,
            IncludeWorkingDirectoryWriteAccess =
                draft.UseAppContainer && draft.IncludeWorkingDirectoryWriteAccess
        };

    private bool ValidateLaunchDraft(ExperimentalSandboxDraft draft)
    {
        var support = _manager.Support;
        if (!support.IsAvailable)
        {
            ShowNotice(support.Summary);
            return false;
        }

        if (_loadedCard?.IsActive != true && string.IsNullOrWhiteSpace(draft.Name))
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

        if (draft.UseAppContainer &&
            draft.NetworkModeIndex == 2 &&
            (!Uri.TryCreate(draft.ProxyUrl, UriKind.Absolute, out var proxy) ||
             proxy.Scheme is not ("http" or "https")))
        {
            ShowNotice("Proxy mode requires an absolute HTTP or HTTPS proxy URL.");
            ProxyUrlTextBox.Focus();
            return false;
        }

        if (draft.FileRules.Any(rule => rule.AccessIndex == 2) &&
            support.FileSystemDenySupportKnown &&
            !support.SupportsFileSystemDeny)
        {
            ShowNotice("This Windows build does not advertise filesystem deny rules.");
            return false;
        }

        return true;
    }

    private static string[] BuildCapabilityNames(
        ExperimentalSandboxDraft draft)
    {
        var capabilities = new List<string>();
        if (draft.InternetClient)
        {
            capabilities.Add("internetClient");
        }

        if (draft.InternetClientServer)
        {
            capabilities.Add("internetClientServer");
        }

        if (draft.PrivateNetwork)
        {
            capabilities.Add("privateNetworkClientServer");
        }

        if (draft.RegistryRead)
        {
            capabilities.Add("registryRead");
        }

        capabilities.AddRange(draft.CustomCapabilities
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return capabilities
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static ExperimentalSandboxUiRestrictions BuildUiRestrictions(
        ExperimentalSandboxDraft draft)
    {
        var restrictions = ExperimentalSandboxUiRestrictions.None;
        AddUiRestriction(draft.BlockExternalHandles, ExperimentalSandboxUiRestrictions.ExternalHandles);
        AddUiRestriction(draft.BlockClipboardRead, ExperimentalSandboxUiRestrictions.ReadClipboard);
        AddUiRestriction(draft.BlockClipboardWrite, ExperimentalSandboxUiRestrictions.WriteClipboard);
        AddUiRestriction(draft.BlockSystemParameters, ExperimentalSandboxUiRestrictions.SystemParameters);
        AddUiRestriction(draft.BlockDisplaySettings, ExperimentalSandboxUiRestrictions.DisplaySettings);
        AddUiRestriction(draft.BlockGlobalAtoms, ExperimentalSandboxUiRestrictions.GlobalAtoms);
        AddUiRestriction(draft.BlockDesktop, ExperimentalSandboxUiRestrictions.Desktop);
        AddUiRestriction(draft.BlockExitWindows, ExperimentalSandboxUiRestrictions.ExitWindows);
        AddUiRestriction(draft.BlockIme, ExperimentalSandboxUiRestrictions.InputMethodEditor);
        AddUiRestriction(draft.BlockInputInjection, ExperimentalSandboxUiRestrictions.InputInjection);
        return restrictions;

        void AddUiRestriction(
            bool enabled,
            ExperimentalSandboxUiRestrictions restriction)
        {
            if (enabled)
            {
                restrictions |= restriction;
            }
        }
    }

    private void SandboxChanged(
        object? sender,
        ExperimentalSandboxChangedEventArgs eventArgs)
    {
        if (_disposed || sender is not ExperimentalSandbox sandbox)
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
                if (_cards.Contains(card))
                {
                    RemoveCard(card);
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
        ExperimentalSandboxLaunchResult launch,
        string successMessage)
    {
        var message = successMessage;
        if (launch.Warnings.Count > 0)
        {
            message += " " + string.Join(" ", launch.Warnings);
        }

        ShowNotice(message);
    }

    private static void SyncEffectiveRulesToDraft(ExperimentalSandboxCard card)
    {
        var snapshot = card.Snapshot;
        if (snapshot is null)
        {
            return;
        }

        card.Draft.Name = snapshot.DisplayName;
        card.Draft.FileRules.Clear();
        foreach (var rule in snapshot.Options.FileSystemRules)
        {
            card.Draft.FileRules.Add(new ExperimentalSandboxFileRuleDraft(
                rule.Path,
                rule.Access switch
                {
                    ExperimentalSandboxFileAccess.ReadOnly => 1,
                    ExperimentalSandboxFileAccess.Deny => 2,
                    _ => 0
                }));
        }
    }

    private void RemoveCard(ExperimentalSandboxCard card)
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

    private void UpdateSupportDisplay(ExperimentalSandboxSupport support)
    {
        var knownFeaturesEnabled = support.RequiredFeatures.Count > 0 &&
            support.RequiredFeatures.All(feature =>
                feature.ConfigurationState ==
                ExperimentalFeatureConfigurationState.Enabled);
        SupportStateText.Text = support.Availability switch
        {
            ExperimentalSandboxAvailability.Available => "Ready on this Windows build",
            ExperimentalSandboxAvailability.FeatureDisabled => knownFeaturesEnabled
                ? "Configured, but unavailable"
                : "Installed, but unavailable",
            ExperimentalSandboxAvailability.EntryPointMissing => "API is not installed",
            ExperimentalSandboxAvailability.LibraryMissing => "Process model is unavailable",
            ExperimentalSandboxAvailability.PlatformNotSupported => "Windows is required",
            _ => "Support could not be determined"
        };

        var processModel = string.IsNullOrWhiteSpace(support.ProcessModelVersion)
            ? "unknown processmodel.dll version"
            : $"processmodel.dll {support.ProcessModelVersion}";
        var probe = support.QueryExportPresent &&
                    support.CapabilityMask is { } capabilityMask
            ? support.IsAvailable
                ? $"Windows support query: capability mask 0x{capabilityMask:X}."
                : "Windows support query returned capability mask " +
                  $"0x{capabilityMask:X}; process creation is not advertised."
            : support.ProbeErrorCode is { } error
                ? $"Runtime probe: {error} " +
                  $"({new System.ComponentModel.Win32Exception(error).Message})."
                : support.IsAvailable
                    ? "Runtime probe succeeded."
                    : support.Summary;
        SupportDetailText.Text =
            $"Windows {support.OsVersion} • {processModel}. {probe}";
        FeatureStateText.Text = support.RequiredFeatures.Count == 0
            ? "Feature Store state is unavailable."
            : "Feature Store: " + string.Join(
                "; ",
                support.RequiredFeatures.Select(feature =>
                    $"{feature.Id} {ShortFeatureName(feature.Name)} — " +
                    FeatureStateDescription(
                        feature.ConfigurationState,
                        feature.Priority))) + ".";

        EmptySandboxListHint.Text = support.IsAvailable
            ? "Choose New sandbox to begin a policy draft."
            : "Windows does not currently advertise experimental process " +
              "sandbox creation.";
        var unavailableTip = support.IsAvailable ? null : support.Summary;
        NewSandboxButton.ToolTip = unavailableTip;
        EmptyNewSandboxButton.ToolTip = unavailableTip;

        if (FileSystemAccessComboBox.Items.Count > 2 &&
            FileSystemAccessComboBox.Items[2] is ComboBoxItem denyItem)
        {
            denyItem.IsEnabled = !support.FileSystemDenySupportKnown ||
                                 support.SupportsFileSystemDeny;
        }

        DenySupportText.Text = DenySupportDescription(support);
        UpdateActionState();
    }

    private static string FeatureStateDescription(
        ExperimentalFeatureConfigurationState state,
        uint? priority)
    {
        var source = priority switch
        {
            0 => "image default",
            1 => "servicing",
            2 => "safeguard",
            3 => "edition default",
            4 => "service override",
            6 => "dynamic override",
            8 => "user override",
            9 => "security override",
            10 => "user policy",
            12 => "test override",
            15 => "image override",
            _ => null
        };
        var value = state switch
        {
            ExperimentalFeatureConfigurationState.Enabled => "enabled",
            ExperimentalFeatureConfigurationState.Disabled => "disabled",
            ExperimentalFeatureConfigurationState.Default => "default",
            _ => "unknown"
        };
        return source is null ? value : $"{value} ({source})";
    }

    private static string ShortFeatureName(string name) =>
        name.EndsWith("core", StringComparison.OrdinalIgnoreCase)
            ? "core"
            : "specification";

    private static string DenySupportDescription(
        ExperimentalSandboxSupport support)
    {
        if (!support.FileSystemDenySupportKnown)
        {
            return "This build has no support-query export, so deny-rule support " +
                   "cannot be confirmed before launch.";
        }

        return support.SupportsFileSystemDeny
            ? "Windows reports support for native deny rules."
            : "Windows does not advertise native deny rules on this build.";
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        OperationProgress.Visibility = busy
            ? Visibility.Visible
            : Visibility.Collapsed;
        SandboxList.IsEnabled = !busy;
        UpdateActionState();
    }

    private void UpdateActionState()
    {
        var hasSelection = _loadedCard is not null;
        var canCreate = !_isBusy && _manager.Support.IsAvailable;
        NewSandboxButton.IsEnabled = canCreate;
        EmptyNewSandboxButton.IsEnabled = canCreate;
        CreateAndLaunchButton.IsEnabled =
            !_isBusy && hasSelection && _manager.Support.IsAvailable;
        CloseSandboxButton.IsEnabled = !_isBusy && hasSelection;
        ResetDraftButton.IsEnabled =
            !_isBusy && _loadedCard is { IsActive: false };
    }

    private void UpdateEmptyStates()
    {
        var hasSelection = SandboxList.SelectedItem is ExperimentalSandboxCard;
        EmptySandboxListHint.Visibility = _cards.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        EmptyEditorState.Visibility = hasSelection
            ? Visibility.Collapsed
            : Visibility.Visible;
        SandboxEditorHost.Visibility = hasSelection
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateActionState();
    }

    private static ExperimentalSandboxIntegrityLevel IntegrityLevel(int index) =>
        index switch
        {
            1 => ExperimentalSandboxIntegrityLevel.Inherit,
            2 => ExperimentalSandboxIntegrityLevel.Untrusted,
            3 => ExperimentalSandboxIntegrityLevel.Low,
            4 => ExperimentalSandboxIntegrityLevel.Medium,
            5 => ExperimentalSandboxIntegrityLevel.High,
            _ => ExperimentalSandboxIntegrityLevel.SystemDefault
        };

    private static string IntegrityText(int index) =>
        index switch
        {
            1 => "Inherit",
            2 => "Untrusted",
            3 => "Low",
            4 => "Medium",
            5 => "High",
            _ => "System default"
        };

    private static ExperimentalSandboxNetworkMode NetworkMode(int index) =>
        index switch
        {
            1 => ExperimentalSandboxNetworkMode.Allowed,
            2 => ExperimentalSandboxNetworkMode.Proxy,
            _ => ExperimentalSandboxNetworkMode.Blocked
        };

    private static string NetworkText(int index) =>
        index switch
        {
            1 => "Direct network allowed",
            2 => "Proxy network",
            _ => "Direct network blocked"
        };

    private static ExperimentalSandboxFileAccess FileAccess(int index) =>
        index switch
        {
            1 => ExperimentalSandboxFileAccess.ReadOnly,
            2 => ExperimentalSandboxFileAccess.Deny,
            _ => ExperimentalSandboxFileAccess.ReadWrite
        };

    private static int CountUiRestrictions(ExperimentalSandboxDraft draft) =>
        new[]
        {
            draft.BlockExternalHandles,
            draft.BlockClipboardRead,
            draft.BlockClipboardWrite,
            draft.BlockSystemParameters,
            draft.BlockDisplaySettings,
            draft.BlockGlobalAtoms,
            draft.BlockDesktop,
            draft.BlockExitWindows,
            draft.BlockIme,
            draft.BlockInputInjection
        }.Count(enabled => enabled);

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
