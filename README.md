# Shackles

<img width="1054" height="959" alt="image" src="https://github.com/user-attachments/assets/efd5550d-4ba7-4eff-8efd-5f6b1d9252a3" />

Shackles is a Windows desktop app for creating and inspecting Job Objects, configuring their documented restrictions, and assigning running processes with drag and drop.

The current Job Objects workspace puts running processes beside tool-owned jobs. Drag one or more processes onto a job, review the irreversible-assignment warning, and Shackles applies the assignment and refreshes the job membership. A keyboard-accessible **Assign to job** action is available as well.

> [!IMPORTANT]
> Windows does not provide a detach operation. Once a process is assigned successfully, it remains in that job for the rest of the process lifetime. Shackles validates each PID immediately before assignment and reports success or failure separately for every process.

## Runtime requirement

> [!IMPORTANT]
> The normal, framework-dependent build requires the **.NET 10 Desktop Runtime**. Install it from Microsoft's [official .NET 10 download page](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) before running Shackles. The base **.NET Runtime** and **ASP.NET Core Runtime** do not include WPF and are not sufficient.

- [Download .NET 10 from Microsoft](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
  - To run Shackles, choose **.NET Desktop Runtime 10** for the machine architecture (normally x64).
  - To build Shackles, install the **.NET 10 SDK** instead; the SDK includes the desktop runtime.
- [Microsoft's Windows installation guide](https://learn.microsoft.com/en-us/dotnet/core/install/windows)

You can verify the desktop runtime with:

```powershell
dotnet --list-runtimes
```

Look for `Microsoft.WindowsDesktop.App 10.x` in the output.

Shackles can also be published as a self-contained executable. A self-contained build includes the required .NET runtime and does not require a separate runtime installation.

## Build and run

Prerequisites:

- Windows 10 version 2004 (build 19041) or later, including Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)

```powershell
dotnet restore Shackles.slnx
dotnet build Shackles.slnx -c Release
dotnet run --project src/Shackles.App/Shackles.App.csproj
```

Run the tests with:

```powershell
dotnet test Shackles.slnx -c Release
```

Create the normal x64 build, which uses the separately installed .NET 10 Desktop Runtime, with:

```powershell
dotnet publish src/Shackles.App/Shackles.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -p:PublishSingleFile=true `
  -o artifacts/Shackles-win-x64-framework-dependent
```

Create a self-contained x64 build with:

```powershell
dotnet publish src/Shackles.App/Shackles.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -o artifacts/Shackles-win-x64
```

## What the current Job Objects workspace manages

Shackles uses the documented Win32 Job Object APIs. The current workspace groups its configuration surface by intent rather than exposing an unsafe raw-structure editor:

- hard limits for process count, user time, working set, committed memory, priority, scheduling, affinity, lifetime, and child-process behavior;
- CPU rate modes: rate, hard cap, relative weight, and minimum/maximum rate;
- outbound network bandwidth and DSCP policy where the operating system supports it;
- USER and clipboard restrictions;
- notification-only thresholds, kept visually separate from enforced limits;
- processor-group affinity and end-of-job behavior;
- member processes, accounting, I/O accounting, and limit-violation telemetry.

Shackles queries the complete typed state before editing and refreshes it after applying changes. For structures where partial edits could clear sibling or unknown bits, the Job Objects provider uses query–modify–set; other structures are written as complete, validated models. Unsupported classes are shown as unavailable instead of being emulated. In particular:

- modern Windows does not support `JobObjectSecurityLimitInformation`;
- limit-violation information is read-only telemetry;
- completion-port association is internal owner plumbing, not an arbitrary handle editor;
- disk I/O rate control uses separate APIs that Microsoft documents as unsupported on current Windows versions.

Shackles lists jobs that it created or that you explicitly open by name. The documented Windows API does not enumerate every Job Object on the system, so the UI does not pretend that it does.

The current app is intentionally session-scoped: it owns job handles while it is running and does not install a service. A named Job Object is a kernel object, not a durable policy file; it disappears after its last handle closes and its member processes exit, and it never survives a reboot. A future keeper/broker process would be the right extension for persistent ownership or on-demand elevated assignment.

## `SetInformationJobObject` support matrix

This table covers every information class currently listed by Microsoft for `SetInformationJobObject`. “Covered” means Shackles uses the newer documented superset instead of issuing the older call.

| Class | Shackles support |
| --- | --- |
| `2` — `JobObjectBasicLimitInformation` | **Covered by class 9.** Every class-2 limit is exposed through the extended-limit editor. |
| `4` — `JobObjectBasicUIRestrictions` | **Query and edit.** Includes all flags in the current Windows SDK, including IME and injection restrictions. |
| `5` — `JobObjectSecurityLimitInformation` | **Unsupported.** Windows 10 and later do not support this job-wide class; security limits must be applied to processes individually. Shackles does not emulate them. |
| `6` — `JobObjectEndOfJobTimeInformation` | **Query and edit.** Posting a notification is allowed only while this `JobObject` instance owns a live completion port. |
| `7` — `JobObjectAssociateCompletionPortInformation` | **Owned internally.** This is notification-lifecycle plumbing, not a user-editable restriction or raw handle field. |
| `9` — `JobObjectExtendedLimitInformation` | **Query and edit.** Process/job time, working set, process count, affinity, priority, scheduling, process/job memory, breakaway, unhandled-exception, kill-on-close, and subset-affinity controls. |
| `11` — `JobObjectGroupInformation` | **Covered by class 14.** The older group-ID-only representation is a subset of the group-and-affinity representation. |
| `12` — `JobObjectNotificationLimitInformation` | **Covered by class 33.** The v1 notification structure is a subset of v2. |
| `14` — `JobObjectGroupInformationEx` | **Query and edit.** Processor group and affinity-mask pairs are preserved together. |
| `15` — `JobObjectCpuRateControlInformation` | **Query and edit.** Disabled, rate, hard-cap, weight, and minimum/maximum modes, plus the notification flag. |
| `32` — `JobObjectNetRateControlInformation` | **Query and edit.** Outbound maximum bandwidth and DSCP tagging. |
| `33` — `JobObjectNotificationLimitInformation2` | **Query and edit.** Soft time, memory, and I/O-byte thresholds plus CPU, I/O, and network rate tolerances. |
| `34` — `JobObjectLimitViolationInformation2` | **Query only.** Microsoft's `SetInformationJobObject` page lists class 34, but the structure represents observed violation state and is queried to acknowledge/rearm notification-limit delivery. Shackles never writes it. |

I/O *rate enforcement* is not a `SetInformationJobObject` information class. It uses `SetIoRateControlInformationJobObject`, which Microsoft documents as unsupported starting with Windows 10 version 1607; Shackles therefore does not expose it. This is separate from class-33 I/O-byte thresholds and I/O-rate notification-tolerance fields.

Class 33 has one subtle time behavior: `PerJobUserTimeLimit` is relative to the job time already consumed. Windows provides no `PRESERVE_JOB_TIME` equivalent for this structure, so changing any sibling class-33 field while that time threshold is enabled rebases the effective threshold. Shackles avoids the native write when the complete class-33 model is unchanged, but a real sibling edit necessarily writes the whole structure and rebases the time value.

## Completion-port lifecycle

A genuinely new job gets a private I/O completion port automatically. Shackles owns and consumes that port, queries class 34 to acknowledge/rearm soft-limit messages, and detaches the port when the owning handle or app closes. A job opened by name remains in sampled-query mode because Windows does not provide a safe way to discover and adopt another application's port; the GUI does not replace it. Library callers may explicitly call `EnableNotificationDelivery`, but an opened job gains live delivery only if Windows accepts the new association.

Completion messages are a live convenience view, not an event ledger. Windows guarantees class-33 notification-limit messages, but other job messages are best effort, and associating a port with an already-active job can miss earlier process transitions. Refreshing the snapshot remains the authoritative way to inspect current membership and counters. Snapshot fields are gathered through several native queries and are therefore a close-in-time view rather than one atomic kernel transaction.

> [!WARNING]
> Closing Shackles detaches its owned completion port. If `PostNotification` is selected for a hard per-job user-time limit and no other live completion port is associated afterward, Windows falls back to terminating the job's member processes when that limit expires. Do not rely on `PostNotification` after the port owner exits.

## Important Windows behavior

- Assignment can fail because of process access rights, session boundaries, protected processes, existing incompatible job membership, UI restrictions, or nested-job rules.
- Adding several existing processes is not transactional: an earlier assignment cannot be undone if a later one fails.
- A parent job may impose a stricter effective limit than the values configured on the selected job. Shackles labels its view accordingly.
- UI restrictions are not a security sandbox. They do not restrict filesystem, registry, token, or code-execution access.
- Network rate control applies to outgoing traffic and can be owned by only one job in a nested hierarchy.
- CPU minimum-rate reservations are system-wide: the minimums configured across jobs must total no more than 10,000. Windows can also reject CPU-rate control with `ERROR_NOT_SUPPORTED` when Remote Desktop Services Dynamic Fair Share Scheduling is active.
- Shackles runs with the permissions of the current user and surfaces access-denied failures per process. If you deliberately start it elevated to manage another user's process, its job handles remain confined to that elevated instance.

## Existing tools

If a GUI is not important, [Process Governor](https://github.com/lowleveldesign/process-governor) already provides a good command-line experience for common CPU, memory, affinity, time, and priority limits. [System Informer](https://github.com/winsiderss/systeminformer) and [Process Explorer](https://learn.microsoft.com/en-us/sysinternals/downloads/process-explorer) are strong inspectors. None of them combines drag-and-drop assignment with a complete, editable view of the documented restriction classes, which is the gap Shackles is intended to fill.

## Windows API references

- [Job Objects overview](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects)
- [`SetInformationJobObject`](https://learn.microsoft.com/en-us/windows/win32/api/jobapi2/nf-jobapi2-setinformationjobobject)
- [`QueryInformationJobObject`](https://learn.microsoft.com/en-us/windows/win32/api/jobapi2/nf-jobapi2-queryinformationjobobject)
- [`AssignProcessToJobObject`](https://learn.microsoft.com/en-us/windows/win32/api/jobapi2/nf-jobapi2-assignprocesstojobobject)
- [`JOBOBJECT_NOTIFICATION_LIMIT_INFORMATION_2`](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-jobobject_notification_limit_information_2)
- [`JOBOBJECT_ASSOCIATE_COMPLETION_PORT`](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-jobobject_associate_completion_port)
- [`JOBOBJECT_CPU_RATE_CONTROL_INFORMATION`](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-jobobject_cpu_rate_control_information)
- [`SetIoRateControlInformationJobObject`](https://learn.microsoft.com/en-us/windows/win32/api/jobapi2/nf-jobapi2-setioratecontrolinformationjobobject)
- [Nested jobs](https://learn.microsoft.com/en-us/windows/win32/procthread/nested-jobs)
- [Job Object security and access rights](https://learn.microsoft.com/en-us/windows/win32/procthread/job-object-security-and-access-rights)

## Security notes

Shackles requests the minimum job and process rights needed for each operation, uses owning safe handles for native resources, validates all numeric and textual input, and rechecks process identity before assignment to reduce PID-reuse mistakes. It contains no credentials, cryptographic material, or certificate data.
