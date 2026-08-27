namespace Shackles.App.Models;

internal sealed record RestrictionProfile(
    HardLimitSettings HardLimits,
    CpuControlSettings Cpu,
    NetworkControlSettings Network,
    UiRestrictionFlags UiRestrictions,
    NotificationSettings Notifications,
    IReadOnlyList<ProcessorGroupAffinity> ProcessorGroups,
    JobEndAction EndAction)
{
    public static RestrictionProfile Empty { get; } = new(
        new HardLimitSettings(),
        new CpuControlSettings(),
        new NetworkControlSettings(),
        UiRestrictionFlags.None,
        new NotificationSettings(),
        Array.Empty<ProcessorGroupAffinity>(),
        JobEndAction.TerminateAtEndOfJob);
}

internal sealed record HardLimitSettings(
    bool KillOnJobClose = false,
    bool BreakawayAllowed = false,
    bool SilentBreakawayAllowed = false,
    bool DieOnUnhandledException = false,
    uint? ActiveProcessLimit = null,
    TimeSpan? PerProcessUserTimeLimit = null,
    TimeSpan? PerJobUserTimeLimit = null,
    ulong? MinimumWorkingSetBytes = null,
    ulong? MaximumWorkingSetBytes = null,
    ulong? ProcessMemoryLimitBytes = null,
    ulong? JobMemoryLimitBytes = null,
    ulong? AffinityMask = null,
    bool SubsetAffinityAllowed = false,
    ProcessPriorityChoice? PriorityClass = null,
    uint? SchedulingClass = null);

internal enum ProcessPriorityChoice : uint
{
    Idle = 0x00000040,
    BelowNormal = 0x00004000,
    Normal = 0x00000020,
    AboveNormal = 0x00008000,
    High = 0x00000080,
    Realtime = 0x00000100
}

internal enum CpuControlMode
{
    Disabled,
    Rate,
    HardCap,
    Weight,
    MinimumMaximum
}

internal sealed record CpuControlSettings(
    CpuControlMode Mode = CpuControlMode.Disabled,
    double? RatePercent = null,
    uint? Weight = null,
    double? MinimumPercent = null,
    double? MaximumPercent = null,
    bool Notify = false,
    bool UsesUnsupportedPerProcessorCaps = false);

internal sealed record NetworkControlSettings(
    double? MaximumBandwidthMegabitsPerSecond = null,
    byte? DscpTag = null,
    ulong? ExactMaximumBandwidthBytesPerSecond = null);

[Flags]
internal enum UiRestrictionFlags : uint
{
    None = 0,
    Handles = 0x00000001,
    ReadClipboard = 0x00000002,
    WriteClipboard = 0x00000004,
    SystemParameters = 0x00000008,
    DisplaySettings = 0x00000010,
    GlobalAtoms = 0x00000020,
    Desktops = 0x00000040,
    ExitWindows = 0x00000080,
    Ime = 0x00000100,
    Injection = 0x00000200
}

internal enum RateTolerance
{
    None,
    Low,
    Medium,
    High
}

internal enum RateToleranceInterval
{
    None,
    Short,
    Medium,
    Long
}

internal sealed record NotificationSettings(
    TimeSpan? PerJobUserTime = null,
    ulong? JobMemoryBytes = null,
    ulong? JobLowMemoryBytes = null,
    ulong? IoReadBytes = null,
    ulong? IoWriteBytes = null,
    RateTolerance CpuTolerance = RateTolerance.None,
    RateToleranceInterval CpuToleranceInterval = RateToleranceInterval.None,
    RateTolerance IoTolerance = RateTolerance.None,
    RateToleranceInterval IoToleranceInterval = RateToleranceInterval.None,
    RateTolerance NetworkTolerance = RateTolerance.None,
    RateToleranceInterval NetworkToleranceInterval = RateToleranceInterval.None);

internal enum JobEndAction
{
    TerminateAtEndOfJob,
    PostNotification
}

internal sealed record ProcessorGroupAffinity(ushort Group, ulong Mask);
