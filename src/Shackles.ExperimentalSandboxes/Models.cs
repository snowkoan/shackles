namespace Shackles.ExperimentalSandboxes;

public enum ExperimentalSandboxAvailability
{
    Available,
    PlatformNotSupported,
    LibraryMissing,
    EntryPointMissing,
    FeatureDisabled,
    ProbeFailed
}

public enum ExperimentalFeatureConfigurationState
{
    Unknown,
    Default,
    Enabled,
    Disabled
}

public sealed record ExperimentalFeatureState(
    uint Id,
    string Name,
    ExperimentalFeatureConfigurationState ConfigurationState,
    uint? Priority);

public sealed record ExperimentalSandboxSupport(
    ExperimentalSandboxAvailability Availability,
    string Summary,
    Version OsVersion,
    string? ProcessModelVersion,
    bool CreateExportPresent,
    bool QueryExportPresent,
    ulong? CapabilityMask,
    int? ProbeErrorCode,
    IReadOnlyList<ExperimentalFeatureState> RequiredFeatures)
{
    public bool IsAvailable => Availability == ExperimentalSandboxAvailability.Available;

    public bool SupportsFileSystemDeny =>
        CapabilityMask is { } capabilities && (capabilities & 0x2) != 0;

    public bool FileSystemDenySupportKnown => CapabilityMask.HasValue;

    public bool SupportsLegacyProxy =>
        !QueryExportPresent || CapabilityMask is { } capabilities && (capabilities & 0x4) != 0;
}

public enum ExperimentalSandboxIntegrityLevel
{
    SystemDefault,
    Inherit,
    Untrusted,
    Low,
    Medium,
    High
}

[Flags]
public enum ExperimentalSandboxUiRestrictions : ulong
{
    None = 0,
    ExternalHandles = 0x0001,
    ReadClipboard = 0x0002,
    WriteClipboard = 0x0004,
    SystemParameters = 0x0008,
    DisplaySettings = 0x0010,
    GlobalAtoms = 0x0020,
    Desktop = 0x0040,
    ExitWindows = 0x0080,
    InputMethodEditor = 0x0100,
    InputInjection = 0x0200
}

public enum ExperimentalSandboxFileAccess
{
    ReadWrite,
    ReadOnly,
    Deny
}

public sealed record ExperimentalSandboxFileRule(
    string Path,
    ExperimentalSandboxFileAccess Access);

public enum ExperimentalSandboxNetworkMode
{
    Blocked,
    Allowed,
    Proxy
}

public sealed record ExperimentalSandboxOptions
{
    public required string DisplayName { get; init; }

    public bool UseAppContainer { get; init; } = true;

    public ExperimentalSandboxIntegrityLevel IntegrityLevel { get; init; } =
        ExperimentalSandboxIntegrityLevel.SystemDefault;

    public bool LeastPrivilege { get; init; }

    public bool DisallowWin32kSystemCalls { get; init; }

    public ExperimentalSandboxUiRestrictions UiRestrictions { get; init; }

    public ExperimentalSandboxNetworkMode NetworkMode { get; init; }

    public string? ProxyUrl { get; init; }

    public bool UseMinimalEnvironment { get; init; }

    public IReadOnlyList<string> CapabilityNames { get; init; } =
        Array.Empty<string>();

    public IReadOnlyList<ExperimentalSandboxFileRule> FileSystemRules { get; init; } =
        Array.Empty<ExperimentalSandboxFileRule>();
}

public sealed record ExperimentalSandboxLaunchOptions(string FileName)
{
    public string Arguments { get; init; } = string.Empty;

    public string? WorkingDirectory { get; init; }

    public bool IncludeTargetDirectoryReadAccess { get; init; } = true;

    public bool IncludeWorkingDirectoryWriteAccess { get; init; } = true;
}

public sealed record ExperimentalSandboxLaunchResult(
    int ProcessId,
    long CreationTimeFileTimeUtc,
    IReadOnlyList<string> Warnings);

public sealed record ExperimentalSandboxCreationResult(
    ExperimentalSandbox Sandbox,
    ExperimentalSandboxLaunchResult FirstLaunch);

public sealed record ExperimentalSandboxSnapshot(
    string DisplayName,
    string Identity,
    string? AppContainerSid,
    ExperimentalSandboxOptions Options,
    IReadOnlyList<int> ProcessIds,
    DateTimeOffset CapturedAtUtc,
    bool IsClosed);

public sealed record ExperimentalSandboxCleanupResult(
    string DisplayName,
    bool Completed,
    IReadOnlyList<string> Warnings);

public sealed class ExperimentalSandboxChangedEventArgs : EventArgs
{
    public ExperimentalSandboxChangedEventArgs(bool closed)
    {
        Closed = closed;
    }

    public bool Closed { get; }
}
