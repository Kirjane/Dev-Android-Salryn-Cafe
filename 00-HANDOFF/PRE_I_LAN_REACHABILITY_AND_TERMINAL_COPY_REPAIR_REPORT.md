# PRE-I LAN Reachability and Terminal Copy Repair Report

## Candidate

- Base branch: `KIRCH-PRE-I-SHELL-SIDEBAR-COLLAPSE-POS-USABILITY`
- Accepted base SHA: `fbabfe1303a85972e420b2fb11ffa2c0ae75d64e`
- Candidate branch: `KIRCH-PRE-I-LAN-REACHABILITY-AND-TERMINAL-COPY-REPAIR`
- Implementation commit: `c71592d9b1018372f5dc3aa7dd595a6d5303a5d3`
- Decision: `PRE_I_LAN_REACHABILITY_AND_TERMINAL_COPY_REPAIR_REPAIRED_AND_PASSED`
- Promotion: not performed

The pre-existing local branch name was clean but pointed to older ancestor `6d84e8d` and had no remote branch. It was safely fast-forwarded to the accepted shell candidate before this repair; no unique commit was rewritten or discarded.

## Repair result

1. The terminal link now comes only from `runtime.clientUrl` when `runtime.clientUrlReady` is true. The UI no longer constructs a preview URL from saved bind configuration.
2. Saving a LAN address refreshes runtime status before notifying the operator. A loopback-only running process reports Restart required and does not claim access is ready.
3. The duplicate Restart required status pill was removed. The summary badge uses Action needed while the detailed state provides one Restart required status.
4. Assigned Host PC IPv4 addresses are now detected separately from active listener addresses. Setup can recommend a real host address while listener truth remains the sole readiness authority.
5. The informal phone/build/database-reset copy was replaced with terminal-device reconnection guidance. Developer commands and labels were removed from runtime remediation and firewall copy.
6. Listener URLs, configured-bind details, raw firewall detail, and transport/runtime fields remain collapsed under Advanced diagnostics.
7. Normal connection help now gives client-facing Windows Firewall guidance. The terminal link is shown and copy-enabled only after the running server confirms the exact host and port listener.
8. A new focused gate combines runtime listener cases with source guards for binding, copy readiness, restart copy, assigned-IP detection, Advanced placement, and forbidden developer notes.

## Runtime proof

Browser QA used a fully isolated development root and temporary store. No production or repository development database was used.

| Runtime state | Observed result |
| --- | --- |
| LAN disabled, loopback listener | Assigned Host PC address `192.168.254.127` was detected; the false not-detected warning was absent; terminal link was unavailable and copy disabled. |
| Address saved without restart | LAN status showed Restart required; summary badge showed Action needed; no terminal URL was displayed; copy remained disabled; restart instructions explicitly said to wait for Ready. |
| Explicit LAN listener after restart | Both `127.0.0.1:5299/health` and `192.168.254.127:5299/health` returned HTTP 200. LAN status showed Ready. |
| Ready terminal link | UI displayed and copied exactly `http://192.168.254.127:5299/remote-client`; copy was enabled. |
| Connection help | Client-facing Windows Firewall help was visible without shell commands or development terminology. |
| Diagnostics and console | Advanced diagnostics was closed by default. Browser console errors: zero. |

The isolated API was stopped after QA. The user license SHA-256 was unchanged before and after full CoreSmoke: `3044728E9D104378148ABB77471FCEBF52E6E494C392B56D78CCFA7C93C527CD`.

## Automated gates

- `npm --prefix src/Salryn.App.Web run build` — PASS. Existing Vite chunk-size advisory only.
- `dotnet build Salryn.sln -c Release` — PASS, zero errors.
- Focused `pre-i-lan-reachability-and-terminal-copy-repair` — PASS.
- Full `Salryn.Tools.CoreSmoke` with canonical activation-proof manifest — PASS: `SALRYN Core smoke checks passed.`
- Related focused gates — PASS:
  - `pre-i-lan-setup-ux-clarity`
  - `v2-lan-bind-truth`
  - `v2-host-lan-control-ux`
  - `pre-i1-lan-terminal-activation-management-beta`
  - `pre-i-client-facing-product-polish`
  - `pre-i-action-notification-system`
  - `pre-i-shell-sidebar-collapse-pos-usability`
- `git diff --check` — PASS.

## Runtime gate cases

The new runtime harness proves:

- saved LAN bind plus loopback-only listener => Restart required, no client URL;
- matching host with the wrong port => no client URL;
- matching host and port listener => Ready and exact `/remote-client` URL;
- disabled transport with a matching listener => Disabled and no client URL;
- assigned Host PC address detection remains available independently from listener readiness.

## Files changed

- `src/Salryn.Api.Local/Contracts/RemoteReadOnlyApiContracts.cs`
- `src/Salryn.Api.Local/Services/LocalApiLanRuntimeStatusService.cs`
- `src/Salryn.App.Web/src/pages/HostLanControlPage.tsx`
- `src/Salryn.App.Web/src/types/remoteReadOnlyClient.ts`
- `tests/Salryn.Tests/PreILanReachabilityAndTerminalCopyRepairChecks.cs`
- `tests/Salryn.Tests/PreILanSetupUxClarityChecks.cs`
- `tests/Salryn.Tests/V2LanBindTruthChecks.cs`
- `tests/Salryn.Tests/PreIClientFacingProductPolishChecks.cs`
- `tests/Salryn.Tests/PreIActionNotificationSystemChecks.cs`
- `tools/Salryn.Tools.CoreSmoke/Program.cs`

## Scope boundary

No product, audit, licensing, printer, checkout, inventory, or sales behavior changed. Backend changes are limited to LAN runtime status truth and client-facing LAN guidance. Existing cross-lane source assertions were updated only where the prior expected copy contradicted the repaired reachability contract.

## Remaining manual judgment

No automated or browser-runtime blocker remains. Before promotion, Kirch may still verify the Ready link from a separate physical terminal on the target network, including the actual Windows Firewall policy used at that installation.
