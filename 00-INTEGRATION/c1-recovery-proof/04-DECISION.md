# 04 — Engineering Decision & Scope Boundaries

**Mission:** `FORGE-SALRYN-CAFE-CANONICAL-RECOVERY-R0-C1-PROOF`  
**System of Record:** [Kirjane/Dev-Salryn-Cafe#44](https://github.com/Kirjane/Dev-Salryn-Cafe/issues/44)  
**Engineering Verdict:** **`PORT_CANONICAL`**

---

## 1. Verdict Summary: `PORT_CANONICAL`

Canonical Cafe Yza C1 menu management (`src/Salryn.App.Web/src/components/CafeMenuManager.tsx` + `src/api/menuCatalog.ts` at `Kirjane/Salryn-CafeYza@4241ea1ec0a8cebf552d93f25a7ac134d7d4d6f3`) should be **ported cleanly into `apps/cafe-web`** behind a thin typed idempotent adapter (`menuCatalogAdapter.ts`) targeting the current authenticated ASP.NET Core Host (`Kirjane/Dev-Salryn-Cafe@9417f3ad7af7e5f957ae5b6d2adee15d4e8b9ac5`), rather than rewriting C1 from scratch or resurrecting the unguarded legacy SPA root shell.

### Why `PORT_CANONICAL` Is Proven
1. **Backend Compatibility Confirmed:** `LocalMenuCatalogEndpoints.cs` and `SqliteMenuCatalogStore.cs` already implement the full canonical C1 schema and catalog publication pipeline (categories, items, variants, modifier groups/options, item & variant modifier linking, draft vs published `MenuRevision`, SHA-256 content/publication hashes). No C# business logic or SQLite migration changes were needed.
2. **Idempotency & Safety Upgrade Preserved:** Wrapping canonical requests inside `hostRequest`, `useBackofficeOperation`, `withUncertainOutcomeGuard`, explicit `Idempotency-Key` headers, and `{ operationId }` publish payloads solves the canonical frontend's missing idempotency protections while preserving server-side `400`/`401`/`403`/`409` fail-closed guards.
3. **Production Bundle Safety Maintained:** Lazy build gating (`C1_RECOVERY_ROUTE_ENABLED`) guarantees zero recovery leakage into default `/cafe/` production builds (`0` matches in `dist/assets/*.js`), while runtime `/api/test/host-identity` validation guards against accidental execution on non-disposable databases.
4. **All Real-Host Proof Gates Passed (`DEBUG_DEVELOPMENT_LICENSE`):** `54` Phase 1 browser assertions, `21` Phase 2 restart/idempotency/RBAC assertions, `18` SQLite assertions, `29/29` Vitest unit tests (`MOCK_ONLY`), `11/11` `/cafe/` Playwright E2E checks, `74/74` `I3Tests`, `58/58` `I6ATests`, and default `CafeLiveAcceptance` (`7` replay + `10` SQLite assertions) all passed on the current unmodified worktree state.

---

## 2. Explicit Differences from Canonical `CafeMenuManager.tsx`

| Area | Canonical `4241ea1` Behavior | R0 Recovery Proof (`CafeMenuManagerRecovery.tsx`) | Rationale |
|---|---|---|---|
| **API Transport & Idempotency** | Direct `fetch` wrappers in `menuCatalog.ts` | Typed `menuCatalogAdapter.ts` (`hostRequest`, `Idempotency-Key`, `{ operationId }` on `/api/menu/publish`, `BackofficeUnknownOutcomeError`, `withUncertainOutcomeGuard`) | Preserves Candidate Host durable mutation/replay/unknown-outcome fence authority (`Issue #44`) |
| **Styling & Components** | Tailwind utility classes in `src/Salryn.App.Web` | `@salryn/cafe-ui` components/tokens + inline styles in `apps/cafe-web` | Ponytail proportionality: avoids adding Tailwind dependency to `apps/cafe-web` |
| **Variant Modifier Linking** | Item-level modifier link present; variant-level modifier link omitted in canonical UI | Added bounded `SMALL_EXTENSION` UI bound to `PUT /api/menu/variants/{variantId}/modifier-groups` | Closes the backend-supported variant modifier-linking workflow highlighted in Issue #44 |
| **Background Refresh State** | `loadAll` toggled loading state | Keeps `<CafeMenuManagerRecovery />` mounted during refresh once `catalog` is loaded (`loading && !catalog`) | Real Chromium bug found & fixed: prevents wiping success notices and active item selection after save |
| **Empty Catalog vs Error State** | Simple empty check | Distinguishes HTTP failure (`401`/`403`/`5xx` visible error banner) from empty catalog (`catalog.items.length === 0`, accounting for 2 seeded default categories in `DatabaseSeeder.cs`) | Prevents auth/server errors or empty product catalogs from appearing broken |
| **Disposable Host Protection** | None | Checks `/api/test/host-identity` and refuses non-disposable / protected DB paths | Prevents accidental operator/QA execution against protected state |
| **Canonical Create/Toggle Parity** | Create/toggle flows; no item description/cost-price or inline variant/option edit forms | Kept exact create/toggle scope without inventing extra edit forms | Follows bounded R0 scope; optional edit forms deferred to follow-on UI integration if requested |

---

## 3. Known Gaps, Constraints & Out-of-Scope Work

1. **Isolated Route Only (Not Production Navigation):** `/cafe/recovery/c1` is deliberately omitted from default `/cafe/` production builds and navigation until Technical Authority / Forge Maintainer approves promotion and UX placement.
2. **`DEBUG_DEVELOPMENT_LICENSE` Boundary:** Real Host tests ran with `SALRYN_LICENSE_DEVELOPMENT_MODE=1`. Signed Release `Salryn.Cafe` entitlement verification remains `NOT_RUN`.
3. **Hardware & Operator Acceptance:** Windows installer generation (`Salryn-Cafe-Setup.exe`) and physical Windows + installed operator walkthrough (`INSTALLED_OPERATOR_VERIFIED`) remain `NOT_RUN` / `HUMAN_REQUIRED`.
4. **Future Recovery Waves:** Canonical `C2–C11` F&B domains, centralized sync (`#13`), Android cashier (`#14`), and progressive Cafe Yza visual reskin (`assets/brand/*`) remain separate future waves.
5. **Non-Author Authority Boundary:** Writer (`Kirch-Nairu` session agent) cannot self-approve. Promotion or follow-on integration awaits independent Technical Authority / Forge Maintainer acceptance.
