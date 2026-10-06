using System.Globalization;
using Shackles.App.Models;
using Shackles.App.ViewModels;

namespace Shackles.App.Tests;

[TestClass]
public sealed class NetworkBandwidthEditorTests
{
    private static readonly string[] ByteUnitSymbols = ["B/s", "kB/s", "MB/s"];

    [TestMethod]
    public void UploadEditorOffersOnlyBytesAndDefaultsToMegabytes()
    {
        using var culture = new CultureScope("en-CA");
        var editor = CreateEditor();

        CollectionAssert.AreEqual(
            ByteUnitSymbols,
            editor.NetworkBandwidthUnits.Select(unit => unit.Symbol).ToArray());
        Assert.AreEqual(NetworkBandwidthUnit.MegabytesPerSecond, editor.NetworkBandwidthUnit);
        Assert.AreEqual("1.25", editor.NetworkBandwidthValue);

        editor.NetworkBandwidthEnabled = true;
        Assert.IsTrue(editor.TryBuild(out var profile), editor.ValidationMessage);
        Assert.AreEqual(1_250_000UL, profile.Network.ExactMaximumBandwidthBytesPerSecond);
    }

    [TestMethod]
    [DataRow("B/s", "62500")]
    [DataRow("kB/s", "62.5")]
    [DataRow("MB/s", "0.0625")]
    public void FractionalUploadRateHasSameNativeValueInEveryUnit(string symbol, string value)
    {
        using var culture = new CultureScope("en-CA");
        var editor = CreateEditor();
        editor.NetworkBandwidthUnit = FindUnit(editor, symbol);
        editor.NetworkBandwidthEnabled = true;
        editor.NetworkBandwidthValue = value;

        Assert.IsTrue(editor.TryBuild(out var profile), editor.ValidationMessage);
        Assert.AreEqual(62_500UL, profile.Network.ExactMaximumBandwidthBytesPerSecond);
        Assert.AreEqual(0.5d, profile.Network.MaximumBandwidthMegabitsPerSecond);
    }

    [TestMethod]
    [DataRow(1UL)]
    [DataRow(123_457UL)]
    [DataRow(ulong.MaxValue)]
    public void UnitChangesPreserveExactNativeRateWithoutCreatingAnEdit(ulong bytesPerSecond)
    {
        var editor = CreateEditor();
        editor.Load(NetworkProfile(bytesPerSecond));

        foreach (var unit in editor.NetworkBandwidthUnits)
        {
            editor.NetworkBandwidthUnit = unit;
            Assert.IsFalse(editor.IsDirty, unit.Symbol);
            Assert.IsTrue(editor.TryBuild(out var profile), editor.ValidationMessage);
            Assert.AreEqual(bytesPerSecond, profile.Network.ExactMaximumBandwidthBytesPerSecond, unit.Symbol);
        }
    }

    [TestMethod]
    public void UnitChangesPreservePendingEditsAndRefreshKeepsChosenUnit()
    {
        using var culture = new CultureScope("en-CA");
        var editor = CreateEditor();
        editor.Load(NetworkProfile(125_000));
        editor.NetworkBandwidthValue = "0.0625";
        editor.KillOnJobClose = true;
        editor.NetworkBandwidthUnit = NetworkBandwidthUnit.KilobytesPerSecond;

        Assert.AreEqual("62.5", editor.NetworkBandwidthValue);
        Assert.IsTrue(editor.IsDirty);
        Assert.IsTrue(editor.TryBuild(out var draft), editor.ValidationMessage);
        Assert.AreEqual(62_500UL, draft.Network.ExactMaximumBandwidthBytesPerSecond);
        Assert.IsTrue(draft.HardLimits.KillOnJobClose);

        editor.MarkApplied(draft);
        Assert.AreEqual(NetworkBandwidthUnit.KilobytesPerSecond, editor.NetworkBandwidthUnit);
        Assert.AreEqual("62.5", editor.NetworkBandwidthValue);
        Assert.IsFalse(editor.IsDirty);
    }

    [TestMethod]
    public void UnrelatedEditPreservesNativeValueBeyondDoubleIntegerPrecision()
    {
        const ulong bytesPerSecond = 9_007_199_254_740_993;
        var editor = CreateEditor();
        editor.Load(NetworkProfile(bytesPerSecond));
        editor.KillOnJobClose = true;

        Assert.IsTrue(editor.TryBuild(out var profile), editor.ValidationMessage);
        Assert.AreEqual(bytesPerSecond, profile.Network.ExactMaximumBandwidthBytesPerSecond);
    }

    [TestMethod]
    public void FractionalBytesRoundToNearestWholeByte()
    {
        using var culture = new CultureScope("en-CA");
        var editor = CreateEditor();
        editor.NetworkBandwidthUnit = NetworkBandwidthUnit.BytesPerSecond;
        editor.NetworkBandwidthEnabled = true;
        editor.NetworkBandwidthValue = "1.5";
        editor.NetworkBandwidthUnit = NetworkBandwidthUnit.MegabytesPerSecond;

        Assert.IsTrue(editor.TryBuild(out var profile), editor.ValidationMessage);
        Assert.AreEqual(2UL, profile.Network.ExactMaximumBandwidthBytesPerSecond);
    }

    [TestMethod]
    public void UnitConversionDoesNotMoveHighPrecisionDraftAcrossHalfByteBoundary()
    {
        using var culture = new CultureScope("en-CA");
        var editor = CreateEditor();
        editor.NetworkBandwidthUnit = NetworkBandwidthUnit.BytesPerSecond;
        editor.NetworkBandwidthEnabled = true;
        editor.NetworkBandwidthValue = "1.4999999999999999999999999999";
        Assert.IsTrue(editor.TryBuild(out var before), editor.ValidationMessage);
        Assert.AreEqual(1UL, before.Network.ExactMaximumBandwidthBytesPerSecond);

        editor.NetworkBandwidthUnit = NetworkBandwidthUnit.MegabytesPerSecond;
        Assert.IsTrue(editor.TryBuild(out var after), editor.ValidationMessage);
        Assert.AreEqual(before.Network.ExactMaximumBandwidthBytesPerSecond, after.Network.ExactMaximumBandwidthBytesPerSecond);

        editor.NetworkBandwidthValue = "0.0000016";
        Assert.IsTrue(editor.TryBuild(out var edited), editor.ValidationMessage);
        Assert.AreEqual(2UL, edited.Network.ExactMaximumBandwidthBytesPerSecond);

        editor.Load(NetworkProfile(3));
        Assert.IsTrue(editor.TryBuild(out var refreshed), editor.ValidationMessage);
        Assert.AreEqual(3UL, refreshed.Network.ExactMaximumBandwidthBytesPerSecond);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("0")]
    [DataRow("-1")]
    [DataRow("0.999")]
    [DataRow("18446744073709551616")]
    [DataRow("NaN")]
    [DataRow("Infinity")]
    public void InvalidNativeRateIsRejectedBeforeApplying(string value)
    {
        using var culture = new CultureScope("en-CA");
        var editor = CreateEditor();
        editor.NetworkBandwidthUnit = NetworkBandwidthUnit.BytesPerSecond;
        editor.NetworkBandwidthEnabled = true;
        editor.NetworkBandwidthValue = value;

        Assert.IsFalse(editor.TryBuild(out _));
        Assert.IsTrue(editor.HasValidationMessage);
        Assert.IsFalse(editor.CanChangeNetworkBandwidthUnit);
    }

    [TestMethod]
    public void IncompleteInputKeepsUnitAndCanBeCorrected()
    {
        using var culture = new CultureScope("en-CA");
        var editor = CreateEditor();
        editor.NetworkBandwidthEnabled = true;
        editor.NetworkBandwidthValue = "-";
        editor.NetworkBandwidthUnit = NetworkBandwidthUnit.KilobytesPerSecond;

        Assert.AreEqual(NetworkBandwidthUnit.MegabytesPerSecond, editor.NetworkBandwidthUnit);
        Assert.AreEqual("-", editor.NetworkBandwidthValue);
        editor.NetworkBandwidthValue = "0.0625";
        Assert.IsTrue(editor.CanChangeNetworkBandwidthUnit);
        editor.NetworkBandwidthUnit = NetworkBandwidthUnit.KilobytesPerSecond;
        Assert.AreEqual("62.5", editor.NetworkBandwidthValue);
    }

    [TestMethod]
    public void DisabledBandwidthIgnoresInvalidLatentTextAndKeepsDscp()
    {
        var editor = CreateEditor();
        editor.NetworkBandwidthValue = "invalid";
        editor.DscpEnabled = true;
        editor.DscpTag = "12";

        Assert.IsTrue(editor.TryBuild(out var profile), editor.ValidationMessage);
        Assert.IsNull(profile.Network.ExactMaximumBandwidthBytesPerSecond);
        Assert.IsNull(profile.Network.MaximumBandwidthMegabitsPerSecond);
        Assert.AreEqual((byte)12, profile.Network.DscpTag);
    }

    [TestMethod]
    public void FractionalInputAndUnitConversionUseCurrentCulture()
    {
        using var culture = new CultureScope("fr-CA");
        var editor = CreateEditor();
        editor.NetworkBandwidthEnabled = true;
        editor.NetworkBandwidthValue = "0,0625";
        editor.NetworkBandwidthUnit = NetworkBandwidthUnit.KilobytesPerSecond;

        Assert.AreEqual("62,5", editor.NetworkBandwidthValue);
        Assert.IsTrue(editor.TryBuild(out var profile), editor.ValidationMessage);
        Assert.AreEqual(62_500UL, profile.Network.ExactMaximumBandwidthBytesPerSecond);
    }

    [TestMethod]
    public void LegacyMegabitProfileLoadsAsExactBytes()
    {
        var editor = CreateEditor();
        editor.Load(RestrictionProfile.Empty with { Network = new NetworkControlSettings(0.5) });

        Assert.IsTrue(editor.TryBuild(out var profile), editor.ValidationMessage);
        Assert.AreEqual(62_500UL, profile.Network.ExactMaximumBandwidthBytesPerSecond);
    }

    private static RestrictionEditorViewModel CreateEditor()
    {
        var editor = new RestrictionEditorViewModel(canPostEndOfJobNotification: false);
        editor.Load(RestrictionProfile.Empty);
        return editor;
    }

    private static NetworkBandwidthUnit FindUnit(RestrictionEditorViewModel editor, string symbol) =>
        editor.NetworkBandwidthUnits.Single(unit => unit.Symbol == symbol);

    private static RestrictionProfile NetworkProfile(ulong bytesPerSecond) =>
        RestrictionProfile.Empty with
        {
            Network = new NetworkControlSettings(
                bytesPerSecond * 8d / 1_000_000d,
                ExactMaximumBandwidthBytesPerSecond: bytesPerSecond)
        };

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

        public CultureScope(string name) => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);

        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }
}
