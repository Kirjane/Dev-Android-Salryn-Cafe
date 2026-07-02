# LAN-M14-B Explicit Host Bind Readiness and Guarded Runtime Cutline

## Source Stamp

- Repo: `Kirch-Nairu/Salryn`
- Stable branch: `KIRCH-SANDBOX`
- Candidate branch: `KIRCH-FREEHAND`
- `KIRCH-SANDBOX` SHA: `d8f2a8b346c3128336a14899567369278b801c75`
- `KIRCH-FREEHAND` SHA before LAN-M14-B: `d8f2a8b346c3128336a14899567369278b801c75`
- Ahead/behind before LAN-M14-B: `0 ahead / 0 behind`
- Date/context: LAN-M14-A stable; this is the first host-bind readiness cutline after the post-promotion LAN scope audit.
- Status: LAN-M14-A stable; LAN-M14-B candidate; LAN implementation still unstarted.

## Stable Stack

Stable on Sandbox as of `d8f2a8b346c3128336a14899567369278b801c75`:

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

## LAN-M14-B Purpose

LAN-M14-B defines explicit host-bind readiness and guarded runtime cutline.

It does not enable LAN bind.
It does not make Api.Local listen on LAN.
It does not enable live remote dispatch.
It does not implement remote client runtime behavior.
It does not implement mutation.

LAN-M14-B may add docs and guards only. It may not alter production runtime behavior.

## Current Host-Bind Baseline

Inspected source areas:

- `src/Salryn.Api.Local/LocalApiApplicationFactory.cs`
- `src/Salryn.Api.Local/Program.cs`
- `src/Salryn.Api.Local/Services/RemoteReadOnlyApiService.cs`
- `src/Salryn.Api.Local/RemoteReadOnlyApiEndpoints.cs`
- `src/Salryn.Desktop.WebView/DevHostConfig.cs`
- `src/Salryn.Desktop.WebView/LocalWebOriginPolicy.cs`
- `src/Salryn.Host.Local/HostCommands/SalrynHostCommandRemoteTransport.cs`
- Existing `LanRemoteTransportConfig` and `LanRemoteTransportGate` source references
- `tools/Salryn.Tools.InfrastructureSmoke/ControlledRemoteTransportGateSmokeChecks.cs`
- `tools/Salryn.Tools.InfrastructureSmoke/PairingActivationHandshakeSmokeChecks.cs`
- `tools/Salryn.Tools.InfrastructureSmoke/RemoteReadOnlyLanUsabilitySmokeChecks.cs`

Current source facts:

- Api.Local default URL remains `http://127.0.0.1:5299`.
- Api.Local is not LAN-bound by default.
- `Program.cs` uses `LocalApiApplicationFactory.CreateAsync(args)` and does not add LAN bind behavior.
- `RemoteReadOnlyApiService` bind policy reports `LocalhostOnly`.
- `RemoteReadOnlyApiService` reports `DisabledByDefault`.
- `RemoteReadOnlyApiService` reports `ExplicitHostBindRequired`.
- `RemoteReadOnlyApiService` reports `SpecificIpOnly`.
- `RemoteReadOnlyApiService` reports `NoWildcardBind`.
- `RemoteReadOnlyApiService` reports `NoDiscovery`.
- `RemoteReadOnlyApiEndpoints` exposes `/api/remote-readonly` through GET-only routes.
- WebView host is local-origin only.
- `DevHostConfig` only targets localhost and never exposes anything to LAN.
- `LocalWebOriginPolicy` allows localhost/127.0.0.1 origins only and blocks external, LAN, file, data, and javascript navigation.
- Host command remote transport already has gate and handshake foundations.
- Existing smokes already reject `0.0.0.0`.
- Existing smokes already allow explicit LAN address config as a saved config concept.
- Existing smokes already block official checkout/admin/unsafe remote commands.
- Existing smokes already allow safe read categories only after handshake.

## Ownership Question

Future LAN exposure is split across separate ownership areas:

1. Api.Local HTTP bind exposure
   - Owner: Api.Local host composition and future explicit host-bind configuration.
   - Boundary: remains localhost-only by default. Any future LAN bind must be disabled by default, specific-IP-only, never wildcard, and operator-controlled.

2. Host command remote transport dispatch
   - Owner: `SalrynHostCommandRemoteTransport` plus `ILanRemoteTransportGate` and handshake services.
   - Boundary: responsible for command category authorization, terminal state, remote origin, and handshake enforcement. It must not own Api.Local socket binding.

3. Remote-readonly Web client consumption
   - Owner: React/Web remote-readonly API client and page surface.
   - Boundary: consumes read-only HTTP surfaces only. It must not access SQLite, bypass Api.Local, or execute mutation.

4. Terminal pairing/handshake identity
   - Owner: terminal registry, pairing activation, credential, and handshake services.
   - Boundary: proves terminal identity before safe remote reads. It must not grant admin/cashier execution remotely.

5. Operator settings/control plane
   - Owner: future operator-approved settings/control surface.
   - Boundary: must expose local-only status and any future explicit LAN readiness setting, with audit trail and clear enable/disable status. It must not silently enable bind.

LAN-M14-C should not jump directly to live runtime activation. The safest architectural direction is a bridge pattern: Api.Local HTTP routes remain the local host read surface, host command transport remains the guarded dispatch boundary for remote command categories, and remote-readonly Web consumption remains a read-only client surface. The exact bridge implementation should be deferred until maintainer review.

## Required Future Host-Bind Safety Conditions

Before any future live bind could be implemented, all minimum safety conditions must be met:

- disabled by default
- explicit operator action required
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
- authenticated/paired terminal required before remote reads
- audit trail requirement for enable/disable decision
- clear operator status showing `LocalOnlyReadMode` vs configured explicit LAN readiness
- smoke coverage before any runtime enable

## LAN-M14-C Candidate Decision

Decision: Stop for maintainer review unless repo evidence clearly supports a narrow LAN-M14-C.

Rationale:

- Api.Local HTTP binding and host command remote transport are separate boundaries.
- Existing config and smoke coverage show explicit LAN address config as a controlled concept, but not live Api.Local LAN binding.
- A bridge pattern appears safest, but the next step should be selected by maintainer review from fresh verified LAN-M14-B results.
- LAN-M14-C should not be started automatically by LAN-M14-B.

Potential LAN-M14-C options for maintainer review:

- `LAN-M14-C` - Remote Read-Only Client Session Bridge Cutline
- `LAN-M14-C` - Host Bind Config Seam Implementation
- `LAN-M14-C` - Pairing UX Cutline
- Stop and keep LAN implementation unstarted

## Forbidden Scope

LAN-M14-B explicitly forbids:

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

- LAN-M14-B does not implement LAN.
- LAN-M14-B does not enable bind.
- LAN-M14-B does not make Api.Local listen on LAN.
- LAN-M14-B does not enable live remote dispatch.
- LAN-M14-B defines host-bind readiness and runtime ownership cutline.
- Codex must verify LAN-M14-B before any LAN-M14-C task.
- LAN implementation remains unstarted until a later explicitly approved sprint.

## Verification Required

Codex must verify LAN-M14-B locally before any LAN-M14-C task:

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
