using System.Diagnostics;
using Shackles.Wesp.Internal;
using Shackles.Wesp.Interop;

namespace Shackles.Wesp;

public sealed class WespSession : IDisposable
{
    private const string ClientName = "Shackles WESP POC";
    private const string ClientAltitude = "125100";
    private const int HResultNotFound = unchecked((int)0x80070490);
    private static readonly Guid ClientId = new("CD46BE96-A040-4F94-BF3C-566A711D018B");

    private readonly object _gate = new();
    private readonly List<TrackedWespProcess> _processes = [];
    private readonly Guid _clientId;
    private readonly ulong _policyId;
    private readonly WespEventQueue _eventQueue;
    private readonly IReadOnlyList<string> _sessionWarnings;
    private nint _client;
    private bool _closeRequested;
    private bool _closed;

    private WespSession(
        Guid clientId,
        nint client,
        ulong policyId,
        WespEventQueue eventQueue,
        WespPolicy policy,
        string? rootExecutablePath,
        string clientVersion,
        IReadOnlyList<string> sessionWarnings)
    {
        _clientId = clientId;
        _client = client;
        _policyId = policyId;
        _eventQueue = eventQueue;
        _sessionWarnings = sessionWarnings;
        Policy = policy;
        RootExecutablePath = rootExecutablePath;
        ClientVersion = clientVersion;
    }

    public WespPolicy Policy { get; }

    public string? RootExecutablePath { get; }

    public string ClientVersion { get; }

    public bool IsClosed
    {
        get
        {
            lock (_gate)
            {
                return _closed;
            }
        }
    }

    public bool CanLaunch
    {
        get
        {
            lock (_gate)
            {
                return RootExecutablePath is not null &&
                       !_closeRequested &&
                       !_closed;
            }
        }
    }

    public bool CanApply
    {
        get
        {
            lock (_gate)
            {
                return !_closeRequested && !_closed;
            }
        }
    }

    public static WespSession Create(WespPolicy policy) =>
        CreateCore(policy, rootExecutablePath: null, hasConfiguredRoot: false);

    public static WespSession Create(WespPolicy policy, string rootExecutablePath) =>
        CreateCore(policy, rootExecutablePath, hasConfiguredRoot: true);

    private static unsafe WespSession CreateCore(
        WespPolicy policy,
        string? rootExecutablePath,
        bool hasConfiguredRoot)
    {
        // WESP rejects client connections from a medium-integrity process. Check
        // the token first so users receive an actionable error before any native
        // registration state is changed.
        WespProcessIntegrityProbe.EnsureHighIntegrity();

        var support = WespSupport.Probe();
        if (!support.IsAvailable)
        {
            throw new WespException(WespOperation.CheckSupport, support.Summary);
        }

        var normalizedPolicy = WespPolicyNormalizer.Normalize(policy);
        support = WespSupport.Probe(normalizedPolicy);
        if (!support.IsAvailable)
        {
            throw new WespException(WespOperation.CheckSupport, support.Summary);
        }

        var normalizedRoot = hasConfiguredRoot
            ? WespPolicyNormalizer.NormalizeRootExecutable(rootExecutablePath!)
            : null;
        var clientId = ClientId;
        nint client = 0;
        WespEventQueue? eventQueue = null;
        var registered = false;
        try
        {
            // A WESP process crash automatically disconnects its client and tears
            // down client-session resources. Registration itself is persistent,
            // though, so reset this POC's stable client ID and require WESP to
            // confirm the reset before any new rules are created.
            WespStartupStateCleanup.ConfirmPreviousRegistrationRemoved(
                NativeMethods.EspUnregisterClient(in clientId));

            fixed (char* name = ClientName)
            fixed (char* altitude = ClientAltitude)
            {
                var descriptor = new NativeClientDescriptor
                {
                    ClientId = clientId,
                    ClientName = (nint)name,
                    Altitude = (nint)altitude
                };
                Check(
                    NativeMethods.EspRegisterClient(in descriptor),
                    WespOperation.RegisterClient,
                    "WESP could not register the Shackles proof-of-concept client.");
            }

            registered = true;
            Check(
                NativeMethods.EspConnectClient(in clientId, out client),
                WespOperation.ConnectClient,
                "WESP could not connect the Shackles proof-of-concept client.");

            // Registration reset already removes persisted client state. Clear
            // every rule lifetime again on the connected client so installing the
            // requested policy always begins from an explicitly empty rule set.
            WespStartupStateCleanup.ConfirmAllRulesRemoved(
                NativeMethods.EspRemoveAllRulesForClient(client));

            var clientInfo = InspectConnectedClient(support.ClientLibraryPath);
            // Compatibility is established against the connected WESP
            // implementation by querying required event and built-in property
            // capabilities rather than rejecting a client by its file version.
            // Context-key IDs are not built-in process-property IDs; support for
            // them is validated by the filter, rule, and tagging operations that
            // actually consume them.
            WespRuleCompiler.ValidateCapabilities(client, normalizedPolicy);
            eventQueue = WespEventQueue.Create(client, clientInfo.MatchesProfile);
            var policyId = WespRuleCompiler.CreatePolicyId();
            WespRuleCompiler.Install(
                client,
                normalizedPolicy,
                policyId,
                eventQueue);
            var frozenPolicy = new WespPolicy(
                BlockedFilePaths: normalizedPolicy.BlockedFilePaths.ToArray(),
                ReadOnlyFilePaths: normalizedPolicy.ReadOnlyFilePaths.ToArray(),
                BlockedRegistryKeys: normalizedPolicy.BlockedRegistryKeys.ToArray(),
                ReadOnlyRegistryKeys: normalizedPolicy.ReadOnlyRegistryKeys.ToArray(),
                BlockedChildExecutables: normalizedPolicy.BlockedChildExecutables.ToArray(),
                BlockUncPaths: normalizedPolicy.BlockUncPaths);
            return new WespSession(
                clientId,
                client,
                policyId,
                eventQueue,
                frozenPolicy,
                normalizedRoot,
                clientInfo.DisplayVersion,
                clientInfo.Warning is null ? [] : [clientInfo.Warning]);
        }
        catch
        {
            if (client != 0)
            {
                _ = NativeMethods.EspRemoveRulesForClient(
                    client,
                    EspRuleLifetime.ClientSession);
                eventQueue?.Dispose();
                _ = NativeMethods.EspDisconnectClient(client);
            }

            if (registered)
            {
                _ = NativeMethods.EspUnregisterClient(in clientId);
            }

            throw;
        }
    }

    public WespLaunchResult Launch(WespLaunchOptions options)
    {
        lock (_gate)
        {
            ThrowIfClosed();
            var rootExecutablePath = RootExecutablePath ??
                throw new InvalidOperationException(
                    "This WESP Blocking session was created for existing processes only and cannot launch an application.");
            var tracked = WespProcessLauncher.Launch(
                _client,
                _policyId,
                rootExecutablePath,
                options,
                out var result);
            _processes.Add(tracked);
            _eventQueue.RecordActivity(new WespActivity(
                DateTimeOffset.Now,
                WespActivityKind.ProcessStarted,
                WespActivityResourceKind.Process,
                "Root application started",
                string.Empty,
                rootExecutablePath,
                Path.GetFileName(rootExecutablePath),
                result.ProcessId,
                EventId: 0));
            return _sessionWarnings.Count == 0
                ? result
                : result with
                {
                    Warnings = result.Warnings.Concat(_sessionWarnings).ToArray()
                };
        }
    }

    public WespApplyProcessResult ApplyToProcess(
        int processId,
        long creationTimeUtcFileTime)
    {
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(processId),
                processId,
                "The process ID must be positive.");
        }

        if (creationTimeUtcFileTime <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(creationTimeUtcFileTime),
                creationTimeUtcFileTime,
                "The expected process creation time must be positive.");
        }

        lock (_gate)
        {
            ThrowIfClosed();
            RemoveExitedProcesses();
            if (_processes.Any(process =>
                    process.ProcessId == processId &&
                    process.CreationTimeFileTimeUtc == creationTimeUtcFileTime &&
                    process.IsRunning))
            {
                return new WespApplyProcessResult(
                    processId,
                    creationTimeUtcFileTime,
                    WespApplyProcessStatus.AlreadyApplied,
                    ErrorMessage: null);
            }

            try
            {
                var applied = WespExistingProcessAttacher.Apply(
                    _client,
                    _policyId,
                    processId,
                    creationTimeUtcFileTime);
                _processes.Add(applied.Tracking);
                _eventQueue.RecordActivity(new WespActivity(
                    DateTimeOffset.Now,
                    WespActivityKind.ProcessAttached,
                    WespActivityResourceKind.Process,
                    "WESP Blocking applied to running process",
                    string.Empty,
                    applied.Identity.ImagePath ?? "Process image unavailable",
                    applied.Identity.ProcessName,
                    processId,
                    EventId: 0));
                return new WespApplyProcessResult(
                    processId,
                    creationTimeUtcFileTime,
                    WespApplyProcessStatus.Applied,
                    ErrorMessage: null);
            }
            catch (WespException exception)
            {
                return new WespApplyProcessResult(
                    processId,
                    creationTimeUtcFileTime,
                    WespApplyProcessStatus.Failed,
                    exception.Message);
            }
        }
    }

    public IReadOnlyList<WespTrackedProcessInfo> GetProcesses()
    {
        lock (_gate)
        {
            RemoveExitedProcesses();

            return _processes.Select(process => process.GetInfo()).ToArray();
        }
    }

    public IReadOnlyList<WespActivity> GetActivities()
    {
        lock (_gate)
        {
            return _eventQueue.GetActivities();
        }
    }

    public string ActivityCaptureStatus
    {
        get
        {
            lock (_gate)
            {
                return _eventQueue.CaptureStatus;
            }
        }
    }

    public void Close()
    {
        var error = CloseCore();
        if (error is not null)
        {
            throw error;
        }
    }

    public void Dispose() => _ = CloseCore();

    private WespException? CloseCore()
    {
        lock (_gate)
        {
            if (_closed)
            {
                return null;
            }

            _closeRequested = true;
            foreach (var process in _processes.Where(process =>
                         process.Origin == WespProcessOrigin.Launched))
            {
                process.RequestTermination();
            }

            var terminationDeadline = Environment.TickCount64 + 2000;
            foreach (var process in _processes.Where(process =>
                         process.Origin == WespProcessOrigin.Launched))
            {
                var remaining = Math.Max(0, terminationDeadline - Environment.TickCount64);
                process.WaitForExit(checked((uint)remaining));
            }

            for (var index = _processes.Count - 1; index >= 0; index--)
            {
                if (_processes[index].Origin == WespProcessOrigin.Attached ||
                    !_processes[index].IsRunning)
                {
                    _processes[index].Dispose();
                    _processes.RemoveAt(index);
                }
            }

            if (_processes.Count != 0)
            {
                return new WespException(
                    WespOperation.CloseSession,
                    $"Windows did not terminate {string.Join(", ", _processes.Select(process => $"PID {process.ProcessId}"))}. " +
                    "The WESP rules remain active; close those processes and retry closing the session.");
            }

            WespException? error = null;
            if (_client != 0)
            {
                CaptureFailure(
                    NativeMethods.EspRemoveRulesForClient(_client, EspRuleLifetime.ClientSession),
                    "WESP could not explicitly remove all client-session rules.",
                    ref error);
                var queueError = _eventQueue.Close();
                if (queueError is not null && error is null)
                {
                    error = queueError;
                }

                if (!_eventQueue.IsDisposed)
                {
                    // The queue still owns live native callbacks. Keep the
                    // client connected so cleanup can be retried safely.
                    return error;
                }

                var disconnectResult = NativeMethods.EspDisconnectClient(_client);
                // ESP_CLIENT is _Post_invalid_ in the pinned preview header,
                // even when the call reports failure. Only GUID cleanup can
                // be retried from this point.
                _client = 0;
                CaptureFailure(
                    disconnectResult,
                    "WESP could not disconnect the Shackles client.",
                    ref error);
            }

            if (_client == 0)
            {
                var unregisterResult = NativeMethods.EspUnregisterClient(in _clientId);
                if (unregisterResult != HResultNotFound)
                {
                    CaptureFailure(
                        unregisterResult,
                        "WESP could not unregister the Shackles client.",
                        ref error);
                }

                _closed = unregisterResult >= 0 || unregisterResult == HResultNotFound;
            }

            return error;
        }
    }

    private void RemoveExitedProcesses()
    {
        for (var index = _processes.Count - 1; index >= 0; index--)
        {
            if (!_processes[index].IsRunning)
            {
                _processes[index].Dispose();
                _processes.RemoveAt(index);
            }
        }
    }

    private static void CaptureFailure(int hresult, string detail, ref WespException? error)
    {
        if (hresult < 0 && error is null)
        {
            error = WespException.FromHResult(WespOperation.CloseSession, hresult, detail);
        }
    }

    private void ThrowIfClosed()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        if (_closeRequested)
        {
            throw new InvalidOperationException(
                "This WESP session has begun closing and cannot accept another process.");
        }
    }

    private static void Check(int hresult, WespOperation operation, string detail)
    {
        if (hresult < 0)
        {
            throw WespException.FromHResult(operation, hresult, detail);
        }
    }

    private static ConnectedClientInfo InspectConnectedClient(string? libraryPath)
    {
        if (string.IsNullOrWhiteSpace(libraryPath))
        {
            return new ConnectedClientInfo(
                "Version unavailable",
                "The connected WESP client DLL version could not be identified. Required capabilities were checked, but native layout compatibility is not confirmed.",
                MatchesProfile: false);
        }

        try
        {
            var version = FileVersionInfo.GetVersionInfo(libraryPath);
            var displayVersion = version.ProductVersion ??
                                 version.FileVersion ??
                                 "Version unavailable";
            var matchesProfile =
                string.Equals(
                    version.FileVersion,
                    WespAbiV013.FileVersion,
                    StringComparison.Ordinal) &&
                string.Equals(
                    version.ProductVersion,
                    WespAbiV013.ProductVersion,
                    StringComparison.Ordinal);
            return new ConnectedClientInfo(
                displayVersion,
                matchesProfile
                    ? null
                    : $"The connected WESP client DLL reports {displayVersion}; this POC's native layouts were validated with {WespAbiV013.ProductVersion}. Required capabilities are available, so the session was allowed to continue.",
                matchesProfile);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new ConnectedClientInfo(
                "Version unavailable",
                $"The connected WESP client DLL version could not be read ({exception.Message}). Required capabilities were checked, so the session was allowed to continue.",
                MatchesProfile: false);
        }
    }

    private sealed record ConnectedClientInfo(
        string DisplayVersion,
        string? Warning,
        bool MatchesProfile);
}
