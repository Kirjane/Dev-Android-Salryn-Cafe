# Salryn Owner Analytics Execution Ledger

Date: 2026-11-08 Asia/Manila

Branch: `codex/salryn-owner-analytics-w0-w4`

Stack base: `codex/issue-9-s0-s5-rc` at `1d53a907425549355626b4ba39a104cc9a7a0292`

Tested corrected implementation SHA: `a34e16ee36f49e316dc2e836d710a35c3869af64`

This lane is Base Salryn POS plus the existing signed U1 Accounts Receivable feature. It does not modify PR #10, main, Café Yza, production LAN exposure, licensing keys, or release artifacts.

## Workstream status

| Workstream | Status | Executable evidence |
|---|---|---|
| W0 trusted money/date contract | PASS | `OwnerAnalyticsW0W3RuntimeChecks`: Manila boundaries, flow semantics, void separation, historical cost and no-read-write checks. P1 fixture proves reversed collections restore original FIFO exposure without becoming a second debt. |
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
- `CollectionReversal` reduces net credits, restoring the original exposure, but is excluded from exposure-generating debits. Other undated debits remain valid later/no-due exposure.
- Product identity groups by stable product ID and historical item label. Categories are explicitly current classification. Item revenue is a disclosed non-additive ranking base when order discount/additive tax exists.
- Estimated gross margin is completed sale total minus historical item cost snapshots only when every item is covered. It is not net profit.

## Commands and results

| Command | Result |
|---|---|
| Pre-fix focused run with new P1 fixture | EXPECTED FAIL: aging total/buckets did not equal canonical outstanding; defect captured before SQL repair |
| `dotnet run --project tools/Salryn.Tools.CoreSmoke/Salryn.Tools.CoreSmoke.csproj -c Release --no-build -- --only owner-analytics-w0-w3` | PASS at corrected SHA; P1 reconciliation plus 100k in 1,206 ms |
| `... --only pos-r1-owner-reports` | PASS |
| `... --only u1-entitlement-gating` | PASS: Base denied, paid Host allowed, Terminal denied |
| `... --only u1-accounts-receivable` | PASS: runtime HTTP/ledger/idempotency/backup-restore plus AR aging/export ACL/no-leak matrix |
| `dotnet build Salryn.sln -c Release` | PASS, 0 errors; 12 pre-existing nullable/analyzer warnings |
| `npm run build` in `src/Salryn.App.Web` | PASS; TypeScript + Vite production build; size advisory only |
| `npm audit --omit=dev --audit-level=high` | PASS, 0 vulnerabilities |
| `dotnet list Salryn.sln package --vulnerable --include-transitive` | PASS, no vulnerable packages |
| `git diff --check` | PASS |

| full `Salryn.Tools.CoreSmoke` entrypoint | Feature checks including owner analytics PASS until independent canonical license proof gate; exit 1: `SALRYN_CANONICAL_LICENSE_PROOF_MANIFEST` / controlled LicensingOps asset unavailable. This is inherited release infrastructure, not changed or bypassed here. |
| full `Salryn.Tools.InfrastructureSmoke` entrypoint | PASS (status): DB initialized, 24 migrations, no pending upgrade |

## P1 AR aging correction proof

The old SQL treated a collection reversal twice: it reduced cumulative credits and emitted the same debit as undated exposure. The corrected CTE derives `exposure_debit` only from non-reversal debit rows, retains every legitimate dated or undated exposure debit in FIFO order, and derives `net_credits` as credits less reversal debits.

| Case | Expected remaining |
|---|---:|
| ₱100 overdue sale, full collection, full reversal | ₱100 overdue |
| ₱100 due-today sale, partial collection, reversal | ₱100 due today |
| ₱100 overdue + ₱200 soon, unreversed ₱150 collection | ₱150 soon |
| ₱80 undated adjustment debit, ₱30 adjustment credit | ₱50 later/no due |
| ₱70 sale debit and sale reversal | ₱0 |
| ₱90 overdue sale and unreversed ₱20 collection | ₱70 overdue |
| **Canonical ledger / bucket sum** | **₱470 / ₱470** |

Bucket proof: overdue ₱170, today ₱100, soon ₱150, later/no due ₱50. AR summary, customer balances/oldest due, owner report and analytics agree at the same 2026-11-08 Manila as-of. A repeated reversal is rejected by the immutable unique constraint. Reads do not mutate ledger, sales, payments, stock movements, or audit.

`dotnet test` is intentionally not represented as executed tests because this repository's test project is a class library.

## W4 decision required from Kirch

Approve, in one written policy, (1) who enters and may correct counted cash; (2) expected cash formula including opening float, cash sale net change, shift-linked cash AR collections, actual refunds, and cash-in/out; (3) whether missing events block close; (4) variance approval threshold and role; and (5) immutable audit/backup behavior. Then add the missing session-linked movement records before changing close behavior. Until then, the present auto-balanced close is a known defect and must not be called a real reconciliation.
