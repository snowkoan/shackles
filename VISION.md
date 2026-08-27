# Shackles vision

## Purpose

Shackles is a Windows desktop app for placing practical restrictions on processes. It brings supported Windows controls into one understandable interface and reports exactly what Windows accepted or rejected.

It is for developers, administrators, and power users. It is not a general security sandbox.

## Experience

The running-process list stays visible on the left. Separate workspaces on the right represent different Windows control mechanisms, starting with Job Objects. Future controls should not be squeezed into Job Object settings or presented as if they were Job Object features.

Each workspace should explain what it controls, how broad the effect is, how long it lasts, and what permissions it needs. Drag and drop should have a keyboard equivalent, and launching a new process directly under restrictions should be available when it is safer or more capable than attaching later. Every operation should produce a result for each process.

The normal interface should stay calm and readable. Detailed Windows evidence belongs in failure results, where it helps the user act.

## Today

The Job Objects workspace can create and open named jobs, assign existing processes, launch processes directly into a job, edit documented restrictions, and inspect membership, accounting, and notifications.

The limits are part of the product:

- job assignment lasts for the life of the process and cannot be undone;
- attaching an existing process is best effort and Windows may deny it;
- parent jobs can impose stricter limits than the selected job shows;
- some Job Object features are unavailable on current Windows versions;
- Shackles owns its jobs only while the app is running;
- Job Object network rate control affects outgoing traffic only.

Unsupported features should remain visibly unavailable rather than being simulated. Network blocking is not implemented today.

## Possible direction

1. **Polish the foundation.** Keep improving diagnostics, verified read-back, reusable configurations, and the assignment and launch workflows.
2. **Tune running processes.** Add reversible controls such as EcoQoS, memory priority, affinity, and preferred processors where Windows supports them.
3. **Control network access.** A future Windows Filtering Platform workspace could block IP networking for every instance of an executable, optionally limited to one user. It would not be a true one-PID switch. It would need a small privileged background component and reliable cleanup, and it must remain separate from Job Object network-rate control.
4. **Harden new processes.** Offer compatibility-sensitive launch profiles using documented exploit protections and restricted identities. Windows AppContainer may provide stronger isolation for compatible apps; hostile workloads still belong in Windows Sandbox or a virtual machine.

Many security controls work only at launch. Attaching to a running process cannot undo files, connections, child processes, or other effects that already happened.

## Principles

- **Be exact about scope.** Say whether a control applies to one process, every instance of an executable, one user, a job hierarchy, or future launches.
- **Show verified state.** Separate requested settings from values Windows accepted and restrictions inherited elsewhere.
- **Explain consequences first.** Warn before irreversible, disruptive, or broad changes.
- **Use least privilege.** Keep the main interface unelevated and isolate future privileged work behind narrow operations.
- **Use supported Windows APIs.** Do not make undocumented APIs or kernel drivers part of the normal product.
- **Fail clearly.** Distinguish a rejected operation from one Shackles did not attempt, preserve the Windows error, and do not guess.
- **Protect the clean design.** Give each new shackle a focused workspace instead of making Job Objects harder to understand.

## Non-goals

Shackles will not bypass protected processes, endpoint security, or Windows access controls. It will not promise that administrator access makes every process controllable, call Job Objects or network policy a complete sandbox, disguise executable-wide network policy as a one-process checkbox, or hide partial failures.

## Success

A user should be able to answer four questions without reading Windows API documentation: What is restricted? How broad is the effect? How long will it last? What did Windows actually do?
