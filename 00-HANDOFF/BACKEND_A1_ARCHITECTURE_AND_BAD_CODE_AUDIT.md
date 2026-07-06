# BACKEND-A1 — Architecture and Bad-Code Audit

Review date: 2026-07-06

## Source Stamp

| Item | Value |
| --- | --- |
| Repo | `Kirch-Nairu/Salryn` |
| Local path | `C:\Users\kirch\Salryn` |
| Branch reviewed | `KIRCH-FREEHAND` |
| Local HEAD | `93a2f92146aec9973586ea0c066474b6a7894a16` |
| `origin/KIRCH-FREEHAND` | `93a2f92146aec9973586ea0c066474b6a7894a16` |
| `origin/KIRCH-SANDBOX` | `93a2f92146aec9973586ea0c066474b6a7894a16` |
| Compare `origin/KIRCH-SANDBOX...origin/KIRCH-FREEHAND` | `0 0` |
| Worktree before report | clean |
| Scope note | Review-only. No promotion. No code changes. `KIRCH-SANDBOX` not modified. |

Post-PROMO-M1 context: LAN-M16-RC-B3 + USER-M1 are promoted to `KIRCH-SANDBOX`. Manual acceptance reported passed before promotion (host/client UX, LAN control, pairing, remote login boundaries, user creation MVP, stale frontend dist resolved operationally). Reference: `00-HANDOFF/LAN_M16_FINAL_ACCEPTANCE_AND_BACKEND_REVIEW_PREP.md`.

---

## Verification Results

Executed on `KIRCH-FREEHAND` at `93a2f92146aec9973586ea0c066474b6a7894a16`.

| Command | Result |
| --- | --- |
| `git diff --check` | Passed |
| `dotnet build .\Salryn.sln --no-restore` | Passed — 0 warnings, 0 errors |
| `dotnet test .\Salryn.sln --no-build` | Passed — exit 0 |
| `dotnet run --no-build --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj` | Passed — `SALRYN Core smoke checks passed.` |
| `dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- lan-m16-rc-b-entry-boundary` | Passed |
| `dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- remote-pos-pairing-gate` | Passed |
| `dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- remote-pos-final-acceptance` | Passed |
| `dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- remote-readonly-lan-local-control` | Passed |
| `npm install` in `src\Salryn.App.Web` | Passed — 0 vulnerabilities |
| `npm run build` in `src\Salryn.App.Web` | Passed |
| `git status --short` after review | Clean |

---

## Backend Architecture Map

### Layer overview

| Layer | Path | Role |
| --- | --- | --- |
| API host | `src/Salryn.Api.Local` | ASP.NET minimal API: loopback operator console, LAN control, remote read-only bridge, remote POS commands, static web shell |
| Domain/core | `src/Salryn.Core` | Business rules, permissions, host-command authority, checkout, official/RMO services, models |
| Persistence | `src/Salryn.Infrastructure` | SQLite migrations, repositories, seeding, backup/recovery, official persistence participants |
| Composition | `src/Salryn.Composition.Local` | Wires read repositories + auth for API/smoke (`SalrynLocalComposition`) |
| Host runtime | `src/Salryn.Host.Local` | Local host command dispatch, official output handlers |
| Desktop app | `src/Salryn.App.WinForms` | Primary host-local mutation surface (catalog, inventory, checkout, users, backup, official output) |
| Verification | `tests/Salryn.Tests` | Compile-check + runtime smoke harnesses (no xUnit package) |
| Tooling | `tools/Salryn.Tools.CoreSmoke`, `tools/Salryn.Tools.InfrastructureSmoke`, `tools/Salryn.Tools.CheckoutLocalHostSmoke` | Executable smoke gates |

### `Salryn.Api.Local` composition

Entry: `Program.cs` → `LocalApiApplicationFactory.CreateAsync()`.

Registered singletons (`LocalApiApplicationFactory.cs`):

- `SalrynLocalComposition` — read/auth composition root
- `LanRemoteTransportApiServices` — transport config, terminal registry, handshake, audit
- `RemotePosRuntimeGate`, `RemotePosApiServices` — remote POS runtime
- `LocalApiSessionService`, `InMemoryLocalApiSessionStore` — loopback operator sessions (in-memory, cookie-backed)
- `DashboardSummaryService`, `OperatorReadService`, `UserAdminApiService`, `RemoteReadOnlyApiService`, `LocalLanControlService`

Static assets: serves `src/Salryn.App.Web/dist` when `index.html` exists (`ResolveWebDistPath`, `LocalApiApplicationFactory.cs:244-257`).

Listen URLs: default `http://127.0.0.1:5299`; optional explicit LAN bind via `LocalApiLanUrlResolver` when transport config enabled.

### Endpoint groups (registration order)

| Group | Registration | Guard family |
| --- | --- | --- |
| Health | `LocalApiApplicationFactory.cs:114` | None |
| Host auth/session | `LocalApiApplicationFactory.cs:120-174` | `LocalApiLocalAccessGuard` |
| Host dashboard | `LocalApiApplicationFactory.cs:176` | `LocalApiPermissionGuards` (authenticated session) |
| Host read-only operator APIs | `ReadOnlyEndpointExtensions.cs` | `LocalApiPermissionGuards` per permission |
| Host user admin | `LocalUserAdminEndpoints.cs` | `LocalApiPermissionGuards` + `UsersManage` |
| Remote read-only | `RemoteReadOnlyApiEndpoints.cs` | `RemoteReadOnlyApiAuthorization` (bridge or localhost) |
| LAN control | `LocalLanControlEndpoints.cs` | `LocalLanControlAuthorization` |
| Remote POS | `RemotePos/RemotePosApiEndpoints.cs` | `RemotePosApiAuthorization` + matrix |

### Service boundaries

| Boundary | Key types | Notes |
| --- | --- | --- |
| Loopback operator | `LocalApiLocalAccessGuard`, `LocalApiSessionService`, `LocalApiPermissionGuards` | Rejects remote bridge headers; cookie alone never grants LAN operator trust |
| LAN control | `LocalLanControlService`, `LocalLanControlAuthorization` | Transport, pairing, terminal disable/revoke; loopback only |
| Remote read-only | `RemoteReadOnlyApiService`, `RemoteReadOnlyApiAuthorization`, bridge context | GET-only business reads; audit side-effect on bridge authorize |
| Remote POS | `RemotePosCheckoutService`, `RemotePosPairingGateService`, `RemotePosOperatorSessionService`, `RemotePosAuthorizationMatrix` | Only reviewed remote mutation surface over HTTP |
| User admin (API) | `UserAdminApiService` | Create-only MVP; Cashier/Manager roles |
| Host commands | `Salryn.Host.Local` dispatchers | Official checkout, backup, catalog mutations — WinForms/host path |

### Repository boundaries

Read (API composition): `SalrynLocalComposition` exposes sale/product/inventory/catalog/user/audit/error-log read repositories only.

Write (Infrastructure `Sqlite*WriteRepository`):

| Repository | Tables |
| --- | --- |
| `SqliteProductWriteRepository` | `products`, `audit_logs` (deactivate) |
| `SqliteSaleWriteRepository` | `sales`, `sale_items`, `payments`, `stock_movements`, `products` (stock) |
| `SqliteInventoryWriteRepository` | `stock_movements`, `products` |
| `SqliteUserWriteRepository` | `users`, `audit_logs` |
| `SqliteStoreSettingsWriteRepository` | `store_settings` |
| `SqliteCashierSessionWriteRepository` | `cashier_sessions` |
| `SqliteAuditWriteRepository` | `audit_logs` |

Additional write paths: terminal registry/credentials/pairing invites, LAN transport config, official BIR tables, backup file replace, seeding/migrations.

### Auth/session guard map

| Guard | File | Purpose |
| --- | --- | --- |
| `LocalApiLocalAccessGuard` | `Auth/LocalApiLocalAccessGuard.cs` | Loopback + no remote bridge headers |
| `LocalApiPermissionGuards` | `Auth/LocalApiPermissionGuards.cs` | Loopback session + permission |
| `LocalLanControlAuthorization` | `Auth/LocalLanControlAuthorization.cs` | LAN control local operator wrapper |
| `RemoteReadOnlyApiAuthorization` | `Services/RemoteReadOnlyApiAuthorization.cs` | Terminal/handshake/capability bridge |
| `RemotePosApiAuthorization` | `RemotePos/RemotePosApiAuthorization.cs` | Terminal/handshake/session token + command matrix |

### Audit/logging paths

- Product deactivate, user admin, WinForms workflows → `SqliteAuditWriteRepository`
- Remote read-only bridge → `RemoteReadOnlyReadAttemptAuditRecorder` → `audit_logs`
- Remote POS checkout → best-effort audit post-commit (`RemotePosCheckoutService`)
- Error logs: `IErrorLogReadRepository` exists; `IErrorLogWriteRepository` interface exists but no SQLite implementation found

### Backup/recovery paths

- `SqliteBackupRecoveryService` — whole-DB file backup/restore (host-local, WinForms)
- Schema table: `backup_logs`
- Not exposed via `Salryn.Api.Local` HTTP API

---

## Endpoint Inventory

**No PUT/PATCH/DELETE routes in `Salryn.Api.Local`.** All HTTP mutations are POST.

### Classification legend

| Class | Meaning |
| --- | --- |
| HL | Host-local only (loopback operator) |
| LC | LAN control (loopback operator + permissions) |
| RRO | Remote read-only bridge |
| RPOS | Remote POS |
| HA | Host admin/user management |
| RO | Read-only operator (loopback) |
| SYS | System/health |

### Full inventory

| Route | Method | Class | Guard | Read/Write | Risk |
| --- | --- | --- | --- | --- | --- |
| `/health` | GET | SYS | None | Read | Low — service enumeration on LAN bind |
| `/api/auth/login` | POST | HL | `LocalApiLocalAccessGuard` | Session create (memory) | Low |
| `/api/auth/logout` | POST | HL | `LocalApiLocalAccessGuard` | Session revoke (memory) | Low |
| `/api/session` | GET | HL | `LocalApiLocalAccessGuard` | Read | Low |
| `/api/dashboard/summary` | GET | RO | Authenticated session | Read | Low |
| `/api/products` | GET | RO | `ProductsView` | Read | Low |
| `/api/products/{id}` | GET | RO | `ProductsView` | Read | Low |
| `/api/products/catalog/categories` | GET | RO | `ProductsView` | Read | Low |
| `/api/products/catalog/brands` | GET | RO | `ProductsView` | Read | Low |
| `/api/inventory/low-stock` | GET | RO | `InventoryView` | Read | Low |
| `/api/inventory/movements` | GET | RO | `InventoryView` | Read | Low |
| `/api/sales/history` | GET | RO | `SalesViewHistory` | Read | Low |
| `/api/sales/{id}` | GET | RO | `SalesViewHistory` | Read | Low |
| `/api/users` | GET | RO | `UsersView` | Read | Low |
| `/api/users/roles` | GET | RO | `UsersView` | Read | Low |
| `/api/users/{id}` | GET | RO | `UsersView` | Read | Low |
| `/api/audit/logs` | GET | RO | `AuditView` | Read | Low |
| `/api/audit/errors` | GET | RO | `AuditView` | Read | Low |
| `/api/users` | POST | HA | `UsersManage` + loopback session | Write `users` | Medium — intentional host-local create |
| `/api/lan-control/status` | GET | LC | `LocalLanControlAuthorization` | Read | Low |
| `/api/lan-control/remote-transport` | POST | LC | `SettingsManage` | Write transport config | Medium |
| `/api/lan-control/remote-pos` | POST | LC | `SettingsManage` | Runtime gate (memory) | Medium |
| `/api/lan-control/remote-pos/pairing` | GET | LC | `StoreTerminalView` | Read pairing state | Low |
| `/api/lan-control/remote-pos/pairing/join-code/rotate` | POST | LC | `StoreTerminalManage` | Memory join code | Medium |
| `/api/lan-control/remote-pos/pairing/requests` | GET | LC | `StoreTerminalView` | Read pending | Low |
| `/api/lan-control/remote-pos/pairing/requests/{id}/accept` | POST | LC | `StoreTerminalManage` | Write pairing invite | High — trust expansion |
| `/api/lan-control/remote-pos/pairing/requests/{id}/reject` | POST | LC | `StoreTerminalManage` | Memory state | Low |
| `/api/lan-control/terminals/{id}/disable` | POST | LC | `StoreTerminalManage` | Write terminal registry | Medium |
| `/api/lan-control/terminals/{id}/revoke` | POST | LC | `StoreTerminalManage` | Revoke credentials | Medium |
| `/api/remote-readonly/*` | GET | RRO | Bridge or localhost | Read (+ audit write) | Low–Medium |
| `/api/remote-pos/pairing/status` | GET | RPOS | None | Read in-memory state | Low — GUID knowledge |
| `/api/remote-pos/pairing/request` | POST | RPOS | Bridge context only | Memory pending | Medium |
| `/api/remote-pos/pairing/activate` | POST | RPOS | Bridge context | Write terminals/credentials | High — trust expansion |
| `/api/remote-pos/session/login` | POST | RPOS | Bridge + runtime gate | Memory session | Medium |
| `/api/remote-pos/session/logout` | POST | RPOS | Command auth | Memory revoke | Low |
| `/api/remote-pos/session` | GET | RPOS | Command auth | Read | Low |
| `/api/remote-pos/capabilities` | GET | RPOS | Command auth | Read | Low |
| `/api/remote-pos/cart/*` | POST | RPOS | `SalesCreate` matrix | Memory cart | Medium |
| `/api/remote-pos/shift/open\|close` | POST | RPOS | `ShiftOpenClose` | Write `cashier_sessions` | Medium |
| `/api/remote-pos/checkout/submit` | POST | RPOS | `SalesCreate` + idempotency | Write sales/inventory | High — durable remote mutation |
| `/api/remote-pos/checkout/status/{id}` | GET | RPOS | Command auth | Read | Low |
| `/api/remote-pos/manager/override` | POST | RPOS | Matrix `NotSupported` | None | Low — blocked by design |
| `/api/remote-pos/admin/status` | GET | RPOS | Command auth | Read | Low |

### Risky endpoints — detail

| Endpoint | File | Primary risk | Mitigation present |
| --- | --- | --- | --- |
| `POST /api/remote-pos/checkout/submit` | `RemotePosApiEndpoints.cs:174` | Remote sale/inventory mutation | Bridge + session + matrix + idempotency + host-owned receipt allocation |
| `POST /api/remote-pos/pairing/activate` | `RemotePosApiEndpoints.cs:35` | Terminal trust write | Transport gate, invite scope, atomic activation |
| `POST /api/lan-control/.../accept` | `LocalLanControlEndpoints.cs:142` | Host approves pairing | Loopback + `StoreTerminalManage` |
| `POST /api/users` | `LocalUserAdminEndpoints.cs:12` | User creation | Loopback + `UsersManage`; Cashier/Manager only |
| `GET /health` | `LocalApiApplicationFactory.cs:114` | LAN enumeration | Minimal payload; no secrets |

---

## Write-Path Audit

### HTTP API write paths (durable)

| Path | Classification | Evidence |
| --- | --- | --- |
| `POST /api/users` | Intended host-local | `UserAdminApiService` → `SqliteUserWriteRepository.CreateAsync` |
| `POST /api/lan-control/remote-transport` | Intended host-local | `LanRemoteTransportConfigService` UPSERT |
| `POST /api/lan-control/terminals/*/disable\|revoke` | Intended host-local | `SqliteTerminalRegistryRepository` |
| `POST /api/lan-control/.../accept` | Intended host-local | Pairing invite persistence |
| `POST /api/remote-pos/pairing/activate` | Intended Remote POS | `TerminalPairingActivationService` |
| `POST /api/remote-pos/shift/*` | Intended Remote POS | `CashierSessionService` → `cashier_sessions` |
| `POST /api/remote-pos/checkout/submit` | Intended Remote POS | `PosCheckoutApplicationService` → `SqliteSaleWriteRepository` |
| Remote read-only bridge audit | Read-only boundary side-effect (intentional) | `RemoteReadOnlyReadAttemptAuditRecorder` → `audit_logs` only |

### Host-local only (not HTTP API)

| Domain | Write mechanism | Classification |
| --- | --- | --- |
| Products | `SqliteProductWriteRepository` via WinForms | Intended host-local |
| Inventory | `SqliteInventoryWriteRepository` via WinForms | Intended host-local |
| Sales void | `SqliteSaleWriteRepository.MarkVoidedAsync` via WinForms | Intended host-local |
| Store settings | `SqliteStoreSettingsWriteRepository` via WinForms | Intended host-local |
| User update/password/status | `SqliteUserWriteRepository` via WinForms | Intended host-local |
| Official checkout | `SqliteOfficialCheckoutPersistenceService` shared txn | Intended host-local official |
| BIR journal/Z-reading | Dedicated official services | Intended host-local official |
| Backup restore | `SqliteBackupRecoveryService` file replace | Intended host-local — high blast radius |

### Receipt/journal numbering

| Path | Numbering | Remote reachable? |
| --- | --- | --- |
| Remote POS checkout | `RemotePosReceiptNumberAllocator` — `RPOS-{epoch}-{token}-{seq}` | Yes — host-allocated, not client-supplied |
| Official checkout | `bir_official_document_sequences` + electronic journal | No — `SalrynLanRemoteTransportGate` hard-blocks `AllowOfficialCommandsRemotely` (`SalrynLanRemoteTransportGate.cs:170-174`) |

### Unclear / improvement items (not blockers)

| Item | Classification | Notes |
| --- | --- | --- |
| `IErrorLogWriteRepository` | Unclear | Interface at `WriteRepositoryInterfaces.cs:143`; no factory implementation |
| Remote POS vs official numbering | Unclear for compliance ops | By design separate; operators must not confuse `RPOS-*` with BIR official numbers |
| API user admin incomplete | Unclear product gap | API create-only; update/password/deactivate WinForms-only |
| Remote POS audit post-commit | Unclear resilience | Sale durable even if audit write fails (`RemotePosCheckoutService`) |
| `allow_official_commands_remotely` DB flag | Low risk | Hard-blocked at runtime even if DB tampered |

**No read-only boundary violation** found for business mutations. Remote read-only writes audit rows only.

---

## Security Boundary Review

| Boundary | Verdict | Evidence |
| --- | --- | --- |
| Local admin/user management not usable from LAN client | **PASS** | `LocalApiLocalAccessGuard` on auth/session; `LocalApiPermissionGuards` chains loopback first |
| Remote POS cannot bypass pairing | **PASS** | Terminal + handshake headers required; activation through invite gate |
| Remote POS cannot login Admin/Owner | **PASS** | `RemotePosOperatorSessionService.cs:224-231`, `:359` — Cashier/Manager only |
| Remote Read-Only cannot mutate business data | **PASS** | GET-only routes; audit side-effect only |
| LAN Control is local-only | **PASS** | `LocalLanControlAuthorization` — loopback before session |
| No broad CORS relaxation | **PASS** | Only `127.0.0.1:5173` and `localhost:5173` (`LocalApiApplicationFactory.cs:91`) |
| No cookie-only LAN trust | **PASS** | Remote POS/read-only reject cookie-only; require bridge headers |
| No remote official/RMO path | **PASS** | `SalrynLanRemoteTransportGate` forbids official/admin remote flags |
| No wildcard bind / 0.0.0.0 | **PASS** | `SalrynHostCommandLoopbackRules.IsForbiddenBindAddress` (`SalrynHostCommandAuthorityGuard.cs:39`); `LocalApiLanUrlResolver` requires explicit LAN IP |

### Security gaps (non-blocking)

| ID | Severity | Finding |
| --- | --- | --- |
| S1 | Moderate | `RemoteReadOnlyApiService` may return unsanitized `ErrorDetail.Message` in response bodies to remote clients — can chain to `exception.Message` from host commands |
| S2 | Low | `RemotePosApiEndpoints` pairing/cart/shift error paths lack `SanitizePublicText` (inconsistent with auth rejections) |
| S3 | Low | `/health` and `/api/remote-pos/pairing/status` unauthenticated when LAN bind active |
| S4 | Low | `/api/remote-pos/pairing/request` does not verify transport enabled before accepting |
| S5 | Info | Session cookie lacks `Secure` flag (HTTP localhost deployment) |
| S6 | Info | Dev `Console.WriteLine` exposes SQLite path (`LocalApiApplicationFactory.cs:48-49`) |

**Security blockers: none.**

---

## Bad-Code / Stale-Code Findings

Search performed with workspace grep across `src`, `tests`, `tools`, `00-HANDOFF` (PowerShell `rg` unavailable in shell; grep tool used).

### Keyword summary

| Pattern | `src` hits | Classification |
| --- | ---: | --- |
| `TODO`, `FIXME`, `HACK` | 0 in `src` | None found in runtime source |
| `mock`, `fake`, `test-only` | ~3 files in `src` (comments/docs) | Harmless — WinForms frontend notes |
| `bypass` | WinForms presenter comments | Harmless — "do not bypass presenter" guidance |
| `password`, `secret`, `token` | ~35 `src` files | Acceptable — auth, hashing, remote session, sanitization guards |
| `0.0.0.0`, `wildcard` | 3 `src` files | Acceptable known guards/policy text |
| `official`, `RMO`, `certification` | ~90 `src` files | Acceptable — host-local official engineering; explicit non-certification posture |
| `workaround`, `hardcoded`, `debug` | Sparse WinForms comments | Improvement candidate — UI polish debt |
| `seed`, `admin password` | Bootstrap/seed/tests | Acceptable — first-run admin creation, not default credentials |

### Notable classifications

| Finding | Classification | Notes |
| --- | --- | --- |
| No `TODO`/`FIXME`/`HACK` in `src` | Acceptable | Clean runtime source marker posture |
| `DefaultSeedData` roles/permissions only | Acceptable known guard | No seeded user passwords |
| `FirstRunBootstrapper` admin password | Acceptable | Operator-supplied first-run only |
| `RemotePosReceiptNumberAllocator` in-process numbering | Acceptable known guard | Documented limitation vs BIR official path |
| `IErrorLogWriteRepository` without impl | Stale/temporary seam | Improvement candidate for DB-A1 |
| WinForms "temporary" UI comments | Stale/temporary code comments | Non-blocking polish |
| Extensive RMO/official code in Core/Infrastructure | Acceptable engineering candidate | Host-local only; not LAN-remote |
| Historical handoff doc noise | Harmless documentation | Prior scans hit 3,000+ lines across docs/tests |

**Bad-code blockers: none.**

---

## Error and Logging Review

### Console output (`src`)

| Location | Content | Risk |
| --- | --- | --- |
| `LocalApiApplicationFactory.cs:48-49` | `[Salryn Dev] SQLite database: {path}` | Low — dev-only path disclosure |
| `Program.cs:72-73` | Smoke test counters | None — `--smoke` only |

**No password, token, or secret logging found in `Console.WriteLine`.**

### Remote client error surfacing

- Bridge **denials** sanitized via `RemoteReadOnlyBridgeDiagnostics.SanitizePublicText`
- Bridge **success-path** service messages may include raw infrastructure errors (see S1)
- Remote POS auth rejections sanitized; cart/pairing/shift errors may not be (see S2)
- No `UseDeveloperExceptionPage`; no stack traces in API responses reviewed

### Operator diagnostics

- Remote read-only diagnostics/preflight/bind-policy provide operator-safe readiness text
- Dashboard warnings embed raw repo errors — loopback-only today (`DashboardSummaryService`)
- Audit recorder redacts secret markers (`RemoteReadOnlyReadAttemptAuditRecorder.cs:108-115`)

---

## Backend Quality Findings

Report only — no refactor performed.

### Large classes (split candidates for later)

| File | ~Lines | Concern |
| --- | ---: | --- |
| `RemotePosCheckoutService.cs` | 540 | Cart + shift + checkout + idempotency in one service |
| `RemotePosPairingGateService.cs` | 508 | Pairing state machine + capacity + codes |
| `RemoteReadOnlyApiService.cs` | 438 | Multiple read surfaces + error mapping |
| `RemotePosOperatorSessionService.cs` | 377 | Session lifecycle + login policy |
| `RemotePosApiEndpoints.cs` | 317 | Route wiring + inline handlers |

### Duplicate / inconsistent logic

- Error sanitization: `RemoteReadOnlyApiAuthorization` and `RemotePosApiAuthorization.Rejected` sanitize; `RemoteReadOnlyApiService` and some `RemotePosApiEndpoints` handlers do not
- Permission guard pattern repeated per endpoint in `ReadOnlyEndpointExtensions.cs` — acceptable but verbose
- Host command error wrapping (`HostCommandDispatcher`) vs API error mapping — inconsistent remote exposure

### Missing tests around risky code

| Area | Coverage |
| --- | --- |
| Remote POS checkout/idempotency | Strong — infrastructure smoke + compile checks |
| Remote POS pairing gate | Strong |
| Remote read-only bridge | Strong |
| USER-M1 create user | Strong — `UserM1HostOnlyUserCreationChecks` |
| Error sanitization on success paths | Weak — no dedicated guard |
| `IErrorLogWriteRepository` seam | Absent — interface only |
| Stale frontend dist detection | Absent — operational only |

### Other quality notes

- **Idempotency:** Remote POS checkout has client request idempotency; official checkout uses shared transaction participants
- **Transaction boundaries:** Official checkout uses atomic shared SQLite transaction; standard remote POS uses `SqliteSaleWriteRepository` path
- **State machines:** Pairing (pending → accepted → activated) and remote session lifecycle are in-memory + DB — document clearly for DB-A1
- **Brittle string checks:** Permission and command names are string constants — covered by smoke compile checks
- **Exception handling:** Host commands catch and wrap `exception.Message` — feeds S1 gap

---

## Database Review Prep Notes

Do not redesign DB in BACKEND-A1. Prep for DB-A1:

### Schema/migration source

- Runner: `src/Salryn.Infrastructure/Data/Migrations/MigrationRunner.cs` → `schema_migrations`
- 13 migrations including initial schema, LAN/terminal, deployment identity, BIR official tables

### Core tables (initial + extensions)

| Domain | Tables |
| --- | --- |
| Identity | `app_metadata`, `roles`, `users`, `role_permissions`, `deployment_identity` |
| Store/catalog | `store_settings`, `categories`, `brands`, `units`, `suppliers`, `products` |
| Sales | `receipt_sequences`, `cashier_sessions`, `sales`, `sale_items`, `payments` |
| Inventory | `stock_movements` |
| Audit/logging | `audit_logs`, `error_logs`, `backup_logs` |
| LAN/terminal | `salryn_lan_transport_config`, `salryn_terminal_registrations`, `salryn_terminal_pairing_invites`, `salryn_terminal_credentials`, `salryn_remote_client_handshakes` |
| Official/RMO | `bir_machine_profiles`, `bir_official_document_sequences`, `bir_electronic_journal_*`, `bir_z_readings` |

### Seed source

- `DefaultSeedData` — roles (Admin/Manager/Cashier), permissions, catalog defaults, receipt sequence
- No default user credentials seeded

### Integrity risks for DB-A1

| Risk | Notes |
| --- | --- |
| Remote POS `RPOS-*` vs `receipt_sequences` / BIR sequences | Two numbering systems; document operational boundary |
| `sales.receipt_no UNIQUE` | Backstop for remote allocator collisions |
| BIR journal hardening triggers | UPDATE/DELETE blocked on journal tables |
| Whole-DB backup restore | High blast radius; verify FK/migration ordering on restore |
| `IErrorLogWriteRepository` dead interface | Error log persistence path unclear |
| In-memory pairing/session state vs DB terminal state | Reconciliation on host restart |
| `allow_official_commands_remotely` column | Runtime hard-block exists; DB-A1 should confirm column cannot bypass gate |

---

## RMO Review Prep Notes

No RMO implementation in this audit. Backend areas likely relevant later:

| Area | Existing backend surface |
| --- | --- |
| Official receipt/invoice numbering | `bir_official_document_sequences`, `OfficialDocumentSequenceAllocationService` |
| Electronic journal | `bir_electronic_journal_entries`, append participants, hardening migrations |
| Audit trail | `audit_logs`, official document audit events, engineering evidence packages |
| Void/refund/discount | Permissions seeded; lifecycle services in Core/Infrastructure |
| Z-reading / X-reading | `OfficialZReadingService`, `OfficialXReadingService`, `bir_z_readings` |
| Reports | Official report renderers, text file output workflow |
| Tax/VAT/non-VAT | `SalrynOfficialTaxBucketPolicy`, product tax classification migration |
| Certification wording | Extensive "engineering candidate / non-certification" language — do not promote to legal claims |

Remote LAN stack **does not** invoke official checkout (`official.checkout.complete`). `SalrynLanRemoteTransportGate` forbids official remote commands.

---

## Licensing Review Prep Notes

No license implementation. Integration points for future LIC-M1:

| Integration point | Current state |
| --- | --- |
| Startup guard | Not present — no license check on API or WinForms startup |
| Local activation storage | `app_metadata` table exists; no license payload schema |
| Signed token verification | Not present |
| Offline activation | Not present |
| Device binding | `deployment_identity` table exists for store/host/terminal identity |
| SQLite-only unlock | Not present — no unlock path today |
| Private signing key in app | Not present |

Recommended shape (study only, from prior handoff): 16-digit ID as identifier; signed payload verified with embedded public key; private key never in app/installer/DB; offline request/response file flow; startup/runtime gate before licensed features.

---

## Blockers

**None found** for continued backend/database planning post-PROMO-M1.

No evidence of:

- Remote admin/user management exposure
- Remote official/RMO checkout path
- Wildcard or `0.0.0.0` bind in runtime
- Seeded default operator credentials
- Plaintext password logging
- Read-only bridge business mutation

---

## Non-blockers

| Item | Priority | Notes |
| --- | --- | --- |
| Stale frontend `dist` | Medium operational | Gitignored; requires `npm run build` + API restart; no startup stamp guard |
| Remote error sanitization inconsistency (S1, S2) | Medium | Harden before wider LAN exposure |
| Unauthenticated `/health`, `/pairing/status` on LAN bind | Low | Enumeration surface |
| `IErrorLogWriteRepository` dead interface | Low | DB-A1 should resolve error log write path |
| Large Remote POS / read-only service classes | Low | Split in dedicated refactor sprint |
| API user admin create-only vs WinForms full admin | Low | Product gap, not security blocker |
| WinForms temporary UI comments | Low | Polish sprint |
| Session cookie `Secure` flag | Low | Revisit if HTTPS deployed |

---

## Recommended Cleanup Sprint

Suggested order for Kirch (no implementation in BACKEND-A1):

1. **BACKEND-A2 (hardening)** — Uniform `SanitizePublicText` on all remote-facing API messages; rate-limit `/pairing/request`; optional startup web-dist stamp log
2. **DB-A1** — Schema ownership map, migration audit, numbering integrity, error log write path, pairing/session persistence review
3. **Stale dist guard** — Build stamp in `index.html` or dev startup diagnostic
4. **RMO source study** — Compliance matrix before any official-output promotion claims
5. **LIC-M1 design spike** — Signed activation plan using `deployment_identity` + `app_metadata`

Do not start: LAN-M17, QR/discovery/cloud, shared SQLite, remote Admin login, wildcard bind, printer/PDF implementation.

---

## GO / NO-GO for DB-A1

**GO** — with scope discipline.

Rationale:

- Post-PROMO-M1 baseline verification is green at `93a2f921`
- Security boundaries for LAN stack are sound; no blocker mutations outside intended paths
- Database surface is substantial (core POS + LAN + BIR official) and needs dedicated integrity review before any redesign
- Known prep items (`IErrorLogWriteRepository`, dual numbering, backup restore, in-memory vs DB pairing state) are exactly DB-A1 scope

DB-A1 should remain **review and integrity mapping first** — no schema redesign, no shared SQLite, no license/RMO implementation until Kirch approves follow-on sprints.

---

## Audit Boundaries

This report:

- Is review-only on `KIRCH-FREEHAND` at promoted stamp `93a2f921`
- Does not promote, reset, or modify branches
- Does not claim RMO/legal/production certification
- Does not implement license keys, RMO checkout, printers, or database redesign
- Extends `LAN_M16_FINAL_ACCEPTANCE_AND_BACKEND_REVIEW_PREP.md` with full backend architecture, write-path, and quality audit at post-promotion stamp

Kirch remains project lead. No feature code changed in this audit.