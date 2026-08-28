using Shackles.AppContainers.Internal;

namespace Shackles.AppContainers;

public sealed class AppContainerManager : IDisposable
{
    private readonly object _gate = new();
    private readonly List<AppContainerSandbox> _sandboxes = [];
    private readonly string _journalDirectory;
    private bool _disposed;

    public AppContainerManager()
        : this(CleanupJournal.DefaultDirectory)
    {
    }

    internal AppContainerManager(string journalDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalDirectory);
        _journalDirectory = Path.GetFullPath(journalDirectory);
        RecoveryResult = CleanupJournal.RecoverStale(_journalDirectory);
    }

    public AppContainerRecoveryResult RecoveryResult { get; }

    public IReadOnlyList<AppContainerSandbox> Sandboxes
    {
        get
        {
            lock (_gate)
            {
                return _sandboxes.ToArray();
            }
        }
    }

    public AppContainerCreationResult CreateAndLaunch(
        AppContainerSandboxOptions sandboxOptions,
        AppContainerLaunchOptions launchOptions)
    {
        ArgumentNullException.ThrowIfNull(sandboxOptions);
        ArgumentNullException.ThrowIfNull(launchOptions);
        ThrowIfDisposed();

        var normalizedOptions = NormalizeOptions(sandboxOptions, out var grants);
        var capabilitySids =
            CapabilitySidResolver.Resolve(normalizedOptions.CapabilityNames);
        var identity =
            AppContainerIdentity.Create(normalizedOptions.DisplayName);
        CleanupJournal? journal = null;
        AppContainerSandbox? sandbox = null;
        try
        {
            journal = CleanupJournal.Create(
                _journalDirectory,
                identity,
                normalizedOptions.DisplayName);
            sandbox = new AppContainerSandbox(
                identity,
                capabilitySids,
                normalizedOptions,
                journal);
            journal = null;

            foreach (var grant in grants)
            {
                sandbox.AddInitialGrant(grant);
            }

            var launch = sandbox.Launch(launchOptions);
            if (!sandbox.IsClosed)
            {
                lock (_gate)
                {
                    ThrowIfDisposed();
                    _sandboxes.Add(sandbox);
                    sandbox.Changed += SandboxChanged;
                }
            }

            return new AppContainerCreationResult(sandbox, launch);
        }
        catch (Exception exception)
        {
            if (sandbox is not null)
            {
                var cleanup = sandbox.Close();
                if (!cleanup.Completed)
                {
                    throw new AppContainerException(
                        exception is AppContainerException appContainerException
                            ? appContainerException.Operation
                            : AppContainerOperation.CreateProcess,
                        exception.Message +
                        " Cleanup was also incomplete: " +
                        string.Join(" ", cleanup.Warnings),
                        exception is AppContainerException native
                            ? native.NativeErrorCode
                            : null,
                        exception);
                }
            }
            else
            {
                _ = AppContainerIdentity.TryDelete(identity.ProfileName);
                if (journal is not null)
                {
                    journal.Delete();
                }
            }

            throw;
        }
    }

    public void Dispose()
    {
        AppContainerSandbox[] sandboxes;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            sandboxes = _sandboxes.ToArray();
            _sandboxes.Clear();
            foreach (var sandbox in sandboxes)
            {
                sandbox.Changed -= SandboxChanged;
            }
        }

        foreach (var sandbox in sandboxes)
        {
            sandbox.Dispose();
        }
    }

    private static AppContainerSandboxOptions NormalizeOptions(
        AppContainerSandboxOptions options,
        out IReadOnlyList<TrackedAclGrant> grants)
    {
        var displayName = options.DisplayName?.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException(
                "A sandbox name is required.",
                nameof(options));
        }

        if (displayName.Length > 128 ||
            displayName.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The sandbox name must be 128 characters or fewer and cannot " +
                "contain a null character.",
                nameof(options));
        }

        ArgumentNullException.ThrowIfNull(options.CapabilityNames);
        ArgumentNullException.ThrowIfNull(options.FileSystemGrants);
        ArgumentNullException.ThrowIfNull(options.RegistryGrants);

        var normalizedCapabilities = options.CapabilityNames
            .Select(item => item?.Trim() ?? string.Empty)
            .Where(item => item.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var normalizedGrants = options.FileSystemGrants
            .Select(AclGrantManager.Normalize)
            .Concat(options.RegistryGrants.Select(AclGrantManager.Normalize))
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToArray();
        grants = normalizedGrants;
        return options with
        {
            DisplayName = displayName,
            CapabilityNames = normalizedCapabilities,
            FileSystemGrants = normalizedGrants
                .Where(item => item.Kind == TrackedGrantKind.FileSystem)
                .Select(item => new FileSystemGrant(
                    item.Target,
                    item.IsDirectory,
                    item.FileSystemAccess))
                .ToArray(),
            RegistryGrants = normalizedGrants
                .Where(item => item.Kind == TrackedGrantKind.Registry)
                .Select(item => new RegistryGrant(
                    item.Target,
                    item.RegistryAccess,
                    item.RegistryView))
                .ToArray()
        };
    }

    private void SandboxChanged(
        object? sender,
        AppContainerSandboxChangedEventArgs eventArgs)
    {
        if (!eventArgs.Closed || sender is not AppContainerSandbox sandbox)
        {
            return;
        }

        lock (_gate)
        {
            sandbox.Changed -= SandboxChanged;
            _sandboxes.Remove(sandbox);
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);
}
