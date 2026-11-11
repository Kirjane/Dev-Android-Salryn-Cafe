# Customer Order line integrity — Jane R2 acceptance

Issue: #40. Baseline: `ce554a2c354dce47952345862b3de59418a141de`.
Scope: Customer Orders only. No installer/version/deployment/checkout/licensing/financial posting changes.

## 1. Installed QA1 evidence: READ ONLY

The original QA1 order `CO-202611-0001` has **not** been read from this source-only development environment. Do **not** treat the UI's 0 count or the QA1 PHP 70.00 parent as proof of an empty persisted rowset.

Determine the actual QA1 SQLite database path from the installed runtime configuration. Do not guess, stop the app, reset credentials, run migrations, or alter the installed database. From a verified copy of this repository in PowerShell:

```powershell
py -3 .\tools\qa\customer_order_readonly_probe.py --db "<VERIFIED_QA1_DB_PATH>" --order-number CO-202611-0001
```

This probe uses SQLite `mode=ro`, `PRAGMA query_only=ON` and a consistent read transaction. It prints order/status, persisted child count, quantity/price/line-total cents, reconciled parent total and number of matching creation audit events; it does **not** print customer name or phone or execute writes. Record its stdout and full source commit SHA as evidence.

- `persisted_line_count > 0` and `integrity = RECONCILED`: source list hydration was the display defect. Verify item count/slip against the probe and manager detail endpoint.
- `persisted_line_count = 0`: historical integrity defect; **stop**. Preserve read-only evidence; do not synthesize child rows, fulfill/cancel the order, or repair installed QA1 without separate Engineer authorization.
- `LINE_AMOUNT_MISMATCH`: stop any financial/fulfillment action pending review; do not print.

## 2. Isolated Windows development verification (no QA1 writes)

```powershell
git fetch origin
git switch jane/salryn-customer-order-lines-invariant-r1
git rev-parse HEAD
dotnet build .\tests\Salryn.Tests\Salryn.Tests.csproj -c Release
dotnet run --project .\tools\Salryn.Tools.CoreSmoke\Salryn.Tools.CoreSmoke.csproj -c Release -- --only ceo-orders-quotes
npm --prefix .\src\Salryn.App.Web run test:customer-order-lines
npm --prefix .\src\Salryn.App.Web run build
```

If node modules are absent, run `npm --prefix .\src\Salryn.App.Web ci` first. CoreSmoke's `ceo-orders-quotes` runner creates a temporary `DbPaths.CreateDevelopment(root)` database and cleans it up; it does not select the installed QA1 DB.

## 3. API/database acceptance criteria on the disposable runtime

- **CO-LINE-01:** manager HTTP create 201 one-line order; actual `customer_order_lines` COUNT = 1; list, detail and refreshed list show same line ID/quantity and total.
- **CO-LINE-02:** multi-line order with different products; actual child COUNT = 2; sum of stored line amounts equals parent; filtered and unfiltered list, detail and slip have identical items.
- **CO-LINE-03/04:** missing/`[]` lines, zero quantity, empty product or negative price return 400; overflow rejected by service. Compare parent, child, creation audit, sales, payments, drawer and stock counts before/after; all unchanged.
- **CO-LINE-05:** manager/admin list and detail 200; cashier list/detail/create/status/fulfill 403.
- **CO-LINE-06:** existing Confirmed → Preparing → Ready, verified POS sale fulfillment, terminal state and mismatch protections continue to pass in the existing focused CoreSmoke runner.
- **CO-LINE-07:** after refreshing the browser list, click Slip; UI fetches `GET /api/customer-orders/{id}` and prints every persisted line. Empty/inconsistent detail returns a visible error and Print stays disabled.
- **CO-LINE-08:** synthetic confirmed parent with no children (disposable fixture only) remains unhydrated, marked `Verify line data`, and cannot print. No automatic repair.
- **CO-LINE-09:** branch compare against baseline contains no checkout, AR/AP, licensing, migration, version or installer files.

## 4. Required Windows evidence to attach to PR

Capture exact git SHA, all command exit codes/terminal text, read-only QA1 probe output, disposable SQL counts, API list/detail JSON counts (redact personal fields), screenshot of one-line and multi-line list rows after reload, slip preview and print/PDF, and denied cashier request status. Treat any missing gate as **NOT RUN** rather than PASS. Codex independently reviews the frozen candidate only. No merge or package until Engineer approval.
