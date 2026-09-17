using System.Net.Sockets;
using Shackles.Wfp.Internal;

namespace Shackles.Wfp.Tests;

[TestClass]
public sealed class WfpRuleNormalizerTests
{
    [TestMethod]
    public void NormalizeCanonicalizesExecutableInterfaceAndIpv4Scopes()
    {
        var executablePath = Path.Combine(
            Path.GetTempPath(),
            "Shackles.Wfp.Tests",
            "intermediate",
            "..",
            "target.exe");

        var normalized = WfpRuleNormalizer.Normalize(CreateOptions() with
        {
            ExecutablePath = $"  {executablePath}  ",
            IpVersions = WfpIpVersion.All,
            LocalNetwork = " 192.0.2.129/25 ",
            RemoteNetwork = "192.0.2.250",
            InterfaceLuid = 42,
            InterfaceName = "  Test Ethernet  "
        }, requireExistingExecutable: false);

        Assert.AreEqual(Path.GetFullPath(executablePath), normalized.ExecutablePath);
        Assert.AreEqual(WfpIpVersion.Ipv4, normalized.IpVersions);
        Assert.AreEqual("192.0.2.128/25", normalized.LocalNetwork?.CanonicalText);
        Assert.AreEqual("192.0.2.250/32", normalized.RemoteNetwork?.CanonicalText);
        Assert.AreEqual("Test Ethernet", normalized.InterfaceName);
        Assert.AreEqual(0xC0000280u, normalized.LocalNetwork?.GetIpv4AddressHostOrder());
        Assert.AreEqual(0xFFFFFF80u, normalized.LocalNetwork?.GetIpv4MaskHostOrder());
    }

    [TestMethod]
    [DataRow("2001:db8::1234/64", "2001:db8::/64", 64)]
    [DataRow("2001:db8::1", "2001:db8::1/128", 128)]
    public void ParseNetworkCanonicalizesIpv6Cidrs(
        string input,
        string expectedCanonicalText,
        int expectedPrefixLength)
    {
        var parsed = WfpRuleNormalizer.ParseNetwork(input, "remote");

        Assert.IsNotNull(parsed);
        Assert.AreEqual(AddressFamily.InterNetworkV6, parsed.AddressFamily);
        Assert.AreEqual(expectedCanonicalText, parsed.CanonicalText);
        Assert.AreEqual((byte)expectedPrefixLength, parsed.PrefixLength);
        Assert.HasCount(16, parsed.NetworkBytes);
    }

    [TestMethod]
    [DataRow("0.0.0.0/0", "::ffff:0:0/96", 96)]
    [DataRow("192.0.2.129/25", "::ffff:192.0.2.128/121", 121)]
    [DataRow("192.0.2.10", "::ffff:192.0.2.10/128", 128)]
    public void Ipv4NetworkConvertsToMappedIpv6Prefix(
        string input,
        string expectedCanonicalText,
        int expectedPrefixLength)
    {
        var parsed = WfpRuleNormalizer.ParseNetwork(input, "remote");

        Assert.IsNotNull(parsed);
        var mapped = parsed.ToIpv4MappedIpv6();
        Assert.AreEqual(AddressFamily.InterNetworkV6, mapped.AddressFamily);
        Assert.AreEqual(expectedCanonicalText, mapped.CanonicalText);
        Assert.AreEqual((byte)expectedPrefixLength, mapped.PrefixLength);
        CollectionAssert.AreEqual(
            new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xFF, 0xFF },
            mapped.NetworkBytes[..12]);
    }

    [TestMethod]
    [DataRow("not-an-address")]
    [DataRow("192.0.2.1/33")]
    [DataRow("2001:db8::1/129")]
    [DataRow("192.0.2.1/")]
    [DataRow("192.0.2.1/24/1")]
    [DataRow("fe80::1%1/64")]
    [DataRow("fe80::1%0/64")]
    [DataRow("::/0")]
    [DataRow("::/80")]
    [DataRow("::ffff:192.0.2.1/128")]
    public void ParseNetworkRejectsInvalidCidrs(string network)
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            WfpRuleNormalizer.ParseNetwork(network, "remote"));
    }

    [TestMethod]
    public void NormalizeRejectsMixedNetworkFamilies()
    {
        var exception = Assert.ThrowsExactly<ArgumentException>(() =>
            WfpRuleNormalizer.Normalize(CreateOptions() with
            {
                LocalNetwork = "192.0.2.0/24",
                RemoteNetwork = "2001:db8::/32"
            }, requireExistingExecutable: false));

        StringAssert.Contains(exception.Message, "same IP version");
    }

    [TestMethod]
    public void NormalizeRejectsNetworkFamilyThatIsNotSelected()
    {
        var exception = Assert.ThrowsExactly<ArgumentException>(() =>
            WfpRuleNormalizer.Normalize(CreateOptions() with
            {
                IpVersions = WfpIpVersion.Ipv4,
                RemoteNetwork = "2001:db8::/32"
            }, requireExistingExecutable: false));

        StringAssert.Contains(exception.Message, "IPv6");
    }

    [TestMethod]
    public void NormalizeRejectsMissingOrRelativeExecutablePaths()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            WfpRuleNormalizer.Normalize(CreateOptions() with
            {
                ExecutablePath = "   "
            }, requireExistingExecutable: false));

        Assert.ThrowsExactly<ArgumentException>(() =>
            WfpRuleNormalizer.Normalize(CreateOptions() with
            {
                ExecutablePath = "tools\\target.exe"
            }, requireExistingExecutable: false));
    }

    [TestMethod]
    public void NormalizeRequiresExecutableToExistByDefault()
    {
        var missingPath = Path.Combine(
            Path.GetTempPath(),
            "Shackles.Wfp.Tests",
            Guid.NewGuid().ToString("N"),
            "missing.exe");

        var exception = Assert.ThrowsExactly<FileNotFoundException>(() =>
            WfpRuleNormalizer.Normalize(CreateOptions() with
            {
                ExecutablePath = missingPath
            }));

        Assert.AreEqual(missingPath, exception.FileName);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(4)]
    [DataRow(7)]
    public void NormalizeRejectsInvalidDirectionFlags(int value)
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            WfpRuleNormalizer.Normalize(CreateOptions() with
            {
                Directions = (WfpTrafficDirection)value
            }, requireExistingExecutable: false));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(4)]
    [DataRow(7)]
    public void NormalizeRejectsInvalidIpVersionFlags(int value)
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            WfpRuleNormalizer.Normalize(CreateOptions() with
            {
                IpVersions = (WfpIpVersion)value
            }, requireExistingExecutable: false));
    }

    [TestMethod]
    [DataRow((int)WfpTransportProtocol.Any)]
    [DataRow((int)WfpTransportProtocol.Icmp)]
    public void NormalizeRejectsPortsForProtocolsWithoutPorts(int protocolValue)
    {
        var protocol = (WfpTransportProtocol)protocolValue;

        Assert.ThrowsExactly<ArgumentException>(() =>
            WfpRuleNormalizer.Normalize(CreateOptions() with
            {
                Protocol = protocol,
                LocalPort = 443
            }, requireExistingExecutable: false));

        Assert.ThrowsExactly<ArgumentException>(() =>
            WfpRuleNormalizer.Normalize(CreateOptions() with
            {
                Protocol = protocol,
                RemotePort = 443
            }, requireExistingExecutable: false));
    }

    [TestMethod]
    [DataRow((int)WfpTransportProtocol.Tcp)]
    [DataRow((int)WfpTransportProtocol.Udp)]
    public void NormalizeAcceptsPortsForTcpAndUdp(int protocolValue)
    {
        var protocol = (WfpTransportProtocol)protocolValue;

        var normalized = WfpRuleNormalizer.Normalize(CreateOptions() with
        {
            Protocol = protocol,
            LocalPort = 53,
            RemotePort = 5353
        }, requireExistingExecutable: false);

        Assert.AreEqual(protocol, normalized.Protocol);
        Assert.AreEqual((ushort)53, normalized.LocalPort);
        Assert.AreEqual((ushort)5353, normalized.RemotePort);
    }

    [TestMethod]
    public void NormalizeRejectsUndefinedProtocol()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            WfpRuleNormalizer.Normalize(CreateOptions() with
            {
                Protocol = (WfpTransportProtocol)int.MaxValue
            }, requireExistingExecutable: false));
    }

    [TestMethod]
    public void NormalizeRejectsZeroInterfaceLuidAndClearsBlankInterfaceName()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            WfpRuleNormalizer.Normalize(CreateOptions() with
            {
                InterfaceLuid = 0
            }, requireExistingExecutable: false));

        var normalized = WfpRuleNormalizer.Normalize(CreateOptions() with
        {
            InterfaceName = "   "
        }, requireExistingExecutable: false);

        Assert.IsNull(normalized.InterfaceName);
    }

    [TestMethod]
    public void NormalizeRejectsInterfaceNameWithoutLuid()
    {
        var exception = Assert.ThrowsExactly<ArgumentException>(() =>
            WfpRuleNormalizer.Normalize(CreateOptions() with
            {
                InterfaceName = "Ethernet"
            }, requireExistingExecutable: false));

        StringAssert.Contains(exception.Message, "interface LUID");
    }

    private static WfpBlockRuleOptions CreateOptions() => new(
        ExecutablePath: Path.Combine(Path.GetTempPath(), "Shackles.Wfp.Tests", "target.exe"),
        Directions: WfpTrafficDirection.Both,
        IpVersions: WfpIpVersion.All);
}
