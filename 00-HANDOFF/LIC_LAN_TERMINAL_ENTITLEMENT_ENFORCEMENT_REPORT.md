# LIC-LAN — Terminal Entitlement Enforcement Report

**Branch:** `KIRCH-LIC-LAN-TERMINAL-ENTITLEMENT-ENFORCEMENT`
**Base:** `KIRCH-LIC-BETA-MANUAL-HOST-TERMINAL-ACTIVATION` @ Phase A final SHA
**Phase A WEB-A3 repaired SHA:** `a7ca61488fa844de4f053a2882b41efe6c791db9`
**Final SHA:** branch tip (`git rev-parse HEAD` after commit)
**Status:** `CANDIDATE_AUTOMATED_PROVEN`

**Protected refs (unchanged):**
- `KIRCH-SANDBOX` = `1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`
- `KIRCH-INSTALLER-M1` = `6b9c46676390b8a86a989eca600cb02e406ce865`

**Beta contact:** `salryn.info@gmail.com`

---

## PASS 1 findings

### Where LAN terminal count was enforced
- `RemotePosPairingGateService.AcceptAsync` and `ActivateAsync` compared paired/reserved counts to `RemotePosCapacityPolicy.MaxPairedTerminals`.
- `GetCapacityStatusAsync` reported the same static policy.
- `RemotePosOperatorSessionService` used `MaxActiveSessions` from the same policy.

### Static defaults
- `RemotePosCapacityPolicy`: DefaultMaxPairedTerminals = **5**, DefaultMaxActiveSessions = **3**, HardCap = **10**.
- `ClampToHardCap` forced values `<= 0` to **1** (abuse floor), so commercial “0 terminals” could not be represented by that policy alone.

### Tracking of terminals / sessions
- Active paired cashier-client terminals: durable terminal registry (`ListTerminalsAsync`, non-revoked CashierClient).
- Pending/accepted join requests: **in-memory** concurrent dictionary on the pairing gate (process-scoped; not durable across restart).
- Active remote POS sessions: in-memory session service count.

### Host / client identity
- Host: local operator + store/host IDs from existing pairing invite / deployment identity path.
- Terminal: terminal registry TerminalId, handshake session, observed origin.
- Commercial license fields available on host license: `LicenseId`, `StoreId`, `AllowedLanTerminals`, `LicenseType`.
- Terminal license fields: `HostLicenseId`, `TerminalId`, `StoreId`, `LicenseType` (local install model from LIC-BETA).

### Safe enforcement points chosen
1. Host entitlement evaluation from local offline Host license.
2. Join-code rotate reject when not entitled / AllowedLanTerminals = 0.
3. Approve (accept) reject when over licensed limit or not entitled.
4. Activate reject when over licensed limit or not entitled.
5. Capacity status surfaces licensed max + entitlement message to UI.
6. Optional terminal license JSON proof on activation (StoreId / HostLicenseId / type) when presented.

---

## Implementation

### New
- `LocalLanTerminalEntitlementContracts.cs` — status + error codes
- `LocalLanTerminalEntitlementService.cs` — host entitlement + terminal proof validation
- `LocalLicensingLanTerminalEntitlementEnforcementChecks.cs` + CoreSmoke `lic-lan-terminal-entitlement-enforcement`

### Wired
- `RemotePosPairingGateService` uses entitlement effective max (can be **0**) instead of static 5 alone
- `RemotePosApiServices` constructs entitlement service with `LocalLicenseService`
- `RemotePosCapacityStatusResponse` extended with license fields
- `RemotePosPairingActivationRequest.TerminalLicenseJson` optional proof field
- Host LAN UI copy + disable rotate/accept when not entitled
- Join-code rotate returns `Result` and is rejected without entitlement

### Behavior summary
| Condition | Result |
|-----------|--------|
| No / invalid Host license | Effective max = 0; rotate/approve/activate blocked |
| Host AllowedLanTerminals = 0 | Blocked; contact email in message |
| Host AllowedLanTerminals = N | Effective max = min(N, hard cap 10, policy max) |
| Terminal license as host operational license | Host LAN blocked |
| Host license as terminal proof | Rejected (`HostLicenseAsTerminal`) |
| Wrong StoreId / HostLicenseId on proof | Rejected |
| Valid terminal proof | Accepted |

---

## PASS 2 findings

| Check | Result |
|-------|--------|
| Terminal cannot unlock host POS | Pass (LIC-BETA + Terminal-as-host blocked for LAN host controls) |
| Host license as terminal proof | Pass (explicit reject) |
| Wrong StoreId pair proof | Pass (when proof presented) |
| Terminal count exceeded | Pass (approve/activate use licensed max) |
| Cloud required | No |
| LAN production-ready overclaim | No (UI non-claim retained) |
| salryn.info@gmail.com visible | Yes |
| Private keys absent | Yes |
| PRINTER1 / hardware / RMO / eSales untouched | Yes (no product code in those lanes) |

---

## Remaining gaps

1. **Mandatory terminal license proof at activation:** optional field; remote client does not yet always send TerminalLicenseJson. Host count still enforced without it.
2. **Terminal machine binding on host:** machine match remains a terminal-device local check; host proof path does not re-bind to terminal hardware identity.
3. **Pending join request store is in-memory** — not durable across host restart (pre-existing).
4. **Product endpoint ApiLicenseGuard** still not broadly wired (pre-existing commercial gap).
5. **Session max** still primarily capacity policy; entitlement zeroes sessions in capacity status when not entitled.

---

## Safety boundaries

**Allowed claims:** beta manual LAN entitlement model; host license carries terminal count; terminal license is separate; operational pairing gated by entitlement.

**Forbidden claims:** cloud activation ready; LAN broadly production-ready; installer ready; BIR/eSales ready; hardware support; terminal support without separate license.

---

## Files changed (Phase B)

- `src/Salryn.Core/Services/Licensing/LocalLanTerminalEntitlementContracts.cs` (new)
- `src/Salryn.Infrastructure/Licensing/LocalLanTerminalEntitlementService.cs` (new)
- `src/Salryn.Api.Local/RemotePos/RemotePosPairingGateService.cs`
- `src/Salryn.Api.Local/RemotePos/RemotePosApiServices.cs`
- `src/Salryn.Api.Local/RemotePos/RemotePosCapacityPolicy.cs`
- `src/Salryn.Api.Local/RemotePos/RemotePosPairingContracts.cs`
- `src/Salryn.Api.Local/LocalLanControlEndpoints.cs`
- `src/Salryn.App.Web/src/pages/HostLanControlPage.tsx`
- `src/Salryn.App.Web/src/types/remoteReadOnlyClient.ts`
- `tests/Salryn.Tests/LocalLicensingLanTerminalEntitlementEnforcementChecks.cs` (new)
- `tools/Salryn.Tools.CoreSmoke/Program.cs`
- `00-HANDOFF/LIC_LAN_TERMINAL_ENTITLEMENT_ENFORCEMENT_REPORT.md` (this file)

---

## Gates

| Gate | Result |
|------|--------|
| build | PASS |
| WEB-A3 | PASS |
| R3 | PASS |
| LIC-M3 | PASS |
| LIC-BETA | PASS |
| B1 | PASS |
| LIC-LAN | PASS |
| LAN-M16 identity | PASS (via full CoreSmoke) |
| LAN-M16 boundary | PASS (via full CoreSmoke) |
| USER-M1 | PASS |
| PRINTER1 | PASS |
| SURFACE1 | PASS |
| full CoreSmoke | PASS |
| web build | PASS |
| git diff --check | PASS (at commit) |

## Final status

`CANDIDATE_AUTOMATED_PROVEN`
