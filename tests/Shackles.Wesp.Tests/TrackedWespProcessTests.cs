using Shackles.Wesp.Internal;

namespace Shackles.Wesp.Tests;

[TestClass]
public sealed class TrackedWespProcessTests
{
    [TestMethod]
    [DataRow(uint.MaxValue, 6)]
    [DataRow(128u, 0)]
    public void FailedAndUnexpectedWaitsPreserveTrackedProcess(uint result, int error)
    {
        var native = new FakeProcessHandle { WaitResult = new(result, error) };
        using var tracked = Track(native);
        IList<TrackedWespProcess> processes = new List<TrackedWespProcess> { tracked };

        WespTrackedProcessCleanup.RemoveConfirmedExits(processes);

        Assert.HasCount(1, processes);
        Assert.IsTrue(tracked.GetInfo().IsStateUnknown);
        Assert.IsFalse(native.Disposed);
        StringAssert.Contains(tracked.GetInfo().StateError!, "PID 42");
    }

    [TestMethod]
    public void ConfirmedExitReleasesHandleAndTracking()
    {
        var native = new FakeProcessHandle { WaitResult = new(0) };
        using var tracked = Track(native);
        IList<TrackedWespProcess> processes = new List<TrackedWespProcess> { tracked };

        WespTrackedProcessCleanup.RemoveConfirmedExits(processes);

        Assert.IsEmpty(processes);
        Assert.IsTrue(native.Disposed);
    }

    [TestMethod]
    public void TerminationFailureKeepsOwnershipAndReportsNativeError()
    {
        var native = new FakeProcessHandle { TerminationError = 5 };
        using var tracked = Track(native);
        IList<TrackedWespProcess> processes = new List<TrackedWespProcess> { tracked };

        var failure = WespTrackedProcessCleanup.Close(processes, waitMilliseconds: 0);

        Assert.IsNotNull(failure);
        StringAssert.Contains(failure.Message, "could not terminate PID 42");
        StringAssert.Contains(failure.Message, "80070005");
        Assert.HasCount(1, processes);
        Assert.IsFalse(native.Disposed);
    }

    [TestMethod]
    public void UnknownExitDuringCleanupRetainsRulesUntilRetryConfirmsExit()
    {
        var native = new FakeProcessHandle { WaitResult = new(uint.MaxValue, 6) };
        using var tracked = Track(native);
        IList<TrackedWespProcess> processes = new List<TrackedWespProcess> { tracked };

        var failure = WespTrackedProcessCleanup.Close(processes, waitMilliseconds: 0);

        Assert.IsNotNull(failure);
        StringAssert.Contains(failure.Message, "rules remain active");
        Assert.AreEqual(1, native.TerminationRequests);
        Assert.IsFalse(native.Disposed);
        native.WaitResult = new(0);
        Assert.IsNull(WespTrackedProcessCleanup.Close(processes, waitMilliseconds: 0));
        Assert.IsEmpty(processes);
        Assert.IsTrue(native.Disposed);
    }

    [TestMethod]
    public void ClosingAttachedProcessNeverRequestsTermination()
    {
        var native = new FakeProcessHandle { WaitResult = new(uint.MaxValue, 6) };
        using var tracked = Track(native, WespProcessOrigin.Attached);
        IList<TrackedWespProcess> processes = new List<TrackedWespProcess> { tracked };

        Assert.IsNull(WespTrackedProcessCleanup.Close(processes, waitMilliseconds: 0));
        Assert.AreEqual(0, native.TerminationRequests);
        Assert.IsTrue(native.Disposed);
        Assert.IsEmpty(processes);
    }

    [TestMethod]
    public void FailedTerminationRacingConfirmedExitDoesNotBlockCleanup()
    {
        var native = new FakeProcessHandle { TerminationError = 5, ExitAfterTermination = true };
        using var tracked = Track(native);
        IList<TrackedWespProcess> processes = new List<TrackedWespProcess> { tracked };

        Assert.IsNull(WespTrackedProcessCleanup.Close(processes, waitMilliseconds: 0));
        Assert.IsEmpty(processes);
        Assert.IsTrue(native.Disposed);
    }

    private static TrackedWespProcess Track(FakeProcessHandle native,
        WespProcessOrigin origin = WespProcessOrigin.Launched) => new(native, 42, 123, origin);

    private sealed class FakeProcessHandle : IWespTrackedProcessHandle
    {
        public bool IsUsable => !Disposed;
        internal bool Disposed { get; private set; }
        internal WespProcessWaitResult WaitResult { get; set; } = new(258);
        internal int? TerminationError { get; init; }
        internal bool ExitAfterTermination { get; init; }
        internal int TerminationRequests { get; private set; }
        public WespProcessWaitResult Wait(uint milliseconds) => WaitResult;
        public int? RequestTermination()
        {
            TerminationRequests++;
            if (ExitAfterTermination)
            {
                WaitResult = new(0);
            }

            return TerminationError;
        }

        public void Dispose() => Disposed = true;
    }
}
