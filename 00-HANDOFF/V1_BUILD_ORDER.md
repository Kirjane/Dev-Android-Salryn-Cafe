# Salryn V1 Build Order

Status:
**HISTORICAL PLANNING DOCUMENT** — retained for archaeology only.
Superseded for product truth by:

- `00-HANDOFF/BETA_CAPABILITY_MATRIX.md` (PRE-BETA-H seal)
- `06-HARDWARE/HARDWARE_LOCK.md`
- `09-DEPLOYMENT/DEPLOYMENT_LOCK.md`
- Current beta readiness lane reports under `00-HANDOFF/PRE_BETA_*`

Critical correction:
**Step 22 “ESC/POS printer” is NOT beta/product truth.** ESC/POS, drawer, cutter, and thermal profiles are **FORBIDDEN** for beta. Current printer path is **Windows print queue internal receipts only**.

App code exists and is far past “no app code yet.” Do not use this file as a release claim source.

Original status (obsolete):
PLANNING LOCK ONLY.
No app code yet.

Purpose (historical):
This document defined the exact order for building Salryn V1 after the planning docs were accepted.

Core rule:
Build the engine first, then the screens.

Do not start with random UI forms.
Do not start with receipt printing.
Do not start with reports.
Do not start with installer.

---

# 1. Build Strategy

Salryn must be built in this order:

1. Project skeleton
2. Core value objects
3. Core models
4. Result/error pattern
5. SQLite infrastructure
6. Database migration system
7. Seed/default data
8. Product module
9. Fast Add Product module
10. Inventory ledger
11. Cart engine
12. Checkout transaction
13. Receipt snapshot generation
14. File receipt printer
15. Cashier UI
16. Product UI
17. Inventory UI
18. Sales history
19. Void sale
20. Reports
21. Backup/restore
22. ESC/POS printer  ← **OBSOLETE / FORBIDDEN for beta** (use Windows print queue only; see HARDWARE_LOCK)
23. User roles/security
24. Client settings/customization
25. Testing pass
26. Deployment prep

Reason:
The POS must have a correct transaction core before the interface becomes fancy.

---

# 2. Phase 1: Solution Skeleton

Goal:
Create the project structure only.

Projects:
- Salryn.App.WinForms
- Salryn.Core
- Salryn.Infrastructure
- Salryn.Tests
- Salryn.Tools optional

Rules:
- No business logic in WinForms.
- Core must not depend on WinForms.
- Infrastructure can depend on Core.
- App can depend on Core and Infrastructure.

Done when:
- Solution builds.
- Empty WinForms app launches.
- Project references are correct.
- No database yet unless migration phase starts.

---

# 3. Phase 2: Core Value Objects

Goal:
Create safe primitive wrappers.

Value objects:
- Money
- Quantity
- Barcode
- ReceiptNumber
- Result
- Result<T>

Rules:
- Money uses cents.
- Quantity uses x1000.
- No double for money.
- No float for money.

Done when:
- Money calculations work.
- Quantity calculations work.
- Result pattern works.
- Unit tests exist for Money and Quantity.

---

# 4. Phase 3: Core Models

Goal:
Create clean business models.

Models:
- User
- Role
- Product
- ProductSnapshot
- Category
- Unit
- Cart
- CartLine
- Sale
- SaleItem
- Payment
- StockMovement
- StoreSettings
- PrintJob
- AuditLog

Rules:
- Cart is in-memory.
- ProductSnapshot is used for cashier speed.
- SaleItem uses snapshot fields.
- StockMovement records every inventory change.

Done when:
- Models compile.
- Cart can add/remove/merge lines in tests.
- Totals calculate correctly.

---

# 5. Phase 4: SQLite Infrastructure

Goal:
Prepare local database access.

Components:
- SqliteConnectionFactory
- DbPaths
- DbInitializer
- MigrationRunner
- Repository base helpers

SQLite startup PRAGMA:
- foreign_keys = ON
- journal_mode = WAL
- synchronous = NORMAL
- busy_timeout = 5000

Rules:
- Use Microsoft.Data.Sqlite.
- Do not use heavy ORM in V1.
- Keep SQL predictable.

Done when:
- App can open SQLite connection.
- Migrations can run.
- Database version can be read.

---

# 6. Phase 5: Database Schema and Seed Data

Goal:
Create first real database schema.

Tables:
- app_metadata
- users
- roles
- role_permissions
- store_settings
- categories
- brands
- units
- suppliers
- products
- custom_field_definitions
- custom_field_values
- sales
- sale_items
- payments
- stock_movements
- print_jobs
- audit_logs
- backup_logs

Seed data:
- Admin role
- Cashier role
- Default permissions
- Default units
- General category
- Default store settings

Done when:
- Fresh database initializes.
- Default units exist.
- Default roles exist.
- Schema version is recorded.

---

# 7. Phase 6: Product Module

Goal:
Make product catalog work before sales.

Services:
- ProductService
- ProductRepository

Required methods:
- FindByBarcodeAsync
- SearchAsync
- CreateProductAsync
- UpdateProductAsync
- DeactivateProductAsync
- GetProductDetailsAsync

Rules:
- Duplicate active barcode blocked.
- Inactive products hidden from cashier search.
- Products used in sales are deactivated, not hard deleted.

Done when:
- Product can be added.
- Product can be searched.
- Barcode lookup works.
- Duplicate barcode is blocked.

---

# 8. Phase 7: Fast Add Product

Goal:
Build Salryn's core speed feature.

Flow:
1. Scan unknown barcode.
2. Open Fast Add Product.
3. Barcode is prefilled.
4. User enters minimum fields.
5. Save product.
6. Initial stock creates STOCK_IN movement.
7. Product can immediately be added to cart.

Required fields:
- Barcode
- Product name
- Unit
- Selling price
- Initial stock

Optional:
- Category
- Cost price
- Low-stock level
- Supplier
- Notes

Done when:
- Unknown barcode can create product.
- Product appears immediately in search.
- Initial stock is recorded in stock_movements.
- Fast Add can return to cashier flow.

---

# 9. Phase 8: Inventory Ledger

Goal:
Make stock trustworthy.

Services:
- InventoryService
- InventoryRepository

Required methods:
- StockInAsync
- AdjustStockAsync
- GetStockMovementsAsync
- GetLowStockAsync
- GetInventoryValueAsync

Rules:
- Every stock change creates stock_movement.
- Adjustment requires reason.
- Stock-in quantity must be positive.
- Product stock_on_hand_x1000 is updated inside same transaction.

Done when:
- Stock-in works.
- Stock adjustment works.
- Movement history shows correct entries.
- Low-stock list works.

---

# 10. Phase 9: Cart Engine

Goal:
Make cashier cart work without database side effects.

Data structure:
- List<CartLine> for display order
- Dictionary<ProductId, CartLine> for fast merge

Required behavior:
- Add product
- Merge same product
- Increase quantity
- Change quantity
- Remove item
- Apply discount
- Calculate subtotal
- Calculate total

Rules:
- Cart is not saved until checkout.
- Quantity must be greater than zero.
- Discount cannot exceed line total.

Done when:
- Cart tests pass.
- Repeated scan merges item.
- Totals are correct.

---

# 11. Phase 10: Checkout Transaction

Goal:
Build the most important POS transaction.

Service:
- SaleService

Required method:
- CompleteSaleAsync(cart, payment, cashierUserId)

Transaction steps:
1. Validate cart.
2. Validate payment.
3. Generate receipt number.
4. Insert sales row.
5. Insert sale_items.
6. Insert payment.
7. Validate and reduce stock.
8. Insert SALE stock movements.
9. Build receipt text.
10. Save receipt snapshot.
11. Insert print job.
12. Insert audit log.
13. Commit.

After commit:
- Print receipt.
- Mark print job PRINTED or FAILED.
- Clear cart only after commit.

Done when:
- Sale saves correctly.
- Stock reduces correctly.
- Receipt snapshot exists.
- Print job exists.
- Failed printer does not erase sale.

---

# 12. Phase 11: Receipt Snapshot and File Printer

Goal:
Test receipts without physical printer.

Components:
- ReceiptService
- ReceiptRenderer
- IReceiptPrinter
- FileReceiptPrinter

Rules:
- Receipt snapshot is saved at sale time.
- Reprint uses snapshot.
- Product edits do not change old receipts.
- FileReceiptPrinter writes receipt .txt for testing.

Done when:
- Receipt text is readable.
- Receipt snapshot is saved.
- Reprint uses same snapshot.
- Receipt can be exported as .txt.

---

# 13. Phase 12: Cashier UI

Goal:
Build the main selling screen.

Screen:
- CashierForm

Required:
- Scan/search box
- Cart grid
- Big total
- Payment button
- Fast Add button
- Price check
- Reprint last receipt
- Keyboard shortcuts

Shortcuts:
- F2 Fast Add
- F3 Quantity
- F4 Price Check
- F5 New Sale
- F6 Reprint Last Receipt
- F9 Pay
- Esc Back/Cancel

Rules:
- Scan box refocuses after every action.
- Unknown barcode opens Fast Add.
- Cashier can operate mostly by keyboard.

Done when:
- Known barcode adds to cart.
- Unknown barcode opens Fast Add.
- Sale can be completed from UI.
- Receipt file output works.

---

# 14. Phase 13: Product and Inventory UI

Goal:
Make product and stock management usable.

Screens:
- ProductListForm
- ProductEditorForm
- FastAddProductForm
- InventoryForm
- StockInForm
- StockAdjustmentForm
- StockMovementForm

Rules:
- Product list uses search/paging.
- Stock adjustment requires reason.
- Stock-in is faster than full product edit.

Done when:
- Products can be managed from UI.
- Stock-in works from UI.
- Stock adjustment works from UI.
- Movement history visible.

---

# 15. Phase 14: Sales History and Void Sale

Goal:
Make completed sales traceable.

Screens:
- SalesHistoryForm
- SaleDetailsForm
- VoidSaleForm

Required:
- Search by receipt number
- Filter by date
- Filter by cashier
- View receipt snapshot
- Reprint
- Void sale

Void rules:
- Require permission.
- Require reason.
- Mark sale VOIDED.
- Return stock.
- Insert VOID_SALE movements.
- Insert audit log.

Done when:
- Sale history works.
- Reprint works.
- Void returns stock correctly.
- Voided sale excluded from main sales total.

---

# 16. Phase 15: Reports

Goal:
Give owner usable summaries.

Screens:
- ReportsForm

Reports:
- Daily sales report
- Monthly sales report
- Top products
- Low-stock report
- Void/refund report
- Inventory value estimate

Exports:
- PDF
- CSV

Rules:
- Completed sales only in main totals.
- Voids shown separately.
- Old sale snapshots used for historical item names/prices.

Done when:
- Daily totals are correct.
- Monthly PDF exports.
- CSV opens in Excel.
- Low-stock report works.

---

# 17. Phase 16: Backup and Restore

Goal:
Protect client data.

Screens:
- BackupForm
- RestoreForm

Required:
- Manual backup
- Backup verification
- Backup log
- Restore requires admin
- Backup before restore
- Restore audit log

Rules:
- Never overwrite database without backup.
- Never store only one backup.
- Restore must warn user.

Done when:
- Backup creates file.
- Backup log exists.
- Restore flow is guarded.
- Restore requires admin.

---

# 18. Phase 17: ESC/POS Printer

Goal:
Move from file printing to real receipt printer.

Implementations:
- EscPosTcpPrinter
- WindowsPrintPrinter fallback

Preferred:
- LAN printer on port 9100

Rules:
- Printer failure does not rollback sale.
- Reprint does not duplicate sale.
- Printer settings are configurable.

Done when:
- Test print works.
- Receipt print works.
- Offline printer shows friendly error.
- Reprint works.

---

# 19. Phase 18: Users, Roles, and Security

Goal:
Protect sensitive actions.

Required:
- Login
- Admin role
- Cashier role
- Permission checks
- Inactive user blocking
- Password hashing

Sensitive permissions:
- Void sale
- Stock adjustment
- Settings edit
- Backup restore
- User management

Done when:
- Cashier cannot access admin-only actions.
- Void sale requires permission.
- Restore requires admin.
- Audit logs record sensitive actions.

---

# 20. Phase 19: Client Settings and Customization

Goal:
Let clients configure without code edits.

Screens:
- StoreSettingsForm
- ReceiptSettingsForm
- PrinterSettingsForm
- CustomFieldsForm

Settings:
- Store name
- Business name
- Address
- Contact
- TIN optional
- Receipt header/footer
- Receipt width
- Currency label
- Default unit
- Low-stock warning
- Negative stock setting

Done when:
- Receipt reflects store settings.
- Reports show store name.
- Printer settings persist.
- Client can change receipt footer.

---

# 21. Phase 20: Full Testing Pass

Goal:
Make Salryn client-safe.

Required test groups:
- Product tests
- Fast Add tests
- Barcode tests
- Cart tests
- Checkout tests
- Printer tests
- Void tests
- Inventory tests
- Report tests
- Backup tests
- Security tests

Done when:
- All manual test cases pass.
- No critical transaction bug exists.
- Backup and restore tested.
- Printer failure tested.
- Void reversal tested.

---

# 22. Phase 21: Deployment Prep

Goal:
Prepare for real-world install.

Required:
- Production data path
- First-run setup wizard
- Default data creation
- Desktop shortcut later
- Backup folder
- Client handoff checklist
- Support notes

Avoid:
- Claiming BIR compliance.
- Installer wiping data.
- Database stored in Downloads.

Done when:
- Client setup checklist passes.
- Sample sale works.
- Sample report exports.
- Backup exists.

---

# Final Coding Rule

The first coding milestone should not be a pretty UI.

The first coding milestone should be:

1. Product can be created.
2. Fast Add can create product.
3. Cart can add product.
4. Checkout can save sale.
5. Stock reduces correctly.
6. Receipt snapshot is saved.
7. File receipt is generated.

Only after that should the UI become polished.

---

# FINAL REVIEW PATCH: Build Order Clarification

The 26-item Build Strategy list is the source of truth.

The detailed phase sections group related milestones together, so the numbering may not be one-to-one with the 26-item list.

Additional implementation milestones added by final review:
- Receipt sequence service/table
- Cashier shift/day close
- Product CSV import/export
- Error logging

Recommended placement:
1. Receipt sequence should be added during database/schema phase.
2. Error logging should be added during infrastructure phase.
3. Product import/export should be added after product module.
4. Cashier shift/day close should be added after checkout transaction and before reports.

Do not start UI polish until:
- Checkout transaction works
- Receipt snapshot works
- Stock ledger works
- FileReceiptPrinter works
- Receipt sequencing works

