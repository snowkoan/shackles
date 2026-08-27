namespace Shackles.App.Models;

internal enum CapabilityState
{
    Available,
    QueryOnly,
    Unavailable
}

internal sealed record CapabilityInfo(CapabilityState State, string Reason)
{
    public string BadgeText => State switch
    {
        CapabilityState.Available => "AVAILABLE",
        CapabilityState.QueryOnly => "QUERY ONLY",
        _ => "UNAVAILABLE"
    };

    public bool CanSet => State == CapabilityState.Available;
}

internal sealed record JobCapabilitySet(
    CapabilityInfo ExtendedLimits,
    CapabilityInfo CpuRateControl,
    CapabilityInfo NetworkRateControl,
    CapabilityInfo UiRestrictions,
    CapabilityInfo NotificationLimits,
    CapabilityInfo ProcessorGroups,
    CapabilityInfo EndOfJobAction)
{
    public static JobCapabilitySet Unavailable(string reason)
    {
        var unavailable = new CapabilityInfo(CapabilityState.Unavailable, reason);
        return new JobCapabilitySet(unavailable, unavailable, unavailable, unavailable, unavailable, unavailable, unavailable);
    }
}

internal sealed record JobAccountingDisplay(
    uint ActiveProcessCount,
    uint TotalProcessCount,
    uint TerminatedProcessCount,
    TimeSpan TotalUserTime,
    TimeSpan TotalKernelTime,
    ulong ReadBytes,
    ulong WriteBytes)
{
    public static JobAccountingDisplay Empty { get; } = new(0, 0, 0, TimeSpan.Zero, TimeSpan.Zero, 0, 0);

    public string CpuTimeDisplay => (TotalUserTime + TotalKernelTime).ToString(@"d\.hh\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture);
    public string IoDisplay => $"{SizeFormatter.Format(ClampToLong(ReadBytes))} read · {SizeFormatter.Format(ClampToLong(WriteBytes))} written";

    private static long ClampToLong(ulong value) => value > long.MaxValue ? long.MaxValue : (long)value;
}

internal sealed record LimitViolationDisplay(string Kind, string Detail, DateTimeOffset ObservedAt);

internal sealed class LiveJobNotificationDisplay(
    uint rawMessageCode,
    string kind,
    int? processId,
    string detail,
    bool isError,
    DateTimeOffset receivedAt) : EventArgs
{
    public uint RawMessageCode { get; } = rawMessageCode;
    public string Kind { get; } = kind;
    public int? ProcessId { get; } = processId;
    public string Detail { get; } = detail;
    public bool IsError { get; } = isError;
    public DateTimeOffset ReceivedAt { get; } = receivedAt;

    public string ProcessIdDisplay => ProcessId?.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? "—";
    public string ReceivedAtDisplay => ReceivedAt.ToLocalTime().ToString("G", System.Globalization.CultureInfo.CurrentCulture);
}

internal sealed record JobSessionSnapshot(
    RestrictionProfile Restrictions,
    IReadOnlyList<int> ProcessIds,
    JobAccountingDisplay Accounting,
    IReadOnlyList<LimitViolationDisplay> LimitViolations);

internal sealed record AssignmentOutcome(
    int ProcessId,
    string ProcessName,
    bool Succeeded,
    string Message,
    bool WasAttempted = true)
{
    public string Status => Succeeded ? "Assigned" : WasAttempted ? "Failed" : "Not attempted";
}

internal sealed record LaunchRequest(string FileName, IReadOnlyList<string> Arguments, string? WorkingDirectory);

internal sealed record LaunchOutcome(int ProcessId, string ProcessName);
