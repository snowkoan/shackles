using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using Shackles.App.Infrastructure;
using Shackles.App.Models;
using Shackles.App.Services;

namespace Shackles.App.ViewModels;

internal sealed class JobViewModel : ObservableObject, IDisposable
{
    private const int MaximumRetainedNotifications = 200;

    private readonly IJobSession _session;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly string _privateDisplayName;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly object _notificationQueueGate = new();
    private readonly Queue<LiveJobNotificationDisplay> _pendingNotifications = new();
    private JobAccountingDisplay _accounting = JobAccountingDisplay.Empty;
    private bool _isBusy;
    private volatile bool _isDisposed;
    private bool _notificationDrainScheduled;
    private string _lastOperationMessage = "Ready";
    private bool _lastOperationFailed;
    private string _restrictionSummary = "No configured hard limits";
    private bool _killOnCloseConfigured;
    private bool _liveNotificationOwnerRequiredOnClose;

    public JobViewModel(IJobSession session, JobCapabilitySet capabilities, int privateJobNumber)
    {
        _session = session;
        Capabilities = capabilities;
        _privateDisplayName = $"Private job {privateJobNumber}";
        CanPostEndOfJobNotification = session.HasOwnedNotificationDelivery;
        NotificationDeliveryBadge = CanPostEndOfJobNotification ? "OWNED LIVE PORT" : "SAMPLED ONLY";
        NotificationDeliveryDescription = CanPostEndOfJobNotification
            ? "Shackles owns and consumes this job's completion port; PostNotification is safe to select."
            : "This opened handle does not own a completion port. PostNotification is unavailable because Windows could terminate members instead of posting.";
        Editor = new RestrictionEditorViewModel(CanPostEndOfJobNotification);
        RefreshCommand = new AsyncRelayCommand(RefreshFromCommandAsync, () => !IsBusy);
        ApplyCommand = new AsyncRelayCommand(ApplyFromCommandAsync, () => !IsBusy && Editor.IsDirty);
        RevertCommand = new AsyncRelayCommand(RevertFromCommandAsync, () => !IsBusy && Editor.IsDirty);
        Editor.DraftChanged += (_, _) =>
        {
            ApplyCommand.RaiseCanExecuteChanged();
            RevertCommand.RaiseCanExecuteChanged();
        };
        _session.NotificationReceived += SessionNotificationReceived;
    }

    public JobCapabilitySet Capabilities { get; }
    public RestrictionEditorViewModel Editor { get; }
    public bool CanPostEndOfJobNotification { get; }
    public string NotificationDeliveryBadge { get; }
    public string NotificationDeliveryDescription { get; }
    public string LiveNotificationDescription => CanPostEndOfJobNotification
        ? "Live Windows job messages received by this handle appear here. This in-memory history is cleared when the job card closes."
        : "No live completion-port stream is attached to this opened job. Use the sampled violation state below.";
    public ObservableCollection<JobMemberViewModel> Members { get; } = [];
    public ObservableCollection<LimitViolationDisplay> LimitViolations { get; } = [];
    public ObservableCollection<LiveJobNotificationDisplay> LiveNotifications { get; } = [];
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand ApplyCommand { get; }
    public AsyncRelayCommand RevertCommand { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(_session.Name) ? _privateDisplayName : _session.Name;
    public string OriginBadge => _session.CreatedNew ? "CREATED" : "OPENED";
    public string OriginDescription => _session.CreatedNew
        ? "This handle created the job object."
        : "This handle opened an existing named job object.";
    public int MemberCount => Members.Count;
    public string MemberCountDisplay => MemberCount == 1 ? "1 process" : $"{MemberCount} processes";

    public JobAccountingDisplay Accounting
    {
        get => _accounting;
        private set => SetProperty(ref _accounting, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RefreshCommand.RaiseCanExecuteChanged();
                ApplyCommand.RaiseCanExecuteChanged();
                RevertCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string LastOperationMessage
    {
        get => _lastOperationMessage;
        private set => SetProperty(ref _lastOperationMessage, value);
    }

    public bool LastOperationFailed
    {
        get => _lastOperationFailed;
        private set => SetProperty(ref _lastOperationFailed, value);
    }

    public string RestrictionSummary
    {
        get => _restrictionSummary;
        private set => SetProperty(ref _restrictionSummary, value);
    }

    public bool KillOnCloseConfigured
    {
        get => _killOnCloseConfigured;
        private set => SetProperty(ref _killOnCloseConfigured, value);
    }

    public bool LiveNotificationOwnerRequiredOnClose
    {
        get => _liveNotificationOwnerRequiredOnClose;
        private set => SetProperty(ref _liveNotificationOwnerRequiredOnClose, value);
    }

    public async Task InitializeAsync() => await RefreshAsync().ConfigureAwait(true);

    public async Task<IReadOnlyList<AssignmentOutcome>> AssignProcessesAsync(IReadOnlyCollection<ProcessIdentity> processes)
    {
        if (processes.Count == 0)
        {
            return Array.Empty<AssignmentOutcome>();
        }

        return await RunExclusiveAsync(async () =>
        {
            var outcomes = await Task.Run(() => _session.AssignProcesses(processes)).ConfigureAwait(true);
            var successCount = outcomes.Count(item => item.Succeeded);
            LastOperationFailed = successCount != outcomes.Count;
            LastOperationMessage = successCount == outcomes.Count
                ? $"Assigned {successCount} process{(successCount == 1 ? string.Empty : "es")}."
                : $"Assigned {successCount} of {outcomes.Count} processes; review the results.";
            await RefreshCoreAsync(reloadEditor: false).ConfigureAwait(true);
            return outcomes;
        }).ConfigureAwait(true);
    }

    public async Task<LaunchOutcome> LaunchProcessAsync(LaunchRequest request)
    {
        return await RunExclusiveAsync(async () =>
        {
            var outcome = await Task.Run(() => _session.LaunchProcess(request)).ConfigureAwait(true);
            LastOperationFailed = false;
            LastOperationMessage = $"Launched {outcome.ProcessName} (PID {outcome.ProcessId}) inside the job.";
            await RefreshCoreAsync(reloadEditor: false).ConfigureAwait(true);
            return outcome;
        }).ConfigureAwait(true);
    }

    public async Task RefreshAsync()
    {
        await RunExclusiveAsync(async () =>
        {
            await RefreshCoreAsync(reloadEditor: !Editor.IsDirty).ConfigureAwait(true);
            LastOperationFailed = false;
            LastOperationMessage = Editor.IsDirty
                ? "Membership refreshed. Unsaved restriction edits were preserved."
                : "Job state refreshed.";
        }).ConfigureAwait(true);
    }

    private async Task ApplyAsync()
    {
        if (!Editor.TryBuild(out var profile))
        {
            LastOperationFailed = true;
            LastOperationMessage = "Correct the validation error before applying changes.";
            return;
        }

        await RunExclusiveAsync(async () =>
        {
            await Task.Run(() => _session.ApplyRestrictions(profile)).ConfigureAwait(true);
            await RefreshCoreAsync(reloadEditor: true).ConfigureAwait(true);
            LastOperationFailed = false;
            LastOperationMessage = "Restrictions applied and read back from Windows.";
        }).ConfigureAwait(true);
    }

    private async Task RefreshFromCommandAsync()
    {
        try
        {
            await RefreshAsync().ConfigureAwait(true);
        }
        catch
        {
            // RunExclusiveAsync has already converted the exception into a user-visible status.
        }
    }

    private async Task ApplyFromCommandAsync()
    {
        try
        {
            await ApplyAsync().ConfigureAwait(true);
        }
        catch
        {
            // SetInformationJobObject is not transactional across information classes.
            // Refresh membership and safety-sensitive read-back while preserving the draft.
            var applyError = LastOperationMessage;
            try
            {
                await RefreshAsync().ConfigureAwait(true);
            }
            catch
            {
                // Keep the original apply failure, which is more actionable.
            }

            LastOperationFailed = true;
            LastOperationMessage = $"{applyError} Earlier information classes may already have been applied; current state was refreshed where possible.";
        }
    }

    private async Task RevertFromCommandAsync()
    {
        try
        {
            await RunExclusiveAsync(async () =>
            {
                await RefreshCoreAsync(reloadEditor: true).ConfigureAwait(true);
                LastOperationFailed = false;
                LastOperationMessage = "Unsaved edits reverted to the current Windows job state.";
            }).ConfigureAwait(true);
        }
        catch
        {
            // RunExclusiveAsync has already converted the exception into a user-visible status.
        }
    }

    private async Task RefreshCoreAsync(bool reloadEditor)
    {
        var snapshot = await Task.Run(_session.GetSnapshot).ConfigureAwait(true);
        Accounting = snapshot.Accounting;

        Members.Clear();
        foreach (var processId in snapshot.ProcessIds.Order())
        {
            Members.Add(DescribeProcess(processId));
        }

        LimitViolations.Clear();
        foreach (var violation in snapshot.LimitViolations.OrderByDescending(item => item.ObservedAt))
        {
            LimitViolations.Add(violation);
        }

        if (reloadEditor)
        {
            Editor.Load(snapshot.Restrictions);
        }

        RestrictionSummary = BuildRestrictionSummary(snapshot.Restrictions);
        KillOnCloseConfigured = snapshot.Restrictions.HardLimits.KillOnJobClose;
        LiveNotificationOwnerRequiredOnClose =
            CanPostEndOfJobNotification &&
            snapshot.Restrictions.EndAction == JobEndAction.PostNotification &&
            snapshot.Restrictions.HardLimits.PerJobUserTimeLimit.HasValue;
        OnPropertyChanged(nameof(MemberCount));
        OnPropertyChanged(nameof(MemberCountDisplay));
    }

    private async Task RunExclusiveAsync(Func<Task> action)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync().ConfigureAwait(true);
        IsBusy = true;
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            LastOperationFailed = true;
            LastOperationMessage = ToUserMessage(ex);
            throw;
        }
        finally
        {
            IsBusy = false;
            _operationGate.Release();
        }
    }

    private async Task<T> RunExclusiveAsync<T>(Func<Task<T>> action)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync().ConfigureAwait(true);
        IsBusy = true;
        try
        {
            return await action().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            LastOperationFailed = true;
            LastOperationMessage = ToUserMessage(ex);
            throw;
        }
        finally
        {
            IsBusy = false;
            _operationGate.Release();
        }
    }

    private static JobMemberViewModel DescribeProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return new JobMemberViewModel(processId, process.ProcessName, "Running");
        }
        catch (ArgumentException)
        {
            return new JobMemberViewModel(processId, "Process exited", "Exited");
        }
        catch (InvalidOperationException)
        {
            return new JobMemberViewModel(processId, "Unavailable", "Unavailable");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return new JobMemberViewModel(processId, "Protected process", "Access denied");
        }
    }

    private static string BuildRestrictionSummary(RestrictionProfile profile)
    {
        var hardCount = 0;
        var hard = profile.HardLimits;
        if (hard.KillOnJobClose) hardCount++;
        if (hard.ActiveProcessLimit.HasValue) hardCount++;
        if (hard.PerProcessUserTimeLimit.HasValue || hard.PerJobUserTimeLimit.HasValue) hardCount++;
        if (hard.ProcessMemoryLimitBytes.HasValue || hard.JobMemoryLimitBytes.HasValue) hardCount++;
        if (hard.MinimumWorkingSetBytes.HasValue || hard.MaximumWorkingSetBytes.HasValue) hardCount++;
        if (hard.AffinityMask.HasValue || hard.SubsetAffinityAllowed || hard.PriorityClass.HasValue || hard.SchedulingClass.HasValue) hardCount++;
        if (profile.Cpu.Mode != CpuControlMode.Disabled || profile.Cpu.UsesUnsupportedPerProcessorCaps) hardCount++;
        if (profile.Network.MaximumBandwidthMegabitsPerSecond.HasValue || profile.Network.DscpTag.HasValue) hardCount++;
        if (profile.UiRestrictions != UiRestrictionFlags.None) hardCount++;

        var notificationCount = 0;
        var notification = profile.Notifications;
        if (notification.PerJobUserTime.HasValue) notificationCount++;
        if (notification.JobMemoryBytes.HasValue || notification.JobLowMemoryBytes.HasValue) notificationCount++;
        if (notification.IoReadBytes.HasValue || notification.IoWriteBytes.HasValue) notificationCount++;
        if (notification.CpuTolerance != RateTolerance.None || notification.IoTolerance != RateTolerance.None || notification.NetworkTolerance != RateTolerance.None) notificationCount++;

        if (hardCount == 0 && notificationCount == 0)
        {
            return "No configured restrictions";
        }

        return $"{hardCount} hard/control · {notificationCount} notification-only";
    }

    private static string ToUserMessage(Exception exception)
    {
        var message = exception.Message.Trim();
        return string.IsNullOrWhiteSpace(message) ? "Windows rejected the operation." : message;
    }

    private void SessionNotificationReceived(object? sender, LiveJobNotificationDisplay notification)
    {
        lock (_notificationQueueGate)
        {
            if (_isDisposed)
            {
                return;
            }

            if (_pendingNotifications.Count >= MaximumRetainedNotifications)
            {
                _ = _pendingNotifications.Dequeue();
            }

            _pendingNotifications.Enqueue(notification);
            if (_notificationDrainScheduled)
            {
                return;
            }

            _notificationDrainScheduled = true;
        }

        try
        {
            if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
            {
                ResetPendingNotifications();
                return;
            }

            _ = _dispatcher.BeginInvoke(DispatcherPriority.Background, DrainPendingNotifications);
        }
        catch (InvalidOperationException)
        {
            ResetPendingNotifications();
        }
        catch (TaskCanceledException)
        {
            ResetPendingNotifications();
        }
    }

    private void DrainPendingNotifications()
    {
        LiveJobNotificationDisplay[] pending;
        lock (_notificationQueueGate)
        {
            if (_isDisposed)
            {
                _pendingNotifications.Clear();
                _notificationDrainScheduled = false;
                return;
            }

            pending = _pendingNotifications.ToArray();
            _pendingNotifications.Clear();
            _notificationDrainScheduled = false;
        }

        foreach (var notification in pending)
        {
            LiveNotifications.Insert(0, notification);
            if (LiveNotifications.Count > MaximumRetainedNotifications)
            {
                LiveNotifications.RemoveAt(LiveNotifications.Count - 1);
            }
        }
    }

    private void ResetPendingNotifications()
    {
        lock (_notificationQueueGate)
        {
            _pendingNotifications.Clear();
            _notificationDrainScheduled = false;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _session.NotificationReceived -= SessionNotificationReceived;
        ResetPendingNotifications();
        _session.Dispose();
        _operationGate.Dispose();
    }
}

internal sealed record JobMemberViewModel(int ProcessId, string Name, string State);
