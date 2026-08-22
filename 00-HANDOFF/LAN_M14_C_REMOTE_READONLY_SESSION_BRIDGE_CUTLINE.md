# LAN-M14-C Remote Read-Only Client Session Bridge Cutline

## Source Stamp

- Repo: `Kirch-Nairu/Salryn`
- Stable branch: `KIRCH-SANDBOX`
- Candidate branch: `KIRCH-FREEHAND`
- `KIRCH-SANDBOX` SHA: `05f2e40f7e21bf11fd85887cbb7a09d32f6d6ec8`
- `KIRCH-FREEHAND` SHA before LAN-M14-C: `05f2e40f7e21bf11fd85887cbb7a09d32f6d6ec8`
- Ahead/behind before LAN-M14-C: `0 ahead / 0 behind`
- Date/context: LAN-M14-B stable; this sprint defines the remote read-only client session bridge cutline after explicit host-bind readiness ownership was documented.
- Status: LAN-M14-B stable; LAN-M14-C candidate; LAN implementation still unstarted.

## Stable Stack

Stable on Sandbox as of `05f2e40f7e21bf11fd85887cbb7a09d32f6d6ec8`:

- `LAN-M13` - remote read-only LAN usability
- `ROLE-M1` - role-aware operator UX boundary
- `ROLE-M2` - backend permission enforcement and confidence baseline
- `ROLE-M3` - HTTP-level Api.Local permission runtime harness
- `ROLE-M4` - permission boundary regression matrix and operator role scenario coverage
- `M5` - current-state drift guard and verified candidate stack ledger
- `M6` - candidate stack verification replay and promotion-readiness dry run
- `M7` - candidate stack limit guard and promotion debt meter
- `M8` - M-Series Completion Criteria + LAN Transition Gate
- `LAN-M14-A` - Post-Promotion LAN Source Stamp + Completion Scope Audit / First Cutline
- `LAN-M14-B` - Explicit Host Bind Configuration Readiness + Guarded Runtime Cutline

## LAN-M14-C Purpose

LAN-M14-C defines the remote read-only client session bridge cutline.

It does not implement the bridge.
It does not enable LAN bind.
It does not make Api.Local listen on LAN.
It does not enable live remote dispatch.
It does not implement remote client runtime behavior.
It does not implement mutation.

LAN-M14-C may add docs and guards only. It may not alter production runtime behavior.

## Current Bridge Baseline

Inspected source areas:

- `00-HANDOFF/LAN_M14_A_POST_PROMOTION_SCOPE_AUDIT_AND_FIRST_CUTLINE.md`
- `00-HANDOFF/LAN_M14_B_EXPLICIT_HOST_BIND_READINESS_AND_GUARDED_RUNTIME_CUTLINE.md`
- `src/Salryn.Api.Local/LocalApiApplicationFactory.cs`
- `src/Salryn.Api.Local/Program.cs`
- `src/Salryn.Api.Local/Services/RemoteReadOnlyApiService.cs`
- `src/Salryn.Api.Local/RemoteReadOnlyApiEndpoints.cs`
- `src/Salryn.App.Web/src/api/remoteReadOnlyClient.ts`
- `src/Salryn.App.Web/src/hooks/useRemoteReadOnlyClient.ts`
- `src/Salryn.App.Web/src/pages/RemoteReadOnlyClientPage.tsx`
- `src/Salryn.Desktop.WebView/DevHostConfig.cs`
- `src/Salryn.Desktop.WebView/LocalWebOriginPolicy.cs`
- `src/Salryn.Host.Local/HostCommands/SalrynHostCommandRemoteTransport.cs`
- Existing terminal pairing/activation/handshake services
- Existing `LanRemoteTransportConfig` and `LanRemoteTransportGate` source references
- `tools/Salryn.Tools.InfrastructureSmoke/ControlledRemoteTransportGateSmokeChecks.cs`
- `tools/Salryn.Tools.InfrastructureSmoke/PairingActivationHandshakeSmokeChecks.cs`
- `tools/Salryn.Tools.InfrastructureSmoke/RemoteReadOnlyLanUsabilitySmokeChecks.cs`

Current source facts:

- Api.Local default URL remains `http://127.0.0.1:5299`.
- Api.Local is not LAN-bound by default.
- Remote-readonly API currently serves localhost-authenticated read-only endpoints.
- Remote-readonly Web client currently consumes `/api/remote-readonly` through same-origin fetch.
- Remote-readonly Web hook currently uses read-only query calls only.
- Remote-readonly Web page exposes status, bind policy, terminal identity, product browse, dashboard, and audit visibility surfaces without mutation controls.
- `RemoteReadOnlyApiService` still reports `LocalOnlyReadMode`.
- Bind policy remains localhost-only, disabled by default, explicit host bind required, specific-IP-only, no wildcard bind, no discovery.
- WebView host remains local-origin only.
- Host command remote transport has gate/handshake foundations but no live client bridge.
- Pairing activation/handshake foundations exist.
- Existing smokes allow safe status/product/dashboard/terminal reads after valid handshake.
- Existing smokes block official checkout/admin/unsafe remote commands.
- Existing smokes reject `0.0.0.0`.
- LAN-M14-C does not select a runtime implementation yet.

## Bridge Ownership Options

1. Api.Local HTTP bridge
   - Remote client talks to Api.Local HTTP routes after explicit bind and auth/session constraints.
   - Risks: exposing HTTP surface, cookies/session semantics, CORS/origin boundaries, and network binding.

2. Host command transport bridge
   - Remote client uses host command envelopes gated by terminal identity and handshake.
   - Risks: transport implementation ambiguity, command DTO mapping, and client protocol definition.

3. Hybrid bridge
   - Api.Local exposes a read-only remote-client facade, internally delegates to host transport/session gate.
   - Risks: duplicate authorization layers and unclear ownership unless strictly documented.

4. Stop / no bridge yet
   - Defer implementation until bind/session/audit ownership is fully verified.

Hybrid bridge appears safest conceptually because Api.Local can remain the HTTP facade while host transport and handshake rules remain the execution gate for remote read categories. However, no implementation is authorized until Codex verifies LAN-M14-C and the maintainer selects LAN-M14-D.

## Required Future Bridge Safety Conditions

Before any future bridge implementation, all minimum safety conditions must be met:

- LAN bind still disabled by default
- explicit operator enable required
- specific IP only
- no `0.0.0.0`
- no wildcard bind
- no discovery
- no QR unless separately approved
- no mobile/cloud
- no DB path exposure
- no shared SQLite
- no mutation
- read-only category whitelist
- authenticated session or paired terminal required before remote reads
- terminal identity and handshake required for remote client reads
- no cookie/session ambiguity across LAN clients without explicit design
- no CORS relaxation unless explicitly justified and guarded
- no official journal writes
- no official sequence allocation
- audit trail for bridge enable/disable and remote read attempts
- operator-visible status showing `LocalOnlyReadMode` vs configured explicit LAN readiness vs bridge readiness
- smoke coverage before runtime enable

## LAN-M14-D Recommendation

Decision: Stop for maintainer review unless repo evidence clearly supports a narrow LAN-M14-D.

Rationale:

- LAN-M14-C defines bridge ownership only; it does not implement a bridge.
- Api.Local HTTP routes, host command transport, Web client consumption, terminal identity, and operator control plane remain separate ownership areas.
- Hybrid bridge appears safest conceptually, but implementation requires a stricter session contract and audit/read-attempt policy first.
- LAN-M14-D should not be started automatically by LAN-M14-C.

Potential LAN-M14-D options for maintainer review:

- `LAN-M14-D` - Bridge Session Contract Study
- `LAN-M14-D` - Host Bind Config Seam Implementation
- `LAN-M14-D` - Pairing UX Cutline
- Stop and keep LAN implementation unstarted

## Forbidden Scope

LAN-M14-C explicitly forbids:

- implementing the bridge
- enabling Api.Local LAN bind
- making Api.Local listen on `0.0.0.0`
- runtime host bind activation
- live remote transport dispatch
- remote cashier/admin execution
- remote checkout
- sales/inventory/product/backup mutation
- official journal writes
- official sequence allocation
- shared SQLite
- SQLite path exposure
- network discovery
- QR pairing unless separately approved later
- mobile/cloud
- RMO/printer/PDF/Crystal/ESC-POS
- certification/accreditation/legal compliance claims

## Decision

- LAN-M14-C does not implement LAN.
- LAN-M14-C does not enable bind.
- LAN-M14-C does not implement the bridge.
- LAN-M14-C does not make Api.Local listen on LAN.
- LAN-M14-C does not enable live remote dispatch.
- LAN-M14-C defines bridge ownership and session safety cutline.
- Codex must verify LAN-M14-C before any LAN-M14-D task.
- LAN implementation remains unstarted until a later explicitly approved sprint.

## Verification Required

Codex must verify LAN-M14-C locally before any LAN-M14-D task:

```powershell
git fetch origin KIRCH-SANDBOX KIRCH-FREEHAND --prune
git switch KIRCH-FREEHAND
git pull --ff-only origin KIRCH-FREEHAND
git status --short
git rev-parse HEAD
git rev-parse origin/KIRCH-SANDBOX
git rev-parse origin/KIRCH-FREEHAND
git rev-list --left-right --count origin/KIRCH-SANDBOX...origin/KIRCH-FREEHAND
git diff --check

dotnet build .\Salryn.sln --no-restore
dotnet test .\Salryn.sln --no-build

dotnet run --no-build --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj

dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- controlled-remote-transport-gate
dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- pairing-activation-handshake
dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- safe-remote-readonly-client
dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- remote-readonly-web-shell
dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- remote-readonly-lan-usability

cd .\src\Salryn.App.Web
npm install
npm run build
```
