# WFP Blocking

WFP Blocking is Shackles' user-mode policy workspace for the
[Windows Filtering Platform (WFP)](https://learn.microsoft.com/en-us/windows/win32/fwp/windows-filtering-platform-start-page).
It installs block filters in Windows' Application Layer Enforcement (ALE)
authorization layers. Windows performs the enforcement; Shackles configures the
Base Filtering Engine (BFE) through the documented user-mode management API and
does not install a custom driver or callout.

The first version is deliberately default-allow and executable-scoped. A rule
blocks selected network activity for one application identity and may narrow the
match by user, direction, IP version, local or remote network, protocol, port,
and local interface. It does not otherwise isolate or launch the application.

## Requirements and elevation

- Windows 10 version 2004 (build 19041) or later, including Windows 11.
- The **Base Filtering Engine** service must be available.
- Shackles must be started with **Run as administrator** to install or remove
  policy. Reading support status alone does not require changing policy.
- Only one active Shackles WFP session is supported. Its private provider and
  sublayer keys intentionally make a second active session fail clearly.

If the current Shackles window is not running at high integrity, the workspace
stays locked and offers **Open WFP Blocking as administrator**. That opens a
separate elevated copy directly on the WFP workspace, following the same model
as WESP Blocking. Elevating with another account means that account is the
"current user" for user-scoped WFP rules.

## Basic usage

1. Open **WFP Blocking**. If the workspace is locked, select **Open WFP
   Blocking as administrator** and continue in the elevated window.
2. Browse for an executable or enter its full path. The file must exist when the
   rule is added.
3. Select inbound and/or outbound flow authorization, IPv4 and/or IPv6, and
   whether the rule is limited to the current user. An all-users rule requires
   an additional confirmation.
4. Optionally select TCP, UDP, or best-effort ICMP; add one exact local or remote
   TCP/UDP port; enter a local or remote address/CIDR; or select one interface.
   Empty optional fields remain unrestricted.
5. Select **Add block rule**. The first rule opens a dynamic WFP session, and
   every rule takes effect when its complete filter transaction commits.
6. Inspect **Active policy**. **Remove** deletes one logical rule and all native
   filters created for it. Removing the final rule also closes the empty dynamic
   session.
7. Select **Close session** when finished to remove all remaining Shackles WFP
   objects. Closing Shackles performs the same cleanup automatically.

## What a rule matches

Every rule starts with an existing, fully qualified executable path. Direction
and family selections expand into separate filters. Within each such filter,
the selected user, address, protocol, port, and interface conditions are
combined with logical **AND**. An omitted optional field is not constrained.

| Field | Current behavior |
| --- | --- |
| Executable | Required full path. Windows derives an ALE application ID from that path. |
| Direction | Outbound, inbound, or both. |
| IP version | IPv4, IPv6, or both. |
| Protocol | Any, TCP, UDP, or best-effort ICMP. ICMP maps to protocol 1 for IPv4 and 58 for IPv6, but only when Windows attributes the operation to the selected application identity. |
| Local network | One IPv4/IPv6 address or CIDR prefix, such as `192.0.2.10` or `2001:db8::/32`. A bare address becomes `/32` or `/128`. |
| Remote network | One IPv4/IPv6 address or CIDR prefix, with the same normalization as the local network. |
| Local port | One exact port, available only for TCP or UDP. |
| Remote port | One exact port, available only for TCP or UDP. |
| Interface | One local network-interface LUID selected from the interfaces Windows currently reports. |
| User | The account running the elevated Shackles process by default, or all users when **Current user only** is cleared. |

If either network field specifies an address family, the rule installs only for
that family even when both versions were selected. Local and remote network
prefixes in one rule must use the same family. Selecting both directions and
both IP versions without an address-family scope normally creates four filters;
an ICMP selection adds mapped-IPv4 companions. Narrower rules create the
required direction/family combinations. An IPv4-only rule also
creates an IPv6-layer companion per direction, constrained to the corresponding
`::ffff:0:0/96` IPv4-mapped range. Windows can classify traffic from a true
dual-stack socket only at the V6 ALE layer, so the companion prevents that
traffic from bypassing the IPv4 selection. Conversely, an unscoped IPv6-only
rule expands into two V6 filters whose sortable address bounds fall below and
above the mapped range, so it does not over-block IPv4.
IPv6 CIDRs that overlap the mapped range (including `::/0`) are rejected; leave
the address empty to mean all native IPv6 addresses. The whole filter set is
added or removed in one WFP transaction.

### Application identity is a full path

Shackles passes the full path to
[`FwpmGetAppIdFromFileName`](https://learn.microsoft.com/en-us/windows/win32/api/fwpmu/nf-fwpmu-fwpmgetappidfromfilename0)
and matches the returned `FWPM_CONDITION_ALE_APP_ID`. This is Windows' normalized
application identity for ALE. It is not:

- a basename match such as every `python.exe` on the machine;
- a PID or one selected process instance;
- a hash, signer, file ID, or certificate identity; or
- automatic child-process inheritance.

Consequently, a rule applies to current and future executions that Windows
classifies with that path identity, regardless of who launched them, subject to
the optional user condition. Moving or copying the executable to another path
requires another rule. Replacing the file at the same path is not prevented or
detected by this identity. A process can also delegate network work to another
executable, service, broker, or already-open handle outside the matched identity.

The executable must exist when the rule is created so Windows can derive its
application ID. Shackles validates and stores an absolute path, but Windows—not
the display filename in the workspace—is authoritative for the final ALE
identity.

### Current-user scope

With **Current user only** selected, Shackles adds an
`FWPM_CONDITION_ALE_USER_ID` security descriptor for the SID of the elevated
Shackles process. Clearing the option omits that condition, so every user running
the matched executable path is in scope. This is an ALE socket-owner identity,
not a restriction on the interactive desktop session, and it does not select a
particular PID.

## Direction is stateful

The current filters use `ALE_AUTH_CONNECT` for outbound authorization and
`ALE_AUTH_RECV_ACCEPT` for inbound authorization, with separate IPv4 and IPv6
layers. These describe how an ALE flow begins, not the direction of every packet:

| Selected direction | Windows authorizes at | Practical meaning |
| --- | --- | --- |
| Outbound | `ALE_AUTH_CONNECT_V4/V6` | TCP `connect`; the first UDP send to a remote tuple; the first outbound non-error ICMP exchange. |
| Inbound | `ALE_AUTH_RECV_ACCEPT_V4/V6` | TCP `accept`; the first inbound UDP packet from a remote tuple; the first inbound non-error ICMP exchange. |

Once ALE authorizes a flow, its reply traffic belongs to the same flow. An
outbound-only block therefore blocks outbound-initiated connections; it does not
mean "drop every packet whose wire direction is outbound." Likewise, an inbound
block does not block replies belonging to an allowed outbound flow.

WFP can reauthorize an ALE flow after policy changes, so adding or removing a
rule may affect an already established flow when Windows next reauthorizes it.
That is still not packet-by-packet filtering, and some fields can be unavailable
during reauthorization. For a deterministic test, begin a new connection after
installing the rule and test inbound- and outbound-initiated flows separately.

The current inbound rule does not deny `bind()` or `listen()` themselves. It
denies covered inbound connection or first-packet authorization. Explicit
bind/listen/raw-endpoint controls require other ALE layers and are listed under
future additions below.

ICMP is marked best effort because some Windows ICMP APIs and brokers can cause
the network operation to be attributed to `System` or another component instead
of the calling executable. In that case an application-path (and possibly user)
condition for the original caller will not match, including under **Any
protocol**. Test the exact target API on the intended Windows build; this first
version has no elevated live-event trace that can prove its effective ALE app
identity.

## Address, port, and interface scope

**Local** and **remote** retain their endpoint meaning in either direction:
local is the address or port on this computer; remote is the peer. They are not
aliases for source and destination. CIDR input is normalized to its network
boundary, so `192.0.2.99/24` is shown and installed as `192.0.2.0/24`.

An address-unscoped rule is not an "internet only" rule. It also covers private,
link-local, and loopback traffic that Windows classifies at these ALE layers.
Loopback can be targeted with `127.0.0.0/8` or `::1/128`, but the current
block-only model cannot express "everything except loopback" as one rule.

An interface rule uses `FWPM_CONDITION_IP_LOCAL_INTERFACE` with a 64-bit
`NET_LUID`. Shackles obtains that LUID from the adapter GUID; IPv4 and IPv6
interface indexes are retained only as diagnostics. Unlike an interface index,
a provider-owned LUID is designed to remain stable across computer restarts and
works for both IP families. Removing and recreating an adapter or virtual/VPN
interface can still produce a different LUID, so saved configurations must
resolve the intended interface again.

"Local interface" is the interface Windows exposes at the chosen ALE layer. It
is not always equivalent to "the packet arrived on Wi-Fi" on a weak-host,
routed, tunneled, or reauthorized flow. Arrival interface, next-hop interface,
interface type, tunnel type, and network profile are distinct WFP conditions;
the current model does not expose them.

## Session lifetime and cleanup

WFP supports persistent policy, but **Shackles does not create persistent WFP
objects**. `WfpSession.Open` uses `FWPM_SESSION_FLAG_DYNAMIC`, which gives the
provider, sublayer, and filters the lifetime of the engine session.

Normal and abnormal cleanup intentionally overlap:

1. On normal workspace or application shutdown, Shackles transactionally deletes
   every installed filter, then its sublayer and provider, and finally closes the
   engine session. An explicit **Close session** operation surfaces cleanup
   warnings; application shutdown performs the same best-effort sequence before
   process exit.
2. If explicit deletion fails, closing the engine remains a cleanup backstop.
3. If Shackles crashes or is terminated, RPC rundown ends the dynamic session
   and BFE automatically deletes all objects owned by it. The rules do not
   survive that session, a BFE restart, or a reboot.

Each engine session has a fresh session GUID. Each logical rule and native filter
also has its own GUID, and filter display names include the session, rule,
direction/family, and filter keys. Provider data carries a Shackles marker plus
the session and optional rule key. The provider and sublayer use stable,
Shackles-owned keys so concurrent sessions collide instead of silently sharing
policy. These names and markers make live inspection unambiguous and leave a
reliable signature for a future enumerator or recovery tool if Shackles ever
adds non-dynamic policy.

The current implementation does not need a next-launch "orphan reaper" for a
crashed process because BFE owns that dynamic-session cleanup. It also does not
silently delete similarly named third-party objects.

## Example rule shapes

| Goal | Rule shape |
| --- | --- |
| Block all covered inbound/outbound ALE flow authorization for one executable and the current user | Both directions, IPv4 + IPv6, any protocol, no optional scopes. |
| Block calls to one service subnet | Outbound, the subnet's IP family, TCP, remote CIDR plus the service's remote port. |
| Stop one program accepting a local TCP service | Inbound, TCP, the service's local port; optionally add a local address or interface. |
| Block traffic only on one adapter | Select the adapter LUID and the required directions/families; leave address fields empty unless both constraints are wanted. |

These are independent block rules. There is no implicit exception relationship:
for example, a broad all-network block plus a narrower rule does not create an
allow exception.

## Current boundaries

- This is block-only, default-allow policy. It has no allowlist mode or explicit
  permit filters.
- Rules identify an executable path, optionally combined with one user. They do
  not target one PID, a process tree, a basename, a hash, or a publisher.
- One rule accepts one local prefix, one remote prefix, one local port, one
  remote port, and one interface. Port ranges and arbitrary address ranges are
  not supported; use separate rules for separate CIDRs.
- DNS names are not conditions. Resolve and maintain addresses outside the
  current workspace if a destination does not have stable IP ranges.
- `Any` protocol means protocols classified at the selected ALE authorization
  layers. It is not Ethernet, per-packet, payload, DNS, TLS, or HTTP filtering.
- There is no separate bind, listen, raw-socket-creation, promiscuous-mode,
  loopback, network-profile, interface-type, or tunnel-type switch.
- Packaged application identity is not matched. Shared hosts and packaged apps
  need package-aware conditions rather than only a host executable path.
- The workspace does not launch, suspend, terminate, or otherwise contain the
  target process. It does not revoke filesystem, registry, token, or IPC access.
- The first version reports the filters installed by its own session but does
  not provide packet logs, byte counters, BFE net-event subscriptions, or a
  general WFP policy browser.
- Existing Windows Firewall, IPsec, VPN, security-product, and other WFP policy
  remains in force and can add further restrictions. WFP treats an ordinary
  `FWP_ACTION_BLOCK` filter as a hard block by default; a matching Shackles filter can
  veto a hard permit in another sublayer, with Windows conflict auditing or
  notification. Arbitration does not cross filtering layers, and a privileged
  actor can still remove or replace Shackles policy.

Treat WFP Blocking as one mechanism-specific network control, not a complete
sandbox or an adversarial boundary.

## Researched future additions

The following extensions fit documented WFP capabilities, but are not part of
the current rule contract:

1. **Bind, listen, and raw-endpoint controls.** Add deliberately separate rules
   at `ALE_RESOURCE_ASSIGNMENT` for explicit or implicit bind, raw socket
   creation, and promiscuous-mode requests, and at `ALE_AUTH_LISTEN` for TCP
   `listen()`. Keeping these separate avoids presenting receive/accept blocking
   as if it prevented endpoint creation.
2. **Ranges and reusable groups.** Add port ranges through WFP range values,
   multiple CIDRs/ports/interfaces, and named rule groups while preserving an
   inspectable expansion into native filters.
3. **Profiles and richer interface semantics.** Offer domain/private/public
   profile IDs, interface and tunnel types, and explicit arrival or next-hop
   interface matching. Profile-crossing reauthorization and VPN churn need
   careful UI and test coverage.
4. **Loopback policy.** Expose an explicit include/exclude/only choice using WFP
   loopback classification instead of assuming local IPC should follow internet
   policy.
5. **Package identity.** Add package SID/family conditions for UWP and packaged
   Win32 applications, with path identity retained where it remains meaningful.
   Fully qualified binary name/security attributes are another possible identity
   axis on supported Windows versions.
6. **Allowlist mode.** Build explicit permit exceptions over a block baseline,
   with well-defined sublayer weights and action arbitration. DNS, DHCP,
   loopback, captive portals, VPNs, and required broker traffic need visible
   treatment so an apparently narrow allowlist is not misleading.
7. **Inspection and recovery.** Enumerate filters by the stable provider key and
   validate the Shackles marker, add BFE net-event diagnostics, and provide an
   explicit recovery view if persistent policy is ever introduced. Dynamic
   sessions should remain the default for temporary workspace rules.

## Windows references

- [Application Layer Enforcement](https://learn.microsoft.com/en-us/windows/win32/fwp/application-layer-enforcement--ale-)
- [ALE layers](https://learn.microsoft.com/en-us/windows/win32/fwp/ale-layers)
- [ALE stateful filtering](https://learn.microsoft.com/en-us/windows/win32/fwp/ale-stateful-filtering)
- [ALE reauthorization](https://learn.microsoft.com/en-us/windows/win32/fwp/ale-re-authorization)
- [Filtering conditions available at each layer](https://learn.microsoft.com/en-us/windows/win32/fwp/filtering-conditions-available-at-each-filtering-layer)
- [Filter arbitration](https://learn.microsoft.com/en-us/windows/win32/fwp/filter-arbitration)
- [WFP object management and lifetimes](https://learn.microsoft.com/en-us/windows/win32/fwp/object-management)
- [`FWPM_SESSION0` and dynamic sessions](https://learn.microsoft.com/en-us/windows/win32/api/fwpmtypes/ns-fwpmtypes-fwpm_session0)
- [`NET_LUID` values](https://learn.microsoft.com/en-us/windows-hardware/drivers/network/net-luid-value)
- [NDIS interface identity and persistence](https://learn.microsoft.com/en-us/windows-hardware/drivers/network/ndis-network-interface-services)
