# Salryn Owner Analytics Acceptance Report

Corrected tested implementation: `a34e16ee36f49e316dc2e836d710a35c3869af64` on `codex/salryn-owner-analytics-w0-w4`, stacked from PR #10 head `1d53a907425549355626b4ba39a104cc9a7a0292`.

## Acceptance decision

W0–W3 are code-complete and pass executable acceptance. W4 is intentionally not implemented because the current Base data model cannot calculate or audit honest drawer cash. The feature lane is acceptable for draft review; it is not merged, released, licensed, or distributed.

The full CoreSmoke entrypoint ran through this feature's checks and stopped only at the pre-existing controlled canonical-activation proof manifest gate. No manifest or signing asset is available in this lane, and the gate was not relaxed or fabricated. The focused feature, owner reports, U1 runtime, and U1 entitlement commands pass independently. InfrastructureSmoke status passes.

## Delivered paths

- Reports: owner date controls, equal prior comparison, sales/cash/Utang daily charts, payment mix, top historical products, current-category disclosure, separate void/low-stock, historical-cost margin and authorized AR aging.
- Sales drilldown: KPI/day selections open Sales & Receipts with exact incoming dates; browser Back restores Reports.
- Utang: aging buckets open the authorized ledger; customer and ledger reads are paged with exact totals and visible errors.
- API: `/api/owner-analytics/overview`, `/sales-trend`, `/payment-mix`, `/top-products`, `/ar-aging`, plus guarded bounded sales/AR CSV routes.

## Authorization and privacy

All analytics routes require loopback Host access, authenticated `Reports.View`, and signed Reports entitlement. AR data/export additionally requires `Receivables.View` and signed `ReceivablesUtangLedger`. Runtime HTTP proof directly exercises `/ar-aging` and `/export/ar.csv`: anonymous is 401; Base, Cashier, Reports-only and Terminal contexts are 403 without financial values/PII; signed U1 Host with both permissions is 200. AR PII is absent from general aggregate endpoints.

## Reconciliation proof

Disposable SQLite fixtures cover truly empty periods, completed and void sales, Manila midnight boundaries, cash over-tender/change, full Utang, equal preceding periods, current catalog cost changed away from historical snapshot, formula-prefixed product names, 205 customers, 501 ledger rows and 100,000 additional sales. Overview totals equal the sum of daily buckets; reads leave sale counts unchanged. Existing U1 runtime covers partial/full Utang, replay, collection reversal, receipt identity, aging and restore.

The P1 collection-reversal regression was run against the old SQL first and failed because aging buckets exceeded canonical `SUM(debit_cents-credit_cents)`. After correction, a mixed fixture reconciles at ₱470: overdue ₱170, due today ₱100, due soon ₱150 and legitimate undated exposure ₱50. It covers full and partial collection reversal, differently dated sales with FIFO allocation, undated debit/credit adjustments, sale reversal, unreversed collection and duplicate reversal denial. A reversal now restores the original receivable by reducing net credits but cannot generate a second undated debt. AR summary, customer balances/oldest due, owner report and analytics agree under the same Manila as-of, with no read mutation.

The item/category ranking amount is not presented as additive sales composition because order-level discount and additive tax live on the sale. Stable product IDs use historical item labels; categories are disclosed as current classification. Gross margin is shown only with complete historical item cost coverage and is labeled an estimate, not net profit.

## Query and export limits

Date ranges are capped at 366 store days. Trend output is therefore bounded. Product limits are clamped. CSV stops rather than truncating after 50,000 rows and asks for a smaller range. The 100k fixture used the sale-date covering index and completed overview in 1,133 ms on this Windows host; this is evidence, not a promised SLA. CSV is UTF-8 BOM, quoted, exact-filter scoped and neutralizes leading `=`, `+`, `-`, and `@`.

## Known limits

- AR aging is account-level FIFO exposure, not invoice-level allocation.
- Category grouping uses the current category and can reclassify history; the UI discloses this.
- Operational PDF is deferred because the existing PDF pipeline is a guarded internal compliance artifact, not an approved customer export.
- Product-specific Sales History filtering is not added; authoritative date drilldown and stable-ID ranking are provided.
- W4 is blocked on approved money policy and missing shift-linked movement sources. No synthetic counted cash or refund was added.
- Interactive physical/touch visual QA is not substituted by automation; production React compilation and source/runtime behavior pass.

## Reviewer checklist

1. Review Reports with a signed Host Reports entitlement; separately verify AR is absent without U1/permission.
2. Select a daily bar, confirm Sales & Receipts dates and browser Back.
3. Export a disposable range and confirm spreadsheet displays the escaped `'=Historical Product` test only in disposable QA, never by importing test data into a client database.
4. Do not approve W4 until Kirch answers the five policy questions in the ledger.
5. Keep the stacked PR draft; do not merge or release without exact-SHA approval.
