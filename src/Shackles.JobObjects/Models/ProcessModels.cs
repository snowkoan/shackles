namespace Shackles.JobObjects;

/// <summary>A PID plus its kernel creation timestamp, used to reject PID-reuse races.</summary>
public readonly record struct ProcessIdentity(int ProcessId, long CreationTimeFileTimeUtc)
{
    public DateTimeOffset CreationTimeUtc => new(DateTime.FromFileTimeUtc(CreationTimeFileTimeUtc), TimeSpan.Zero);
}

/// <summary>The result of reading a PID's stable kernel creation timestamp.</summary>
public sealed record ProcessIdentityCaptureResult(
    int ProcessId,
    ProcessIdentity? Identity,
    JobOperationError? Error)
{
    public bool Succeeded => Identity.HasValue && Error is null;
}

public enum JobOperation
{
    CreateJob,
    OpenJob,
    QueryInformation,
    SetInformation,
    CreateCompletionPort,
    AssociateCompletionPort,
    MonitorNotifications,
    OpenProcess,
    ReadProcessIdentity,
    AssignProcess,
    CreateProcess,
    ResumeProcess,
    TerminateProcess
}

public sealed record JobOperationError(JobOperation Operation, int NativeErrorCode, string Message);

public sealed record ProcessAssignmentResult(
    int ProcessId,
    long? CreationTimeFileTimeUtc,
    bool Succeeded,
    JobOperationError? Error);

public sealed record ProcessLaunchOptions(string FileName)
{
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();

    public string? WorkingDirectory { get; init; }

    public bool CreateNoWindow { get; init; }
}

public sealed record LaunchedProcess(int ProcessId, long CreationTimeFileTimeUtc)
{
    public ProcessIdentity Identity => new(ProcessId, CreationTimeFileTimeUtc);

    public DateTimeOffset CreationTimeUtc => Identity.CreationTimeUtc;
}
