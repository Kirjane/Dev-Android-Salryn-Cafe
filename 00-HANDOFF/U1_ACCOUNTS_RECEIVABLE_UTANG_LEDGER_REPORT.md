# Salryn U1 — Utang / Accounts Receivable Ledger

Branch: `KIRCH-U1-ACCOUNTS-RECEIVABLE-UTANG-LEDGER`

Base HEAD: `95c327d12f6ade56affd358b8a19332b442873c2`

Final HEAD: Resolve the commit containing this report with `git rev-parse HEAD`.

Decision: `U1_ACCOUNTS_RECEIVABLE_UTANG_LEDGER_CODE_COMPLETE_MANUAL_QA_PENDING`

## Files changed

U1 changes are limited to the additive SQLite migration and catalog, receivables domain/service/API, controlled checkout and sale-void integration, receipt text, role permissions, React checkout and ledger UI, owner reports, focused smoke coverage, and migration-version guards.

## Database migration

Migration 22 adds `customers`, `accounts_receivable_entries`, and `accounts_receivable_collections`. It adds nullable customer/due-date/note fields plus payment status and balance due to sales. The tables are registered as backup-critical so the schema-based SQLite backup/restore path includes them.

## Checkout math

Cash remains money collected now. `AccountsReceivable` tender completes the sale accounting total without being classified as cash. Full Utang requires zero cash and creates an AR debit for the total. Cash + Utang records the cash payment and debits only the unpaid balance. Customer selection is mandatory whenever the AR amount is positive. Change is zero for credit/partial sales.

Sale, sale items, payments, stock movement, receipt snapshot, and the AR debit use the existing sale SQLite transaction. Failure rolls back the transaction.

## Receipt behavior

New snapshots identify the customer, optional phone, cash paid, Utang amount, balance due, optional due date, cashier, and optional note. Reprint consumes persisted sale evidence and does not write to the AR ledger.

## Utang ledger behavior

The role-aware Utang Ledger page provides totals, due/overdue visibility, customer name/phone search, customer balances, chronological entries, notes, and collection entry.

## Recurring utang behavior

Every credit sale or controlled adjustment appends a debit. Entries never overwrite prior history; each customer balance is accumulated from append-only debit and credit entries.

## Collection behavior

Collections append a collection record and matching AR credit atomically. They record collector and timestamp, reject over-collection, reduce the running balance, and do not create a sale.

## Void/reversal behavior

An unpaid credit-sale void appends an AR `SaleReversal` credit in the same transaction as sale void and stock reversal. A credit sale with a later collection/credit adjustment is blocked from the unpaid-sale void path. Collection void is permission-controlled and appends a `CollectionReversal`; ledger history is not deleted.

## Permissions

Added `Receivables.View`, `Receivables.CreateCreditSale`, `Receivables.Collect`, `Receivables.Adjust`, `Receivables.Export`, and `Receivables.ManageCustomers`. Admin receives all. Manager receives view/create/collect/export/customer management. Cashier receives none by default. U1 adds no remote terminal receivables endpoint or navigation.

## Reports

Owner reports keep credit sales in sales totals while showing credit sales, cash received on sales, AR collections, outstanding receivables, and overdue debit exposure separately. Collections never count as sales.

## Frontend

Host checkout offers Cash, Utang, and Cash + Utang; customer selection and creation, due-date picker, note, and balance preview are shown for credit. The host-only Utang Ledger is permission-aware. The remote terminal shell remains unchanged and cannot navigate to receivables.

## API endpoints

- `GET/POST /api/customers`
- `PATCH /api/customers/{id}`
- `GET /api/receivables/summary`
- `GET /api/receivables/customers`
- `GET /api/receivables/customers/{id}/ledger`
- `POST /api/receivables/collections`
- `POST /api/receivables/adjustments`
- `POST /api/receivables/collections/{id}/void`

All are host-local and permission guarded.

## Gates

- `git diff --check`
- `dotnet build Salryn.sln --no-restore`
- `npm --prefix src\Salryn.App.Web run build`
- focused `u1-accounts-receivable` CoreSmoke
- existing host checkout smoke
- stock movement smoke
- role/page-lock smoke
- LAN shell parity smoke

See the final maintainer report for the exact last-run results.

## Known limitations

- Credit-limit data is stored but U1 does not enforce a limit automatically.
- U1 collections are customer-account credits, not invoice-by-invoice allocation.
- Partially collected credit-sale void is deliberately blocked; a later manager allocation/reversal workflow can extend this safely.
- Terminal Utang is deliberately disabled.
- Visual, installed-app, printer, and real two-machine behavior remain manual proof.

## Manual QA required

- Installed host cash, full Utang, and partial Cash + Utang checkout
- Customer create/select/search and recurring Utang
- Receipt print/reprint content
- Collection, over-collection rejection, unpaid void, and partially collected void rejection
- Admin/Manager/Cashier permission boundaries
- Owner report separation of sales, cash, collections, and outstanding AR
- Backup/restore with real U1 data
- Real terminal pairing and proof that terminal receivables remain unavailable

Installer rebuild required: Yes, if U1 is accepted for packaging. No installer was rebuilt.

Protected refs: `origin/KIRCH-SANDBOX` and `origin/KIRCH-INSTALLER-M1` must remain unchanged.

Promotion: None. This feature branch is not merged, promoted, pushed, packaged, or released by this task.
