# ARGON-SALRYN-LPI-PROCURE-TO-PAY-R1 — M0 Codebase Discovery & Architecture Report

**Mission:** `ARGON-SALRYN-LPI-PROCURE-TO-PAY-R1`  
**Repository:** `Kirjane/Salryn`  
**Assignment:** [GitHub Issue #25](https://github.com/Kirjane/Salryn/issues/25)  
**Authoritative Business Specification:** `SALRYN_Logistics_Procurement_Inventory_Development_Brief.pptx` (20 slides) + Issue #25 Full Specification  
**Branch:** `antigravity/argon-salryn-procure-to-pay-r1`  
**Pinned Baseline HEAD:** `5a539f496c00ff8f8136e0c66962e304b04ebbe1`  
**Isolated Worktree:** `C:\Salryn-Argon-LPI`  
**Date:** 2026-11-09  

---

## 1. Executive Summary & Architectural Posture

This document records the complete **M0 Codebase Discovery & Architecture Report** prior to writing any application code for the Salryn Logistics, Procurement & Inventory Management (`LPI` / Procure-to-Pay) feature suite.

### Key Architectural Findings
1. **Strict Worktree & Process Isolation:**
   - Work is strictly confined to the dedicated worktree `C:\Salryn-Argon-LPI` on branch `antigravity/argon-salryn-procure-to-pay-r1` (branched from `5a539f496c00ff8f8136e0c66962e304b04ebbe1`).
   - Codex worktrees (`C:\Salryn`, `C:\Salryn-Integration-I1` for PR #24, `C:\Salryn-W4-Codex`) and the installed host service on port `5299` remain untouched. Disposable local hosts during S5 verification will bind to ephemeral loopback ports (`127.0.0.1:0` or `127.0.0.1:5399+`) against temporary isolated SQLite databases.
2. **Existing Schema & Migration Catalog (`v1`–`v26`):**
   - `SalrynDatabaseMigrationCatalog.SupportedMigrationVersion` (`src/Salryn.Infrastructure/Data/SalrynDatabaseMigrationCatalog.cs:5`) is currently `26` (`25_cash_drawer_reconciliation_schema` and `26_cash_drawer_close_replay_schema`).
   - LPI will introduce **Migration Version 27** (`27_procure_to_pay_schema`) via `ProcureToPaySchemaMigration.cs`, increment `SupportedMigrationVersion` to `27`, and register the critical v27 tables in `SalrynDatabaseMigrationCatalog.CriticalTablesByIntroducingVersion` so `SqliteBackupRecoveryService` and `ControlledBackupPackageService` validate v27 backups while preserving backward restore compatibility for v1–v26 backups.
3. **Catalog & Supplier Truth:**
   - `InitialSchemaMigration.cs` (`v1`) created `suppliers (id, name, contact_number, notes, is_active)` and `units (id, name, abbreviation, allows_decimal_quantity, is_active)`, plus `products.supplier_id`.
   - `SqliteCatalogReadRepository.GetSuppliersAsync` (`src/Salryn.Infrastructure/Data/Repositories/Products/SqliteCatalogReadRepository.cs:79-99`) only reads minimal supplier rows, and no supplier write repository or `/api/suppliers` endpoints exist.
   - Migration v27 will extend `suppliers` via additive columns (`code`, `contact_person`, `email`, `address`, `tin`, `payment_terms_days`, `lead_time_days`, `default_currency`, `created_at`, `updated_at`) and add full Supplier CRUD, UOM catalog queries, and logical stock locations/warehouses.
4. **Atomic Stock Movement & Receiving Boundary:**
   - `products.stock_on_hand_x1000` (`INTEGER`, `1000 = 1.000` unit) and `stock_movements` are the canonical inventory truth (`SqliteInventoryWriteRepository.cs`).
   - Purchase Requisitions (`PR`) and Purchase Orders (`PO`) **never** alter `products.stock_on_hand_x1000` or insert `stock_movements`.
   - Stock increases **only** when a Receiving Report (`RR`) is posted (`AcceptedQtyX1000 > 0`) for stock-tracked items (`PurchaseClassification` in `Resale`, `RawMaterial`, `Consumable` where `track_stock = 1`), inside a **single atomic SQLite transaction** that updates the RR, updates PO line received quantities and PO status (`PartiallyReceived` / `FullyReceived`), increments `products.stock_on_hand_x1000`, updates `product_stock_buckets` (`Available` vs `Quarantine`/`Damaged` for rejected quantities), inserts `stock_movements` (`reference_type = 'RECEIVING_REPORT'`, `reference_id = rr.Id`), and records an `audit_logs` entry.
5. **Financial & Cash Drawer Isolation (`U1` & `W4`):**
   - `U1` (`accounts_receivable_entries`, `accounts_receivable_collections`, `customers`) is strictly Customer AR (Utang).
   - `W4` (`CashDrawerReconciliationService.cs:61-89`) enforces exact coverage between `cash_drawer_events` and POS `sales` / `accounts_receivable_collections` / `cash_drawer_manual_movements`.
   - Supplier Accounts Payable (`supplier_invoices`, `supplier_payable_entries`, `supplier_payments`) is **100% isolated** from `sales`, `payments`, `customers`, `accounts_receivable_*`, and `cash_drawer_events`. Supplier payments record explicit disbursement funding sources (`BankTransfer`, `Check`, `PettyCash`, `ExternalFund`, `GCashBusiness`) and never mutate the POS cashier shift drawer.
6. **Existing Compile/Source Guard Compliance (`RoleM1`–`RoleM4`, `WebB0`, `PosSurface1`):**
   - `RoleM1OperatorRolePermissionUxCompileChecks.cs` and `RoleM2BackendPermissionEnforcementChecks.cs` contain git-diff scope guards (`IsRoleM1OwnedPath`, `IsRoleM2OwnedPath`) that trigger **only** if specific legacy files (`RoleM1` / `RoleM2` owned paths) are modified, OR if static assertions on `DefaultSeedData.cs`, `permissions.ts`, `navigation.ts`, `App.tsx`, `LocalApiPermissionGuards.cs`, or `ReadOnlyEndpointExtensions.cs` are violated.
   - Specifically:
     - `RoleM1` checks `IsRoleM1OwnedPath` against `git diff --name-only origin/KIRCH-SANDBOX...HEAD`. Wait: if `src/Salryn.App.Web/src/security/permissions.ts`, `navigation.ts`, `AppShell.tsx`, or `App.tsx` are modified, `RoleM1` checks that **all** `roleM1ChangedPaths` are in `RoleM1`'s `allowedPaths` (which they are: `permissions.ts`, `navigation.ts`, `AppShell.tsx`, `App.tsx` ARE in `RoleM1`'s `allowedPaths`!).
     - **CRITICAL FINDING in `RoleM2BackendPermissionEnforcementChecks.cs:166-258`:**
       Let's examine `RoleM2BackendPermissionEnforcementChecks.cs`:
       - `GetChangedPaths` runs `git diff --name-only origin/KIRCH-SANDBOX...HEAD`, `git diff --name-only`, `git diff --name-only --cached`, `git ls-files --others --exclude-standard`.
       - Then `roleM2ChangedPaths = changedPaths.Where(IsRoleM2OwnedPath).ToArray()`.
       - And every path in `IsRoleM2OwnedPath` (`LocalApiPermissionGuards.cs`, `ApiAuthExtensions.cs`, `LocalApiApplicationFactory.cs`, `Program.cs`, `ReadOnlyEndpointExtensions.cs`, `RemoteReadOnlyApiEndpoints.cs`, `CoreSmoke/Program.cs`) is **also** in `RoleM2`'s `allowedPaths`!
       - **HOWEVER**, look at lines 90–92 of `RoleM2BackendPermissionEnforcementChecks.cs`:
         - `AssertNoMutationRoutes(readOnlyEndpoints, "ReadOnlyEndpointExtensions.cs")` — `ReadOnlyEndpointExtensions.cs` must NEVER contain `MapPost`, `MapPut`, `MapPatch`, `MapDelete`, `StockAdjustment`, etc.
         - `AssertNoBackendRuntimeExpansion(apiRoot)` — NO `.cs` file in `src/Salryn.Api.Local` may contain the literal strings `"0.0.0.0"`, `"QRCode"`, `"network discovery"`, `"mobile/cloud"`, or `"Salryn.App.WinForms"`!
       - And look at `WebB0ReadOnlyOperatorSurfacesChecks.cs:132-160`:
         - No `.tsx` file in `src/Salryn.App.Web/src/pages` may start with `"Backup"` (except `BackupRestorePage.tsx`), `"Settings"`, `"OfficialOutput"`, or `"Rmo"`.
         - `products.ts`, `inventory.ts`, `sales.ts`, `audit.ts`, `InventoryPage.tsx`, and `AuditPage.tsx` are concatenated into `readOnlyModuleSource` (`lines 80-87`) and asserted to **NEVER** contain `method: "POST"`, `method: "PUT"`, `method: "PATCH"`, `method: "DELETE"`, or tokens `"Void Sale"`, `"Complete Sale"`, `"Adjust Stock"`, `"Add Product"`, `"Deactivate Product"`, `"Reset Password"`, `"checkout"`, `"handoff"`, `"upload"`, `"file-picker"`, `"blob"`, `"printer"`, `"scanner"`, `"photo"`!
         - That is why existing write operations in Salryn use separate API client files (e.g., `productWrite.ts`, `inventoryWrite.ts`, `ar.ts`, `cashDrawer.ts`) and separate page/component files rather than putting `method: "POST"` inside `inventory.ts` or `InventoryPage.tsx`!

---

## 2. Repository & Solution Topology

At pinned commit `5a539f496c00ff8f8136e0c66962e304b04ebbe1`, `Salryn.sln` contains:

| Layer / Project | Path | Responsibility |
|---|---|---|
| `Salryn.Core` | `src/Salryn.Core` | Domain models, enums, repository/service interfaces, `SalrynPermissions`, `Result<T>` primitives |
| `Salryn.Infrastructure` | `src/Salryn.Infrastructure` | SQLite connection factory, migrations (`v1`–`v26`), `DefaultSeedData`, repositories, domain services, backup/restore |
| `Salryn.Composition.Local` | `src/Salryn.Composition.Local` | Dependency composition (`SalrynLocalComposition`) wiring SQLite repositories and services for Local Host |
| `Salryn.Api.Local` | `src/Salryn.Api.Local` | ASP.NET Core Minimal API host (`LocalApiApplicationFactory`) serving `/api/*` endpoints and static React SPA |
| `Salryn.App.Web` | `src/Salryn.App.Web` | React 19 + TypeScript + Vite operator/manager web UI (`AppShell`, pages, API clients, Playwright suites) |
| `Salryn.App.WinForms` | `src/Salryn.App.WinForms` | Desktop WebView2/WinForms host wrapper |
| `Salryn.Tests` | `tests/Salryn.Tests` | Compile-time and runtime verification classes invoked by `CoreSmoke` and xUnit |
| `Salryn.Tools.CoreSmoke` | `tools/Salryn.Tools.CoreSmoke` | Canonical repository smoke runner executing all phase/domain/security/UI/API checks |

---

## 3. Existing Database Schema & Migration Inventory (`v1`–`v26`)

Registered in `DefaultDatabaseInitializationFactory.cs` (`src/Salryn.Infrastructure/Data/Initialization/DefaultDatabaseInitializationFactory.cs:47-75`) and cataloged in `SalrynDatabaseMigrationCatalog.cs` (`src/Salryn.Infrastructure/Data/SalrynDatabaseMigrationCatalog.cs:1-86`):

| Version | Migration Name | Key Tables / Columns Introduced | LPI Relevance |
|---|---|---|---|
| `1` | `001_initial_schema` | `roles`, `users`, `role_permissions`, `categories`, `brands`, `units`, `suppliers`, `products`, `product_barcodes`, `stock_movements`, `sales`, `sale_items`, `payments`, `cashier_shifts`, `store_settings`, `audit_logs`, `error_logs`, `backup_logs` | Core catalog (`suppliers`, `units`, `products`), `stock_movements`, `audit_logs` |
| `2`–`12` | `002`–`012` BIR / RMO / Official output & readings | `bir_*`, `official_*` tables | **Protected** — do not touch |
| `13`–`15` | `13`–`15` Local licensing & terminal registrations | `salryn_local_license_state`, `salryn_terminal_registrations`, `salryn_terminal_pairing_records` | **Protected** — do not touch |
| `16`–`22` | `16`–`22` Receipt snapshots, print jobs, checkout requests, store settings, barcode indexes | `sale_receipt_snapshots`, `sale_print_jobs`, `checkout_request_records`, etc. | Pattern reference for `client_request_id` idempotency |
| `23` | `23_accounts_receivable_ledger_schema` | `customers`, `accounts_receivable_entries`, `accounts_receivable_collections` | Customer AR (`U1`) — **Protected**; structural pattern for Supplier AP (`S3`) |
| `24` | `24_owner_analytics_foundation_schema` | Analytics indices & views | Read-only analytics coexistence |
| `25`–`26` | `25`–`26` Cash drawer reconciliation & close replay | `cash_drawer_sessions`, `cash_drawer_events`, `cash_drawer_manual_movements`, `cash_drawer_reconciliations` | **Protected (`W4`)** — Supplier AP payments must never insert into `cash_drawer_events` |

### Detailed Inspection of Core Tables Relevant to LPI

1. **`suppliers` (`InitialSchemaMigration.cs:135-143`):**
   ```sql
   CREATE TABLE suppliers (
       id TEXT PRIMARY KEY,
       name TEXT NOT NULL UNIQUE,
       contact_number TEXT NULL,
       notes TEXT NULL,
       is_active INTEGER NOT NULL DEFAULT 1
   );
   ```
   - Currently lacks `code`, `contact_person`, `email`, `address`, `tin`, `payment_terms_days`, `lead_time_days`, `created_at`, `updated_at`.
   - Referenced by `products.supplier_id TEXT NULL REFERENCES suppliers(id) ON DELETE SET NULL`.

2. **`units` (`InitialSchemaMigration.cs:126-133`):**
   ```sql
   CREATE TABLE units (
       id TEXT PRIMARY KEY,
       name TEXT NOT NULL UNIQUE,
       abbreviation TEXT NOT NULL UNIQUE,
       allows_decimal_quantity INTEGER NOT NULL DEFAULT 0,
       is_active INTEGER NOT NULL DEFAULT 1
   );
   ```
   - Seeded in `DefaultSeedData.cs:204-209`: `Piece (pc, allows_decimal=0)`, `Box (box, allows_decimal=0)`, `Pack (pack, allows_decimal=0)`, `Kilogram (kg, allows_decimal=1)`, `Liter (L, allows_decimal=1)`.

3. **`products` (`InitialSchemaMigration.cs:145-177`):**
   - Stores `sku`, `barcode`, `name`, `description`, `category_id`, `brand_id`, `unit_id`, `supplier_id`, `cost_price_cents` (`INTEGER`), `selling_price_cents` (`INTEGER`), `tax_type` (`INTEGER`), `tax_rate_bp` (`INTEGER`), `track_stock` (`INTEGER`), `allow_decimal_quantity` (`INTEGER`), `stock_on_hand_x1000` (`INTEGER`), `reorder_level_x1000` (`INTEGER`), `status` (`INTEGER`), `is_deleted` (`INTEGER`).
   - All quantities use `_x1000` fixed-point representation (`1000 = 1.000` unit) and all money values use `_cents` (`100 = ₱1.00`).

4. **`stock_movements` (`InitialSchemaMigration.cs:189-206`):**
   ```sql
   CREATE TABLE stock_movements (
       id TEXT PRIMARY KEY,
       product_id TEXT NOT NULL,
       movement_type INTEGER NOT NULL,
       quantity_change_x1000 INTEGER NOT NULL,
       quantity_before_x1000 INTEGER NOT NULL,
       quantity_after_x1000 INTEGER NOT NULL,
       unit_cost_cents INTEGER NULL,
       reference_type TEXT NULL,
       reference_id TEXT NULL,
       notes TEXT NULL,
       created_by_user_id TEXT NOT NULL,
       created_at TEXT NOT NULL,
       FOREIGN KEY (product_id) REFERENCES products(id) ON DELETE RESTRICT,
       FOREIGN KEY (created_by_user_id) REFERENCES users(id) ON DELETE RESTRICT
   );
   ```
   - `StockMovementType` enum (`src/Salryn.Core/Enums/StockMovementType.cs`):
     - `InitialStock = 1`
     - `PurchaseReceive = 2`
     - `SaleDeduction = 3`
     - `VoidRestore = 4`
     - `ManualAdjustmentIncrease = 5`
     - `ManualAdjustmentDecrease = 6`
     - `DamagedWriteOff = 7`
     - `SupplierReturn = 8`
     - `ReturnRestock = 9`
   - Notice `PurchaseReceive = 2`, `ManualAdjustmentIncrease = 5`, `ManualAdjustmentDecrease = 6`, `DamagedWriteOff = 7`, and `SupplierReturn = 8` **already exist** in `StockMovementType`!
   - When an RR is posted in S2, inserting `movement_type = 2` (`StockMovementType.PurchaseReceive`) with `reference_type = 'RECEIVING_REPORT'` and `reference_id = rr.Id` integrates seamlessly with existing `SqliteInventoryReadRepository.GetRecentMovementsAsync` (`src/Salryn.Infrastructure/Data/Repositories/Inventory/SqliteInventoryReadRepository.cs:96-176`) and `InventoryPage.tsx`!
   - When a Supplier Return (`SRET`) is posted in S2, inserting `movement_type = 8` (`StockMovementType.SupplierReturn`) with negative `quantity_change_x1000`, `reference_type = 'SUPPLIER_RETURN'`, and `reference_id = sret.Id` also integrates directly with `StockMovementType.SupplierReturn`.

---

## 4. Assessment of Existing Capabilities vs. 20-Slide Brief Requirements

### 4.1 Multi-Warehouse / Location Support
- **Current Truth:** Salryn is a single-store Local Host architecture (`store_settings` has a single store profile). There is no multi-branch cloud synchronization.
- **LPI Design:** We will add a local `inventory_locations` table seeded with canonical store-local locations:
  - `MAIN-STORE` (`Main Store / Selling Floor & Backroom`, default `Available` location)
  - `RECEIVING-DOCK` (`Receiving & Inspection Area`)
  - `QUARANTINE-HOLD` (`Quarantine / Quality Hold Area`)
  - `DAMAGED-RETURN` (`Damaged & Supplier Return Staging`)
- We will also track stock status buckets per product/location (`product_stock_buckets`: `Available`, `Reserved`, `Quarantine`, `Damaged`, `InTransit`, `OnHold`). Only `Available` stock in `MAIN-STORE` contributes to `products.stock_on_hand_x1000` (sellable POS stock), while rejected/quarantined/damaged quantities are tracked in `product_stock_buckets` without inflating sellable POS stock.

### 4.2 Item Purchase Classification & Non-Inventory Purchases
- **Slide 4 & Slide 11 Requirement:** Purchases may be classified as:
  1. `Resale` (`Inventory / Resale Item` — stock-tracked, updates `products.stock_on_hand_x1000` on RR acceptance)
  2. `RawMaterial` (`Raw Material` — stock-tracked if linked to a tracked product)
  3. `Consumable` (`Supplies / Consumables` — expensed or tracked depending on product `track_stock` flag; PR/PO lines can also specify non-catalog consumable/service descriptions)
  4. `FixedAsset` (`Equipment / Fixed Asset` — received via RR for inspection/accountability and matched in 3-way AP, but **does not** inflate POS sellable `products.stock_on_hand_x1000`)
  5. `Service` (`Services / Direct Expense` — acknowledged/completed and matched in AP, never touches product stock)

### 4.3 Cost Valuation Policy
- **Current Truth:** `products.cost_price_cents` is a single integer column on `products`, and `stock_movements.unit_cost_cents` records the unit cost of each movement.
- **LPI Design:** When an RR is posted with accepted quantity > 0 for a `Resale` / tracked product, `stock_movements.unit_cost_cents` records the PO/RR unit cost in cents, and we compute & record the moving weighted-average cost (or latest landed unit cost when prior stock <= 0) into `products.cost_price_cents` while preserving `last_purchase_cost_cents` and full historical cost per `receiving_report_lines` and `stock_movements`.

### 4.4 Permissions, Roles & Separation of Duties
- **Guard Test Constraint (`RoleM1`, `RoleM2`, `RoleM4`, `PosSurface1`):**
  - `RoleM1` (`lines 97-126`), `RoleM4` (`lines 231-274`), and `PosSurface1` (`lines 31-38`) parse `DefaultSeedData.cs` by extracting text between `"AdminPermissions"`, `"ManagerPermissions"`, `"CashierPermissions"`, and `"RolePermissions"`.
  - `RoleM4` checks `AssertFrontendBackendPermissionConsistency` (`lines 282-338`) and requires `permissions.ts` to contain `const authenticatedOnlyPages = new Set<AppPage>(["dashboard"])` and `PosSurface1` requires `const cashierPages: readonly AppPage[] = ["checkout", "sales"]`.
  - Therefore:
    1. All existing 28 permissions in `DefaultSeedData.cs`, `SalrynPermissions.cs`, `LocalApiPermissions`, and `permissions.ts` must remain intact.
    2. New LPI permissions will be added to `SalrynPermissions`, `DefaultSeedData.Permissions`, `DefaultSeedData.AdminPermissions`, `DefaultSeedData.ManagerPermissions` (where appropriate for store manager oversight), `LocalApiPermissions`, and `permissions.ts`:
       - `Procurement.View` (`"Procurement.View"`)
       - `Procurement.Request` (`"Procurement.Request"`) — create/submit PR
       - `Procurement.ApproveRequisition` (`"Procurement.ApproveRequisition"`) — approve/reject/revise PR
       - `Procurement.ManagePurchaseOrder` (`"Procurement.ManagePurchaseOrder"`) — manage suppliers, quotes, create/update PO
       - `Procurement.ApprovePurchaseOrder` (`"Procurement.ApprovePurchaseOrder"`) — approve/cancel PO
       - `Receiving.View` (`"Receiving.View"`)
       - `Receiving.Manage` (`"Receiving.Manage"`) — record RR, inspect goods, post accepted stock
       - `Receiving.ApproveException` (`"Receiving.ApproveException"`) — approve overdelivery / variance exceptions
       - `Inventory.ManageReturns` (`"Inventory.ManageReturns"`) — supplier returns (`SRET`) & count adjustments approval
       - `Payables.View` (`"Payables.View"`)
       - `Payables.ManageInvoices` (`"Payables.ManageInvoices"`) — record supplier invoices, run 3-way match
       - `Payables.ApproveVariance` (`"Payables.ApproveVariance"`) — approve flagged 3-way match variance
       - `Payables.RecordPayment` (`"Payables.RecordPayment"`) — record/approve supplier payment
    3. In `DefaultSeedData.cs`, we will keep `AdminPermissions`, `ManagerPermissions`, `CashierPermissions`, and `RolePermissions` in their exact structural order so `ExtractBlock(seedFile, "AdminPermissions", "ManagerPermissions")`, `ExtractBlock(seedFile, "ManagerPermissions", "CashierPermissions")`, and `ExtractBlock(seedFile, "CashierPermissions", "RolePermissions")` in `RoleM1` and `RoleM4` work without a single hiccup!
    4. Additional specialized LPI roles (`Purchasing Officer`, `Receiving Clerk`, `Inventory Officer`, `Accounts Payable Clerk`, `Finance Manager`, `Department Head`) will be seeded in `DefaultSeedData.Roles` and `DefaultSeedData.RolePermissions` after `CashierPermissions`.
    5. **Separation of Duties (Self-Approval Denial):**
       - A user who created/requested a Purchase Requisition (`requested_by_user_id`) **cannot** approve that same PR (`approved_by_user_id != requested_by_user_id`), returning a domain error (`Procurement.SelfApprovalDenied`) and writing an audit log entry on any attempted self-approval.
       - Similarly, PO approval and 3-way match variance override / overdelivery exception approval enforce actor separation or explicit supervised reason + audit logging.

---

## 5. REUSE / EXTEND / ADD / PROTECTED Architecture Matrix

| Area | Classification | Existing Files / Symbols | Planned Action |
|---|---|---|---|
| **POS Checkout, Shifts, Void, Receipts** | **PROTECTED** | `SqliteSalesWriteRepository.cs`, `LocalCheckoutEndpoints.cs`, `CheckoutPage.tsx` | Do not modify. Verify via CoreSmoke. |
| **Customer AR / Utang (`U1`)** | **PROTECTED** | `SqliteAccountsReceivableService.cs`, `LocalAccountsReceivableEndpoints.cs`, `AccountsReceivablePage.tsx` | Do not modify. Supplier AP is completely separate. |
| **Cash Drawer Reconciliation (`W4`)** | **PROTECTED** | `CashDrawerReconciliationService.cs`, `LocalCashDrawerEndpoints.cs`, `CashDrawerPage.tsx` | Do not modify. Supplier payments never write to `cash_drawer_events`. |
| **BIR / RMO / Official Output / Licensing** | **PROTECTED** | `src/Salryn.Infrastructure/Official/*`, `LocalLicenseEndpoints.cs` | Do not modify. |
| **Read-Only Operator Surfaces (`WebB0`, `RoleM2`)** | **PROTECTED** | `ReadOnlyEndpointExtensions.cs`, `RemoteReadOnlyApiEndpoints.cs`, `inventory.ts`, `InventoryPage.tsx`, `AuditPage.tsx` | Do not add mutation routes or forbidden tokens to these files. |
| **Database Migration Catalog & Initialization** | **EXTEND** | `SalrynDatabaseMigrationCatalog.cs`, `DefaultDatabaseInitializationFactory.cs` | Add migration v27 (`ProcureToPaySchemaMigration.cs`) and bump `SupportedMigrationVersion` to `27`. |
| **Permissions & Role Seeding** | **EXTEND** | `SalrynPermissions.cs`, `DefaultSeedData.cs`, `LocalApiPermissionGuards.cs`, `permissions.ts`, `navigation.ts`, `AppShell.tsx`, `App.tsx` | Add LPI permission constants, role seeds, and navigation entries while preserving all `RoleM1`–`RoleM4` & `PosSurface1` invariants. |
| **Stock Movements & Product Stock (`x1000`)** | **REUSE + EXTEND** | `products`, `stock_movements`, `StockMovementType` (`PurchaseReceive=2`, `SupplierReturn=8`) | Atomic RR posting and Supplier Return posting write canonical `stock_movements` rows and update `products.stock_on_hand_x1000`. |
| **Procure-to-Pay Domain & SQLite Service** | **ADD** | `src/Salryn.Core/Models/ProcureToPay/*`, `src/Salryn.Core/Services/IProcureToPayService.cs`, `src/Salryn.Infrastructure/Data/Services/SqliteProcureToPayService.cs` | Full transactional implementation of Suppliers, Locations, Sequences, PR, Quotations, PO, RR, Stock Buckets, Count Adjustments, Supplier Returns, Supplier Invoices, 3-Way Match, AP Ledger/Aging, Supplier Payments, and LPI Reports. |
| **Local Host API Endpoints** | **ADD** | `src/Salryn.Api.Local/LocalProcureToPayEndpoints.cs` | Map `/api/lpi/*` and `/api/suppliers/*` endpoints with `LocalApiPermissionGuards.RequirePermission(...)` and `DisallowTerminalOperatorApi`. |
| **Frontend LPI Workspace & Pages** | **ADD** | `src/Salryn.App.Web/src/api/procureToPay.ts`, `src/Salryn.App.Web/src/pages/ProcureToPayPage.tsx` | High-craft Salryn-native manager workspace covering Suppliers, Requisitions, Purchase Orders, Receiving & QC, Stock Cards/Adjustments/Returns, Payables & 3-Way Match, Payments & Aging, and LPI Reports. |

---

## 6. Proposed Migration v27 Schema (`27_procure_to_pay_schema`)

Migration v27 (`ProcureToPaySchemaMigration.cs`) is forward-only and idempotent:

1. **Additive Columns on `suppliers`:**
   - `code TEXT NULL`, `contact_person TEXT NULL`, `email TEXT NULL`, `address TEXT NULL`, `tin TEXT NULL`, `payment_terms_days INTEGER NOT NULL DEFAULT 30`, `lead_time_days INTEGER NOT NULL DEFAULT 3`, `default_currency TEXT NOT NULL DEFAULT 'PHP'`, `created_at TEXT NULL`, `updated_at TEXT NULL`.
   - Unique index on `suppliers(code) WHERE code IS NOT NULL`.
2. **`lpi_document_sequences`:**
   - `prefix TEXT PRIMARY KEY` (`PR`, `PO`, `RR`, `SRET`, `ADJ`, `APV`, `PAY`), `current_number INTEGER NOT NULL`, `updated_at TEXT NOT NULL`.
   - Generates gap-free human-readable document numbers (`PR-2026-00001`, `PO-2026-00001`, `RR-2026-00001`, `SRET-2026-00001`, `ADJ-2026-00001`, `APV-2026-00001`, `PAY-2026-00001`).
3. **`inventory_locations` & `product_stock_buckets`:**
   - `inventory_locations (id, code UNIQUE, name, location_type, is_active, created_at)` seeded with `MAIN-STORE`, `RECEIVING-DOCK`, `QUARANTINE-HOLD`, `DAMAGED-RETURN`.
   - `product_stock_buckets (id, product_id, location_id, bucket_status, quantity_x1000, updated_at, UNIQUE(product_id, location_id, bucket_status))` tracking `Available`, `Reserved`, `Quarantine`, `Damaged`, `InTransit`, `OnHold`.
4. **Purchase Requisitions (`purchase_requisitions`, `purchase_requisition_lines`, `purchase_requisition_actions`):**
   - Header: `id`, `pr_number UNIQUE`, `department_name`, `requested_by_user_id`, `date_requested`, `date_needed`, `priority` (`Low`, `Normal`, `High`, `Urgent`), `purpose`, `status` (`Draft`, `Submitted`, `UnderReview`, `RevisionRequested`, `Approved`, `PartiallyConverted`, `ConvertedToPo`, `Rejected`, `Cancelled`), `estimated_total_cents`, `approved_by_user_id`, `approved_at`, `rejection_or_revision_reason`, `notes`, `created_at`, `updated_at`.
   - Lines: `id`, `requisition_id`, `line_number`, `product_id NULL`, `item_code`, `item_description`, `category_name`, `purchase_classification` (`Resale`, `Consumable`, `RawMaterial`, `FixedAsset`, `Service`), `unit_name`, `requested_qty_x1000`, `approved_qty_x1000`, `converted_po_qty_x1000`, `estimated_unit_cost_cents`, `estimated_line_total_cents`, `preferred_supplier_id NULL`, `specifications`, `notes`.
   - Actions/History: `id`, `requisition_id`, `action_type`, `actor_user_id`, `previous_status`, `new_status`, `reason`, `created_at`.
5. **Supplier Quotations (`supplier_quotations`):**
   - `id`, `requisition_id NULL`, `requisition_line_id NULL`, `supplier_id`, `product_id NULL`, `item_description`, `quoted_unit_cost_cents`, `minimum_order_qty_x1000`, `lead_time_days`, `payment_terms_days`, `validity_date`, `is_selected`, `selection_reason`, `recorded_by_user_id`, `created_at`.
6. **Purchase Orders (`purchase_orders`, `purchase_order_lines`, `purchase_order_history`):**
   - Header: `id`, `po_number UNIQUE`, `supplier_id`, `source_requisition_id NULL`, `order_date`, `expected_delivery_date`, `delivery_location_id`, `payment_terms_days`, `status` (`Draft`, `PendingApproval`, `Approved`, `Sent`, `Confirmed`, `PartiallyReceived`, `FullyReceived`, `Closed`, `Cancelled`), `subtotal_cents`, `discount_cents`, `tax_cents`, `shipping_cents`, `total_amount_cents`, `created_by_user_id`, `approved_by_user_id NULL`, `approved_at NULL`, `sent_at NULL`, `confirmed_at NULL`, `cancelled_by_user_id NULL`, `cancelled_at NULL`, `cancellation_reason NULL`, `terms_and_conditions NULL`, `notes NULL`, `created_at`, `updated_at`.
   - Lines: `id`, `purchase_order_id`, `line_number`, `requisition_line_id NULL`, `product_id NULL`, `item_code`, `item_description`, `purchase_classification`, `unit_name`, `ordered_qty_x1000`, `received_accepted_qty_x1000`, `received_rejected_qty_x1000`, `cancelled_qty_x1000`, `unit_cost_cents`, `discount_cents`, `tax_rate_bp`, `tax_amount_cents`, `line_total_cents`, `notes`.
   - History: `id`, `purchase_order_id`, `action_type`, `actor_user_id`, `previous_status`, `new_status`, `reason`, `created_at`.
7. **Receiving Reports (`receiving_reports`, `receiving_report_lines`):**
   - Header: `id`, `rr_number UNIQUE`, `purchase_order_id`, `supplier_id`, `location_id`, `delivery_receipt_no`, `supplier_invoice_ref NULL`, `carrier_or_driver NULL`, `received_date`, `status` (`Draft`, `PendingExceptionApproval`, `Posted`, `Cancelled`), `has_overdelivery`, `overdelivery_approved_by_user_id NULL`, `overdelivery_approval_reason NULL`, `received_by_user_id`, `inspected_by_user_id NULL`, `posted_by_user_id NULL`, `posted_at NULL`, `client_request_id UNIQUE NULL`, `notes NULL`, `created_at`, `updated_at`.
   - Lines: `id`, `receiving_report_id`, `purchase_order_line_id`, `product_id NULL`, `item_code`, `item_description`, `purchase_classification`, `unit_name`, `ordered_qty_x1000`, `previously_received_qty_x1000`, `actual_delivered_qty_x1000`, `accepted_qty_x1000`, `rejected_qty_x1000`, `shortage_qty_x1000`, `overdelivery_qty_x1000`, `unit_cost_cents`, `accepted_line_total_cents`, `rejection_reason NULL`, `rejection_disposition` (`None`, `ReturnToSupplier`, `QuarantineHold`, `DamagedWriteOff`), `lot_or_batch_no NULL`, `expiry_date NULL`, `stock_movement_id NULL`.
8. **Physical Count & Stock Adjustments (`stock_count_adjustments`) and Supplier Returns (`supplier_returns`, `supplier_return_lines`):**
   - `stock_count_adjustments (id, adjustment_number UNIQUE, product_id, location_id, system_qty_x1000, actual_counted_qty_x1000, variance_qty_x1000, unit_cost_cents, variance_value_cents, reason_code, notes, status, counted_by_user_id, approved_by_user_id, stock_movement_id, created_at, posted_at)`.
   - `supplier_returns (id, return_number UNIQUE, supplier_id, receiving_report_id, purchase_order_id, supplier_invoice_id NULL, return_date, status, total_return_amount_cents, reason, created_by_user_id, approved_by_user_id, posted_at, created_at)` and `supplier_return_lines (id, supplier_return_id, receiving_report_line_id, product_id, return_qty_x1000, unit_cost_cents, line_total_cents, reason, stock_movement_id)`.
9. **Supplier Accounts Payable & 3-Way Match (`supplier_invoices`, `supplier_invoice_lines`, `supplier_payable_entries`, `supplier_payments`):**
   - `supplier_invoices`: `id`, `ap_voucher_number UNIQUE`, `supplier_id`, `normalized_supplier_invoice_no`, `supplier_invoice_no`, `purchase_order_id NULL`, `receiving_report_id NULL`, `invoice_date`, `due_date`, `status` (`Draft`, `Matched`, `VarianceFlagged`, `VarianceApproved`, `PostedOpen`, `PartiallyPaid`, `Paid`, `Disputed`, `Voided`), `match_status` (`Unmatched`, `Matched`, `QtyVariance`, `PriceVariance`, `QtyAndPriceVariance`), `subtotal_cents`, `discount_cents`, `tax_cents`, `withholding_tax_cents`, `total_invoice_cents`, `credit_memo_applied_cents`, `paid_amount_cents`, `remaining_balance_cents`, `variance_notes NULL`, `variance_approved_by_user_id NULL`, `variance_approval_reason NULL`, `recorded_by_user_id`, `posted_by_user_id NULL`, `posted_at NULL`, `created_at`, `updated_at`.
   - Partial unique index `UX_supplier_invoices_supplier_normalized_no` on `(supplier_id, normalized_supplier_invoice_no) WHERE status != 'Voided'` to block duplicate supplier invoices!
   - `supplier_invoice_lines`: `id`, `supplier_invoice_id`, `purchase_order_line_id NULL`, `receiving_report_line_id NULL`, `product_id NULL`, `item_description`, `po_ordered_qty_x1000`, `rr_accepted_qty_x1000`, `invoiced_qty_x1000`, `po_unit_cost_cents`, `invoiced_unit_cost_cents`, `tax_cents`, `line_total_cents`, `qty_variance_x1000`, `unit_cost_variance_cents`, `line_match_status`.
   - `supplier_payable_entries`: immutable AP ledger entries (`InvoicePosted`, `SupplierReturnCredit`, `PaymentApplied`, `Adjustment`, `VoidReversal`) with `amount_delta_cents`, `balance_before_cents`, `balance_after_cents`, `actor_user_id`, `created_at`.
   - `supplier_payments`: `id`, `payment_number UNIQUE`, `supplier_invoice_id`, `supplier_id`, `payment_date`, `amount_cents`, `payment_method` (`BankTransfer`, `Check`, `PettyCash`, `GCashBusiness`, `ExternalCashFund`), `disbursement_source` (`HeadOfficeBank`, `StorePettyCashFund`, `CheckDisbursement`, `OwnerExternalFund`), `reference_no`, `check_no NULL`, `bank_name NULL`, `remarks NULL`, `recorded_by_user_id`, `approved_by_user_id NULL`, `client_request_id UNIQUE NULL`, `created_at`.

---

## 7. Ordered Implementation Slices (S0–S5)

1. **M0 (This Commit):** Architectural discovery report committed at `00-HANDOFF/ARGON_LPI_M0_CODEBASE_DISCOVERY.md`.
2. **S0 — Foundation:** Migration v27, `SalrynDatabaseMigrationCatalog` v27, LPI permissions & role seeds, Supplier CRUD, UOM & Location catalog, document numbering sequences, and S0 verification suite.
3. **S1 — Procurement (`PR`, Quotations, `PO`):** Purchase Requisition lifecycle, self-approval denial, partial/split conversion to multiple POs, Supplier Quotation comparison, PO lifecycle (`Draft` → `PendingApproval` → `Approved` → `Sent` → `Confirmed` → `Cancelled`), and S1 verification suite.
4. **S2 — Receiving & Inventory (`RR`, Atomic Stock Posting, Adjustments, `SRET`):** Receiving Report against PO, line-by-line inspection, overdelivery gate, atomic SQLite transaction updating RR + PO + `products.stock_on_hand_x1000` + `product_stock_buckets` + `stock_movements`, physical count variance adjustments, post-acceptance Supplier Returns (`SRET`), and S2 verification suite.
5. **S3 — Accounts Payable (`AP`), 3-Way Match & Payments (`PAY`):** Supplier invoice recording, duplicate invoice guard, automated 3-way match (PO vs Accepted RR vs Invoice) with variance flagging & approval, AP ledger & aging (`Current`, `1-30`, `31-60`, `61-90`, `>90`), partial/full payments with explicit disbursement source isolated from POS cash drawer, and S3 verification suite.
6. **S4 — Manager UX:** Salryn-native React workspace (`ProcureToPayPage.tsx`) integrated into `AppShell` and `navigation.ts` with role-gated tabs for Overview/KPIs, Suppliers, Requisitions (`PR`), Purchase Orders (`PO`), Receiving (`RR`), Inventory Control (Stock Cards, Buckets, Count Adjustments, Supplier Returns), Accounts Payable & 3-Way Match, and Payments & Aging Reports.
7. **S5 — End-to-End QA, 10 Slide-17 Scenarios, Screenshots & Draft PR:** Execute all 10 Slide-17 scenarios + edge cases against a live disposable Host, run `dotnet build`, `CoreSmoke`, `dotnet test`, `npm run build`, Playwright UI proofs, and open a **DRAFT** PR targeting `integration/salryn-w4-premium-ux-r1`.
