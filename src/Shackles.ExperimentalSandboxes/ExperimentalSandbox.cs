using Shackles.ExperimentalSandboxes.Internal;

namespace Shackles.ExperimentalSandboxes;

public sealed class ExperimentalSandbox : IDisposable
{
    private readonly object _operationGate = new();
    private readonly object _stateGate = new();
    private readonly SandboxIdentity _identity;
    private readonly Func<string?> _deleteProfile;
    private readonly List<TrackedSandboxProcess> _processes = [];
    private ExperimentalSandboxOptions _effectiveOptions;
    private bool _profileMayExist;
    private bool _closed;
    private bool _closing;
    private ExperimentalSandboxCleanupResult? _cleanupResult;

    internal ExperimentalSandbox(
        SandboxIdentity identity,
        ExperimentalSandboxOptions options,
        Func<string?>? deleteProfile = null,
        bool profileMayExist = false)
    {
        _identity = identity;
        _effectiveOptions = options;
        _deleteProfile = deleteProfile ?? identity.TryDeleteProfile;
        _profileMayExist = profileMayExist;
    }

    public event EventHandler<ExperimentalSandboxChangedEventArgs>? Changed;

    public string DisplayName => _effectiveOptions.DisplayName;

    public string Identity => _identity.ProfileName;

    public string? AppContainerSid => _identity.Sid;

    public bool IsClosed
    {
        get
        {
            lock (_stateGate)
            {
                return _closed;
            }
        }
    }

    public ExperimentalSandboxCleanupResult? CleanupResult
    {
        get
        {
            lock (_stateGate)
            {
                return _cleanupResult;
            }
        }
    }

    public bool CleanupCompleted => CleanupResult?.Completed == true;

    public ExperimentalSandboxLaunchResult Launch(
        ExperimentalSandboxLaunchOptions launchOptions)
    {
        ArgumentNullException.ThrowIfNull(launchOptions);
        lock (_operationGate)
        {
            lock (_stateGate)
            {
                ObjectDisposedException.ThrowIf(_closed || _closing, this);
            }

            var options = AddLaunchPathRules(_effectiveOptions, launchOptions);
            var warnings = new List<string>
            {
                "Each launch receives its own OS-managed Job Object. Shackles " +
                "tracks the directly launched process; the experimental API does " +
                "not return its internal job handle."
            };
            if (options.UseAppContainer)
            {
                _profileMayExist = true;
            }

            var process = SandboxLauncher.Launch(
                _identity,
                options,
                launchOptions,
                warnings);
            var added = false;
            try
            {
                lock (_stateGate)
                {
                    _effectiveOptions = options;
                    _processes.Add(process);
                    added = true;
                }

                process.StartMonitoring(ProcessExited);
            }
            catch
            {
                if (added)
                {
                    lock (_stateGate)
                    {
                        _processes.Remove(process);
                    }
                }

                _ = process.TryTerminate();
                process.Dispose();
                throw;
            }

            OnChanged(closed: false);
            return process.Result;
        }
    }

    public ExperimentalSandboxSnapshot GetSnapshot()
    {
        var exited = new List<TrackedSandboxProcess>();
        int[] processIds;
        ExperimentalSandboxOptions options;
        bool closed;
        lock (_stateGate)
        {
            closed = _closed;
            options = _effectiveOptions;
            if (!_closing)
            {
                for (var index = _processes.Count - 1; index >= 0; index--)
                {
                    var process = _processes[index];
                    bool hasExited;
                    try
                    {
                        hasExited = process.HasExited;
                    }
                    catch (Exception exception) when (exception is not OutOfMemoryException)
                    {
                        continue;
                    }
                    if (!hasExited)
                    {
                        continue;
                    }

                    _processes.RemoveAt(index);
                    exited.Add(process);
                }

            }

            processIds = _processes.Select(process => process.ProcessId).ToArray();
        }

        foreach (var process in exited)
        {
            process.Dispose();
        }

        return new ExperimentalSandboxSnapshot(
            DisplayName,
            Identity,
            AppContainerSid,
            options,
            processIds,
            DateTimeOffset.UtcNow,
            closed);
    }

    public ExperimentalSandboxCleanupResult Close()
    {
        lock (_operationGate)
        {
            TrackedSandboxProcess[] processes;
            lock (_stateGate)
            {
                if (_cleanupResult is { Completed: true })
                {
                    return _cleanupResult;
                }

                _closing = true;
                _closed = true;
                processes = _processes.ToArray();
            }

            var warnings = new List<string>();
            foreach (var process in processes)
            {
                try
                {
                    var warning = process.TryTerminate();
                    if (warning is not null)
                    {
                        warnings.Add(warning);
                    }
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    warnings.Add($"Could not terminate directly launched PID {process.ProcessId}: {exception.Message}");
                }
            }

            var deadline = Environment.TickCount64 + 3000;
            foreach (var process in processes)
            {
                try
                {
                    var remaining = TimeSpan.FromMilliseconds(Math.Max(0, deadline - Environment.TickCount64));
                    if (!process.WaitForExit(remaining))
                    {
                        warnings.Add(
                            $"Directly launched PID {process.ProcessId} did not " +
                            "exit before cleanup stopped. Retry cleanup after it exits.");
                        continue;
                    }

                    lock (_stateGate)
                    {
                        _processes.Remove(process);
                    }
                    process.Dispose();
                }
                catch (Exception exception)
                {
                    warnings.Add(
                        $"Could not confirm that directly launched PID " +
                        $"{process.ProcessId} exited: {exception.Message}");
                }
            }

            bool hasRemainingProcesses;
            lock (_stateGate)
            {
                hasRemainingProcesses = _processes.Count != 0;
            }
            if (_profileMayExist && !hasRemainingProcesses)
            {
                try
                {
                    var warning = _deleteProfile();
                    if (warning is not null)
                    {
                        warnings.Add(warning + " A descendant may still be using the profile.");
                    }
                    else
                    {
                        _profileMayExist = false;
                    }
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    warnings.Add($"The Windows profile could not be deleted: {exception.Message}");
                }
            }

            var result = new ExperimentalSandboxCleanupResult(
                DisplayName,
                warnings.Count == 0,
                warnings);
            lock (_stateGate)
            {
                _closing = false;
                _cleanupResult = result;
            }

            OnChanged(closed: true);
            return result;
        }
    }

    public void Dispose() => _ = Close();

    private static ExperimentalSandboxOptions AddLaunchPathRules(
        ExperimentalSandboxOptions options,
        ExperimentalSandboxLaunchOptions launchOptions)
    {
        var rules = options.FileSystemRules.ToList();
        if (options.UseAppContainer && launchOptions.IncludeTargetDirectoryReadAccess)
        {
            var executable = Path.GetFullPath(launchOptions.FileName);
            var directory = Path.GetDirectoryName(executable) ??
                throw new ArgumentException(
                    "The executable does not have a parent directory.",
                    nameof(launchOptions));
            AddIfMissing(
                rules,
                new ExperimentalSandboxFileRule(
                    directory,
                    ExperimentalSandboxFileAccess.ReadOnly));
        }

        if (options.UseAppContainer &&
            launchOptions.IncludeWorkingDirectoryWriteAccess &&
            !string.IsNullOrWhiteSpace(launchOptions.WorkingDirectory))
        {
            AddIfMissing(
                rules,
                new ExperimentalSandboxFileRule(
                    launchOptions.WorkingDirectory,
                    ExperimentalSandboxFileAccess.ReadWrite));
        }

        return SandboxPolicyNormalizer.Normalize(
            options with { FileSystemRules = rules });
    }

    private static void AddIfMissing(
        List<ExperimentalSandboxFileRule> rules,
        ExperimentalSandboxFileRule candidate)
    {
        var normalized = SandboxPolicyNormalizer.NormalizeRule(candidate);
        if (!rules.Any(rule => string.Equals(
                rule.Path,
                normalized.Path,
                StringComparison.OrdinalIgnoreCase)))
        {
            rules.Add(normalized);
        }
    }

    private void ProcessExited(TrackedSandboxProcess process)
    {
        var removed = false;
        lock (_stateGate)
        {
            if (!_closing)
            {
                removed = _processes.Remove(process);
            }
        }

        if (removed)
        {
            process.Dispose();
            OnChanged(closed: false);
        }
    }

    private void OnChanged(bool closed)
    {
        try
        {
            Changed?.Invoke(
                this,
                new ExperimentalSandboxChangedEventArgs(closed));
        }
        catch
        {
            // A UI observer cannot compromise process/profile cleanup.
        }
    }
}
