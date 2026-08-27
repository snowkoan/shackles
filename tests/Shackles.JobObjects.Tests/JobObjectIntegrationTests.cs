using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Shackles.JobObjects.Interop;

namespace Shackles.JobObjects.Tests;

[TestClass]
[TestCategory("WindowsIntegration")]
public sealed class JobObjectIntegrationTests
{
    private static readonly string[] LongRunningPingArguments = ["-n", "30", "127.0.0.1"];

    [TestMethod]
    public void NamedJobCanBeCreatedAndOpened()
    {
        var name = $"Local\\Shackles.Tests.{Guid.NewGuid():N}";
        using var created = JobObject.Create(name);
        using var opened = JobObject.Open(name);

        Assert.IsTrue(created.CreatedNew);
        Assert.IsFalse(opened.CreatedNew);
        Assert.AreEqual(name, opened.Name);
        Assert.AreEqual(created.GetExtendedLimits(), opened.GetExtendedLimits());
        Assert.AreEqual(JobNotificationDeliveryMode.OwnedCompletionPort, created.NotificationDeliveryMode);
        Assert.AreEqual(JobNotificationDeliveryMode.SampledQueryOnly, opened.NotificationDeliveryMode);
        Assert.Throws<UnsupportedJobFeatureException>(() => opened.SetEndOfJobAction(JobEndOfJobAction.PostNotification));
    }

    [TestMethod]
    public void NamedJobCannotBeReopenedAfterLastHandleClosesEvenWhileMemberRemainsAlive()
    {
        const int ErrorFileNotFound = 2;
        var name = $"Local\\Shackles.Tests.CrossOwner.{Guid.NewGuid():N}";
        var startInfo = new ProcessStartInfo
        {
            FileName = GetWorkerExecutable(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("--create-named-job-owner");
        startInfo.ArgumentList.Add(name);
        using var creator = Process.Start(startInfo) ??
            throw new InvalidOperationException("Could not start the named-job owner process.");
        var memberProcessId = 0;

        try
        {
            Assert.IsTrue(
                creator.WaitForExit(20_000),
                "The named-job owner process did not exit after disposing its job handle.");
            var standardOutput = creator.StandardOutput.ReadToEnd().Trim();
            var standardError = creator.StandardError.ReadToEnd().Trim();
            var fields = standardOutput.Split('|');
            if (fields.Length > 0)
            {
                _ = int.TryParse(
                    fields[0],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out memberProcessId);
            }

            Assert.AreEqual(
                0,
                creator.ExitCode,
                $"The named-job owner failed. stdout: {standardOutput}; stderr: {standardError}");
            Assert.AreEqual(3, fields.Length, $"Unexpected owner result: {standardOutput}");
            Assert.IsGreaterThan(0, memberProcessId, $"The owner returned an invalid member PID: {standardOutput}");
            Assert.AreEqual("1", fields[1], "The owner unexpectedly opened a pre-existing job.");
            Assert.AreEqual("0", fields[2], $"Completion-port detach failed with native error {fields[2]}.");
            AssertProcessIsAlive(memberProcessId);
            AssertProcessIsInAnyJob(memberProcessId);

            using (var member = Process.GetProcessById(memberProcessId))
            {
                member.Refresh();
                Assert.AreEqual(
                    ProcessPriorityClass.Idle,
                    member.PriorityClass,
                    "The live member no longer reflects the original job's priority restriction.");
            }

            var openError = Assert.Throws<JobObjectException>(
                () => JobObject.Open(name, JobAccessRights.Query));
            Assert.AreEqual(JobOperation.OpenJob, openError.Operation);
            Assert.AreEqual(ErrorFileNotFound, openError.NativeErrorCode);

            // The original temporary object's name is gone even though its member still holds a
            // kernel reference. Reusing the name creates a distinct, empty job with default limits.
            using (var replacement = JobObject.Create(name))
            {
                Assert.IsTrue(replacement.CreatedNew);
                Assert.IsNull(replacement.GetExtendedLimits().PriorityClass);
                CollectionAssert.DoesNotContain(replacement.GetProcessIds().ToArray(), memberProcessId);
            }

            AssertProcessIsAlive(memberProcessId);
        }
        finally
        {
            if (!creator.HasExited)
            {
                creator.Kill(entireProcessTree: true);
                creator.WaitForExit(5_000);
            }

            if (memberProcessId > 0)
            {
                TryKill(memberProcessId);
            }
        }
    }

    [TestMethod]
    public void IdenticalNotificationLimitsDoNotRequireSetAccessOrRebaseTime()
    {
        var name = $"Local\\Shackles.Tests.{Guid.NewGuid():N}";
        using var owner = JobObject.Create(name);
        owner.SetNotificationLimits(new JobNotificationLimits
        {
            PerJobUserTime = TimeSpan.FromMinutes(5),
            IoReadBytes = 1_000_000
        });
        using var queryOnly = JobObject.Open(name, JobAccessRights.Query);
        var unchanged = queryOnly.GetNotificationLimits();

        // This succeeds with a QUERY-only handle only if SetNotificationLimits avoids the native Set call.
        queryOnly.SetNotificationLimits(unchanged);
    }

    [TestMethod]
    public void NativeDefaultRateToleranceValuesAreNormalized()
    {
        var native = new NativeNotificationLimitInformation2
        {
            LimitFlags =
                JobNotificationLimitFlags.CpuRateControl |
                JobNotificationLimitFlags.IoRateControl |
                JobNotificationLimitFlags.NetworkRateControl
            // Windows defines zero tolerance/interval as the High/Short defaults.
        };
        var limits = JobObject.FromNative(native);

        Assert.AreEqual(new JobRateNotification(JobRateControlTolerance.High, JobRateControlToleranceInterval.Short), limits.CpuRate);
        Assert.AreEqual(new JobRateNotification(JobRateControlTolerance.High, JobRateControlToleranceInterval.Short), limits.IoRate);
        Assert.AreEqual(new JobRateNotification(JobRateControlTolerance.High, JobRateControlToleranceInterval.Short), limits.NetworkRate);
    }

    [TestMethod]
    public void UnchangedAggregateApplyRequiresOnlyQueryAccess()
    {
        var name = $"Local\\Shackles.Tests.{Guid.NewGuid():N}";
        using var owner = JobObject.Create(name);
        var configured = owner.GetRestrictions() with
        {
            ExtendedLimits = new JobExtendedLimits
            {
                ActiveProcessLimit = 4,
                KillOnJobClose = true
            },
            UiRestrictions = JobUiRestrictions.ReadClipboard | JobUiRestrictions.WriteClipboard,
            CpuRateControl = new JobCpuRateControl
            {
                Mode = JobCpuRateMode.HardCap,
                Rate = 7_500
            },
            NetworkRateControl = new JobNetworkRateControl
            {
                Enabled = true,
                MaximumBandwidthBytesPerSecond = 1_000_000,
                DscpTag = 10
            },
            EndOfJobAction = JobEndOfJobAction.PostNotification,
            NotificationLimits = new JobNotificationLimits
            {
                IoReadBytes = 1_000_000
            }
        };
        owner.ApplyRestrictions(configured);

        using var queryOnly = JobObject.Open(name, JobAccessRights.Query);
        var unchanged = queryOnly.GetRestrictions();

        queryOnly.ApplyRestrictions(unchanged);
        Assert.AreEqual(JobAccessRights.Query, queryOnly.AccessRights);
        Assert.AreEqual(JobFeatureSupport.QueryOnly, queryOnly.Capabilities.ExtendedLimits.Support);
        Assert.IsFalse(queryOnly.Capabilities.ExtendedLimits.CanSet);
    }

    [TestMethod]
    public void InvalidLateApplyModelCannotChangeEarlierClasses()
    {
        using var job = JobObject.Create();
        var before = job.GetRestrictions();
        var invalid = before with
        {
            ExtendedLimits = before.ExtendedLimits with { ActiveProcessLimit = 7 },
            NotificationLimits = new JobNotificationLimits
            {
                CpuRate = new JobRateNotification(
                    (JobRateControlTolerance)99,
                    JobRateControlToleranceInterval.Short)
            }
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => job.ApplyRestrictions(invalid));
        Assert.AreEqual(before.ExtendedLimits, job.GetExtendedLimits());
        Assert.AreEqual(before.UiRestrictions, job.GetUiRestrictions());
    }

    [TestMethod]
    public void DocumentedRestrictionClassesRoundTrip()
    {
        using var job = JobObject.Create();

        var extended = new JobExtendedLimits
        {
            ActiveProcessLimit = 4,
            PerProcessUserTimeLimit = TimeSpan.FromMinutes(5),
            PerJobUserTimeLimit = TimeSpan.FromMinutes(10),
            ProcessMemoryLimitBytes = 256UL * 1024 * 1024,
            JobMemoryLimitBytes = 512UL * 1024 * 1024,
            DieOnUnhandledException = true,
            KillOnJobClose = true
        };
        job.SetExtendedLimits(extended);
        var firstRead = job.GetExtendedLimits();
        Assert.AreEqual(extended.ActiveProcessLimit, firstRead.ActiveProcessLimit);
        Assert.AreEqual(extended.PerJobUserTimeLimit, firstRead.PerJobUserTimeLimit);

        // A second update with the same job time exercises PRESERVE_JOB_TIME rather than resetting it.
        extended = extended with { ActiveProcessLimit = 5 };
        job.SetExtendedLimits(extended);
        var secondRead = job.GetExtendedLimits();
        Assert.AreEqual((uint)5, secondRead.ActiveProcessLimit);
        Assert.AreEqual(TimeSpan.FromMinutes(10), secondRead.PerJobUserTimeLimit);

        var ui = JobUiRestrictions.ReadClipboard | JobUiRestrictions.WriteClipboard;
        job.SetUiRestrictions(ui);
        Assert.AreEqual(ui, job.GetUiRestrictions() & ui);

        var cpu = new JobCpuRateControl { Mode = JobCpuRateMode.HardCap, Rate = 2_500 };
        job.SetCpuRateControl(cpu);
        Assert.AreEqual(cpu, job.GetCpuRateControl());

        var network = new JobNetworkRateControl
        {
            Enabled = true,
            MaximumBandwidthBytesPerSecond = 1_000_000,
            DscpTag = 10
        };
        job.SetNetworkRateControl(network);
        Assert.AreEqual(network, job.GetNetworkRateControl());

        job.SetEndOfJobAction(JobEndOfJobAction.PostNotification);
        Assert.AreEqual(JobEndOfJobAction.PostNotification, job.GetEndOfJobAction());

        var notifications = new JobNotificationLimits
        {
            IoReadBytes = 16UL * 1024 * 1024,
            IoWriteBytes = 8UL * 1024 * 1024,
            JobHighMemoryBytes = 384UL * 1024 * 1024,
            JobLowMemoryBytes = 128UL * 1024 * 1024
        };
        job.SetNotificationLimits(notifications);
        var notificationRead = job.GetNotificationLimits();
        Assert.AreEqual(notifications.IoReadBytes, notificationRead.IoReadBytes);
        Assert.AreEqual(notifications.JobHighMemoryBytes, notificationRead.JobHighMemoryBytes);

        var groups = job.GetProcessorGroups();
        Assert.IsNotEmpty(groups);
        job.SetProcessorGroups(groups);
        CollectionAssert.AreEqual(groups.ToArray(), job.GetProcessorGroups().ToArray());

        _ = job.GetAccounting();
        _ = job.GetLimitViolations();
        _ = job.GetSnapshot();
    }

    [TestMethod]
    public void ExistingDisposableProcessCanBeAssignedUsingStableIdentity()
    {
        using var job = JobObject.Create();
        job.SetExtendedLimits(new JobExtendedLimits { KillOnJobClose = true });
        using var child = DisposableChildProcess.Start();
        var capture = JobObject.TryCaptureProcessIdentity(child.Process.Id);
        Assert.IsTrue(capture.Succeeded, capture.Error?.Message);
        Assert.IsTrue(capture.Identity.HasValue);

        var result = job.AssignProcess(capture.Identity.Value);

        Assert.IsTrue(result.Succeeded, result.Error?.Message);
        CollectionAssert.Contains(job.GetProcessIds().ToArray(), child.Process.Id);
    }

    [TestMethod]
    public void AssignmentFromUnrelatedJobIntoNonemptyTargetAddsObservedNestingEvidence()
    {
        using var sourceJob = JobObject.Create();
        using var targetJob = JobObject.Create();
        sourceJob.SetExtendedLimits(new JobExtendedLimits { KillOnJobClose = true });
        targetJob.SetExtendedLimits(new JobExtendedLimits { KillOnJobClose = true });
        var executable = GetWorkerExecutable();
        var sourceMember = sourceJob.LaunchProcess(new ProcessLaunchOptions(executable)
        {
            CreateNoWindow = true
        });
        var targetMember = targetJob.LaunchProcess(new ProcessLaunchOptions(executable)
        {
            CreateNoWindow = true
        });

        try
        {
            var sourceMembersBeforeAssignment = sourceJob.GetProcessIds().ToArray();
            var targetMembersBeforeAssignment = targetJob.GetProcessIds().ToArray();
            CollectionAssert.Contains(sourceMembersBeforeAssignment, sourceMember.ProcessId);
            CollectionAssert.Contains(targetMembersBeforeAssignment, targetMember.ProcessId);
            CollectionAssert.DoesNotContain(sourceMembersBeforeAssignment, targetMember.ProcessId);
            CollectionAssert.DoesNotContain(targetMembersBeforeAssignment, sourceMember.ProcessId);

            var result = targetJob.AssignProcess(sourceMember.Identity);

            Assert.IsFalse(result.Succeeded);
            Assert.IsNotNull(result.Error);
            Assert.AreEqual(JobOperation.AssignProcess, result.Error.Operation);
            Assert.AreEqual(5, result.Error.NativeErrorCode);
            StringAssert.Contains(
                result.Error.Message,
                "already associated with at least one other job and is not a member of this target job");
            StringAssert.Contains(result.Error.Message, "The target job reports");
            StringAssert.Contains(result.Error.Message, "so it is not empty.");
            StringAssert.Contains(result.Error.Message, "Running elevated does not bypass these kernel rules.");
            StringAssert.Contains(result.Error.Message, "the process's existing job, so its exact hierarchy and limits remain unknown");
            CollectionAssert.Contains(sourceJob.GetProcessIds().ToArray(), sourceMember.ProcessId);
            var targetMembersAfterFailure = targetJob.GetProcessIds().ToArray();
            CollectionAssert.Contains(targetMembersAfterFailure, targetMember.ProcessId);
            CollectionAssert.DoesNotContain(targetMembersAfterFailure, sourceMember.ProcessId);
        }
        finally
        {
            TryKill(sourceMember.ProcessId);
            TryKill(targetMember.ProcessId);
        }
    }

    [TestMethod]
    public void QueryOnlyTargetReportsMissingAssignRightWithoutNestingDiagnosis()
    {
        var targetName = $"Local\\Shackles.Tests.{Guid.NewGuid():N}";
        using var sourceJob = JobObject.Create();
        using var targetOwner = JobObject.Create(targetName);
        using var queryOnlyTarget = JobObject.Open(targetName, JobAccessRights.Query);
        sourceJob.SetExtendedLimits(new JobExtendedLimits { KillOnJobClose = true });
        var sourceMember = sourceJob.LaunchProcess(new ProcessLaunchOptions(GetWorkerExecutable())
        {
            CreateNoWindow = true
        });

        try
        {
            var result = queryOnlyTarget.AssignProcess(sourceMember.Identity);

            Assert.IsFalse(result.Succeeded);
            Assert.IsNotNull(result.Error);
            Assert.AreEqual(JobOperation.AssignProcess, result.Error.Operation);
            Assert.AreEqual(5, result.Error.NativeErrorCode);
            StringAssert.Contains(result.Error.Message, "does not grant JOB_OBJECT_ASSIGN_PROCESS");
            Assert.IsFalse(result.Error.Message.Contains("nested association", StringComparison.Ordinal));
        }
        finally
        {
            TryKill(sourceMember.ProcessId);
        }
    }

    [TestMethod]
    public void LaunchProcessAssignsBeforeResuming()
    {
        using var job = JobObject.Create();
        job.SetExtendedLimits(new JobExtendedLimits { KillOnJobClose = true });
        var executable = Path.Combine(Environment.SystemDirectory, "ping.exe");

        var launched = job.LaunchProcess(new ProcessLaunchOptions(executable)
        {
            Arguments = LongRunningPingArguments,
            CreateNoWindow = true
        });

        try
        {
            CollectionAssert.Contains(job.GetProcessIds().ToArray(), launched.ProcessId);
            using var process = Process.GetProcessById(launched.ProcessId);
            Assert.IsFalse(process.HasExited);
        }
        finally
        {
            TryKill(launched.ProcessId);
        }
    }

    [TestMethod]
    public async Task OwnedCompletionPortDeliversJobMessages()
    {
        using var job = JobObject.Create();
        job.SetExtendedLimits(new JobExtendedLimits { KillOnJobClose = true });
        var received = new TaskCompletionSource<JobNotificationEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        job.NotificationReceived += (_, message) =>
        {
            if (message.MessageKind == JobNotificationMessageKind.NewProcess)
            {
                received.TrySetResult(message);
            }
        };
        var executable = Path.Combine(Environment.SystemDirectory, "ping.exe");
        var launched = job.LaunchProcess(new ProcessLaunchOptions(executable)
        {
            Arguments = LongRunningPingArguments,
            CreateNoWindow = true
        });

        try
        {
            var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(launched.ProcessId, message.ProcessId);
            Assert.IsNull(message.Error);
        }
        finally
        {
            TryKill(launched.ProcessId);
        }
    }

    [TestMethod]
    public async Task PostNotificationEndOfJobTimeDoesNotTerminateWorker()
    {
        using var job = JobObject.Create();
        var received = Observe(job, JobNotificationMessageKind.EndOfJobTime);
        job.SetEndOfJobAction(JobEndOfJobAction.PostNotification);
        job.SetExtendedLimits(new JobExtendedLimits
        {
            PerJobUserTimeLimit = TimeSpan.FromMilliseconds(150),
            KillOnJobClose = true
        });

        var launched = job.LaunchProcess(new ProcessLaunchOptions(GetWorkerExecutable())
        {
            CreateNoWindow = true
        });

        try
        {
            var message = await received.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.IsNull(message.Error);
            await Task.Delay(250);
            AssertProcessIsAlive(launched.ProcessId);
        }
        finally
        {
            TryKill(launched.ProcessId);
        }
    }

    [TestMethod]
    public async Task ApplyInstallsPostActionBeforeNewHardTimeLimit()
    {
        using var job = JobObject.Create();
        job.SetExtendedLimits(new JobExtendedLimits { KillOnJobClose = true });
        var received = Observe(job, JobNotificationMessageKind.EndOfJobTime);
        var launched = job.LaunchProcess(new ProcessLaunchOptions(GetWorkerExecutable())
        {
            CreateNoWindow = true
        });

        try
        {
            using var process = Process.GetProcessById(launched.ProcessId);
            await WaitForUserProcessorTime(process, TimeSpan.FromMilliseconds(300));

            var requested = job.GetRestrictions();
            requested = requested with
            {
                EndOfJobAction = JobEndOfJobAction.PostNotification,
                ExtendedLimits = requested.ExtendedLimits with
                {
                    // Windows makes this relative to accumulated job time. A 1 ms increment keeps
                    // the action/limit ordering regression window as narrow as practical.
                    PerJobUserTimeLimit = TimeSpan.FromMilliseconds(1)
                }
            };

            job.ApplyRestrictions(requested);
            var message = await received.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.IsNull(message.Error);
            await Task.Delay(250);
            AssertProcessIsAlive(launched.ProcessId);
        }
        finally
        {
            TryKill(launched.ProcessId);
        }
    }

    [TestMethod]
    public async Task DisposeWaitsForInFlightHandlerAndDropsPendingCallbacks()
    {
        var job = JobObject.Create();
        job.SetExtendedLimits(new JobExtendedLimits { KillOnJobClose = true });
        using var releaseHandler = new ManualResetEventSlim(false);
        var handlerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackCount = 0;
        job.NotificationReceived += (_, message) =>
        {
            if (message.MessageKind != JobNotificationMessageKind.NewProcess)
            {
                return;
            }

            Interlocked.Increment(ref callbackCount);
            handlerEntered.TrySetResult();
            releaseHandler.Wait();
        };

        var launched = job.LaunchProcess(new ProcessLaunchOptions(GetWorkerExecutable())
        {
            CreateNoWindow = true
        });

        try
        {
            await handlerEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var disposeTask = Task.Run(job.Dispose);
            await Task.Delay(200);
            Assert.IsFalse(disposeTask.IsCompleted, "Dispose returned while a notification callback was still running.");

            releaseHandler.Set();
            await disposeTask.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(200);
            Assert.AreEqual(1, Volatile.Read(ref callbackCount));
        }
        finally
        {
            releaseHandler.Set();
            job.Dispose();
            TryKill(launched.ProcessId);
        }
    }

    private static Task<JobNotificationEventArgs> Observe(
        JobObject job,
        JobNotificationMessageKind messageKind)
    {
        var received = new TaskCompletionSource<JobNotificationEventArgs>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        job.NotificationReceived += (_, message) =>
        {
            if (message.MessageKind == messageKind)
            {
                received.TrySetResult(message);
            }
        };
        return received.Task;
    }

    private static string GetWorkerExecutable()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Shackles.TestWorker.exe");
        Assert.IsTrue(File.Exists(path), $"The dedicated test worker was not copied to {path}.");
        return path;
    }

    private static async Task WaitForUserProcessorTime(Process process, TimeSpan minimum)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            process.Refresh();
            Assert.IsFalse(process.HasExited, "The dedicated test worker exited unexpectedly.");
            if (process.UserProcessorTime >= minimum)
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.Fail($"The dedicated test worker did not accrue {minimum} of user CPU time.");
    }

    private static void AssertProcessIsAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            Assert.IsFalse(process.HasExited, "The job terminated its worker instead of posting a notification.");
        }
        catch (ArgumentException)
        {
            Assert.Fail("The job terminated its worker instead of posting a notification.");
        }
    }

    private static void AssertProcessIsInAnyJob(int processId)
    {
        var rawProcess = NativeMethods.OpenProcess(
            JobObject.ProcessIdentityQueryAccess,
            inheritHandle: 0,
            checked((uint)processId));
        var openError = Marshal.GetLastPInvokeError();
        using var process = new SafeProcessHandle(rawProcess);
        Assert.IsFalse(
            process.IsInvalid,
            $"OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION) failed with native error {openError}.");

        var succeeded = NativeMethods.IsProcessInAnyJob(process, job: 0, out var isInJob);
        var membershipError = Marshal.GetLastPInvokeError();
        Assert.AreNotEqual(0, succeeded, $"IsProcessInJob(process, NULL) failed with native error {membershipError}.");
        Assert.AreNotEqual(0, isInJob, "The live member no longer has any job association.");
    }

    private static void TryKill(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5_000);
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private sealed class DisposableChildProcess : IDisposable
    {
        private DisposableChildProcess(Process process)
        {
            Process = process;
        }

        internal Process Process { get; }

        internal static DisposableChildProcess Start()
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "ping.exe"),
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-n");
            startInfo.ArgumentList.Add("30");
            startInfo.ArgumentList.Add("127.0.0.1");
            return new DisposableChildProcess(Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start test child process."));
        }

        public void Dispose()
        {
            if (!Process.HasExited)
            {
                Process.Kill(entireProcessTree: true);
                Process.WaitForExit(5_000);
            }

            Process.Dispose();
        }
    }
}
