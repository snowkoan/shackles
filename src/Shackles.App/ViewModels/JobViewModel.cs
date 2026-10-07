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
    private string _restrictionSummary = "Job state has not been read";
    private bool _killOnCloseConfigured;
    private bool _liveNotificationOwnerRequiredOnClose;
    private long _editorRevision;
    private bool _isSnapshotStale = true;
    private bool _hasSnapshot;

    public JobViewModel(IJobSession session, JobCapabilitySet capabilities, int privateJobNumber)
    {
        _session = session;
        Capabilities = capabilities;
        _privateDisplayName = $"Private job {privateJobNumber}";
        CanPostEndOfJobNotification = session.HasOwnedNotificationDelivery;
        NotificationDeliveryBadge = CanPostEndOfJobNotification ? "OWNED LIVE PORT" : "SAMPLED ONLY";
        NotificationDeliveryDescription = CanPostEndOfJobNotification
            ? "Shackles owns and consumes this job's completion port; PostNotification is safe to select."
            : "This opened handle does not own a completion port. An existing notification action can be preserved, but a new one requires an owned live port. Windows terminates members if the time limit expires without a port.";
        Editor = new RestrictionEditorViewModel(CanPostEndOfJobNotification);
        RefreshCommand = new AsyncRelayCommand(RefreshFromCommandAsync, () => !IsBusy);
        ApplyCommand = new AsyncRelayCommand(ApplyFromCommandAsync, () => CanEditDraft && Editor.IsDirty);
        RevertCommand = new AsyncRelayCommand(RevertFromCommandAsync, () => !IsBusy && Editor.IsDirty);
        Editor.DraftChanged += (_, _) =>
        {
            Interlocked.Increment(ref _editorRevision);
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
        ? "Live Windows job messages received by this handle appear here. This in-memory history is cleared when the job tab closes."
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
                OnPropertyChanged(nameof(CanEditDraft));
                OnPropertyChanged(nameof(CanOperate));
                RefreshCommand.RaiseCanExecuteChanged();
                ApplyCommand.RaiseCanExecuteChanged();
                RevertCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanEditDraft => !IsBusy && HasSnapshot;
    public bool CanOperate => !IsBusy;

    public bool HasSnapshot
    {
        get => _hasSnapshot;
        private set
        {
            if (SetProperty(ref _hasSnapshot, value))
            {
                OnPropertyChanged(nameof(CanEditDraft));
                ApplyCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsSnapshotStale
    {
        get => _isSnapshotStale;
        private set => SetProperty(ref _isSnapshotStale, value);
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
        var requestedProcesses = processes.ToArray();
        if (requestedProcesses.Length == 0)
        {
            return Array.Empty<AssignmentOutcome>();
        }

        return await RunExclusiveAsync(async () =>
        {
            var outcomes = await Task.Run(() => _session.AssignProcesses(requestedProcesses)).ConfigureAwait(true);
            var successCount = outcomes.Count(item => item.Succeeded);
            LastOperationFailed = successCount != outcomes.Count;
            LastOperationMessage = successCount == outcomes.Count
                ? $"Assigned {successCount} process{(successCount == 1 ? string.Empty : "es")}."
                : $"Assigned {successCount} of {outcomes.Count} processes; review the results.";
            await RefreshAfterMutationAsync(reloadEditor: false).ConfigureAwait(true);
            return outcomes;
        }).ConfigureAwait(true);
    }

    public async Task<LaunchOutcome> LaunchProcessAsync(LaunchRequest request)
    {
        var capturedRequest = request with { Arguments = request.Arguments.ToArray() };
        return await RunExclusiveAsync(async () =>
        {
            var outcome = await Task.Run(() => _session.LaunchProcess(capturedRequest)).ConfigureAwait(true);
            LastOperationFailed = false;
            LastOperationMessage = $"Launched {outcome.ProcessName} (PID {outcome.ProcessId}) inside the job.";
            await RefreshAfterMutationAsync(reloadEditor: false).ConfigureAwait(true);
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

    public async Task ApplyAsync()
    {
        await RunExclusiveAsync(async () =>
        {
            if (!HasSnapshot)
            {
                LastOperationFailed = true;
                LastOperationMessage = "Refresh the job successfully before editing or applying its restrictions.";
                return;
            }

            if (!Editor.TryBuild(out var profile))
            {
                LastOperationFailed = true;
                LastOperationMessage = "Correct the validation error before applying changes.";
                return;
            }

            var editorRevision = Volatile.Read(ref _editorRevision);
            // A failure can leave earlier native information classes applied. Keep close
            // warnings conservative until a successful read-back confirms the settings.
            KillOnCloseConfigured |= profile.HardLimits.KillOnJobClose;
            LiveNotificationOwnerRequiredOnClose |= RequiresLiveNotificationOwner(profile);
            await Task.Run(() => _session.ApplyRestrictions(profile)).ConfigureAwait(true);
            LastOperationFailed = false;
            LastOperationMessage = "Restrictions accepted by Windows.";
            if (await RefreshAfterMutationAsync(reloadEditor: true, editorRevision).ConfigureAwait(true))
            {
                LastOperationMessage = Editor.IsDirty
                    ? "Restrictions applied and read back from Windows. Newer unsaved edits were preserved."
                    : "Restrictions applied and read back from Windows.";
            }
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
            await RevertAsync().ConfigureAwait(true);
        }
        catch
        {
            // RunExclusiveAsync has already converted the exception into a user-visible status.
        }
    }

    public async Task RevertAsync()
    {
        await RunExclusiveAsync(async () =>
        {
            await RefreshCoreAsync(reloadEditor: true).ConfigureAwait(true);
            LastOperationFailed = false;
            LastOperationMessage = Editor.IsDirty
                ? "Job state refreshed. Newer unsaved edits were preserved."
                : "Unsaved edits reverted to the current Windows job state.";
        }).ConfigureAwait(true);
    }

    private async Task<bool> RefreshAfterMutationAsync(bool reloadEditor, long? editorRevision = null)
    {
        try
        {
            await RefreshCoreAsync(reloadEditor, editorRevision).ConfigureAwait(true);
            return true;
        }
        catch (Exception ex)
        {
            IsSnapshotStale = true;
            LastOperationMessage += $" Job state could not be refreshed: {ToUserMessage(ex)} Displayed values may be outdated; refresh to verify them.";
            return false;
        }
    }

    private async Task RefreshCoreAsync(bool reloadEditor, long? editorRevision = null)
    {
        var expectedRevision = editorRevision ?? Volatile.Read(ref _editorRevision);
        var snapshot = await Task.Run(_session.GetSnapshot).ConfigureAwait(true);
        HasSnapshot = true;
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

        if (reloadEditor && expectedRevision == Volatile.Read(ref _editorRevision))
        {
            Editor.Load(snapshot.Restrictions);
        }

        RestrictionSummary = BuildRestrictionSummary(snapshot.Restrictions);
        KillOnCloseConfigured = snapshot.Restrictions.HardLimits.KillOnJobClose;
        LiveNotificationOwnerRequiredOnClose = RequiresLiveNotificationOwner(snapshot.Restrictions);
        IsSnapshotStale = false;
        OnPropertyChanged(nameof(MemberCount));
        OnPropertyChanged(nameof(MemberCountDisplay));
    }

    private bool RequiresLiveNotificationOwner(RestrictionProfile profile) =>
        CanPostEndOfJobNotification && profile.EndAction == JobEndAction.PostNotification &&
        profile.HardLimits.PerJobUserTimeLimit.HasValue;

    private Task<bool> RunExclusiveAsync(Func<Task> action) => RunExclusiveAsync(async () =>
    {
        await action().ConfigureAwait(true);
        return true;
    });

    private async Task<T> RunExclusiveAsync<T>(Func<Task<T>> action)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync().ConfigureAwait(true);
        try
        {
            ThrowIfDisposed();
            IsBusy = true;
            return await action().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            IsSnapshotStale = true;
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

    internal static string BuildRestrictionSummary(RestrictionProfile profile)
    {
        var parts = new List<string>();
        var hard = profile.HardLimits;
        if (hard.KillOnJobClose) parts.Add("Terminate on last handle close");
        if (hard.BreakawayAllowed) parts.Add("Breakaway allowed");
        if (hard.SilentBreakawayAllowed) parts.Add("Silent breakaway allowed");
        if (hard.DieOnUnhandledException) parts.Add("Terminate on unhandled exception");
        if (hard.ActiveProcessLimit is { } count) parts.Add($"Process limit {count}");
        if (hard.PerProcessUserTimeLimit is { } processTime) parts.Add($"Process CPU time {Seconds(processTime)} s");
        if (hard.PerJobUserTimeLimit is { } jobTime) parts.Add($"Job CPU time {Seconds(jobTime)} s");
        if (hard.ProcessMemoryLimitBytes is { } processMemory) parts.Add($"Process memory {Bytes(processMemory)}");
        if (hard.JobMemoryLimitBytes is { } jobMemory) parts.Add($"Job memory {Bytes(jobMemory)}");
        if (hard.MinimumWorkingSetBytes is { } minimumWorkingSet) parts.Add($"Minimum working set {Bytes(minimumWorkingSet)}");
        if (hard.MaximumWorkingSetBytes is { } maximumWorkingSet) parts.Add($"Maximum working set {Bytes(maximumWorkingSet)}");
        if (hard.AffinityMask is { } affinity) parts.Add($"Affinity 0x{affinity:X}");
        if (hard.SubsetAffinityAllowed) parts.Add("Subset affinity allowed");
        if (hard.PriorityClass is { } priority) parts.Add($"Priority {priority}");
        if (hard.SchedulingClass is { } scheduling) parts.Add($"Scheduling class {scheduling}");
        if (profile.Cpu.UsesUnsupportedPerProcessorCaps) parts.Add("Per-processor CPU caps (preserved)");
        else
        {
            switch (profile.Cpu.Mode)
            {
                case CpuControlMode.Rate: parts.Add($"CPU rate {profile.Cpu.RatePercent:G}%"); break;
                case CpuControlMode.HardCap: parts.Add($"CPU cap {profile.Cpu.RatePercent:G}%"); break;
                case CpuControlMode.Weight: parts.Add($"CPU weight {profile.Cpu.Weight}"); break;
                case CpuControlMode.MinimumMaximum: parts.Add($"CPU range {profile.Cpu.MinimumPercent:G}–{profile.Cpu.MaximumPercent:G}%"); break;
            }
        }
        if (profile.Cpu.Notify) parts.Add("CPU notifications");
        if (profile.Network.ExactMaximumBandwidthBytesPerSecond is { } upload)
            parts.Add($"Upload {ByteRate(upload)}");
        else if (profile.Network.MaximumBandwidthMegabitsPerSecond is { } megabits)
            parts.Add($"Upload {megabits / 8:G} MB/s");
        if (profile.Network.DscpTag is { } dscp) parts.Add($"DSCP {dscp}");
        if (profile.UiRestrictions != UiRestrictionFlags.None) parts.Add($"UI restrictions: {profile.UiRestrictions}");
        if (profile.ProcessorGroups.Count != 0) parts.Add($"Processor groups: {string.Join(", ", profile.ProcessorGroups.Select(group => $"{group.Group}:0x{group.Mask:X}"))}");
        if (profile.EndAction == JobEndAction.PostNotification) parts.Add("Notify at end of job time");
        var notification = profile.Notifications;
        if (notification.PerJobUserTime is { } notifyTime) parts.Add($"Notify at {Seconds(notifyTime)} s job CPU time");
        if (notification.JobMemoryBytes is { } notifyMemory) parts.Add($"Notify above {Bytes(notifyMemory)} job memory");
        if (notification.JobLowMemoryBytes is { } lowMemory) parts.Add($"Notify below {Bytes(lowMemory)} job memory");
        if (notification.IoReadBytes is { } readBytes) parts.Add($"Notify at {Bytes(readBytes)} read");
        if (notification.IoWriteBytes is { } writeBytes) parts.Add($"Notify at {Bytes(writeBytes)} written");
        if (notification.CpuTolerance != RateTolerance.None) parts.Add($"CPU notification tolerance {notification.CpuTolerance} / {notification.CpuToleranceInterval}");
        if (notification.IoTolerance != RateTolerance.None) parts.Add($"I/O notification tolerance {notification.IoTolerance} / {notification.IoToleranceInterval}");
        if (notification.NetworkTolerance != RateTolerance.None) parts.Add($"Network notification tolerance {notification.NetworkTolerance} / {notification.NetworkToleranceInterval}");
        return parts.Count == 0 ? "No configured restrictions" : string.Join(" · ", parts);
    }

    private static string Seconds(TimeSpan value) => ((decimal)value.Ticks / TimeSpan.TicksPerSecond).ToString("0.#######", System.Globalization.CultureInfo.CurrentCulture);
    private static string Bytes(ulong value) => value >= 1_048_576 ? $"{value / 1_048_576m:0.###} MiB" : $"{value} B";
    private static string ByteRate(ulong value) => value >= 1_000_000 ? $"{value / 1_000_000m:G29} MB/s" : value >= 1_000 ? $"{value / 1_000m:G29} kB/s" : $"{value} B/s";

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

        _session.Dispose();
        _isDisposed = true;
        _session.NotificationReceived -= SessionNotificationReceived;
        ResetPendingNotifications();
        _operationGate.Dispose();
    }
}

internal sealed record JobMemberViewModel(int ProcessId, string Name, string State);
