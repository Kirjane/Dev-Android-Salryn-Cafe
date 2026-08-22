# KIRCH-SANDBOX Master Production Plan

Date: 2026-06-24
Branch: KIRCH-SANDBOX
Baseline commit studied: 0c9aff6, from origin/KIRCH-J21-5F-FROM-8BB-20260623-230615
Scope of this file: planning only, no app code yet.

## What Salryn Is

Salryn is an offline-first Windows POS for MSME retail operations. It is not just a screen build. The project is becoming a local-first store operating system with:

- cashier checkout
- product and inventory management
- cashier sessions
- sales history
- receipt snapshots and printing
- audit logs
- backup and restore
- future Host/Receiver mode for LAN terminals
- RMO 24-2023 compliance groundwork

The correct product direction is:

1. Start on one affordable Windows PC.
2. Keep the database local and safe.
3. Let the same PC become the Store Host when the store grows.
4. Connect receiver/client terminals to the Store Host over LAN later.
5. Keep internet optional for backup, support, licensing, and updates.

## What Exists Now

The branch has real code, not only planning:

- .NET 10 solution with WinForms, Core, Infrastructure, Host.Local, tests, and smoke tools.
- SQLite schema migrations up to version 5.
- Product, inventory, cashier session, sale write/read, and authentication foundations.
- WinForms shell, login, products, inventory, dashboard, settings, sales history, shift gate, and checkout screens.
- In-process Local Host runtime and dispatch boundary.
- Checkout product lookup through Local Host-backed product lookup.
- RMO machine profile schema, official document sequence schema, electronic journal schema, append-only hardening triggers, and electronic journal append persistence smoke.

Verification performed on this branch:

- `dotnet restore Salryn.sln` passed after NuGet network approval.
- `dotnet build Salryn.sln --no-restore` passed.
- `dotnet test Salryn.sln --no-build` passed, but the test project is compile-check style and has no test framework package.
- `dotnet run --no-build --project tools/Salryn.Tools.CoreSmoke/Salryn.Tools.CoreSmoke.csproj` passed.
- `dotnet run --no-build --project tools/Salryn.Tools.InfrastructureSmoke/Salryn.Tools.InfrastructureSmoke.csproj` reported no initialized dev DB yet.
- `dotnet run --no-build --project tools/Salryn.Tools.CheckoutLocalHostSmoke/Salryn.Tools.CheckoutLocalHostSmoke.csproj -- fake` passed.
- `dotnet run --no-build --project tools/Salryn.Tools.CheckoutLocalHostSmoke/Salryn.Tools.CheckoutLocalHostSmoke.csproj -- database` passed.

Important restore warning:

- `SQLitePCLRaw.lib.e_sqlite3` 2.1.11 is pulled transitively and has a high severity vulnerability warning through Microsoft.Data.Sqlite 10.0.8. This is a release blocker.

## Documents And PDFs Studied

Repo planning and code:

- README
- feature, architecture, database, UI, deployment locks
- RMO compliance folder
- latest J21 and Phase 61-66 handoff documents
- checkout, Local Host, startup, schema, repository, and smoke code

Supplied PDFs:

- System overview diagram
- Project dependency graph
- Startup and runtime flow
- Documentation and phase ecosystem
- Checkout data flow current
- SALRYN Architecture Directional Plan
- Salryn Visual Project Scheme
- RMO No. 24-2023

PDF extraction note:

- The Salryn diagrams extracted usable text.
- `RMO No. 24-2023.pdf` is image-only in this environment. Text extraction produced blank pages. The compliance plan below relies on the repo RMO matrix and must still be checked against the official RMO text by a human/legal/compliance reviewer before any accreditation claim.

## Major Findings

### P0 Release Blockers

1. Checkout sale completion trusts UI/request product snapshots.
   - Current `PosCheckoutApplicationService` builds sale items from request line product name, barcode, unit, selling price, cost price, and track-stock state.
   - Production checkout must re-resolve products from `IProductReadRepository` at completion time.
   - Missing guards: inactive product, missing product, discount not allowed, changed price, changed stock tracking, and trusted cost price.

2. Receipt numbers are random/timestamp based.
   - `SystemCheckoutReceiptNumberBoundary` creates `POS-{timestamp}-{session}-{random}`.
   - This is not the RMO official sequence path and not suitable for official output.
   - Even non-official mode should use a safer sequence boundary to prevent duplicates and simplify support.

3. RMO official output is not wired.
   - Machine profiles, sequence schemas, and electronic journal persistence exist.
   - Checkout does not allocate official numbers, persist official snapshots, append electronic journal entries, render official output, print official output, or create X/Z readings.

4. Server/client mode is not finished.
   - Current Host.Local is in-process and product lookup only.
   - No LAN transport, Store Host process/service, pairing, receiver setup, terminal registry persistence, host diagnostics, or client offline behavior exists.

5. UI is still phase/guardrail shaped.
   - Checkout exposes "Validate Completion Dry Run" and "Prepare Submit Guard" instead of a polished cashier payment flow.
   - Many modules are modal/skeleton/workflow-lite, not a finished POS operator experience.

6. Package vulnerability warning blocks production release.
   - Microsoft.Data.Sqlite dependency path must be updated or pinned through a safe package set.

### P1 Production Gaps

- No receipt renderer or ESC/POS printer adapter is implemented.
- `print_jobs` exists but sale completion does not create a print job.
- `receipt_snapshot` exists but is not populated with official or stable non-official receipt content.
- Void flow exists at repository level but is not complete in UI/application workflow.
- Reprint flow is preview-oriented and not journaled.
- Backup and restore are planned but not implemented as production workflow.
- Error logs and audit logs exist as tables but are not comprehensively written by services.
- Reports/dashboard are read-model oriented but not daily/monthly/PDF/CSV complete.
- First-run setup exists enough for admin bootstrap, but not the full Host/Create Store/Join Store setup from the visual scheme.
- README and lock docs are stale. They still describe earlier phases and "no app code yet" while the branch already has app code and Phase 66/J21 work.

## Production Build Order

### KSB-00: Baseline Hardening And Truth Update

Goal: make the repo honest and safe to continue.

Required work:

- Update README/current phase docs to match J21-5X and Phase 66G8A standing.
- Add a single readiness dashboard document that lists build/test/smoke status.
- Fix or suppress only with proof the SQLite native package vulnerability.
- Add `global.json` or explicit SDK policy if deployment needs pinned .NET behavior.
- Keep KIRCH-SANDBOX separate from main until production slices pass.

Exit checks:

- restore/build/test/CoreSmoke/CheckoutLocalHostSmoke pass.
- no vulnerability warnings unless explicitly accepted with a documented mitigation.

### KSB-01: Checkout Integrity Hardening

Goal: make sale completion service-owned, not UI-owned.

Required work:

- Inject `IProductReadRepository` into `PosCheckoutApplicationService`.
- Re-resolve every product by ID during completion.
- Reject inactive/missing products.
- Use repository product snapshots for sale item name, barcode, unit, selling price, cost price, stock tracking, and discount policy.
- Reject discounts when product does not allow discount.
- Reject null payment entries cleanly.
- Confirm stock movement signs and non-stock item behavior.
- Add smoke checks for tampered UI line price/name/barcode.

Exit checks:

- sale completion cannot be manipulated through UI/request snapshots.
- checkout repository smoke proves product changes are honored at final submit.

### KSB-02: Receipt Numbering And Non-Official Snapshot

Goal: finish safe non-official POS receipt flow before official RMO mode.

Required work:

- Replace random receipt boundary with repository-backed `receipt_sequences` allocation.
- Keep official BIR sequence separate from normal non-official POS receipt sequence.
- Generate stable receipt snapshot at sale completion.
- Persist receipt snapshot in the same sale transaction.
- Create print job after sale commit or inside the appropriate safe transaction boundary.
- Add reprint-ready receipt preview that reads saved snapshot only.

Exit checks:

- no duplicate receipt numbers under repeated checkout.
- sale can complete if printer fails.
- receipt preview and reprint use saved snapshot, not live recomputation.

### KSB-03: Cashier POS UX Finish

Goal: make checkout feel like a real cashier station.

Required work:

- Replace guardrail buttons with one normal flow: scan/search, add line, pay, complete.
- Add keyboard shortcuts: F2 fast add, F3 quantity, F4 price check, F5 new sale, F6 reprint, F9 pay, Esc cancel/back.
- Keep scan box focus after each action.
- Add quantity edit, remove line, clear sale, paid/full/change display.
- Add unknown barcode path to fast add.
- Add completion lock after successful sale and a clear "new sale" reset.

Exit checks:

- cashier can operate without mouse for normal sale.
- no phase/debug wording appears in cashier UI.

### KSB-04: Product And Inventory Completion

Goal: make catalog setup and stock management usable.

Required work:

- Finish product edit detail workflow.
- Add fast add product from checkout with barcode prefilled.
- Add stock-in and stock adjustment screens with required reason.
- Add product CSV import/export with validation preview.
- Add low-stock workflow.
- Write audit logs for create/edit/price/stock changes.

Exit checks:

- a store can load initial products, sell, stock in, adjust, and review stock movements.

### KSB-05: Void, Reprint, Audit, Sales History

Goal: preserve transaction history and accountability.

Required work:

- Add application service for void sale with permission checks and reason.
- Return stock through reversal movements.
- Add sales history details, void, and reprint actions.
- Journal reprint/void events in non-official audit first, then RMO electronic journal when official mode is enabled.
- Prevent direct editing/deleting completed sales.

Exit checks:

- voiding never deletes sales.
- stock reversal is traceable.
- reprint is logged.

### KSB-06: Reports, Shift Close, And Daily Operations

Goal: support owner/accountability workflows.

Required work:

- Close cashier shift with expected cash, counted cash, variance, and notes.
- Daily sales summary.
- Monthly sales summary.
- CSV export.
- PDF export.
- Low-stock report.
- Top products and cashier breakdown.

Exit checks:

- daily operation can be opened, sold, closed, reported, backed up.

### KSB-07: RMO Compliance Spine Integration

Goal: turn existing RMO schemas into a controlled official mode, without overclaiming.

Required work:

- Machine profile setup UI and validation.
- Official sequence setup UI and range/exhaustion checks.
- Official snapshot generator using machine profile, sale data, tax bucket data, sequence allocation, cashier, terminal, and timestamps.
- Atomic official sale path: sale, official number allocation, snapshot persistence, electronic journal anchor, ORIGINAL event, and print job.
- Tax bucket calculator or explicit non-VAT mode policy.
- Training/demo/non-official watermark rules.

Exit checks:

- official output cannot be enabled unless machine profile and sequence are ready.
- official number, saved snapshot, sale, and journal evidence commit or roll back together.

### KSB-08: X/Z Readings And Evidence Pack

Goal: produce compliance review artifacts.

Required work:

- X-reading model and report.
- Z-reading close model and report.
- Accumulated grand totals where required.
- Report range includes receipt/invoice sequence range.
- Electronic journal export/preservation workflow.
- Accreditation evidence pack with sample receipts/invoices, X/Z reports, e-journal sample, backend reports, test setup, version identity, and change log.

Exit checks:

- compliance reviewer can inspect deterministic evidence from a known database.

### KSB-09: Host/Receiver LAN Mode

Goal: finish server/client mode without sharing SQLite over the network.

Required work:

- Store setup wizard: Create New Store Host or Join Existing Store Receiver.
- Persist store, host, terminal, and deployment profile identities.
- Terminal registry table and service.
- Pairing code/QR flow with expiration and device token.
- Host transport, preferably local HTTP on LAN with HTTPS or signed local tokens.
- Manual IP fallback and discovery diagnostics.
- Client app mode that never opens SQLite directly.
- Host endpoints for login, product lookup/search, cashier session open/close, checkout complete, reports, backup health.

Exit checks:

- second Windows PC can complete product lookup and checkout through the Store Host.
- client cannot access database file directly.
- host shutdown and network failure are handled with clear UI messages.

### KSB-10: Deployment And Support Readiness

Goal: make install/update/backup/support safe for real clients.

Required work:

- Installer or publish package.
- `C:\ProgramData\Salryn` production data paths.
- Backup engine with SQLite online backup API and verification.
- Restore workflow with pre-restore backup and admin permission.
- App version/build identity in UI and database.
- Error logging and support bundle export.
- Smoke checklist for client handoff.

Exit checks:

- install, first run, sample sale, print/reprint, void, report, backup, restore, and update are tested.

## Non-Negotiable Rules

- No official RMO claim until the compliance spine is implemented, reviewed, and evidence-packed.
- No client PC opens a shared SQLite database over LAN.
- No sale exists only because a receipt printed.
- No completed sale editing.
- No product hard delete after sale history exists.
- No UI-owned money, stock, receipt number, or official document truth.
- No hidden network/cloud dependency for daily store operation.

## Immediate Next Coding Target

First coding phase after this plan should be KSB-00 and KSB-01 together only:

1. Fix package vulnerability path.
2. Update stale docs enough to prevent confusion.
3. Harden checkout product snapshots by re-reading products in the application service.
4. Add tests/smoke checks for tampered checkout input.

Do not start UI restyling or LAN mode until checkout completion truth is hardened.
