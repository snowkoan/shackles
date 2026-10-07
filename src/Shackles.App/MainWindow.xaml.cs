using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using Shackles.App.Controls;
using Shackles.App.Dialogs;
using Shackles.App.Infrastructure;
using Shackles.App.Models;
using Shackles.App.Services;
using Shackles.App.ViewModels;
using Shackles.Wfp;

namespace Shackles.App;

public sealed partial class MainWindow : Window, IDisposable
{
    private readonly MainViewModel _viewModel;
    private bool _isOpeningNamedJob;
    private bool _closeConfirmed;
    private bool _disposed;
    private bool _isClosing;

    public MainWindow() : this(new MainViewModel(new JobControlService()))
    {
    }

    internal MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        ConfigureElevationIndicator();
        _viewModel = viewModel;
        DataContext = _viewModel;
        Loaded += async (_, _) =>
        {
            if (!_isClosing && !_disposed)
            {
                await _viewModel.InitializeAsync().ConfigureAwait(true);
            }
        };

        if (App.ShouldOpenWfpWorkspace)
        {
            WfpWorkspaceTab.IsChecked = true;
        }
        else if (App.ShouldOpenWespWorkspace)
        {
            WespWorkspaceTab.IsChecked = true;
        }
    }

    private void ConfigureElevationIndicator()
    {
        string description;
        try
        {
            if (!WfpSupport.IsCurrentProcessHighIntegrity())
            {
                return;
            }

            Title = "Shackles (Administrator)";
            description = "This Shackles process is running elevated with administrator privileges.";
            AutomationProperties.SetName(ElevationBadgeText, "Shackles is running as administrator (elevated)");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Title = "Shackles (Elevation unknown)";
            ElevationBadgeText.Text = "Elevation unknown";
            description = $"Shackles could not determine whether this process is running elevated. {exception.Message}";
            AutomationProperties.SetName(ElevationBadgeText, "Shackles elevation status is unknown");
        }

        ElevationBadge.ToolTip = description;
        AutomationProperties.SetHelpText(ElevationBadgeText, description);
        ElevationBadge.Visibility = Visibility.Visible;
    }

    private bool ShowWorkspace(FrameworkElement? selected)
    {
        if (JobObjectsWorkspace is null ||
            AppContainerWorkspace is null ||
            ExperimentalSandboxWorkspace is null ||
            WespWorkspace is null ||
            WfpWorkspace is null)
        {
            return false;
        }

        foreach (var workspace in new FrameworkElement[]
                 { JobObjectsWorkspace, AppContainerWorkspace, ExperimentalSandboxWorkspace, WespWorkspace, WfpWorkspace })
        {
            workspace.Visibility = workspace == selected ? Visibility.Visible : Visibility.Collapsed;
        }
        return true;
    }

    private void JobObjectsWorkspaceTab_Click(object sender, RoutedEventArgs e) => ShowWorkspace(JobObjectsWorkspace);

    private void AppContainerWorkspaceTab_Click(object sender, RoutedEventArgs e)
    {
        if (ShowWorkspace(AppContainerWorkspace))
        {
            AppContainerWorkspace.PrepareForDisplay();
        }
    }

    private void ExperimentalSandboxWorkspaceTab_Click(object sender, RoutedEventArgs e)
    {
        if (ShowWorkspace(ExperimentalSandboxWorkspace))
        {
            ExperimentalSandboxWorkspace.PrepareForDisplay();
        }
    }

    private void WespWorkspaceTab_Click(object sender, RoutedEventArgs e)
    {
        if (ShowWorkspace(WespWorkspace))
        {
            WespWorkspace.PrepareForDisplay();
        }
    }

    private void WfpWorkspaceTab_Click(object sender, RoutedEventArgs e)
    {
        if (ShowWorkspace(WfpWorkspace))
        {
            WfpWorkspace.PrepareForDisplay();
        }
    }

    private async void NewJob_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new JobNameDialog(openExisting: false) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            await _viewModel.CreateJobAsync(dialog.JobName).ConfigureAwait(true);
        }
    }

    private async void OpenJob_Click(object sender, RoutedEventArgs e)
    {
        if (_isOpeningNamedJob)
        {
            return;
        }

        _isOpeningNamedJob = true;
        try
        {
            var dialog = new JobNameDialog(openExisting: true) { Owner = this };
            if (dialog.ShowDialog() == true && dialog.JobName is { } name)
            {
                var opened = await _viewModel.OpenJobAsync(name).ConfigureAwait(true);
                if (opened is null && _viewModel.LastOpenJobErrorMessage is { Length: > 0 } errorMessage)
                {
                    MessageBox.Show(
                        this,
                        errorMessage,
                        "Could not open named Job Object",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }
        finally
        {
            _isOpeningNamedJob = false;
        }
    }

    private async void JobDetails_LaunchRequested(object sender, RoutedEventArgs e) => await LaunchInSelectedJobAsync().ConfigureAwait(true);

    private async void AssignRunningProcesses_Click(object sender, RoutedEventArgs e) =>
        await AssignRunningProcessesAsync().ConfigureAwait(true);

    private async Task LaunchInSelectedJobAsync()
    {
        if (_viewModel.SelectedJob is not { } target)
        {
            MessageBox.Show(
                this,
                "Choose a job before launching an executable.",
                "No job selected",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new LaunchProcessDialog(target.DisplayName) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Request is { } request)
        {
            await _viewModel.LaunchProcessAsync(target, request).ConfigureAwait(true);
        }
    }

    private void JobDetails_CloseRequested(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedJob is { } job)
        {
            CloseJobWithWarning(job);
        }
    }

    private void JobTab_CloseRequested(object? sender, InstanceTabCloseRequestedEventArgs e)
    {
        if (e.Item is JobViewModel job && _viewModel.Jobs.Contains(job))
        {
            CloseJobWithWarning(job);
        }
    }

    private void CloseJobWithWarning(JobViewModel job)
    {
        if (job.IsBusy)
        {
            MessageBox.Show(
                this,
                $"Wait for the current operation on '{job.DisplayName}' to finish before closing its handle.",
                "Job operation in progress",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var closeRisks = new List<string>();
        if (job.IsSnapshotStale)
        {
            closeRisks.Add("The current job settings could not be verified. Closing its handle may terminate members or detach notification delivery. Refresh first to inspect the current settings.");
        }
        if (job.KillOnCloseConfigured)
        {
            closeRisks.Add("KillOnJobClose is configured. If this is the last open handle, Windows may terminate every process in the job.");
        }

        if (job.LiveNotificationOwnerRequiredOnClose)
        {
            closeRisks.Add("This handle owns the completion port required by PostNotification. Closing it detaches live delivery; if the per-job time limit later expires without another port, Windows falls back to terminating job processes.");
        }

        if (closeRisks.Count > 0)
        {
            var answer = MessageBox.Show(
                this,
                $"'{job.DisplayName}' has close-sensitive settings:\n\n• {string.Join("\n\n• ", closeRisks)}\n\nClose this handle anyway?",
                "Closing this handle may affect job processes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }
        }

        if (!_viewModel.CloseJob(job))
        {
            MessageBox.Show(this, _viewModel.StatusMessage, "Could not close Job Object handle",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task AssignRunningProcessesAsync()
    {
        if (_viewModel.SelectedJob is not { } target)
        {
            MessageBox.Show(
                this,
                "Choose a target job tab before assigning the selected processes.",
                "No job selected",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            JobList.Focus();
            return;
        }

        if (target.IsBusy)
        {
            MessageBox.Show(
                this,
                $"Wait for the current operation on '{target.DisplayName}' to finish before assigning processes.",
                "Job operation in progress",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            var targetRefresh = target.RefreshAsync();
            var processRefresh = _viewModel.RefreshProcessesAsync();
            await Task.WhenAll(targetRefresh, processRefresh).ConfigureAwait(true);
            if (!processRefresh.Result)
            {
                MessageBox.Show(
                    this,
                    _viewModel.StatusMessage,
                    "Could not open the process picker",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"Shackles could not refresh the Job Object and running processes: {exception.Message}",
                "Could not open the process picker",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        var memberProcessIds = target.Members
            .Select(member => member.ProcessId)
            .ToHashSet();
        var memberIdentities = _viewModel.Processes
            .Where(process =>
                memberProcessIds.Contains(process.ProcessId) &&
                process.CreationTimeUtcFileTime.HasValue)
            .Select(process => new ProcessIdentity(
                process.ProcessId,
                process.CreationTimeUtcFileTime!.Value))
            .ToArray();
        var dialog = new RunningProcessPickerDialog(
            _viewModel,
            new RunningProcessPickerOptions(
                WindowTitle: $"Assign running processes to {target.DisplayName}",
                Description:
                    $"Selected processes will be assigned to '{target.DisplayName}'. This cannot be undone " +
                    "while a process is running; normally it must be terminated to leave the Job Object. " +
                    "New children normally inherit membership unless breakaway applies. The current Shackles " +
                    "process, processes without a verified identity, and current members are omitted.",
                SelectButtonText: "_Assign selected",
                SelectButtonAutomationName: "Assign selected processes to the Job Object",
                ExcludedIdentities: memberIdentities))
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true && dialog.SelectedProcesses.Count != 0)
        {
            await ConfirmAndAssignAsync(target, dialog.SelectedProcesses).ConfigureAwait(true);
        }
    }

    private async Task ConfirmAndAssignAsync(JobViewModel target, IReadOnlyList<ProcessEntry> rows)
    {
        var assignable = rows.Where(item => item.IsAssignable && item.CreationTimeUtcFileTime.HasValue).ToArray();
        var skipped = rows.Where(item => !item.IsAssignable || !item.CreationTimeUtcFileTime.HasValue).ToArray();
        if (assignable.Length == 0)
        {
            var unavailableResults = skipped
                .Select(item => new AssignmentOutcome(item.ProcessId, item.Name, false, item.AssignmentHint, WasAttempted: false))
                .ToArray();
            new AssignmentResultsDialog(unavailableResults) { Owner = this }.ShowDialog();
            return;
        }

        var names = string.Join(
            Environment.NewLine,
            assignable.Take(6).Select(item => $"  • {item.Name} (PID {item.ProcessId})"));
        if (assignable.Length > 6)
        {
            names += $"{Environment.NewLine}  • …and {assignable.Length - 6} more";
        }

        var skippedNote = skipped.Length == 0
            ? string.Empty
            : $"{Environment.NewLine}{Environment.NewLine}{skipped.Length} identity-unverified row{(skipped.Length == 1 ? " will" : "s will")} be reported as not attempted.";
        var answer = MessageBox.Show(
            this,
            $"Assign these processes to '{target.DisplayName}'?{Environment.NewLine}{Environment.NewLine}{names}{skippedNote}{Environment.NewLine}{Environment.NewLine}" +
            "This assignment is effectively irreversible for a running process: Windows does not provide a supported detach operation. Removing a successfully assigned process generally requires terminating it.",
            "Confirm irreversible job assignment",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        var identities = assignable
            .Select(item => new ProcessIdentity(item.ProcessId, item.CreationTimeUtcFileTime!.Value))
            .ToArray();
        var attemptedResults = await _viewModel.AssignProcessesAsync(target, identities).ConfigureAwait(true);
        var resultsByPid = attemptedResults.ToDictionary(item => item.ProcessId);
        var combined = rows.Select(row =>
        {
            if (resultsByPid.TryGetValue(row.ProcessId, out var attempted))
            {
                return attempted with { ProcessName = row.Name };
            }

            return new AssignmentOutcome(row.ProcessId, row.Name, false, row.AssignmentHint, WasAttempted: false);
        }).ToArray();

        new AssignmentResultsDialog(combined) { Owner = this }.ShowDialog();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (JobObjectsWorkspace.Visibility == Visibility.Visible && JobList.HandleWorkspaceKeyDown(e))
        {
            return;
        }

        if (JobObjectsWorkspace.Visibility == Visibility.Visible &&
            e.Key == Key.Enter &&
            Keyboard.Modifiers == ModifierKeys.Control)
        {
            AssignRunningProcesses_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        e.Cancel = true;
        if (_isClosing)
        {
            return;
        }

        if (_viewModel.HasPendingOperations)
        {
            MessageBox.Show(this, "Wait for the current job or process-list operation to finish before closing Shackles.",
                "Operation in progress", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (AppContainerWorkspace.IsBusy ||
            ExperimentalSandboxWorkspace.IsBusy ||
            WespWorkspace.IsBusy ||
            WfpWorkspace.IsBusy)
        {
            var workspace = AppContainerWorkspace.IsBusy
                ? "AppContainer"
                : ExperimentalSandboxWorkspace.IsBusy
                    ? "experimental sandbox"
                    : WespWorkspace.IsBusy
                        ? "WESP Blocking"
                        : "WFP Blocking";
            MessageBox.Show(
                this,
                $"Wait for the current {workspace} operation to finish before closing Shackles.",
                "Restriction operation in progress",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var busyJobs = _viewModel.Jobs.Where(job => job.IsBusy).Select(job => job.DisplayName).ToArray();
        if (busyJobs.Length > 0)
        {
            MessageBox.Show(
                this,
                $"Wait for the current operation on {string.Join(", ", busyJobs)} to finish before closing Shackles.",
                "Job operation in progress",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!_closeConfirmed)
        {
            var killOnCloseJobs = _viewModel.Jobs.Where(job => job.KillOnCloseConfigured).Select(job => job.DisplayName).ToArray();
            var liveNotificationJobs = _viewModel.Jobs.Where(job => job.LiveNotificationOwnerRequiredOnClose).Select(job => job.DisplayName).ToArray();
            var unverifiedJobs = _viewModel.Jobs.Where(job => job.IsSnapshotStale).Select(job => job.DisplayName).ToArray();
            var appContainerTrackedCount = AppContainerWorkspace.TrackedLaunchCount;
            var canHaveUntrackedDescendants =
                AppContainerWorkspace.CanHaveUntrackedDescendants;
            var experimentalTrackedCount =
                ExperimentalSandboxWorkspace.TrackedLaunchCount;
            var wespTrackedCount = WespWorkspace.TrackedLaunchCount;
            var wespHasActiveSession = WespWorkspace.HasActiveSession;
            var wfpRuleCount = WfpWorkspace.ActiveRuleCount;
            var wfpHasActivePolicy = WfpWorkspace.HasActivePolicy;
            if (killOnCloseJobs.Length > 0 ||
                liveNotificationJobs.Length > 0 ||
                unverifiedJobs.Length > 0 ||
                appContainerTrackedCount > 0 ||
                experimentalTrackedCount > 0 ||
                wespHasActiveSession ||
                wfpHasActivePolicy)
            {
                var warnings = new List<string>();
                if (unverifiedJobs.Length > 0)
                {
                    warnings.Add($"Current settings could not be verified for: {string.Join(", ", unverifiedJobs)}. Closing these handles may affect their processes or notification delivery.");
                }
                if (killOnCloseJobs.Length > 0)
                {
                    warnings.Add($"KillOnJobClose may terminate members of: {string.Join(", ", killOnCloseJobs)}.");
                }

                if (liveNotificationJobs.Length > 0)
                {
                    warnings.Add($"Closing detaches the live completion ports for: {string.Join(", ", liveNotificationJobs)}. If a per-job time limit later expires without another port, Windows falls back to terminating members.");
                }

                if (appContainerTrackedCount > 0)
                {
                    warnings.Add(
                        $"Closing terminates {appContainerTrackedCount} directly launched AppContainer " +
                        $"process{(appContainerTrackedCount == 1 ? string.Empty : "es")} " +
                        $"and cleans up: {string.Join(", ", AppContainerWorkspace.ActiveSandboxNames)}." +
                        (canHaveUntrackedDescendants
                            ? " Descendants are not tracked and may continue running."
                            : string.Empty));
                }

                if (experimentalTrackedCount > 0)
                {
                    warnings.Add(
                        $"Closing terminates {experimentalTrackedCount} directly launched " +
                        $"experimental sandbox process" +
                        $"{(experimentalTrackedCount == 1 ? string.Empty : "es")} " +
                        $"and cleans up: {string.Join(", ", ExperimentalSandboxWorkspace.ActiveSandboxNames)}. " +
                        "The experimental API does not expose its internal Job Objects, " +
                        "so descendant lifetime cannot be inspected by Shackles.");
                }

                if (wespHasActiveSession)
                {
                    var rootEffect = wespTrackedCount == 0
                        ? "There are no running directly tracked roots, but tagged descendants may still exist."
                        : $"Shackles will request termination of {wespTrackedCount} directly launched root " +
                          $"process{(wespTrackedCount == 1 ? string.Empty : "es")}.";
                    warnings.Add(
                        $"An active WESP Blocking session will be closed. {rootEffect} Closing removes " +
                        "the client-session blocking rules. Processes selected from the running-process list " +
                        "will not be terminated; surviving processes and descendants may continue unrestricted.");
                }

                if (wfpHasActivePolicy)
                {
                    warnings.Add(
                        $"Closing removes {wfpRuleCount} active WFP block rule" +
                        $"{(wfpRuleCount == 1 ? string.Empty : "s")}. Other firewall and WFP policies remain in force. " +
                        "The rules are also dynamic, so BFE removes them if Shackles exits unexpectedly.");
                }

                var answer = MessageBox.Show(
                    this,
                    $"{string.Join("\n\n", warnings)}\n\nClose Shackles anyway?",
                    "Closing Shackles may affect processes",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);
                if (answer != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            _closeConfirmed = true;
        }

        _isClosing = true;
        IsEnabled = false;
        var previousTitle = Title;
        Title = "Shackles — Cleaning up…";
        try
        {
            // Empty workspaces can finish cleanup synchronously. Let the original
            // Closing callback return before cleanup attempts the final Close.
            await System.Windows.Threading.Dispatcher.Yield();

            var warnings = await WorkspaceCleanupCoordinator.CloseAsync(
            [
                ("AppContainer", AppContainerWorkspace.CloseAllAsync),
                ("Experimental sandboxes", ExperimentalSandboxWorkspace.CloseAllAsync),
                ("WESP Blocking", WespWorkspace.CloseAllAsync),
                ("WFP Blocking", WfpWorkspace.CloseAllAsync),
                ("Job Objects", _viewModel.CloseAllAsync)
            ]).ConfigureAwait(true);

            if (warnings.Count > 0)
            {
                IsEnabled = true;
                MessageBox.Show(this,
                    $"Some resources could not be cleaned up:\n\n• {string.Join("\n\n• ", warnings)}\n\nShackles will stay open. Retry cleanup in the workspace or close again to retry.",
                    "Cleanup needs attention", MessageBoxButton.OK, MessageBoxImage.Warning);
                _closeConfirmed = false;
                return;
            }

            Dispose();
            Close();
        }
        catch (Exception ex)
        {
            IsEnabled = true;
            _closeConfirmed = false;
            MessageBox.Show(this, $"Cleanup could not finish: {ex.Message}\n\nClose again to retry.",
                "Cleanup needs attention", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Title = previousTitle;
            IsEnabled = true;
            _isClosing = false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        WorkspaceCleanupCoordinator.Dispose(
        [
            ("AppContainer", AppContainerWorkspace.Dispose),
            ("Experimental sandboxes", ExperimentalSandboxWorkspace.Dispose),
            ("WESP Blocking", WespWorkspace.Dispose),
            ("WFP Blocking", WfpWorkspace.Dispose),
            ("Job Objects", _viewModel.Dispose)
        ]);
        _disposed = true;
    }
}
