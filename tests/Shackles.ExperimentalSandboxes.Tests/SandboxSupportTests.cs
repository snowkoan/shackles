using Shackles.ExperimentalSandboxes.Internal;

namespace Shackles.ExperimentalSandboxes.Tests;

[TestClass]
public sealed class SandboxSupportTests
{
    [TestMethod]
    [TestCategory("WindowsIntegration")]
    public void ProbeAlwaysReportsKnownFeatureIds()
    {
        using var manager = new ExperimentalSandboxManager();
        var support = manager.Support;

        Assert.IsFalse(string.IsNullOrWhiteSpace(support.Summary));
        CollectionAssert.AreEquivalent(
            new uint[] { 61389575, 61155944 },
            support.RequiredFeatures.Select(feature => feature.Id).ToArray());
    }

    [TestMethod]
    [TestCategory("WindowsIntegration")]
    public void RefreshSupportReturnsCurrentSnapshot()
    {
        using var manager = new ExperimentalSandboxManager();

        var refreshed = manager.RefreshSupport();

        Assert.AreEqual(manager.Support, refreshed);
    }

    [TestMethod]
    public void DecodeFeatureConfigurationReadsEnabledUserOverride()
    {
        Assert.AreEqual(
            ExperimentalFeatureConfigurationState.Enabled,
            SandboxSupportProbe.DecodeFeatureConfigurationState(0x28));
    }

    [TestMethod]
    [DataRow(50, true)]
    [DataRow(120, true)]
    [DataRow(unchecked((int)0x80004001), true)]
    [DataRow(0, false)]
    [DataRow(5, false)]
    [DataRow(87, false)]
    public void OnlyUnsupportedErrorsIndicateAnUnavailableContract(int error, bool unsupported)
    {
        Assert.AreEqual(unsupported, SandboxSupportProbe.IsUnsupportedError(error));
    }
}
