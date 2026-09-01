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
    private readonly List<TrackedAclGrant> _aclGrants = [];
    private readonly List<TrackedAclGrant> _brokeredFileSystemGrants = [];
    private readonly List<TrackedAppContainerProcess> _processes = [];
    private bool _brokeredFileSystemPolicyMayExist;
    private bool _closed;
    private bool _closing;
    private AppContainerCleanupResult? _cleanupResult;

    internal AppContainerSandbox(
        AppContainerIdentity identity,
        IReadOnlyList<byte[]> capabilitySids,
        AppContainerSandboxOptions options,
        CleanupJournal journal,
        IBrokeredFileSystemConfigurator brokeredFileSystem)
    {
        _identity = identity;
        _capabilitySids = capabilitySids;
        Options = options;
        _journal = journal;
        _brokeredFileSystem = brokeredFileSystem;
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
            if (closed)
            {
                processIds = [];
            }
            else
            {
                processIds = _processes
                    .Where(process =>
                    {
                        try
                        {
                            return !process.HasExited;
                        }
                        catch
                        {
                            return true;
                        }
                    })
                    .Select(process => process.ProcessId)
                    .ToArray();
            }
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

            var canDeleteProfile = ReleaseResourcePolicy(warnings);

            if (canDeleteProfile)
            {
                var profileWarning = AppContainerIdentity.TryDelete(ProfileName);
                if (profileWarning is not null)
                {
                    warnings.Add(profileWarning);
                }
            }
            else
            {
                warnings.Add(
                    $"The AppContainer profile '{ProfileName}' was retained so " +
                    "Brokered File System cleanup can be retried on the next run.");
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
                _brokeredFileSystemPolicyMayExist = false;
                _brokeredFileSystemGrants.Clear();
                try
                {
                    _journal.MarkBrokeredFileSystemPolicyCleared();
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

            _aclGrants.RemoveAt(index);
            try
            {
                _journal.Untrack(grant);
            }
            catch (Exception exception)
            {
                warnings.Add(
                    $"Access was revoked from '{grant.Target}', but its cleanup " +
                    $"journal could not be updated: {exception.Message}");
            }
        }

        return canDeleteProfile;
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
                if (!_closing && !_closed)
                {
                    removed = _processes.Remove(process);
                    releaseResourcePolicy = removed && _processes.Count == 0;
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
