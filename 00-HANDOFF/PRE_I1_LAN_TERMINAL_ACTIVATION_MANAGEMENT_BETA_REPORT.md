# PRE-I1 LAN Terminal Activation + Terminal Management Beta Report

## 1. Branch, base, and final implementation state

- Branch: `KIRCH-PRE-I1-LAN-TERMINAL-ACTIVATION-MANAGEMENT-BETA`
- Required base: `68be8cbf62c907b8031e29d02f307304149a480c`
- Implementation commits:
  - `cb39a48` - licensed LAN terminal activation flow
  - `67eecfc` - durable terminal trust and client-facing restart UX repair
- Final implementation SHA before this report-only commit: `67eecfc`
- The report commit is the final branch tip. Its immutable SHA is recorded in the delivery evidence because a commit cannot contain its own SHA.

## 2. Study performed

Studied the existing Host license activation, LicensingOps Host/Terminal issuance contract, Local LAN entitlement enforcement, terminal registry and handshake persistence, Remote POS pairing/session flow, Host LAN controls, client navigation, role permissions, and existing CoreSmoke coverage before changing product code.

LicensingOps was verified read-only at branch `KIRCH-LICOPS-ISSUANCE-CONTRACT-HARDENING`, SHA `10be2b3e03604ffb32f5e312f6569c38eacf486c`. Its normal issuance UI already states that Host licenses carry no commercial terminal count and each LAN terminal requires its own Terminal license.

## 3. Files changed

API and Host enforcement:

- `src/Salryn.Api.Local/LocalLanControlEndpoints.cs`
- `src/Salryn.Api.Local/RemotePos/RemotePosApiEndpoints.cs`
- `src/Salryn.Api.Local/RemotePos/RemotePosCapacityPolicy.cs`
- `src/Salryn.Api.Local/RemotePos/RemotePosOperatorSessionService.cs`
- `src/Salryn.Api.Local/RemotePos/RemotePosPairingContracts.cs`
- `src/Salryn.Api.Local/RemotePos/RemotePosPairingGateService.cs`
- `src/Salryn.Api.Local/RemotePos/RemotePosTerminalTrustService.cs`

Web client:

- `src/Salryn.App.Web/src/App.tsx`
- `src/Salryn.App.Web/src/api/remotePosClient.ts`
- `src/Salryn.App.Web/src/hooks/useRemotePosClient.ts`
- `src/Salryn.App.Web/src/navigation.ts`
- `src/Salryn.App.Web/src/pages/HostLanControlPage.tsx`
- `src/Salryn.App.Web/src/pages/RemoteClientLandingPage.tsx`
- `src/Salryn.App.Web/src/pages/RemotePosClientPage.tsx`
- `src/Salryn.App.Web/src/pages/TerminalActivationPage.tsx`
- `src/Salryn.App.Web/src/types/remotePosClient.ts`
- `src/Salryn.App.Web/src/types/remoteReadOnlyClient.ts`
- `src/Salryn.App.Web/src/utils/terminalLicenseStorage.ts`

Core and infrastructure:

- `src/Salryn.Core/Services/Licensing/LocalLanTerminalEntitlementContracts.cs`
- `src/Salryn.Core/TerminalRegistry/TerminalPairingActivationHandshakeModels.cs`
- `src/Salryn.Infrastructure/Data/Services/TerminalPairingActivationService.cs`
- `src/Salryn.Infrastructure/Licensing/LocalLanTerminalEntitlementService.cs`
- `src/Salryn.Infrastructure/Licensing/LocalLicenseEnforcementService.cs`
- `src/Salryn.Infrastructure/Licensing/LocalLicenseValidator.cs`

Verification:

- `tests/Salryn.Tests/LanM16HostClientIdentityUxChecks.cs`
- `tests/Salryn.Tests/LanM16RcBEntryBoundaryChecks.cs`
- `tests/Salryn.Tests/LanM8ReactRemoteReadOnlyShellChecks.cs`
- `tests/Salryn.Tests/LanM8ReactRemoteReadOnlyShellRuntimeChecks.cs`
- `tests/Salryn.Tests/LanM9M13RemoteReadOnlyLanUsabilityChecks.cs`
- `tests/Salryn.Tests/LocalLicensingCloseoutHardGatesAndTerminalProofChecks.cs`
- `tests/Salryn.Tests/LocalLicensingLanTerminalEntitlementEnforcementChecks.cs`
- `tests/Salryn.Tests/PreI1LanTerminalActivationManagementBetaChecks.cs`
- `tools/Salryn.Tools.CoreSmoke/Program.cs`
- `00-HANDOFF/PRE_I1_LAN_TERMINAL_ACTIVATION_MANAGEMENT_BETA_REPORT.md`

## 4. Host Terminal Management behavior

Terminal Management is a normal Admin navigation item. It shows client-facing LAN readiness, the explicit client URL, Remote POS runtime control, pairing approvals, active/blocked/revoked terminal rows, lifecycle actions, and capacity safety status. Raw bind normalization, listener URLs, warnings, and technical policy state are confined to collapsed **Advanced diagnostics**. Report Artifacts and internal Local API surfaces are absent from normal navigation.

Host/Admin authorization remains enforced server-side. The localhost Host shell is distinct from the LAN terminal shell.

## 5. Terminal Activation behavior

The remote client is activation-first. With no installed terminal license it shows a stable terminal-origin machine code, beta issuance contact, import control, and clear status. It accepts only a signed `Terminal` license matching the current machine, Host license ID, Store ID, and terminal identity. The installed license and stable machine code use versioned browser-origin persistence. Pairing is unavailable until Host validation succeeds.

## 6. Role behavior

Remote operator authentication allows `Cashier` and `Manager` only. `Admin` and `Owner` are explicitly host-local. The Host validates credentials, role, active user state, terminal trust, handshake, observed origin, Store/Host identity, and current terminal-license proof before login and every command.

## 7. Cashier proof

Created and signed in with a Cashier account from the paired terminal. The client showed `Role: Cashier` and only the Remote POS shift/cart/cash-checkout workflow. Terminal Management, Users, Reports, and the Host navigation shell were absent. A browser refresh preserved pairing and restored the still-valid Host-issued operator token after Host validation.

## 8. Manager proof and current boundary

Created and signed in with a Manager account from the paired terminal. The client showed `Role: Manager` and the same safe shift/cart/cash-checkout workflow. Host administration, Users, Reports, and Terminal Management were absent. Expanded manager-only remote operations are not introduced in this lane; unsupported manager/admin command categories remain fail-closed.

## 9. Admin block proof

The paired terminal rejected the valid Admin account with: `Remote POS login is limited to Cashier or Manager accounts. Admin and Owner accounts are host-local only for this build.` No remote Host shell was entered.

## 10. Commercial license model

The Host license authorizes the Host and makes Terminal Management available. Each client must independently present one valid signed Terminal license. The Terminal license proof supplies commercial identity and binding: license ID, Store ID, Host license ID, Terminal ID, terminal machine code, type, signature, and optional expiry. The Host persists this verified proof with the trusted terminal and enforces it on login and commands.

A non-revoked licensed Terminal ID cannot be paired twice. Revocation releases that license identity for a new pairing. Hard safety caps remain operational abuse limits, not paid-seat truth.

## 11. `AllowedLanTerminals` conflict resolution

`AllowedLanTerminals` is removed from client-facing Host capacity DTOs, UI copy, and commercial decisions. Host entitlement no longer reads it to grant or restrict Terminal licenses. LicensingOps hardened Host JSON still writes `AllowedLanTerminals: 0` as reserved schema compatibility; it is not exposed to the operator and is not the source of truth.

## 12. Terminal license persistence proof

The valid Terminal license remained installed across terminal page refreshes, navigation back to the remote-client landing page, pairing/re-pairing, and Host API restarts. The same stable terminal machine code was used throughout the no-reset persistence checks.

## 13. Host license persistence proof

A freshly issued Host license was imported with development bypass variables cleared. It survived multiple API restarts without re-import. The Host returned to authenticated login, retained deployment identity, Store ID, Host license ID, explicit LAN configuration, users, and terminal registry state.

## 14. Pairing and restart proof

The terminal requested access using the Host join code; Terminal Management showed the pending device and server-observed origin; the Admin accepted it; the client consumed the one-time pairing code; and the Host created an active licensed terminal plus handshake.

Manual restart testing found and repaired a defect where an intentionally stopped Remote POS runtime was misclassified as revoked. Final behavior is:

1. API restart resets the fail-safe Remote POS runtime to Off.
2. The terminal retains its installed license and trusted pairing.
3. The client shows **Remote POS is paused on the Host** rather than clearing trust.
4. Admin re-enables Remote POS in Terminal Management.
5. **Retry Host connection** refreshes the persisted terminal handshake without a new pairing code.
6. In-memory operator sessions require login again after restart, as designed.

## 15. Revoke and disable proof

- Revoke moved the old active terminal to **Retired / revoked terminals**, reduced the active paired count, and allowed the paid Terminal license identity to be paired again only after revocation.
- Disable moved the current active terminal to **Blocked terminals**. On its next trust probe the client could not restore Remote POS and returned to request-access state. Re-enable is intentionally unsupported in this build; the Host guidance directs the operator to revoke/re-pair when needed.
- Client wording distinguishes Disabled from Revoked in the final source.

## 16. Wrong-license rejection proof

Manual signed-license checks proved all required refusal paths:

- Host license pasted as Terminal license: rejected because a Host license cannot activate a terminal.
- Terminal license issued for another machine: rejected as wrong device.
- Terminal license with a different Host license ID: rejected by the Host.
- Terminal license with a different Store ID: rejected by the Host.
- Correct Terminal license: validated, persisted, and allowed pairing.

## 17. Automated gates

Final post-implementation verification:

- `dotnet build Salryn.sln`: PASS, 0 errors (existing nullable/analyzer warnings only).
- `pre-i1-lan-terminal-activation-management-beta`: PASS.
- `web-a3-shell-design-system`: PASS.
- `web-a4-native-host-visual-fidelity`: PASS.
- `lan-m16-remote-pos`: PASS.
- `v2-lan-client-pairing`: PASS.
- `v2-terminal-lifecycle`: PASS.
- `v2-host-lan-control-ux`: PASS.
- Full CoreSmoke: PASS (`SALRYN Core smoke checks passed.`).
- `npm --prefix src/Salryn.App.Web run build`: PASS; final JS asset `index-BhS-Y2ql.js`.
- `git diff --check`: PASS.

Required focused gates also passed during the implementation run and are covered again by the final full CoreSmoke:

- `lic-closeout-hard-gates-and-terminal-proof`
- `lic-beta-manual-host-terminal-activation`
- `lic-lan-terminal-entitlement-enforcement`
- `pre-i1-fresh-license-first-flow`
- `pos-b1-backup-restore-safety`
- `web-b0-read-only-operator-surfaces`

## 18. Manual app proof

Performed with development license bypass cleared, a freshly archived/reset `.salryn-dev` start, a freshly issued Host license, and a separately archived prior Host license file. Confirmed activation-first Host launch, bootstrap, Admin sign-in, clean normal navigation, explicit LAN URL, Host/Terminal issuance in LicensingOps, activation-first terminal UX, license import/refusals, pairing approval, Cashier/Manager/Admin role behavior, refresh behavior, multiple API restarts, handshake recovery, Revoke, and Disable.

The terminal used `http://192.168.254.130:5299/remote-client` in a separate in-app browser context on the same Windows machine. This proves separate-origin browser behavior but is not physical two-device network proof.

## 19. Remaining LAN / Terminal risks

- Physical second-device proof remains pending: real Wi-Fi/Ethernet routing, Windows Firewall prompts/rules, browser behavior on another OS/device, and actual device clock/storage behavior were not exercised.
- The LAN transport remains explicit-address HTTP for local beta; production-grade TLS or hostile-network deployment is outside this lane.
- Remote POS runtime is intentionally fail-safe Off after API restart and requires a Host Admin to re-enable it; pairing now survives that state correctly.
- Remote operator sessions remain memory-only and are invalidated by API restart.
- Manager currently receives the safe Remote POS workflow only; expanded manager actions remain deferred and fail-closed.
- Disable has no re-enable path in this build; recovery is revoke/re-pair.
- Operational hard caps remain 5 paired terminals and 3 active sessions as abuse/safety limits, independent of paid Terminal license count.

## 20. Decision

`LAN_TERMINAL_CODE_COMPLETE_DEVICE_PROOF_PENDING`

The code, automated gates, LicensingOps contract checks, and same-machine manual Host/terminal lifecycle proof are complete. LAN should not be called fully device-proven beta until a physical second terminal validates real network discovery/reachability, firewall behavior, activation, pairing, Cashier/Manager login, restart recovery, and disable/revoke enforcement.
