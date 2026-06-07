# Salryn POS Feature Lock

Project Name: Salryn

Product Type:
Native offline-first Windows POS for general merchandise and small retail stores.

Final build direction:
C# + WinForms + SQLite + ESC/POS receipt printing + keyboard-wedge barcode scanner.

Important rule:
NO APP CODE UNTIL FEATURES ARE LOCKED.

---

# 1. MUST-HAVE FEATURES

These are required for Salryn v1.

## 1.1 Login and User Roles

Needed:
- Admin login
- Cashier login
- User accounts
- Role-based permissions
- Active/inactive users

Permissions needed:
- Create sale
- Void sale
- Reprint receipt
- Add product
- Edit product
- Stock-in
- Stock adjustment
- View reports
- Edit settings
- Backup and restore

---

## 1.2 Store Profile and Client Customization

Needed:
- Store name
- Business name
- Address
- Contact number
- Optional TIN field
- Receipt header
- Receipt footer
- Logo path optional
- Receipt width setting: 58mm or 80mm
- Currency symbol
- Default unit
- Low-stock warning toggle

Purpose:
Each client should customize their own receipt and store identity without editing code.

---

## 1.3 Product and Inventory Management

Needed product fields:
- Product ID
- Barcode
- SKU
- Product name
- Category
- Brand optional
- Unit
- Cost price
- Selling price
- Wholesale price optional
- Stock on hand
- Reorder level / low-stock level
- Track stock yes/no
- Allow discount yes/no
- Active/inactive status
- Notes optional

Needed inventory actions:
- Add product
- Edit product
- Deactivate product
- Search product
- Scan barcode
- Stock-in
- Stock adjustment
- View stock movement history
- Low-stock list

Important:
Products used in sales should not be hard deleted. They should be deactivated.

---

## 1.4 Fast Add Inventory

This is a core Salryn selling feature.

Needed:
- Fast Add Product button
- Barcode auto-fill when unknown barcode is scanned
- Product name
- Category
- Unit
- Cost price
- Selling price
- Initial stock
- Low-stock alert quantity
- Supplier optional
- Notes optional
- Save and continue
- Save and add another

Fast Add behavior:
1. Cashier scans barcode.
2. If product exists, add to cart.
3. If product does not exist, open Fast Add Product.
4. Barcode is already filled.
5. User enters name, price, stock, and unit.
6. Save.
7. Product is added to inventory.
8. Product can immediately be added to cart.

Fast Add shortcuts:
- F2 = Fast Add Product
- Enter = next field / save
- F8 = save and add another
- Esc = cancel

Avoid:
- Slow full product form for every new item
- Too many required fields
- Blocking the cashier with unnecessary details

---

## 1.5 Barcode Scanner Support

Needed:
- Keyboard-wedge scanner mode
- Scanner acts like keyboard input
- Scan box always focused on cashier screen
- Enter suffix expected
- Unknown barcode opens Fast Add Product
- Exact barcode lookup first
- Product name search fallback

Avoid for v1:
- Direct USB HID scanner API
- Bluetooth scanner special handling
- Complex scanner driver integration

---

## 1.6 Cashier / Sales Screen

Needed:
- Big scan/search box
- Product search result area
- Cart table
- Quantity change
- Remove item
- Discount optional
- Total display
- Cash payment
- Change calculation
- Complete sale
- New sale
- Reprint last receipt
- Price check
- Keyboard shortcuts

Core shortcuts:
- F2 = Fast Add Product
- F3 = Change Quantity
- F4 = Price Check
- F5 = New Sale
- F6 = Reprint Last Receipt
- F9 = Pay
- Esc = Cancel/Back

Cashier screen must be:
- Fast
- Keyboard-friendly
- Large text
- Minimal clicks
- Not overdesigned

---

## 1.7 Cart System

Needed:
- Add product by barcode
- Add product by search
- Merge same product into one cart line
- Increase quantity if scanned again
- Change quantity manually
- Remove line
- Calculate subtotal
- Calculate discounts
- Calculate total
- Validate stock before checkout

Important:
Cart is in-memory until checkout is completed.

---

## 1.8 Checkout and Payment

Needed:
- Cash payment
- Amount received
- Change calculation
- Save sale
- Save sale items
- Save payment
- Reduce stock
- Save receipt snapshot
- Save print job
- Print receipt after sale is committed

V1 payment method:
- Cash only

Good later:
- GCash
- Maya
- Card
- Bank transfer
- Split payment

---

## 1.9 Receipt Printing

Needed:
- Receipt text generation
- Receipt snapshot saved in database
- ESC/POS support
- LAN printer support preferred
- Windows printer fallback optional
- Test print button
- Reprint receipt
- Reprint last receipt
- Print failure handling

Important:
Sale must be saved before printing.

Avoid:
- Sale only existing because receipt printed
- Losing sale data if printer fails

---

## 1.10 Sales History

Needed:
- Search by receipt number
- Search by date
- Search by cashier
- View sale details
- View receipt snapshot
- Reprint receipt
- Void sale

Avoid:
- Editing old completed sale directly

---

## 1.11 Void Sale

Needed:
- Void completed sale
- Require reason
- Require authorized user
- Mark sale as voided
- Return stock to inventory
- Create stock movement reversal
- Save audit log

Important:
Never delete sale records.

---

## 1.12 Inventory Ledger

Needed:
Every stock change must create a stock movement record.

Movement types:
- SALE
- VOID_SALE
- REFUND
- STOCK_IN
- ADJUSTMENT_ADD
- ADJUSTMENT_REMOVE
- DAMAGE
- EXPIRED
- COUNT_CORRECTION

Important:
Product stock is the current balance.
Stock movements are the history/truth trail.

---

## 1.13 Reports

Needed:
- Daily sales summary
- Monthly sales summary
- Monthly PDF export
- CSV export
- Top-selling products
- Low-stock products
- Void/refund log
- Sales by cashier
- Sales by category
- Inventory value estimate

Monthly PDF should include:
- Store name
- Month covered
- Generated date
- Total sales
- Total cost
- Gross profit estimate
- Number of transactions
- Average transaction value
- Discounts
- Voids/refunds
- Top products
- Low-stock list
- Cashier breakdown

---

## 1.14 Backup and Restore

Needed:
- Manual backup
- Backup folder
- Backup timestamp
- Backup verification
- Restore warning
- Restore requires admin
- Audit log for restore

Avoid:
- Copying database while app is writing
- No backup before restore

---

## 1.15 Audit Logs

Needed audit events:
- Login
- Failed login
- Product created
- Product edited
- Price changed
- Stock-in
- Stock adjustment
- Sale completed
- Sale voided
- Receipt reprinted
- Settings changed
- Backup created
- Backup restored
- User created/deactivated

Purpose:
Traceability.

---

# 2. GOOD-TO-HAVE FEATURES

These are useful but should not block v1.

## 2.1 Supplier Management

Good to have:
- Supplier name
- Contact number
- Address
- Products supplied
- Stock-in linked to supplier

---

## 2.2 Purchase Orders

Good to have:
- Create purchase order
- Receive stock from purchase order
- Supplier cost tracking

Avoid in v1 unless needed.

---

## 2.3 Multiple Payment Methods

Good to have:
- GCash
- Maya
- Card
- Bank transfer
- Split payment
- Reference number

---

## 2.4 Customer Records

Good to have:
- Customer name
- Contact
- Purchase history
- Simple credit/utang later

Avoid in v1 if it slows down cashier flow.

---

## 2.5 Barcode Label Printing

Good to have:
- Generate barcode
- Print barcode labels
- Label templates

---

## 2.6 Expiry Date Tracking

Good to have for:
- Groceries
- Cosmetics
- Pharmacy-like stores

Fields:
- Batch number
- Expiry date
- Expiry warning

---

## 2.7 Stock Count Mode

Good to have:
- Physical inventory count
- Expected vs counted
- Difference posting
- Count correction movement

---

## 2.8 Multi-Terminal Mode

Good later:
- Server database
- Multiple cashier terminals
- Sync
- Branch support

Avoid in v1.

---

## 2.9 Cloud Backup

Good later:
- Manual cloud backup
- Scheduled backup
- Encrypted upload

Avoid until local version is stable.

---

## 2.10 Licensing System

Good later:
- Client license key
- Trial mode
- Activation
- Expiry
- Offline grace period

Avoid until core POS is stable.

---

# 3. AVOID FOR NOW

These should not be built in v1.

Avoid:
- Web app architecture
- Electron
- React frontend
- Cloud-first database
- Multi-branch sync
- Online accounts
- E-commerce integration
- Full accounting system
- Payroll
- AI features
- Mobile app
- Complicated tax engine
- BIR-compliant claims before legal/registration work
- Touchscreen-only design
- Fancy animated UI
- Overdesigned dashboard
- Editing completed sales directly
- Deleting sales
- Deleting products used in sales
- Direct USB scanner API
- Complex printer drivers before basic ESC/POS works

---

# 4. ENGINEERING RULES

Rules:
- UI does not own business logic.
- Services own business logic.
- Repositories own database access.
- Critical operations use database transactions.
- Sales are saved before printing.
- Stock changes require stock movement records.
- Completed sales are voided, not edited.
- Old receipt data is preserved using receipt snapshots.
- Money is stored as integer cents.
- Quantity is stored as scaled integer.
- Product deletion is soft delete/deactivation.
- Reports read from completed sales only unless specifically including voids.

---

# 5. FINAL V1 FEATURE LOCK

Build these first:

1. Login
2. Store profile settings
3. Product list
4. Fast Add Product
5. Barcode scan to cart
6. Cashier cart
7. Cash checkout
8. Receipt generation
9. Receipt printing
10. Sales history
11. Receipt reprint
12. Void sale
13. Stock-in
14. Stock adjustment
15. Stock movement ledger
16. Low-stock list
17. Daily report
18. Monthly PDF report
19. CSV export
20. Backup and restore
21. Audit logs

This is Salryn v1.

---

# FINAL REVIEW PATCH: Scope Corrections

## Added V1 Support Features

Add these to V1 scope:

1. Basic Cashier Shift / Day Close
2. Product CSV Import/Export
3. Technical Error Logs
4. Receipt Number Sequence Control

---

## Cashier Shift / Day Close

Needed:
- Open shift
- Opening cash amount
- Active cashier session
- Close shift
- Counted cash amount
- Expected cash amount
- Difference/variance
- Shift close report
- Admin override if needed

Purpose:
Cash sales need daily accountability.

Avoid:
- Full accounting
- Payroll
- Bank reconciliation

---

## Product CSV Import/Export

Needed:
- Export product list to CSV
- Import product list from CSV
- Validate duplicate barcodes
- Validate missing names/prices
- Preview errors before import commit

Purpose:
Fast Add handles live selling, but CSV import helps initial product setup.

---

## Error Logs

Needed:
- Friendly user errors
- Technical error log file or table
- Support/debug information
- No raw stack traces shown to cashier

---

## Receipt Number Sequencing

Needed:
- Central receipt sequence rule
- Unique receipt number
- Safe generation inside checkout transaction

Format example:
SLRN-2026-000001

---

## Refund Boundary

V1 supports:
- Void sale

V1 does not support:
- Refund
- Partial refund

Refunds are reserved for V2.

