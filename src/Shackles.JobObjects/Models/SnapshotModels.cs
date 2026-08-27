namespace Shackles.JobObjects;

public sealed record JobIoCounters(
    ulong ReadOperations,
    ulong WriteOperations,
    ulong OtherOperations,
    ulong ReadBytes,
    ulong WriteBytes,
    ulong OtherBytes);

public sealed record JobAccounting(
    TimeSpan TotalUserTime,
    TimeSpan TotalKernelTime,
    TimeSpan ThisPeriodUserTime,
    TimeSpan ThisPeriodKernelTime,
    uint TotalPageFaults,
    uint TotalProcesses,
    uint ActiveProcesses,
    uint TotalTerminatedProcesses,
    JobIoCounters Io);

public sealed record JobMemoryPeaks(ulong PeakProcessMemoryBytes, ulong PeakJobMemoryBytes);

public enum JobNotificationDeliveryMode
{
    /// <summary>The caller can sample class-34 state, but this JobObject does not own a live delivery port.</summary>
    SampledQueryOnly,

    /// <summary>This JobObject owns, consumes, and will detach a private I/O completion port.</summary>
    OwnedCompletionPort
}

public enum JobNotificationMessageKind : uint
{
    EndOfJobTime = 1,
    EndOfProcessTime = 2,
    ActiveProcessLimit = 3,
    ActiveProcessZero = 4,
    NewProcess = 6,
    ExitProcess = 7,
    AbnormalExitProcess = 8,
    ProcessMemoryLimit = 9,
    JobMemoryLimit = 10,
    NotificationLimit = 11,
    JobCycleTimeLimit = 12,
    SiloTerminated = 13
}

public sealed class JobNotificationEventArgs : EventArgs
{
    public JobNotificationEventArgs(
        uint rawMessageCode,
        int? processId,
        JobLimitViolations? limitViolations,
        JobOperationError? error,
        DateTimeOffset receivedAtUtc)
    {
        RawMessageCode = rawMessageCode;
        MessageKind = (JobNotificationMessageKind)rawMessageCode;
        ProcessId = processId;
        LimitViolations = limitViolations;
        Error = error;
        ReceivedAtUtc = receivedAtUtc;
    }

    public uint RawMessageCode { get; }

    public JobNotificationMessageKind MessageKind { get; }

    /// <summary>A transient PID supplied by Windows; it is not a stable ProcessIdentity.</summary>
    public int? ProcessId { get; }

    public JobLimitViolations? LimitViolations { get; }

    public JobOperationError? Error { get; }

    public DateTimeOffset ReceivedAtUtc { get; }
}

[Flags]
public enum JobNotificationLimitFlags : uint
{
    None = 0,
    PerJobUserTime = 0x00000004,
    JobHighMemory = 0x00000200,
    JobLowMemory = 0x00008000,
    JobReadBytes = 0x00010000,
    JobWriteBytes = 0x00020000,
    CpuRateControl = 0x00040000,
    IoRateControl = 0x00080000,
    NetworkRateControl = 0x00100000
}

/// <summary>The most recently queried class-34 notification state. This class is query-only.</summary>
public sealed record JobLimitViolations
{
    public JobNotificationLimitFlags ConfiguredLimits { get; init; }

    public JobNotificationLimitFlags ViolatedLimits { get; init; }

    public ulong IoReadBytes { get; init; }

    public ulong IoReadBytesLimit { get; init; }

    public ulong IoWriteBytes { get; init; }

    public ulong IoWriteBytesLimit { get; init; }

    public TimeSpan PerJobUserTime { get; init; }

    public TimeSpan PerJobUserTimeLimit { get; init; }

    public ulong JobMemoryBytes { get; init; }

    public ulong JobHighMemoryLimitBytes { get; init; }

    public ulong JobLowMemoryLimitBytes { get; init; }

    public JobRateControlTolerance CpuRateTolerance { get; init; }

    public JobRateControlTolerance CpuRateToleranceLimit { get; init; }

    public JobRateControlTolerance IoRateTolerance { get; init; }

    public JobRateControlTolerance IoRateToleranceLimit { get; init; }

    public JobRateControlTolerance NetworkRateTolerance { get; init; }

    public JobRateControlTolerance NetworkRateToleranceLimit { get; init; }
}

public sealed record JobSnapshot(
    JobRestrictions Restrictions,
    JobAccounting Accounting,
    IReadOnlyList<int> ProcessIds,
    JobMemoryPeaks MemoryPeaks,
    JobLimitViolations LimitViolations,
    DateTimeOffset CapturedAtUtc);
