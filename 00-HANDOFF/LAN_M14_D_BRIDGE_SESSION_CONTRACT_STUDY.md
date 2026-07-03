# LAN-M14-D Bridge Session Contract Study

## Source Stamp

- Repo: `Kirch-Nairu/Salryn`
- Stable branch: `KIRCH-SANDBOX`
- Candidate branch: `KIRCH-FREEHAND`
- `KIRCH-SANDBOX` SHA: `9aba4e2c5acda61a0d6f88e3606eb9331a8657ab`
- `KIRCH-FREEHAND` SHA: `9aba4e2c5acda61a0d6f88e3606eb9331a8657ab`
- Ahead/behind: `0 ahead / 0 behind`
- Date/context: LAN-M14-C stable; this sprint defines the bridge session contract after remote read-only bridge ownership was cutlined.
- Status: LAN-M14-C stable; LAN-M14-D candidate; bridge implementation still unstarted; LAN implementation still unstarted.

## Stable Stack

Stable on Sandbox as of `9aba4e2c5acda61a0d6f88e3606eb9331a8657ab`:

- `LAN-M13` — remote read-only LAN usability
- `ROLE-M1` — role-aware operator UX boundary
- `ROLE-M2` — backend permission enforcement and confidence baseline
- `ROLE-M3` — HTTP-level Api.Local permission runtime harness
- `ROLE-M4` — permission boundary regression matrix and operator role scenario coverage
- `M5` — current-state drift guard and verified candidate stack ledger
- `M6` — candidate stack verification replay and promotion-readiness dry run
- `M7` — candidate stack limit guard and promotion debt meter
- `M8` — M-Series Completion Criteria + LAN Transition Gate
- `LAN-M14-A` — Post-Promotion LAN Source Stamp + Completion Scope Audit / First Cutline
- `LAN-M14-B` — Explicit Host Bind Configuration Readiness + Guarded Runtime Cutline
- `LAN-M14-C` — Remote Read-Only Client Session Bridge Cutline

## LAN-M14-D Purpose

LAN-M14-D defines the future bridge session contract.

It does not implement the bridge.
It does not implement session runtime behavior.
It does not enable LAN bind.
It does not make Api.Local listen on LAN.
It does not enable live remote dispatch.
It does not implement remote client runtime behavior.
It does not implement mutation.

LAN-M14-D may add docs and guards only. It may not alter production runtime behavior.

## Current Contract Baseline

Inspected source areas:

- `00-HANDOFF/LAN_M14_A_POST_PROMOTION_SCOPE_AUDIT_AND_FIRST_CUTLINE.md`
- `00-HANDOFF/LAN_M14_B_EXPLICIT_HOST_BIND_READINESS_AND_GUARDED_RUNTIME_CUTLINE.md`
- `00-HANDOFF/LAN_M14_C_REMOTE_READONLY_SESSION_BRIDGE_CUTLINE.md`
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
- `tools/Salryn.Tools.InfrastructureSmoke/ControlledRemoteTransportGateSmokeChecks.cs`
- `tools/Salryn.Tools.InfrastructureSmoke/PairingActivationHandshakeSmokeChecks.cs`
- `tools/Salryn.Tools.InfrastructureSmoke/RemoteReadOnlyLanUsabilitySmokeChecks.cs`

Current source facts:

- Api.Local default URL remains `http://127.0.0.1:5299`.
- Api.Local is not LAN-bound by default.
- `Program.cs` uses `LocalApiApplicationFactory.CreateAsync(args)` and does not add LAN bind behavior.
- Remote-readonly API currently serves localhost-authenticated read-only endpoints under `/api/remote-readonly`.
- Remote-readonly endpoints are GET-only: status, capabilities, dashboard summary, products, terminal status, bind policy, and audit visibility.
- Remote-readonly Web client currently consumes `/api/remote-readonly` through same-origin fetch with `credentials: "include"`.
- Remote-readonly Web hook uses `useQuery` read-only queries only; no `useMutation`.
- Remote-readonly Web page exposes status, bind policy, terminal identity, product browse, dashboard, and audit visibility without mutation controls.
- `RemoteReadOnlyApiService` still reports `LocalOnlyReadMode`.
- `RemoteReadOnlyApiService` bind policy reports `LocalhostOnly`, `DisabledByDefault`, `ExplicitHostBindRequired`, `SpecificIpOnly`, `NoWildcardBind`, and `NoDiscovery`.
- WebView host remains local-origin only.
- Host command remote transport has gate/handshake foundations via `ILanRemoteTransportGate` and `IRemoteClientHandshakeService` but no live client bridge.
- Pairing activation/handshake foundations exist through terminal registry, pairing activation, and remote handshake services.
- Existing smokes allow safe status/product/dashboard/terminal reads after valid handshake.
- Existing smokes block official checkout/admin/unsafe remote commands.
- Existing smokes reject `0.0.0.0`.
- LAN-M14-D does not select or implement a runtime bridge.

## Session Contract Ownership

Future bridge-session ownership is split across these areas. LAN-M14-D defines ownership only; no runtime implementation is authorized.

### 1. Operator enablement state

- Owns whether LAN/bridge readiness is disabled, configured, or explicitly enabled.
- Must remain disabled by default.
- Operator action is required before any future LAN bind or bridge readiness is considered active.
- Enable/disable decisions must be auditable before runtime activation.

### 2. Terminal identity

- Owns remote terminal identity and terminal state.
- Must distinguish: unknown, pending, active, disabled, revoked.
- Terminal identity is separate from localhost operator session identity.
- Revoked or disabled terminals must fail closed for remote reads.

### 3. Pairing activation

- Owns one-time activation and terminal credential issuance.
- Must not expose raw stored credentials to clients or logs.
- Activation is a deliberate operator-controlled step, not automatic discovery.
- Pairing issuance and revocation remain separate from bridge read execution.

### 4. Handshake validation

- Owns proof that a remote caller is an active paired terminal.
- Must fail closed when handshake is missing, expired, revoked, disabled, or unknown.
- Handshake proof is required before any future bridge read session is established.
- Localhost operator cookies or browser sessions must not substitute for handshake proof.

### 5. Api.Local browser/session state

- Owns localhost operator auth/session only.
- Authenticated localhost sessions gate current `/api/remote-readonly` reads for same-origin Web shell use.
- Must not be silently reused as LAN remote-terminal trust.
- Operator session expiry, role, and permission boundaries remain localhost-scoped.

### 6. Bridge read session

- Future concept only.
- Must require explicit source terminal identity, handshake proof, read-only category, and audit/read-attempt metadata before any remote read is honored.
- Bridge read session is ephemeral and category-scoped; it is not a general-purpose remote operator session.
- No bridge read session exists in current source.

### 7. Audit/read-attempt policy

- Owns enable/disable decision audit and remote read-attempt visibility.
- Must not write official journal or allocate official sequence.
- Audit visibility for remote read attempts is distinct from official document lifecycle audit.
- Policy must be defined before bridge runtime enable; missing audit policy is a denial condition.

## Required Session Contract Fields

The following is a future conceptual contract for bridge read sessions. This is conceptual only. Do not add code DTOs or runtime models in LAN-M14-D.

| Field | Purpose |
| --- | --- |
| `SessionId` | Unique bridge read session identifier for correlation |
| `TerminalId` | Remote terminal identity |
| `TerminalName` or `TerminalLabel` | Human-readable terminal label |
| `TerminalState` | unknown / pending / active / disabled / revoked |
| `RemoteOrigin` | Declared remote caller origin for policy checks |
| `ConfiguredBindAddress` | Effective explicit bind address, if any |
| `RequestedCategory` | Safe read category being requested |
| `RequestedOperation` | Specific read operation within category |
| `HandshakeSessionId` | Linked handshake proof identifier |
| `IssuedAt` | Session issuance timestamp |
| `ExpiresAt` | Session expiry timestamp |
| `LastSeenAt` | Last successful validation timestamp |
| `ReadOnlyOnly` | Must be true for all bridge read sessions |
| `MutationAllowed` | Fixed false |
| `OfficialWriteAllowed` | Fixed false |
| `OfficialSequenceAllowed` | Fixed false |
| `AuditCorrelationId` | Correlates enable/disable and read-attempt audit entries |
| `FailureReason` | Populated on denial for operator/debug visibility |

This contract is documentation-only. Implementation sprints must not assume these fields exist in runtime until explicitly approved and guarded.

## Allowed Future Read Categories

Only these read categories may be considered for future bridge read sessions:

- status
- product lookup/product browse
- dashboard summary
- terminal status
- bind policy/status visibility
- audit visibility if permission/session policy is explicitly defined

All allowed categories remain read-only. Permission gating may still apply per category.

## Forbidden Future Categories

Future bridge sessions must never authorize:

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

## Failure / Denial Contract

Future bridge session establishment and read attempts must fail closed with explicit denial states:

| Denial state | Meaning |
| --- | --- |
| remote disabled | LAN/remote transport not enabled by operator |
| bind not enabled | Explicit host bind not active |
| unknown terminal | Terminal identity not recognized |
| pending terminal | Terminal paired but not yet active |
| disabled terminal | Terminal administratively disabled |
| revoked terminal | Terminal credential revoked |
| expired handshake | Handshake proof past expiry |
| missing handshake | No valid handshake proof presented |
| invalid origin | Remote origin does not match policy |
| unsafe category | Requested category outside whitelist |
| mutation requested | Any mutation intent detected |
| official write requested | Official journal or lifecycle write attempted |
| audit policy missing | Audit/read-attempt policy not configured |
| session ambiguity | Conflicting or ambiguous session/identity state |

Denials must not leak raw credentials, SQLite paths, or internal secrets.

## Cookie / CORS / Browser Session Boundary

- Localhost operator cookies/sessions must not become LAN remote trust.
- LAN remote terminal trust must require terminal identity and handshake proof.
- CORS must not be relaxed unless a later sprint adds explicit guard coverage.
- Same-origin fetch remains current behavior for the remote-readonly Web shell.
- No browser session bridge is implemented in LAN-M14-D.
- Future LAN clients must not inherit Api.Local operator session cookies as proof of remote terminal identity.

## LAN-M14-E Recommendation

Decision: **Stop for maintainer review.**

Rationale:

- LAN-M14-D defines the bridge session contract and denial boundary only.
- Audit/read-attempt policy ownership is named but not yet studied in implementation depth.
- Host bind config seam, pairing UX, and bridge runtime remain separate unstarted workstreams.
- LAN-M14-C concluded hybrid bridge appears safest conceptually, but no runtime bridge is authorized until session contract, audit policy, and maintainer direction align.

Potential LAN-M14-E options for maintainer review:

- `LAN-M14-E` — Audit and Read-Attempt Policy Study
- `LAN-M14-E` — Host Bind Config Seam Implementation
- `LAN-M14-E` — Pairing UX Cutline
- Stop and keep LAN implementation unstarted

Codex must verify LAN-M14-D before any LAN-M14-E task.

## Forbidden Scope

LAN-M14-D explicitly forbids:

- implementing the bridge
- adding session runtime models
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
- RMO/printer/PDF/Crystal/ESC/POS
- certification/accreditation/legal compliance claims

## Decision

- LAN-M14-D does not implement LAN.
- LAN-M14-D does not enable bind.
- LAN-M14-D does not implement the bridge.
- LAN-M14-D does not implement a session runtime.
- LAN-M14-D defines the bridge session contract and denial boundary only.
- Codex must verify before any LAN-M14-E task.
- LAN implementation remains unstarted until a later explicitly approved sprint.

## Verification Required

Codex must verify LAN-M14-D locally before any LAN-M14-E task:

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
