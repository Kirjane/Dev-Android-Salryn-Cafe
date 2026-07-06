# FULL-SYSTEM-A1 — Master Review and Release Risk Audit

Review date: 2026-07-06

## Source Stamp

| Item | Value |
| --- | --- |
| Repo | `Kirch-Nairu/Salryn` |
| Local path | `C:\Users\kirch\Salryn` |
| Branch reviewed | `KIRCH-FREEHAND` |
| Local HEAD | `6cfda87160b9dbbae40aa16d7ad5797210d8f9fc` |
| `origin/KIRCH-FREEHAND` | `6cfda87160b9dbbae40aa16d7ad5797210d8f9fc` |
| `origin/KIRCH-SANDBOX` | `93a2f92146aec9973586ea0c066474b6a7894a16` |
| Compare `origin/KIRCH-SANDBOX...origin/KIRCH-FREEHAND` | `0 1` |
| Freehand-only commit | `6cfda871` — Add BACKEND-A1 architecture and bad-code audit |
| Prior review | `00-HANDOFF/BACKEND_A1_ARCHITECTURE_AND_BAD_CODE_AUDIT.md` |
| Worktree before report | clean |
| Scope | Review-only. No promotion. No code changes. `KIRCH-SANDBOX` not modified. |

Post-PROMO-M1: LAN-M16-RC-B3 + USER-M1 promoted to sandbox at `93a2f921`. Manual acceptance reported passed. This audit extends BACKEND-A1 across database, frontend, WinForms, deployment, RMO/licensing readiness, and release risk.

---

## Verification Results

Executed on `KIRCH-FREEHAND` at `6cfda871` (includes promoted LAN stack at `93a2f921` plus report-only freehand commit).

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

## Executive Verdict

**Salryn is safe to continue into structured cleanup, DB-A1 integrity work, and later RMO/licensing design spikes.** No full-system blocker was found that would prevent moving forward on backend/database/RMO/licensing/frontend hardening planning.

| Dimension | Verdict |
| --- | --- |
| LAN security model (post-PROMO-M1) | Sound — loopback operator, pairing gate, remote POS boundaries hold |
| Database schema foundation | Strong for POS + BIR engineering; operational backup/numbering risks remain |
| Remote POS persistence model | Intentional split: durable terminal trust, ephemeral runtime state |
| Official/RMO boundary | Host-local only over HTTP; remote cannot touch official tables |
| Licensing | Not implemented — design hooks exist (`app_metadata`, `deployment_identity`) |
| Frontend web shell | Host/remote split works; stale `dist` is the main deployment risk |
| Release readiness | Dev/operator workflow works; production installer/service hardening not complete |

**Blocker count: 0**  
**High-risk non-blocker count: 8** (see section below)

This audit does **not** claim RMO/legal/production certification.

---

## Backend Architecture Review

*Prior evidence: `BACKEND_A1_ARCHITECTURE_AND_BAD_CODE_AUDIT.md`. Re-verified at `6cfda871`.*

### Layer map

| Layer | Path | Responsibility |
| --- | --- | --- |
| API | `src/Salryn.Api.Local` | HTTP: health, loopback auth, read APIs, user create, LAN control, remote read-only, remote POS, static shell |
| Core | `src/Salryn.Core` | Domain rules, permissions, checkout, official/RMO services, host-command authority |
| Infrastructure | `src/Salryn.Infrastructure` | SQLite migrations, repositories, seeding, backup, official persistence |
| Composition | `src/Salryn.Composition.Local` | Read-repo + auth wiring for API/smoke |
| Host | `src/Salryn.Host.Local` | Host command dispatch, official output handlers |
| WinForms | `src/Salryn.App.WinForms` | Primary host-local mutation UI |
| Tests | `tests/Salryn.Tests` | Compile-check + runtime smoke harnesses |
| Tools | `tools/Salryn.Tools.*` | CoreSmoke, InfrastructureSmoke, CheckoutLocalHostSmoke |

### Endpoint groups (HTTP)

| Group | Prefix | Mutations | Guard |
| --- | --- | --- | --- |
| Health | `/health` | None | None |
| Host auth | `/api/auth/*`, `/api/session` | Session memory | `LocalApiLocalAccessGuard` |
| Host reads | `/api/products`, `/api/inventory`, `/api/sales`, `/api/users`, `/api/audit` | GET only | `LocalApiPermissionGuards` |
| User admin | `POST /api/users` | User create | `UsersManage` + loopback |
| LAN control | `/api/lan-control/*` | Transport, pairing, terminals | `LocalLanControlAuthorization` |
| Remote read-only | `/api/remote-readonly/*` | GET only (+ audit side-effect) | Bridge authorization |
| Remote POS | `/api/remote-pos/*` | Pairing, session, cart, shift, checkout | `RemotePosApiAuthorization` + matrix |

### Write-path summary

| Classification | Paths |
| --- | --- |
| Intended host-local HTTP | `POST /api/users`, `/api/lan-control/*` |
| Intended remote POS HTTP | Pairing activate, shift, checkout submit |
| Host-local WinForms/host-commands | Products, inventory, void, settings, official checkout, backup |
| Read-only violation risk | None for business data; remote read-only writes `audit_logs` only |
| Official remote | Hard-blocked — `SalrynLanRemoteTransportGate.cs:170-174` |

### Test/smoke coverage (backend)

Strong: remote POS final acceptance, pairing gate, read-only LAN control, USER-M1, role permission matrix, SEC-A1 web/API security compile checks. Weak: remote success-path error sanitization, stale dist guard.

---

## Database and Migration Review

### Migration runner

- Registry: `DefaultDatabaseInitializationFactory.cs:39-53` — 12 migrations, explicit order
- Runner: `MigrationRunner.cs` — one transaction per version, `schema_migrations` ledger
- Startup: `DatabaseInitializationOrchestrator` — migrations then seed (`RunPendingMigrations` → `SeedDefaultData`)
- Pragmas: `SqlitePragma.cs` — `foreign_keys=ON`, `journal_mode=WAL`, `synchronous=NORMAL`, `busy_timeout=5000`
- Forward-only: no `Down()` migrations

### Migration version order

| Ver | Migration |
| ---: | --- |
| 1 | Initial Salryn schema |
| 2 | BIR machine profiles |
| 3 | BIR official document sequences |
| 4 | BIR electronic journal |
| 5 | BIR journal hardening (triggers) |
| 6 | BIR Z-readings |
| 7 | BIR journal correction events (destructive rebuild) |
| 8 | Product tax classification |
| 9 | Deployment identity |
| 10 | Terminal registry + pairing invites |
| 11 | LAN remote transport config |
| 12 | Terminal credentials + handshakes |

### Idempotency

| Layer | Status |
| --- | --- |
| Runner ledger | Idempotent — skips applied versions |
| Individual migrations | Mostly **not** idempotent (`CREATE TABLE` without `IF NOT EXISTS`; exception: v11 `IF NOT EXISTS`) |
| Seeding | Idempotent — `INSERT OR IGNORE` |
| v7 correction | Destructive rebuild in one transaction — safe only with ledger |

### Startup behavior risks

- `GetPendingMigrationCount` returns `0` when DB file missing (`MigrationRunner.cs:43-46`) but status check reports full pending count — status UX inconsistency
- Seeding re-executes every startup after migrations (safe via `INSERT OR IGNORE`)

---

## Table/Constraint Inventory

**36 application tables** at migration v12 (plus `schema_migrations`).

### Core POS tables (v1)

`app_metadata`, `roles`, `users`, `role_permissions`, `store_settings`, `categories`, `brands`, `units`, `suppliers`, `products`, `receipt_sequences`, `cashier_sessions`, `sales`, `sale_items`, `payments`, `stock_movements`, `custom_field_definitions`, `custom_field_values`, `print_jobs`, `audit_logs`, `error_logs`, `import_export_jobs`, `backup_logs`

### LAN/identity tables (v9–v12)

`deployment_identity`, `salryn_terminal_registrations`, `salryn_terminal_pairing_invites`, `salryn_lan_transport_config`, `salryn_terminal_credentials`, `salryn_remote_client_handshakes`

### BIR/official tables (v2–v7)

`bir_machine_profiles`, `bir_official_document_sequences`, `bir_electronic_journal_entries`, `bir_electronic_journal_preservation_batches`, `bir_electronic_journal_entry_events`, `bir_z_readings`

### Key constraints

| Constraint | Table | Evidence |
| --- | --- | --- |
| `users.username UNIQUE` | `users` | `InitialSchemaMigration.cs:33` |
| `sales.receipt_no UNIQUE` | `sales` | `InitialSchemaMigration.cs:185` |
| `receipt_sequences UNIQUE (prefix, year)` | `receipt_sequences` | `:157` |
| `idx_products_barcode_active` partial unique | `products(barcode)` where active | `:141-143` |
| `ix_salryn_terminal_credentials_hash` unique | `salryn_terminal_credentials` | `TerminalCredentialHandshakeSchemaMigration.cs:30` |
| BIR journal immutability | 8 triggers block UPDATE/DELETE | `BirElectronicJournalSchemaHardeningMigration.cs` |
| SKU uniqueness | **None** | `products.sku` nullable, no index |

### Foreign keys

Strong on core POS and BIR tables. **LAN tables (v9–v12) have no FK constraints** — intentional flexibility, weaker referential integrity.

### Cascade behavior

SQLite FK enforcement enabled; explicit `ON DELETE CASCADE` not widely used — deletes are application-controlled.

---

## Write-Path and Mutation Review

### HTTP API mutations (POST only)

| Endpoint | Write target | Classification |
| --- | --- | --- |
| `POST /api/users` | `users` + `audit_logs` | Host-local |
| `POST /api/lan-control/remote-transport` | `salryn_lan_transport_config` | Host-local |
| `POST /api/lan-control/.../accept` | `salryn_terminal_pairing_invites` | Host-local |
| `POST /api/lan-control/terminals/*/disable\|revoke` | terminal registry + credentials | Host-local |
| `POST /api/remote-pos/pairing/activate` | terminals + credentials + handshakes | Remote POS |
| `POST /api/remote-pos/shift/*` | `cashier_sessions` | Remote POS |
| `POST /api/remote-pos/checkout/submit` | `sales`, items, payments, stock | Remote POS |

### WinForms / host-command mutations (not HTTP)

Products, inventory, sale void, store settings, full user admin, official checkout (`SqliteOfficialCheckoutPersistenceService`), Z-reading, backup restore, first-run admin bootstrap.

### Dead seam

`IErrorLogWriteRepository` — interface at `WriteRepositoryInterfaces.cs:143`; **no SQLite implementation or factory method**. Error log writes path unclear.

### Hidden mutation endpoints

**None found** beyond documented groups. Read-only bridge GET-only for business data.

---

## Remote POS Persistence Review

### Persistence model (by design)

| State | Storage | Survives restart? |
| --- | --- | --- |
| Runtime gate enabled | In-memory `RemotePosRuntimeGate` | **No** (fail-safe disabled) |
| Join codes / pending pairing | In-memory `RemotePosPairingGateService` | **No** |
| Operator sessions | In-memory 30 min TTL | **No** |
| Carts | In-memory per session | **No** |
| Checkout idempotency cache | In-memory | **No** |
| Terminal registrations | SQLite | **Yes** |
| Terminal credentials (hashed) | SQLite | **Yes** |
| Handshakes | SQLite 15 min TTL | **Yes** if unexpired |
| Pairing invites | SQLite | **Yes** |
| LAN transport enabled | SQLite config | **Yes** |
| Cashier shifts | SQLite | **Yes** |
| Completed sales | SQLite | **Yes** |

Evidence: `RemotePosRuntimeGate.cs:8-9`, `RemotePosOperatorSessionService.cs:100-101`, `LAN_M16_HOST_OWNED_MULTI_TERMINAL_ROLE_RUNTIME.md:177-179`.

### Duplicate checkout protection

1. Required client idempotency key (`RemotePosCheckoutService.cs:312-316`)
2. Per-key lock + in-memory memoization after commit (`:318-341, 483-486`)
3. DB backstop: `sales.receipt_no UNIQUE` + host-owned `RemotePosReceiptNumberAllocator`

**Restart caveat:** Idempotency cache lost; same key after restart could attempt new sale (cart also gone). DB UNIQUE is final guard.

### Admin/owner rejection

`RemotePosOperatorSessionService.cs:104, 225-231` — only Cashier/Manager. Matrix blocks admin actions (`RemotePosAuthorizationMatrix.cs:105-111`). Smoke-enforced.

---

## Numbering and Official Boundary Review

### Four parallel numbering paths

| Path | Format | Allocator | Uses DB sequence table? |
| --- | --- | --- | --- |
| WinForms POS | `POS-{timestamp}-{session}-{guid}` | `CheckoutReceiptNumberBoundary` | No |
| Remote POS | `RPOS-{epoch}-{token}-{seq}` | `RemotePosReceiptNumberAllocator` | No |
| Official BIR | `{prefix}{year}{padded}` | `SharedSqliteOfficialDocumentSequenceAllocationParticipant` | `bir_official_document_sequences` |
| Legacy seed | `SLRN` prefix | **None — table seeded only** | `receipt_sequences` (unused at runtime) |

Evidence: `RemotePosReceiptNumberAllocator.cs:6-14` documents non-official remote scope. Official path host-local only.

### Collision handling

| Scenario | Mitigation |
| --- | --- |
| Remote idempotency retry | In-memory memo + per-key lock |
| Cross-path receipt collision | Distinct prefixes; `sales.receipt_no UNIQUE` |
| Official sequence race | Optimistic `UPDATE ... WHERE current_number = $n` |
| Remote POS restart | New epoch + instance token per process |

### Official boundary (proven)

- `SalrynLanRemoteTransportGate.cs:170-174` — `AllowOfficialCommandsRemotely` always rejected
- Remote POS does not invoke `official.checkout.complete`
- BIR tables writable only via host-local official services

---

## RMO/BIR Readiness Study

**Study only. No compliance claim.**

### What exists (engineering candidate)

| Area | Backend evidence |
| --- | --- |
| Machine profiles | `bir_machine_profiles`, `BirMachineProfileSchemaMigration` |
| Official document sequences | `bir_official_document_sequences`, allocation participant |
| Electronic journal | Tables + append participants + **immutable triggers** |
| Z-reading | `bir_z_readings`, `OfficialZReadingService` |
| X-reading | `OfficialXReadingService` |
| Void/refund/discount permissions | Seeded in `DefaultSeedData` |
| Tax/VAT fields | `ProductTaxClassificationSchemaMigration`, `SalrynOfficialTaxBucketPolicy` |
| Report/output | Official renderers, text-file workflow, evidence packages |
| Audit trail | `audit_logs`, official document audit events |

### What is missing / not validated

| Gap | Notes |
| --- | --- |
| Legal/source-backed compliance matrix | Extensive planning tests exist; no authoritative BIR citation bundle in repo |
| Production printer/PDF/Crystal/ESC-POS | Not implemented |
| Field-level BIR rule completeness | Engineering schemas exist; rule coverage unproven |
| Accreditation claims | Code explicitly disclaims — `OfficialEngineeringEvidencePackageBuilder.cs:37` |

### Certification-risk wording

- **Good:** `NON_CERTIFICATION_FOOTER` in engineering evidence builder
- **Risk:** UI fields named `AccreditationNumber` in official renderers — data fields, not claims, but operator-facing wording must stay disciplined
- **No false legal approval found** in runtime LAN/remote paths

### LAN/remote official access

**Proven absent.** Official tables touched only by host-local WinForms/host-command paths.

### RMO-A1 later verdict basis

Engineering foundation is substantial; legal validation and operator certification workflow remain future work.

---

## Licensing / Anti-Piracy Readiness Study

**Not implemented. Design study only.**

### Current state

| Integration point | Status |
| --- | --- |
| Startup license guard | Absent |
| Signed token verification | Absent |
| `app_metadata` table | Exists — key/value store, no license schema |
| `deployment_identity` | Exists — store/host/terminal identity |
| Offline activation | Absent |
| Feature gating by license | Absent |
| Private signing key in app | Absent (good) |
| SQLite-only unlock | Absent (good) |
| 16-digit-only unlock | Absent (good) |

### Recommended design (from prior handoff, unchanged)

- 16-digit ID = human identifier only
- Real proof = signed payload (edition, scope, installation ID, expiry)
- App verifies with embedded **public key** only
- Private key never in app/installer/DB
- Offline request/response file activation
- Startup/runtime gate before licensed features
- Revocation via signed update bundles later

### Tamper risks today

- No license gate — full feature surface available after first-run bootstrap
- `app_metadata` could store future activation payload — needs signature verification at read time, not SQLite presence alone

### Feature-gating points (future)

- WinForms `Program.cs` / `SalrynAppServices` startup
- `LocalApiApplicationFactory` API startup
- Official output commands
- Remote transport enable (optional tier gate)

---

## Security Boundary Review

*Re-verified at `6cfda871`. Aligns with BACKEND-A1 and PROMO-M1 evidence.*

| Boundary | Verdict | Key evidence |
| --- | --- | --- |
| Local admin/user mgmt from LAN | **PASS** | `LocalApiLocalAccessGuard.cs:12-23` |
| LAN control local-only | **PASS** | `LocalLanControlAuthorization.cs:12-22` |
| Remote POS pairing required | **PASS** | `RemotePosApiAuthorization.cs:28-46` |
| Admin/Owner remote login blocked | **PASS** | `RemotePosOperatorSessionService.cs:225-231` |
| Remote read-only no business mutation | **PASS** | GET-only endpoints |
| CORS restricted | **PASS** | Vite dev origins only (`LocalApiApplicationFactory.cs:91`) |
| No cookie-only LAN trust | **PASS** | Bridge headers required |
| No remote official path | **PASS** | `SalrynLanRemoteTransportGate.cs:170-174` |
| No wildcard/0.0.0.0 bind | **PASS** | `SalrynHostCommandAuthorityGuard.cs:39-43` |
| No seeded default credentials | **PASS** | `DefaultSeedData` roles only |
| No password/token logging | **PASS** | Only 2 `Console.WriteLine` in `src` (dev path + smoke) |
| No shared SQLite exposure | **PASS** | Single-host SQLite file model |

### Security gaps (non-blocking)

| ID | Severity | Finding |
| --- | --- | --- |
| S1 | Moderate | `RemoteReadOnlyApiService` may return unsanitized errors to remote clients |
| S2 | Low | `RemotePosApiEndpoints` cart/pairing errors lack uniform sanitization |
| S3 | Low | `/health`, `/pairing/status` unauthenticated on LAN bind |
| S4 | Low | Host operator opening app via LAN IP gets remote client shell (`lanEntry.ts:15`) |

**Security blockers: 0**

---

## Bad-Code / Stale-Code Findings

Search: workspace grep (PowerShell `rg` unavailable). Patterns: `TODO|FIXME|HACK|temporary|mock|fake|bypass|password|secret|token|0.0.0.0|wildcard|official|RMO|certification|license|printer|receipt|pdf|crystal|esc-pos|dev-only`.

### Summary classification

| Category | Count scope | Classification |
| --- | --- | --- |
| `TODO`/`FIXME`/`HACK` in `src` | **0** | Clean runtime source |
| `mock`/`fake`/`test-only` in `src` | ~3 comment hits | Harmless — WinForms "do not bypass presenter" notes |
| `password`/`secret`/`token` in `src` | ~35 files | Acceptable — auth, hashing, sanitization guards |
| `official`/`RMO`/`certification` in `src` | ~90 files | Acceptable engineering + explicit non-certification footers |
| `0.0.0.0`/`wildcard` in `src` | 3 files | Acceptable guard/policy text |
| `bypass` in WinForms | Presenter guard comments | Harmless |
| `temporary` UI comments | WinForms forms | Improvement candidate — polish debt |
| `license`/`activation` in `src` | Terminal pairing only | Not product licensing |
| Tests/tools hits | Majority of broad scan | Harmless test/smoke coverage |

### Notable items

| Finding | Class | Risk |
| --- | --- | --- |
| `IErrorLogWriteRepository` dead interface | Stale seam | Low |
| `receipt_sequences` seeded but unused | Stale schema | Medium — DB-A1 |
| WinForms temporary styling comments | Stale comments | Low |
| `TerminalPairingActivationService` exposes `ex.Message` on failure | Improvement candidate | Low–Medium |
| No `TODO` markers in runtime `src` | Acceptable | — |

**Bad-code blockers: 0**

---

## Frontend/Web Shell Review

### Host/client routing

```170:176:src/Salryn.App.Web/src/App.tsx
export default function App() {
  if (isRemoteClientBrowserLocation()) {
    return <RemoteClientApp />;
  }
  return <ConsoleApp />;
}
```

Loopback: `localhost`, `::1`, `127.*` (`utils/lanEntry.ts:1-16`). Remote shell has landing → Remote POS or Read-Only only. No `UsersPage`, `HostLanControlPage`, or host login on LAN.

### Privileged routes on LAN client?

**No** — runtime hostname split + API loopback guards. Same JS bundle serves both modes; security is not separate builds.

**UX caveat:** Host operator using LAN IP (not `127.0.0.1`) sees remote client shell.

### Role permission gating

`security/permissions.ts` — page-level `canAccessPage`, nav filter, `AccessDenied` render, redirect to first allowed page. Backend authoritative via `LocalApiPermissionGuards`.

### Stale dist risk (proven)

| Evidence | Location |
| --- | --- |
| `dist/` gitignored | `.gitignore:40` |
| Serves `{repo}/src/Salryn.App.Web/dist` | `LocalApiApplicationFactory.cs:248` |
| No MSBuild/npm integration | Manual `npm run build` required |
| Documented incident | `LAN_M16_FINAL_ACCEPTANCE...md:29,84-88` |
| Vite content-hashed assets | `dist/index.html` → `index-Cmalk0Yk.js` |
| `index.html` unhashed — cache weak link | `MapFallback` serves static `index.html` |

### UI error exposure

`api/http.ts` throws `ApiError` with `body.error` only. Host pages and Remote POS display raw API messages. No stack traces. Remote read-only shows denial codes (intentional for diagnostics).

---

## WinForms/Desktop Review

### Mutation surfaces (primary host-local)

| Module | Presenter | Backend path |
| --- | --- | --- |
| Checkout | `CheckoutPresenter` | `PosCheckoutApplicationService`, official boundary |
| Users | `UsersAdminPresenter` | `SqliteUserWriteRepository` full CRUD |
| Products | `ProductManagementPresenter` | `SqliteProductWriteRepository` |
| Inventory | Inventory forms | `SqliteInventoryWriteRepository` |
| Backup | Backup UI | `SqliteBackupRecoveryService` |
| Official output | `OfficialDocumentLifecyclePresenter`, readings | Official services |
| Shell | `ShellPresenter` | Permission/module routing |

### Presenter bypass risks

Forms carry comments: "do not bypass presenter" (`CheckoutShellForm.cs:8`, `SalrynShellForm.cs:224`, `SalrynAppServices.cs:24`). Architecture expects `AppServices` consumption. **No evidence of direct repository bypass in reviewed presenters** — pattern is presenter-mediated.

### UI vs backend validation

- USER-M1 web create: client validation + `UserAdminApiService` server validation
- WinForms checkout: `CheckoutPresenter` + trusted re-pricing boundary
- Official checkout: separate `OfficialCheckoutCompletionBoundary` with shift gate

### Host-local assumptions

WinForms is the intended admin/catalog/inventory/official surface. API web console is read-heavy + LAN control + user create MVP.

---

## Backup and Recovery Review

### Backup creation

`SalrynAppServices.CreateLocalDatabaseBackup()` (`SalrynAppServices.cs:368-400`):

1. `SqliteConnectionFactory.ClearAllPools()`
2. `File.Copy` raw `.db` file
3. **No WAL checkpoint** — DB uses WAL mode (`SqlitePragma.cs:13`)

### Restore

`SqliteBackupRecoveryService.RestoreVerifiedBackup()`:

- Requires typed confirmation `RESTORE SALRYN BACKUP`
- Re-verifies backup
- Safety copy `salryn-pre-restore-{timestamp}.db`
- Tests exclusive file lock (fails if app running)
- `File.Copy` overwrites live DB — **full blast radius**

### Verification gap

`CoreTables` = only `roles`, `users`, `store_settings`, `products`, `sales` (`SqliteBackupRecoveryService.cs:12-18`). BIR/LAN/terminal tables not required for "restorable" verdict.

### Operational risks (proven in PROMO-M1 session)

- Stale `Salryn.Api.Local` process locks DLLs during build
- SQLite file lock blocks restore while app running
- WAL copy without checkpoint may miss recent pages

---

## Error/Logging/Diagnostics Review

| Path | Behavior | Risk |
| --- | --- | --- |
| `audit_logs` | Standard + remote read-only bridge + remote POS best-effort | Low |
| `error_logs` | Schema exists; write repo missing | Medium — dead seam |
| Dev console | SQLite path in dev only | Low |
| Remote sanitization | Partial — denials yes, success paths inconsistent | Moderate (S1) |
| Stack traces to remote | None found | Good |
| `SanitizePublicText` | Secret marker redaction when applied | Good |

Remote POS audit failure after commit is swallowed by design — sale durable, audit may be missing.

---

## Test and Smoke Coverage Review

### Tooling

| Tool | Role |
| --- | --- |
| `Salryn.Tools.CoreSmoke` | ~200+ compile/runtime checks — permissions, web, RMO schema, POS stab, USER-M1 |
| `Salryn.Tools.InfrastructureSmoke` | LAN, remote POS, official output, backup, pairing |
| `Salryn.Tools.CheckoutLocalHostSmoke` | Checkout local host integration |
| `Salryn.Tests` | Compile-check library (no xUnit runner) |

### Well-covered high-risk areas

- Remote POS pairing, session, checkout idempotency, admin boundary
- Remote read-only bridge, LAN control local-only
- Role permission matrix (ROLE-M1–M4)
- USER-M1 host-only user creation
- SEC-A1 API/WebView security
- BIR schema/journal hardening compile checks
- Backup recovery compile checks (`PosStabB5BackupRecoveryChecks`)

### Coverage gaps

| Gap | Risk |
| --- | --- |
| Stale frontend dist guard | Operational |
| WAL-safe backup runtime test | High — backup integrity |
| Restore with BIR/LAN table verification | Medium |
| Remote error sanitization runtime | Medium |
| Licensing | N/A — not implemented |
| Full migration idempotency re-run | Low |
| E2E browser automation | Low — manual acceptance relied upon |

---

## Performance and Concurrency Review

### SQLite assumptions

- Single-host, single-writer model
- WAL mode with 5s busy timeout
- No distributed/multi-host sync

### Remote POS concurrency

- `ConcurrentDictionary` for sessions, carts, idempotency
- Per-cart `lock` and per-idempotency-key `Monitor.Enter`
- Single API process singletons

### Receipt sequence contention

- Official: optimistic concurrency on `bir_official_document_sequences`
- Remote: in-process `Interlocked.Increment` + DB UNIQUE backstop
- No runtime consumer of `receipt_sequences` table

### Catalog scale

- API read endpoints paginated (`page`, `pageSize` defaults 25)
- Large catalog not load-tested in this audit

### Backup/restore blocking

- Restore requires app stopped (exclusive lock test)
- Backup clears connection pools but does not pause writers

---

## Deployment and Release Readiness Review

### Current operator workflow

1. `npm run build` in `src/Salryn.App.Web`
2. `dotnet run --project src/Salryn.Api.Local -- --development`
3. Browse `http://127.0.0.1:5299` (host console) or LAN IP (remote client)

### Risks

| Risk | Severity | Evidence |
| --- | --- | --- |
| Stale `dist` | **High** operational | Gitignored; manual build; documented incident |
| Stale process on 5299 | **Medium** | DLL lock during rebuild; old bundle served |
| No build/version stamp | **Medium** | No startup diagnostic for dist age |
| `index.html` browser cache | **Medium** | Unhashed fallback |
| Host uses LAN IP for console | **Low** UX | Gets remote shell |
| No Windows service/installer | **Low** | Dev workflow only |
| CORS dev-only | Low | By design |
| Production cookie `Secure` flag | Low | HTTP localhost |

### Environment flags

- `--development` / `--dev` — dev database path logging
- `SalrynLocalComposition.CreateDevelopment` vs `CreateProduction`
- Remote transport off by default until host enables

---

## Blockers

**None.**

No finding blocks safe continuation into Cleanup Sprint A, DB-A1 integrity mapping, or later RMO/licensing design work.

---

## High-Risk Non-Blockers

| # | Area | Finding | Evidence |
| ---: | --- | --- | --- |
| H1 | Backup | WAL-mode `File.Copy` without checkpoint | `SqlitePragma.cs:13`, `SalrynAppServices.cs:396-397` |
| H2 | Backup/restore | Restore replaces entire DB; verify ignores BIR/LAN tables | `SqliteBackupRecoveryService.cs:12-18, 105-169` |
| H3 | Numbering | `receipt_sequences` seeded but unused; four parallel numbering paths | `DatabaseSeeder.cs:169`, allocators |
| H4 | Numbering | Restore rewinds sequence counters vs existing `sales.receipt_no` | Official sequence participant |
| H5 | Frontend deploy | Stale `dist` serves old UI until rebuild + restart | `.gitignore`, handoff incident |
| H6 | Security | Unsanitized remote read-only success-path errors | `RemoteReadOnlyApiService` |
| H7 | Remote POS | In-memory idempotency lost on restart — retry could re-attempt sale | `RemotePosCheckoutService` |
| H8 | Operations | Stale API process locks binaries and serves stale static assets | PROMO-M1 / debug sessions |

---

## Medium-Risk Non-Blockers

| # | Finding |
| ---: | --- |
| M1 | `IErrorLogWriteRepository` interface without implementation |
| M2 | SKU has no uniqueness constraint |
| M3 | LAN tables lack foreign keys |
| M4 | Remote POS audit post-commit best-effort (sale without audit possible) |
| M5 | Host operator using LAN IP sees remote client shell |
| M6 | `index.html` cache without `Cache-Control: no-cache` |
| M7 | Migrations not individually idempotent (except partial v11) |
| M8 | API user admin create-only vs WinForms full admin |
| M9 | No automated browser/E2E regression |
| M10 | `GetPendingMigrationCount` vs status inconsistency when no DB file |

---

## Low-Risk Cleanup

- Uniform `SanitizePublicText` on all remote-facing errors
- Session cookie `Secure` when HTTPS deployed
- Startup log for web static root + dist mtime stamp
- WinForms temporary UI comment cleanup
- Rate limit `/pairing/request` and `/health` on LAN bind
- Document host console URL must be `127.0.0.1:5299`
- Split large Remote POS service classes (later refactor)
- Classification script for bad-code scan noise

---

## Recommended Fix Order

Ordered roadmap for Kirch. **No implementation in FULL-SYSTEM-A1.**

### Phase 1 — Cleanup Sprint A (operational hardening)

1. Stale `dist` guard — startup mtime warning or build stamp in `index.html`
2. `Cache-Control: no-cache` on SPA `index.html` fallback
3. Uniform remote error sanitization (S1, S2)
4. Operator runbook: `npm run build` → kill port 5299 → restart API
5. Document host console must use loopback URL

### Phase 2 — DB-A1 (integrity mapping, no redesign)

1. WAL-safe backup procedure (checkpoint or SQLite backup API)
2. Extend backup verify to include BIR + LAN + terminal tables
3. Numbering ownership document — reconcile `receipt_sequences` vs allocators
4. Resolve `IErrorLogWriteRepository` seam
5. Restore sequence-state vs sales integrity test
6. SKU/barcode policy decision

### Phase 3 — BACKEND-A2 (security hardening)

1. Rate limits on pairing/health when LAN bind active
2. Transport-enabled check on `/pairing/request`
3. Remote POS error path sanitization parity

### Phase 4 — RMO-A1 (source-backed study)

1. BIR requirement matrix with authoritative citations
2. Gap analysis vs existing schema/services
3. Explicit non-certification operator wording audit
4. No printer/PDF until study complete

### Phase 5 — LIC-M1 (design spike)

1. Signed activation payload spec
2. `app_metadata` + `deployment_identity` binding model
3. Startup gate integration points
4. Offline activation file workflow

### Phase 6 — Frontend polish (later)

1. Host LAN IP console detection UX
2. Dist build integration into release pipeline
3. Version/build stamp in UI footer

**Do not start:** LAN-M17, QR/discovery/cloud, shared SQLite, remote admin login, wildcard bind, remote official checkout, printer implementation.

---

## GO / NO-GO Verdicts

| Sprint | Verdict | Rationale |
| --- | --- | --- |
| **Cleanup Sprint A** | **GO** | Addresses H5, H6, deployment ops; no schema changes |
| **DB-A1** | **GO** | Integrity mapping needed; BACKEND-A1 + this audit confirm scope |
| **RMO-A1 later** | **GO** (study only) | Engineering foundation exists; legal validation required before implementation claims |
| **LIC-M1 later** | **GO** (design only) | Clean slate; hooks exist; no conflicting implementation |
| **Frontend polish later** | **GO** | Stale dist and host-IP UX are real but non-blocking |

| Item | Verdict |
| --- | --- |
| Promotion | **Not performed** — `KIRCH-SANDBOX` untouched |
| Production/legal/RMO certification | **NO-GO** — not claimed, not ready |
| Remote official checkout | **NO-GO** — correctly absent |
| Shared SQLite / cloud sync | **NO-GO** — out of scope |

---

## Audit Boundaries

This master review:

- Extends `BACKEND_A1_ARCHITECTURE_AND_BAD_CODE_AUDIT.md` and `LAN_M16_FINAL_ACCEPTANCE_AND_BACKEND_REVIEW_PREP.md`
- Is review-only on `KIRCH-FREEHAND` at `6cfda871`
- Makes no code, schema, or migration changes
- Does not promote, reset, or modify `KIRCH-SANDBOX`
- Does not claim RMO/legal/production certification

Kirch remains project lead.

---

## Final Ordered Roadmap (summary)

```
NOW     → Cleanup Sprint A (dist guard, error sanitization, ops runbook)
NEXT    → DB-A1 (WAL backup, numbering integrity, error log seam, restore verify)
THEN    → BACKEND-A2 (LAN bind rate limits, pairing hardening)
LATER   → RMO-A1 study (source-backed matrix, no cert claims)
LATER   → LIC-M1 design spike (signed activation, startup gate)
LATER   → Frontend polish (pipeline, host-IP UX, build stamp)
NEVER   → Remote official checkout, wildcard bind, shared SQLite (without explicit new sprint)
```