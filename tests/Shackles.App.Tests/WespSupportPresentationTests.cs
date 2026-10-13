using Shackles.App.Views;
using Shackles.Wesp;

namespace Shackles.App.Tests;

[TestClass]
public sealed class WespSupportPresentationTests
{
    [TestMethod]
    public void MissingClientGuidanceIsVisibleBeforeElevation()
    {
        var support = new WespSupportInfo(
            WespAvailability.ClientLibraryMissing, "The WESP client DLL was not found.",
            @"C:\Windows\System32\espclient.dll", [])
        {
            CheckedClientLibraryPaths = [@"C:\Shackles\espclient.dll", @"C:\Windows\System32\espclient.dll"]
        };

        var detail = WespWorkspaceView.FormatSupportDetail(support, hasRequiredIntegrity: false);

        StringAssert.Contains(detail, @"local-dependencies\wesp\espclient.dll");
        StringAssert.Contains(detail, "authorized x64");
        StringAssert.Contains(detail, "Refresh");
        StringAssert.Contains(detail, @"C:\Shackles\espclient.dll");
        StringAssert.Contains(detail, @"C:\Windows\System32\espclient.dll");
        StringAssert.Contains(detail, "administrator");
    }

    [TestMethod]
    public void AvailableClientStillRequiresElevationForEnforcement()
    {
        var support = new WespSupportInfo(
            WespAvailability.Available, "Client API found. Driver support is checked when you start a session.",
            @"C:\Shackles\espclient.dll", []);
        var detail = WespWorkspaceView.FormatSupportDetail(support, hasRequiredIntegrity: false);

        StringAssert.Contains(detail, "administrator");
        StringAssert.Contains(detail, "Driver");
        Assert.IsFalse(detail.Contains("was not found", StringComparison.Ordinal));
    }

    [TestMethod]
    public void MissingAndIncompatibleClientsHaveDistinctStatusLabels()
    {
        var missing = WespWorkspaceView.GetSupportStateText(WespAvailability.ClientLibraryMissing);
        var incompatible = WespWorkspaceView.GetSupportStateText(WespAvailability.ClientLibraryArchitectureNotSupported);

        Assert.AreNotEqual(missing, incompatible);
        StringAssert.Contains(missing.ToLowerInvariant(), "missing");
        StringAssert.Contains(incompatible.ToLowerInvariant(), "x64");
    }
}
