namespace Shackles.JobObjects;

public enum JobFeatureSupport
{
    Supported,
    QueryOnly,
    OwnerManaged,
    Unsupported
}

public sealed record JobFeatureCapability(JobFeatureSupport Support, string Reason)
{
    public bool CanQuery => Support is JobFeatureSupport.Supported or JobFeatureSupport.QueryOnly;

    public bool CanSet => Support == JobFeatureSupport.Supported;
}

/// <summary>Availability of the documented job information classes exposed by Shackles.</summary>
public sealed record JobCapabilities
{
    public required JobFeatureCapability ExtendedLimits { get; init; }

    public required JobFeatureCapability UiRestrictions { get; init; }

    public required JobFeatureCapability CpuRateControl { get; init; }

    public required JobFeatureCapability NetworkRateControl { get; init; }

    public required JobFeatureCapability EndOfJobAction { get; init; }

    public required JobFeatureCapability NotificationLimits { get; init; }

    public required JobFeatureCapability ProcessorGroups { get; init; }

    public required JobFeatureCapability LimitViolationInformation2 { get; init; }

    public required JobFeatureCapability CompletionPortAssociation { get; init; }

    public required JobFeatureCapability SecurityLimits { get; init; }

    public static JobCapabilities Detect()
    {
        var supported = new JobFeatureCapability(JobFeatureSupport.Supported, "Supported by this version of Windows.");
        var unavailablePlatform = new JobFeatureCapability(JobFeatureSupport.Unsupported, "Windows job objects are only available on Windows.");

        if (!OperatingSystem.IsWindows())
        {
            return new JobCapabilities
            {
                ExtendedLimits = unavailablePlatform,
                UiRestrictions = unavailablePlatform,
                CpuRateControl = unavailablePlatform,
                NetworkRateControl = unavailablePlatform,
                EndOfJobAction = unavailablePlatform,
                NotificationLimits = unavailablePlatform,
                ProcessorGroups = unavailablePlatform,
                LimitViolationInformation2 = unavailablePlatform,
                CompletionPortAssociation = unavailablePlatform,
                SecurityLimits = unavailablePlatform
            };
        }

        var requiresWindows8 = OperatingSystem.IsWindowsVersionAtLeast(6, 2)
            ? supported
            : new JobFeatureCapability(JobFeatureSupport.Unsupported, "Requires Windows 8 or Windows Server 2012.");
        var requiresWindows10 = OperatingSystem.IsWindowsVersionAtLeast(10)
            ? supported
            : new JobFeatureCapability(JobFeatureSupport.Unsupported, "Requires Windows 10 or Windows Server 2016.");

        return new JobCapabilities
        {
            ExtendedLimits = supported,
            UiRestrictions = supported,
            CpuRateControl = requiresWindows8,
            NetworkRateControl = requiresWindows10,
            EndOfJobAction = supported,
            NotificationLimits = requiresWindows10,
            ProcessorGroups = requiresWindows8,
            LimitViolationInformation2 = requiresWindows10 with
            {
                Support = requiresWindows10.Support == JobFeatureSupport.Supported
                    ? JobFeatureSupport.QueryOnly
                    : JobFeatureSupport.Unsupported,
                Reason = requiresWindows10.Support == JobFeatureSupport.Supported
                    ? "Class 34 is documented as query-only."
                    : requiresWindows10.Reason
            },
            CompletionPortAssociation = new JobFeatureCapability(
                JobFeatureSupport.OwnerManaged,
                "Completion ports are lifecycle and notification plumbing, not a restriction."),
            SecurityLimits = new JobFeatureCapability(
                JobFeatureSupport.Unsupported,
                "JobObjectSecurityLimitInformation is deprecated and unsupported on modern Windows.")
        };
    }

    internal static JobCapabilities Detect(JobAccessRights access)
    {
        var detected = Detect();
        var queryAndSet = RestrictConfigurableFeature(detected.ExtendedLimits, access);
        return detected with
        {
            ExtendedLimits = queryAndSet,
            UiRestrictions = RestrictConfigurableFeature(detected.UiRestrictions, access),
            CpuRateControl = RestrictConfigurableFeature(detected.CpuRateControl, access),
            NetworkRateControl = RestrictConfigurableFeature(detected.NetworkRateControl, access),
            EndOfJobAction = RestrictConfigurableFeature(detected.EndOfJobAction, access),
            NotificationLimits = RestrictConfigurableFeature(detected.NotificationLimits, access),
            ProcessorGroups = RestrictConfigurableFeature(detected.ProcessorGroups, access),
            LimitViolationInformation2 = (access & JobAccessRights.Query) != 0
                ? detected.LimitViolationInformation2
                : new JobFeatureCapability(
                    JobFeatureSupport.Unsupported,
                    "This handle does not grant JOB_OBJECT_QUERY."),
            CompletionPortAssociation =
                (access & (JobAccessRights.Query | JobAccessRights.SetAttributes)) ==
                (JobAccessRights.Query | JobAccessRights.SetAttributes)
                    ? detected.CompletionPortAssociation
                    : new JobFeatureCapability(
                        JobFeatureSupport.Unsupported,
                        "Owned notification delivery requires JOB_OBJECT_QUERY and JOB_OBJECT_SET_ATTRIBUTES.")
        };
    }

    private static JobFeatureCapability RestrictConfigurableFeature(
        JobFeatureCapability detected,
        JobAccessRights access)
    {
        if (detected.Support == JobFeatureSupport.Unsupported)
        {
            return detected;
        }

        if ((access & JobAccessRights.Query) == 0)
        {
            return new JobFeatureCapability(
                JobFeatureSupport.Unsupported,
                "Canonical reads and writes require JOB_OBJECT_QUERY on this handle.");
        }

        if ((access & JobAccessRights.SetAttributes) == 0)
        {
            return new JobFeatureCapability(
                JobFeatureSupport.QueryOnly,
                "This handle grants JOB_OBJECT_QUERY but not JOB_OBJECT_SET_ATTRIBUTES.");
        }

        return detected;
    }
}
