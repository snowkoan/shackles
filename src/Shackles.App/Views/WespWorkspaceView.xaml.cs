using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using Shackles.App.Dialogs;
using Shackles.App.Models;
using Shackles.App.ViewModels;
using Shackles.Wesp;

namespace Shackles.App.Views;

public sealed partial class WespWorkspaceView : UserControl, IDisposable
{
    private const int BlockedAccessIndex = 0;
    private const int ReadOnlyAccessIndex = 1;
    private const int ErrorCancelled = 1223;

    private readonly ObservableCollection<WespResourceRuleDraft> _fileRules = [];
    private readonly ObservableCollection<WespResourceRuleDraft> _registryRules = [];
    private readonly ObservableCollection<string> _blockedChildExecutables = [];
    private readonly DispatcherTimer _sessionRefreshTimer;
    private IReadOnlyList<SessionActivityRow> _activityRows = [];
    private ActivityLogContext? _activityLogContext;
    private WespSupportInfo? _support;
    private WespSession? _session;
    private int _runningSessionProcessCount;
    private readonly bool _hasRequiredIntegrity;
    private readonly string? _integrityCheckFailure;
    private bool _isBusy;
    private bool _prepared;
    private bool _disposed;

    public WespWorkspaceView()
    {
        InitializeComponent();

        try
        {
            _hasRequiredIntegrity = WespSupport.IsCurrentProcessHighIntegrity();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _hasRequiredIntegrity = false;
            _integrityCheckFailure = exception.Message;
        }

        FileRuleList.ItemsSource = _fileRules;
        RegistryRuleList.ItemsSource = _registryRules;
        BlockedChildExecutableList.ItemsSource = _blockedChildExecutables;

        _sessionRefreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _sessionRefreshTimer.Tick += SessionRefreshTimer_Tick;

        ConfigureIntegrityGate();
        UpdateSummary();
        UpdateActionState();
    }

    public bool IsBusy => _isBusy;

    public bool HasActiveSession => _session is not null;

    public int TrackedLaunchCount
    {
        get
        {
            if (_session is null)
            {
                return 0;
            }

            try
            {
                return _session.GetProcesses().Count(process =>
                    process.IsRunning && process.Origin == WespProcessOrigin.Launched);
            }
            catch
            {
                // Shutdown warnings are best effort; session operations report their own errors.
                return 0;
            }
        }
    }

    public void PrepareForDisplay()
    {
        if (_disposed)
        {
            return;
        }

        if (!_hasRequiredIntegrity)
        {
            _prepared = true;
            return;
        }

        if (!_isBusy)
        {
            RefreshSessionDetails(showProcessNotice: false);
        }

        if (_prepared)
        {
            return;
        }

        _prepared = true;
        _ = RefreshSupportAsync();
    }

    private async void RefreshSupport_Click(object sender, RoutedEventArgs e) =>
        await RefreshSupportAsync().ConfigureAwait(true);

    private async Task RefreshSupportAsync()
    {
        if (_disposed || _isBusy || !_hasRequiredIntegrity)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var support = await Task.Run(WespSupport.Probe).ConfigureAwait(true);
            if (_disposed)
            {
                return;
            }

            _support = support;
            SupportStateText.Text = support.IsAvailable
                ? "WESP client is available"
                : "WESP Blocking is unavailable";
            SupportDetailText.Text = support.IsAvailable
                ? "Client API found. Driver and rule support are checked when you start a session."
                : support.Summary;
            ShowNotice(
                support.IsAvailable
                    ? "The WESP client API was found. Driver compatibility and rule capabilities will be checked when the session starts. You can launch a new process or choose one that is already running."
                    : support.Summary);
        }
        catch (Exception exception)
        {
            if (_disposed)
            {
                return;
            }

            _support = null;
            SupportStateText.Text = "WESP support could not be checked";
            SupportDetailText.Text = exception.Message;
            ShowNotice($"WESP support could not be checked: {exception.Message}");
        }
        finally
        {
            if (!_disposed)
            {
                SetBusy(false);
            }
        }
    }

    private void ConfigureIntegrityGate()
    {
        InteractiveWorkspace.Visibility = _hasRequiredIntegrity
            ? Visibility.Visible
            : Visibility.Collapsed;
        ElevationGate.Visibility = _hasRequiredIntegrity
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (_hasRequiredIntegrity)
        {
            return;
        }

        RefreshSupportButton.Visibility = Visibility.Collapsed;
        SupportStateText.Text = _integrityCheckFailure is null
            ? "Administrator access required"
            : "Administrator access could not be verified";
        SupportDetailText.Text = _integrityCheckFailure is null
            ? "Open a high-integrity Shackles window before configuring WESP Blocking."
            : "The WESP workspace is locked because Shackles could not verify this process's integrity level.";

        if (_integrityCheckFailure is not null)
        {
            ElevationLaunchStatusText.Text =
                $"Integrity check failed: {_integrityCheckFailure}";
            ElevationLaunchStatusText.Visibility = Visibility.Visible;
        }
    }

    private void OpenElevatedWesp_Click(object sender, RoutedEventArgs e)
    {
        if (_hasRequiredIntegrity)
        {
            return;
        }

        try
        {
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                throw new InvalidOperationException(
                    "Windows could not determine the Shackles application path.");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = Environment.CurrentDirectory,
                UseShellExecute = true,
                Verb = "runas"
            };

            if (string.Equals(
                    Path.GetFileNameWithoutExtension(executablePath),
                    "dotnet",
                    StringComparison.OrdinalIgnoreCase))
            {
                var applicationAssemblyPath = typeof(App).Assembly.Location;
                if (string.IsNullOrWhiteSpace(applicationAssemblyPath))
                {
                    throw new InvalidOperationException(
                        "Windows could not determine the Shackles application assembly path.");
                }

                startInfo.ArgumentList.Add(applicationAssemblyPath);
            }

            startInfo.ArgumentList.Add(App.WespWorkspaceArgument);
            using var elevatedProcess = Process.Start(startInfo);
            if (elevatedProcess is null)
            {
                throw new InvalidOperationException(
                    "Windows did not start the administrator copy of Shackles.");
            }

            OpenElevatedWespButton.IsEnabled = false;
            ElevationLaunchStatusText.Text =
                "An administrator copy is opening directly on WESP Blocking. This window remains open.";
            ElevationLaunchStatusText.Visibility = Visibility.Visible;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorCancelled)
        {
            ElevationLaunchStatusText.Text =
                "The administrator prompt was canceled. You can try again when you are ready.";
            ElevationLaunchStatusText.Visibility = Visibility.Visible;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ElevationLaunchStatusText.Text =
                $"Shackles could not open an administrator copy: {exception.Message}";
            ElevationLaunchStatusText.Visibility = Visibility.Visible;
        }
    }

    private void BrowseFileRuleFolder_Click(object sender, RoutedEventArgs e) =>
        BrowseFolder(
            FileRulePathTextBox,
            "Choose a folder to block or make read-only");

    private void BrowseWorkingDirectory_Click(object sender, RoutedEventArgs e) =>
        BrowseFolder(
            WorkingDirectoryTextBox,
            "Choose the process working directory");

    private void BrowseBlockedChildExecutable_Click(
        object sender,
        RoutedEventArgs e) =>
        BrowseExecutable(
            BlockedChildExecutableTextBox,
            "Choose a child application to block");

    private void BrowseRootExecutable_Click(object sender, RoutedEventArgs e) =>
        BrowseExecutable(
            RootExecutableTextBox,
            "Choose the root application for WESP Blocking");

    private void BrowseFolder(TextBox target, string title)
    {
        if (_isBusy)
        {
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = title,
            Multiselect = false
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            target.Text = dialog.FolderName;
            target.Focus();
        }
    }

    private void BrowseExecutable(TextBox target, string title)
    {
        if (_isBusy)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            target.Text = dialog.FileName;
            target.Focus();
        }
    }

    private void AddFileRule_Click(object sender, RoutedEventArgs e)
    {
        if (!TryNormalizeExistingFolder(FileRulePathTextBox, out var path))
        {
            return;
        }

        var accessIndex = NormalizeAccessIndex(FileRuleAccessComboBox.SelectedIndex);
        var existing = FindRule(_fileRules, path);
        if (existing is not null)
        {
            if (existing.AccessIndex == accessIndex)
            {
                ShowNotice("That folder is already configured with this access.");
            }
            else
            {
                existing.AccessIndex = accessIndex;
                ShowNotice($"The folder is now {AccessText(accessIndex).ToLowerInvariant()}.");
            }
        }
        else
        {
            _fileRules.Add(new WespResourceRuleDraft(path, accessIndex, UpdateSummary));
            var overlapsBlockedRule = accessIndex == ReadOnlyAccessIndex &&
                                      _fileRules.Any(rule =>
                                          rule != _fileRules[^1] &&
                                          rule.AccessIndex == BlockedAccessIndex &&
                                          PathsOverlap(path, rule.Path));
            ShowNotice(
                overlapsBlockedRule
                    ? "The read-only folder overlaps a blocked folder. The blocked rule still wins where they overlap."
                    : accessIndex == BlockedAccessIndex
                        ? "Blocked folder added. WESP will deny configured reads and changes for it and its descendants."
                        : "Read-only folder added. WESP will allow reads and deny configured changes for it and its descendants.");
        }

        FileRulePathTextBox.Clear();
        UpdateSummary();
    }

    private void AddRegistryRule_Click(object sender, RoutedEventArgs e)
    {
        if (!TryNormalizeRegistryKey(RegistryRulePathTextBox, out var path))
        {
            return;
        }

        var accessIndex = NormalizeAccessIndex(RegistryRuleAccessComboBox.SelectedIndex);
        var existing = FindRule(_registryRules, path);
        if (existing is not null)
        {
            if (existing.AccessIndex == accessIndex)
            {
                ShowNotice("That registry key is already configured with this access.");
            }
            else
            {
                existing.AccessIndex = accessIndex;
                ShowNotice($"The registry key is now {AccessText(accessIndex).ToLowerInvariant()}.");
            }
        }
        else
        {
            _registryRules.Add(new WespResourceRuleDraft(path, accessIndex, UpdateSummary));
            ShowNotice(
                accessIndex == BlockedAccessIndex
                    ? "Blocked registry key added. WESP will deny configured reads and changes for it and its descendants."
                    : "Read-only registry key added. WESP will allow queries and deny configured changes for it and its descendants.");
        }

        RegistryRulePathTextBox.Clear();
        UpdateSummary();
    }

    private void AddBlockedChildExecutable_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!TryNormalizeChildExecutableName(
                BlockedChildExecutableTextBox,
                out var imageName))
        {
            return;
        }

        if (AddUniqueExecutableName(_blockedChildExecutables, imageName))
        {
            ShowNotice(
                $"{imageName} was added. It will be blocked by name regardless of folder in this process tree.");
        }
        else
        {
            ShowNotice($"{imageName} is already blocked by name.");
        }

        BlockedChildExecutableTextBox.Clear();
        UpdateSummary();
    }

    private void ResourceAccess_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e) => UpdateSummary();

    private void BlockUncPaths_Click(object sender, RoutedEventArgs e)
    {
        ShowNotice(BlockUncPathsCheckBox.IsChecked == true
            ? "UNC path blocking was added to this draft."
            : "UNC path blocking was removed from this draft.");
        UpdateSummary();
    }

    private void RemoveFileRule_Click(object sender, RoutedEventArgs e)
    {
        if (TryRemoveRule(sender, _fileRules))
        {
            ShowNotice("The folder was removed from the WESP Blocking draft.");
            UpdateSummary();
        }
    }

    private void RemoveRegistryRule_Click(object sender, RoutedEventArgs e)
    {
        if (TryRemoveRule(sender, _registryRules))
        {
            ShowNotice("The registry key was removed from the WESP Blocking draft.");
            UpdateSummary();
        }
    }

    private void RemoveBlockedChildExecutable_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (TryRemoveTaggedPath(sender, _blockedChildExecutables))
        {
            ShowNotice("The application was removed from the blocked list.");
            UpdateSummary();
        }
    }

    private bool TryRemoveRule(
        object sender,
        ObservableCollection<WespResourceRuleDraft> rules)
    {
        if (_session is not null || _isBusy ||
            sender is not Button { Tag: WespResourceRuleDraft rule })
        {
            return false;
        }

        return rules.Remove(rule);
    }

    private bool TryRemoveTaggedPath(
        object sender,
        ObservableCollection<string> paths)
    {
        if (_session is not null || _isBusy ||
            sender is not Button { Tag: string path })
        {
            return false;
        }

        return paths.Remove(path);
    }

    private void RootExecutableText_Changed(
        object sender,
        TextChangedEventArgs e)
    {
        UpdateSummary();
        UpdateActionState();
    }

    private async void PrimaryAction_Click(object sender, RoutedEventArgs e)
    {
        if (PrimaryActionUsesProcessPicker())
        {
            await ApplyExistingProcessesAsync().ConfigureAwait(true);
            return;
        }

        await LaunchAsync().ConfigureAwait(true);
    }

    private async Task LaunchAsync()
    {
        if (_disposed ||
            _isBusy ||
            (_session is null && _support?.IsAvailable != true) ||
            _session is { CanApply: false })
        {
            return;
        }

        if (!TryBuildLaunchOptions(
                out var executablePath,
                out var launchOptions))
        {
            return;
        }

        var existingSession = _session;
        var creatingNewSession = existingSession is null;
        var sessionWasCreated = false;
        var policy = BuildPolicy();

        SetBusy(true);
        try
        {
            WespLaunchResult launchResult;
            if (existingSession is null)
            {
                var createdSession = await Task.Run(
                    () => WespSession.Create(policy, executablePath))
                    .ConfigureAwait(true);
                _session = createdSession;
                sessionWasCreated = true;
                _runningSessionProcessCount = 0;
                _activityLogContext = ActivityLogContext.From(createdSession);
                SetActivityRows([]);
                ActivityCaptureStatusText.Text = createdSession.ActivityCaptureStatus;
                _sessionRefreshTimer.Start();
                RootExecutableTextBox.Text = executablePath;
                try
                {
                    launchResult = await Task.Run(
                        () => createdSession.Launch(launchOptions))
                        .ConfigureAwait(true);
                }
                catch (Exception launchException)
                {
                    Exception? cleanupException = null;
                    try
                    {
                        await Task.Run(() => CloseAndDispose(createdSession))
                            .ConfigureAwait(true);
                    }
                    catch (Exception exception)
                    {
                        cleanupException = exception;
                    }

                    CaptureActivitySnapshot(createdSession);
                    if (createdSession.IsClosed)
                    {
                        _session = null;
                        _sessionRefreshTimer.Stop();
                        ActivityCaptureStatusText.Text =
                            "Activity capture stopped with the closed blocking session.";
                    }

                    if (cleanupException is not null)
                    {
                        throw new AggregateException(
                            "The process launch failed and the new WESP Blocking session did not close cleanly.",
                            launchException,
                            cleanupException);
                    }

                    throw;
                }
            }
            else
            {
                launchResult = await Task.Run(
                    () => existingSession.Launch(launchOptions))
                    .ConfigureAwait(true);
            }

            RefreshSessionDetails(showProcessNotice: false);
            var warningText = launchResult.Warnings.Count == 0
                ? string.Empty
                : " " + string.Join(" ", launchResult.Warnings);
            ShowNotice(
                $"Launched PID {launchResult.ProcessId} with WESP Blocking active.{warningText}");
        }
        catch (Exception exception)
        {
            var sessionCreationFailed = creatingNewSession && !sessionWasCreated;
            var errorMessage = sessionCreationFailed
                ? WespStartupTroubleshooting.FormatStartupFailure(exception)
                : exception.Message;
            ShowNotice(
                sessionCreationFailed
                    ? "WESP Blocking could not be started. See the error details for things to check."
                    : $"The process could not be launched: {exception.Message}");
            MessageBox.Show(
                Window.GetWindow(this),
                errorMessage,
                sessionCreationFailed
                    ? "Could not start WESP Blocking"
                    : "Could not launch with WESP Blocking",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
            UpdateSummary();
        }
    }

    private WespPolicy BuildPolicy() => new(
        BlockedFilePaths: _fileRules
            .Where(rule => rule.AccessIndex == BlockedAccessIndex)
            .Select(rule => rule.Path)
            .ToArray(),
        ReadOnlyFilePaths: _fileRules
            .Where(rule => rule.AccessIndex == ReadOnlyAccessIndex)
            .Select(rule => rule.Path)
            .ToArray(),
        BlockedRegistryKeys: _registryRules
            .Where(rule => rule.AccessIndex == BlockedAccessIndex)
            .Select(rule => rule.Path)
            .ToArray(),
        ReadOnlyRegistryKeys: _registryRules
            .Where(rule => rule.AccessIndex == ReadOnlyAccessIndex)
            .Select(rule => rule.Path)
            .ToArray(),
        BlockedChildExecutables: _blockedChildExecutables.ToArray(),
        BlockUncPaths: BlockUncPathsCheckBox.IsChecked == true);

    private bool TryBuildLaunchOptions(
        out string executablePath,
        out WespLaunchOptions launchOptions)
    {
        executablePath = string.Empty;
        launchOptions = default!;

        if (!TryNormalizeExistingExecutable(
                RootExecutableTextBox,
                out executablePath))
        {
            return false;
        }

        var arguments = ArgumentsTextBox.Text;
        if (arguments.Contains('\0'))
        {
            ShowNotice("The process arguments contain an invalid character.");
            ArgumentsTextBox.Focus();
            return false;
        }

        string? workingDirectory = null;
        if (!string.IsNullOrWhiteSpace(WorkingDirectoryTextBox.Text))
        {
            try
            {
                workingDirectory = Path.GetFullPath(
                    WorkingDirectoryTextBox.Text.Trim());
            }
            catch (Exception exception)
            {
                ShowNotice(
                    $"The working directory is invalid: {exception.Message}");
                WorkingDirectoryTextBox.Focus();
                return false;
            }

            if (!Directory.Exists(workingDirectory))
            {
                ShowNotice("The working directory does not exist.");
                WorkingDirectoryTextBox.Focus();
                return false;
            }

            WorkingDirectoryTextBox.Text = workingDirectory;
        }

        launchOptions = new WespLaunchOptions(
            executablePath,
            arguments,
            workingDirectory);
        return true;
    }

    private async void CloseSession_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || _isBusy)
        {
            return;
        }

        RefreshSessionDetails(showProcessNotice: false);
        var trackedCount = TrackedLaunchCount;
        var trackedText = trackedCount == 0
            ? "There are no running roots launched by Shackles."
            : $"Shackles will request termination of {trackedCount} root process" +
              (trackedCount == 1 ? "." : "es.");
        var question =
            $"Close the active WESP Blocking session? {trackedText} Processes selected from the running-process " +
            "list will keep running. The blocking rules will be removed, so surviving processes and descendants " +
            "may continue unrestricted.";
        if (MessageBox.Show(
                Window.GetWindow(this),
                question,
                "Close WESP Blocking session",
                MessageBoxButton.YesNo,
                trackedCount == 0
                    ? MessageBoxImage.Question
                    : MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        var session = _session;
        string? closeError = null;
        SetBusy(true);
        try
        {
            await Task.Run(() => CloseAndDispose(session)).ConfigureAwait(true);
            CaptureActivitySnapshot(session);
            ShowNotice(
                "The WESP Blocking session was closed. The rules can now be edited again.");
        }
        catch (Exception exception)
        {
            CaptureActivitySnapshot(session);
            closeError = $"The WESP Blocking session closed with an error: {exception.Message}";
            MessageBox.Show(
                Window.GetWindow(this),
                exception.Message,
                "WESP Blocking close error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            if (session.IsClosed)
            {
                _session = null;
                _sessionRefreshTimer.Stop();
                _runningSessionProcessCount = 0;
                MemberProcessList.ItemsSource = Array.Empty<string>();
                ActivityCaptureStatusText.Text =
                    "Activity capture stopped with the closed blocking session. The entries above are retained from that session.";
            }
            else
            {
                RefreshSessionDetails(showProcessNotice: false);
            }

            SetBusy(false);
            UpdateSummary();
            if (closeError is not null)
            {
                ShowNotice(closeError);
            }
        }
    }

    private void ResetDraft_Click(object sender, RoutedEventArgs e)
    {
        if (_session is not null || _isBusy)
        {
            return;
        }

        _fileRules.Clear();
        _registryRules.Clear();
        _blockedChildExecutables.Clear();
        BlockUncPathsCheckBox.IsChecked = false;
        FileRulePathTextBox.Clear();
        RegistryRulePathTextBox.Clear();
        BlockedChildExecutableTextBox.Clear();
        RootExecutableTextBox.Clear();
        ArgumentsTextBox.Clear();
        WorkingDirectoryTextBox.Clear();
        _runningSessionProcessCount = 0;
        MemberProcessList.ItemsSource = Array.Empty<string>();
        _activityLogContext = null;
        SetActivityRows([]);
        ActivityCaptureStatusText.Text =
            "Activity capture starts with the blocking session.";
        ShowNotice("The WESP Blocking draft was reset. No session was started.");
        UpdateSummary();
    }

    private void RefreshSession_Click(object sender, RoutedEventArgs e) =>
        RefreshSessionDetails(showProcessNotice: true);

    private async void ApplyExistingProcesses_Click(
        object sender,
        RoutedEventArgs e)
    {
        await ApplyExistingProcessesAsync().ConfigureAwait(true);
    }

    private async Task ApplyExistingProcessesAsync()
    {
        if (_disposed ||
            _isBusy ||
            (_session is null && _support?.IsAvailable != true) ||
            _session is { CanApply: false })
        {
            return;
        }

        if (DataContext is not MainViewModel viewModel)
        {
            ShowNotice("The running-process list is not available.");
            return;
        }

        var existingSession = _session;
        var creatingNewSession = existingSession is null;
        string? configuredRoot = null;
        WespPolicy? policy = null;
        if (creatingNewSession)
        {
            if (!string.IsNullOrWhiteSpace(RootExecutableTextBox.Text))
            {
                if (!TryNormalizeExistingExecutable(
                        RootExecutableTextBox,
                        out var normalizedRoot))
                {
                    return;
                }

                configuredRoot = normalizedRoot;
                RootExecutableTextBox.Text = normalizedRoot;
            }

            policy = BuildPolicy();
        }

        ProcessIdentity[] existingMembers;
        try
        {
            existingMembers = existingSession?.GetProcesses()
                .Where(process => process.IsRunning)
                .Select(process => new ProcessIdentity(
                    process.ProcessId,
                    process.CreationTimeFileTimeUtc))
                .ToArray() ?? [];
        }
        catch (Exception exception)
        {
            ShowNotice($"Session processes could not be read: {exception.Message}");
            return;
        }

        var dialog = new RunningProcessPickerDialog(
            viewModel,
            new RunningProcessPickerOptions(
                WindowTitle: creatingNewSession
                    ? "Start WESP Blocking with running processes"
                    : "Apply WESP Blocking to running processes",
                Description:
                    (creatingNewSession
                        ? "The configured rules take effect immediately for each selected process. "
                        : "The active rules take effect immediately for each selected process. ") +
                    "Children started afterward inherit the WESP tag; already-running descendants do not. " +
                    "Processes selected here keep running when the session closes. Shackles, processes " +
                    "without a verified identity, and processes already using this session are omitted.",
                SelectButtonText: creatingNewSession
                    ? "_Start with selected"
                    : "_Apply selected",
                SelectButtonAutomationName: creatingNewSession
                    ? "Start WESP Blocking with selected processes"
                    : "Apply WESP Blocking to selected processes",
                ExcludedIdentities: existingMembers,
                ExcludeOtherShacklesInstances: true))
        {
            Owner = Window.GetWindow(this)
        };
        if (dialog.ShowDialog() != true || dialog.SelectedProcesses.Count == 0)
        {
            return;
        }

        var selected = dialog.SelectedProcesses;
        var sessionWasCreated = false;
        SetBusy(true);
        try
        {
            var session = existingSession;
            if (session is null)
            {
                session = configuredRoot is null
                    ? await Task.Run(() => WespSession.Create(policy!))
                        .ConfigureAwait(true)
                    : await Task.Run(() => WespSession.Create(policy!, configuredRoot))
                        .ConfigureAwait(true);
                _session = session;
                sessionWasCreated = true;
                _runningSessionProcessCount = 0;
                _activityLogContext = ActivityLogContext.From(session);
                SetActivityRows([]);
                ActivityCaptureStatusText.Text = session.ActivityCaptureStatus;
                _sessionRefreshTimer.Start();
            }

            var outcomes = await Task.Run(() => selected
                    .Select(process => ApplyToExistingProcess(session, process))
                    .ToArray())
                .ConfigureAwait(true);
            var applied = outcomes.Count(outcome =>
                outcome.Status == WespApplyProcessStatus.Applied);
            var alreadyApplied = outcomes.Count(outcome =>
                outcome.Status == WespApplyProcessStatus.AlreadyApplied);
            var failed = outcomes.Count(outcome =>
                outcome.Status == WespApplyProcessStatus.Failed);
            var summary = FormatApplySummary(
                applied,
                alreadyApplied,
                failed);
            if (sessionWasCreated && applied == 0 && alreadyApplied == 0)
            {
                summary +=
                    " The blocking session started with no processes and remains active so you can try again.";
            }

            RefreshSessionDetails(showProcessNotice: false);
            ShowNotice(summary);
            if (failed == 0 && alreadyApplied == 0)
            {
                return;
            }

            var resultsDialog = new WespProcessResultsDialog(outcomes)
            {
                Owner = Window.GetWindow(this)
            };
            _ = resultsDialog.ShowDialog();
        }
        catch (Exception exception)
        {
            var sessionCreationFailed = creatingNewSession && !sessionWasCreated;
            var errorMessage = sessionCreationFailed
                ? WespStartupTroubleshooting.FormatStartupFailure(exception)
                : exception.Message;
            ShowNotice(sessionCreationFailed
                ? "WESP Blocking could not be started. See the error details for things to check."
                : $"WESP Blocking could not be applied: {exception.Message}");
            MessageBox.Show(
                Window.GetWindow(this),
                errorMessage,
                sessionCreationFailed
                    ? "Could not start WESP Blocking"
                    : "Could not apply WESP Blocking",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
            UpdateSummary();
        }
    }

    private static WespProcessApplyOutcome ApplyToExistingProcess(
        WespSession session,
        ProcessEntry process)
    {
        try
        {
            var result = session.ApplyToProcess(
                process.ProcessId,
                process.CreationTimeUtcFileTime!.Value);
            return new WespProcessApplyOutcome(
                process,
                result.Status,
                result.ErrorMessage ?? (result.Status != WespApplyProcessStatus.Failed
                    ? string.Empty
                    : "WESP rejected the process without additional details."));
        }
        catch (Exception exception)
        {
            return new WespProcessApplyOutcome(
                process,
                WespApplyProcessStatus.Failed,
                exception.Message);
        }
    }

    private static string FormatApplySummary(
        int applied,
        int alreadyApplied,
        int failed)
    {
        var parts = new List<string>();
        if (applied != 0)
        {
            parts.Add(
                $"Applied WESP Blocking to {applied} process" +
                (applied == 1 ? string.Empty : "es"));
        }

        if (alreadyApplied != 0)
        {
            parts.Add(
                $"{alreadyApplied} " +
                (alreadyApplied == 1 ? "was" : "were") +
                " already using this session");
        }

        if (failed != 0)
        {
            parts.Add($"{failed} failed");
        }

        return parts.Count == 0
            ? "No processes were changed."
            : string.Join(". ", parts) + ".";
    }

    private void SessionRefreshTimer_Tick(object? sender, EventArgs e)
    {
        if (!_disposed && !_isBusy && _session is not null)
        {
            RefreshSessionDetails(showProcessNotice: false);
        }
    }

    private void RefreshSessionDetails(bool showProcessNotice)
    {
        var session = _session;
        if (session is null)
        {
            _runningSessionProcessCount = 0;
            MemberProcessList.ItemsSource = Array.Empty<string>();
            UpdateSummary();
            return;
        }

        try
        {
            var runningProcesses = session.GetProcesses()
                .Where(process => process.IsRunning)
                .OrderBy(process => process.ProcessId)
                .ToArray();
            var running = runningProcesses
                .Select(process => process.Origin == WespProcessOrigin.Launched
                    ? $"PID {process.ProcessId} · launched by Shackles"
                    : $"PID {process.ProcessId} · selected while running")
                .ToArray();
            _runningSessionProcessCount = running.Length;
            MemberProcessList.ItemsSource = running;
            if (showProcessNotice)
            {
                ShowNotice(
                    running.Length == 0
                        ? "The WESP Blocking session is active, with no running session processes."
                        : $"The WESP Blocking session reports {running.Length} running session process" +
                          (running.Length == 1 ? "." : "es."));
            }
        }
        catch (Exception exception)
        {
            if (showProcessNotice)
            {
                ShowNotice(
                    $"Session processes could not be refreshed: {exception.Message}");
            }
        }

        CaptureActivitySnapshot(session);
        UpdateSummary();
    }

    private void CaptureActivitySnapshot(WespSession session)
    {
        try
        {
            _activityLogContext = ActivityLogContext.From(session);
            var rows = session.GetActivities()
                .OrderByDescending(activity => activity.ObservedAt)
                .Select(CreateActivityRow)
                .ToArray();
            SetActivityRows(rows);
            ActivityCaptureStatusText.Text = session.ActivityCaptureStatus;
        }
        catch (Exception exception)
        {
            ActivityCaptureStatusText.Text =
                $"Session activity could not be refreshed: {exception.Message}";
        }
    }

    private void SetActivityRows(IReadOnlyList<SessionActivityRow> rows)
    {
        _activityRows = rows;
        if (SessionActivityList.ItemsSource is not IEnumerable<SessionActivityRow> currentRows ||
            !currentRows.SequenceEqual(rows))
        {
            SessionActivityList.ItemsSource = rows;
        }

        ActivityCountText.Text = rows.Count == 1
            ? "1 EVENT"
            : $"{rows.Count.ToString(CultureInfo.InvariantCulture)} EVENTS";
        SaveActivityLogButton.IsEnabled = !_isBusy && rows.Count != 0;
    }

    private static SessionActivityRow CreateActivityRow(WespActivity activity)
    {
        var processName = string.IsNullOrWhiteSpace(activity.ProcessName)
            ? "Unknown process"
            : activity.ProcessName;
        var targetPath = string.IsNullOrWhiteSpace(activity.TargetPath)
            ? "Target unavailable"
            : activity.TargetPath;
        return new SessionActivityRow(
            activity.ObservedAt,
            activity.ActivityKind switch
            {
                WespActivityKind.Blocked => "BLOCKED",
                WespActivityKind.ProcessAttached => "APPLIED",
                _ => "STARTED"
            },
            activity.ResourceKind,
            activity.Operation,
            processName,
            activity.ProcessId,
            targetPath,
            activity.ConfiguredPath ?? string.Empty,
            activity.EventId);
    }

    private void SaveActivityLog_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy || _activityRows.Count == 0)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Save WESP Blocking session activity",
            Filter = "Tab-separated log (*.tsv)|*.tsv|Text files (*.txt)|*.txt|All files (*.*)|*.*",
            DefaultExt = ".tsv",
            AddExtension = true,
            FileName = $"Shackles-WESP-{DateTime.Now:yyyyMMdd-HHmmss}.tsv",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        try
        {
            if (_session is { } activeSession)
            {
                CaptureActivitySnapshot(activeSession);
            }

            SaveActivityLog(dialog.FileName);
            ShowNotice(
                $"Saved {_activityRows.Count.ToString(CultureInfo.InvariantCulture)} activity " +
                $"{(_activityRows.Count == 1 ? "event" : "events")} to {dialog.FileName}.");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ShowNotice($"The activity log could not be saved: {exception.Message}");
            MessageBox.Show(
                Window.GetWindow(this),
                $"Shackles could not save the WESP Blocking activity log.\n\n{exception.Message}",
                "Could not save WESP activity",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void SaveActivityLog(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read);
        using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        WriteTsvRow(writer, "Shackles WESP Blocking session activity");
        WriteTsvRow(writer, "Format version", "1");
        WriteTsvRow(
            writer,
            "Exported",
            DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture));

        if (_activityLogContext is { } context)
        {
            WriteTsvRow(writer, "Root application", context.RootExecutablePath);
            WriteTsvRow(writer, "WESP client", context.ClientVersion);
            WriteTsvRow(
                writer,
                "File policy",
                $"{context.BlockedFileRuleCount.ToString(CultureInfo.InvariantCulture)} blocked; " +
                $"{context.ReadOnlyFileRuleCount.ToString(CultureInfo.InvariantCulture)} read-only; " +
                $"UNC paths {(context.BlockUncPaths ? "blocked" : "allowed")}");
            WriteTsvRow(
                writer,
                "Registry policy",
                $"{context.BlockedRegistryRuleCount.ToString(CultureInfo.InvariantCulture)} blocked; " +
                $"{context.ReadOnlyRegistryRuleCount.ToString(CultureInfo.InvariantCulture)} read-only");
            WriteTsvRow(
                writer,
                "Blocked child images",
                context.BlockedChildExecutables.Count == 0
                    ? "None"
                    : string.Join("; ", context.BlockedChildExecutables));
        }

        WriteTsvRow(writer);
        WriteTsvRow(
            writer,
            "ObservedAt",
            "Status",
            "Resource",
            "Operation",
            "ProcessName",
            "ProcessId",
            "Target",
            "ConfiguredRule",
            "WespEventId");
        foreach (var row in _activityRows)
        {
            WriteTsvRow(
                writer,
                row.ObservedAt.ToString("O", CultureInfo.InvariantCulture),
                row.Status,
                row.ResourceKind.ToString(),
                row.Operation,
                row.ProcessName,
                row.ProcessId?.ToString(CultureInfo.InvariantCulture),
                row.TargetPath,
                row.ConfiguredRule,
                row.EventId == 0
                    ? string.Empty
                    : row.EventId.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void WriteTsvRow(TextWriter writer, params string?[] fields) =>
        writer.WriteLine(string.Join('\t', fields.Select(EscapeTsvField)));

    private static string EscapeTsvField(string? value)
    {
        var field = value ?? string.Empty;
        return field.IndexOfAny(['\t', '\r', '\n', '"']) >= 0
            ? $"\"{field.Replace("\"", "\"\"")}\""
            : field;
    }

    private bool TryNormalizeExistingFolder(
        TextBox source,
        out string path)
    {
        path = string.Empty;
        if (_session is not null || _isBusy)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(source.Text))
        {
            ShowNotice("Enter or choose a folder first.");
            source.Focus();
            return false;
        }

        try
        {
            path = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(source.Text.Trim()));
        }
        catch (Exception exception)
        {
            ShowNotice($"The folder path is invalid: {exception.Message}");
            source.Focus();
            return false;
        }

        if (!Directory.Exists(path))
        {
            ShowNotice("The folder does not exist.");
            source.Focus();
            return false;
        }

        return true;
    }

    private bool TryNormalizeRegistryKey(TextBox source, out string path)
    {
        path = string.Empty;
        if (_session is not null || _isBusy)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(source.Text) || source.Text.Contains('\0'))
        {
            ShowNotice("Enter a valid registry key path first.");
            source.Focus();
            return false;
        }

        var candidate = source.Text.Trim().TrimEnd('\\');
        if (candidate.StartsWith("Computer\\", StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate[9..];
        }

        if (candidate.Length == 0)
        {
            ShowNotice("Enter a registry key path first.");
            source.Focus();
            return false;
        }

        if (candidate.StartsWith("\\REGISTRY\\", StringComparison.OrdinalIgnoreCase))
        {
            var nativeTail = candidate["\\REGISTRY\\".Length..];
            if (!ValidateRegistrySegments(nativeTail, source))
            {
                return false;
            }

            var separator = nativeTail.IndexOf('\\');
            var root = separator < 0 ? nativeTail : nativeTail[..separator];
            if (!string.Equals(root, "MACHINE", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(root, "USER", StringComparison.OrdinalIgnoreCase))
            {
                ShowNotice("A canonical registry path must begin with \\REGISTRY\\MACHINE or \\REGISTRY\\USER.");
                source.Focus();
                return false;
            }

            path = candidate;
            return true;
        }

        var firstSeparator = candidate.IndexOf('\\');
        var suppliedRoot = firstSeparator < 0
            ? candidate
            : candidate[..firstSeparator];
        var subKey = firstSeparator < 0
            ? string.Empty
            : candidate[(firstSeparator + 1)..];
        if (!ValidateRegistrySegments(subKey, source))
        {
            return false;
        }

        var normalizedRoot = suppliedRoot.ToUpperInvariant() switch
        {
            "HKCU" or "HKEY_CURRENT_USER" => "HKCU",
            "HKLM" or "HKEY_LOCAL_MACHINE" => "HKLM",
            "HKCR" or "HKEY_CLASSES_ROOT" => "HKCR",
            "HKU" or "HKEY_USERS" => "HKU",
            "HKCC" or "HKEY_CURRENT_CONFIG" => string.Empty,
            _ => null
        };
        if (normalizedRoot is null)
        {
            ShowNotice(
                $"Unsupported registry root '{suppliedRoot}'. Use HKCU, HKLM, HKCR, HKU, or a canonical \\REGISTRY path.");
            source.Focus();
            return false;
        }

        if (normalizedRoot.Length == 0)
        {
            ShowNotice(
                "HKCC is a symbolic view. Use its canonical \\REGISTRY\\MACHINE path for this WESP preview.");
            source.Focus();
            return false;
        }

        path = subKey.Length == 0
            ? normalizedRoot
            : $"{normalizedRoot}\\{subKey}";
        return true;
    }

    private bool ValidateRegistrySegments(string path, TextBox source)
    {
        if (path.Length != 0 && path.Split('\\').Any(segment => segment.Length == 0))
        {
            ShowNotice("The registry key path contains an empty segment.");
            source.Focus();
            return false;
        }

        return true;
    }

    private bool TryNormalizeExistingExecutable(
        TextBox source,
        out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(source.Text))
        {
            ShowNotice("Enter or choose an executable first.");
            source.Focus();
            return false;
        }

        try
        {
            path = Path.GetFullPath(source.Text.Trim());
        }
        catch (Exception exception)
        {
            ShowNotice($"The executable path is invalid: {exception.Message}");
            source.Focus();
            return false;
        }

        if (!File.Exists(path))
        {
            ShowNotice("The executable does not exist.");
            source.Focus();
            return false;
        }

        return true;
    }

    private bool TryNormalizeChildExecutableName(
        TextBox source,
        out string imageName)
    {
        imageName = string.Empty;
        if (string.IsNullOrWhiteSpace(source.Text) || source.Text.Any(char.IsControl))
        {
            ShowNotice(
                "Enter an executable name, such as powershell.exe, or browse to an application.");
            source.Focus();
            return false;
        }

        try
        {
            var entered = source.Text;
            var trimmed = entered.Trim();
            var candidate = trimmed.Length >= 2 &&
                            trimmed[0] == '"' &&
                            trimmed[^1] == '"'
                ? trimmed[1..^1]
                : entered.TrimStart();
            if (candidate.Length == 0 ||
                candidate.Any(char.IsControl) ||
                candidate.Contains('"'))
            {
                throw new ArgumentException(
                    "The executable name contains an invalid character.");
            }

            imageName = Path.GetFileName(candidate);
            if (!string.Equals(candidate, imageName, StringComparison.Ordinal) &&
                !Path.IsPathFullyQualified(candidate))
            {
                ShowNotice(
                    "Enter only an executable name or a fully qualified path to an application.");
                source.Focus();
                return false;
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            ShowNotice($"The executable name is invalid: {exception.Message}");
            source.Focus();
            return false;
        }

        if (string.IsNullOrWhiteSpace(imageName) ||
            imageName is "." or ".." ||
            imageName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            imageName.EndsWith('.') ||
            imageName.EndsWith(' '))
        {
            ShowNotice(
                "Enter a valid executable file name, such as powershell.exe. You may also paste a full path.");
            source.Focus();
            return false;
        }

        return true;
    }

    private static WespResourceRuleDraft? FindRule(
        IEnumerable<WespResourceRuleDraft> rules,
        string path) => rules.FirstOrDefault(rule => string.Equals(
            rule.Path,
            path,
            StringComparison.OrdinalIgnoreCase));

    private static bool AddUniqueExecutableName(
        ObservableCollection<string> imageNames,
        string imageName)
    {
        if (imageNames.Any(item => string.Equals(
                item,
                imageName,
                StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        imageNames.Add(imageName);
        return true;
    }

    private static bool PathsOverlap(string first, string second) =>
        IsSameOrDescendant(first, second) || IsSameOrDescendant(second, first);

    private static bool IsSameOrDescendant(string candidate, string root)
    {
        if (string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private void SetBusy(bool busy)
    {
        _isBusy = busy;
        OperationProgress.Visibility = busy
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateActionState();
    }

    private void UpdateActionState()
    {
        if (!IsReadyForControlUpdates())
        {
            return;
        }

        var active = _session is not null;
        var canApply = _session?.CanApply ?? true;
        var primaryUsesProcessPicker = PrimaryActionUsesProcessPicker();
        var canEditLaunchOptions =
            !_isBusy &&
            !primaryUsesProcessPicker &&
            (!active || _session!.CanLaunch);
        PolicyEditorPanel.IsEnabled = !_isBusy && !active;
        RootExecutablePanel.IsEnabled = !_isBusy && !active;
        ArgumentsTextBox.IsEnabled = canEditLaunchOptions;
        WorkingDirectoryTextBox.IsEnabled = canEditLaunchOptions;
        BrowseWorkingDirectoryButton.IsEnabled = canEditLaunchOptions;
        RefreshSupportButton.IsEnabled = !_isBusy;
        ApplyExistingProcessesButton.IsEnabled =
            !_isBusy &&
            (active || _support?.IsAvailable == true) &&
            canApply;
        ApplyExistingProcessesButton.Content = active
            ? "_Apply to running processes…"
            : "_Use running processes…";
        ApplyExistingProcessesButton.ToolTip = active
            ? "Apply this active WESP Blocking session to processes that are already running"
            : "Use one or more running processes instead of launching the configured root application";
        System.Windows.Automation.AutomationProperties.SetName(
            ApplyExistingProcessesButton,
            active
                ? "Apply WESP Blocking to running processes"
                : "Use running processes for WESP Blocking");
        System.Windows.Automation.AutomationProperties.SetHelpText(
            ApplyExistingProcessesButton,
            ApplyExistingProcessesButton.ToolTip?.ToString() ?? string.Empty);
        ApplyExistingProcessesButton.Visibility = primaryUsesProcessPicker
            ? Visibility.Collapsed
            : Visibility.Visible;
        RefreshSessionButton.IsEnabled = !_isBusy && active;
        PrimaryActionButton.IsEnabled = primaryUsesProcessPicker
            ? !_isBusy &&
              (_session is not null || _support?.IsAvailable == true) &&
              canApply
            : !_isBusy &&
              (active
                  ? _session!.CanLaunch
                  : _support?.IsAvailable == true);
        PrimaryActionButton.Content = primaryUsesProcessPicker
            ? active
                ? "_Apply to running processes…"
                : "_Start WESP Blocking with running processes…"
            : active
                ? "_Launch again with these blocking rules"
                : "_Start WESP Blocking and launch";
        PrimaryActionButton.ToolTip = primaryUsesProcessPicker
            ? active
                ? "Apply this active WESP Blocking session to processes that are already running"
                : "Choose one or more running processes and start WESP Blocking"
            : active
                ? "Launch the configured application again with this WESP Blocking session"
                : "Start WESP Blocking and launch the configured application";
        System.Windows.Automation.AutomationProperties.SetName(
            PrimaryActionButton,
            primaryUsesProcessPicker
                ? active
                    ? "Apply WESP Blocking to running processes"
                    : "Start WESP Blocking with running processes"
                : active
                    ? "Launch another process with WESP Blocking"
                    : "Start WESP Blocking and launch the root application");
        System.Windows.Automation.AutomationProperties.SetHelpText(
            PrimaryActionButton,
            PrimaryActionButton.ToolTip?.ToString() ?? string.Empty);
        CloseSessionButton.Visibility = active
            ? Visibility.Visible
            : Visibility.Collapsed;
        CloseSessionButton.IsEnabled = !_isBusy && active;
        ResetDraftButton.IsEnabled = !_isBusy && !active;
        SaveActivityLogButton.IsEnabled = !_isBusy && _activityRows.Count != 0;
    }

    private bool IsReadyForControlUpdates() =>
        PrimaryActionButton is not null && PolicyEditorPanel is not null;

    private bool PrimaryActionUsesProcessPicker() =>
        _session is { } session
            ? session.RootExecutablePath is null
            : string.IsNullOrWhiteSpace(RootExecutableTextBox.Text);

    private void UpdateSummary()
    {
        if (!IsReadyForControlUpdates())
        {
            return;
        }

        var active = _session is not null;
        SessionStateBadgeText.Text = active
            ? _session!.CanApply
                ? "ACTIVE"
                : "CLEANUP NEEDED"
            : "DRAFT";
        RootSummaryText.Text = active && _session!.RootExecutablePath is null
            ? "None (attach-only)"
            : string.IsNullOrWhiteSpace(RootExecutableTextBox.Text)
                ? "Not selected"
                : RootExecutableTextBox.Text.Trim();
        FileRulesSummaryText.Text = RuleCountSummary(_fileRules) +
                                    (BlockUncPathsCheckBox.IsChecked == true
                                        ? " • UNC blocked"
                                        : string.Empty);
        RegistryRulesSummaryText.Text = RuleCountSummary(_registryRules);
        BlockedChildrenSummaryText.Text = $"{_blockedChildExecutables.Count} blocked";
        ClientVersionSummaryText.Text = active
            ? _session!.ClientVersion
            : "Not connected";
        SessionProcessesSummaryText.Text =
            _runningSessionProcessCount.ToString(CultureInfo.InvariantCulture);
        UpdateActionState();
    }

    private static string RuleCountSummary(
        IEnumerable<WespResourceRuleDraft> rules)
    {
        var snapshot = rules.ToArray();
        var blocked = snapshot.Count(rule => rule.AccessIndex == BlockedAccessIndex);
        var readOnly = snapshot.Length - blocked;
        return $"{blocked} blocked • {readOnly} read-only";
    }

    private static int NormalizeAccessIndex(int accessIndex) =>
        accessIndex == ReadOnlyAccessIndex
            ? ReadOnlyAccessIndex
            : BlockedAccessIndex;

    private static string AccessText(int accessIndex) =>
        accessIndex == ReadOnlyAccessIndex ? "Read-only" : "Blocked";

    private void ShowNotice(string message)
    {
        WorkspaceNoticeText.Text = message;
        WorkspaceNotice.Visibility = Visibility.Visible;
    }

    private static void CloseAndDispose(WespSession session)
    {
        session.Close();
        session.Dispose();
    }

    private static void TryCloseAndDispose(WespSession session)
    {
        try
        {
            CloseAndDispose(session);
        }
        catch
        {
            // Application shutdown is already confirmed. Process exit forces the
            // preview client session to disconnect if explicit cleanup fails.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _sessionRefreshTimer.Stop();
        _sessionRefreshTimer.Tick -= SessionRefreshTimer_Tick;
        var session = _session;
        _session = null;
        if (session is not null)
        {
            TryCloseAndDispose(session);
        }
    }

    private sealed class WespResourceRuleDraft : INotifyPropertyChanged
    {
        private readonly Action _onChanged;
        private int _accessIndex;

        internal WespResourceRuleDraft(
            string path,
            int accessIndex,
            Action onChanged)
        {
            Path = path;
            _accessIndex = NormalizeAccessIndex(accessIndex);
            _onChanged = onChanged;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Path { get; }

        public int AccessIndex
        {
            get => _accessIndex;
            set
            {
                var normalized = NormalizeAccessIndex(value);
                if (_accessIndex == normalized)
                {
                    return;
                }

                _accessIndex = normalized;
                PropertyChanged?.Invoke(
                    this,
                    new PropertyChangedEventArgs(nameof(AccessIndex)));
                _onChanged();
            }
        }
    }

    private sealed record SessionActivityRow(
        DateTimeOffset ObservedAt,
        string Status,
        WespActivityResourceKind ResourceKind,
        string Operation,
        string ProcessName,
        int? ProcessId,
        string TargetPath,
        string ConfiguredRule,
        ulong EventId)
    {
        public string TimeAndOperation => $"{ObservedAt:HH:mm:ss} · {Operation}";

        public string Process => ProcessId is int processId
            ? $"Process: {ProcessName} · PID {processId.ToString(CultureInfo.InvariantCulture)}"
            : $"Process: {ProcessName}";

        public string Target => $"Target: {TargetPath}";

        public string Rule => ConfiguredRule;
    }

    private sealed record ActivityLogContext(
        string RootExecutablePath,
        string ClientVersion,
        int BlockedFileRuleCount,
        int ReadOnlyFileRuleCount,
        bool BlockUncPaths,
        int BlockedRegistryRuleCount,
        int ReadOnlyRegistryRuleCount,
        IReadOnlyList<string> BlockedChildExecutables)
    {
        public static ActivityLogContext From(WespSession session) => new(
            session.RootExecutablePath ?? "None (attach-only session)",
            session.ClientVersion,
            session.Policy.BlockedFilePaths.Count,
            session.Policy.ReadOnlyFilePaths.Count,
            session.Policy.BlockUncPaths,
            session.Policy.BlockedRegistryKeys.Count,
            session.Policy.ReadOnlyRegistryKeys.Count,
            session.Policy.BlockedChildExecutables.ToArray());
    }
}
