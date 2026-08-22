# LAN-M16 Final Acceptance And Backend Review Prep

Review date: 2026-07-06

## Source Stamp

- Repo: `Kirch-Nairu/Salryn`
- Local path: `C:\Users\kirch\Salryn`
- Branch reviewed: `KIRCH-FREEHAND`
- Local HEAD: `a6cf0945ebfe8b9728bf3895995a72d0474fc1b0`
- `origin/KIRCH-FREEHAND`: `a6cf0945ebfe8b9728bf3895995a72d0474fc1b0`
- `origin/KIRCH-SANDBOX`: `e477699dca27a21954a5ad9d388307fdb9ed08fd`
- Compare: `origin/KIRCH-SANDBOX...origin/KIRCH-FREEHAND` = `0 22`
- Worktree before report: clean
- Scope note: `KIRCH-SANDBOX` was not touched. No promotion was performed.

## Manual Acceptance Summary

Manual LAN-M16 acceptance was reported as passed before this review:

- Host/client UX split works.
- Host LAN Control works.
- Device pairing works.
- One-time pairing code works.
- Admin remote login is rejected.
- Cashier remote login is accepted.
- Manager remote login is accepted.
- User creation MVP works.
- Stale frontend bundle issue was resolved by `npm run build` plus API restart.

## Automated Verification Results

Executed on `KIRCH-FREEHAND` at `a6cf0945ebfe8b9728bf3895995a72d0474fc1b0`.

| Command | Result |
| --- | --- |
| `git diff --check` | Passed |
| `dotnet build .\Salryn.sln --no-restore` | Passed, 0 warnings, 0 errors |
| `dotnet test .\Salryn.sln --no-build` | Passed, exit 0 |
| `dotnet run --no-build --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj` | Passed |
| `dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- lan-m16-rc-b-entry-boundary` | Passed |
| `dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- remote-pos-pairing-gate` | Passed |
| `dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- remote-pos-final-acceptance` | Passed |
| `dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- remote-readonly-lan-local-control` | Passed |
| `npm install` in `src\Salryn.App.Web` | Passed, 0 vulnerabilities |
| `npm run build` in `src\Salryn.App.Web` | Passed |

## LAN Security Boundary Review

Verdict: no LAN-M16 final acceptance blocker found.

Evidence:

- Host-local auth login, logout, and session endpoints call `LocalApiLocalAccessGuard.RequireLoopbackOperatorRequest` before session trust: `src/Salryn.Api.Local/LocalApiApplicationFactory.cs:120`, `src/Salryn.Api.Local/LocalApiApplicationFactory.cs:148`, `src/Salryn.Api.Local/LocalApiApplicationFactory.cs:163`.
- `LocalApiLocalAccessGuard` accepts only loopback and rejects requests carrying remote bridge headers: `src/Salryn.Api.Local/Auth/LocalApiLocalAccessGuard.cs:12`.
- `/api/lan-control` is a separate group and every route uses `LocalLanControlAuthorization`: `src/Salryn.Api.Local/LocalLanControlEndpoints.cs:21`; the guard rejects non-loopback before permission checks: `src/Salryn.Api.Local/Auth/LocalLanControlAuthorization.cs:24`.
- Remote POS is isolated under `/api/remote-pos`: `src/Salryn.Api.Local/RemotePos/RemotePosApiEndpoints.cs:18`.
- Remote Read-Only is isolated under `/api/remote-readonly` and uses GET endpoints with bridge-or-localhost authorization: `src/Salryn.Api.Local/RemoteReadOnlyApiEndpoints.cs:15`, `src/Salryn.Api.Local/RemoteReadOnlyApiEndpoints.cs:313`.
- Remote POS requires terminal and handshake headers, not cookie-only trust: `src/Salryn.Api.Local/RemotePos/RemotePosApiAuthorization.cs:28`.
- Remote POS command authorization revalidates bridge trust, operator session token, and the command authorization matrix: `src/Salryn.Api.Local/RemotePos/RemotePosApiAuthorization.cs:67`.
- Admin/Owner remote POS login remains blocked by service-level role policy. Cashier/Manager are the only allowed remote login roles: `src/Salryn.Api.Local/RemotePos/RemotePosOperatorSessionService.cs:225`, `src/Salryn.Api.Local/RemotePos/RemotePosOperatorSessionService.cs:359`.
- Session tokens are issued once to the remote client but only the hash is stored server-side, with fixed-time validation: `src/Salryn.Api.Local/RemotePos/RemotePosOperatorSessionService.cs:33`, `src/Salryn.Api.Local/RemotePos/RemotePosOperatorSessionService.cs:276`.
- Wildcard and `0.0.0.0` hits are guard/policy/test/documentation hits, not runtime wildcard binding. Example guard: `src/Salryn.Core/Host/SalrynHostCommandAuthorityGuard.cs:39`.

Hidden mutation path review:

- Remote Read-Only route group reviewed as GET-only. It exposes status, capabilities, dashboard summary, products, terminal status, bind policy, diagnostics, preflight, and audit reads.
- Host-local mutation endpoints exist for auth logout, LAN control, and user creation, but they are loopback/local-operator guarded.
- Remote POS is the only reviewed remote LAN mutation surface. Its commands require bridge headers, accepted terminal, valid handshake, host-issued remote operator session, role/permission matrix approval, and idempotency on write paths.

## Host/Client UX Boundary Review

Verdict: no blocker found.

Evidence:

- `App` renders `RemoteClientApp` for non-loopback browser locations and `ConsoleApp` for loopback: `src/Salryn.App.Web/src/App.tsx:170`.
- The browser location split treats `localhost`, `::1`, and `127.*` as loopback: `src/Salryn.App.Web/src/utils/lanEntry.ts:1`.
- Host sidebar has `Host LAN Control` and `Users`; it does not include `Remote POS Client` or `Remote Read-Only Client`: `src/Salryn.App.Web/src/navigation.ts:20`.
- Remote landing copy includes the required LAN client wording and the Remote POS/Read-Only choices: `src/Salryn.App.Web/src/pages/RemoteClientLandingPage.tsx:22`.
- Host LAN Control shows top status, limits, client URL warning, pairing control, terminal control, and collapsed advanced diagnostics: `src/Salryn.App.Web/src/pages/HostLanControlPage.tsx:90`, `src/Salryn.App.Web/src/pages/HostLanControlPage.tsx:125`, `src/Salryn.App.Web/src/pages/HostLanControlPage.tsx:146`, `src/Salryn.App.Web/src/pages/HostLanControlPage.tsx:158`, `src/Salryn.App.Web/src/pages/HostLanControlPage.tsx:286`, `src/Salryn.App.Web/src/pages/HostLanControlPage.tsx:334`.
- Remote Read-Only Client is focused on read-only status, pairing/session state, terminal identity, bind/readiness, and lifecycle guidance. It does not expose host control buttons: `src/Salryn.App.Web/src/pages/RemoteReadOnlyClientPage.tsx:64`.

Stale dist workflow:

- `LocalApiApplicationFactory` serves the built static app shell from `src/Salryn.App.Web/dist` when present: `src/Salryn.Api.Local/LocalApiApplicationFactory.cs:248`.
- The manual issue was resolved by `npm run build` plus API restart, and the current `npm run build` passed.
- Non-blocker follow-up: add a small operator-facing or startup asset-stamp guard so stale `dist` can be detected automatically instead of relying on dry-run discipline.

## User Creation MVP Review

Verdict: no blocker found.

Evidence:

- Frontend Users page is permission-gated through `Users.Manage`: `src/Salryn.App.Web/src/security/permissions.ts:68`.
- Create form fields exist for display name, username, temporary password, confirm password, role, and active/enabled: `src/Salryn.App.Web/src/pages/UsersPage.tsx:161`.
- Client-side validation rejects blank display name, blank username, blank role, blank/short password, and mismatched confirmation: `src/Salryn.App.Web/src/pages/UsersPage.tsx:84`.
- Role options are filtered to active Cashier/Manager roles: `src/Salryn.App.Web/src/pages/UsersPage.tsx:76`.
- Password fields are cleared after success and the success message does not display the password: `src/Salryn.App.Web/src/pages/UsersPage.tsx:63`.
- Frontend API posts only to `/api/users` with credentials included: `src/Salryn.App.Web/src/api/users.ts:52`.
- Backend create endpoint is `POST /api/users`, guarded by `LocalApiPermissionGuards.RequirePermission(... UsersManage)`: `src/Salryn.Api.Local/LocalUserAdminEndpoints.cs:12`.
- `UserAdminApiService` hashes with existing `SalrynPasswordHasher` and restricts this MVP to Cashier/Manager: `src/Salryn.Api.Local/Services/UserAdminApiService.cs:45`, `src/Salryn.Api.Local/Services/UserAdminApiService.cs:63`.
- No schema or seed change was added for USER-M1. It uses existing `users`, `roles`, and `role_permissions` tables.

## Bad-Code / Temporary-Code Findings

Command used:

```powershell
rg -n "TODO|FIXME|HACK|temporary|temp|workaround|mock|fake|test-only|bypass|debug|hardcoded|password|secret|token|seed|default credential|admin password|0\.0\.0\.0|wildcard|RMO|certification|official" .
```

Raw hit count: 3,750.

Classification:

| Hit group | Count | Classification | Notes |
| --- | ---: | --- | --- |
| `tests` | 1,243 | Harmless test/guard coverage | Most hits assert forbidden tokens, use fakes for isolated test seams, or exercise RMO/official guardrails. |
| `tools` | 607 | Harmless smoke/test tooling | Includes smoke test fakes, temp roots, seed checks, and official-output engineering candidate commands. Not runtime LAN exposure. |
| `src` | 723 | Acceptable known guard, plus non-blocking legacy UI debt | Runtime hits are primarily password hashing/auth flows, remote POS token handling, no-wildcard guard text, official-output host-local code, and UI comments. |
| `00-HANDOFF`, `00-COMPLIANCE`, architecture/deployment/design docs | 1,100+ | Harmless documentation/history | Heavy RMO/official/certification wording is documentation and mostly explicit non-certification language. |
| Root seal/readme docs | Remaining hits | Harmless documentation | Historical seals and plans. |

Keyword-specific notes:

- `TODO`, `FIXME`, `HACK`: no hits.
- `password`, `secret`, `token`: acceptable auth/session/security code or tests. No plaintext password logging or seeded credential blocker found.
- `admin password`, `default credential`, `seed credential`: documentation/test guard hits only. Seed data defines roles/permissions/catalog defaults, not default user passwords.
- `0.0.0.0`, `wildcard`: guard/test/documentation hits proving wildcard bind rejection, plus UI policy display. No runtime wildcard bind found.
- `mock`, `fake`, `test-only`: test/smoke seams and historical docs. No production remote trust bypass found.
- `temporary`, `temp`, `workaround`, `debug`, `hardcoded`: mostly temp test directories, documentation, package-lock dependency names, and legacy UI/design comments. Non-blocking cleanup candidate: older WinForms/UI comments such as temporary shared styling and legacy guard wording should be reviewed in a later polish sprint, not LAN-M16.
- `RMO`, `official`, `certification`: extensive existing RMO/official-output planning and engineering-candidate material. It repeatedly states non-certification and does not add LAN-M16 remote official checkout.

Blocker classification: no blocker found.

## Backend Endpoint Map

Application route registration:

- `GET /health`: health only.
- Host-local operator auth/session:
  - `POST /api/auth/login`
  - `POST /api/auth/logout`
  - `GET /api/session`
  - Guard: `LocalApiLocalAccessGuard`.
- Host-local dashboard:
  - `GET /api/dashboard/summary`
  - Guard: authenticated local session.
- Host-local read-only operator module APIs:
  - Products, inventory, sales, users list/detail/roles, audit.
  - Guard: local authenticated session plus per-page permissions.
- Host-local user creation:
  - `POST /api/users`
  - Guard: local loopback operator session plus `Users.Manage`.
  - Service: `UserAdminApiService`.
- Remote Read-Only:
  - Group: `/api/remote-readonly`
  - GET-only route group for status, capabilities, dashboard summary, products, terminal status, bind policy, diagnostics, preflight, audit.
  - Guard: bridge authorization for remote terminals or local operator guard for localhost diagnostic use.
- Host LAN Control:
  - Group: `/api/lan-control`
  - Local-only status/control endpoints for transport, Remote POS runtime, pairing, pending requests, terminal disable/revoke.
  - Guard: `LocalLanControlAuthorization`.
- Remote POS:
  - Group: `/api/remote-pos`
  - Pairing request/activate/status.
  - Session login/logout/read.
  - Capabilities.
  - Cart, shift, checkout, and limited remote admin status.
  - Guard: `RemotePosApiAuthorization`, `RemotePosOperatorSessionService`, `RemotePosAuthorizationMatrix`.

Auth/session guard map:

- `LocalApiLocalAccessGuard`: loopback plus no remote bridge headers.
- `LocalApiPermissionGuards`: local session and permission guard.
- `LocalLanControlAuthorization`: LAN control local-only wrapper.
- `RemoteReadOnlyApiAuthorization`: terminal/handshake/capability bridge authorization for read-only.
- `RemotePosApiAuthorization`: terminal/handshake/session token/role-permission command authorization for remote POS.

Remote POS services:

- `RemotePosApiServices`: composition aggregate.
- `RemotePosPairingGateService`: join code, pending requests, host accept/reject, one-time pairing code, capacity state.
- `RemotePosOperatorSessionService`: trust validation, Cashier/Manager login boundary, token hash storage, active-session capacity.
- `RemotePosRuntimeGate`: runtime enable/disable.
- `RemotePosCheckoutService`: host-owned checkout/cart/shift command execution.
- `RemotePosAuthorizationMatrix`: command permission matrix and host-local-only action list.

Remote Read-Only services:

- `RemoteReadOnlyApiService`: read-only status/products/dashboard/terminal/audit diagnostics.
- `RemoteReadOnlyApiAuthorization`: bridge authorization.
- `RemoteReadOnlyBridgeRequestContext`: bridge header parsing and observed-origin handling.
- `RemoteReadOnlyBridgeDiagnostics`: sanitized diagnostics.
- `RemoteReadOnlyReadAttemptAuditRecorder`: safe read-attempt audit.
- `RemoteReadOnlyLanPreflightEvaluator` and `RemoteReadOnlyLanBindPolicyMapper`: safe readiness/bind policy display.

Local LAN control services:

- `LocalLanControlService`: host status, transport controls, Remote POS runtime, terminal visibility.
- `LocalApiLanUrlResolver`: host URL and LAN URL resolution.
- `LanRemoteTransportApiComposition`: remote transport, terminal registry, handshake, audit composition.

User creation services:

- `LocalUserAdminEndpoints`: local-only POST route.
- `UserAdminApiService`: validation, role restriction, hash creation, repository create/readback.
- `SqliteUserWriteRepository`: existing users write repository now accepts active/inactive status.
- `SqliteUserReadRepository`: readback/list/detail/roles.

Repository/write boundaries:

- Product writes: `SqliteProductWriteRepository`.
- Inventory writes: `SqliteInventoryWriteRepository`.
- Sales writes: `SqliteSaleWriteRepository`.
- User writes: `SqliteUserWriteRepository`.
- Audit writes: `SqliteAuditWriteRepository`.
- Terminal trust writes: terminal registry, pairing invite, credential repositories.
- Official/electronic journal writes: dedicated official/electronic-journal services and shared SQLite transaction participants.

## Database Surface Map

Migration runner:

- `schema_migrations`: `src/Salryn.Infrastructure/Data/Migrations/MigrationRunner.cs`.

Core schema:

- `InitialSchemaMigration` creates:
  - `app_metadata`
  - `roles`
  - `users`
  - `role_permissions`
  - `store_settings`
  - catalog tables: `categories`, `brands`, `units`, `suppliers`, `products`
  - sales/session tables: `receipt_sequences`, `cashier_sessions`, `sales`, `sale_items`, `payments`
  - inventory: `stock_movements`
  - extensibility/logging: `custom_field_definitions`, `custom_field_values`, `print_jobs`, `audit_logs`, `error_logs`, `import_export_jobs`, `backup_logs`

LAN/terminal schema:

- `LanRemoteTransportSchemaMigration`: `salryn_lan_transport_config`.
- `TerminalRegistrySchemaMigration`: `salryn_terminal_registrations`, `salryn_terminal_pairing_invites`.
- `TerminalCredentialHandshakeSchemaMigration`: `salryn_terminal_credentials`, `salryn_remote_client_handshakes`.
- `DeploymentIdentitySchemaMigration`: `deployment_identity`.

Official/RMO-related schema already present:

- `BirMachineProfileSchemaMigration`: `bir_machine_profiles`.
- `BirOfficialDocumentSequenceSchemaMigration`: `bir_official_document_sequences`.
- `BirElectronicJournalSchemaMigration`: `bir_electronic_journal_entries`, preservation batches, entry events.
- `BirElectronicJournalSchemaHardeningMigration`.
- `BirElectronicJournalCorrectionEventSchemaMigration`.
- `BirZReadingSchemaMigration`: `bir_z_readings`.

Seed data:

- `DefaultSeedData` seeds roles, role permissions, units, categories, brands, and receipt sequence.
- Roles seeded: Admin, Manager, Cashier.
- No default user password or seeded Cashier/Manager credential was found in default seed data.

Backup/recovery touchpoints:

- Schema table: `backup_logs`.
- Services/forms: `SqliteBackupRecoveryService`, `BackupRecoveryService`, `BackupRecoveryAppServices`, `BackupForm`.

## Product Activation / 16-Digit License-Key Design Note

Study only. No implementation was performed.

Recommended shape:

- Treat the 16-digit license ID as an identifier only, not proof of entitlement.
- The real proof should be a signed license payload containing license ID, product/edition, customer/store identity, activation scope, expiration or maintenance terms if any, installation ID, and optional device fingerprint hash.
- The app should verify the signed payload with a public key embedded in the app.
- The private signing key must never ship with the app, installer, SQLite database, or local config.
- Activation should bind to a locally generated installation ID. Optional device fingerprinting should be privacy-conscious and tolerate normal hardware changes.
- Offline activation can be supported by request/response files:
  - app generates activation request with installation ID and license ID
  - operator transfers request to signing service
  - signing service returns signed activation token
  - app stores token locally and verifies signature on startup
- Do not store plaintext license secrets locally.
- Do not rely on client-only UI unlock checks.
- Do not rely on SQLite-only unlock checks. SQLite can store the signed activation payload, but startup/runtime gates must verify signature and binding before enabling licensed features.
- Add revocation/update later through signed update payloads or a revocation list. Offline installs can consume signed revocation/update bundles during maintenance.

## RMO/BIR Study Placeholder

No RMO/BIR implementation was performed in this review.

RMO/BIR compliance must be source-backed and reviewed separately before any official-output implementation is promoted or claimed. Future work should cite the exact BIR/RMO source materials, identify which source requirement each schema/workflow satisfies, and keep engineering readiness distinct from legal certification or accreditation.

This LAN-M16 review does not certify production RMO/BIR readiness, official output readiness, printer/PDF/Crystal/ESC-POS readiness, or compliance approval.

## Promotion Readiness Verdict

Verdict for LAN-M16 Freehand final acceptance bundle: GO for lead review and any separate promotion decision by Kirch.

Boundaries:

- This is not a promotion.
- This does not touch `KIRCH-SANDBOX`.
- This does not claim production/stable readiness.
- This does not start LAN-M17.
- This does not start RMO, license-key, printer/PDF/Crystal/ESC-POS, shared SQLite, cloud, QR, discovery, DNS, or mDNS work.

## Blockers / Non-blockers

Blockers:

- None found in this review.

Non-blockers:

- Stale frontend `dist` remains an operational risk if the API is not restarted after `npm run build`. Manual acceptance already resolved this once. Recommended follow-up: add a build stamp or startup/operator warning so stale bundles are visible.
- Bad-code scan has large historical RMO/official/test/doc noise. No blocker found, but future review would benefit from a maintained allow-list or classification script.
- Existing legacy UI comments mention temporary/shared styling and debug/guard wording outside LAN-M16. These are polish/debt items, not acceptance blockers.

## Recommended Next Sprint

Do not start LAN-M17 inside this review.

Recommended next sprint options for Kirch to prioritize:

1. Promotion review packet only: have the lead compare this report, manual acceptance notes, and current CI/local verification before deciding whether to promote Freehand.
2. Backend/database deep review: source-map all write paths, migrations, official/electronic-journal tables, backup/recovery, transaction boundaries, and repository ownership before any database redesign.
3. Stale frontend bundle guard: add a small asset-build stamp or startup diagnostic that tells the operator when the served `dist` is older than the source/build stamp.
4. Product activation design spike: write a source-backed implementation plan for signed activation payloads, offline activation, installation binding, and revocation before writing license code.
5. RMO/BIR source-backed study: gather authoritative requirements and create a compliance matrix before touching official-output implementation.
