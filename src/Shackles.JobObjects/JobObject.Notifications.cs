using Shackles.JobObjects.Internal;
using Shackles.JobObjects.Interop;

namespace Shackles.JobObjects;

public sealed partial class JobObject
{
    private const JobNotificationLimitFlags KnownNotificationFlags =
        JobNotificationLimitFlags.PerJobUserTime |
        JobNotificationLimitFlags.JobHighMemory |
        JobNotificationLimitFlags.JobLowMemory |
        JobNotificationLimitFlags.JobReadBytes |
        JobNotificationLimitFlags.JobWriteBytes |
        JobNotificationLimitFlags.CpuRateControl |
        JobNotificationLimitFlags.IoRateControl |
        JobNotificationLimitFlags.NetworkRateControl;

    public JobNotificationLimits GetNotificationLimits()
    {
        ThrowIfDisposed();
        return FromNative(Query<NativeNotificationLimitInformation2>(JobObjectInformationClass.NotificationLimitInformation2));
    }

    internal static JobNotificationLimits FromNative(NativeNotificationLimitInformation2 native)
    {
        var flags = native.LimitFlags;
        return new JobNotificationLimits
        {
            IoReadBytes = flags.HasFlag(JobNotificationLimitFlags.JobReadBytes) ? native.IoReadBytesLimit : null,
            IoWriteBytes = flags.HasFlag(JobNotificationLimitFlags.JobWriteBytes) ? native.IoWriteBytesLimit : null,
            PerJobUserTime = flags.HasFlag(JobNotificationLimitFlags.PerJobUserTime)
                ? JobValidation.FromNonNegativeTicks(native.PerJobUserTimeLimit)
                : null,
            JobHighMemoryBytes = flags.HasFlag(JobNotificationLimitFlags.JobHighMemory) ? native.JobHighMemoryLimit : null,
            JobLowMemoryBytes = flags.HasFlag(JobNotificationLimitFlags.JobLowMemory) ? native.JobLowMemoryLimit : null,
            CpuRate = flags.HasFlag(JobNotificationLimitFlags.CpuRateControl)
                ? new JobRateNotification(
                    NormalizeTolerance(native.CpuRateControlTolerance),
                    NormalizeInterval(native.CpuRateControlToleranceInterval))
                : null,
            IoRate = flags.HasFlag(JobNotificationLimitFlags.IoRateControl)
                ? new JobRateNotification(
                    NormalizeTolerance(native.IoRateControlTolerance),
                    NormalizeInterval(native.IoRateControlToleranceInterval))
                : null,
            NetworkRate = flags.HasFlag(JobNotificationLimitFlags.NetworkRateControl)
                ? new JobRateNotification(
                    NormalizeTolerance(native.NetRateControlTolerance),
                    NormalizeInterval(native.NetRateControlToleranceInterval))
                : null
        };
    }

    private static JobRateControlTolerance NormalizeTolerance(JobRateControlTolerance tolerance) =>
        tolerance == 0 ? JobRateControlTolerance.High : tolerance;

    private static JobRateControlToleranceInterval NormalizeInterval(JobRateControlToleranceInterval interval) =>
        interval == 0 ? JobRateControlToleranceInterval.Short : interval;

    /// <summary>
    /// Sets class 33, returning without a native write when the typed model is unchanged. Windows has
    /// no PRESERVE_JOB_TIME equivalent for notification limits: changing any sibling class-33 field
    /// while PerJobUserTime is configured rebases that relative threshold against accumulated job time.
    /// </summary>
    public void SetNotificationLimits(JobNotificationLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        Validate(limits);
        ThrowIfDisposed();

        lock (_mutationGate)
        {
            var native = Query<NativeNotificationLimitInformation2>(JobObjectInformationClass.NotificationLimitInformation2);
            if (FromNative(native) == limits)
            {
                return;
            }

            native.LimitFlags &= ~KnownNotificationFlags;

            if (limits.IoReadBytes is { } readBytes)
            {
                native.IoReadBytesLimit = readBytes;
                native.LimitFlags |= JobNotificationLimitFlags.JobReadBytes;
            }
            else
            {
                native.IoReadBytesLimit = 0;
            }

            if (limits.IoWriteBytes is { } writeBytes)
            {
                native.IoWriteBytesLimit = writeBytes;
                native.LimitFlags |= JobNotificationLimitFlags.JobWriteBytes;
            }
            else
            {
                native.IoWriteBytesLimit = 0;
            }

            if (limits.PerJobUserTime is { } jobTime)
            {
                native.PerJobUserTimeLimit = JobValidation.ToPositiveTicks(jobTime, nameof(limits.PerJobUserTime));
                native.LimitFlags |= JobNotificationLimitFlags.PerJobUserTime;
            }
            else
            {
                native.PerJobUserTimeLimit = 0;
            }

            if (limits.JobHighMemoryBytes is { } highMemory)
            {
                native.JobHighMemoryLimit = highMemory;
                native.LimitFlags |= JobNotificationLimitFlags.JobHighMemory;
            }
            else
            {
                native.JobHighMemoryLimit = 0;
            }

            if (limits.JobLowMemoryBytes is { } lowMemory)
            {
                native.JobLowMemoryLimit = lowMemory;
                native.LimitFlags |= JobNotificationLimitFlags.JobLowMemory;
            }
            else
            {
                native.JobLowMemoryLimit = 0;
            }

            if (limits.CpuRate is { } cpu)
            {
                native.CpuRateControlTolerance = cpu.Tolerance;
                native.CpuRateControlToleranceInterval = cpu.Interval;
                native.LimitFlags |= JobNotificationLimitFlags.CpuRateControl;
            }
            else
            {
                native.CpuRateControlTolerance = 0;
                native.CpuRateControlToleranceInterval = 0;
            }

            if (limits.IoRate is { } io)
            {
                native.IoRateControlTolerance = io.Tolerance;
                native.IoRateControlToleranceInterval = io.Interval;
                native.LimitFlags |= JobNotificationLimitFlags.IoRateControl;
            }
            else
            {
                native.IoRateControlTolerance = 0;
                native.IoRateControlToleranceInterval = 0;
            }

            if (limits.NetworkRate is { } network)
            {
                native.NetRateControlTolerance = network.Tolerance;
                native.NetRateControlToleranceInterval = network.Interval;
                native.LimitFlags |= JobNotificationLimitFlags.NetworkRateControl;
            }
            else
            {
                native.NetRateControlTolerance = 0;
                native.NetRateControlToleranceInterval = 0;
            }

            Set(JobObjectInformationClass.NotificationLimitInformation2, native);
        }
    }

    /// <summary>
    /// Queries class 34. Windows uses this query to acknowledge a notification-limit message and will
    /// not post another such message until the query has occurred; the class has no supported setter.
    /// </summary>
    public JobLimitViolations GetLimitViolations()
    {
        ThrowIfDisposed();
        return GetLimitViolationsCore();
    }

    private JobLimitViolations GetLimitViolationsCore()
    {
        var native = Query<NativeLimitViolationInformation2>(JobObjectInformationClass.LimitViolationInformation2);
        return new JobLimitViolations
        {
            ConfiguredLimits = native.LimitFlags,
            ViolatedLimits = native.ViolationLimitFlags,
            IoReadBytes = native.IoReadBytes,
            IoReadBytesLimit = native.IoReadBytesLimit,
            IoWriteBytes = native.IoWriteBytes,
            IoWriteBytesLimit = native.IoWriteBytesLimit,
            PerJobUserTime = JobValidation.FromNonNegativeTicks(native.PerJobUserTime),
            PerJobUserTimeLimit = JobValidation.FromNonNegativeTicks(native.PerJobUserTimeLimit),
            JobMemoryBytes = native.JobMemory,
            JobHighMemoryLimitBytes = native.JobHighMemoryLimit,
            JobLowMemoryLimitBytes = native.JobLowMemoryLimit,
            CpuRateTolerance = native.CpuRateControlTolerance,
            CpuRateToleranceLimit = native.CpuRateControlToleranceLimit,
            IoRateTolerance = native.IoRateControlTolerance,
            IoRateToleranceLimit = native.IoRateControlToleranceLimit,
            NetworkRateTolerance = native.NetRateControlTolerance,
            NetworkRateToleranceLimit = native.NetRateControlToleranceLimit
        };
    }

    private static void Validate(JobNotificationLimits limits)
    {
        if (limits.IoReadBytes == 0 || limits.IoWriteBytes == 0 || limits.JobHighMemoryBytes == 0 || limits.JobLowMemoryBytes == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limits), "Notification byte and memory thresholds must be positive.");
        }

        if (limits.PerJobUserTime is { } jobTime)
        {
            _ = JobValidation.ToPositiveTicks(jobTime, nameof(limits.PerJobUserTime));
        }

        if (limits.JobLowMemoryBytes is { } low && limits.JobHighMemoryBytes is { } high && low >= high)
        {
            throw new ArgumentException("The low-memory threshold must be less than the high-memory threshold.", nameof(limits));
        }

        Validate(limits.CpuRate, nameof(limits.CpuRate));
        Validate(limits.IoRate, nameof(limits.IoRate));
        Validate(limits.NetworkRate, nameof(limits.NetworkRate));
    }

    private static void Validate(JobRateNotification? notification, string parameterName)
    {
        if (notification is null)
        {
            return;
        }

        if (!Enum.IsDefined(notification.Tolerance) || !Enum.IsDefined(notification.Interval))
        {
            throw new ArgumentOutOfRangeException(parameterName, notification, "Unknown rate-control tolerance or interval.");
        }
    }
}
