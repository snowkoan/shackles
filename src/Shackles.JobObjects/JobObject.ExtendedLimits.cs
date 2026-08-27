using Shackles.JobObjects.Internal;
using Shackles.JobObjects.Interop;

namespace Shackles.JobObjects;

public sealed partial class JobObject
{
    private const NativeExtendedLimitFlags KnownExtendedFlags =
        NativeExtendedLimitFlags.WorkingSet |
        NativeExtendedLimitFlags.ProcessTime |
        NativeExtendedLimitFlags.JobTime |
        NativeExtendedLimitFlags.ActiveProcess |
        NativeExtendedLimitFlags.Affinity |
        NativeExtendedLimitFlags.PriorityClass |
        NativeExtendedLimitFlags.PreserveJobTime |
        NativeExtendedLimitFlags.SchedulingClass |
        NativeExtendedLimitFlags.ProcessMemory |
        NativeExtendedLimitFlags.JobMemory |
        NativeExtendedLimitFlags.DieOnUnhandledException |
        NativeExtendedLimitFlags.BreakawayOk |
        NativeExtendedLimitFlags.SilentBreakawayOk |
        NativeExtendedLimitFlags.KillOnJobClose |
        NativeExtendedLimitFlags.SubsetAffinity;

    public JobExtendedLimits GetExtendedLimits()
    {
        ThrowIfDisposed();
        return FromNative(Query<NativeExtendedLimitInformation>(JobObjectInformationClass.ExtendedLimitInformation));
    }

    /// <summary>
    /// Updates class 9 by querying first and preserving native fields and future flag bits. If the
    /// existing per-job time limit is unchanged, PRESERVE_JOB_TIME is used so accounting is not reset.
    /// </summary>
    public void SetExtendedLimits(JobExtendedLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ThrowIfDisposed();
        Validate(limits);

        lock (_mutationGate)
        {
            var native = Query<NativeExtendedLimitInformation>(JobObjectInformationClass.ExtendedLimitInformation);
            if (FromNative(native) == limits)
            {
                return;
            }

            var currentFlags = native.BasicLimitInformation.LimitFlags;
            var flags = currentFlags & ~KnownExtendedFlags;

            if (limits.PerProcessUserTimeLimit is { } processTime)
            {
                native.BasicLimitInformation.PerProcessUserTimeLimit = JobValidation.ToPositiveTicks(processTime, nameof(limits.PerProcessUserTimeLimit));
                flags |= NativeExtendedLimitFlags.ProcessTime;
            }
            else
            {
                native.BasicLimitInformation.PerProcessUserTimeLimit = 0;
            }

            if (limits.PerJobUserTimeLimit is { } jobTime)
            {
                var ticks = JobValidation.ToPositiveTicks(jobTime, nameof(limits.PerJobUserTimeLimit));
                var hadJobTime = (currentFlags & (NativeExtendedLimitFlags.JobTime | NativeExtendedLimitFlags.PreserveJobTime)) != 0;
                if (hadJobTime && native.BasicLimitInformation.PerJobUserTimeLimit == ticks)
                {
                    flags |= NativeExtendedLimitFlags.PreserveJobTime;
                }
                else
                {
                    native.BasicLimitInformation.PerJobUserTimeLimit = ticks;
                    flags |= NativeExtendedLimitFlags.JobTime;
                }
            }
            else
            {
                native.BasicLimitInformation.PerJobUserTimeLimit = 0;
            }

            if (limits.MinimumWorkingSetBytes is { } minimumWorkingSet)
            {
                native.BasicLimitInformation.MinimumWorkingSetSize = JobValidation.ToNativeSize(minimumWorkingSet, nameof(limits.MinimumWorkingSetBytes));
                native.BasicLimitInformation.MaximumWorkingSetSize = JobValidation.ToNativeSize(limits.MaximumWorkingSetBytes!.Value, nameof(limits.MaximumWorkingSetBytes));
                flags |= NativeExtendedLimitFlags.WorkingSet;
            }
            else
            {
                native.BasicLimitInformation.MinimumWorkingSetSize = 0;
                native.BasicLimitInformation.MaximumWorkingSetSize = 0;
            }

            if (limits.ActiveProcessLimit is { } activeProcessLimit)
            {
                native.BasicLimitInformation.ActiveProcessLimit = activeProcessLimit;
                flags |= NativeExtendedLimitFlags.ActiveProcess;
            }
            else
            {
                native.BasicLimitInformation.ActiveProcessLimit = 0;
            }

            if (limits.ProcessorAffinityMask is { } affinity)
            {
                native.BasicLimitInformation.Affinity = JobValidation.ToNativeSize(affinity, nameof(limits.ProcessorAffinityMask));
                flags |= NativeExtendedLimitFlags.Affinity;
                if (limits.SubsetAffinity)
                {
                    flags |= NativeExtendedLimitFlags.SubsetAffinity;
                }
            }
            else
            {
                native.BasicLimitInformation.Affinity = 0;
            }

            if (limits.PriorityClass is { } priorityClass)
            {
                native.BasicLimitInformation.PriorityClass = (uint)priorityClass;
                flags |= NativeExtendedLimitFlags.PriorityClass;
            }
            else
            {
                native.BasicLimitInformation.PriorityClass = 0;
            }

            if (limits.SchedulingClass is { } schedulingClass)
            {
                native.BasicLimitInformation.SchedulingClass = schedulingClass;
                flags |= NativeExtendedLimitFlags.SchedulingClass;
            }
            else
            {
                native.BasicLimitInformation.SchedulingClass = 0;
            }

            if (limits.ProcessMemoryLimitBytes is { } processMemory)
            {
                native.ProcessMemoryLimit = JobValidation.ToNativeSize(processMemory, nameof(limits.ProcessMemoryLimitBytes));
                flags |= NativeExtendedLimitFlags.ProcessMemory;
            }
            else
            {
                native.ProcessMemoryLimit = 0;
            }

            if (limits.JobMemoryLimitBytes is { } jobMemory)
            {
                native.JobMemoryLimit = JobValidation.ToNativeSize(jobMemory, nameof(limits.JobMemoryLimitBytes));
                flags |= NativeExtendedLimitFlags.JobMemory;
            }
            else
            {
                native.JobMemoryLimit = 0;
            }

            flags |= limits.DieOnUnhandledException ? NativeExtendedLimitFlags.DieOnUnhandledException : 0;
            flags |= limits.BreakawayAllowed ? NativeExtendedLimitFlags.BreakawayOk : 0;
            flags |= limits.SilentBreakawayAllowed ? NativeExtendedLimitFlags.SilentBreakawayOk : 0;
            flags |= limits.KillOnJobClose ? NativeExtendedLimitFlags.KillOnJobClose : 0;
            native.BasicLimitInformation.LimitFlags = flags;

            // These are reserved/output-only in JOBOBJECT_EXTENDED_LIMIT_INFORMATION. Do not echo
            // accounting values obtained by QueryInformationJobObject back into the setter.
            native.IoInfo = default;
            native.PeakProcessMemoryUsed = 0;
            native.PeakJobMemoryUsed = 0;
            Set(JobObjectInformationClass.ExtendedLimitInformation, native);
        }
    }

    private static JobExtendedLimits FromNative(NativeExtendedLimitInformation native)
    {
        var basic = native.BasicLimitInformation;
        var flags = basic.LimitFlags;
        return new JobExtendedLimits
        {
            PerProcessUserTimeLimit = flags.HasFlag(NativeExtendedLimitFlags.ProcessTime)
                ? JobValidation.FromNonNegativeTicks(basic.PerProcessUserTimeLimit)
                : null,
            PerJobUserTimeLimit = (flags & (NativeExtendedLimitFlags.JobTime | NativeExtendedLimitFlags.PreserveJobTime)) != 0
                ? JobValidation.FromNonNegativeTicks(basic.PerJobUserTimeLimit)
                : null,
            MinimumWorkingSetBytes = flags.HasFlag(NativeExtendedLimitFlags.WorkingSet) ? (ulong)basic.MinimumWorkingSetSize : null,
            MaximumWorkingSetBytes = flags.HasFlag(NativeExtendedLimitFlags.WorkingSet) ? (ulong)basic.MaximumWorkingSetSize : null,
            ActiveProcessLimit = flags.HasFlag(NativeExtendedLimitFlags.ActiveProcess) ? basic.ActiveProcessLimit : null,
            ProcessorAffinityMask = flags.HasFlag(NativeExtendedLimitFlags.Affinity) ? (ulong)basic.Affinity : null,
            PriorityClass = flags.HasFlag(NativeExtendedLimitFlags.PriorityClass) ? (JobPriorityClass)basic.PriorityClass : null,
            SchedulingClass = flags.HasFlag(NativeExtendedLimitFlags.SchedulingClass) ? basic.SchedulingClass : null,
            ProcessMemoryLimitBytes = flags.HasFlag(NativeExtendedLimitFlags.ProcessMemory) ? (ulong)native.ProcessMemoryLimit : null,
            JobMemoryLimitBytes = flags.HasFlag(NativeExtendedLimitFlags.JobMemory) ? (ulong)native.JobMemoryLimit : null,
            DieOnUnhandledException = flags.HasFlag(NativeExtendedLimitFlags.DieOnUnhandledException),
            BreakawayAllowed = flags.HasFlag(NativeExtendedLimitFlags.BreakawayOk),
            SilentBreakawayAllowed = flags.HasFlag(NativeExtendedLimitFlags.SilentBreakawayOk),
            KillOnJobClose = flags.HasFlag(NativeExtendedLimitFlags.KillOnJobClose),
            SubsetAffinity = flags.HasFlag(NativeExtendedLimitFlags.SubsetAffinity)
        };
    }

    private static void Validate(JobExtendedLimits limits)
    {
        if (limits.PerProcessUserTimeLimit is { } processTime)
        {
            _ = JobValidation.ToPositiveTicks(processTime, nameof(limits.PerProcessUserTimeLimit));
        }

        if (limits.PerJobUserTimeLimit is { } jobTime)
        {
            _ = JobValidation.ToPositiveTicks(jobTime, nameof(limits.PerJobUserTimeLimit));
        }

        if (limits.MinimumWorkingSetBytes.HasValue != limits.MaximumWorkingSetBytes.HasValue)
        {
            throw new ArgumentException("Minimum and maximum working-set limits must be supplied together.", nameof(limits));
        }

        if (limits.MinimumWorkingSetBytes is { } minimumWorkingSet &&
            (minimumWorkingSet == 0 || limits.MaximumWorkingSetBytes!.Value < minimumWorkingSet))
        {
            throw new ArgumentOutOfRangeException(nameof(limits), "Working-set limits must be positive and minimum cannot exceed maximum.");
        }

        if (limits.ActiveProcessLimit == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limits.ActiveProcessLimit), "The active-process limit must be positive.");
        }

        if (limits.ProcessorAffinityMask == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limits.ProcessorAffinityMask), "The affinity mask must contain at least one processor.");
        }

        if (limits.SubsetAffinity && limits.ProcessorAffinityMask is null)
        {
            throw new ArgumentException("SubsetAffinity requires an affinity mask.", nameof(limits));
        }

        if (limits.PriorityClass is { } priority && !Enum.IsDefined(priority))
        {
            throw new ArgumentOutOfRangeException(nameof(limits.PriorityClass), priority, "Unknown process priority class.");
        }

        if (limits.SchedulingClass is > 9)
        {
            throw new ArgumentOutOfRangeException(nameof(limits.SchedulingClass), "The scheduling class must be from 0 through 9.");
        }

        if (limits.ProcessMemoryLimitBytes == 0 || limits.JobMemoryLimitBytes == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limits), "Memory limits must be positive.");
        }

        if (limits.MinimumWorkingSetBytes is { } minimum)
        {
            _ = JobValidation.ToNativeSize(minimum, nameof(limits.MinimumWorkingSetBytes));
            _ = JobValidation.ToNativeSize(limits.MaximumWorkingSetBytes!.Value, nameof(limits.MaximumWorkingSetBytes));
        }

        if (limits.ProcessorAffinityMask is { } affinity)
        {
            _ = JobValidation.ToNativeSize(affinity, nameof(limits.ProcessorAffinityMask));
            // Processor numbers inside a group are not guaranteed to form a contiguous mask.
            // SetInformationJobObject performs the authoritative topology validation.
        }

        if (limits.ProcessMemoryLimitBytes is { } processMemory)
        {
            _ = JobValidation.ToNativeSize(processMemory, nameof(limits.ProcessMemoryLimitBytes));
        }

        if (limits.JobMemoryLimitBytes is { } jobMemory)
        {
            _ = JobValidation.ToNativeSize(jobMemory, nameof(limits.JobMemoryLimitBytes));
        }
    }
}
