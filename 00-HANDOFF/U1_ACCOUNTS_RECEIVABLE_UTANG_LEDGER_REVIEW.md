# U1 Accounts Receivable Ledger Review

Branch: `KIRCH-U1-ACCOUNTS-RECEIVABLE-UTANG-LEDGER`

HEAD: `e9d8472b036659dc9cfbc98cf5889e84b4b2c547`

Decision: `U1_ACCOUNTS_RECEIVABLE_REVIEW_BLOCKED`

Public docs update: Completed in `Kirch-Nairu/Salryn-Point-of-Sale`. README and changelog describe U1 only as an upcoming update under development and manual QA, and explicitly state that it is not in the current installer.

Public docs commit: `5e878edf303f9ad9adec28e7c6af6c277081774c`

## U1 review summary

U1 is substantially wired: migration 22, ledger persistence, host APIs, checkout controls, receipt snapshot fields, ledger UI, owner-report cards, permissions, and navigation all compile and are registered. It is not safe to accept as functionally complete because retry safety and reporting correctness have material defects, and the focused smoke does not execute the actual Utang checkout or authorization paths it claims to cover.

## Functional completeness verdict

Blocked. The happy-path implementation is present, but actual end-to-end Full Utang and Cash + Utang checkout are not runtime-proven. Ambiguous retries can duplicate financial mutations. Overdue and cash-collected reporting can materially misstate money. Manual installed QA alone is not enough; focused automated repairs and regression smokes are required first.

## Stale/placeholder verdict

The core U1 UI is not a placeholder: routes, API calls, selectors, modal, ledger rows, collection button, and reports are wired. However:

- `Receivables.Export` is seeded but has no U1 export endpoint or UI.
- The focused smoke substitutes adjustment debits for actual credit sales and uses source-text presence/absence assertions for checkout atomicity and terminal denial.
- The completed-sale dialog still says `Cash sale committed` for Utang sales.
- Cashier users see Utang controls even though Cashier has no receivables permission by default.

No U1 `TODO`, `FIXME`, `NotImplementedException`, `coming soon`, hard-coded sample customer, or separate WinForms U1 surface was found.

## Database/migration findings

- Migration 22 creates all three required tables and adds nullable/default-safe sales columns.
- Existing cash sales receive `payment_status='Paid'` and `balance_due_cents=0`; no destructive rewrite is performed.
- MigrationRunner records version 22 transactionally, so a failed migration rolls back and an applied migration is not replayed.
- AR tables are registered as critical at version 22.
- Backup/restore copies and verifies the SQLite database as a whole, so U1 tables are included.
- Useful indexes exist for customer name, phone, customer ledger chronology, sale debit uniqueness, and collection-entry uniqueness.
- `%term%` customer search cannot effectively use the name/phone B-tree indexes and all customer/ledger reads are unpaged.

## Checkout/math findings

- Cash math remains supported and the existing host cash checkout smoke passes.
- Full Utang and partial Cash + Utang formulas are implemented in `TrustedPosCheckoutSaleBuilder`.
- AccountsReceivable tender plus cash tender must cover total; partial/full credit change is forced to zero.
- Missing customer ID is rejected when receivable tender is positive.
- Overpayment is supported for Cash and rejected by the Cash + Utang UI rules.
- Sale, items, payments, stock movement, receipt snapshot, and AR debit use the same SQLite sale transaction.
- **P0:** Host `/api/checkout` has no caller request ID or durable idempotency record. Repeating an identical request allocates a new receipt and can create another sale, stock decrement, and AR debit. The durable remote-POS request store does not protect host checkout.
- The customer name and phone printed on the immutable snapshot come from caller-supplied fields rather than a database lookup of `customer_id`.

## Receipt findings

- New checkout snapshots include cash, Utang, balance, due date, cashier, note, customer name, and phone.
- Reprint uses the persisted receipt snapshot and does not write AR entries.
- **P1:** The API accepts `CustomerFullName` and `CustomerPhone` independently from `CustomerId`; a direct authorized request can persist a receipt naming a different customer than the ledger debit.
- The completed-sale dialog says `Cash sale committed` for every payment mode.
- No official/BIR output claim was introduced by U1.

## Ledger findings

- Customer balance uses the latest append-only `balance_after_cents`.
- Customer search covers name and phone.
- Ledger detail exposes debit, credit, adjustment, collection, reversal, note, timestamp, and running balance.
- Recurring debits append and accumulate in the focused service smoke.
- **P1:** Oldest due date is computed from any historical debit whose balance was positive at insertion time. Later collections do not clear or allocate that debit, so a fully paid customer may still show an old overdue date.
- Ledger and customer queries return unbounded result sets.

## Collection findings

- Positive amount, customer, and collector are required; zero/negative and over-collection are rejected.
- Collection and its matching credit entry commit in one SQLite transaction.
- Collector and UTC timestamp are stored.
- Collection does not create a sale.
- **P0:** Collection POST has no idempotency key or caller-supplied unique request identity. Retrying after an ambiguous response can record a second collection and second AR credit.
- Concurrent writers rely on SQLite transaction locking but have no busy retry/user reconciliation contract.

## Void/reversal findings

- Collection void is guarded by `Receivables.Adjust`, marks the original collection voided, and appends a CollectionReversal.
- A second collection void fails because only active non-voided collections are selected.
- Unpaid credit-sale void appends SaleReversal and participates in the same sale-void/stock transaction.
- Partially collected credit-sale simple void fails closed.
- No financial history is deleted.
- The partially-collected test is conservative at customer level: any later collection/credit adjustment for that customer blocks void, even when it was economically for another sale.
- Runtime tests do not execute unpaid credit-sale void, partially collected rejection, duplicate void, or stock/AR balance reconciliation together.

## Report findings

- Credit sales remain sales and collections are read separately.
- Outstanding AR uses net debit minus credit and collections do not create another sale.
- **P1:** Due-today and overdue values sum original debit amounts without allocating/subtracting collections or reversals. Paid balances can remain reported as overdue.
- **P1:** `CashCollectedOnSalesCents` sums cash tender amounts but does not subtract sale change. A cash overpayment overstates actual cash retained.
- `Receivables.Export` is unused; U1 export inclusion/exclusion is therefore not implemented.
- Existing sales totals continue to use completed sales and do not add collections.

## Role/authorization findings

- Every U1 customer/receivables endpoint uses a specific permission guard, not authentication-only.
- Admin defaults include all six permissions.
- Manager defaults include view, credit sale, collect, export, and customer management, but not adjustment.
- Cashier defaults include none of the U1 permissions.
- Credit checkout requires both `Sales.Create` and `Receivables.CreateCreditSale`.
- Collection, adjustment, customer management, and collection void are separately permission guarded.
- Unauthenticated requests fail the common permission guard.
- **P2:** Checkout renders Utang controls and starts the receivable-customer query for Cashier even though Cashier lacks permission by default. Backend enforcement remains correct, but the UI advertises actions that will fail.
- Focused U1 runtime coverage does not directly prove unauthenticated, Cashier, Manager, Admin, or non-loopback status codes for the new endpoints.

## Terminal findings

- U1 did not add receivables routes to the remote-POS API group or terminal navigation.
- Host U1 endpoints use the loopback operator guard, so a real non-loopback terminal request is rejected before session permission evaluation.
- Existing LAN shell parity and cash-host checkout gates pass.
- The focused U1 terminal assertion only searches one source file for the literal `/api/receivables`; it is not an HTTP authorization test.

## Backup/restore findings

- Database backup is whole-file SQLite backup, and restore replaces/verifies the complete database.
- Version 22 critical-table validation includes customers, entries, and collections.
- No U1 data-loss path was found in source.
- **Proof gap:** No focused backup/restore smoke creates U1 rows, restores the package, and verifies balances/history byte-for-byte or row-for-row.

## Frontend UI/UX findings

- Payment mode selector, customer selector, add-customer modal, due-date input, note, and balance preview are wired.
- Ledger page has loading and empty states, customer search, summary cards, customer selection, history, and collection.
- **P2:** Payment panel still labels zero change as `Insufficient cash` during a valid full Utang sale because the indicator only compares cash to subtotal.
- **P2:** Completed dialog always labels the result `Cash sale committed`.
- **P2:** Collection button has no mutation-pending lock, making rapid double-click submission possible in addition to the backend idempotency problem.
- **P2:** Customer-query errors on Checkout and summary/ledger errors on the ledger page are not consistently surfaced.
- **P2:** The ledger has no pagination/virtualization and can become heavy for long-running stores.
- Layout uses responsive grids, but no browser/narrow-screen visual proof was performed.

## Bug bounty findings

- Create AR without customer: builder rejects.
- Customer without full name: service rejects.
- Invalid phone: no format policy exists; field accepts arbitrary trimmed text.
- Negative/zero collection: rejected.
- Over-collection: rejected.
- Duplicate checkout retry: **unsafe; P0**.
- Duplicate collection retry: **unsafe; P0**.
- Void same collection twice: rejected/not found after first void.
- Credit sale void twice: sale status prevents second void.
- Cashier direct calls: backend permission guards appear fail-closed from source; no focused U1 HTTP proof.
- Terminal/non-loopback calls: loopback guard appears fail-closed; no focused U1 HTTP proof.
- Report double counting: collections are not sales, but overdue and cash-collected measures are materially wrong as described above.
- Migration upgrade: additive and transactionally registered; isolated v22 initialization passes.

## Performance/stability findings

- Financial truth is SQLite-backed; no in-memory AR balance store exists.
- Checkout adds one indexed balance lookup and one debit insert to the existing transaction.
- Customer balance/oldest-due queries use correlated subqueries per customer and return every customer.
- Leading-wildcard search and unpaged ledgers will degrade as data grows.
- Collection/adjustment operations have no idempotency or busy-retry/reconciliation protocol.
- No async Monitor/lock hazard was found in U1 code.

## P0 findings

1. Host checkout ambiguous retry can duplicate sale, stock decrement, and AR debit.
2. Collection ambiguous retry/double submission can duplicate collection and AR credit.

## P1 findings

1. Due/overdue calculations and oldest-due display do not account for later credits/reversals.
2. Cash-collected reporting includes tender returned as change.
3. Receipt customer identity can disagree with the ledger customer because snapshot identity is caller supplied.
4. The focused U1 smoke does not execute actual full/partial Utang checkout, authorization, receipts, voids, reports, or backup/restore; functional-completeness proof is insufficient.

## P2 findings

- Cashier sees unauthorized Utang controls.
- Valid Utang shows `Insufficient cash` and completed dialog says `Cash sale committed`.
- Collection UI lacks pending/double-submit protection.
- Error states are incomplete.
- Search and ledger lists are unpaged; leading-wildcard search limits index value.
- Partially-collected sale void policy is over-broad at customer level.
- No phone validation policy or credit-limit enforcement.
- Receivables export permission is unused.
- No focused U1 restore proof.

## P3 findings

- Several U1 React views are compressed into very long JSX lines, reducing maintainability.
- Report/ledger wording could distinguish debit exposure from invoice allocation more clearly.

## Recommended fix order

1. Add durable idempotency/reconciliation for host checkout and collection mutations, then prove concurrent/ambiguous retries.
2. Correct AR aging/allocation calculations and net cash collected after change; add deterministic report fixtures.
3. Resolve receipt customer identity server-side from `customer_id`.
4. Add real HTTP/runtime U1 smokes for full/partial checkout, stock, AR, receipt, permissions, terminal denial, void/reversal, reports, and backup/restore.
5. Gate UI controls by permissions and add pending/error states.
6. Add paging/query improvements and decide/export the U1 report contract.

## Safe-to-defer items

- Phone format validation policy.
- Credit-limit enforcement.
- Invoice-specific collection allocation, provided customer-level allocation is clearly documented.
- UI copy/spacing cleanup after P0/P1 closure.
- Terminal Utang enablement; keeping it disabled is safer.

## Gates run

- `git diff --check` — PASS
- `dotnet build Salryn.sln --no-restore` — PASS, 0 errors; existing warnings only
- `npm --prefix src\Salryn.App.Web run build` — PASS; bundle-size warning only
- `u1-accounts-receivable` — PASS, but limited as documented
- `v2-host-checkout` — PASS for existing cash checkout
- `pos-m6-stock-in-stock-adjustment` — PASS
- `pos-surface1-role-page-lock-store-nav` — PASS
- `pre-beta-e-lan-client-shell-parity` — PASS

## Manual QA still required

- Installed host cash, full Utang, and partial checkout
- Customer creation/selection and receipt content
- Collection and reversal UX
- Role-specific UI and direct API behavior
- Real backup/restore with U1 balances/history
- Owner-report reconciliation against known cash/change/collection fixtures
- Narrow/mobile visual review
- Two-machine terminal cash checkout and U1 denial

Installer rebuild required: Yes, only after P0/P1 fixes are accepted and manual QA passes. The current installer does not include U1.

Protected refs: Must remain unchanged:

- `origin/KIRCH-SANDBOX = 1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`
- `origin/KIRCH-INSTALLER-M1 = 6b9c46676390b8a86a989eca600cb02e406ce865`

Promotion: None. Do not merge, package, release, or promote U1 while this review is blocked.
