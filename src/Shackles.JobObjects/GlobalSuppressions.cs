using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "The Flags suffix is the standard .NET name for this [Flags] enum and mirrors the Windows SDK terminology.",
    Scope = "type",
    Target = "~T:Shackles.JobObjects.JobNotificationLimitFlags")]
[assembly: SuppressMessage(
    "Naming",
    "CA1720:Identifier contains type name",
    Justification = "Short and Long are the exact documented Windows rate-control tolerance intervals.",
    Scope = "type",
    Target = "~T:Shackles.JobObjects.JobRateControlToleranceInterval")]
[assembly: SuppressMessage(
    "Usage",
    "CA2208:Instantiate argument exceptions correctly",
    Justification = "Validation messages intentionally identify a nested immutable-model property.",
    Scope = "member",
    Target = "~M:Shackles.JobObjects.JobObject.Validate(Shackles.JobObjects.JobExtendedLimits)")]
[assembly: SuppressMessage(
    "Usage",
    "CA2208:Instantiate argument exceptions correctly",
    Justification = "Validation messages intentionally identify a nested immutable-model property.",
    Scope = "member",
    Target = "~M:Shackles.JobObjects.JobObject.Validate(Shackles.JobObjects.JobCpuRateControl)")]
[assembly: SuppressMessage(
    "Usage",
    "CA2208:Instantiate argument exceptions correctly",
    Justification = "Validation messages intentionally identify a nested immutable-model property.",
    Scope = "member",
    Target = "~M:Shackles.JobObjects.JobObject.Validate(Shackles.JobObjects.JobNetworkRateControl)")]
