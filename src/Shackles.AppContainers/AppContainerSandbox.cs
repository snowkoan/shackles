using Shackles.AppContainers.Internal;

namespace Shackles.AppContainers;

public sealed class AppContainerSandbox : IDisposable
{
    private readonly object _operationGate = new();
    private readonly object _stateGate = new();
    private readonly AppContainerIdentity _identity;
    private readonly IReadOnlyList<byte[]> _capabilitySids;
    private readonly CleanupJournal _journal;
    private readonly List<TrackedAclGrant> _grants = [];
    private readonly List<TrackedAppContainerProcess> _processes = [];
    private bool _closed;
    private bool _closing;
    private AppContainerCleanupResult? _cleanupResult;

    internal AppContainerSandbox(
        AppContainerIdentity identity,
        IReadOnlyList<byte[]> capabilitySids,
        AppContainerSandboxOptions options,
        CleanupJournal journal)
    {
        _identity = identity;
        _capabilitySids = capabilitySids;
        Options = options;
        _journal = journal;
    }

    public event EventHandler<AppContainerSandboxChangedEventArgs>? Changed;

    public string DisplayName => Options.DisplayName;

    public string ProfileName => _identity.ProfileName;

    public string Sid => _identity.Sid;

    public AppContainerSandboxOptions Options { get; }

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

    public AppContainerLaunchResult Launch(AppContainerLaunchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        lock (_operationGate)
        {
            lock (_stateGate)
            {
                ObjectDisposedException.ThrowIf(_closed || _closing, this);
            }

            var warnings = new List<string>();
            if (!Options.RestrictChildProcessCreation)
            {
                warnings.Add(
                    "Child creation is allowed. Shackles tracks and closes only " +
                    "processes launched directly from this workspace; descendants " +
                    "remain inside the AppContainer but are not owned by it.");
            }

            AddTargetGrantIfNeeded(options, warnings);
            var process = AppContainerLauncher.Launch(
                _identity,
                _capabilitySids,
                Options,
                options,
                warnings);
            var added = false;
            try
            {
                lock (_stateGate)
                {
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

    public AppContainerSnapshot GetSnapshot()
    {
        var exited = new List<TrackedAppContainerProcess>();
        int[] processIds;
        bool closed;
        lock (_stateGate)
        {
            closed = _closed;
            if (closed)
            {
                processIds = [];
            }
            else
            {
                for (var index = _processes.Count - 1; index >= 0; index--)
                {
                    var process = _processes[index];
                    try
                    {
                        if (!process.HasExited)
                        {
                            continue;
                        }
                    }
                    catch
                    {
                        continue;
                    }

                    _processes.RemoveAt(index);
                    exited.Add(process);
                }

                processIds = _processes
                    .Select(process => process.ProcessId)
                    .ToArray();
            }
        }

        foreach (var process in exited)
        {
            process.Dispose();
        }

        return new AppContainerSnapshot(
            DisplayName,
            ProfileName,
            Sid,
            Options,
            processIds,
            DateTimeOffset.UtcNow,
            closed);
    }

    public AppContainerCleanupResult Close()
    {
        lock (_operationGate)
        {
            TrackedAppContainerProcess[] processes;
            lock (_stateGate)
            {
                if (_cleanupResult is not null)
                {
                    return _cleanupResult;
                }

                _closing = true;
                processes = _processes.ToArray();
                _processes.Clear();
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
                catch (Exception exception)
                {
                    warnings.Add(
                        $"Could not terminate directly launched PID " +
                        $"{process.ProcessId}: {exception.Message}");
                }
            }

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            foreach (var process in processes)
            {
                try
                {
                    var remaining = deadline - DateTime.UtcNow;
                    if (remaining > TimeSpan.Zero &&
                        !process.WaitForExit(remaining))
                    {
                        warnings.Add(
                            $"Directly launched PID {process.ProcessId} did not " +
                            "exit before cleanup continued.");
                    }
                }
                catch (Exception exception)
                {
                    warnings.Add(
                        $"Could not confirm that directly launched PID " +
                        $"{process.ProcessId} exited: {exception.Message}");
                }
                finally
                {
                    process.Dispose();
                }
            }

            foreach (var grant in _grants.AsEnumerable().Reverse())
            {
                var warning = AclGrantManager.TryRevoke(
                    grant,
                    _identity.SidBytes);
                if (warning is not null)
                {
                    warnings.Add(warning);
                }
            }

            var profileWarning = AppContainerIdentity.TryDelete(ProfileName);
            if (profileWarning is not null)
            {
                warnings.Add(profileWarning);
            }

            if (warnings.Count == 0)
            {
                try
                {
                    _journal.Delete();
                }
                catch (Exception exception)
                {
                    warnings.Add(
                        "Cleanup completed, but the recovery journal could not " +
                        "be removed: " + exception.Message);
                }
            }

            var result = new AppContainerCleanupResult(
                DisplayName,
                warnings.Count == 0,
                warnings);
            lock (_stateGate)
            {
                _closed = true;
                _closing = false;
                _cleanupResult = result;
            }

            OnChanged(closed: true);
            return result;
        }
    }

    public void Dispose() => _ = Close();

    internal void AddInitialGrant(TrackedAclGrant grant)
    {
        lock (_operationGate)
        {
            AddGrant(grant);
        }
    }

    private void AddTargetGrantIfNeeded(
        AppContainerLaunchOptions options,
        List<string> warnings)
    {
        if (!options.IncludeTargetDirectoryGrant)
        {
            return;
        }

        var executable = Path.GetFullPath(options.FileName);
        var targetDirectory = Path.GetDirectoryName(executable) ??
            throw new ArgumentException(
                "The executable does not have a parent directory.",
                nameof(options));
        if (IsSystemManagedDirectory(targetDirectory))
        {
            warnings.Add(
                "The executable is in a Windows-managed program folder, so " +
                "Shackles used its existing package access instead of changing " +
                "that folder's ACL.");
            return;
        }

        AddGrant(AclGrantManager.Normalize(new FileSystemGrant(
            targetDirectory,
            IsDirectory: true,
            FileSystemGrantAccess.ReadExecute)));
    }

    private void AddGrant(TrackedAclGrant grant)
    {
        if (_grants.Any(item =>
                string.Equals(
                    item.Key,
                    grant.Key,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        // Persist intent first. If the process dies between ACL mutation and the
        // next managed statement, the next Shackles run still knows which unique
        // SID to revoke.
        _journal.Track(grant);
        _grants.Add(grant);
        AclGrantManager.Apply(grant, _identity.SidBytes);
    }

    private static bool IsSystemManagedDirectory(string path)
    {
        var candidates = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };
        var fullPath =
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(candidate =>
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate)))
            .Any(candidate =>
                fullPath.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
                fullPath.StartsWith(
                    candidate + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase));
    }

    private void ProcessExited(TrackedAppContainerProcess process)
    {
        var removed = false;
        lock (_stateGate)
        {
            if (!_closing && !_closed)
            {
                removed = _processes.Remove(process);
            }
        }

        if (!removed)
        {
            return;
        }

        process.Dispose();
        OnChanged(closed: false);
    }

    private void OnChanged(bool closed)
    {
        try
        {
            Changed?.Invoke(
                this,
                new AppContainerSandboxChangedEventArgs(closed));
        }
        catch
        {
            // A UI observer cannot compromise lifecycle cleanup.
        }
    }
}
