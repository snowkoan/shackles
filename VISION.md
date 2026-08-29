# Shackles vision

## Purpose

Shackles brings distinct Windows process-control mechanisms into one understandable desktop app and reports what Windows accepted or rejected. It serves developers, administrators, and power users; it is not a universal sandbox.

## Experience

Each Windows mechanism gets a focused workspace. Jobs support attach and launch; identity and sandbox policy are launch-only. Every view states scope, lifetime, permissions, host changes, and the verified result.

The interface stays calm and readable. Detailed evidence appears when useful, and predictably unavailable actions are disabled.

## Current workspaces

- **Job Objects:** attach compatible processes or launch new ones, apply documented limits, and inspect telemetry. Assignment is irreversible and ownership is session-scoped.
- **App Containers:** launch into a reusable per-card SID with AppContainer/LPAC policy, capabilities, and explicit resource grants. ACL changes are journaled and cleanup is best effort.
- **Experimental Sandboxes:** call the dynamically probed Windows API directly for identity, path, network, and UI policy without ACL changes. Shackles neither falls back nor enables internal feature IDs.

The mechanisms remain separate. Unsupported features stay unavailable rather than being emulated.

## Possible direction

1. Improve diagnostics, verified read-back, cleanup evidence, and reusable configurations.
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
- Prefer supported APIs; keep experimental contracts isolated, dynamically probed, and nonessential.
- Preserve Windows errors and fail clearly without guessing.

## Non-goals

Shackles will not bypass Windows security, call partial controls a complete sandbox, hide host changes or partial failures, or enable internal Windows feature IDs.

## Success

A user should be able to answer five questions without reading Windows API documentation: What is restricted? How broad is the effect? How long will it last? What host state changed? What did Windows actually do?
