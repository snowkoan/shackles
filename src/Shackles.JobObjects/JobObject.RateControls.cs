using Shackles.JobObjects.Interop;

namespace Shackles.JobObjects;

public sealed partial class JobObject
{
    private const NativeCpuRateFlags EditableCpuRateFlags =
        NativeCpuRateFlags.Enable |
        NativeCpuRateFlags.WeightBased |
        NativeCpuRateFlags.HardCap |
        NativeCpuRateFlags.Notify |
        NativeCpuRateFlags.MinMaxRate;

    private const NativeNetworkRateFlags KnownNetworkRateFlags =
        NativeNetworkRateFlags.Enable |
        NativeNetworkRateFlags.MaximumBandwidth |
        NativeNetworkRateFlags.DscpTag;

    private const JobUiRestrictions KnownUiRestrictions =
        JobUiRestrictions.Handles |
        JobUiRestrictions.ReadClipboard |
        JobUiRestrictions.WriteClipboard |
        JobUiRestrictions.SystemParameters |
        JobUiRestrictions.DisplaySettings |
        JobUiRestrictions.GlobalAtoms |
        JobUiRestrictions.Desktop |
        JobUiRestrictions.ExitWindows |
        JobUiRestrictions.InputMethodEditor |
        JobUiRestrictions.Injection;

    public JobUiRestrictions GetUiRestrictions()
    {
        ThrowIfDisposed();
        return Query<NativeBasicUiRestrictions>(JobObjectInformationClass.BasicUiRestrictions).UiRestrictionsClass & KnownUiRestrictions;
    }

    public void SetUiRestrictions(JobUiRestrictions restrictions)
    {
        ValidateUiRestrictions(restrictions);
        ThrowIfDisposed();
        lock (_mutationGate)
        {
            // Preserve unknown future SDK bits while replacing every restriction this version understands.
            var current = Query<NativeBasicUiRestrictions>(JobObjectInformationClass.BasicUiRestrictions);
            if ((current.UiRestrictionsClass & KnownUiRestrictions) == restrictions)
            {
                return;
            }

            current.UiRestrictionsClass = (current.UiRestrictionsClass & ~KnownUiRestrictions) | (restrictions & KnownUiRestrictions);
            Set(JobObjectInformationClass.BasicUiRestrictions, current);
        }
    }

    public JobCpuRateControl GetCpuRateControl()
    {
        ThrowIfDisposed();
        return FromNative(Query<NativeCpuRateControlInformation>(JobObjectInformationClass.CpuRateControlInformation));
    }

    internal static JobCpuRateControl FromNative(NativeCpuRateControlInformation native)
    {
        if ((native.ControlFlags & NativeCpuRateFlags.PerProcessorCaps) != 0)
        {
            return new JobCpuRateControl { UsesUnsupportedPerProcessorCaps = true };
        }

        if ((native.ControlFlags & NativeCpuRateFlags.Enable) == 0)
        {
            return JobCpuRateControl.Disabled;
        }

        var notify = (native.ControlFlags & NativeCpuRateFlags.Notify) != 0;
        if ((native.ControlFlags & NativeCpuRateFlags.MinMaxRate) != 0)
        {
            return new JobCpuRateControl
            {
                Mode = JobCpuRateMode.MinimumMaximum,
                MinimumRate = native.Value.MinimumRate,
                MaximumRate = native.Value.MaximumRate,
                Notify = notify
            };
        }

        if ((native.ControlFlags & NativeCpuRateFlags.WeightBased) != 0)
        {
            return new JobCpuRateControl { Mode = JobCpuRateMode.WeightBased, Weight = native.Value.Weight, Notify = notify };
        }

        return new JobCpuRateControl
        {
            Mode = (native.ControlFlags & NativeCpuRateFlags.HardCap) != 0 ? JobCpuRateMode.HardCap : JobCpuRateMode.Rate,
            Rate = native.Value.CpuRate,
            Notify = notify
        };
    }

    public void SetCpuRateControl(JobCpuRateControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        Validate(control);
        ThrowIfDisposed();

        lock (_mutationGate)
        {
            var current = Query<NativeCpuRateControlInformation>(JobObjectInformationClass.CpuRateControlInformation);
            if (FromNative(current) == control)
            {
                return;
            }

            EnsureCpuRateControlCanChange(current, control);

            var unknownFlags = current.ControlFlags & ~(EditableCpuRateFlags | NativeCpuRateFlags.PerProcessorCaps);
            var native = current;
            native.ControlFlags = unknownFlags;
            if (unknownFlags == 0)
            {
                native.Value = default;
            }

            if (control.Mode != JobCpuRateMode.Disabled)
            {
                native.Value = default;
                native.ControlFlags |= NativeCpuRateFlags.Enable;
                native.ControlFlags |= control.Notify ? NativeCpuRateFlags.Notify : 0;
                switch (control.Mode)
                {
                    case JobCpuRateMode.Rate:
                        native.Value.CpuRate = control.Rate!.Value;
                        break;
                    case JobCpuRateMode.HardCap:
                        native.ControlFlags |= NativeCpuRateFlags.HardCap;
                        native.Value.CpuRate = control.Rate!.Value;
                        break;
                    case JobCpuRateMode.WeightBased:
                        native.ControlFlags |= NativeCpuRateFlags.WeightBased;
                        native.Value.Weight = control.Weight!.Value;
                        break;
                    case JobCpuRateMode.MinimumMaximum:
                        native.ControlFlags |= NativeCpuRateFlags.MinMaxRate;
                        native.Value.MinimumRate = control.MinimumRate!.Value;
                        native.Value.MaximumRate = control.MaximumRate!.Value;
                        break;
                }
            }

            Set(JobObjectInformationClass.CpuRateControlInformation, native);
        }
    }

    public JobNetworkRateControl GetNetworkRateControl()
    {
        ThrowIfDisposed();
        return FromNative(Query<NativeNetworkRateControlInformation>(JobObjectInformationClass.NetRateControlInformation));
    }

    private static JobNetworkRateControl FromNative(NativeNetworkRateControlInformation native)
    {
        var enabled = (native.ControlFlags & NativeNetworkRateFlags.Enable) != 0;
        return new JobNetworkRateControl
        {
            Enabled = enabled,
            MaximumBandwidthBytesPerSecond = enabled && (native.ControlFlags & NativeNetworkRateFlags.MaximumBandwidth) != 0
                ? native.MaximumBandwidth
                : null,
            DscpTag = enabled && (native.ControlFlags & NativeNetworkRateFlags.DscpTag) != 0 ? native.DscpTag : null
        };
    }

    public void SetNetworkRateControl(JobNetworkRateControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        Validate(control);
        ThrowIfDisposed();

        lock (_mutationGate)
        {
            var current = Query<NativeNetworkRateControlInformation>(JobObjectInformationClass.NetRateControlInformation);
            if (FromNative(current) == control)
            {
                return;
            }

            var unknownFlags = current.ControlFlags & ~KnownNetworkRateFlags;
            var native = current;
            native.ControlFlags = unknownFlags;
            if (unknownFlags == 0)
            {
                native.MaximumBandwidth = 0;
                native.DscpTag = 0;
            }

            if (control.Enabled)
            {
                native.ControlFlags |= NativeNetworkRateFlags.Enable;
                if (control.MaximumBandwidthBytesPerSecond is { } bandwidth)
                {
                    native.ControlFlags |= NativeNetworkRateFlags.MaximumBandwidth;
                    native.MaximumBandwidth = bandwidth;
                }
                else if (unknownFlags == 0)
                {
                    native.MaximumBandwidth = 0;
                }

                if (control.DscpTag is { } dscpTag)
                {
                    native.ControlFlags |= NativeNetworkRateFlags.DscpTag;
                    native.DscpTag = dscpTag;
                }
                else if (unknownFlags == 0)
                {
                    native.DscpTag = 0;
                }
            }

            Set(JobObjectInformationClass.NetRateControlInformation, native);
        }
    }

    public JobEndOfJobAction GetEndOfJobAction()
    {
        ThrowIfDisposed();
        return Query<NativeEndOfJobTimeInformation>(JobObjectInformationClass.EndOfJobTimeInformation).EndOfJobTimeAction;
    }

    public void SetEndOfJobAction(JobEndOfJobAction action)
    {
        ValidateEndOfJobAction(action);
        ThrowIfDisposed();

        lock (_mutationGate)
        {
            var current = Query<NativeEndOfJobTimeInformation>(JobObjectInformationClass.EndOfJobTimeInformation).EndOfJobTimeAction;
            if (current == action)
            {
                return;
            }

            EnsureEndOfJobActionCanChange(action);
            Set(JobObjectInformationClass.EndOfJobTimeInformation, new NativeEndOfJobTimeInformation { EndOfJobTimeAction = action });
        }
    }

    private static void ValidateUiRestrictions(JobUiRestrictions restrictions)
    {
        if ((restrictions & ~KnownUiRestrictions) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(restrictions), restrictions, "The value contains unknown UI-restriction bits.");
        }
    }

    private static void ValidateEndOfJobAction(JobEndOfJobAction action)
    {
        if (!Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown end-of-job action.");
        }
    }

    private void EnsureEndOfJobActionCanChange(JobEndOfJobAction action)
    {
        if (action == JobEndOfJobAction.PostNotification &&
            NotificationDeliveryMode != JobNotificationDeliveryMode.OwnedCompletionPort)
        {
            throw new UnsupportedJobFeatureException(
                nameof(JobEndOfJobAction.PostNotification),
                "No owned completion port is active. Call EnableNotificationDelivery first; without a port Windows terminates the job instead of posting.");
        }
    }

    private static void EnsureCpuRateControlCanChange(
        NativeCpuRateControlInformation current,
        JobCpuRateControl requested)
    {
        if ((current.ControlFlags & NativeCpuRateFlags.PerProcessorCaps) != 0 ||
            requested.UsesUnsupportedPerProcessorCaps)
        {
            throw new UnsupportedJobFeatureException(
                nameof(NativeCpuRateFlags.PerProcessorCaps),
                "This job uses Windows per-processor CPU caps. Shackles preserves an unchanged value but cannot safely edit or reinterpret that mode.");
        }
    }

    private static void Validate(JobCpuRateControl control)
    {
        if (!Enum.IsDefined(control.Mode))
        {
            throw new ArgumentOutOfRangeException(nameof(control.Mode), control.Mode, "Unknown CPU rate mode.");
        }

        if (control.UsesUnsupportedPerProcessorCaps)
        {
            if (control.Mode != JobCpuRateMode.Disabled ||
                control.Notify ||
                control.Rate is not null ||
                control.Weight is not null ||
                control.MinimumRate is not null ||
                control.MaximumRate is not null)
            {
                throw new ArgumentException(
                    "The per-processor-caps marker cannot be combined with editable CPU rate values.",
                    nameof(control));
            }

            return;
        }

        switch (control.Mode)
        {
            case JobCpuRateMode.Disabled:
                if (control.Notify || control.Rate is not null || control.Weight is not null || control.MinimumRate is not null || control.MaximumRate is not null)
                {
                    throw new ArgumentException("Disabled CPU control cannot contain rate, weight, range, or notification values.", nameof(control));
                }

                break;
            case JobCpuRateMode.Rate:
            case JobCpuRateMode.HardCap:
                ValidateRate(control.Rate, nameof(control.Rate));
                RequireNull(control.Weight, control.MinimumRate, control.MaximumRate, nameof(control));
                break;
            case JobCpuRateMode.WeightBased:
                if (control.Weight is null or < 1 or > 9)
                {
                    throw new ArgumentOutOfRangeException(nameof(control.Weight), control.Weight, "Weight must be from 1 through 9.");
                }

                RequireNull(control.Rate, control.MinimumRate, control.MaximumRate, nameof(control));
                break;
            case JobCpuRateMode.MinimumMaximum:
                ValidateRate(control.MinimumRate, nameof(control.MinimumRate));
                ValidateRate(control.MaximumRate, nameof(control.MaximumRate));
                if (control.MinimumRate > control.MaximumRate)
                {
                    throw new ArgumentException("The minimum CPU rate cannot exceed the maximum CPU rate.", nameof(control));
                }

                RequireNull(control.Rate, control.Weight, null, nameof(control));
                break;
        }
    }

    private static void Validate(JobNetworkRateControl control)
    {
        if (!control.Enabled)
        {
            if (control.MaximumBandwidthBytesPerSecond is not null || control.DscpTag is not null)
            {
                throw new ArgumentException("Disabled network control cannot contain bandwidth or DSCP values.", nameof(control));
            }

            return;
        }

        if (control.MaximumBandwidthBytesPerSecond is null && control.DscpTag is null)
        {
            throw new ArgumentException("Enabled network control needs a bandwidth limit, a DSCP tag, or both.", nameof(control));
        }

        if (control.MaximumBandwidthBytesPerSecond == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(control.MaximumBandwidthBytesPerSecond), "Maximum bandwidth must be positive.");
        }

        if (control.DscpTag is > 0x3F)
        {
            throw new ArgumentOutOfRangeException(nameof(control.DscpTag), control.DscpTag, "A DSCP tag must be from 0 through 63.");
        }
    }

    private static void ValidateRate(uint? value, string parameterName)
    {
        if (value is null or < 1 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "A CPU rate must be from 1 through 10,000.");
        }
    }

    private static void RequireNull(object? first, object? second, object? third, string parameterName)
    {
        if (first is not null || second is not null || third is not null)
        {
            throw new ArgumentException("The selected CPU mode contains values for another mode.", parameterName);
        }
    }
}
