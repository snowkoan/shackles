# Shackles vision

## Purpose

Shackles brings distinct Windows process-control mechanisms into one understandable desktop app and reports what Windows accepted or rejected. It serves developers, administrators, and power users; it is not a universal sandbox.

Shackles is also an exploration workbench for restrictions that Windows actually provides. A mechanism does not need to be polished, broadly available, or backed by a stable public contract to be worth exposing. Experimental, build-dependent, partially documented, and evolving facilities are valid subjects when Shackles can identify their scope and invoke them without pretending they are production-ready.

## Experience

Each Windows mechanism gets a focused workspace. Jobs support attach and launch; identity and sandbox policy are launch-only. Every view states scope, lifetime, permissions, host changes, and the verified result.

The interface stays calm and readable. Detailed evidence appears when useful, and predictably unavailable actions are disabled. Availability and support status are reported as observed properties, not used as a reason to hide an otherwise explorable mechanism at build time.

## Current workspaces

- **Job Objects:** attach compatible processes or launch new ones, apply documented limits, and inspect telemetry. Assignment is irreversible and ownership is session-scoped.
- **App Containers:** launch with a reusable per-card SID, AppContainer/LPAC policy, capabilities, and explicit resource grants. File rules can use temporary SID ACLs or experimental BFS. BFS gives agent-style processes path-specific access without changing target ACLs by combining the required `AgenticAppContainer` token capability with per-profile broker policy. Registry access remains ACL-based. Temporary policy is released when the sandbox becomes idle; the profile remains reusable.
- **Experimental Sandboxes:** call the dynamically probed Windows API directly for identity, path, network, and UI policy without ACL changes. Shackles neither falls back nor enables internal feature IDs.

The mechanisms remain separate. Unsupported features stay unavailable rather than being emulated.

## Possible direction

1. Improve diagnostics, verified policy read-back, cleanup evidence, and reusable configurations.
2. Generalize launch targets across Win32 paths, packaged Win32 apps, and UWP activation by querying the system dynamically.
3. Add reversible controls such as EcoQoS, memory priority, affinity, and preferred processors.
4. Explore a separate Windows Filtering Platform workspace for executable- or user-scoped network blocking.
5. Follow the supported successor to the experimental APIs while keeping AppContainer independent.

Launch-time controls cannot undo activity that already happened.

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
