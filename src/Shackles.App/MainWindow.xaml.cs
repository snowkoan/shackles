using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shackles.App.Dialogs;
using Shackles.App.Models;
using Shackles.App.Services;
using Shackles.App.ViewModels;

namespace Shackles.App;

public sealed partial class MainWindow : Window, IDisposable
{
    private const string ProcessDragFormat = "Shackles.ProcessRows.v1";

    private readonly MainViewModel _viewModel;
    private Point _dragStartPoint;
    private bool _isOpeningNamedJob;
    private bool _closeConfirmed;
    private bool _disposed;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel(new JobControlService());
        DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.InitializeAsync().ConfigureAwait(true);
    }

    private void JobObjectsWorkspaceTab_Click(object sender, RoutedEventArgs e)
    {
        if (JobObjectsWorkspace is null ||
            AppContainerWorkspace is null ||
            ExperimentalSandboxWorkspace is null)
        {
            return;
        }

        JobObjectsWorkspace.Visibility = Visibility.Visible;
        AppContainerWorkspace.Visibility = Visibility.Collapsed;
        ExperimentalSandboxWorkspace.Visibility = Visibility.Collapsed;
    }

    private void AppContainerWorkspaceTab_Click(object sender, RoutedEventArgs e)
    {
        if (JobObjectsWorkspace is null ||
            AppContainerWorkspace is null ||
            ExperimentalSandboxWorkspace is null)
        {
            return;
        }

        JobObjectsWorkspace.Visibility = Visibility.Collapsed;
        AppContainerWorkspace.Visibility = Visibility.Visible;
        ExperimentalSandboxWorkspace.Visibility = Visibility.Collapsed;
        AppContainerWorkspace.PrepareForDisplay();
    }

    private void ExperimentalSandboxWorkspaceTab_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (JobObjectsWorkspace is null ||
            AppContainerWorkspace is null ||
            ExperimentalSandboxWorkspace is null)
        {
            return;
        }

        JobObjectsWorkspace.Visibility = Visibility.Collapsed;
        AppContainerWorkspace.Visibility = Visibility.Collapsed;
        ExperimentalSandboxWorkspace.Visibility = Visibility.Visible;
        ExperimentalSandboxWorkspace.PrepareForDisplay();
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

    private void ProcessList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(ProcessList);
    }

    private void ProcessList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(ProcessList);
        if (Math.Abs(current.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var selectedRows = ProcessList.SelectedItems.OfType<ProcessEntry>().ToArray();
        if (selectedRows.Length == 0)
        {
            return;
        }

        var data = new DataObject();
        data.SetData(ProcessDragFormat, new ProcessDragPayload(selectedRows));
        _ = DragDrop.DoDragDrop(ProcessList, data, DragDropEffects.Move);
    }

    private void JobCard_DragEnter(object sender, DragEventArgs e)
    {
        if (sender is not Border card || !TryGetPayload(e.Data, out _))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        card.BorderBrush = SystemColors.HighlightBrush;
        card.BorderThickness = new Thickness(2);
        e.Handled = true;
    }

    private void JobCard_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border card)
        {
            ResetDropCard(card);
        }
    }

    private async void JobCard_Drop(object sender, DragEventArgs e)
    {
        if (sender is not Border card)
        {
            return;
        }

        ResetDropCard(card);
        if (card.DataContext is not JobViewModel target || !TryGetPayload(e.Data, out var payload))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        JobList.SelectedItem = target;
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
        await ConfirmAndAssignAsync(target, payload.Rows).ConfigureAwait(true);
    }

    private async void AssignSelected_Click(object sender, RoutedEventArgs e)
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

        var selectedRows = ProcessList.SelectedItems.OfType<ProcessEntry>().ToArray();
        if (selectedRows.Length == 0)
        {
            MessageBox.Show(
                this,
                "Select one or more process rows first.",
                "No process selected",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            ProcessList.Focus();
            return;
        }

        await ConfirmAndAssignAsync(target, selectedRows).ConfigureAwait(true);
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
                return attempted;
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
            AssignSelected_Click(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (AppContainerWorkspace.IsBusy || ExperimentalSandboxWorkspace.IsBusy)
        {
            e.Cancel = true;
            var workspace = AppContainerWorkspace.IsBusy
                ? "AppContainer"
                : "experimental sandbox";
            MessageBox.Show(
                this,
                $"Wait for the current {workspace} operation to finish before closing Shackles.",
                "Sandbox operation in progress",
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
            if (killOnCloseJobs.Length > 0 ||
                liveNotificationJobs.Length > 0 ||
                appContainerTrackedCount > 0 ||
                experimentalTrackedCount > 0)
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

    private static bool TryGetPayload(IDataObject data, out ProcessDragPayload payload)
    {
        if (data.GetDataPresent(ProcessDragFormat) && data.GetData(ProcessDragFormat) is ProcessDragPayload value)
        {
            payload = value;
            return true;
        }

        payload = default!;
        return false;
    }

    private static void ResetDropCard(Border card)
    {
        card.ClearValue(Border.BorderBrushProperty);
        card.ClearValue(Border.BorderThicknessProperty);
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
        _viewModel.Dispose();
    }

    private sealed record ProcessDragPayload(IReadOnlyList<ProcessEntry> Rows);
}
