# WESP Blocking

WESP Blocking is Shackles' proof-of-concept integration with the Windows
Endpoint Security Platform (WESP). It launches or selects an application under
a default-allow policy and asks WESP to deny only the file, registry, and child
process operations selected in the workspace.

This workspace is intended for WESP development and enforcement experiments on
a disposable test machine. WESP is a preview interface, and its API and native
data layouts may change.

## What it is—and is not

WESP Blocking:

- leaves the launched or selected process on the current user's normal Windows
  token;
- does not use a broker, an AppContainer, or a Job Object;
- does not change filesystem or registry ACLs;
- installs client-session WESP rules that apply only to processes carrying the
  session's private context value;
- returns access denied for matching operations; and
- allows everything not covered by an installed rule.

It is not a complete sandbox or a containment boundary. In particular, it does
not isolate identity, network access, the desktop, inherited handles, or work
performed by an already-running broker process.

## Requirements

The current Shackles integration requires:

- an x64 Windows test system supported by the WESP preview package;
- the WESP preview driver installed and the `wesp` driver service running;
- test-signing enabled for the current boot when required by the preview
  driver;
- Shackles running as administrator with a high-integrity process token; and
- an x64 `espclient.dll` compatible with the API surface and layouts used by
  Shackles.

Do not run two Shackles WESP Blocking sessions at the same time. Shackles uses
a stable WESP client registration, and another active instance can prevent a
new client session from being registered or connected.

Shackles ships `espclient.dll` beside `Shackles.exe` and uses that copy by
default. If it is absent, Shackles falls back to
`%SystemRoot%\System32\espclient.dll`. SDK developers can set
`SHACKLES_WESP_CLIENT_DLL` to an explicit full path, but should revalidate the
interop layout tests before substituting a client built from different headers.

The support indicator checks that a usable client DLL and the baseline session
exports are present. It does not prove that the driver is running or that every
configured rule is supported. Shackles checks the draft-specific exports and
driver-facing capabilities when the first blocking session is started.

## Using the workspace

1. Open the **WESP Blocking** workspace. If the current Shackles process is
   below high integrity, the workspace is locked before any policy can be
   configured. Select **Open WESP Blocking as administrator** to open a separate
   elevated copy directly on this workspace; the original window and its other
   workspace state remain open.
2. In the elevated window, wait for the automatic support check. Use **Refresh
   support** to run the check again. Resolve any client-DLL problem before
   continuing.
3. On the **Files** tab, add any existing folders that should be protected and
   choose one of these modes:
   - **Blocked** denies configured reads, opens, and changes.
   - **Read-only** permits ordinary reads but denies creation and modification.
   Select **Block access to UNC paths** if the process tree should be denied
   access to MUP-backed network paths such as `\\server\share`.
4. On the **Registry** tab, add keys and choose **Blocked** or **Read-only**.
   The key does not need to exist. Accepted forms include `HKCU`, `HKLM`,
   `HKCR`, `HKU`, and canonical `\REGISTRY\MACHINE` or `\REGISTRY\USER`
   paths. `HKCC` must be supplied as its explicit canonical native path.
5. On the **Child applications** tab, enter each executable name whose launch
   should be blocked, such as `powershell.exe`. No matching file needs to exist.
   Browsing or pasting a full path is only a convenience; Shackles keeps the
   final name. Matching is case-insensitive but otherwise exact, including the
   extension, so selecting any existing copy of `powershell.exe` also blocks a
   child named `powershell.exe` from another folder.
6. To launch a new root, choose the **Root application**, and optionally enter
   arguments and a working directory. Leave the working directory blank to use
   the root application's folder. Select **Start WESP Blocking and launch**;
   Shackles creates the process suspended and resumes it only after attaching
   the WESP policy context.
7. To use applications that are already running, select **Start WESP Blocking
   with running processes…**. Search or refresh the same running-process picker
   used by the Job Objects workspace, select one or more processes, and choose
   **Apply selected**. A root application is optional, so this can create an
   attach-only session. When a session is active, use **Apply to running
   processes…** to add more processes.
8. While the session is active, the policy and any configured root application
   are fixed. A session with a root can launch it again with different arguments
   or a different working directory. An attach-only session cannot launch a root;
   close it first if a launch is needed.
9. Use **Processes using this session** and **Session activity** to review
   selected processes, root launches, successful child-process creation, and
   operations WESP reports as blocked. Entries show the process, observed target,
   and configured rule when WESP supplies those details. Use **Save logs…** to
   export the retained feed, then select **Close blocking session** when finished.

Before a session is active, **Reset draft** clears the configured rules and
launch fields without starting anything. After a successful close, the final
session-activity rows remain visible for review until the draft is reset or a
new session starts. They can still be saved after the session has closed.

If a blocked folder overlaps a read-only folder, the blocked rule wins in the
overlapping portion.

## Rule semantics

Users enter normal literal paths. Shackles escapes WESP pattern metacharacters
and creates separate case-insensitive matches for the configured object and its
descendants. A folder such as `C:\Data` therefore covers `C:\Data` and
`C:\Data\...`, but not `C:\Database`. Named streams attached directly to the
folder root are covered separately.

### Blocked folders

A blocked folder rule denies the root and its descendants for the blockable
WESP file events used by this POC, including file create/open, reads, writes,
file mappings, file-information queries and changes, directory enumeration,
security and extended-attribute changes, filesystem controls, and file locks.
It also blocks relevant rename operations involving the protected tree.

### Read-only folders

Read-only rules are designed to cover both new-file creation and modification
of existing files through the `IRP_MJ_CREATE` path:

- WESP's pre-create event denies create, replace, overwrite, truncate, and
  delete-on-close requests before the filesystem performs a destructive
  operation.
- WESP's post-create event denies an otherwise successful open that requested
  modification rights, causing the original open to complete with access
  denied.
- Separate rules deny direct writes, writable mappings, file-information and
  security changes, extended-attribute changes, and filesystem controls.
- Rename rules protect the configured tree and its ancestors so the protected
  subtree cannot simply be moved out from under its configured path.

Ordinary read opens remain allowed.

`FILE_OPEN_IF` is denied because it can create a file when the target is absent.
As a result, an application that uses a create-or-open API for read-like access
to an existing file can also be denied; use a pure open operation when testing
ordinary reads.

### UNC paths

The UNC option blocks the normalized NT path `\Device\Mup` and its descendants
using the same full blocked-file event set. The claim is limited to paths that
WESP reports through that MUP namespace; other redirectors or path aliases may
need separate testing.

### Registry keys

Registry rules are matched against canonical native paths and include the
configured key and descendants. Shackles translates common hive aliases before
installing rules:

- `HKCU` resolves to the launching user's SID hive and its separate classes
  mount where appropriate;
- `HKCR` expands to the corresponding user and machine class roots; and
- `HKU` SID roots include their separate classes mount where appropriate.

Blocked keys deny reads, enumeration, opens, and mutations for the registry
events exposed by the tested WESP client. Read-only keys allow queries,
enumeration, and save operations while denying write-capable opens, key/value
changes, security changes, and other supported mutations.

Read-only registry rules also deny create-key events. The tested preview cannot
distinguish a pure create from a create-or-open call early enough, so a caller
that uses a create-or-open API merely to read an existing key can also be
denied.

### Child applications

Child-process rules compare the case-insensitive final image name, not the full
path. Aside from case, the comparison is exact and includes the extension. A
bare name is sufficient and no file needs to exist; full-path entry and Browse
are input conveniences only. The block applies when a tagged process attempts
to create the child; it does not block loading a DLL with the same name.

## Design and lifecycle

At startup, Shackles checks for a high-integrity token before changing WESP
registration state. It locates and loads the client DLL and verifies the exports
required by the draft policy. Before installing anything, it requires WESP to
remove Shackles' prior stable registration, registers and connects a fresh POC
client, then explicitly removes all rules for that connected client. Startup
stops before new rules are installed if any of those cleanup steps cannot confirm
a clean state. This handles state left by a prior crashed process; WESP itself
automatically disconnects a client when its owning process exits.

An active client in another Shackles instance cannot be taken over. WESP rejects
unregistration while that client remains connected, so Shackles reports the
conflict and leaves the existing session alone.

After connecting, Shackles checks the event and built-in file/registry property
capabilities needed by the configured rules. It then creates a random nonzero
policy ID and builds filters that compare Shackles' private process context key
to that per-session value. Every file and registry block rule includes that
process filter, so the rules do not apply to unrelated processes.

The complete rule set is submitted in one WESP update. Shackles can then create
the root process suspended, attach the policy context, and resume its initial
thread. If tagging fails, the suspended process is terminated rather than being
allowed to run outside the policy.

For a selected running process, Shackles captures its PID and creation time in
the picker, asks WESP for a reference to that process, and verifies both values
from the referenced WESP object immediately before setting the context. This
prevents a recycled PID from silently tagging a different process. Applying the
context affects subsequent covered operations. It cannot undo earlier access,
close an existing handle or mapping, or retroactively tag descendants that are
already running.

For an allowed child launch, a process-create rule propagates the same context
to the new process. A following notification rule confirms that the child
carries the context and supplies its image name and PID to the session activity
feed. If WESP cannot establish the tag, a final rule blocks the launch. Rules
that block configured child image names run before propagation.

Rules, process context, and the asynchronous activity queue use WESP
client-session lifetime. Closing a session first asks Windows to terminate only
the root processes launched by Shackles. Processes selected while already
running are released from Shackles' tracking without being terminated. Once the
launched roots have stopped, Shackles removes the rules, closes the activity
queue, disconnects, and unregisters the client. If a launched root remains, or
live callback state cannot be released safely, Shackles reports **Cleanup
needed** and retains the remaining session state so cleanup can be retried.
Other close errors are reported even when the session has closed.

Future descendants are policy-tagged but are not tracked as a lifetime group.
When the session closes successfully, selected existing processes and surviving
descendants may continue after the rules are removed.

The session activity feed records Shackles' successful root launches and
applications selected while running directly. Blocked operations and successful
child-process creations are supplied through WESP's asynchronous notification
queue, so those entries can be delayed or dropped under queue pressure. Shackles
retains only a bounded recent list.
With a validated notification layout, a blocked entry reports the observed
file, registry key, or process image separately from the configured rule that
matched. If a target is absent from the payload, the row says that it is
unavailable instead of substituting the configured rule. Registry value events
identify the containing key rather than the value name, and rename events
currently identify the current/pre-rename object rather than the destination.
Notification delivery is diagnostic and is not part of enforcement.

**Save logs…** writes the current retained feed as a UTF-8 tab-separated file.
It includes the root application, connected WESP client version, a compact
policy summary, and stable columns for time, status, resource type, operation,
process name and PID, observed target, configured rule, and WESP event ID.
Directly recorded root-launch and existing-process rows have no WESP event ID.

Saving refreshes the in-memory feed first when the session is active. The file
is still a point-in-time snapshot: delayed notifications that arrive after it
is written appear only in a later export.

## Compatibility and versioning

The current managed interop profile and its layout tests were validated against
the x64 WESP 0.13 client package and the *Windows Endpoint Security Platform API
Specification, Revision 3.2*.

Shackles reads the connected DLL version and shows it in the session. It does
not reject a client only because its file-version metadata differs. A version
outside the tested profile produces a warning, while required exports,
driver-reported event capabilities, and built-in property support determine
whether the configured rule set can proceed.

Shackles dereferences the extended process-notification payload only when the
connected DLL matches the validated layout profile. With an unvalidated DLL,
enforcement remains enabled and header-level activity rows are retained, but
their process and target are shown as unavailable until that layout is reviewed
and added to the interop profile.

Process context keys are an extensible property namespace rather than built-in
process properties. Shackles therefore validates context-key support through
the filter, rule, and tagging operations that actually consume the key; it does
not pass an encoded context-key ID to the built-in process-property support
query.

A capability check cannot prove that a changed native layout is binary
compatible. When testing a new WESP package, update and run the ABI layout tests
before treating a successful connection as validation.

## Troubleshooting

### Shackles reports that WESP is unavailable

- Confirm that the app-local `espclient.dll` is beside `Shackles.exe`, or that
  a compatible copy exists in `%SystemRoot%\System32`.
- Confirm that Shackles is running as an x64 process.
- If using `SHACKLES_WESP_CLIENT_DLL`, confirm that it contains a valid absolute
  path to the intended x64 DLL.
- A missing-export error usually means the DLL does not expose the API surface
  required by the configured policy.

### Starting WESP Blocking reports access denied

Restart Shackles with **Run as administrator**. Membership in the Administrators
group is not enough when the running process still has a medium-integrity token.

### Registration or connection fails

- Confirm that test-signing is enabled for the current boot.
- Confirm that the `wesp` driver service is installed and running.
- Close any other Shackles instance with an active WESP Blocking session.

Shackles queries the known `wesp` service when formatting these failures and
reports whether it appears running, stopped, absent, or could not be checked.

### A capability or rule-creation check fails

The connected client and driver do not provide something required by the draft
policy, or a new preview package has changed an API contract. Record the exact
operation and HRESULT. Retest with the client DLL supplied with that driver and
run the WESP unit and ABI tests before changing interop definitions.

## Known boundaries

The current claim is limited to tested, direct operations performed by the
tagged process tree. Important gaps include:

- a single-instance application that receives work before its existing process
  is selected and tagged;
- inherited handles, already-open objects, and pre-existing file mappings;
- brokered operations performed by an untagged process;
- reparse points, hard links, path aliases, registry links, and WOW64 registry
  views that may produce an unexpected normalized identity;
- renamed or copied aliases of a blocked child executable, because child rules
  match only the configured final image name;
- DLL and other image loads, which are not child-process creation;
- WESP's File Query Open event, which the tested preview does not make
  blockable;
- enumeration of a protected folder's parent, which can still reveal the
  protected folder's directory entry;
- several registry operations not exposed as blockable rules by the tested
  client, including setting key information, querying multiple values,
  unloading a hive, and flushing a key; and
- inbound rename or load destinations that require further adversarial testing.

Use the workspace to study WESP behavior, not to protect secrets from hostile
code.

## Safe verification checklist

Use disposable folders, files, and registry keys. Avoid selecting Windows,
program installation, or real profile-data roots for an initial test.

- [ ] Start Shackles as administrator and verify that WESP support is detected.
- [ ] Start a session with a harmless existing process, confirm that later
  covered operations are blocked, then close the session and confirm that the
  selected process remains running.
- [ ] Add a disposable blocked folder. From the launched application, confirm
  that reading an existing test file and creating a new file both fail with
  access denied.
- [ ] Add a separate disposable read-only folder. Confirm that reading succeeds,
  while overwriting, appending, deleting, renaming, and creating a new file
  fail.
- [ ] Add a disposable `HKCU\Software\...` test key as read-only. Confirm that a
  query succeeds while setting a value and creating a child key fail. Repeat in
  blocked mode and confirm that the query is denied.
- [ ] Select a harmless child executable to block and confirm that the launched
  process cannot start another copy of that image name from any folder.
- [ ] If UNC blocking is enabled, confirm denial against a harmless test share
  rather than production data.
- [ ] Check that **Session activity** shows the root process, a harmless allowed
  child process, and useful blocked-operation entries with process and target
  names. Save the feed and confirm that the tab-separated log includes the same
  targets and configured rules. Treat missing WESP-notification rows as a
  notification limitation rather than an enforcement result.
- [ ] Close the blocking session and verify that roots launched by Shackles stop,
  selected existing processes remain running, and the workspace no longer
  reports an active or cleanup-needed session.
