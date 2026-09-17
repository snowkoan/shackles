namespace Shackles.Wfp;

[Flags]
public enum WfpIpVersion
{
    None = 0,
    Ipv4 = 1,
    Ipv6 = 2,
    All = Ipv4 | Ipv6
}

[Flags]
public enum WfpTrafficDirection
{
    None = 0,
    Outbound = 1,
    Inbound = 2,
    Both = Outbound | Inbound
}

public enum WfpTransportProtocol
{
    Any,
    Tcp,
    Udp,
    Icmp
}

public sealed record WfpBlockRuleOptions(
    string ExecutablePath,
    WfpTrafficDirection Directions,
    WfpIpVersion IpVersions,
    WfpTransportProtocol Protocol = WfpTransportProtocol.Any,
    ushort? LocalPort = null,
    ushort? RemotePort = null,
    string? LocalNetwork = null,
    string? RemoteNetwork = null,
    ulong? InterfaceLuid = null,
    string? InterfaceName = null,
    bool CurrentUserOnly = true);

public sealed record WfpInstalledFilter(
    Guid FilterKey,
    ulong FilterId,
    string DisplayName,
    WfpTrafficDirection Direction,
    WfpIpVersion IpVersion);

public sealed record WfpInstalledRule(
    Guid RuleKey,
    string DisplayName,
    DateTimeOffset InstalledAt,
    WfpBlockRuleOptions Options,
    IReadOnlyList<WfpInstalledFilter> Filters);

public sealed record WfpNetworkInterface(
    ulong Luid,
    string Name,
    string Description,
    string InterfaceType,
    string Status,
    uint? Ipv4Index,
    uint? Ipv6Index)
{
    public string DisplayName =>
        $"{Name} — {InterfaceType}, {Status}";
}

public sealed record WfpSupportInfo(
    bool IsAvailable,
    bool IsHighIntegrity,
    string Summary);

public sealed record WfpCloseResult(
    int RemovedRuleCount,
    int RemovedFilterCount,
    bool ExplicitRemovalSucceeded,
    bool DynamicSessionClosed,
    IReadOnlyList<string> Warnings);
