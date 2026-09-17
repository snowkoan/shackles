# Shackles vision

## Purpose

Shackles brings distinct Windows process-control mechanisms into one understandable desktop app and reports what Windows accepted or rejected. It serves developers, administrators, and power users; it is not a universal sandbox.

Shackles is also an exploration workbench for restrictions that Windows actually provides. A mechanism does not need to be polished, broadly available, or backed by a stable public contract to be worth exposing. Experimental, build-dependent, partially documented, and evolving facilities are valid subjects when Shackles can identify their scope and invoke them without pretending they are production-ready.

## Experience

Each Windows mechanism gets a focused workspace. Jobs and WESP Blocking support
attach and launch; identity and sandbox policy are launch-only; WFP Blocking
targets a full-path application identity rather than a particular process. Every
view states scope, lifetime, permissions, host changes, and the verified result.

The interface stays calm and readable. Detailed evidence appears when useful, and predictably unavailable actions are disabled. Availability and support status are reported as observed properties, not used as a reason to hide an otherwise explorable mechanism at build time.

## Current workspaces

- **Job Objects:** attach compatible processes or launch new ones, apply documented limits, and inspect telemetry. Assignment is irreversible and ownership is session-scoped.
- **App Containers:** launch with a reusable per-card SID, AppContainer/LPAC policy, capabilities, and explicit resource grants. File rules can use temporary SID ACLs or experimental BFS. BFS gives agent-style processes path-specific access without changing target ACLs by combining the required `AgenticAppContainer` token capability with per-profile broker policy. Registry access remains ACL-based. Temporary policy is released when the sandbox becomes idle; the profile remains reusable.
- **Experimental Sandboxes:** call the dynamically probed Windows API directly for identity, path, network, and UI policy without ACL changes. Shackles neither falls back nor enables internal feature IDs.
- **WESP Blocking:** launch or select a process with its normal user access under
  client-session WESP rules that subtract configured file, registry, UNC, and
  child-application operations. A propagated process context key scopes the rules
  to tagged processes and future children. It is default-allow, makes no
  persistent permission changes, and is presented as a preview proof of concept
  rather than a sandbox.
- **WFP Blocking:** install default-allow network block rules for Windows' ALE
  application identity derived from a full executable path, optionally narrowed
  to the current user, direction, IP family, local/remote CIDR, protocol, exact
  port, or stable local-interface LUID. Policy is configured from user mode but
  enforced by Windows. Rules live in a dynamic BFE session, are explicitly
  removed on normal close, and are removed by BFE if the client disappears.

The mechanisms remain separate. Unsupported features stay unavailable rather than being emulated.

## Possible direction

1. Improve diagnostics, verified policy read-back, cleanup evidence, and reusable configurations.
2. Generalize launch targets across Win32 paths, packaged Win32 apps, and UWP activation by querying the system dynamically.
3. Add reversible controls such as EcoQoS, memory priority, affinity, and preferred processors.
4. Extend WFP Blocking with separate bind/listen/raw-endpoint controls, ranges
   and groups, profile and richer interface matching, explicit loopback and
   packaged-app identity, allowlist policy, and filter/event inspection.
5. Follow the supported successor to the experimental APIs while keeping AppContainer independent.

Controls cannot undo activity that already happened. For a new launch, WESP
Blocking installs its rules before creating the suspended root process and resumes
it only after the process context is attached. For an existing process, it verifies
the selected process identity immediately before tagging it, then applies only to
later covered operations and children started afterward. WFP Blocking instead
changes stateful ALE policy for every matching application identity; Windows can
reauthorize an existing flow after a policy change, and replies belong to the
flow that authorized them rather than to an independent packet direction.

## Principles

- State scope, lifetime, permissions, and host effects precisely.
- Separate requested settings from verified Windows state.
- Explain irreversible, disruptive, or broad consequences first.
- Keep privileged operations narrow and use least privilege.
- Give each mechanism its own workspace.
- Expose real OS mechanisms for exploration even when their contracts are experimental, build-dependent, partially documented, or unpolished.
- Keep experimental contracts isolated, discover them at runtime, label their status plainly, and fail closed when the host cannot provide them.
- Do not require product maturity or broad OS availability as a build-time gate; use runtime evidence and explicit user choice where an operation carries unusual risk.
- Preserve Windows errors and fail clearly without guessing.

## Non-goals

Shackles will not bypass Windows security, call partial controls a complete sandbox, hide host changes or partial failures, or enable internal Windows feature IDs.

## Success

A user should be able to answer five questions without reading Windows API documentation: What is restricted? How broad is the effect? How long will it last? What host state changed? What did Windows actually do?
