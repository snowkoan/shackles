using System.Diagnostics;
using Shackles.App.Models;
using Core = Shackles.JobObjects;

namespace Shackles.App.Services;

internal sealed class JobControlService : IJobControlService
{
    public JobControlService()
    {
        Capabilities = MapCapabilities(Core.JobCapabilities.Detect());
    }

    public JobCapabilitySet Capabilities { get; }

    public ProcessIdentityCaptureResult CaptureProcessIdentity(int processId)
    {
        var result = Core.JobObject.TryCaptureProcessIdentity(processId);
        if (result.Identity is { } identity)
        {
            return new ProcessIdentityCaptureResult(identity.CreationTimeFileTimeUtc, null);
        }

        var error = result.Error ?? new Core.JobOperationError(
            Core.JobOperation.ReadProcessIdentity,
            0,
            "Windows returned no process identity and no diagnostic error.");
        return new ProcessIdentityCaptureResult(
            null,
            new ProcessIdentityQueryFailure(
                error.Operation.ToString(),
                error.NativeErrorCode,
                error.Message));
    }

    public IJobSession CreateJob(string? name) => new JobSession(Core.JobObject.Create(name));

    public IJobSession OpenJob(string name) => new JobSession(Core.JobObject.Open(name, Core.JobAccessRights.Manage));

    public void Dispose()
    {
        // Job sessions own their handles and are disposed by their JobViewModels.
    }

    private static JobCapabilitySet MapCapabilities(Core.JobCapabilities value) => new(
        MapCapability(value.ExtendedLimits),
        MapCapability(value.CpuRateControl),
        MapCapability(value.NetworkRateControl),
        MapCapability(value.UiRestrictions),
        MapCapability(value.NotificationLimits),
        MapCapability(value.ProcessorGroups),
        MapCapability(value.EndOfJobAction));

    private static CapabilityInfo MapCapability(Core.JobFeatureCapability value) => new(
        value.Support switch
        {
            Core.JobFeatureSupport.Supported => CapabilityState.Available,
            Core.JobFeatureSupport.QueryOnly => CapabilityState.QueryOnly,
            _ => CapabilityState.Unavailable
        },
        value.Reason);

    private sealed class JobSession : IJobSession
    {
        private readonly Core.JobObject _job;
        private volatile bool _disposed;

        public JobSession(Core.JobObject job)
        {
            _job = job;
            _job.NotificationReceived += CoreNotificationReceived;
        }

        public string? Name => _job.Name;
        public bool CreatedNew => _job.CreatedNew;
        public bool HasOwnedNotificationDelivery =>
            _job.NotificationDeliveryMode == Core.JobNotificationDeliveryMode.OwnedCompletionPort;

        public event EventHandler<LiveJobNotificationDisplay>? NotificationReceived;

        public JobSessionSnapshot GetSnapshot()
        {
            ThrowIfDisposed();
            var snapshot = _job.GetSnapshot();
            return new JobSessionSnapshot(
                FromCore(snapshot.Restrictions),
                snapshot.ProcessIds,
                new JobAccountingDisplay(
                    snapshot.Accounting.ActiveProcesses,
                    snapshot.Accounting.TotalProcesses,
                    snapshot.Accounting.TotalTerminatedProcesses,
                    snapshot.Accounting.TotalUserTime,
                    snapshot.Accounting.TotalKernelTime,
                    snapshot.Accounting.Io.ReadBytes,
                    snapshot.Accounting.Io.WriteBytes),
                MapViolations(snapshot.LimitViolations, snapshot.CapturedAtUtc));
        }

        public void ApplyRestrictions(RestrictionProfile restrictions)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(restrictions);
            _job.ApplyRestrictions(ToCore(restrictions));
        }

        public IReadOnlyList<AssignmentOutcome> AssignProcesses(IReadOnlyCollection<ProcessIdentity> processes)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(processes);
            var names = processes.ToDictionary(item => item.ProcessId, item => ReadProcessName(item.ProcessId));
            var identities = processes.Select(item => new Core.ProcessIdentity(item.ProcessId, item.CreationTimeUtcFileTime));
            return _job.AssignProcesses(identities)
                .Select(result => new AssignmentOutcome(
                    result.ProcessId,
                    names.GetValueOrDefault(result.ProcessId, $"PID {result.ProcessId}"),
                    result.Succeeded,
                    DescribeAssignmentResult(result)))
                .ToArray();
        }

        public LaunchOutcome LaunchProcess(LaunchRequest request)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(request);
            var launched = _job.LaunchProcess(new Core.ProcessLaunchOptions(request.FileName)
            {
                Arguments = request.Arguments,
                WorkingDirectory = request.WorkingDirectory
            });
            return new LaunchOutcome(launched.ProcessId, ReadProcessName(launched.ProcessId));
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _job.NotificationReceived -= CoreNotificationReceived;
            NotificationReceived = null;
            _job.Dispose();
        }

        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

        private void CoreNotificationReceived(object? sender, Core.JobNotificationEventArgs e)
        {
            if (!_disposed)
            {
                NotificationReceived?.Invoke(this, MapNotification(e));
            }
        }
    }

    private static LiveJobNotificationDisplay MapNotification(Core.JobNotificationEventArgs value)
    {
        if (value.Error is { } error)
        {
            return new LiveJobNotificationDisplay(
                value.RawMessageCode,
                "Delivery error",
                value.ProcessId,
                error.Message,
                isError: true,
                value.ReceivedAtUtc);
        }

        var kind = value.MessageKind switch
        {
            Core.JobNotificationMessageKind.EndOfJobTime => "End of job time",
            Core.JobNotificationMessageKind.EndOfProcessTime => "End of process time",
            Core.JobNotificationMessageKind.ActiveProcessLimit => "Active process limit",
            Core.JobNotificationMessageKind.ActiveProcessZero => "No active processes",
            Core.JobNotificationMessageKind.NewProcess => "New process",
            Core.JobNotificationMessageKind.ExitProcess => "Process exited",
            Core.JobNotificationMessageKind.AbnormalExitProcess => "Abnormal process exit",
            Core.JobNotificationMessageKind.ProcessMemoryLimit => "Process memory limit",
            Core.JobNotificationMessageKind.JobMemoryLimit => "Job memory limit",
            Core.JobNotificationMessageKind.NotificationLimit => "Notification threshold",
            Core.JobNotificationMessageKind.JobCycleTimeLimit => "Job cycle-time limit",
            Core.JobNotificationMessageKind.SiloTerminated => "Silo terminated",
            _ => $"Message {value.RawMessageCode}"
        };

        var detail = value.MessageKind switch
        {
            Core.JobNotificationMessageKind.EndOfJobTime => "The per-job user-time limit was reached.",
            Core.JobNotificationMessageKind.EndOfProcessTime => "A process reached its per-process user-time limit.",
            Core.JobNotificationMessageKind.ActiveProcessLimit => "Windows rejected a process because the active-process limit was reached.",
            Core.JobNotificationMessageKind.ActiveProcessZero => "The job currently has no active processes.",
            Core.JobNotificationMessageKind.NewProcess => "A process entered the job.",
            Core.JobNotificationMessageKind.ExitProcess => "A process exited normally.",
            Core.JobNotificationMessageKind.AbnormalExitProcess => "A process exited abnormally.",
            Core.JobNotificationMessageKind.ProcessMemoryLimit => "A process reached its configured memory limit.",
            Core.JobNotificationMessageKind.JobMemoryLimit => "The job reached its configured memory limit.",
            Core.JobNotificationMessageKind.NotificationLimit => DescribeNotificationLimit(value.LimitViolations),
            Core.JobNotificationMessageKind.JobCycleTimeLimit => "The job reached its cycle-time limit.",
            Core.JobNotificationMessageKind.SiloTerminated => "The job silo terminated.",
            _ => "Windows delivered an unrecognized job notification message."
        };

        return new LiveJobNotificationDisplay(
            value.RawMessageCode,
            kind,
            value.ProcessId,
            detail,
            isError: false,
            value.ReceivedAtUtc);
    }

    private static string DescribeNotificationLimit(Core.JobLimitViolations? violations)
    {
        if (violations is null)
        {
            return "A notification-only threshold was crossed; detailed violation state was unavailable.";
        }

        if (violations.ViolatedLimits == Core.JobNotificationLimitFlags.None)
        {
            return "A notification-only packet was acknowledged; current violation flags were clear when queried.";
        }

        return $"Crossed threshold(s): {violations.ViolatedLimits}.";
    }

    private static Core.JobRestrictions ToCore(RestrictionProfile value)
    {
        var hard = value.HardLimits;
        return new Core.JobRestrictions
        {
            ExtendedLimits = new Core.JobExtendedLimits
            {
                PerProcessUserTimeLimit = hard.PerProcessUserTimeLimit,
                PerJobUserTimeLimit = hard.PerJobUserTimeLimit,
                MinimumWorkingSetBytes = hard.MinimumWorkingSetBytes,
                MaximumWorkingSetBytes = hard.MaximumWorkingSetBytes,
                ActiveProcessLimit = hard.ActiveProcessLimit,
                ProcessorAffinityMask = hard.AffinityMask,
                PriorityClass = hard.PriorityClass.HasValue ? (Core.JobPriorityClass)(uint)hard.PriorityClass.Value : null,
                SchedulingClass = hard.SchedulingClass,
                ProcessMemoryLimitBytes = hard.ProcessMemoryLimitBytes,
                JobMemoryLimitBytes = hard.JobMemoryLimitBytes,
                DieOnUnhandledException = hard.DieOnUnhandledException,
                BreakawayAllowed = hard.BreakawayAllowed,
                SilentBreakawayAllowed = hard.SilentBreakawayAllowed,
                KillOnJobClose = hard.KillOnJobClose,
                SubsetAffinity = hard.SubsetAffinityAllowed
            },
            UiRestrictions = (Core.JobUiRestrictions)(uint)value.UiRestrictions,
            CpuRateControl = ToCore(value.Cpu),
            NetworkRateControl = new Core.JobNetworkRateControl
            {
                Enabled = value.Network.ExactMaximumBandwidthBytesPerSecond.HasValue ||
                    value.Network.MaximumBandwidthMegabitsPerSecond.HasValue ||
                    value.Network.DscpTag.HasValue,
                MaximumBandwidthBytesPerSecond = value.Network.ExactMaximumBandwidthBytesPerSecond ??
                    (value.Network.MaximumBandwidthMegabitsPerSecond.HasValue
                        ? checked((ulong)Math.Round(value.Network.MaximumBandwidthMegabitsPerSecond.Value * 125_000d, MidpointRounding.AwayFromZero))
                        : null),
                DscpTag = value.Network.DscpTag
            },
            EndOfJobAction = value.EndAction == JobEndAction.PostNotification
                ? Core.JobEndOfJobAction.PostNotification
                : Core.JobEndOfJobAction.TerminateProcesses,
            NotificationLimits = ToCore(value.Notifications),
            ProcessorGroups = value.ProcessorGroups
                .Select(item => new Core.ProcessorGroupAffinity(item.Group, item.Mask))
                .ToArray()
        };
    }

    private static Core.JobCpuRateControl ToCore(CpuControlSettings value) => new()
    {
        Mode = value.Mode switch
        {
            CpuControlMode.Rate => Core.JobCpuRateMode.Rate,
            CpuControlMode.HardCap => Core.JobCpuRateMode.HardCap,
            CpuControlMode.Weight => Core.JobCpuRateMode.WeightBased,
            CpuControlMode.MinimumMaximum => Core.JobCpuRateMode.MinimumMaximum,
            _ => Core.JobCpuRateMode.Disabled
        },
        Rate = value.RatePercent.HasValue ? PercentToRate(value.RatePercent.Value) : null,
        Weight = value.Weight,
        MinimumRate = value.MinimumPercent.HasValue ? checked((ushort)PercentToRate(value.MinimumPercent.Value)) : null,
        MaximumRate = value.MaximumPercent.HasValue ? checked((ushort)PercentToRate(value.MaximumPercent.Value)) : null,
        Notify = value.Notify,
        UsesUnsupportedPerProcessorCaps = value.UsesUnsupportedPerProcessorCaps
    };

    private static Core.JobNotificationLimits ToCore(NotificationSettings value) => new()
    {
        IoReadBytes = value.IoReadBytes,
        IoWriteBytes = value.IoWriteBytes,
        PerJobUserTime = value.PerJobUserTime,
        JobHighMemoryBytes = value.JobMemoryBytes,
        JobLowMemoryBytes = value.JobLowMemoryBytes,
        CpuRate = ToCore(value.CpuTolerance, value.CpuToleranceInterval),
        IoRate = ToCore(value.IoTolerance, value.IoToleranceInterval),
        NetworkRate = ToCore(value.NetworkTolerance, value.NetworkToleranceInterval)
    };

    private static Core.JobRateNotification? ToCore(RateTolerance tolerance, RateToleranceInterval interval)
    {
        if (tolerance == RateTolerance.None || interval == RateToleranceInterval.None)
        {
            return null;
        }

        return new Core.JobRateNotification(
            (Core.JobRateControlTolerance)(uint)tolerance,
            (Core.JobRateControlToleranceInterval)(uint)interval);
    }

    private static RestrictionProfile FromCore(Core.JobRestrictions value)
    {
        var hard = value.ExtendedLimits;
        return new RestrictionProfile(
            new HardLimitSettings(
                hard.KillOnJobClose,
                hard.BreakawayAllowed,
                hard.SilentBreakawayAllowed,
                hard.DieOnUnhandledException,
                hard.ActiveProcessLimit,
                hard.PerProcessUserTimeLimit,
                hard.PerJobUserTimeLimit,
                hard.MinimumWorkingSetBytes,
                hard.MaximumWorkingSetBytes,
                hard.ProcessMemoryLimitBytes,
                hard.JobMemoryLimitBytes,
                hard.ProcessorAffinityMask,
                hard.SubsetAffinity,
                hard.PriorityClass.HasValue ? (ProcessPriorityChoice)(uint)hard.PriorityClass.Value : null,
                hard.SchedulingClass),
            FromCore(value.CpuRateControl),
            new NetworkControlSettings(
                value.NetworkRateControl.MaximumBandwidthBytesPerSecond.HasValue
                    ? value.NetworkRateControl.MaximumBandwidthBytesPerSecond.Value * 8d / 1_000_000d
                    : null,
                value.NetworkRateControl.DscpTag,
                value.NetworkRateControl.MaximumBandwidthBytesPerSecond),
            (UiRestrictionFlags)(uint)value.UiRestrictions,
            FromCore(value.NotificationLimits),
            value.ProcessorGroups
                .Select(item => new ProcessorGroupAffinity(item.Group, item.AffinityMask))
                .ToArray(),
            value.EndOfJobAction == Core.JobEndOfJobAction.PostNotification
                ? JobEndAction.PostNotification
                : JobEndAction.TerminateAtEndOfJob);
    }

    private static CpuControlSettings FromCore(Core.JobCpuRateControl value) => new(
        value.Mode switch
        {
            Core.JobCpuRateMode.Rate => CpuControlMode.Rate,
            Core.JobCpuRateMode.HardCap => CpuControlMode.HardCap,
            Core.JobCpuRateMode.WeightBased => CpuControlMode.Weight,
            Core.JobCpuRateMode.MinimumMaximum => CpuControlMode.MinimumMaximum,
            _ => CpuControlMode.Disabled
        },
        value.Rate.HasValue ? value.Rate.Value / 100d : null,
        value.Weight,
        value.MinimumRate.HasValue ? value.MinimumRate.Value / 100d : null,
        value.MaximumRate.HasValue ? value.MaximumRate.Value / 100d : null,
        value.Notify,
        value.UsesUnsupportedPerProcessorCaps);

    private static NotificationSettings FromCore(Core.JobNotificationLimits value) => new(
        value.PerJobUserTime,
        value.JobHighMemoryBytes,
        value.JobLowMemoryBytes,
        value.IoReadBytes,
        value.IoWriteBytes,
        FromCore(value.CpuRate?.Tolerance),
        FromCore(value.CpuRate?.Interval),
        FromCore(value.IoRate?.Tolerance),
        FromCore(value.IoRate?.Interval),
        FromCore(value.NetworkRate?.Tolerance),
        FromCore(value.NetworkRate?.Interval));

    private static RateTolerance FromCore(Core.JobRateControlTolerance? value) => value switch
    {
        Core.JobRateControlTolerance.Low => RateTolerance.Low,
        Core.JobRateControlTolerance.Medium => RateTolerance.Medium,
        Core.JobRateControlTolerance.High => RateTolerance.High,
        _ => RateTolerance.None
    };

    private static RateToleranceInterval FromCore(Core.JobRateControlToleranceInterval? value) => value switch
    {
        Core.JobRateControlToleranceInterval.Short => RateToleranceInterval.Short,
        Core.JobRateControlToleranceInterval.Medium => RateToleranceInterval.Medium,
        Core.JobRateControlToleranceInterval.Long => RateToleranceInterval.Long,
        _ => RateToleranceInterval.None
    };

    private static List<LimitViolationDisplay> MapViolations(Core.JobLimitViolations value, DateTimeOffset capturedAt)
    {
        var result = new List<LimitViolationDisplay>();
        AddViolation(Core.JobNotificationLimitFlags.PerJobUserTime, "Job user time", $"{value.PerJobUserTime} used; threshold {value.PerJobUserTimeLimit}.");
        AddViolation(Core.JobNotificationLimitFlags.JobHighMemory, "Job high memory", $"{SizeFormatter.Format(ClampToLong(value.JobMemoryBytes))} used; threshold {SizeFormatter.Format(ClampToLong(value.JobHighMemoryLimitBytes))}.");
        AddViolation(Core.JobNotificationLimitFlags.JobLowMemory, "Job low memory", $"{SizeFormatter.Format(ClampToLong(value.JobMemoryBytes))} used; threshold {SizeFormatter.Format(ClampToLong(value.JobLowMemoryLimitBytes))}.");
        AddViolation(Core.JobNotificationLimitFlags.JobReadBytes, "I/O read", $"{SizeFormatter.Format(ClampToLong(value.IoReadBytes))} read; threshold {SizeFormatter.Format(ClampToLong(value.IoReadBytesLimit))}.");
        AddViolation(Core.JobNotificationLimitFlags.JobWriteBytes, "I/O write", $"{SizeFormatter.Format(ClampToLong(value.IoWriteBytes))} written; threshold {SizeFormatter.Format(ClampToLong(value.IoWriteBytesLimit))}.");
        AddViolation(Core.JobNotificationLimitFlags.CpuRateControl, "CPU rate tolerance", $"Observed {value.CpuRateTolerance}; configured {value.CpuRateToleranceLimit}.");
        AddViolation(Core.JobNotificationLimitFlags.IoRateControl, "I/O rate tolerance", $"Observed {value.IoRateTolerance}; configured {value.IoRateToleranceLimit}.");
        AddViolation(Core.JobNotificationLimitFlags.NetworkRateControl, "Network rate tolerance", $"Observed {value.NetworkRateTolerance}; configured {value.NetworkRateToleranceLimit}.");
        return result;

        void AddViolation(Core.JobNotificationLimitFlags flag, string kind, string detail)
        {
            if (value.ViolatedLimits.HasFlag(flag))
            {
                result.Add(new LimitViolationDisplay(kind, detail, capturedAt));
            }
        }
    }

    private static uint PercentToRate(double percent) =>
        checked((uint)Math.Round(percent * 100d, MidpointRounding.AwayFromZero));

    private static string DescribeAssignmentResult(Core.ProcessAssignmentResult result)
    {
        if (result.Succeeded)
        {
            return "Assigned to the job.";
        }

        if (result.Error is not { } error)
        {
            return "Windows rejected the assignment without returning an error.";
        }

        if (error.Operation != Core.JobOperation.OpenProcess)
        {
            return error.Message;
        }

        return error.NativeErrorCode == 5
            ? "Windows denied the combined process access required for job assignment and PID revalidation. " +
              "OpenProcess failed (5): Access is denied. Required access: PROCESS_SET_QUOTA | " +
              "PROCESS_TERMINATE | PROCESS_QUERY_LIMITED_INFORMATION (0x00001101). " +
              "The earlier limited-access identity check succeeded; this denial concerns the separate assignment handle."
            : $"{error.Message} The earlier identity check succeeded; the process may have exited or its access may have changed after refresh.";
    }

    private static long ClampToLong(ulong value) => value > long.MaxValue ? long.MaxValue : (long)value;

    private static string ReadProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return $"PID {processId}";
        }
        catch (InvalidOperationException)
        {
            return $"PID {processId}";
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return $"PID {processId}";
        }
    }
}
