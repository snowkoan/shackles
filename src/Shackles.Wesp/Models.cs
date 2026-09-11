namespace Shackles.Wesp;

public enum WespAvailability
{
    Available,
    PlatformNotSupported,
    ArchitectureNotSupported,
    ClientLibraryMissing,
    RequiredExportMissing,
    ProbeFailed
}

public sealed record WespSupportInfo(
    WespAvailability Availability,
    string Summary,
    string? ClientLibraryPath,
    IReadOnlyList<string> MissingExports)
{
    public bool IsAvailable => Availability == WespAvailability.Available;
}

public sealed record WespPolicy(
    IReadOnlyList<string> BlockedFilePaths,
    IReadOnlyList<string> ReadOnlyFilePaths,
    IReadOnlyList<string> BlockedRegistryKeys,
    IReadOnlyList<string> ReadOnlyRegistryKeys,
    IReadOnlyList<string> BlockedChildExecutables,
    bool BlockUncPaths = false);

public sealed record WespLaunchOptions(
    string FileName,
    string Arguments = "",
    string? WorkingDirectory = null);

public sealed record WespLaunchResult(
    int ProcessId,
    long CreationTimeFileTimeUtc,
    IReadOnlyList<string> Warnings);

public enum WespProcessOrigin
{
    Launched,
    Attached
}

public sealed record WespTrackedProcessInfo(
    int ProcessId,
    long CreationTimeFileTimeUtc,
    bool IsRunning,
    WespProcessOrigin Origin);

public enum WespApplyProcessStatus
{
    Applied,
    AlreadyApplied,
    Failed
}

public sealed record WespApplyProcessResult(
    int ProcessId,
    long CreationTimeFileTimeUtc,
    WespApplyProcessStatus Status,
    string? ErrorMessage)
{
    public bool Succeeded => Status != WespApplyProcessStatus.Failed;
}

public enum WespActivityResourceKind
{
    File,
    Registry,
    Process
}

public enum WespActivityKind
{
    Blocked,
    ProcessStarted,
    ProcessAttached
}

public sealed record WespActivity(
    DateTimeOffset ObservedAt,
    WespActivityKind ActivityKind,
    WespActivityResourceKind ResourceKind,
    string Operation,
    string ConfiguredPath,
    string TargetPath,
    string ProcessName,
    int? ProcessId,
    ulong EventId);
