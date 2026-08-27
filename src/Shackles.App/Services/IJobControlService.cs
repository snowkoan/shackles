using Shackles.App.Models;

namespace Shackles.App.Services;

internal interface IJobControlService : IDisposable
{
    JobCapabilitySet Capabilities { get; }

    ProcessIdentityCaptureResult CaptureProcessIdentity(int processId);

    IJobSession CreateJob(string? name);

    IJobSession OpenJob(string name);
}

internal interface IJobSession : IDisposable
{
    string? Name { get; }

    bool CreatedNew { get; }

    bool HasOwnedNotificationDelivery { get; }

    event EventHandler<LiveJobNotificationDisplay>? NotificationReceived;

    JobSessionSnapshot GetSnapshot();

    void ApplyRestrictions(RestrictionProfile restrictions);

    IReadOnlyList<AssignmentOutcome> AssignProcesses(IReadOnlyCollection<ProcessIdentity> processes);

    LaunchOutcome LaunchProcess(LaunchRequest request);
}
