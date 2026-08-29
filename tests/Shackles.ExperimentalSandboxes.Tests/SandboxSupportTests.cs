using Shackles.ExperimentalSandboxes.Internal;

namespace Shackles.ExperimentalSandboxes.Tests;

[TestClass]
public sealed class SandboxSupportTests
{
    [TestMethod]
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
}
