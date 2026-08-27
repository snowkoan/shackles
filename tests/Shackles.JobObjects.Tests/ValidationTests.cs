namespace Shackles.JobObjects.Tests;

using Shackles.JobObjects.Internal;
using Shackles.JobObjects.Interop;

[TestClass]
public sealed class ValidationTests
{
    [TestMethod]
    public void InvalidJobNamesAreRejectedBeforeNativeCall()
    {
        Assert.Throws<ArgumentNullException>(() => JobObject.Open(null!));
        Assert.Throws<ArgumentException>(() => JobObject.Create(string.Empty));
        Assert.Throws<ArgumentException>(() => JobObject.Create("   "));
        Assert.Throws<ArgumentException>(() => JobObject.Create("folder\\job"));
        Assert.Throws<ArgumentException>(() => JobObject.Create("Global\\"));
        Assert.Throws<ArgumentException>(() => JobObject.Create("Local\\folder\\job"));
        Assert.Throws<ArgumentException>(() => JobObject.Create(new string('a', 261)));
    }

    [TestMethod]
    public void CpuModesRequireTheirPayloads()
    {
        using var job = JobObject.Create();

        Assert.Throws<ArgumentOutOfRangeException>(() => job.SetCpuRateControl(new JobCpuRateControl
        {
            Mode = JobCpuRateMode.Rate
        }));
        Assert.Throws<ArgumentOutOfRangeException>(() => job.SetCpuRateControl(new JobCpuRateControl
        {
            Mode = JobCpuRateMode.HardCap
        }));
        Assert.Throws<ArgumentOutOfRangeException>(() => job.SetCpuRateControl(new JobCpuRateControl
        {
            Mode = JobCpuRateMode.WeightBased
        }));
        Assert.Throws<ArgumentOutOfRangeException>(() => job.SetCpuRateControl(new JobCpuRateControl
        {
            Mode = JobCpuRateMode.MinimumMaximum
        }));
    }

    [TestMethod]
    public void StaleProcessIdentityIsRefusedWithoutAssigning()
    {
        using var job = JobObject.Create();
        var current = job.CaptureProcessIdentity(Environment.ProcessId);
        var stale = current with { CreationTimeFileTimeUtc = current.CreationTimeFileTimeUtc + 1 };

        var result = job.AssignProcess(stale);

        Assert.IsFalse(result.Succeeded);
        Assert.IsNotNull(result.Error);
        Assert.AreEqual(JobOperation.AssignProcess, result.Error.Operation);
    }

    [TestMethod]
    public void EmbeddedNullProcessArgumentIsRejected()
    {
        Assert.Throws<ArgumentException>(() => WindowsCommandLine.Build("program.exe", ["before\0after"]));
        Assert.Throws<ArgumentNullException>(() => WindowsCommandLine.Build("program.exe", null!));
    }

    [TestMethod]
    public void PerProcessorCpuCapsAreSurfacedButCannotBeRequested()
    {
        var native = new NativeCpuRateControlInformation
        {
            ControlFlags = NativeCpuRateFlags.Enable | NativeCpuRateFlags.PerProcessorCaps
        };
        var projected = JobObject.FromNative(native);
        Assert.IsTrue(projected.UsesUnsupportedPerProcessorCaps);

        using var job = JobObject.Create();
        Assert.Throws<UnsupportedJobFeatureException>(() => job.SetCpuRateControl(new JobCpuRateControl
        {
            UsesUnsupportedPerProcessorCaps = true
        }));
    }
}
