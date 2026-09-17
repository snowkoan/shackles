using Shackles.Wfp.Interop;

namespace Shackles.Wfp.Internal;

internal enum Ipv6MappedExclusionPartition
{
    None,
    BelowMappedRange,
    AboveMappedRange
}

internal sealed record WfpFilterPlan(
    WfpTrafficDirection Direction,
    WfpIpVersion IpVersion,
    WfpIpVersion LayerIpVersion,
    Guid LayerKey,
    byte? IpProtocol,
    bool MatchesIpv4Mapped,
    Ipv6MappedExclusionPartition Ipv6Partition)
{
    internal string LayerLabel =>
        $"{(Direction == WfpTrafficDirection.Outbound ? "out" : "in")}-" +
        (MatchesIpv4Mapped
            ? "v6-mapped-v4"
            : Ipv6Partition == Ipv6MappedExclusionPartition.BelowMappedRange
                ? "v6-native-low"
                : Ipv6Partition == Ipv6MappedExclusionPartition.AboveMappedRange
                    ? "v6-native-high"
            : LayerIpVersion == WfpIpVersion.Ipv4 ? "v4" : "v6");
}

internal static class WfpFilterPlanBuilder
{
    internal static IReadOnlyList<WfpFilterPlan> Create(NormalizedWfpRule rule)
    {
        var plans = new List<WfpFilterPlan>(4);
        AddDirection(WfpTrafficDirection.Outbound);
        AddDirection(WfpTrafficDirection.Inbound);
        return plans;

        void AddDirection(WfpTrafficDirection direction)
        {
            if (!rule.Directions.HasFlag(direction))
            {
                return;
            }

            AddVersion(WfpIpVersion.Ipv4);
            AddVersion(WfpIpVersion.Ipv6);

            // A true dual-stack socket can carry IPv4 traffic while Windows
            // classifies it only at the V6 ALE layer. A V4-only policy needs a
            // V6 companion constrained to the IPv4-mapped address range. An
            // all-family ICMP rule needs it too because its broad V6 filter
            // matches ICMPv6 protocol 58, not mapped IPv4 ICMP protocol 1.
            if (rule.IpVersions.HasFlag(WfpIpVersion.Ipv4) &&
                (rule.IpVersions == WfpIpVersion.Ipv4 ||
                 rule.Protocol == WfpTransportProtocol.Icmp))
            {
                plans.Add(new WfpFilterPlan(
                    direction,
                    WfpIpVersion.Ipv4,
                    WfpIpVersion.Ipv6,
                    GetLayer(direction, WfpIpVersion.Ipv6),
                    GetProtocol(rule.Protocol, WfpIpVersion.Ipv4),
                    MatchesIpv4Mapped: true,
                    Ipv6MappedExclusionPartition.None));
            }

            void AddVersion(WfpIpVersion version)
            {
                if (!rule.IpVersions.HasFlag(version))
                {
                    return;
                }

                if (version == WfpIpVersion.Ipv6 &&
                    rule.IpVersions == WfpIpVersion.Ipv6 &&
                    rule.LocalNetwork is null &&
                    rule.RemoteNetwork is null)
                {
                    AddPlan(Ipv6MappedExclusionPartition.BelowMappedRange);
                    AddPlan(Ipv6MappedExclusionPartition.AboveMappedRange);
                }
                else
                {
                    AddPlan(Ipv6MappedExclusionPartition.None);
                }

                void AddPlan(Ipv6MappedExclusionPartition partition) =>
                    plans.Add(new WfpFilterPlan(
                        direction,
                        version,
                        version,
                        GetLayer(direction, version),
                        GetProtocol(rule.Protocol, version),
                        MatchesIpv4Mapped: false,
                        partition));
            }
        }
    }

    internal static (byte[] Boundary, FwpMatchType MatchType)
        GetIpv6MappedBoundary(Ipv6MappedExclusionPartition partition)
    {
        var boundary = new byte[16];
        boundary[10] = 0xFF;
        boundary[11] = 0xFF;
        var matchType = partition switch
        {
            Ipv6MappedExclusionPartition.BelowMappedRange => FwpMatchType.Less,
            Ipv6MappedExclusionPartition.AboveMappedRange => FwpMatchType.Greater,
            _ => throw new ArgumentOutOfRangeException(nameof(partition))
        };
        if (partition == Ipv6MappedExclusionPartition.AboveMappedRange)
        {
            Array.Fill(boundary, (byte)0xFF, 12, 4);
        }

        return (boundary, matchType);
    }

    private static Guid GetLayer(
        WfpTrafficDirection direction,
        WfpIpVersion version) => (direction, version) switch
    {
        (WfpTrafficDirection.Outbound, WfpIpVersion.Ipv4) => WfpNativeConstants.AleAuthConnectV4,
        (WfpTrafficDirection.Outbound, WfpIpVersion.Ipv6) => WfpNativeConstants.AleAuthConnectV6,
        (WfpTrafficDirection.Inbound, WfpIpVersion.Ipv4) => WfpNativeConstants.AleAuthReceiveAcceptV4,
        (WfpTrafficDirection.Inbound, WfpIpVersion.Ipv6) => WfpNativeConstants.AleAuthReceiveAcceptV6,
        _ => throw new ArgumentOutOfRangeException(nameof(direction))
    };

    private static byte? GetProtocol(
        WfpTransportProtocol protocol,
        WfpIpVersion version) => protocol switch
    {
        WfpTransportProtocol.Any => null,
        WfpTransportProtocol.Tcp => 6,
        WfpTransportProtocol.Udp => 17,
        WfpTransportProtocol.Icmp when version == WfpIpVersion.Ipv4 => 1,
        WfpTransportProtocol.Icmp => 58,
        _ => throw new ArgumentOutOfRangeException(nameof(protocol))
    };
}
