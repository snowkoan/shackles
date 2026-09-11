using Shackles.Wesp.Internal;

namespace Shackles.Wesp.Tests;

[TestClass]
public sealed class WespProcessIntegrityProbeTests
{
    [TestMethod]
    [DataRow(0x0000u)]
    [DataRow(0x1000u)]
    [DataRow(0x2000u)]
    [DataRow(0x2100u)]
    [DataRow(0x2FFFu)]
    public void IntegrityBelowHighIsRejected(uint integrityRid)
    {
        Assert.IsFalse(WespProcessIntegrityProbe.IsHighIntegrity(integrityRid));
    }

    [TestMethod]
    [DataRow(0x3000u)]
    [DataRow(0x4000u)]
    [DataRow(0x5000u)]
    public void HighAndMoreTrustedIntegrityLevelsAreAccepted(uint integrityRid)
    {
        Assert.IsTrue(WespProcessIntegrityProbe.IsHighIntegrity(integrityRid));
    }

    [TestMethod]
    [DataRow(0x0000u, "untrusted")]
    [DataRow(0x1000u, "low")]
    [DataRow(0x2000u, "medium")]
    [DataRow(0x2100u, "medium-plus")]
    [DataRow(0x3000u, "high")]
    [DataRow(0x4000u, "system")]
    [DataRow(0x5000u, "protected-process")]
    public void IntegrityLevelDescriptionIsActionable(uint integrityRid, string expected)
    {
        Assert.AreEqual(expected, WespProcessIntegrityProbe.DescribeIntegrity(integrityRid));
    }
}
