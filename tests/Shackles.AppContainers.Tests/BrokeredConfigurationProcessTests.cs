using Shackles.AppContainers.Internal;

namespace Shackles.AppContainers.Tests;

[TestClass]
public sealed class BrokeredConfigurationProcessTests
{
    private static readonly int[] ExpectedInitialWaits = [9_000];
    private static readonly int[] ExpectedOutputWaits = [1_000];
    private static readonly int[] ExpectedCleanupWaits = [9_000, 1_000];
    [TestMethod]
    public void ExitAndOutputShareOneDeadline()
    {
        using var process = new FakeProcess { ExitDuration = 8_000, OutputDuration = 500 };

        Assert.AreEqual(0, Run(process));
        CollectionAssert.AreEqual(ExpectedInitialWaits, process.ExitWaits.ToArray());
        CollectionAssert.AreEqual(ExpectedOutputWaits, process.OutputWaits.ToArray());
        Assert.AreEqual(8_500L, process.Elapsed);
        Assert.IsFalse(process.Terminated);
    }

    [TestMethod]
    public void InheritedOutputPipeCannotCauseUnboundedDrainAfterExit()
    {
        using var process = new FakeProcess { ExitDuration = 8_000, OutputDuration = 5_000 };

        var failure = Assert.Throws<InvalidOperationException>(() => Run(process));

        StringAssert.Contains(failure.Message, "output before the operation deadline");
        Assert.IsTrue(process.Terminated);
        Assert.IsTrue(process.OutputCancelled);
        Assert.IsLessThanOrEqualTo(10_000L, process.Elapsed);
        CollectionAssert.AreEqual(ExpectedCleanupWaits, process.ExitWaits.ToArray());
    }

    [TestMethod]
    public void TimeoutIncludesBoundedTerminationAndReportsFailedKill()
    {
        using var process = new FakeProcess { ExitDuration = 20_000, TerminationFails = true };

        var failure = Assert.Throws<InvalidOperationException>(() => Run(process));

        StringAssert.Contains(failure.Message, "Helper termination failed");
        StringAssert.Contains(failure.Message, "exit could not be confirmed");
        Assert.AreEqual(10_000L, process.Elapsed);
        Assert.IsTrue(process.OutputCancelled);
    }

    [TestMethod]
    public void OutputSetupFailureStillTerminatesStartedHelper()
    {
        using var process = new FakeProcess { ReadingFails = true };

        var failure = Assert.Throws<InvalidOperationException>(() => Run(process));

        StringAssert.Contains(failure.Message, "output setup failed");
        Assert.IsTrue(process.Terminated);
        Assert.IsTrue(process.OutputCancelled);
        Assert.HasCount(1, process.ExitWaits);
    }

    [TestMethod]
    public void CancellationFailureDoesNotReplaceTimeoutDiagnostic()
    {
        using var process = new FakeProcess { OutputDuration = 20_000, CancellationFails = true };

        var failure = Assert.Throws<InvalidOperationException>(() => Run(process));

        StringAssert.Contains(failure.Message, "operation deadline");
        StringAssert.Contains(failure.Message, "Output capture could not be cancelled");
        Assert.IsTrue(process.Terminated);
    }

    [TestMethod]
    public void SlowStartupConsumesTheSameCompletionBudget()
    {
        using var process = new FakeProcess
        {
            StartDuration = 8_000,
            ExitDuration = 8_500,
            OutputDuration = 1_000
        };

        Assert.Throws<InvalidOperationException>(() => Run(process));

        Assert.AreEqual(1_000, process.ExitWaits[0]);
        Assert.AreEqual(500, process.OutputWaits[0]);
        Assert.IsLessThanOrEqualTo(10_000L, process.Elapsed);
        Assert.IsTrue(process.Terminated);
    }

    [TestMethod]
    public void NonzeroExitCodeIsReturnedOnlyAfterOutputCompletes()
    {
        using var process = new FakeProcess { ExitCode = 23, ExitDuration = 5, OutputDuration = 50 };

        Assert.AreEqual(23, Run(process));
        Assert.AreEqual(55L, process.Elapsed);
        Assert.HasCount(1, process.OutputWaits);
    }

    private static int Run(FakeProcess process) =>
        BrokeredConfigurationProcessRunner.Run(process, getMilliseconds: () => process.Elapsed);

    private sealed class FakeProcess : IBrokeredConfigurationProcess
    {
        internal long Elapsed { get; private set; }
        internal int StartDuration { get; init; }
        internal int ExitDuration { get; init; }
        internal int OutputDuration { get; init; }
        internal bool TerminationFails { get; init; }
        internal bool ReadingFails { get; init; }
        internal bool CancellationFails { get; init; }
        internal bool Terminated { get; private set; }
        internal bool OutputCancelled { get; private set; }
        internal List<int> ExitWaits { get; } = [];
        internal List<int> OutputWaits { get; } = [];
        public int ExitCode { get; init; }
        public bool Start()
        {
            Elapsed += StartDuration;
            return true;
        }
        public void StartReading()
        {
            if (ReadingFails)
            {
                throw new InvalidOperationException("output setup failed");
            }
        }

        public bool WaitForExit(int milliseconds)
        {
            ExitWaits.Add(milliseconds);
            var remaining = Terminated && !TerminationFails ? 0 : Math.Max(0, ExitDuration - Elapsed);
            Elapsed += Math.Min(milliseconds, remaining);
            return remaining <= milliseconds;
        }

        public bool WaitForOutput(int milliseconds)
        {
            OutputWaits.Add(milliseconds);
            Elapsed += Math.Min(milliseconds, OutputDuration);
            return OutputDuration <= milliseconds;
        }

        public void Terminate()
        {
            Terminated = true;
            if (TerminationFails)
            {
                throw new InvalidOperationException("access denied");
            }
        }

        public void CancelOutput()
        {
            OutputCancelled = true;
            if (CancellationFails)
            {
                throw new InvalidOperationException("cancel failed");
            }
        }
        public void Dispose() { }
    }
}
