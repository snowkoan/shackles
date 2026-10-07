using Shackles.App.Models;
using Shackles.App.ViewModels;

namespace Shackles.App.Tests;

[TestClass]
public sealed class JobSummaryTests
{
    [TestMethod]
    public void EmptyProfileHasNoConfiguredRestrictions() =>
        Assert.AreEqual("No configured restrictions", JobViewModel.BuildRestrictionSummary(RestrictionProfile.Empty));

    [TestMethod]
    [DataRow("unhandled exception")]
    [DataRow("Breakaway allowed")]
    [DataRow("Silent breakaway allowed")]
    public void StandaloneLifetimeSettingAppearsInSummary(string expected)
    {
        var hard = expected switch
        {
            "unhandled exception" => new HardLimitSettings(DieOnUnhandledException: true),
            "Breakaway allowed" => new HardLimitSettings(BreakawayAllowed: true),
            _ => new HardLimitSettings(SilentBreakawayAllowed: true)
        };
        StringAssert.Contains(JobViewModel.BuildRestrictionSummary(RestrictionProfile.Empty with { HardLimits = hard }), expected);
    }

    [TestMethod]
    public void SummaryShowsActualCpuAndByteUploadValues()
    {
        var profile = RestrictionProfile.Empty with
        {
            Cpu = new(CpuControlMode.HardCap, RatePercent: 25),
            Network = new(ExactMaximumBandwidthBytesPerSecond: 62_500)
        };
        var summary = JobViewModel.BuildRestrictionSummary(profile);
        StringAssert.Contains(summary, "CPU cap 25%");
        StringAssert.Contains(summary, "62.5 kB/s");
        Assert.IsFalse(summary.Contains("bit", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void GroupsAndExistingEndNotificationAppearWithoutOtherLimits()
    {
        StringAssert.Contains(JobViewModel.BuildRestrictionSummary(RestrictionProfile.Empty with
        {
            ProcessorGroups = [new(2, 0x4)]
        }), "2:0x4");
        StringAssert.Contains(JobViewModel.BuildRestrictionSummary(RestrictionProfile.Empty with
        {
            EndAction = JobEndAction.PostNotification
        }), "Notify at end of job time");
    }

    [TestMethod]
    public void SmallNativeLimitsAreNeverSummarizedAsZero()
    {
        var summary = JobViewModel.BuildRestrictionSummary(RestrictionProfile.Empty with
        {
            HardLimits = new(PerJobUserTimeLimit: TimeSpan.FromTicks(1), ProcessMemoryLimitBytes: 1)
        });
        StringAssert.Contains(summary, "0.0000001 s");
        StringAssert.Contains(summary, "1 B");
    }
}
