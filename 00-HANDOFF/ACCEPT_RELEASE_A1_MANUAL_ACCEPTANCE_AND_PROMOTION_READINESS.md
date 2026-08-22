# ACCEPT-RELEASE-A1 — Manual Acceptance and Promotion Readiness

Acceptance date: 2026-07-07

## Source Stamp

| Item | Value |
| --- | --- |
| Repo | `Kirch-Nairu/Salryn` |
| Local path | `C:\Users\kirch\Salryn` |
| Branch | `KIRCH-FREEHAND` |
| Base HEAD (pre-report) | `6e7bf9a91c8c55dcf8165cced540072c278ca6a2` |
| `origin/KIRCH-SANDBOX` | `93a2f92146aec9973586ea0c066474b6a7894a16` |
| Compare before report | `origin/KIRCH-SANDBOX...origin/KIRCH-FREEHAND` = `0 6` |
| Scope | Acceptance/release-readiness batch. No promotion. No RMO/licensing. No feature implementation. |
| Prior reports | BACKEND-A1, FULL-SYSTEM-A1, CLEANUP-A1, DB-A1, DB-A2, POST-DB-A2-MASTER-A1 |

---

## Remote State

| Ref | SHA |
| --- | --- |
| `origin/KIRCH-SANDBOX` | `93a2f92146aec9973586ea0c066474b6a7894a16` |
| `origin/KIRCH-FREEHAND` (base) | `6e7bf9a91c8c55dcf8165cced540072c278ca6a2` |
| Compare | `0 6` |
| **KIRCH-SANDBOX touched** | **No** |

Freehand stack (6 commits ahead of SANDBOX):

1. BACKEND-A1 review
2. FULL-SYSTEM-A1 review
3. CLEANUP-A1 operational hardening
4. DB-A1 database integrity review
5. DB-A2 database safety hardening (`9744a97`)
6. POST-DB-A2-MASTER-A1 (`6e7bf9a`)

---

## Automated Verification Results

Executed on `KIRCH-FREEHAND` at `6e7bf9a` during ACCEPT-RELEASE-A1.

| Command | Exit | Result |
| --- | --- | --- |
| `git diff --check` | 0 | Passed |
| `dotnet build .\Salryn.sln --no-restore` | 0 | Passed — 0 errors |
| `dotnet test .\Salryn.sln --no-build` | 0 | Passed |
| CoreSmoke | 0 | `SALRYN Core smoke checks passed.` |
| InfrastructureSmoke `lan-m16-rc-b-entry-boundary` | 0 | Passed |
| InfrastructureSmoke `remote-pos-pairing-gate` | 0 | Passed |
| InfrastructureSmoke `remote-pos-final-acceptance` | 0 | Passed |
| InfrastructureSmoke `remote-readonly-lan-local-control` | 0 | Passed |
| `npm install` + `npm run build` | 0 | Passed |
| `git status --short` (pre-report) | 0 | Clean |

**Build stamp observed:** `2026-07-07T12:44:12.613Z` (Phase 1) / `2026-07-07T12:52:08.551Z` (Phase 2 after rebuild)

---

## Host Startup / Web Dist Acceptance

**Verdict: PASS**

API started fresh on `http://127.0.0.1:5299` with `--development`.

### Startup log (`[Salryn Web]`)

```text
[Salryn Web] Static root: C:\Users\kirch\Salryn\src\Salryn.App.Web\dist
[Salryn Web] index.html: exists=True, mtimeUtc=2026-07-07T12:52:08.3527889+00:00, sizeBytes=418
[Salryn Web] JS assets: /assets/index-Cmalk0Yk.js
[Salryn Web] CSS assets: /assets/index-CoAs8o3B.css
[Salryn Web] Build stamp UTC: 2026-07-07T12:52:08.551Z
```

No stale-bundle warning. Dist was rebuilt before API start in this pass.

### HTTP evidence

| Check | Result |
| --- | --- |
| `GET /health` | 200 — `{"status":"ok","service":"Salryn.Api.Local"}` |
| `GET /api/system/web-dist` (loopback) | 200 — full diagnostics JSON; `warnings=[]` |
| `GET /api/system/web-dist` + `X-Salryn-Terminal-Id` | 403 — `LocalApi.LocalOperatorRequired` |
| `GET /` | 200 — `Cache-Control: no-cache, no-store, must-revalidate`; `Pragma: no-cache`; `Expires: 0` |
| `GET /remote-client` | 200 — same no-cache headers (SPA fallback) |
| `GET /assets/index-Cmalk0Yk.js` | 200 — 332,540 bytes |
| `GET /assets/index-CoAs8o3B.css` | 200 — 20,946 bytes |

---

## Host Operator Acceptance

**Verdict: NOT EXECUTED (live browser)**

No interactive browser/WebView session was run in this executor environment.

### HTTP + smoke equivalents (PASS)

| Check | Evidence | Result |
| --- | --- | --- |
| Host console routing | `lan-m16-rc-b-entry-boundary` — loopback → `ConsoleApp`; default page `host-lan-control` | Pass (smoke) |
| Remote client routes hidden on host | Host nav excludes remote-only routes | Pass (smoke) |
| Loopback auth guard | `LocalApiLocalAccessGuard` on login/session/web-dist | Pass (source + HTTP) |
| LAN control local-only | `/api/lan-control` requires local operator | Pass (smoke) |
| Products/inventory/users APIs | CoreSmoke host API regression checks | Pass (smoke) |
| Role AccessDenied | CoreSmoke permission matrix checks | Pass (smoke) |
| Stale UI bundle | Build stamp + no-cache headers + CLEANUP-A1 checks | Pass (automated) |

### Kirch manual step still required

1. Open `http://127.0.0.1:5299` in browser/WebView
2. Ctrl+F5 hard reload
3. Login as host operator (Admin/Owner)
4. Visually confirm sidebar, Products, Inventory, Users, Audit, LAN Control screens

---

## LAN Client Acceptance

**Verdict: NOT EXECUTED (LAN device browser)**

No second LAN device or non-loopback browser session was available in this executor environment.

### Smoke/static equivalents (PASS)

| Check | Evidence | Result |
| --- | --- | --- |
| Remote client shell entry | `isRemoteClientBrowserLocation()` gates `App.tsx` routing | Pass (source) |
| Host console not exposed on LAN URL | `lan-m16-rc-b-entry-boundary` non-loopback → `RemoteClientApp` | Pass (smoke) |
| Remote POS / Read-Only paths visible | `RemoteClientLandingPage` structure | Pass (source) |
| Host admin pages hidden | Host routes not in remote client nav | Pass (smoke) |
| `/api/lan-control` remote blocked | `remote-readonly-lan-local-control` | Pass (smoke) |
| `/api/system/web-dist` remote blocked | Bridge header → 403; non-loopback rejected | Pass (HTTP + smoke) |
| Admin/Owner remote POS rejected | `remote-pos-final-acceptance` manager/admin boundary | Pass (smoke) |
| Cashier/Manager remote POS accepted | Final acceptance session checks | Pass (smoke) |
| Pairing gate required | `remote-pos-pairing-gate` | Pass (smoke) |

### Kirch manual step still required

1. Enable LAN transport on host (loopback LAN Control)
2. Open LAN IP URL from tablet/phone (not `127.0.0.1`)
3. Ctrl+F5; confirm Remote Client landing, pairing flow, no host admin pages

---

## Remote POS Acceptance

**Verdict: PASS (infrastructure smokes)**

Full end-to-end runtime acceptance via `remote-pos-pairing-gate` + `remote-pos-final-acceptance`.

| Step | Result |
| --- | --- |
| Pairing gate (join code, host approve) | Pass |
| One-time pairing code activation | Pass |
| Cashier login | Pass |
| Manager login | Pass |
| Admin/Owner login rejected | Pass |
| Open shift / cart / checkout submit | Pass (smoke runtime) |
| Idempotency within same process | Pass |
| Sale durable in DB | Pass (smoke) |
| No official/BIR path remotely | Pass — source guards + host write boundary |
| RPOS/non-official receipt numbering | Pass — `RemotePosReceiptNumberAllocator` |
| Sanitized remote errors | Pass — CLEANUP-A1 checks |

**Note:** Live pairing/checkout on physical LAN devices not re-tested manually this pass. Smokes are deterministic host-owned runtime tests.

---

## Remote Read-Only Acceptance

**Verdict: PASS (infrastructure smokes)**

| Check | Result |
| --- | --- |
| Authorization required before connect | Pass — `remote-readonly-lan-local-control` |
| Terminal status / read views | Pass — LAN-M15 final acceptance smokes (prior) + local-control |
| No business mutation endpoints | Pass — GET-only bridge guard in final acceptance |
| Audit side-effect acceptable | Pass — documented read attempt audit |
| Sanitized remote errors | Pass — CLEANUP-A1 |
| 403/401 without stack traces | Pass — smoke negative paths |

---

## DB-A2 Backup Acceptance

**Verdict: PASS**

Rehearsal under test path only: `C:\Users\kirch\Salryn\.salryn-accept-a1-check` (deleted after acceptance; Kirch AppData **not touched**).

| Check | Result |
| --- | --- |
| `CreateBackup()` succeeds | Pass — `salryn-backup-20260707-205650.db` |
| Backup file exists | Pass |
| No backup `-wal` sidecar | Pass — online backup API |
| `VerifyBackup()` succeeds | Pass |
| `CriticalTablesExist` | Pass — core + LAN + BIR tables at v12 |
| `MigrationVersion` | 12 |
| `MigrationVersionCompatible` | Pass |
| `IsRestorable` | Pass |
| `backup_logs` write | **INFO** — 0 rows when test DB has no seeded users (`SqliteBackupLogWriter` best-effort skip) |
| No raw WAL-only `File.Copy` backup | Pass — source guards + utility test |

**Production note:** On initialized store DB with seeded users, `backup_logs` writes are expected per DB-A2 design.

---

## DB-A2 Restore Rehearsal

**Verdict: PASS (partial live overwrite — expected headless limitation)**

Test DB only. No restore over Kirch working database.

| Check | Result |
| --- | --- |
| Safety backup before restore | Pass — `salryn-pre-restore-20260707-205650.db` created via online backup |
| Fake `-wal`/`-shm` sidecars created | Pass |
| `RemoveWalShmSidecars` semantics | Pass — mirror test on `sidecar-probe.db` |
| Live restore overwrite | **Blocked** — exclusive lock gate (API/connections hold DB in real runtime; expected in headless rehearsal) |
| Future migration v13 injected | Pass — `HasFutureMigrationVersion=True`, `IsRestorable=False` |
| Migration guard message | Pass — restore rejected for future schema |

**Operator implication:** Restore still requires app stopped / exclusive DB access. Safety backup is created before blocked restore attempt.

---

## Operator Release Pipeline Review

| Item | Current state |
| --- | --- |
| Windows installer | **Absent** |
| Windows service mode | **Absent** |
| Dist bundled in release artifact | **No** — `dist/` gitignored; manual `npm run build` required |
| Stale API on 5299 detection | Documented in CLEANUP-A1 runbook (`Get-NetTCPConnection`) |
| Old process binary lock | Possible — manual kill required |
| Release artifact folder | **Absent** |
| Debug/dev paths exposed | Dev workflow uses `--development`; loopback bind default |
| Operator runbook | Present in `CLEANUP_A1_OPERATIONAL_HARDENING.md` |
| Pre-promotion operator commands | See Manual Commands Used below |

### Recommended operator sequence before promotion

```powershell
cd "C:\Users\kirch\Salryn\src\Salryn.App.Web"
npm run build

cd "C:\Users\kirch\Salryn"
Get-Process Salryn.Api.Local -ErrorAction SilentlyContinue | Stop-Process -Force
$Pids = Get-NetTCPConnection -LocalPort 5299 -State Listen -ErrorAction SilentlyContinue |
  Select-Object -ExpandProperty OwningProcess -Unique
$Pids | ForEach-Object { taskkill /PID $_ /F }

dotnet run --project .\src\Salryn.Api.Local\Salryn.Api.Local.csproj -- --development
# Confirm [Salryn Web] startup lines and build stamp
# Browser: http://127.0.0.1:5299 → Ctrl+F5
```

---

## Promotion Readiness Verdict

**PROMOTE-AFTER-MANUAL-ACCEPTANCE**

### Classification rationale

| Criterion | Status |
| --- | --- |
| All automated gates | **PASS** |
| Host startup / web dist | **PASS** |
| DB-A2 backup creation | **PASS** |
| DB-A2 restore rehearsal (test DB) | **PASS** (with expected exclusive-lock partial) |
| Remote POS smokes | **PASS** |
| Remote read-only smokes | **PASS** |
| LAN security smokes | **PASS** |
| Host operator browser UI | **NOT EXECUTED** — Kirch manual required |
| LAN device browser UI | **NOT EXECUTED** — Kirch manual required |
| Blockers | **0** |
| Uncommitted changes (pre-report) | **Clean** |

**Not PROMOTE-NOW** because Phases 3–4 live browser/LAN acceptance were not executed by this sprint executor, and Kirch explicit approval is required before any SANDBOX promotion.

**Not DO-NOT-PROMOTE** because all automated evidence, DB-A2 rehearsal, and security smokes pass with no regression.

**Not PROMOTE-AFTER-FIX** because no narrow acceptance blocker was found requiring code changes.

---

## Blockers

**0**

---

## High-Risk Non-Blockers

| # | Risk |
| --- | --- |
| 1 | Host/LAN browser manual acceptance not executed this sprint |
| 2 | No production installer/service packaging |
| 3 | Manual `npm run build` still required before API serves fresh SPA |
| 4 | `backup_logs` skip when no users row (test DB only; production DB has users) |
| 5 | Restore requires app stop / exclusive DB access |
| 6 | No BIR/RMO certification or legal production approval |
| 7 | Remote POS idempotency in-memory only (restart caveat) |
| 8 | Licensing not implemented |

---

## Medium-Risk Non-Blockers

| # | Risk |
| --- | --- |
| 1 | Stale API process on 5299 — manual kill documented, not automated |
| 2 | WebView vs browser dual operator paths not unified in packaging |
| 3 | `dist/` gitignored — easy to forget rebuild on frontend changes |
| 4 | `receipt_sequences` ownership still unresolved (product decision) |
| 5 | `IErrorLogWriteRepository` not broadly wired |

---

## Required Fixes Before Promotion

| # | Fix | Owner |
| --- | --- | --- |
| 1 | Kirch executes Phase 3 host browser acceptance (loopback, Ctrl+F5, role screens) | Kirch |
| 2 | Kirch executes Phase 4 LAN device acceptance (LAN IP, remote client shell, pairing) | Kirch |
| 3 | Kirch explicit written approval to promote `KIRCH-FREEHAND` → `KIRCH-SANDBOX` | Kirch |
| 4 | Optional: one live backup from WinForms Backup UI on real store DB to confirm `backup_logs` row | Kirch |

No code fixes required before promotion decision.

---

## Exact Promotion Recommendation

1. Kirch completes manual Phases 3–4 on host + LAN device.
2. If both pass, Kirch may authorize promotion of `KIRCH-FREEHAND` (7 commits after this report) to `KIRCH-SANDBOX`.
3. Promotion is a **separate explicit action** — this sprint does **not** promote.
4. Promotion does **not** imply BIR certification, production legal use, or licensing readiness.

---

## Exact No-Go Claims

| Claim | Status |
| --- | --- |
| RMO / BIR legal certification | **NO-GO** |
| Production legal POS use | **NO-GO** |
| Remote official checkout | **NO-GO** |
| Cloud sync / shared SQLite | **NO-GO** |
| Licensing implementation | **NO-GO** |
| Automatic promotion | **NO-GO** — requires Kirch approval |
| Marketing production-ready | **NO-GO** — installer gap remains |

---

## Files Reviewed

- `00-HANDOFF/POST_DB_A2_MASTER_ACCEPTANCE_RMO_LIC_REVIEW.md`
- `00-HANDOFF/DB_A2_DATABASE_SAFETY_IMPLEMENTATION.md`
- `00-HANDOFF/CLEANUP_A1_OPERATIONAL_HARDENING.md`
- `src/Salryn.Api.Local/LocalApiApplicationFactory.cs`
- `src/Salryn.Api.Local/WebDistDiagnostics.cs`
- `src/Salryn.Api.Local/Auth/LocalApiLocalAccessGuard.cs`
- `src/Salryn.Infrastructure/Data/Services/SqliteBackupRecoveryService.cs`
- `src/Salryn.Infrastructure/Data/SqliteDatabaseBackupUtility.cs`
- `tests/Salryn.Tests/DbA2DatabaseSafetyChecks.cs`
- `tests/Salryn.Tests/CleanupA1OperationalHardeningChecks.cs`
- `tools/Salryn.Tools.InfrastructureSmoke/RemotePosFinalAcceptanceSmokeChecks.cs`
- `tools/Salryn.Tools.InfrastructureSmoke/LanM16RcBEntryBoundarySmokeChecks.cs`

---

## Manual Commands Used

```powershell
# Phase 0
cd "C:\Users\kirch\Salryn"
git fetch origin KIRCH-SANDBOX KIRCH-FREEHAND --prune
git switch KIRCH-FREEHAND
git pull --ff-only origin KIRCH-FREEHAND

# Phase 1
Get-Process Salryn.Api.Local -ErrorAction SilentlyContinue | Stop-Process -Force
git diff --check
dotnet build .\Salryn.sln --no-restore
dotnet test .\Salryn.sln --no-build
dotnet run --no-build --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj
dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- lan-m16-rc-b-entry-boundary
dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- remote-pos-pairing-gate
dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- remote-pos-final-acceptance
dotnet run --no-build --project .\tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj -- remote-readonly-lan-local-control
cd .\src\Salryn.App.Web; npm install; npm run build
cd "C:\Users\kirch\Salryn"

# Phase 2
dotnet run --project .\src\Salryn.Api.Local\Salryn.Api.Local.csproj -- --development
# HTTP probes: /health, /api/system/web-dist, /, /remote-client, /assets/*

# Phase 7-8 (test DB under .salryn-accept-a1-check — removed after acceptance)
# Rehearsal runner exercised CreateBackup, VerifyBackup, sidecar handling, future v13 guard
```

---

## Final State

| Item | Value |
| --- | --- |
| Automated gate | 11/11 PASS |
| Host web dist | PASS |
| DB-A2 backup | PASS |
| DB-A2 restore rehearsal | PASS (test DB; exclusive-lock partial expected) |
| Remote POS / read-only | PASS (smokes) |
| Host/LAN browser manual | **Pending Kirch** |
| Promotion verdict | **PROMOTE-AFTER-MANUAL-ACCEPTANCE** |
| Blockers | 0 |
| `KIRCH-SANDBOX` | Untouched |
| Code changes this sprint | Report only |

---

*ACCEPT-RELEASE-A1 completed. Kirch remains project lead. Promotion requires Kirch explicit approval after manual Phases 3–4.*