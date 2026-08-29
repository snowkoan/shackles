using Shackles.ExperimentalSandboxes.Internal;

namespace Shackles.ExperimentalSandboxes;

public sealed class ExperimentalSandboxManager : IDisposable
{
    private readonly object _gate = new();
    private readonly List<ExperimentalSandbox> _sandboxes = [];
    private ExperimentalSandboxSupport _support;
    private bool _disposed;

    public ExperimentalSandboxManager()
    {
        _support = SandboxSupportProbe.Probe();
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
        var support = SandboxSupportProbe.Probe();
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
        ExperimentalSandbox[] sandboxes;
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

    private void SandboxChanged(
        object? sender,
        ExperimentalSandboxChangedEventArgs eventArgs)
    {
        if (!eventArgs.Closed || sender is not ExperimentalSandbox sandbox)
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
