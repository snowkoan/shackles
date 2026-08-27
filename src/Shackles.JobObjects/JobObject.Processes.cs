using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Shackles.JobObjects.Internal;
using Shackles.JobObjects.Interop;

namespace Shackles.JobObjects;

public sealed partial class JobObject
{
    private const int ErrorAccessDenied = 5;
    private const int ErrorNotFound = 1168;
    internal static readonly ProcessAccessRights ProcessIdentityQueryAccess = ProcessAccessRights.QueryLimitedInformation;
    internal static readonly ProcessAccessRights ProcessAssignmentAccess =
        ProcessAccessRights.SetQuota | ProcessAccessRights.Terminate | ProcessAccessRights.QueryLimitedInformation;

    /// <summary>
    /// Attempts to capture a PID plus its kernel creation timestamp without requiring a job handle.
    /// PID zero is passed to OpenProcess so callers receive its documented native failure; negative
    /// values remain invalid caller input.
    /// </summary>
    public static ProcessIdentityCaptureResult TryCaptureProcessIdentity(int processId)
    {
        if (processId < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId), processId, "A process ID cannot be negative.");
        }

        EnsureWindows();
        try
        {
            return new ProcessIdentityCaptureResult(processId, CaptureProcessIdentityCore(processId), null);
        }
        catch (JobObjectException exception)
        {
            return new ProcessIdentityCaptureResult(processId, null, exception.ToError());
        }
    }

    public ProcessIdentity CaptureProcessIdentity(int processId)
    {
        ValidateProcessId(processId);
        ThrowIfDisposed();
        return CaptureProcessIdentityCore(processId);
    }

    private static ProcessIdentity CaptureProcessIdentityCore(int processId)
    {
        var rawProcess = NativeMethods.OpenProcess(
            ProcessIdentityQueryAccess,
            inheritHandle: 0,
            checked((uint)processId));
        var error = Marshal.GetLastPInvokeError();
        using var process = new SafeProcessHandle(rawProcess);
        if (process.IsInvalid)
        {
            throw CreateOpenProcessException(processId, ProcessIdentityQueryAccess, error);
        }

        return new ProcessIdentity(
            processId,
            GetCreationTime(process, processId, DescribeProcessAccess(ProcessIdentityQueryAccess)));
    }

    /// <summary>
    /// Best-effort convenience overload. For drag-and-drop, capture and carry a ProcessIdentity when
    /// the process list is populated, then call the identity overload to close the PID-reuse window.
    /// </summary>
    public ProcessAssignmentResult AssignProcess(int processId)
    {
        ValidateProcessId(processId);
        try
        {
            return AssignProcess(CaptureProcessIdentity(processId));
        }
        catch (JobObjectException exception)
        {
            return new ProcessAssignmentResult(processId, null, false, exception.ToError());
        }
    }

    public ProcessAssignmentResult AssignProcess(ProcessIdentity identity)
    {
        ValidateProcessId(identity.ProcessId);
        if (identity.CreationTimeFileTimeUtc <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(identity), identity, "The expected process creation time must be positive.");
        }

        ThrowIfDisposed();

        // AssignProcessToJobObject documents exactly SET_QUOTA and TERMINATE. QUERY_LIMITED is added
        // only to re-read the creation timestamp from this same stable handle before assignment.
        var rawProcess = NativeMethods.OpenProcess(
            ProcessAssignmentAccess,
            inheritHandle: 0,
            checked((uint)identity.ProcessId));
        var openError = Marshal.GetLastPInvokeError();
        using var process = new SafeProcessHandle(rawProcess);
        if (process.IsInvalid)
        {
            return new ProcessAssignmentResult(
                identity.ProcessId,
                identity.CreationTimeFileTimeUtc,
                false,
                CreateOpenProcessException(identity.ProcessId, ProcessAssignmentAccess, openError).ToError());
        }

        long actualCreationTime;
        try
        {
            actualCreationTime = GetCreationTime(
                process,
                identity.ProcessId,
                DescribeProcessAccess(ProcessAssignmentAccess));
        }
        catch (JobObjectException exception)
        {
            return new ProcessAssignmentResult(identity.ProcessId, identity.CreationTimeFileTimeUtc, false, exception.ToError());
        }

        if (actualCreationTime != identity.CreationTimeFileTimeUtc)
        {
            return new ProcessAssignmentResult(
                identity.ProcessId,
                identity.CreationTimeFileTimeUtc,
                false,
                new JobOperationError(
                    JobOperation.AssignProcess,
                    ErrorNotFound,
                    "The PID now belongs to a different process; assignment was refused."));
        }

        if (NativeMethods.AssignProcessToJobObject(_handle, process) == 0)
        {
            var error = Marshal.GetLastPInvokeError();
            return AssignmentFailure(identity, process, error);
        }

        return new ProcessAssignmentResult(identity.ProcessId, identity.CreationTimeFileTimeUtc, true, null);
    }

    public IReadOnlyList<ProcessAssignmentResult> AssignProcesses(IEnumerable<int> processIds)
    {
        ArgumentNullException.ThrowIfNull(processIds);
        return processIds.Select(AssignProcess).ToArray();
    }

    public IReadOnlyList<ProcessAssignmentResult> AssignProcesses(IEnumerable<ProcessIdentity> processes)
    {
        ArgumentNullException.ThrowIfNull(processes);
        return processes.Select(AssignProcess).ToArray();
    }

    /// <summary>Creates a process suspended, assigns it to this job, and only then resumes its primary thread.</summary>
    public LaunchedProcess LaunchProcess(ProcessLaunchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Arguments);
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(options.FileName))
        {
            throw new ArgumentException("An executable path is required.", nameof(options));
        }

        if (options.FileName.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("The executable path cannot contain a null character.", nameof(options));
        }

        var executablePath = Path.GetFullPath(options.FileName);
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("The executable was not found.", executablePath);
        }

        string? workingDirectory = null;
        if (options.WorkingDirectory is { } requestedWorkingDirectory)
        {
            if (string.IsNullOrWhiteSpace(requestedWorkingDirectory))
            {
                throw new ArgumentException("The working directory cannot be empty.", nameof(options));
            }

            if (requestedWorkingDirectory.Contains('\0', StringComparison.Ordinal))
            {
                throw new ArgumentException("The working directory cannot contain a null character.", nameof(options));
            }

            workingDirectory = Path.GetFullPath(requestedWorkingDirectory);
            if (!Directory.Exists(workingDirectory))
            {
                throw new DirectoryNotFoundException($"The working directory does not exist: {workingDirectory}");
            }
        }

        var commandLine = WindowsCommandLine.Build(executablePath, options.Arguments);
        var mutableCommandLine = string.Concat(commandLine, '\0').ToCharArray();
        var flags = ProcessCreationFlags.Suspended;
        flags |= options.CreateNoWindow ? ProcessCreationFlags.CreateNoWindow : 0;

        SafeProcessHandle? process = null;
        SafeThreadHandle? thread = null;
        var resumed = false;
        try
        {
            unsafe
            {
                var startup = new NativeStartupInfo { Size = checked((uint)sizeof(NativeStartupInfo)) };
                var processInformation = new NativeProcessInformation();
                fixed (char* commandLinePointer = mutableCommandLine)
                {
                    if (NativeMethods.CreateProcess(
                            executablePath,
                            commandLinePointer,
                            0,
                            0,
                            inheritHandles: 0,
                            flags,
                            0,
                            workingDirectory,
                            &startup,
                            &processInformation) == 0)
                    {
                        throw LastError(JobOperation.CreateProcess);
                    }
                }

                process = new SafeProcessHandle(processInformation.Process);
                thread = new SafeThreadHandle(processInformation.Thread);
                var processId = checked((int)processInformation.ProcessId);
                var creationTime = GetCreationTime(
                    process,
                    processId,
                    "PROCESS_ALL_ACCESS (handle returned by CreateProcess)");

                if (NativeMethods.AssignProcessToJobObject(_handle, process) == 0)
                {
                    throw LastError(JobOperation.AssignProcess);
                }

                if (NativeMethods.ResumeThread(thread) == ResumeFailed)
                {
                    throw LastError(JobOperation.ResumeProcess);
                }

                resumed = true;
                return new LaunchedProcess(processId, creationTime);
            }
        }
        finally
        {
            // A failure can never allow untrusted child code to escape the intended job: it is still
            // suspended here and is terminated before its handles are released.
            if (!resumed && process is { IsInvalid: false, IsClosed: false })
            {
                _ = NativeMethods.TerminateProcess(process, 1);
            }

            thread?.Dispose();
            process?.Dispose();
        }
    }

    private static long GetCreationTime(
        SafeProcessHandle process,
        int processId,
        string processHandleAccessDescription)
    {
        if (NativeMethods.GetProcessTimes(process, out var creation, out _, out _, out _) == 0)
        {
            var error = Marshal.GetLastPInvokeError();
            throw new JobObjectException(
                JobOperation.ReadProcessIdentity,
                error,
                BuildGetProcessTimesFailureDetail(
                    processId,
                    processHandleAccessDescription,
                    new Win32Exception(error).Message));
        }

        return creation.ToLong();
    }

    private static JobObjectException CreateOpenProcessException(
        int processId,
        ProcessAccessRights requestedAccess,
        int error)
    {
        return new JobObjectException(
            JobOperation.OpenProcess,
            error,
            BuildOpenProcessFailureDetail(
                processId,
                requestedAccess,
                new Win32Exception(error).Message));
    }

    internal static string BuildOpenProcessFailureDetail(
        int processId,
        ProcessAccessRights requestedAccess,
        string nativeMessage)
    {
        var purpose = requestedAccess == ProcessIdentityQueryAccess
            ? "to read its creation time"
            : requestedAccess == ProcessAssignmentAccess
                ? "for assignment and creation-time revalidation"
                : "for the requested operation";
        var assignmentRequirements = requestedAccess == ProcessAssignmentAccess
            ? " PROCESS_SET_QUOTA and PROCESS_TERMINATE are required by AssignProcessToJobObject;" +
              " PROCESS_QUERY_LIMITED_INFORMATION is required to revalidate the PID's creation time before assignment."
            : string.Empty;

        return FormattableString.Invariant(
            $"OpenProcess could not open PID {processId} {purpose}. Requested process access: {DescribeProcessAccess(requestedAccess)}.{assignmentRequirements} Native error: {nativeMessage}");
    }

    internal static string BuildGetProcessTimesFailureDetail(
        int processId,
        string processHandleAccessDescription,
        string nativeMessage)
    {
        return FormattableString.Invariant(
            $"GetProcessTimes could not read PID {processId}'s creation time. The process handle access is {processHandleAccessDescription}; GetProcessTimes requires PROCESS_QUERY_INFORMATION or PROCESS_QUERY_LIMITED_INFORMATION. Native error: {nativeMessage}");
    }

    internal static string DescribeProcessAccess(ProcessAccessRights access)
    {
        if (access == ProcessIdentityQueryAccess)
        {
            return "PROCESS_QUERY_LIMITED_INFORMATION (0x00001000)";
        }

        return access == ProcessAssignmentAccess
            ? "PROCESS_SET_QUOTA | PROCESS_TERMINATE | PROCESS_QUERY_LIMITED_INFORMATION (0x00001101)"
            : FormattableString.Invariant($"process access mask 0x{(uint)access:X8}");
    }

    private ProcessAssignmentResult AssignmentFailure(
        ProcessIdentity identity,
        SafeProcessHandle process,
        int error)
    {
        var nativeMessage = new Win32Exception(error).Message;
        var targetHandleCanAssign = (AccessRights & JobAccessRights.AssignProcess) != 0;
        var evidence = error == ErrorAccessDenied && targetHandleCanAssign
            ? CollectAssignmentDiagnosticEvidence(process)
            : default;
        return new ProcessAssignmentResult(
            identity.ProcessId,
            identity.CreationTimeFileTimeUtc,
            false,
            new JobOperationError(
                JobOperation.AssignProcess,
                error,
                BuildAssignmentFailureMessage(
                    error,
                    nativeMessage,
                    targetHandleCanAssign,
                    evidence)));
    }

    private ProcessAssignmentDiagnosticEvidence CollectAssignmentDiagnosticEvidence(
        SafeProcessHandle process)
    {
        // These probes reuse the PROCESS_QUERY_LIMITED_INFORMATION right that was already required
        // for PID identity validation. They never open another process/job handle or mutate either.
        var processInAnyJob = TryIsProcessInAnyJob(process);
        bool? processInTargetJob = null;
        uint? targetMemberCount = null;
        uint? targetUiRestrictionFlags = null;

        if ((AccessRights & JobAccessRights.Query) != 0)
        {
            processInTargetJob = TryIsProcessInTargetJob(process);
            targetMemberCount = TryGetTargetMemberCount();

            if (TryQueryForAssignmentDiagnostic(
                    JobObjectInformationClass.BasicUiRestrictions,
                    out NativeBasicUiRestrictions uiRestrictions))
            {
                targetUiRestrictionFlags = (uint)uiRestrictions.UiRestrictionsClass;
            }
        }

        return new ProcessAssignmentDiagnosticEvidence(
            processInAnyJob,
            processInTargetJob,
            targetMemberCount,
            targetUiRestrictionFlags);
    }

    private static bool? TryIsProcessInAnyJob(SafeProcessHandle process)
    {
        try
        {
            return NativeMethods.IsProcessInAnyJob(process, job: 0, out var result) != 0
                ? result != 0
                : null;
        }
        catch (Exception)
        {
            // Diagnostics are strictly best-effort and must not replace the assignment error.
            return null;
        }
    }

    private bool? TryIsProcessInTargetJob(SafeProcessHandle process)
    {
        try
        {
            return NativeMethods.IsProcessInJob(process, _handle, out var result) != 0
                ? result != 0
                : null;
        }
        catch (Exception)
        {
            // A concurrent Dispose or an unusual interop failure only makes this fact unknown.
            return null;
        }
    }

    private unsafe bool TryQueryForAssignmentDiagnostic<T>(
        JobObjectInformationClass informationClass,
        out T value)
        where T : unmanaged
    {
        var result = default(T);
        try
        {
            uint returned = 0;
            var succeeded = NativeMethods.QueryInformationJobObject(
                    _handle,
                    informationClass,
                    &result,
                    checked((uint)sizeof(T)),
                    &returned) != 0;
            value = result;
            return succeeded;
        }
        catch (Exception)
        {
            // Read-only diagnostics must never mask the original AssignProcessToJobObject result.
            value = default;
            return false;
        }
    }

    private uint? TryGetTargetMemberCount()
    {
        try
        {
            return checked((uint)GetProcessIds().Count);
        }
        catch (Exception)
        {
            // A sizing race, access change, or concurrent Dispose only makes the count unknown.
            return null;
        }
    }

    internal static string BuildAssignmentDiagnosticSuffix(
        ProcessAssignmentDiagnosticEvidence evidence)
    {
        if (evidence.ProcessInAnyJob is not true)
        {
            return string.Empty;
        }

        var message = new StringBuilder();
        if (evidence.ProcessInTargetJob is false)
        {
            message.Append(
                " Read-only checks show that the process is already associated with at least one other job and is not a member of this target job.");
        }
        else if (evidence.ProcessInTargetJob is true)
        {
            message.Append(
                " Read-only checks show that the process is already a member of this target job.");
        }
        else
        {
            message.Append(
                " Read-only checks show that the process is already associated with at least one job; membership in this target job could not be determined.");
        }

        if (evidence.TargetMemberCount is > 0)
        {
            var memberWord = evidence.TargetMemberCount == 1 ? "member" : "members";
            message.Append(
                CultureInfo.InvariantCulture,
                $" The target job reports {evidence.TargetMemberCount} active {memberWord}, so it is not empty.");
        }

        if (evidence.TargetUiRestrictionFlags is > 0)
        {
            message.Append(
                CultureInfo.InvariantCulture,
                $" The target job reports UI restriction flags 0x{evidence.TargetUiRestrictionFlags:X8}.");
        }

        if (evidence.ProcessInTargetJob is not true)
        {
            message.Append(
                " The failure is consistent with Windows rejecting a nested association: valid nesting requires a compatible hierarchy/subset and neither job may have UI limits.");
            message.Append(
                " These probes cannot identify or inspect the process's existing job, so its exact hierarchy and limits remain unknown.");
            message.Append(
                " Running elevated does not bypass these kernel rules.");
            message.Append(
                " Close and relaunch the process with Shackles' Launch in job action into the intended target, or retry with a new empty target job.");
            message.Append(
                " Launching can still fail if an inherited parent job prevents a compatible nested hierarchy.");
        }

        return message.ToString();
    }

    internal static string BuildAssignmentFailureMessage(
        int error,
        string nativeMessage,
        bool targetHandleCanAssign,
        ProcessAssignmentDiagnosticEvidence evidence)
    {
        var diagnosticSuffix = string.Empty;
        if (error == ErrorAccessDenied)
        {
            diagnosticSuffix = targetHandleCanAssign
                ? BuildAssignmentDiagnosticSuffix(evidence)
                : " This job handle does not grant JOB_OBJECT_ASSIGN_PROCESS, which AssignProcessToJobObject requires. Reopen the named job with AssignProcess access; Shackles' Manage access includes it.";
        }

        return $"{JobOperation.AssignProcess} failed ({error}): {nativeMessage}{diagnosticSuffix}";
    }

    private static void ValidateProcessId(int processId)
    {
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId), processId, "A process ID must be positive.");
        }
    }
}

internal readonly record struct ProcessAssignmentDiagnosticEvidence(
    bool? ProcessInAnyJob,
    bool? ProcessInTargetJob,
    uint? TargetMemberCount,
    uint? TargetUiRestrictionFlags);
