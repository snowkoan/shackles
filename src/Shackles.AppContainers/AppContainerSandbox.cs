using Shackles.AppContainers.Internal;

namespace Shackles.AppContainers;

public sealed class AppContainerSandbox : IDisposable
{
    private readonly object _operationGate = new();
    private readonly object _stateGate = new();
    private readonly AppContainerIdentity _identity;
    private readonly IReadOnlyList<byte[]> _capabilitySids;
    private readonly CleanupJournal _journal;
    private readonly IBrokeredFileSystemConfigurator _brokeredFileSystem;
    private readonly Func<string, string?> _deleteProfile;
    private readonly List<TrackedAclGrant> _aclGrants = [];
    private readonly List<TrackedAclGrant> _brokeredFileSystemGrants = [];
    private readonly List<TrackedAppContainerProcess> _processes = [];
    private bool _brokeredFileSystemPolicyMayExist;
    private bool _profileDeleted;
    private bool _closed;
    private bool _closing;
    private AppContainerCleanupResult? _cleanupResult;

    internal AppContainerSandbox(
        AppContainerIdentity identity,
        IReadOnlyList<byte[]> capabilitySids,
        AppContainerSandboxOptions options,
        CleanupJournal journal,
        IBrokeredFileSystemConfigurator brokeredFileSystem,
        Func<string, string?>? deleteProfile = null)
    {
        _identity = identity;
        _capabilitySids = capabilitySids;
        Options = options;
        _journal = journal;
        _brokeredFileSystem = brokeredFileSystem;
        _deleteProfile = deleteProfile ?? AppContainerIdentity.TryDelete;
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

    public AppContainerCleanupResult? CleanupResult
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

            EnsureConfiguredGrants();
            AddTargetGrantIfNeeded(options, warnings);
            if (_brokeredFileSystemPolicyMayExist)
            {
                warnings.Add(
                    "File access uses experimental Brokered File System policy; " +
                    "Shackles did not add file ACL entries for these rules.");
                warnings.Add(
                    "The process token includes the AgenticAppContainer " +
                    "capability required by bfs.sys.");
                warnings.AddRange(_brokeredFileSystem.Support.Warnings);
            }

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
        int[] processIds;
        bool closed;
        lock (_stateGate)
        {
            closed = _closed;
            processIds = _processes
                    .Where(process =>
                    {
                        try
                        {
                            // Close owns the process waits. Keep its remaining
                            // handles visible without blocking a UI snapshot.
                            return _closing || !process.HasExited;
                        }
                        catch
                        {
                            return true;
                        }
                    })
                    .Select(process => process.ProcessId)
                    .ToArray();
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
                catch (Exception exception)
                {
                    warnings.Add(
                        $"Could not terminate directly launched PID " +
                        $"{process.ProcessId}: {exception.Message}");
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
            var canDeleteProfile = false;
            if (!hasRemainingProcesses)
            {
                try
                {
                    canDeleteProfile = ReleaseResourcePolicy(warnings);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    warnings.Add($"Resource cleanup could not complete: {exception.Message}");
                }
            }

            if (canDeleteProfile && !_profileDeleted)
            {
                try
                {
                    var profileWarning = _deleteProfile(ProfileName);
                    if (profileWarning is not null)
                    {
                        warnings.Add(profileWarning);
                    }
                    else
                    {
                        _profileDeleted = true;
                    }
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    warnings.Add($"The Windows profile could not be deleted: {exception.Message}");
                }
            }
            else if (!canDeleteProfile && !_profileDeleted)
            {
                warnings.Add(
                    $"The AppContainer profile '{ProfileName}' was retained so " +
                    "cleanup can be retried.");
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

    private void EnsureConfiguredGrants()
    {
        foreach (var grant in Options.FileSystemGrants)
        {
            AddGrant(AclGrantManager.Normalize(grant));
        }

        foreach (var grant in Options.RegistryGrants)
        {
            AddGrant(AclGrantManager.Normalize(grant));
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
                "Shackles used its existing AppContainer package access instead " +
                "of adding another file-access policy entry.");
            return;
        }

        AddGrant(AclGrantManager.Normalize(new FileSystemGrant(
            targetDirectory,
            IsDirectory: true,
            FileSystemGrantAccess.ReadExecute)));
    }

    private void AddGrant(TrackedAclGrant grant)
    {
        if (grant.Kind == TrackedGrantKind.FileSystem &&
            Options.FileSystemPolicyBackend ==
            AppContainerFileSystemPolicyBackend.BrokeredFileSystem)
        {
            AddBrokeredFileSystemGrant(grant);
            return;
        }

        if (_aclGrants.Any(item =>
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
        _aclGrants.Add(grant);
        AclGrantManager.Apply(grant, _identity.SidBytes);
    }

    private void AddBrokeredFileSystemGrant(TrackedAclGrant grant)
    {
        if (_brokeredFileSystemGrants.Any(item =>
                string.Equals(
                    item.Key,
                    grant.Key,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        if (!_brokeredFileSystemPolicyMayExist)
        {
            // Persist intent before invoking bfscfg. A successful or timed-out
            // native operation can then be cleared after a process crash.
            _journal.MarkBrokeredFileSystemPolicyMayExist();
            _brokeredFileSystemPolicyMayExist = true;
        }

        _brokeredFileSystem.AddPolicy(ProfileName, grant);
        _brokeredFileSystemGrants.Add(grant);
    }

    private bool ReleaseResourcePolicy(List<string> warnings)
    {
        var canDeleteProfile = true;
        if (_brokeredFileSystemPolicyMayExist)
        {
            var warning = _brokeredFileSystem.TryClearPolicy(ProfileName);
            if (warning is null)
            {
                try
                {
                    _journal.MarkBrokeredFileSystemPolicyCleared();
                    _brokeredFileSystemPolicyMayExist = false;
                    _brokeredFileSystemGrants.Clear();
                }
                catch (Exception exception)
                {
                    canDeleteProfile = false;
                    warnings.Add(
                        "BFS policy was cleared, but its cleanup state could " +
                        "not be journaled: " + exception.Message);
                }
            }
            else
            {
                canDeleteProfile = false;
                warnings.Add(warning);
            }
        }

        for (var index = _aclGrants.Count - 1; index >= 0; index--)
        {
            var grant = _aclGrants[index];
            var warning = AclGrantManager.TryRevoke(
                grant,
                _identity.SidBytes);
            if (warning is not null)
            {
                warnings.Add(warning);
                continue;
            }

            try
            {
                _journal.Untrack(grant);
                _aclGrants.RemoveAt(index);
            }
            catch (Exception exception)
            {
                warnings.Add(
                    $"Access was revoked from '{grant.Target}', but its cleanup " +
                    $"journal could not be updated: {exception.Message}");
            }
        }

        return canDeleteProfile && _aclGrants.Count == 0;
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
        lock (_operationGate)
        {
            var removed = false;
            var releaseResourcePolicy = false;
            lock (_stateGate)
            {
                if (!_closing)
                {
                    removed = _processes.Remove(process);
                    releaseResourcePolicy = removed && _processes.Count == 0 && !_closed;
                }
            }

            if (!removed)
            {
                return;
            }

            process.Dispose();
            var warnings = new List<string>();
            if (releaseResourcePolicy)
            {
                _ = ReleaseResourcePolicy(warnings);
            }

            OnChanged(
                closed: false,
                resourcePolicyCleanupAttempted: releaseResourcePolicy,
                warnings);
        }
    }

    private void OnChanged(
        bool closed,
        bool resourcePolicyCleanupAttempted = false,
        IReadOnlyList<string>? cleanupWarnings = null)
    {
        try
        {
            Changed?.Invoke(
                this,
                new AppContainerSandboxChangedEventArgs(
                    closed,
                    resourcePolicyCleanupAttempted,
                    cleanupWarnings ?? Array.Empty<string>()));
        }
        catch
        {
            // A UI observer cannot compromise lifecycle cleanup.
        }
    }
}
