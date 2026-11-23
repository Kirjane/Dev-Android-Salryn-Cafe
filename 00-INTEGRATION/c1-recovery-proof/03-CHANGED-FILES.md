# 03 — Changed Files & Ponytail FULL Review Gate

**Mission:** `FORGE-SALRYN-CAFE-CANONICAL-RECOVERY-R0-C1-PROOF`  
**Branch:** `workstreams/salryn-canonical-c1-recovery-proof`  
**Base Commit:** `9417f3ad7af7e5f957ae5b6d2adee15d4e8b9ac5`

---

## 1. Exact File Inventory

### Tracked Modified Files (`3 files changed, 383 insertions(+), 80 deletions(-)`)
| Path | Purpose | Scope Boundary |
|---|---|---|
| `apps/cafe-web/src/cashier/main.tsx` | Lazily mounts `/cafe/recovery/c1` only when `C1_RECOVERY_ROUTE_ENABLED` (`import.meta.env.DEV \|\| import.meta.env.VITE_ENABLE_C1_RECOVERY === "1"`) | Production `/cafe/` build omits route & component completely (`0` matches in `dist/assets/*.js`) |
| `tools/Salryn.Tools.CafeLiveAcceptance/Program.cs` | Adds `--c1-recovery-proof` Debug runner over disposable `C4ProofDatabase.CreateEmpty`, launching Phase 1/Phase 2 Playwright proof & verifying `18` SQLite assertions | Harness-only; does not mutate production server or installer code |
| `tools/Salryn.Tools.CafeLiveAcceptance/Salryn.Tools.CafeLiveAcceptance.csproj` | Configures build/content copy for `c1-recovery-browser.mjs` | Test-runner tooling only |

### Added Files
| Path | Line Count / Role |
|---|---|
| `apps/cafe-web/src/recovery/menuCatalogAdapter.ts` | `298` lines — Minimal typed Host API adapter for canonical C1 (`Idempotency-Key` header, `{ operationId }` on `POST /api/menu/publish`, `BackofficeUnknownOutcomeError` mapping) |
| `apps/cafe-web/src/recovery/CafeMenuManagerRecovery.tsx` | `1454` lines — Recovered canonical `CafeMenuManager` UI adapted onto `@salryn/cafe-ui` + `useBackofficeOperation("c1-menu-recovery")` + `withUncertainOutcomeGuard`, `/api/test/host-identity` disposable-DB check, background-refresh mount fix, and bounded variant modifier-group linking UI |
| `apps/cafe-web/src/recovery/c1Recovery.test.tsx` | `180` lines — `4` Vitest unit tests (`MOCK_ONLY`) covering route gating, `Idempotency-Key`/`publishMenu`/unknown-outcome mapping, `operationId` retry + fenced payload block, and protected DB path blocking |
| `tools/Salryn.Tools.CafeLiveAcceptance/c1-recovery-browser.mjs` | `377` lines — Real Chromium Phase 1 (`54` assertions) & Phase 2 (`21` assertions) acceptance script |
| `00-INTEGRATION/c1-recovery-proof/00-CUSTODY.md` | Source custody, SHA pins, worktree isolation, Forge capability handshake, and bundle architecture analysis |
| `00-INTEGRATION/c1-recovery-proof/01-C1-CONTRACT-MATRIX.md` | Source-to-target contract comparison matrix across all `11` canonical C1 operations |
| `00-INTEGRATION/c1-recovery-proof/02-C1-REAL-HOST-PROOF.md` | Executed real Host, Chromium, SQLite, bundle-isolation, and regression verification record |
| `00-INTEGRATION/c1-recovery-proof/03-CHANGED-FILES.md` | This manifest and Ponytail FULL simplicity review |
| `00-INTEGRATION/c1-recovery-proof/04-DECISION.md` | Engineering decision (`PORT_CANONICAL`) and deferred boundaries |

### Explicitly Unmodified Critical Areas
- `src/Salryn.App.Web/*` — unmodified (used solely as read-only reference / lockfile-verified dependency restore via `npm ci --ignore-scripts`).
- `src/Salryn.Api.Local/*`, `src/Salryn.Domain/*`, `src/Salryn.Infrastructure/*`, `installer/*` — unmodified; zero backend contract, schema, licensing, or installer rewrites.
- `apps/cafe-web/package.json`, `apps/cafe-web/package-lock.json` — unmodified (`0` packages added).

---

## 2. Ponytail `full` Review Gate

| Ponytail Dimension | Review Finding |
|---|---|
| **Existing implementation reused** | Canonical C1 authoring state and domain workflow from `Kirjane/Salryn-CafeYza@4241ea1ec0a8cebf552d93f25a7ac134d7d4d6f3` (`CafeMenuManager.tsx`, `menuCatalog.ts`); existing `apps/cafe-web` HTTP transport (`hostRequest`, `HostApiError`, `BackofficeUnknownOutcomeError`), operation hooks (`useBackofficeOperation`, `withUncertainOutcomeGuard`), and UI primitives (`@salryn/cafe-ui`); existing ASP.NET Core `LocalMenuCatalogEndpoints.cs`, `SqliteMenuCatalogStore.cs`, `CafeMutationIdempotencyEndpointFilter.cs`, and `C4ProofDatabase.CreateEmpty`. |
| **New lines/files introduced and why** | `apps/cafe-web/src/recovery/{menuCatalogAdapter.ts (298), CafeMenuManagerRecovery.tsx (1454), c1Recovery.test.tsx (180)}`, lazy gate in `apps/cafe-web/src/cashier/main.tsx`, and disposable `--c1-recovery-proof` runner in `Salryn.Tools.CafeLiveAcceptance` (`Program.cs`, `.csproj`, `c1-recovery-browser.mjs (377)`) plus `00-INTEGRATION/c1-recovery-proof/` records. Strictly bounded to proving canonical C1 over the real Candidate Host without altering C# business authority or shipped `/cafe/` UI. |
| **Dependencies added** | **Zero (`0`)** npm dependencies added; **zero (`0`)** NuGet packages added; lockfiles untouched. |
| **Avoidable abstractions rejected** | Rejected introducing Tailwind CSS into `apps/cafe-web` just to copy utility classes from `src/Salryn.App.Web`; rejected launching a second SPA entrypoint; rejected creating new global state containers (Redux/Zustand) or bespoke fetch retry wrappers; rejected altering C# `LocalMenuCatalogEndpoints.cs` to remove `Idempotency-Key` or `{ operationId }` protections. |
| **Duplicate logic eliminated** | Eliminated raw `fetch` error handling from canonical `menuCatalog.ts` by delegating HTTP transport, correlation/idempotency keys, `409` conflict/unknown-outcome mapping, and retry fencing cleanly to `apps/cafe-web`'s existing `hostRequest` + `useBackofficeOperation` + `withUncertainOutcomeGuard`. Reused `C4ProofDatabase.CreateEmpty` and `CafeLiveAcceptance` bootstrap instead of creating another Host harness. |
| **Remaining unnecessary complexity** | `CafeMenuManagerRecovery.tsx` (`1454` lines) retains single-file form state and inline style objects mirroring canonical `CafeMenuManager.tsx` rather than sub-component extraction; accepted for isolated R0 proof so diff fidelity against canonical `CafeMenuManager.tsx` stays transparent during review, before R1/R3 production integration. |
| **Proof that simplification did not remove safeguards** | Verified on real Host + SQLite + Chromium + Vitest: unauthenticated `401`, Cashier RBAC `403` (`Products.Manage` guard -> `"Permission denied."`), `Idempotency-Key` missing `400` (`CafeMutation.OperationIdRequired`), replay `200`, conflict `409` (`ERR_IDEMPOTENCY_CONFLICT`), unknown-outcome fence `409` (`ERR_IDEMPOTENCY_OUTCOME_UNKNOWN` + `0` client requests after payload mutation), disposable DB `/api/test/host-identity` check, default build bundle exclusion (`0` matches in `dist/assets/*.js`), plus `11/11` `/cafe/` Playwright E2E (including axe-core `0` critical/serious violations), `74/74` `I3Tests`, and `58/58` `I6ATests`. |
