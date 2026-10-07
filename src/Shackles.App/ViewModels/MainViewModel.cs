using System.Collections.ObjectModel;
using System.Diagnostics;
using Shackles.App.Infrastructure;
using Shackles.App.Models;
using Shackles.App.Services;

namespace Shackles.App.ViewModels;

internal sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly IJobControlService _service;
    private readonly Func<IReadOnlyList<ProcessEntry>> _readProcesses;
    private int _privateJobNumber;
    private JobViewModel? _selectedJob;
    private string _statusMessage = "Ready";
    private bool _statusIsError;
    private bool _isDisposed;
    private int _pendingOperations;

    public MainViewModel(IJobControlService service, Func<IReadOnlyList<ProcessEntry>>? readProcesses = null)
    {
        _service = service;
        _readProcesses = readProcesses ?? ReadProcesses;
        Capabilities = service.Capabilities;
    }

    public ObservableCollection<ProcessEntry> Processes { get; } = [];
    public ObservableCollection<JobViewModel> Jobs { get; } = [];
    public JobCapabilitySet Capabilities { get; }

    public JobViewModel? SelectedJob
    {
        get => _selectedJob;
        set
        {
            if (SetProperty(ref _selectedJob, value))
            {
                OnPropertyChanged(nameof(HasSelectedJob));
            }
        }
    }

    public bool HasSelectedJob => SelectedJob is not null;
    public bool HasPendingOperations => _pendingOperations != 0;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool StatusIsError
    {
        get => _statusIsError;
        private set => SetProperty(ref _statusIsError, value);
    }

    public string? LastOpenJobErrorMessage { get; private set; }

    public async Task InitializeAsync() => await RefreshProcessesAsync().ConfigureAwait(true);

    public async Task<JobViewModel?> CreateJobAsync(string? name)
    {
        ThrowIfDisposed();
        using var operation = TrackOperation();
        IJobSession? session = null;
        JobViewModel? job = null;
        try
        {
            session = await Task.Run(() => _service.CreateJob(string.IsNullOrWhiteSpace(name) ? null : name.Trim())).ConfigureAwait(true);
            ThrowIfDisposed();
            var createdNew = session.CreatedNew;
            job = new JobViewModel(session, Capabilities, ++_privateJobNumber);
            session = null;
            Jobs.Add(job);
            SelectedJob = job;
            var successMessage = createdNew
                ? $"Created {job.DisplayName}."
                : $"Opened existing job {job.DisplayName}; the name already existed.";
            await InitializeOpenedJobAsync(job, successMessage).ConfigureAwait(true);
            return job;
        }
        catch (Exception ex)
        {
            CleanupFailedInitialization(job, session);
            SetStatus(ToUserMessage(ex), true);
            return null;
        }
    }

    public async Task<JobViewModel?> OpenJobAsync(string name)
    {
        ThrowIfDisposed();
        using var operation = TrackOperation();
        LastOpenJobErrorMessage = null;
        var normalized = name.Trim();
        var existing = Jobs.FirstOrDefault(job => string.Equals(job.DisplayName, normalized, StringComparison.Ordinal));
        if (existing is not null)
        {
            SelectedJob = existing;
            SetStatus($"{existing.DisplayName} is already open in this session.", false);
            return existing;
        }

        IJobSession? session = null;
        JobViewModel? job = null;
        try
        {
            session = await Task.Run(() => _service.OpenJob(normalized)).ConfigureAwait(true);
            ThrowIfDisposed();
            job = new JobViewModel(session, Capabilities, ++_privateJobNumber);
            session = null;
            Jobs.Add(job);
            SelectedJob = job;
            await InitializeOpenedJobAsync(job, $"Opened named job {job.DisplayName}.").ConfigureAwait(true);
            return job;
        }
        catch (Exception ex)
        {
            CleanupFailedInitialization(job, session);
            LastOpenJobErrorMessage = JobOpenErrorFormatter.Format(normalized, ex);
            SetStatus(LastOpenJobErrorMessage, true);
            return null;
        }
    }

    public async Task<IReadOnlyList<AssignmentOutcome>> AssignProcessesAsync(JobViewModel target, IReadOnlyCollection<ProcessIdentity> processes)
    {
        ThrowIfDisposed();
        try
        {
            SelectedJob = target;
            var result = await target.AssignProcessesAsync(processes).ConfigureAwait(true);
            SetStatus(target.LastOperationMessage, target.LastOperationFailed);
            return result;
        }
        catch (Exception ex)
        {
            SetStatus(ToUserMessage(ex), true);
            return processes.Select(identity =>
            {
                var process = Processes.FirstOrDefault(item => item.ProcessId == identity.ProcessId);
                return new AssignmentOutcome(identity.ProcessId, process?.Name ?? "Unknown", false, ToUserMessage(ex));
            }).ToArray();
        }
    }

    public async Task<LaunchOutcome?> LaunchProcessAsync(JobViewModel target, LaunchRequest request)
    {
        ThrowIfDisposed();
        try
        {
            SelectedJob = target;
            var result = await target.LaunchProcessAsync(request).ConfigureAwait(true);
            var launchMessage = target.LastOperationMessage;
            if (await RefreshProcessesAsync().ConfigureAwait(true))
            {
                SetStatus(launchMessage, false);
            }
            else
            {
                SetStatus($"{launchMessage} {StatusMessage}", false);
            }
            return result;
        }
        catch (Exception ex)
        {
            SetStatus(ToUserMessage(ex), true);
            return null;
        }
    }

    public bool CloseJob(JobViewModel job)
    {
        ThrowIfDisposed();
        var wasSelected = ReferenceEquals(job, SelectedJob);
        var index = Jobs.IndexOf(job);
        if (index < 0)
        {
            return true;
        }

        try { job.Dispose(); }
        catch (Exception ex)
        {
            SetStatus($"Could not close {job.DisplayName}: {ToUserMessage(ex)} The handle is retained; retry closing it.", true);
            return false;
        }

        Jobs.Remove(job);
        if (wasSelected)
        {
            SelectedJob = Jobs.Count == 0 ? null : Jobs[Math.Min(index, Jobs.Count - 1)];
        }

        SetStatus($"Closed the app's handle to {job.DisplayName}.", false);
        return true;
    }

    public async Task<bool> RefreshProcessesAsync()
    {
        ThrowIfDisposed();
        using var operation = TrackOperation();
        try
        {
            var entries = await Task.Run(_readProcesses).ConfigureAwait(true);
            ThrowIfDisposed();
            Processes.Clear();
            foreach (var entry in entries)
            {
                Processes.Add(entry);
            }

            SetStatus($"Found {entries.Count} running processes.", false);
            return true;
        }
        catch (Exception ex)
        {
            SetStatus($"Could not refresh processes: {ToUserMessage(ex)}", true);
            return false;
        }
    }

    private List<ProcessEntry> ReadProcesses()
    {
        var currentId = Environment.ProcessId;
        var result = new List<ProcessEntry>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var id = process.Id;
                    var identityCapture = _service.CaptureProcessIdentity(id);
                    var name = SafeRead(() => process.ProcessName, $"PID {id}");
                    var path = SafeRead<string?>(() => process.MainModule?.FileName, null);
                    var session = SafeRead<int?>(() => process.SessionId, null);
                    var workingSet = SafeRead(() => process.WorkingSet64, -1L);
                    result.Add(new ProcessEntry(
                        id,
                        name,
                        path,
                        session,
                        workingSet,
                        identityCapture.CreationTimeUtcFileTime,
                        identityCapture.Failure,
                        id == currentId));
                }
                catch (InvalidOperationException)
                {
                    // The process exited while the snapshot was being collected.
                }
                catch (ArgumentException)
                {
                    // The process exited while the snapshot was being collected.
                }
            }
        }

        return result;
    }

    private static T SafeRead<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return fallback;
        }
        catch (InvalidOperationException)
        {
            return fallback;
        }
        catch (NotSupportedException)
        {
            return fallback;
        }
    }

    private void SetStatus(string message, bool isError)
    {
        StatusMessage = message;
        StatusIsError = isError;
    }

    private async Task InitializeOpenedJobAsync(JobViewModel job, string successMessage)
    {
        try
        {
            await job.InitializeAsync().ConfigureAwait(true);
            SetStatus(successMessage, false);
        }
        catch (Exception ex)
        {
            SetStatus($"{successMessage} Its state could not be read: {ToUserMessage(ex)} Refresh the job to verify its settings.", false);
        }
    }

    private void CleanupFailedInitialization(JobViewModel? job, IJobSession? session)
    {
        if (job is not null)
        {
            _ = Jobs.Remove(job);
            if (ReferenceEquals(SelectedJob, job))
            {
                SelectedJob = Jobs.FirstOrDefault();
            }

            job.Dispose();
            return;
        }

        session?.Dispose();
    }

    private static string ToUserMessage(Exception exception)
    {
        var message = exception.Message.Trim();
        return string.IsNullOrWhiteSpace(message) ? "Windows rejected the operation." : message;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_isDisposed, this);

    private OperationScope TrackOperation()
    {
        _pendingOperations++;
        OnPropertyChanged(nameof(HasPendingOperations));
        return new OperationScope(() =>
        {
            _pendingOperations--;
            OnPropertyChanged(nameof(HasPendingOperations));
        });
    }

    private sealed class OperationScope(Action finish) : IDisposable
    {
        public void Dispose() => finish();
    }

    public async Task<IReadOnlyList<string>> CloseAllAsync()
    {
        var warnings = new List<string>();
        foreach (var job in Jobs.ToArray())
        {
            try
            {
                await Task.Run(job.Dispose).ConfigureAwait(true);
                Jobs.Remove(job);
            }
            catch (Exception ex)
            {
                warnings.Add($"{job.DisplayName}: {ToUserMessage(ex)}");
            }
        }

        SelectedJob = Jobs.FirstOrDefault();
        return warnings;
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        var errors = new List<Exception>();
        foreach (var job in Jobs.ToArray())
        {
            try { job.Dispose(); Jobs.Remove(job); }
            catch (Exception ex) { errors.Add(ex); }
        }

        SelectedJob = Jobs.FirstOrDefault();
        if (Jobs.Count == 0)
        {
            try { _service.Dispose(); }
            catch (Exception ex) { errors.Add(ex); }
        }

        if (errors.Count != 0)
        {
            throw new AggregateException("Some Job Object resources could not be closed. Retry cleanup.", errors);
        }

        _isDisposed = true;
    }
}
