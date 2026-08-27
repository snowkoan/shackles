namespace Shackles.JobObjects;

/// <summary>The documented hard limits represented by JobObjectExtendedLimitInformation.</summary>
public sealed record JobExtendedLimits
{
    public TimeSpan? PerProcessUserTimeLimit { get; init; }

    public TimeSpan? PerJobUserTimeLimit { get; init; }

    public ulong? MinimumWorkingSetBytes { get; init; }

    public ulong? MaximumWorkingSetBytes { get; init; }

    public uint? ActiveProcessLimit { get; init; }

    public ulong? ProcessorAffinityMask { get; init; }

    public JobPriorityClass? PriorityClass { get; init; }

    public uint? SchedulingClass { get; init; }

    public ulong? ProcessMemoryLimitBytes { get; init; }

    public ulong? JobMemoryLimitBytes { get; init; }

    public bool DieOnUnhandledException { get; init; }

    public bool BreakawayAllowed { get; init; }

    public bool SilentBreakawayAllowed { get; init; }

    public bool KillOnJobClose { get; init; }

    public bool SubsetAffinity { get; init; }
}

public enum JobPriorityClass : uint
{
    Idle = 0x00000040,
    BelowNormal = 0x00004000,
    Normal = 0x00000020,
    AboveNormal = 0x00008000,
    High = 0x00000080,
    Realtime = 0x00000100
}

[Flags]
public enum JobUiRestrictions : uint
{
    None = 0,
    Handles = 0x00000001,
    ReadClipboard = 0x00000002,
    WriteClipboard = 0x00000004,
    SystemParameters = 0x00000008,
    DisplaySettings = 0x00000010,
    GlobalAtoms = 0x00000020,
    Desktop = 0x00000040,
    ExitWindows = 0x00000080,
    InputMethodEditor = 0x00000100,
    Injection = 0x00000200
}

public enum JobCpuRateMode
{
    Disabled,
    Rate,
    HardCap,
    WeightBased,
    MinimumMaximum
}

/// <summary>CPU rate values use Windows' units of one hundredth of one percent (1 through 10,000).</summary>
public sealed record JobCpuRateControl
{
    public JobCpuRateMode Mode { get; init; }

    public uint? Rate { get; init; }

    public uint? Weight { get; init; }

    public ushort? MinimumRate { get; init; }

    public ushort? MaximumRate { get; init; }

    public bool Notify { get; init; }

    /// <summary>
    /// True when Windows reports JOB_OBJECT_CPU_RATE_CONTROL_PER_PROCESSOR_CAPS. Shackles can
    /// preserve this state on an unchanged apply but cannot edit or interpret its native payload.
    /// </summary>
    public bool UsesUnsupportedPerProcessorCaps { get; init; }

    public static JobCpuRateControl Disabled { get; } = new();
}

public sealed record JobNetworkRateControl
{
    public bool Enabled { get; init; }

    public ulong? MaximumBandwidthBytesPerSecond { get; init; }

    public byte? DscpTag { get; init; }

    public static JobNetworkRateControl Disabled { get; } = new();
}

public enum JobEndOfJobAction : uint
{
    TerminateProcesses = 0,
    PostNotification = 1
}

public enum JobRateControlTolerance : uint
{
    Low = 1,
    Medium = 2,
    High = 3
}

public enum JobRateControlToleranceInterval : uint
{
    Short = 1,
    Medium = 2,
    Long = 3
}

public sealed record JobRateNotification(
    JobRateControlTolerance Tolerance,
    JobRateControlToleranceInterval Interval);

/// <summary>Soft notification thresholds; exceeding these does not stop a process.</summary>
public sealed record JobNotificationLimits
{
    public ulong? IoReadBytes { get; init; }

    public ulong? IoWriteBytes { get; init; }

    public TimeSpan? PerJobUserTime { get; init; }

    public ulong? JobHighMemoryBytes { get; init; }

    public ulong? JobLowMemoryBytes { get; init; }

    public JobRateNotification? CpuRate { get; init; }

    public JobRateNotification? IoRate { get; init; }

    public JobRateNotification? NetworkRate { get; init; }
}

public sealed record ProcessorGroupAffinity(ushort Group, ulong AffinityMask);

/// <summary>A complete restriction view. Applying it updates independently supported native classes.</summary>
public sealed record JobRestrictions
{
    public JobExtendedLimits ExtendedLimits { get; init; } = new();

    public JobUiRestrictions UiRestrictions { get; init; }

    public JobCpuRateControl CpuRateControl { get; init; } = JobCpuRateControl.Disabled;

    public JobNetworkRateControl NetworkRateControl { get; init; } = JobNetworkRateControl.Disabled;

    public JobEndOfJobAction EndOfJobAction { get; init; } = JobEndOfJobAction.TerminateProcesses;

    public JobNotificationLimits NotificationLimits { get; init; } = new();

    public IReadOnlyList<ProcessorGroupAffinity> ProcessorGroups { get; init; } = Array.Empty<ProcessorGroupAffinity>();
}
