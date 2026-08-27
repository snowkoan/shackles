using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Data;
using Shackles.App.Infrastructure;
using Shackles.App.Models;
using Shackles.App.Services;

namespace Shackles.App.ViewModels;

internal sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly IJobControlService _service;
    private int _privateJobNumber;
    private string _processSearch = string.Empty;
    private JobViewModel? _selectedJob;
    private bool _isRefreshingProcesses;
    private string _statusMessage = "Ready";
    private bool _statusIsError;
    private bool _isDisposed;

    public MainViewModel(IJobControlService service)
    {
        _service = service;
        Capabilities = service.Capabilities;
        ProcessView = CollectionViewSource.GetDefaultView(Processes);
        ProcessView.Filter = FilterProcess;
        ProcessView.SortDescriptions.Add(new SortDescription(nameof(ProcessEntry.Name), ListSortDirection.Ascending));
        ProcessView.SortDescriptions.Add(new SortDescription(nameof(ProcessEntry.ProcessId), ListSortDirection.Ascending));
        RefreshProcessesCommand = new AsyncRelayCommand(RefreshProcessesAsync, () => !IsRefreshingProcesses);
    }

    public ObservableCollection<ProcessEntry> Processes { get; } = [];
    public ObservableCollection<JobViewModel> Jobs { get; } = [];
    public ICollectionView ProcessView { get; }
    public JobCapabilitySet Capabilities { get; }
    public AsyncRelayCommand RefreshProcessesCommand { get; }

    public string ProcessSearch
    {
        get => _processSearch;
        set
        {
            if (SetProperty(ref _processSearch, value))
            {
                ProcessView.Refresh();
            }
        }
    }

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

    public bool IsRefreshingProcesses
    {
        get => _isRefreshingProcesses;
        private set
        {
            if (SetProperty(ref _isRefreshingProcesses, value))
            {
                RefreshProcessesCommand.RaiseCanExecuteChanged();
            }
        }
    }

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
        IJobSession? session = null;
        JobViewModel? job = null;
        try
        {
            session = await Task.Run(() => _service.CreateJob(string.IsNullOrWhiteSpace(name) ? null : name.Trim())).ConfigureAwait(true);
            var createdNew = session.CreatedNew;
            job = new JobViewModel(session, Capabilities, ++_privateJobNumber);
            session = null;
            Jobs.Add(job);
            SelectedJob = job;
            await job.InitializeAsync().ConfigureAwait(true);
            SetStatus(createdNew
                ? $"Created {job.DisplayName}."
                : $"Opened existing job {job.DisplayName}; the name already existed.", false);
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
            job = new JobViewModel(session, Capabilities, ++_privateJobNumber);
            session = null;
            Jobs.Add(job);
            SelectedJob = job;
            await job.InitializeAsync().ConfigureAwait(true);
            SetStatus($"Opened named job {job.DisplayName}.", false);
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
            SetStatus(target.LastOperationMessage, false);
            await RefreshProcessesAsync().ConfigureAwait(true);
            return result;
        }
        catch (Exception ex)
        {
            SetStatus(ToUserMessage(ex), true);
            return null;
        }
    }

    public void CloseJob(JobViewModel job)
    {
        ThrowIfDisposed();
        var wasSelected = ReferenceEquals(job, SelectedJob);
        if (!Jobs.Remove(job))
        {
            return;
        }

        job.Dispose();
        if (wasSelected)
        {
            SelectedJob = Jobs.FirstOrDefault();
        }

        SetStatus($"Closed the app's handle to {job.DisplayName}.", false);
    }

    public async Task RefreshProcessesAsync()
    {
        ThrowIfDisposed();
        IsRefreshingProcesses = true;
        try
        {
            var entries = await Task.Run(ReadProcesses).ConfigureAwait(true);
            Processes.Clear();
            foreach (var entry in entries)
            {
                Processes.Add(entry);
            }

            SetStatus($"Found {entries.Count} running processes.", false);
        }
        catch (Exception ex)
        {
            SetStatus($"Could not refresh processes: {ToUserMessage(ex)}", true);
        }
        finally
        {
            IsRefreshingProcesses = false;
        }
    }

    private bool FilterProcess(object value)
    {
        if (value is not ProcessEntry process || string.IsNullOrWhiteSpace(ProcessSearch))
        {
            return true;
        }

        var query = ProcessSearch.Trim();
        return process.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
               process.ProcessId.ToString(CultureInfo.CurrentCulture).Contains(query, StringComparison.OrdinalIgnoreCase) ||
               (process.ImagePath?.Contains(query, StringComparison.CurrentCultureIgnoreCase) ?? false);
    }

    private IReadOnlyList<ProcessEntry> ReadProcesses()
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

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        foreach (var job in Jobs.ToArray())
        {
            job.Dispose();
        }

        Jobs.Clear();
        _service.Dispose();
    }
}
