using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Shackles.App.Dialogs;
using Shackles.App.Models;
using Shackles.App.Services;
using Shackles.App.ViewModels;

namespace Shackles.App;

public sealed partial class MainWindow : Window, IDisposable
{
    private readonly MainViewModel _viewModel;
    private bool _isOpeningNamedJob;
    private bool _closeConfirmed;
    private bool _disposed;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(new JobControlService());
        DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.InitializeAsync().ConfigureAwait(true);

        if (App.ShouldOpenWespWorkspace)
        {
            WespWorkspaceTab.IsChecked = true;
        }
    }

    private void JobObjectsWorkspaceTab_Click(object sender, RoutedEventArgs e)
    {
        if (JobObjectsWorkspace is null ||
            AppContainerWorkspace is null ||
            ExperimentalSandboxWorkspace is null ||
            WespWorkspace is null)
        {
            return;
        }

        JobObjectsWorkspace.Visibility = Visibility.Visible;
        AppContainerWorkspace.Visibility = Visibility.Collapsed;
        ExperimentalSandboxWorkspace.Visibility = Visibility.Collapsed;
        WespWorkspace.Visibility = Visibility.Collapsed;
    }

    private void AppContainerWorkspaceTab_Click(object sender, RoutedEventArgs e)
    {
        if (JobObjectsWorkspace is null ||
            AppContainerWorkspace is null ||
            ExperimentalSandboxWorkspace is null ||
            WespWorkspace is null)
        {
            return;
        }

        JobObjectsWorkspace.Visibility = Visibility.Collapsed;
        AppContainerWorkspace.Visibility = Visibility.Visible;
        ExperimentalSandboxWorkspace.Visibility = Visibility.Collapsed;
        WespWorkspace.Visibility = Visibility.Collapsed;
        AppContainerWorkspace.PrepareForDisplay();
    }

    private void ExperimentalSandboxWorkspaceTab_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (JobObjectsWorkspace is null ||
            AppContainerWorkspace is null ||
            ExperimentalSandboxWorkspace is null ||
            WespWorkspace is null)
        {
            return;
        }

        JobObjectsWorkspace.Visibility = Visibility.Collapsed;
        AppContainerWorkspace.Visibility = Visibility.Collapsed;
        ExperimentalSandboxWorkspace.Visibility = Visibility.Visible;
        WespWorkspace.Visibility = Visibility.Collapsed;
        ExperimentalSandboxWorkspace.PrepareForDisplay();
    }

    private void WespWorkspaceTab_Click(object sender, RoutedEventArgs e)
    {
        if (JobObjectsWorkspace is null ||
            AppContainerWorkspace is null ||
            ExperimentalSandboxWorkspace is null ||
            WespWorkspace is null)
        {
            return;
        }

        JobObjectsWorkspace.Visibility = Visibility.Collapsed;
        AppContainerWorkspace.Visibility = Visibility.Collapsed;
        ExperimentalSandboxWorkspace.Visibility = Visibility.Collapsed;
        WespWorkspace.Visibility = Visibility.Visible;
        WespWorkspace.PrepareForDisplay();
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

    private async void LaunchProcess_Click(object sender, RoutedEventArgs e) => await LaunchInSelectedJobAsync().ConfigureAwait(true);

    private async void JobDetails_LaunchRequested(object sender, RoutedEventArgs e) => await LaunchInSelectedJobAsync().ConfigureAwait(true);

    private async void AssignRunningProcesses_Click(object sender, RoutedEventArgs e) =>
        await AssignRunningProcessesAsync().ConfigureAwait(true);

    private async void JobDetails_AssignProcessesRequested(object sender, RoutedEventArgs e) =>
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

        _viewModel.CloseJob(job);
    }

    private async Task AssignRunningProcessesAsync()
    {
        if (_viewModel.SelectedJob is not { } target)
        {
            MessageBox.Show(
                this,
                "Choose a target job card before assigning the selected processes.",
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
        if (JobObjectsWorkspace.Visibility == Visibility.Visible &&
            e.Key == Key.Enter &&
            Keyboard.Modifiers == ModifierKeys.Control)
        {
            AssignRunningProcesses_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (AppContainerWorkspace.IsBusy ||
            ExperimentalSandboxWorkspace.IsBusy ||
            WespWorkspace.IsBusy)
        {
            e.Cancel = true;
            var workspace = AppContainerWorkspace.IsBusy
                ? "AppContainer"
                : ExperimentalSandboxWorkspace.IsBusy
                    ? "experimental sandbox"
                    : "WESP Blocking";
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
            e.Cancel = true;
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
            var appContainerTrackedCount = AppContainerWorkspace.TrackedLaunchCount;
            var canHaveUntrackedDescendants =
                AppContainerWorkspace.CanHaveUntrackedDescendants;
            var experimentalTrackedCount =
                ExperimentalSandboxWorkspace.TrackedLaunchCount;
            var wespTrackedCount = WespWorkspace.TrackedLaunchCount;
            var wespHasActiveSession = WespWorkspace.HasActiveSession;
            if (killOnCloseJobs.Length > 0 ||
                liveNotificationJobs.Length > 0 ||
                appContainerTrackedCount > 0 ||
                experimentalTrackedCount > 0 ||
                wespHasActiveSession)
            {
                var warnings = new List<string>();
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

                var answer = MessageBox.Show(
                    this,
                    $"{string.Join("\n\n", warnings)}\n\nClose Shackles anyway?",
                    "Closing Shackles may affect processes",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);
                if (answer != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }

            _closeConfirmed = true;
        }

        Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        AppContainerWorkspace.Dispose();
        ExperimentalSandboxWorkspace.Dispose();
        WespWorkspace.Dispose();
        _viewModel.Dispose();
    }
}
