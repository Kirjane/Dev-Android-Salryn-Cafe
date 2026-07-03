# LAN-M14-E Audit and Read-Attempt Policy Cutline

## Source Stamp

- Repo: `Kirch-Nairu/Salryn`
- Stable branch: `KIRCH-SANDBOX`
- Candidate branch: `KIRCH-FREEHAND`
- `KIRCH-SANDBOX` SHA: `85f48294883142d7488fb107403f977e9897882e`
- `KIRCH-FREEHAND` SHA: `85f48294883142d7488fb107403f977e9897882e`
- Ahead/behind: `0 ahead / 0 behind`
- Date/context: LAN-M14-D stable; this sprint defines the audit and read-attempt policy before any bridge/session runtime.
- Status: LAN-M14-D stable; LAN-M14-E candidate; audit/read-attempt runtime unstarted; bridge implementation unstarted; LAN implementation unstarted.

## Stable Stack

Stable on Sandbox as of `85f48294883142d7488fb107403f977e9897882e`:

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
- `LAN-M14-C` - Remote Read-Only Client Session Bridge Cutline
- `LAN-M14-D` - Bridge Session Contract Study

## LAN-M14-E Purpose

LAN-M14-E defines the audit and read-attempt policy cutline for future remote read-only LAN bridge work.

It does not implement audit/read-attempt runtime.
It does not implement the bridge.
It does not implement session runtime behavior.
It does not add runtime DTOs or models.
It does not enable LAN bind.
It does not make Api.Local listen on LAN.
It does not enable live remote dispatch.
It does not implement remote client runtime behavior.
It does not implement mutation.

LAN-M14-E may add docs and guards only. It may not alter production runtime behavior.

## Current Audit Baseline

Inspected source areas:

- `00-HANDOFF/LAN_M14_A_POST_PROMOTION_SCOPE_AUDIT_AND_FIRST_CUTLINE.md`
- `00-HANDOFF/LAN_M14_B_EXPLICIT_HOST_BIND_READINESS_AND_GUARDED_RUNTIME_CUTLINE.md`
- `00-HANDOFF/LAN_M14_C_REMOTE_READONLY_SESSION_BRIDGE_CUTLINE.md`
- `00-HANDOFF/LAN_M14_D_BRIDGE_SESSION_CONTRACT_STUDY.md`
- `src/Salryn.Api.Local/LocalApiApplicationFactory.cs`
- `src/Salryn.Api.Local/Program.cs`
- `src/Salryn.Api.Local/Services/RemoteReadOnlyApiService.cs`
- `src/Salryn.Api.Local/RemoteReadOnlyApiEndpoints.cs`
- `src/Salryn.App.Web/src/api/remoteReadOnlyClient.ts`
- `src/Salryn.App.Web/src/hooks/useRemoteReadOnlyClient.ts`
- `src/Salryn.App.Web/src/pages/RemoteReadOnlyClientPage.tsx`
- `src/Salryn.Host.Local/HostCommands/SalrynHostCommandRemoteTransport.cs`
- Existing terminal pairing/activation/handshake services
- Existing remote terminal credential/session services
- Existing `LanRemoteTransportConfig`-related files
- Existing `LanRemoteTransportGate`-related files
- Existing audit-log and official-document audit services for boundary separation only
- `tools/Salryn.Tools.InfrastructureSmoke/ControlledRemoteTransportGateSmokeChecks.cs`
- `tools/Salryn.Tools.InfrastructureSmoke/PairingActivationHandshakeSmokeChecks.cs`
- `tools/Salryn.Tools.InfrastructureSmoke/RemoteReadOnlyLanUsabilitySmokeChecks.cs`

Current source facts:

- Api.Local default URL remains `http://127.0.0.1:5299`.
- Api.Local is not LAN-bound by default.
- Remote-readonly API currently serves localhost-authenticated read-only endpoints under `/api/remote-readonly`.
- Remote-readonly endpoints are GET-only.
- Remote-readonly Web client currently consumes `/api/remote-readonly` through same-origin fetch.
- `RemoteReadOnlyApiService` still reports `LocalOnlyReadMode`.
- `RemoteReadOnlyApiService` bind policy reports `LocalhostOnly`, `DisabledByDefault`, `ExplicitHostBindRequired`, `SpecificIpOnly`, `NoWildcardBind`, and `NoDiscovery`.
- Host command remote transport has gate/handshake foundations but no live client bridge.
- Existing smokes allow safe status/product/dashboard/terminal reads after valid handshake.
- Existing smokes block official checkout/admin/unsafe remote commands.
- Existing smokes reject `0.0.0.0`.
- LAN-M14-E does not select or implement an audit runtime.

## Audit Policy Ownership

Future remote read-only LAN bridge audit ownership must be split cleanly:

### 1. Operator enable/disable audit

- Records explicit operator intent to enable, disable, or reconfigure future bridge readiness.
- Must include actor identity when available.
- Must not be produced by passive status reads.
- Must remain separate from official document lifecycle audit.

### 2. Remote read-attempt audit

- Records a future remote terminal read attempt after bridge runtime exists.
- Must capture allow/deny outcome, requested category, requested operation, terminal identity, origin, session correlation, and denial reason.
- Must not write official journal.
- Must not allocate official sequence.
- Must not mutate sales, inventory, product, backup, user/admin, settings, report, official output, printer, receipt, PDF, Crystal, or ESC-POS state.

### 3. Denial audit

- Records denied attempts without leaking secrets.
- Must not log raw terminal credentials, raw pairing codes, raw tokens, cookies, SQLite paths, or connection strings.
- Must preserve enough reason codes for operator diagnosis.

### 4. Retention and visibility

- Retention policy is a future configuration topic and is not implemented in LAN-M14-E.
- Operator visibility must be read-only.
- Export/package behavior is not implemented in LAN-M14-E.

### 5. Boundary separation

- Remote bridge read-attempt audit is distinct from official document audit.
- Remote bridge read-attempt audit is distinct from electronic journal.
- Remote bridge read-attempt audit is distinct from sales/void/cancel/refund lifecycle audit.
- Missing audit policy must remain a denial condition before runtime enable.

## Required Conceptual Audit Fields

The following fields are conceptual only. Do not add code DTOs or runtime models in LAN-M14-E.

| Field | Purpose |
| --- | --- |
| `AuditCorrelationId` | Correlates enable/disable, session, and read-attempt records |
| `AttemptId` | Unique remote read-attempt identifier |
| `BridgeSessionId` | Future bridge read session identifier |
| `TerminalId` | Remote terminal identity |
| `TerminalName` or `TerminalLabel` | Human-readable terminal label |
| `TerminalState` | unknown / pending / active / disabled / revoked |
| `RemoteOrigin` | Declared or observed remote caller origin |
| `ConfiguredBindAddress` | Explicit configured bind address, if any |
| `RequestedCategory` | Safe read category requested |
| `RequestedOperation` | Specific read operation requested |
| `Decision` | allowed / denied |
| `FailureReason` | Denial reason when denied |
| `ActorUserId` | Local operator actor when available |
| `ActorUserName` | Local operator display name when available |
| `OccurredAt` | Attempt or enablement timestamp |
| `ReadOnlyOnly` | Must be true |
| `MutationAllowed` | Fixed false |
| `OfficialWriteAllowed` | Fixed false |
| `OfficialSequenceAllowed` | Fixed false |

This contract is documentation-only. Future implementation must not assume these fields exist until explicitly approved and guarded.

## Allowed Audit Subjects

Future audit policy may cover only:

- bridge readiness enable/disable decisions
- explicit bind configuration visibility decisions
- remote read session creation decisions
- safe read attempts
- denied unsafe category attempts
- terminal state denial attempts
- handshake/session expiry denial attempts

## Forbidden Audit Subjects

Future audit policy must not authorize or imply:

- checkout
- sales mutation
- inventory mutation
- product mutation
- backup mutation
- user/admin mutation
- role/permission mutation
- official journal write
- official sequence allocation
- Z reading/finalize
- settings mutation
- reports/official output generation
- printer/receipt/PDF/Crystal/ESC-POS actions
- shared SQLite
- SQLite path exposure
- QR/discovery/mobile/cloud behavior

## Failure / Denial Audit Reasons

Future denial records should use explicit reason codes:

- remote disabled
- bind not enabled
- unknown terminal
- pending terminal
- disabled terminal
- revoked terminal
- expired handshake
- missing handshake
- invalid origin
- unsafe category
- mutation requested
- official write requested
- official sequence requested
- audit policy missing
- session ambiguity
- CORS not allowed
- cookie trust rejected

Denials must fail closed. Denials must not leak raw credentials, raw tokens, cookies, SQLite paths, connection strings, or internal secrets.

## Cookie / CORS / Secret Logging Boundary

- Localhost operator cookies/sessions must not become LAN remote trust.
- LAN remote terminal trust must require terminal identity and handshake proof.
- CORS must not be relaxed unless a later sprint adds explicit guard coverage.
- Cookies, raw credentials, raw pairing codes, raw tokens, SQLite paths, and connection strings must not be logged in future audit records.
- Same-origin fetch remains current behavior for the remote-readonly Web shell.
- No browser session bridge is implemented in LAN-M14-E.

## LAN-M14-F Recommendation

Decision: **Stop for maintainer review.**

Rationale:

- LAN-M14-E defines the audit/read-attempt policy and secret-redaction boundary only.
- Host bind config seam, pairing UX, bridge runtime, audit runtime, and session runtime remain separate unstarted workstreams.
- No runtime bridge is authorized until audit policy, session contract, host bind, and maintainer direction align.

Potential LAN-M14-F options for maintainer review:

- `LAN-M14-F` - Host Bind Config Seam Implementation
- `LAN-M14-F` - Pairing UX Cutline
- `LAN-M14-F` - Audit Runtime Storage Study
- Stop and keep LAN implementation unstarted

Codex must verify LAN-M14-E before any LAN-M14-F task.

## Forbidden Scope

LAN-M14-E explicitly forbids:

- implementing audit/read-attempt runtime
- implementing the bridge
- adding session runtime models
- adding audit runtime DTOs
- adding runtime bridge DTOs
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

- LAN-M14-E does not implement LAN.
- LAN-M14-E does not enable bind.
- LAN-M14-E does not implement the bridge.
- LAN-M14-E does not implement a session runtime.
- LAN-M14-E does not implement audit/read-attempt runtime.
- LAN-M14-E defines the audit/read-attempt policy and denial boundary only.
- Codex must verify before any LAN-M14-F task.
- LAN implementation remains unstarted until a later explicitly approved sprint.

## Verification Required

Codex must verify LAN-M14-E locally before any LAN-M14-F task:

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
