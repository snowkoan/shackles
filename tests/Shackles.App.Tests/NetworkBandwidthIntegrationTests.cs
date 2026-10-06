using System.Globalization;
using Shackles.App.Models;
using Shackles.App.Services;
using Shackles.App.ViewModels;

namespace Shackles.App.Tests;

[TestClass]
[TestCategory("WindowsIntegration")]
public sealed class NetworkBandwidthIntegrationTests
{
    [TestMethod]
    public void FractionalUploadLimitIsAppliedAndReadBackAsExactBytes()
    {
        using var service = new JobControlService();
        if (!service.Capabilities.NetworkRateControl.CanSet)
        {
            Assert.Inconclusive(service.Capabilities.NetworkRateControl.Reason);
        }

        using var session = service.CreateJob(null);
        var editor = new RestrictionEditorViewModel(session.HasOwnedNotificationDelivery);
        editor.Load(session.GetSnapshot().Restrictions);
        editor.NetworkBandwidthEnabled = true;
        editor.NetworkBandwidthValue = 0.0625m.ToString(CultureInfo.CurrentCulture);
        editor.NetworkBandwidthUnit = NetworkBandwidthUnit.KilobytesPerSecond;
        Assert.IsTrue(editor.TryBuild(out var profile), editor.ValidationMessage);

        session.ApplyRestrictions(profile);
        var actual = session.GetSnapshot().Restrictions;
        Assert.AreEqual(62_500UL, actual.Network.ExactMaximumBandwidthBytesPerSecond);

        editor.MarkApplied(actual);
        Assert.AreEqual(NetworkBandwidthUnit.KilobytesPerSecond, editor.NetworkBandwidthUnit);
        Assert.AreEqual(62.5m.ToString(CultureInfo.CurrentCulture), editor.NetworkBandwidthValue);
        Assert.IsFalse(editor.IsDirty);
    }
}
