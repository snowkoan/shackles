using Shackles.Wfp.Internal;
using Shackles.Wfp.Interop;

namespace Shackles.Wfp.Tests;

[TestClass]
public sealed class WfpFilterPlanBuilderTests
{
    private static readonly byte[] LowerMappedBoundary =
        [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xFF, 0xFF, 0, 0, 0, 0];

    private static readonly byte[] UpperMappedBoundary =
        [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];

    private static readonly Ipv6MappedExclusionPartition[] NativeIpv6Partitions =
        [
            Ipv6MappedExclusionPartition.BelowMappedRange,
            Ipv6MappedExclusionPartition.AboveMappedRange
        ];

    private static readonly string[] NativeIpv6Labels =
        ["in-v6-native-low", "in-v6-native-high"];

    [TestMethod]
    public void CreateMapsDirectionsAndIpVersionsToAleAuthorizationLayers()
    {
        var plans = WfpFilterPlanBuilder.Create(CreateRule(
            WfpTrafficDirection.Both,
            WfpIpVersion.All,
            WfpTransportProtocol.Any));

        Assert.HasCount(4, plans);
        AssertPlan(
            plans,
            WfpTrafficDirection.Outbound,
            WfpIpVersion.Ipv4,
            WfpIpVersion.Ipv4,
            WfpNativeConstants.AleAuthConnectV4,
            expectedProtocol: null,
            expectedLabel: "out-v4",
            expectedMapped: false,
            expectedPartition: Ipv6MappedExclusionPartition.None);
        AssertPlan(
            plans,
            WfpTrafficDirection.Outbound,
            WfpIpVersion.Ipv6,
            WfpIpVersion.Ipv6,
            WfpNativeConstants.AleAuthConnectV6,
            expectedProtocol: null,
            expectedLabel: "out-v6",
            expectedMapped: false,
            expectedPartition: Ipv6MappedExclusionPartition.None);
        AssertPlan(
            plans,
            WfpTrafficDirection.Inbound,
            WfpIpVersion.Ipv4,
            WfpIpVersion.Ipv4,
            WfpNativeConstants.AleAuthReceiveAcceptV4,
            expectedProtocol: null,
            expectedLabel: "in-v4",
            expectedMapped: false,
            expectedPartition: Ipv6MappedExclusionPartition.None);
        AssertPlan(
            plans,
            WfpTrafficDirection.Inbound,
            WfpIpVersion.Ipv6,
            WfpIpVersion.Ipv6,
            WfpNativeConstants.AleAuthReceiveAcceptV6,
            expectedProtocol: null,
            expectedLabel: "in-v6",
            expectedMapped: false,
            expectedPartition: Ipv6MappedExclusionPartition.None);
    }

    [TestMethod]
    public void CreateUsesFamilySpecificIcmpProtocolNumbers()
    {
        var plans = WfpFilterPlanBuilder.Create(CreateRule(
            WfpTrafficDirection.Outbound,
            WfpIpVersion.All,
            WfpTransportProtocol.Icmp));

        Assert.HasCount(3, plans);
        Assert.AreEqual(
            (byte?)1,
            plans.Single(plan =>
                plan.IpVersion == WfpIpVersion.Ipv4 &&
                !plan.MatchesIpv4Mapped).IpProtocol);
        Assert.AreEqual(
            (byte?)58,
            plans.Single(plan => plan.IpVersion == WfpIpVersion.Ipv6).IpProtocol);
    }

    [TestMethod]
    [DataRow((int)WfpTransportProtocol.Any, null)]
    [DataRow((int)WfpTransportProtocol.Tcp, 6)]
    [DataRow((int)WfpTransportProtocol.Udp, 17)]
    public void CreateMapsTransportProtocols(int protocolValue, int? expectedProtocol)
    {
        var plans = WfpFilterPlanBuilder.Create(CreateRule(
            WfpTrafficDirection.Outbound,
            WfpIpVersion.Ipv4,
            (WfpTransportProtocol)protocolValue));

        Assert.HasCount(2, plans);
        Assert.AreEqual(
            expectedProtocol.HasValue ? (byte?)expectedProtocol.Value : null,
            plans.Single(plan => !plan.MatchesIpv4Mapped).IpProtocol);
    }

    [TestMethod]
    [DataRow((int)WfpTrafficDirection.Outbound, (int)WfpIpVersion.Ipv4, 2)]
    [DataRow((int)WfpTrafficDirection.Inbound, (int)WfpIpVersion.Ipv6, 2)]
    [DataRow((int)WfpTrafficDirection.Both, (int)WfpIpVersion.Ipv4, 4)]
    [DataRow((int)WfpTrafficDirection.Outbound, (int)WfpIpVersion.All, 2)]
    [DataRow((int)WfpTrafficDirection.Both, (int)WfpIpVersion.All, 4)]
    public void CreateProducesOneUniquePlanPerSelectedDirectionAndVersion(
        int directionValue,
        int ipVersionValue,
        int expectedCount)
    {
        var plans = WfpFilterPlanBuilder.Create(CreateRule(
            (WfpTrafficDirection)directionValue,
            (WfpIpVersion)ipVersionValue,
            WfpTransportProtocol.Tcp));

        Assert.HasCount(expectedCount, plans);
        Assert.AreEqual(
            plans.Count,
            plans.Select(plan => (plan.Direction, plan.LayerKey, plan.MatchesIpv4Mapped, plan.Ipv6Partition)).Distinct().Count());
        Assert.AreEqual(
            plans.Count,
            plans.Select(plan => (plan.LayerKey, plan.MatchesIpv4Mapped, plan.Ipv6Partition)).Distinct().Count());
    }

    [TestMethod]
    public void CreateAddsV6CompanionForIpv4OnlyDualStackTraffic()
    {
        var plans = WfpFilterPlanBuilder.Create(CreateRule(
            WfpTrafficDirection.Outbound,
            WfpIpVersion.Ipv4,
            WfpTransportProtocol.Tcp));

        var companion = plans.Single(plan => plan.MatchesIpv4Mapped);
        Assert.AreEqual(WfpTrafficDirection.Outbound, companion.Direction);
        Assert.AreEqual(WfpIpVersion.Ipv4, companion.IpVersion);
        Assert.AreEqual(WfpIpVersion.Ipv6, companion.LayerIpVersion);
        Assert.AreEqual(WfpNativeConstants.AleAuthConnectV6, companion.LayerKey);
        Assert.AreEqual((byte?)6, companion.IpProtocol);
        Assert.AreEqual("out-v6-mapped-v4", companion.LayerLabel);
        Assert.AreEqual(Ipv6MappedExclusionPartition.None, companion.Ipv6Partition);
    }

    [TestMethod]
    public void Ipv4IcmpCompanionUsesLogicalIpv4Protocol()
    {
        var plans = WfpFilterPlanBuilder.Create(CreateRule(
            WfpTrafficDirection.Outbound,
            WfpIpVersion.Ipv4,
            WfpTransportProtocol.Icmp));

        Assert.AreEqual(
            (byte?)1,
            plans.Single(plan => plan.MatchesIpv4Mapped).IpProtocol);
    }

    [TestMethod]
    public void UnscopedIpv6OnlyPlanExcludesMappedIpv4Traffic()
    {
        var plans = WfpFilterPlanBuilder.Create(CreateRule(
            WfpTrafficDirection.Inbound,
            WfpIpVersion.Ipv6,
            WfpTransportProtocol.Any));

        Assert.HasCount(2, plans);
        CollectionAssert.AreEquivalent(
            NativeIpv6Partitions,
            plans.Select(plan => plan.Ipv6Partition).ToArray());
        CollectionAssert.AreEquivalent(
            NativeIpv6Labels,
            plans.Select(plan => plan.LayerLabel).ToArray());
    }

    [TestMethod]
    public void NativeIpv6PartitionsUseSortableMappedRangeBoundaries()
    {
        var (lowBoundary, lowMatch) = WfpFilterPlanBuilder.GetIpv6MappedBoundary(
            Ipv6MappedExclusionPartition.BelowMappedRange);
        var (highBoundary, highMatch) = WfpFilterPlanBuilder.GetIpv6MappedBoundary(
            Ipv6MappedExclusionPartition.AboveMappedRange);

        CollectionAssert.AreEqual(
            LowerMappedBoundary,
            lowBoundary);
        CollectionAssert.AreEqual(
            UpperMappedBoundary,
            highBoundary);
        Assert.AreEqual(FwpMatchType.Less, lowMatch);
        Assert.AreEqual(FwpMatchType.Greater, highMatch);
    }

    private static void AssertPlan(
        IReadOnlyList<WfpFilterPlan> plans,
        WfpTrafficDirection direction,
        WfpIpVersion version,
        WfpIpVersion expectedLayerVersion,
        Guid expectedLayer,
        byte? expectedProtocol,
        string expectedLabel,
        bool expectedMapped,
        Ipv6MappedExclusionPartition expectedPartition)
    {
        var plan = plans.Single(candidate =>
            candidate.Direction == direction && candidate.IpVersion == version);
        Assert.AreEqual(expectedLayer, plan.LayerKey);
        Assert.AreEqual(expectedLayerVersion, plan.LayerIpVersion);
        Assert.AreEqual(expectedProtocol, plan.IpProtocol);
        Assert.AreEqual(expectedLabel, plan.LayerLabel);
        Assert.AreEqual(expectedMapped, plan.MatchesIpv4Mapped);
        Assert.AreEqual(expectedPartition, plan.Ipv6Partition);
    }

    private static NormalizedWfpRule CreateRule(
        WfpTrafficDirection directions,
        WfpIpVersion versions,
        WfpTransportProtocol protocol) => new(
            ExecutablePath: @"C:\Program Files\Shackles.Tests\target.exe",
            Directions: directions,
            IpVersions: versions,
            Protocol: protocol,
            LocalPort: null,
            RemotePort: null,
            LocalNetwork: null,
            RemoteNetwork: null,
            InterfaceLuid: null,
            InterfaceName: null,
            CurrentUserOnly: true);
}
