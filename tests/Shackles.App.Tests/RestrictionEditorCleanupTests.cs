using System.Globalization;
using Shackles.App.Models;
using Shackles.App.ViewModels;

namespace Shackles.App.Tests;

[TestClass]
public sealed class RestrictionEditorCleanupTests
{
    [TestMethod]
    [DataRow((int)CpuControlMode.Disabled, false, false, false, false)]
    [DataRow((int)CpuControlMode.Rate, true, false, false, true)]
    [DataRow((int)CpuControlMode.HardCap, true, false, false, true)]
    [DataRow((int)CpuControlMode.Weight, false, true, false, true)]
    [DataRow((int)CpuControlMode.MinimumMaximum, false, false, true, true)]
    public void CpuModeEnablesOnlyItsRelevantInputs(int mode, bool rate, bool weight, bool range, bool notifications)
    {
        var editor = CreateEditor();
        editor.CpuMode = (CpuControlMode)mode;

        Assert.AreEqual(rate, editor.IsCpuRateInputEnabled);
        Assert.AreEqual(weight, editor.IsCpuWeightInputEnabled);
        Assert.AreEqual(range, editor.IsCpuMinimumMaximumInputEnabled);
        Assert.AreEqual(notifications, editor.IsCpuNotifyInputEnabled);
    }

    [TestMethod]
    public void SwitchingCpuModePreservesDraftAndValidatesOnlyActiveInputs()
    {
        var editor = CreateEditor();
        editor.CpuMode = CpuControlMode.Weight;
        editor.CpuWeight = "7";
        editor.CpuRatePercent = "invalid rate";
        editor.CpuMinimumPercent = "invalid minimum";
        Assert.IsTrue(editor.TryBuild(out var weight), editor.ValidationMessage);
        Assert.AreEqual(7u, weight.Cpu.Weight);

        editor.CpuMode = CpuControlMode.HardCap;
        Assert.AreEqual("invalid rate", editor.CpuRatePercent);
        Assert.IsFalse(editor.TryBuild(out _));
        Assert.AreEqual(nameof(editor.CpuRatePercent), editor.ValidationPropertyName);

        editor.CpuRatePercent = "25";
        Assert.IsTrue(editor.TryBuild(out var hardCap), editor.ValidationMessage);
        Assert.AreEqual(25d, hardCap.Cpu.RatePercent);
        editor.CpuMode = CpuControlMode.Weight;
        Assert.AreEqual("7", editor.CpuWeight);
    }

    [TestMethod]
    public void UnsupportedCpuCapsDisableEveryEditableInput()
    {
        var editor = CreateEditor();
        editor.Load(RestrictionProfile.Empty with { Cpu = new CpuControlSettings(UsesUnsupportedPerProcessorCaps: true) });

        Assert.IsFalse(editor.CanEditCpu);
        Assert.IsFalse(editor.IsCpuRateInputEnabled);
        Assert.IsFalse(editor.IsCpuWeightInputEnabled);
        Assert.IsFalse(editor.IsCpuMinimumMaximumInputEnabled);
        Assert.IsFalse(editor.IsCpuNotifyInputEnabled);
    }

    [TestMethod]
    public void ChoicesExposeReadableLabelsWhileRetainingNativeEnumValues()
    {
        var editor = CreateEditor();
        Assert.AreEqual("Hard cap", editor.CpuModeChoices.Single(choice => choice.Value == CpuControlMode.HardCap).Label);
        Assert.AreEqual("Minimum and maximum", editor.CpuModeChoices.Single(choice => choice.Value == CpuControlMode.MinimumMaximum).Label);
        Assert.AreEqual("Terminate at time limit", editor.EndActionChoices.Single().Label);
    }

    [TestMethod]
    public void UnrelatedEditPreservesExternallyOwnedNotificationAction()
    {
        var editor = CreateEditor();
        editor.Load(RestrictionProfile.Empty with { EndAction = JobEndAction.PostNotification });
        editor.KillOnJobClose = true;

        Assert.IsTrue(editor.TryBuild(out var profile), editor.ValidationMessage);
        Assert.AreEqual(JobEndAction.PostNotification, profile.EndAction);
        Assert.IsTrue(profile.HardLimits.KillOnJobClose);
        Assert.AreEqual("Keep existing notification action", editor.EndActionChoices.Single(choice => choice.Value == JobEndAction.PostNotification).Label);

        editor.Load(RestrictionProfile.Empty);
        Assert.AreEqual(1, editor.EndActionChoices.Count);
        editor.EndAction = JobEndAction.PostNotification;
        Assert.IsFalse(editor.TryBuild(out _));
        Assert.AreEqual(nameof(editor.EndAction), editor.ValidationPropertyName);
    }

    [TestMethod]
    public void OwnedPortAllowsSelectingANewNotificationAction()
    {
        var editor = new RestrictionEditorViewModel(canPostEndOfJobNotification: true);
        editor.Load(RestrictionProfile.Empty);
        editor.EndAction = JobEndAction.PostNotification;

        Assert.IsTrue(editor.TryBuild(out var profile), editor.ValidationMessage);
        Assert.AreEqual(JobEndAction.PostNotification, profile.EndAction);
    }

    [TestMethod]
    public void TinyLoadedLimitsDisplayTheirActualPositiveValues()
    {
        using var culture = new CultureScope("en-CA");
        var editor = CreateEditor();
        editor.Load(RestrictionProfile.Empty with
        {
            HardLimits = new HardLimitSettings(PerProcessUserTimeLimit: TimeSpan.FromTicks(1), ProcessMemoryLimitBytes: 512),
            Notifications = new NotificationSettings(IoReadBytes: 1)
        });

        Assert.AreEqual("0.0000001", editor.PerProcessTimeSeconds);
        Assert.AreEqual("0.00048828125", editor.ProcessMemoryMb);
        Assert.AreEqual("0.00000095367431640625", editor.NotifyIoReadMb);
        editor.KillOnJobClose = true;
        Assert.IsTrue(editor.TryBuild(out var profile), editor.ValidationMessage);
        Assert.AreEqual(TimeSpan.FromTicks(1), profile.HardLimits.PerProcessUserTimeLimit);
        Assert.AreEqual(512UL, profile.HardLimits.ProcessMemoryLimitBytes);
        Assert.AreEqual(1UL, profile.Notifications.IoReadBytes);
    }

    [TestMethod]
    public void EditedTimeAndMemoryUseWholeNativeTicksAndBytes()
    {
        using var culture = new CultureScope("en-CA");
        var editor = CreateEditor();
        editor.PerProcessTimeEnabled = true;
        editor.PerProcessTimeSeconds = "0.00000015";
        editor.ProcessMemoryEnabled = true;
        editor.ProcessMemoryMb = (1.5m / 1_048_576m).ToString(CultureInfo.CurrentCulture);

        Assert.IsTrue(editor.TryBuild(out var profile), editor.ValidationMessage);
        Assert.AreEqual(TimeSpan.FromTicks(2), profile.HardLimits.PerProcessUserTimeLimit);
        Assert.AreEqual(2UL, profile.HardLimits.ProcessMemoryLimitBytes);
    }

    [TestMethod]
    public void NativeMaximumsSurviveFormattingAndAnEquivalentTextEdit()
    {
        var editor = CreateEditor();
        editor.Load(RestrictionProfile.Empty with
        {
            HardLimits = new HardLimitSettings(PerProcessUserTimeLimit: TimeSpan.MaxValue, ProcessMemoryLimitBytes: ulong.MaxValue)
        });
        editor.PerProcessTimeSeconds += " ";
        editor.ProcessMemoryMb += " ";

        Assert.IsTrue(editor.TryBuild(out var profile), editor.ValidationMessage);
        Assert.AreEqual(TimeSpan.MaxValue, profile.HardLimits.PerProcessUserTimeLimit);
        Assert.AreEqual(ulong.MaxValue, profile.HardLimits.ProcessMemoryLimitBytes);
    }

    [TestMethod]
    public void ValidationIdentifiesFieldAndDisplaysNonzeroMinimumWithUnits()
    {
        using var culture = new CultureScope("en-CA");
        var editor = CreateEditor();
        editor.PerProcessTimeEnabled = true;
        editor.PerProcessTimeSeconds = "0";
        var failedEvents = 0;
        editor.ValidationFailed += (_, _) => failedEvents++;

        Assert.IsFalse(editor.TryBuild(out _));
        StringAssert.Contains(editor.ValidationMessage, "at least 100 nanoseconds");
        StringAssert.Contains(editor.ValidationMessage, "0.0000001");
        StringAssert.Contains(editor.ValidationMessage, "seconds");
        Assert.AreEqual(nameof(editor.PerProcessTimeSeconds), editor.ValidationPropertyName);
        Assert.AreEqual(1, failedEvents);

        editor.PerProcessTimeEnabled = false;
        editor.ProcessMemoryEnabled = true;
        editor.ProcessMemoryMb = "0";
        Assert.IsFalse(editor.TryBuild(out _));
        StringAssert.Contains(editor.ValidationMessage, "at least 1 byte");
        StringAssert.Contains(editor.ValidationMessage, "MiB");
        Assert.IsFalse(editor.ValidationMessage.Contains("0.00000095367431640625", StringComparison.Ordinal));
        Assert.IsTrue(editor.ValidationMessage.Length < 100, editor.ValidationMessage);
        Assert.AreEqual(nameof(editor.ProcessMemoryMb), editor.ValidationPropertyName);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("not a number")]
    [DataRow("NaN")]
    public void InvalidNativeLimitsExplainTheirInputUnits(string value)
    {
        var editor = CreateEditor();
        editor.PerProcessTimeEnabled = true;
        editor.PerProcessTimeSeconds = value;

        Assert.IsFalse(editor.TryBuild(out _));
        StringAssert.Contains(editor.ValidationMessage, "positive number in seconds");
        Assert.AreEqual(nameof(editor.PerProcessTimeSeconds), editor.ValidationPropertyName);

        editor.PerProcessTimeEnabled = false;
        editor.ProcessMemoryEnabled = true;
        editor.ProcessMemoryMb = value;
        Assert.IsFalse(editor.TryBuild(out _));
        StringAssert.Contains(editor.ValidationMessage, "positive number in MiB");
        Assert.AreEqual(nameof(editor.ProcessMemoryMb), editor.ValidationPropertyName);
    }

    [TestMethod]
    [DataRow("0")]
    [DataRow("-1")]
    [DataRow("0.0000005")]
    public void MemoryUnderflowUsesReadableWholeByteMinimum(string value)
    {
        using var culture = new CultureScope("en-CA");
        var editor = CreateEditor();
        editor.ProcessMemoryEnabled = true;
        editor.ProcessMemoryMb = value;

        Assert.IsFalse(editor.TryBuild(out _));
        Assert.AreEqual("Process memory limit must be at least 1 byte; enter the value in MiB.", editor.ValidationMessage);
    }

    [TestMethod]
    [DataRow("en-CA")]
    [DataRow("fr-CA")]
    public void NativeOverflowMessagesUseGroupedLimitsAndCurrentCulture(string cultureName)
    {
        using var culture = new CultureScope(cultureName);
        var editor = CreateEditor();
        editor.PerProcessTimeEnabled = true;
        var maximumSeconds = long.MaxValue / (decimal)TimeSpan.TicksPerSecond;
        editor.PerProcessTimeSeconds = (maximumSeconds + 0.0000001m).ToString(CultureInfo.CurrentCulture);

        Assert.IsFalse(editor.TryBuild(out _));
        StringAssert.Contains(editor.ValidationMessage, $"cannot exceed {maximumSeconds.ToString("#,0.#######", CultureInfo.CurrentCulture)} seconds");
        Assert.AreEqual(nameof(editor.PerProcessTimeSeconds), editor.ValidationPropertyName);

        editor.PerProcessTimeEnabled = false;
        editor.ProcessMemoryEnabled = true;
        editor.ProcessMemoryMb = "17592186044416";
        Assert.IsFalse(editor.TryBuild(out _));
        StringAssert.Contains(editor.ValidationMessage, $"cannot exceed {ulong.MaxValue:N0} bytes");
        StringAssert.Contains(editor.ValidationMessage, "MiB");
        Assert.IsFalse(editor.ValidationMessage.Contains("17592186044415", StringComparison.Ordinal));
        Assert.IsTrue(editor.ValidationMessage.Length < 120, editor.ValidationMessage);
        Assert.AreEqual(nameof(editor.ProcessMemoryMb), editor.ValidationPropertyName);
    }

    [TestMethod]
    public void PrecisionAndValidationUseTheCurrentDecimalSeparator()
    {
        using var culture = new CultureScope("fr-CA");
        var editor = CreateEditor();
        editor.PerProcessTimeEnabled = true;
        editor.PerProcessTimeSeconds = "0,0000001";
        editor.ProcessMemoryEnabled = true;
        editor.ProcessMemoryMb = "0,00000095367431640625";

        Assert.IsTrue(editor.TryBuild(out var profile), editor.ValidationMessage);
        Assert.AreEqual(TimeSpan.FromTicks(1), profile.HardLimits.PerProcessUserTimeLimit);
        Assert.AreEqual(1UL, profile.HardLimits.ProcessMemoryLimitBytes);
    }

    private static RestrictionEditorViewModel CreateEditor()
    {
        var editor = new RestrictionEditorViewModel(canPostEndOfJobNotification: false);
        editor.Load(RestrictionProfile.Empty);
        return editor;
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;
        public CultureScope(string name) => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }
}
