# 02 — C1 Canonical Recovery Real Host Proof & Verification Ledger

**Mission:** `FORGE-SALRYN-CAFE-CANONICAL-RECOVERY-R0-C1-PROOF`  
**Base Candidate SHA:** `9417f3ad7af7e5f957ae5b6d2adee15d4e8b9ac5` (`origin/forge/salryn-operator-recovery-r1`)  
**Proof Branch:** `workstreams/salryn-canonical-c1-recovery-proof`  
**Verification Host Scope:** Isolated worktree `C:\Users\arane\.gemini\antigravity\scratch\Dev-Salryn-Cafe-wt\c1-recovery-proof`  
**External Evidence Artifact Root (outside Git):** `C:\Users\arane\SalrynForgeRecovery\20261123-c1-recovery-proof\run-01`  
**Verification Date:** 2026-11-23 / 2026-11-24 (`Asia/Manila`)

> [!IMPORTANT]
> All mutating Host/SQLite/Chromium runs below executed in Debug mode (`SALRYN_LICENSE_DEVELOPMENT_MODE=1`, labeled `DEBUG_DEVELOPMENT_LICENSE`) against fresh disposable SQLite databases created via `C4ProofDatabase.CreateEmpty`. Protected Cafe installation/data/backup roots and Retail directories were not read or modified. Installed operator acceptance and signed Release licensing remain `NOT_RUN` / `HUMAN_REQUIRED`.

---

## 1. Real Host + Disposable SQLite + Chromium Proof (`--c1-recovery-proof`)

**Classification:** `REAL_HOST_VERIFIED`, `PERSISTENCE_VERIFIED` (`DEBUG_DEVELOPMENT_LICENSE`)  
**Build & Execution Sequence (run on current unmodified worktree code):**
1. Built `apps/cafe-web` with `VITE_ENABLE_C1_RECOVERY=1` (`npm run build` -> exit `0`) so `/cafe/recovery/c1` compiled into the disposable Host static bundle under test.
2. Ran:
   ```powershell
   $env:CAFE_C1_ARTIFACT_ROOT = "C:\Users\arane\SalrynForgeRecovery\20261123-c1-recovery-proof\run-01"
   dotnet run --project tools\Salryn.Tools.CafeLiveAcceptance\Salryn.Tools.CafeLiveAcceptance.csproj -c Debug -- --c1-recovery-proof
   ```
   - **Exit Code:** `0`
   - **Observed Stdout Summary:**
     - `PASS Phase 1 C1 recovery browser (54 assertions)`
     - `PASS Phase 2 C1 restart/idempotency/RBAC browser (21 assertions)`
     - `PASS: C1 canonical recovery real Host + Chromium + 18 SQLite assertions. Root: C:\Users\arane\SalrynForgeRecovery\20261123-c1-recovery-proof\run-01. DEBUG_DEVELOPMENT_LICENSE.`
3. Restored default release-safe `apps/cafe-web/dist` (`VITE_ENABLE_C1_RECOVERY` unset) immediately after the `--c1-recovery-proof` run and verified release bundle isolation (Section 2).

### 1.1 Phase 1 — Canonical C1 Browser Authoring & Draft/Publish Isolation (`54` assertions)
- **Artifact:** `C:\Users\arane\SalrynForgeRecovery\20261123-c1-recovery-proof\run-01\c1-phase1-evidence.json`
- **Verified Behaviors:**
  - Verified `/cafe/recovery/c1` boots cleanly on empty product state (`DatabaseSeeder.cs` seeds 2 default categories `Uncategorized` and `General Merchandise` with `0` products).
  - Created categories, catalog item (`createdProductId: "21c2a723-c0fb-442b-a505-bda2325937af"`), 3 variants (`"Standard"`, `"12oz Hot"`, `"16oz Iced"`), 2 modifier groups (`"Milk Choice"` required 1–2, `"Iced Sweetness"` optional 0–1), modifier options, item-level modifier group link (`1` link), and variant-level modifier group link (`1` link: `ce6f5d06-6a8e-4476-bc6b-1bca9543e21d -> c8b74076-14a0-4540-94ec-74bfd53e0bc0`) via `PUT /api/menu/variants/{variantId}/modifier-groups`.
  - Verified post-mutation background refresh keeps `<CafeMenuManagerRecovery />` mounted without clearing active selection or status notices (`loading && !catalog` fix).
  - Published menu revision `40000000-0000-0000-0000-000000000001:1` (`POST /api/menu/publish` with explicit `{ operationId }` and `Idempotency-Key`), then added an unpublished draft item (`"Iced Spanish Latte DRAFT UNPUBLISHED"`, `99900` cents) and verified the `/cafe/` Cashier menu reflects published data only and excludes the unpublished draft (`c1-cashier-published-vs-draft.png`, `111,323` bytes).
  - Verified desktop (`1440px`, `c1-recovery-desktop-1440.png`, `207,150` bytes) and mobile (`390px`, `c1-recovery-mobile-390.png`, `210,526` bytes) layouts.

### 1.2 Phase 2 — Host Restart, Idempotency Fencing & RBAC Negative Proof (`21` assertions)
- **Artifact:** `C:\Users\arane\SalrynForgeRecovery\20261123-c1-recovery-proof\run-01\c1-phase2-evidence.json`
- **Verified Behaviors:**
  - Relaunched ASP.NET Core Host process against the same disposable SQLite DB and verified full catalog/draft/revision persistence across process restart.
  - Verified Host fail-closed security and idempotency contracts:
    - Unauthenticated API call: HTTP `401` (`Auth.AuthenticationRequired`).
    - Missing `Idempotency-Key`: HTTP `400` (`CafeMutation.OperationIdRequired`).
    - Valid mutation: HTTP `200`; duplicate `Idempotency-Key` with identical payload: HTTP `200` with `replayHeader: "true"`; duplicate key with differing payload: HTTP `409` (`ERR_IDEMPOTENCY_CONFLICT`).
  - Unknown-outcome recovery & client fence test: persisted `fencedOpId` (`a936efa3-62a4-479c-a24b-c96d539679e7`) into `localStorage` and executed `page.reload()` so `useBackofficeOperation` hydrated the active `operationId`; Host returned HTTP `409` (`ERR_IDEMPOTENCY_OUTCOME_UNKNOWN`) on retry, and subsequent edited payload `"Changed After Fence"` was blocked client-side by `withUncertainOutcomeGuard` with `0` new HTTP requests (`c1-recovery-unknown-outcome-fence.png`, `116,066` bytes).
  - RBAC enforcement negative test: logged in as Cashier (`c1-cashier`, `CashierRoleId` `00000000-0000-0000-0000-000000000003` possessing `Products.View` without `Products.Manage`) and verified `POST /api/menu/categories` returned HTTP `403` and rendered `"Permission denied."` in UI (`c1-recovery-rbac-403.png`, `99,928` bytes).

### 1.3 SQLite Database Verification (`18` assertions)
- **Artifact:** `C:\Users\arane\SalrynForgeRecovery\20261123-c1-recovery-proof\run-01\c1-sqlite-summary.json`
- **Measured Values:**
  - `publishedRevisionId`: `"40000000-0000-0000-0000-000000000001:1"`
  - `publishedContentHash`: `"39f67ea1a3d226bac3d391a0f4a8f053e0907dc4f5e0b8b7945bd5b3db617c12"`
  - `publishedPublicationHash`: `"0fd63f7667e32e09c05d19a5fa52b15841a2b22cd5c116f1491da45c77314565"`
  - `draftItemName`: `"Iced Spanish Latte DRAFT UNPUBLISHED"`
  - `draftItemPriceCents`: `99900`
  - `idempotencyRecordCount`: `23`
  - `sqliteAssertions`: `18`
  - `licenseMode`: `"DEBUG_DEVELOPMENT_LICENSE (SALRYN_LICENSE_DEVELOPMENT_MODE=1; not Release entitlement proof)"`

---

## 2. Release-Safe Bundle Isolation & Regression Matrix

All checks below ran cleanly after restoring default `apps/cafe-web` build settings (`VITE_ENABLE_C1_RECOVERY` unset) with zero subsequent source edits:

| Check | Command | Classification | Observed Result |
|---|---|---|---|
| Default Release-Safe Frontend Build | `npm run build` (`apps/cafe-web`, `VITE_ENABLE_C1_RECOVERY` unset) | `TESTED` | Exit `0`; `dist/assets/index-corjw9wE.js` (`405,095` bytes) |
| Bundle Tree-Shaking Inspection | `Select-String -Path dist\assets\*.js -Pattern "CafeMenuManagerRecovery\|C1 recovery route blocked\|/cafe/recovery/c1" \| Measure-Object` | `CODE_INSPECTED` / `TESTED` | **`0` matches** — `/cafe/recovery/c1` and `CafeMenuManagerRecovery` excluded from default `/cafe/` production assets |
| Frontend Unit & Contract Guards | `npm test` (`apps/cafe-web`) | `MOCK_ONLY` | Exit `0`; `3` files, `29 passed` (`4` `c1Recovery.test.tsx` + `12` `backoffice.test.tsx` + `13` `workstation.test.tsx`) |
| Existing `/cafe/` Playwright E2E | `npm run test:e2e` (`apps/cafe-web`) | `REAL_HOST_VERIFIED` | Exit `0`; `11/11` checks passed (zero mocks, `/cafe/`, `/cafe/central` isolation, `/cafe/backoffice` nav, `320-1440px` responsive, axe-core `0` critical/serious) |
| Backend Cafe I3 Domain Suite | `dotnet test tests\Salryn.Cafe.I3Tests\Salryn.Cafe.I3Tests.csproj -c Release --no-build` | `TESTED` | Exit `0`; `74 passed, 0 failed, 0 skipped` |
| Backend Cafe I6A Domain Suite | `dotnet test tests\Salryn.Cafe.I6ATests\Salryn.Cafe.I6ATests.csproj -c Release --no-build` | `TESTED` | Exit `0`; `58 passed, 0 failed, 0 skipped` |
| Default Cafe Live Acceptance | `dotnet run --project tools\Salryn.Tools.CafeLiveAcceptance\Salryn.Tools.CafeLiveAcceptance.csproj -c Debug --no-build` | `REAL_HOST_VERIFIED` (`DEBUG_DEVELOPMENT_LICENSE`) | Exit `0`; `7` HTTP restart replay/settings assertions + `10` persisted SQLite assertions |

---

## 3. Explicitly `NOT_RUN` / `HUMAN_REQUIRED` Boundaries

| Boundary | Classification | Reason |
|---|---|---|
| Signed Release License Entitlement (`Salryn.Cafe`) | `NOT_RUN` | Out of isolated R0 Debug Host proof scope (`SALRYN_LICENSE_DEVELOPMENT_MODE=0` untested) |
| Windows Installer Packaging & Side-by-Side Client Install | `NOT_RUN` / `HUMAN_REQUIRED` | Installer builds/deployments prohibited in R0 proof slice; requires target hardware |
| Installed Operator Acceptance (`INSTALLED_OPERATOR_VERIFIED`) | `NOT_RUN` / `HUMAN_REQUIRED` | Requires Technical Authority / operator walkthrough on built product after promotion |
| Remaining Canonical Domains (`C2–C11`), Central Sync (`#13`), Android (`#14`), Cafe Reskin | `NOT_RUN` | Outside `R0-C1-PROOF` scope |
