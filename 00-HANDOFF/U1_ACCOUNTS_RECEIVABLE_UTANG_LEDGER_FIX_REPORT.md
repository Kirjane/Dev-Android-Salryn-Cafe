# U1 Accounts Receivable / Utang Ledger P0-P1 Fix Report

Branch:
KIRCH-U1-ACCOUNTS-RECEIVABLE-UTANG-LEDGER

Base HEAD:
e9d8472b036659dc9cfbc98cf5889e84b4b2c547

Final HEAD:
The commit containing this report. Use `git rev-parse HEAD` for the immutable SHA.

Decision:
U1_ACCOUNTS_RECEIVABLE_P0_P1_FIXED_MANUAL_QA_PENDING

Review report committed:
PASS — `46f6207` (`Document U1 receivables stability review`)

P0 host checkout retry fixed:
PASS — host checkout now requires a client request ID, records a durable pending request with a stable host receipt number, rejects key reuse for different payloads, and reconciles committed sales after ambiguous failure or restart. Duplicate full Utang and cash retry proof creates one sale, one stock movement, and one AR debit.

P0 collection retry fixed:
PASS — collection requests now carry a stable client request ID and request hash. SQLite immediate transactions plus a unique collector/request index make concurrent/double submission durable and return the original collection result without another collection or AR credit. The frontend retains the pending key across ambiguous transport failure and locks the button while submitting.

P1 AR aging fixed:
PASS — oldest-due, due-today, overdue summary, and owner-report overdue exposure now allocate credits against debits and report remaining unpaid exposure. Runtime proof covers unpaid, partial, fully paid, and sale-reversal states.

P1 net cash reporting fixed:
PASS — cash collected on sales is retained cash (`cash tender - change`). Runtime proof covers an overpaid cash sale and partial Cash + Utang. AR collections remain a separate report value and are not counted as new sales.

P1 receipt identity fixed:
PASS — when `customer_id` is present, the host resolves the active customer from SQLite and supplies authoritative name/phone to the sale and receipt snapshot. Caller-supplied mismatched identity no longer overrides the ledger customer. Missing or inactive customers fail closed.

P1 runtime smoke coverage fixed:
PASS — the focused U1 smoke now executes real full Utang, partial Cash + Utang, host retry, collection retry, stock movement, AR ledger, receipt identity, reversal/aging, reports, authorization, terminal denial, and backup/restore behavior against isolated SQLite databases.

Files changed:
- Host checkout API contracts, durable request store, service composition, endpoint client, and Checkout page request lifecycle.
- Receivable service contracts, SQLite service, local endpoints, receivables client/page submission lifecycle.
- Owner report SQL, database migration catalog/initialization, migrations 23 and 24.
- Focused host/U1 runtime checks and migration-version guards.
- This handoff report and the committed blind-review report.

Database changes:
- Migration 23: `host_checkout_requests`, keyed by operator and client request ID, with stable receipt/result reconciliation state.
- Migration 24: collection `client_request_id` and `request_hash`, plus a unique collector/request index.
- Supported migration version advanced from 22 to 24.

API changes:
- Host checkout accepts `clientRequestId` and returns reconciliation metadata (`clientRequestId`, `duplicate`).
- Collection submission accepts `clientRequestId`.
- Host checkout resolves customer identity through the receivables service before sale construction.

Frontend changes:
- Checkout retains one request ID across ambiguous retry and starts a new key after a confirmed response/new sale.
- Collection retains one request ID across ambiguous retry and disables inputs/action while recording.
- Checkout customer query and Utang validation are permission-aware.

Report changes:
- Cash collected on sales excludes returned change.
- Collections remain separate from sales.
- Outstanding and overdue exposure use remaining ledger balance allocation.

Receipt changes:
- Customer name and phone are authoritative server-side snapshots when a customer is linked.
- Full/partial Utang receipt proof includes the linked customer and balance due.

Tests/smokes:
- `u1-accounts-receivable`: real full/partial Utang, duplicate checkout, duplicate collection, receipt identity, aging/reversal, net-cash reports, authorization matrix, terminal denial, backup/restore.
- `v2-host-checkout`: duplicate host request reconciles to one sale and one stock movement.
- Existing stock, role/page-lock, and LAN shell-parity gates retained.

Gates:
- `git diff --check` — PASS.
- `dotnet build Salryn.sln --no-restore` — PASS, 0 errors; existing nullable/analyzer warnings only.
- `npm --prefix src\Salryn.App.Web run build` — PASS; existing Vite bundle-size warning only.
- `u1-accounts-receivable` — PASS.
- `v2-host-checkout` — PASS.
- `pos-m6-stock-in-stock-adjustment` — PASS.
- `pos-surface1-role-page-lock-store-nav` — PASS.
- `pre-beta-e-lan-client-shell-parity` — PASS.

Remaining P2/P3:
- Checkout still has misleading insufficient-cash/completion wording in some Utang states.
- Receivable query/error states are not consistently surfaced.
- Customer and ledger lists remain unpaged; leading-wildcard search limits index efficiency.
- Partially-collected sale void policy remains broad at customer level.
- Phone validation, credit-limit enforcement, export use, and invoice-specific allocation remain deferred product-policy work.
- Several U1 React views retain long JSX lines and could be decomposed later.

Manual QA still required:
- Installed host launch and role-aware UI verification.
- Full Utang and Cash + Utang checkout using real store/customer data.
- Ambiguous network retry and visible reconciliation behavior.
- Rapid collection double-click and refresh/retry behavior.
- Receipt preview/file/print customer identity and balance-due inspection.
- Fully paid/partial/overdue cards and owner report visual verification.
- Cashier cannot select Utang; manager/admin permission behavior.
- Real LAN terminal cannot access receivable/admin routes.
- Backup/restore operator workflow on a QA installation.

Installer rebuild required:
Yes, if U1 is accepted for packaging. Runtime, web, and database migration code changed. No installer was rebuilt in this task.

Protected refs:
- `origin/KIRCH-SANDBOX` remained `1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`.
- `origin/KIRCH-INSTALLER-M1` remained `6b9c46676390b8a86a989eca600cb02e406ce865`.

Promotion:
None. No merge, push, installer build, release asset, or public release was performed.
