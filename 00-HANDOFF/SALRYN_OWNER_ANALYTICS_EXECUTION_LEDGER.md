# Salryn Owner Analytics Execution Ledger

Date: 2026-11-08 Asia/Manila

Branch: `codex/salryn-owner-analytics-w0-w4`

Stack base: `codex/issue-9-s0-s5-rc` at `1d53a907425549355626b4ba39a104cc9a7a0292`

Tested implementation SHA: `0ff285900af913b6d569514a2b3ded29aec68228`

This lane is Base Salryn POS plus the existing signed U1 Accounts Receivable feature. It does not modify PR #10, main, Café Yza, production LAN exposure, licensing keys, or release artifacts.

## Workstream status

| Workstream | Status | Executable evidence |
|---|---|---|
| W0 trusted money/date contract | PASS | `OwnerAnalyticsW0W3RuntimeChecks`: Manila 23:59/00:01, `[start,end)`, equal prior period, cash less change, Utang flow, void separation, historical cost and no-read-write checks. AR failures render Unavailable/error, not zero. |
| W1 aggregate APIs | PASS | Five parameterized, 366-day-capped loopback endpoints; 100,000-sale overview completed in 1,133 ms on this host. Query plan: `SEARCH sales USING COVERING INDEX idx_sales_sale_date (sale_date>? AND sale_date<?)`. No SLA inferred. |
| W2 owner workspace | PASS (automated) | React production build; Today/Yesterday/7/30/90/month/custom, prior comparison, daily sales/cash/Utang, product/category, void and low-stock views, accessible tabular chart data, date drilldown and browser Back state. Physical visual/touch acceptance remains ordinary operator QA, not a code blocker. |
| W2B AR completeness | PASS | Disposable fixture: 205 customers and 501 ledger entries; final pages returned 5 and 1 rows with exact totals/no `hasMore`. Stable name/id and created/id ordering. Existing U1 runtime and entitlement gates PASS. |
| W3 margin/export | PASS | Historical `sale_items.cost_price_cents` only; fully covered estimate or Unavailable, never net-profit wording. Bounded 50,000-row UTF-8 BOM CSV; `=,+,-,@` escaping verified. PDF deliberately DEFERRED: internal compliance output is not repurposed. |
| W4 counted cash | BLOCKED — POLICY/RECORD MODEL | Both Host and Remote close submit `open.ExpectedCash` as counted cash. Base has no approved cash-in/out/refund drawer ledger; AR cash collections have no cashier-session link. A real count/expected composition/variance approval policy is required before any financial write change. W0–W3 are independent and complete. |

## Metric contract

- Store dates are Asia/Manila calendar dates translated to UTC `[start,end)` boundaries. `asOfUtc`, timezone, current and equal preceding boundaries are returned.
- Sales is completed `sales.total_cents`; voids are separate and are not assumed refunds.
- Cash on sales is Cash tender minus recorded change, never AR collections.
- Utang originated is completed-sale `balance_due_cents` period flow.
- AR collections are period flow by recorded method. Outstanding/aging is an as-of stock using disclosed account-level FIFO allocation.
- Product identity groups by stable product ID and historical item label. Categories are explicitly current classification. Item revenue is a disclosed non-additive ranking base when order discount/additive tax exists.
- Estimated gross margin is completed sale total minus historical item cost snapshots only when every item is covered. It is not net profit.

## Commands and results

| Command | Result |
|---|---|
| `dotnet run --project tools/Salryn.Tools.CoreSmoke/Salryn.Tools.CoreSmoke.csproj -c Release --no-restore -- --only owner-analytics-w0-w3` | PASS; 100k 1,047 ms initial / 1,133 ms exact tested SHA |
| `... --only pos-r1-owner-reports` | PASS |
| `... --only u1-entitlement-gating` | PASS: Base denied, paid Host allowed, Terminal denied |
| `... --only u1-accounts-receivable` | PASS: runtime HTTP/ledger/idempotency/backup-restore |
| `dotnet build Salryn.sln -c Release --no-restore` | PASS, 0 warnings, 0 errors |
| `npm run build` in `src/Salryn.App.Web` | PASS; TypeScript + Vite production build; size advisory only |
| `npm audit --omit=dev --audit-level=high` | PASS, 0 vulnerabilities |
| `dotnet list Salryn.sln package --vulnerable --include-transitive` | PASS, no vulnerable packages |
| `git diff --check` | PASS |

| full `Salryn.Tools.CoreSmoke` entrypoint | Feature checks including owner analytics PASS until independent canonical license proof gate; exit 1: `SALRYN_CANONICAL_LICENSE_PROOF_MANIFEST` / controlled LicensingOps asset unavailable. This is inherited release infrastructure, not changed or bypassed here. |
| full `Salryn.Tools.InfrastructureSmoke` entrypoint | PASS (status): DB initialized, 24 migrations, no pending upgrade |

`dotnet test` is intentionally not represented as executed tests because this repository's test project is a class library.

## W4 decision required from Kirch

Approve, in one written policy, (1) who enters and may correct counted cash; (2) expected cash formula including opening float, cash sale net change, shift-linked cash AR collections, actual refunds, and cash-in/out; (3) whether missing events block close; (4) variance approval threshold and role; and (5) immutable audit/backup behavior. Then add the missing session-linked movement records before changing close behavior. Until then, the present auto-balanced close is a known defect and must not be called a real reconciliation.
