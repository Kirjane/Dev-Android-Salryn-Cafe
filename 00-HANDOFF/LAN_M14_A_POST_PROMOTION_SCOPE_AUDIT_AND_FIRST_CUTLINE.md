# LAN-M14-A Post-Promotion Scope Audit and First Cutline

## Source Stamp

- Repo: `Kirch-Nairu/Salryn`
- Stable branch: `KIRCH-SANDBOX`
- Candidate branch: `KIRCH-FREEHAND`
- `KIRCH-SANDBOX` SHA: `bde69e28faa4f92dd7b6765da9f45a0c5cf02056`
- `KIRCH-FREEHAND` SHA before LAN-M14-A: `bde69e28faa4f92dd7b6765da9f45a0c5cf02056`
- Ahead/behind before LAN-M14-A: `0 ahead / 0 behind`
- Date/context: post-M-series promotion source stamp; LAN completion planning starts from fresh aligned branch truth.
- Status: post-M-series promotion; LAN completion not started.

## Stable Stack

Stable on Sandbox after M-series promotion:

- `LAN-M13` - remote read-only LAN usability
- `ROLE-M1` - role-aware operator UX boundary
- `ROLE-M2` - backend permission enforcement and confidence baseline
- `ROLE-M3` - HTTP-level Api.Local permission runtime harness
- `ROLE-M4` - permission boundary regression matrix and operator role scenario coverage
- `M5` - current-state drift guard and verified candidate stack ledger
- `M6` - candidate stack verification replay and promotion-readiness dry run
- `M7` - candidate stack limit guard and promotion debt meter
- `M8` - M-Series Completion Criteria + LAN Transition Gate

## Current LAN Baseline Inventory

Current LAN-related repo state before LAN-M14 implementation:

- Api.Local remains localhost-bound by default.
- `LocalApiApplicationFactory` default URL is `http://127.0.0.1:5299`.
- Remote-readonly API exists under `/api/remote-readonly`.
- Remote-readonly endpoints are GET-only.
- Remote-readonly API covers status, capabilities, dashboard summary, products, terminal status, bind policy, and audit visibility.
- Products, terminal status, and audit visibility are permission-gated.
- `RemoteReadOnlyApiService` still reports `LocalOnlyReadMode`.
- Bind policy remains localhost-only, disabled by default, explicit host bind required, specific-IP-only, no wildcard bind, no discovery.
- React/Web remote-readonly page is read-only visibility only.
- WebView host allows localhost/127.0.0.1 only and blocks external/LAN/file/data/javascript navigation.
- Controlled remote transport gate foundation exists.
- Pairing activation and handshake foundation exists.
- Safe remote command categories are status/product/dashboard/terminal reads only.
- Official checkout, official lifecycle, Z finalize, admin mutation, pending/disabled/revoked/unknown terminals remain blocked by smoke coverage.

## Already-Safe Pieces

- Localhost Api.Local composition.
- Remote-readonly API shape.
- Permission guard coverage.
- Remote-readonly web shell.
- Bind policy status surface.
- Terminal status surface.
- Controlled remote transport gate.
- Pairing activation service.
- Remote handshake validation.
- Smoke coverage.

## Missing / Deferred Pieces

- No operator-approved live LAN bind workflow.
- No live LAN API host bind activation.
- No live WebView/LAN client pairing activation flow.
- No real remote-readonly LAN bridge from client device to host.
- No user-facing terminal pairing issue/activation UI for LAN clients.
- No LAN completion implementation.
- No LAN-M14-B chosen yet.

## Forbidden Scope

LAN-M14-A explicitly forbids:

- remote cashier/admin execution
- remote checkout
- sales/inventory/product/backup mutation
- official journal writes
- official sequence allocation
- shared SQLite
- SQLite path exposure
- `0.0.0.0` wildcard bind
- network discovery
- QR pairing unless separately approved later
- mobile/cloud
- RMO/printer/PDF/Crystal/ESC-POS
- certification/accreditation/legal compliance claims

## First Implementation Candidate Ranking

1. `LAN-M14-B` - Explicit Host Bind Configuration Readiness + Guarded Runtime Cutline
   - Purpose: define and guard the safe path for enabling a specific LAN host address later, without enabling it by default and without wildcard bind.
2. `LAN-M14-C` - Remote Read-Only Client Session Bridge Cutline
   - Purpose: define how a paired client would consume safe status/product/dashboard/terminal reads through the host without DB access and without mutation.
3. `LAN-M14-D` - Pairing / Terminal Activation Operator UX Cutline
   - Purpose: define operator workflow for issuing/revoking terminal pairing, still without QR/discovery/mobile/cloud unless explicitly approved.
4. `LAN-M14-E` - LAN Read-Only Runtime Smoke Expansion
   - Purpose: expand smoke coverage before exposing any LAN bind path.

Recommended first implementation: `LAN-M14-B`.

Reason: host bind configuration is the central gate. Without a precise bind/runtime cutline, session bridge or UX work risks implying LAN is live before the host boundary is safe.

## LAN-M14-B Proposed Scope

Study-only proposal for next sprint:

- inspect existing `LanRemoteTransportConfig` and Api.Local binding options
- define whether Api.Local or host command transport owns LAN exposure
- add docs/guards only or minimal config seam only if separately approved
- no default enable
- no wildcard bind
- no mutation
- no discovery
- no GUI launch

## Verification Required

Codex must verify LAN-M14-A locally before LAN-M14-B starts:

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

## Decision

- LAN-M14-A does not implement LAN.
- LAN-M14-A recommends `LAN-M14-B` as the next implementation-planning target.
- Codex must verify LAN-M14-A before LAN-M14-B.
- LAN implementation remains unstarted until a later explicitly approved sprint.
