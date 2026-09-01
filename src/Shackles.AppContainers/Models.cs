namespace Shackles.AppContainers;

public enum AppContainerIsolationMode
{
    Standard,
    LowPrivilege
}

public enum FileSystemGrantAccess
{
    ReadExecute,
    ReadWriteDelete
}

public enum AppContainerFileSystemPolicyBackend
{
    AccessControlLists,
    BrokeredFileSystem
}

public enum BrokeredFileSystemAvailability
{
    Available,
    PlatformNotSupported,
    WindowsDirectoryUnavailable,
    ConfigurationToolMissing
}

public sealed record BrokeredFileSystemSupport(
    BrokeredFileSystemAvailability Availability,
    string Summary,
    Version OsVersion,
    string? ConfigurationToolPath,
    string? DriverPath,
    bool DriverFilePresent,
    IReadOnlyList<string> Warnings)
{
    public bool IsAvailable =>
        Availability == BrokeredFileSystemAvailability.Available;
}

public enum RegistryGrantAccess
{
    Read,
    ReadWrite
}

public enum RegistryGrantView
{
    Automatic,
    Registry32,
    Registry64
}

public sealed record FileSystemGrant(
    string Path,
    bool IsDirectory,
    FileSystemGrantAccess Access);

public sealed record RegistryGrant(
    string KeyPath,
    RegistryGrantAccess Access,
    RegistryGrantView View);

public sealed record AppContainerSandboxOptions
{
    public required string DisplayName { get; init; }

    public AppContainerIsolationMode IsolationMode { get; init; }

    public AppContainerFileSystemPolicyBackend FileSystemPolicyBackend { get; init; }

    public bool RestrictChildProcessCreation { get; init; }

    public bool UseMinimalEnvironment { get; init; }

    public IReadOnlyList<string> CapabilityNames { get; init; } = Array.Empty<string>();

    public IReadOnlyList<FileSystemGrant> FileSystemGrants { get; init; } = Array.Empty<FileSystemGrant>();

    public IReadOnlyList<RegistryGrant> RegistryGrants { get; init; } = Array.Empty<RegistryGrant>();
}

public sealed record AppContainerLaunchOptions(string FileName)
{
    public string Arguments { get; init; } = string.Empty;

    public string? WorkingDirectory { get; init; }

    public bool IncludeTargetDirectoryGrant { get; init; } = true;
}

public sealed record AppContainerLaunchResult(
    int ProcessId,
    long CreationTimeFileTimeUtc,
    IReadOnlyList<string> Warnings);

public sealed record AppContainerCreationResult(
    AppContainerSandbox Sandbox,
    AppContainerLaunchResult FirstLaunch);

public sealed record AppContainerSnapshot(
    string DisplayName,
    string ProfileName,
    string Sid,
    AppContainerSandboxOptions Options,
    IReadOnlyList<int> ProcessIds,
    DateTimeOffset CapturedAtUtc,
    bool IsClosed);

public sealed record AppContainerCleanupResult(
    string DisplayName,
    bool Completed,
    IReadOnlyList<string> Warnings);

public sealed record AppContainerRecoveryResult(
    int RecoveredSessionCount,
    IReadOnlyList<string> Warnings);

public sealed class AppContainerSandboxChangedEventArgs : EventArgs
{
    public AppContainerSandboxChangedEventArgs(bool closed)
        : this(
            closed,
            resourcePolicyCleanupAttempted: false,
            Array.Empty<string>())
    {
    }

    internal AppContainerSandboxChangedEventArgs(
        bool closed,
        bool resourcePolicyCleanupAttempted,
        IReadOnlyList<string> cleanupWarnings)
    {
        Closed = closed;
        ResourcePolicyCleanupAttempted = resourcePolicyCleanupAttempted;
        CleanupWarnings = cleanupWarnings;
    }

    public bool Closed { get; }

    public bool ResourcePolicyCleanupAttempted { get; }

    public IReadOnlyList<string> CleanupWarnings { get; }
}
