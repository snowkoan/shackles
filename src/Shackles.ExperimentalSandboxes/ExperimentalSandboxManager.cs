using Shackles.ExperimentalSandboxes.Internal;

namespace Shackles.ExperimentalSandboxes;

public sealed class ExperimentalSandboxManager : IDisposable
{
    private readonly object _gate = new();
    private readonly List<ExperimentalSandbox> _sandboxes = [];
    private readonly Func<ExperimentalSandboxSupport> _probe;
    private ExperimentalSandboxSupport _support;
    private bool _disposed;

    public ExperimentalSandboxManager() : this(SandboxSupportProbe.Probe)
    {
    }

    internal ExperimentalSandboxManager(Func<ExperimentalSandboxSupport> probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        _probe = probe;
        _support = probe();
    }

    public ExperimentalSandboxSupport Support
    {
        get
        {
            lock (_gate)
            {
                return _support;
            }
        }
    }

    public IReadOnlyList<ExperimentalSandbox> Sandboxes
    {
        get
        {
            lock (_gate)
            {
                return _sandboxes.ToArray();
            }
        }
    }

    public ExperimentalSandboxSupport RefreshSupport()
    {
        ThrowIfDisposed();
        var support = _probe();
        lock (_gate)
        {
            ThrowIfDisposed();
            _support = support;
            return support;
        }
    }

    public ExperimentalSandboxCreationResult CreateAndLaunch(
        ExperimentalSandboxOptions sandboxOptions,
        ExperimentalSandboxLaunchOptions launchOptions)
    {
        ArgumentNullException.ThrowIfNull(sandboxOptions);
        ArgumentNullException.ThrowIfNull(launchOptions);
        ThrowIfDisposed();
        var normalized = SandboxPolicyNormalizer.Normalize(sandboxOptions);
        var identity = SandboxIdentity.Create(normalized.UseAppContainer);
        var sandbox = new ExperimentalSandbox(identity, normalized);
        try
        {
            var launch = sandbox.Launch(launchOptions);
            lock (_gate)
            {
                ThrowIfDisposed();
                _sandboxes.Add(sandbox);
                sandbox.Changed += SandboxChanged;
            }

            return new ExperimentalSandboxCreationResult(sandbox, launch);
        }
        catch (Exception exception)
        {
            TrackSandbox(sandbox);
            var cleanup = sandbox.Close();
            if (!cleanup.Completed)
            {
                throw new ExperimentalSandboxException(
                    exception is ExperimentalSandboxException native
                        ? native.Operation
                        : ExperimentalSandboxOperation.CreateProcess,
                    exception.Message + " Cleanup was also incomplete: " +
                    string.Join(" ", cleanup.Warnings),
                    exception is ExperimentalSandboxException sandboxException
                        ? sandboxException.NativeErrorCode
                        : null,
                    exception);
            }

            throw;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        _ = CloseAll();
    }

    public IReadOnlyList<ExperimentalSandboxCleanupResult> CloseAll()
    {
        ExperimentalSandbox[] sandboxes;
        lock (_gate)
        {
            sandboxes = _sandboxes.ToArray();
        }

        var results = new List<ExperimentalSandboxCleanupResult>();
        foreach (var sandbox in sandboxes)
        {
            try
            {
                results.Add(sandbox.Close());
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                results.Add(new ExperimentalSandboxCleanupResult(sandbox.DisplayName, false, [exception.Message]));
            }
        }

        return results;
    }

    internal void TrackSandbox(ExperimentalSandbox sandbox)
    {
        lock (_gate)
        {
            if (!_sandboxes.Contains(sandbox))
            {
                _sandboxes.Add(sandbox);
                sandbox.Changed += SandboxChanged;
            }
        }
    }

    private void SandboxChanged(
        object? sender,
        ExperimentalSandboxChangedEventArgs eventArgs)
    {
        if (!eventArgs.Closed || sender is not ExperimentalSandbox sandbox || !sandbox.CleanupCompleted)
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
