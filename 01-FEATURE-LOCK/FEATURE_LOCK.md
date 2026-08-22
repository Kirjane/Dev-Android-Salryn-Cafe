# Salryn POS Feature Lock

Project Name: Salryn

Product Type:
Native offline-first Windows POS for general merchandise and small retail stores.

Current beta/release direction:
C#/.NET local POS + SQLite + keyboard-wedge barcode scanner + Windows print queue internal receipt printing.

Scope-lock note:
Hardware peripherals beyond the Windows print queue are outside the beta scope.

---

## 1. Must-have beta features

### 1.1 Login and user roles

Needed:

- Admin login.
- Cashier login.
- User accounts.
- Role-based permissions.
- Active/inactive users.

Permissions needed:

- Create sale.
- Void sale.
- Reprint receipt.
- Add product.
- Edit product.
- Stock-in.
- Stock adjustment.
- View reports.
- Edit settings.
- Backup and restore, including portable Download/Import Backup File packages.

---

### 1.2 Store profile and client customization

Needed:

- Store name.
- Business name.
- Address.
- Contact number.
- Optional TIN field stored as identity text only.
- Receipt header.
- Receipt footer.
- Currency label.
- Default unit.
- Low-stock warning behavior.
- Store logo / branding (PNG/JPEG/WebP; Host + LAN shell display) — PRE-BETA-C.

Out of beta scope:

- hardware receipt width/profile support.
- thermal receipt formatting.
- hardware-specific templates.
- Official/BIR logo accreditation claims.

Purpose:
Each client should customize receipt text and store identity without editing code. This does not create an official/BIR output claim.

See: `00-HANDOFF/BETA_CAPABILITY_MATRIX.md`.

---

### 1.3 Product and inventory management

Needed product fields:

- Product ID.
- Barcode.
- SKU.
- Product name.
- Category.
- Optional brand.
- Unit.
- Cost price.
- Selling price.
- Stock on hand.
- Reorder level / low-stock level.
- Track stock yes/no.
- Active/inactive status.
- Optional notes.

Needed inventory actions:

- Add product.
- Edit product.
- Deactivate product.
- Search product.
- Scan barcode through keyboard-wedge input.
- Stock-in.
- Stock adjustment.
- View stock movement history.
- Low-stock list.

Important:
Products used in sales should not be hard deleted. They should be deactivated.

---

### 1.4 Fast Add inventory

Needed:

- Fast Add Product button.
- Barcode auto-fill when unknown barcode is scanned.
- Product name.
- Category.
- Unit.
- Cost price.
- Selling price.
- Initial stock.
- Low-stock alert quantity.
- Save and continue.
- Save and add another.

Fast Add behavior:

1. Cashier scans or types barcode.
2. If product exists, add to cart.
3. If product does not exist, open Fast Add Product.
4. Barcode is already filled.
5. User enters name, price, stock, and unit.
6. Save.
7. Product is added to inventory.
8. Product can immediately be added to cart.

---

### 1.5 Barcode scanner support

Needed:

- Keyboard-wedge scanner mode.
- Scanner acts like keyboard input.
- Scan box always focused on cashier screen.
- Enter suffix expected.
- Unknown barcode opens Fast Add Product.
- Exact barcode lookup first.
- Product name search fallback.

Out of scope for this release:

- Direct USB HID scanner API.
- Bluetooth scanner special handling.
- Complex scanner driver integration.

---

### 1.6 Cashier / sales screen

Needed:

- Big scan/search box.
- Product search result area.
- Cart table.
- Quantity change.
- Remove item.
- Total display.
- Cash payment.
- Change calculation.
- Complete sale.
- New sale.
- Reprint receipt.
- Price check.
- Keyboard shortcuts.

---

### 1.7 Cart system

Needed:

- Add product by barcode.
- Add product by search.
- Merge same product into one cart line.
- Increase quantity if scanned again.
- Change quantity manually.
- Remove line.
- Calculate subtotal.
- Calculate discounts.
- Calculate total.
- Validate stock before checkout.

Important:
Cart is in-memory until checkout is completed.

---

### 1.8 Checkout and payment

Needed:

- Cash payment.
- Amount received.
- Change calculation.
- Save sale.
- Save sale items.
- Save payment.
- Reduce stock.
- Save receipt snapshot.
- Print/reprint from saved receipt snapshot only.

V1 payment method:

- Cash only.

---

### 1.9 Receipt printing

Allowed current scope:

- Receipt text generation.
- Receipt snapshot saved in database.
- Windows print queue internal receipt printing.
- Exact Windows printer name setting.
- Test print.
- Reprint receipt from persisted snapshot.
- Print failure handling.

Important:
Sale must be saved before printing. Print/reprint must not duplicate sales, payments, or stock movements.

Banned from beta/release scope:

- cash drawer.
- drawer kick.
- receipt cutter.
- cut receipt command.
- ESC/POS.
- thermal printer profile.
- hardware receipt width/profile support.
- thermal receipt formatting.
- hardware bundle/support claims.

These are not active roadmap items, not release blockers, and not tracked for this release.

---

### 1.10 Sales history

Needed:

- Search by receipt number.
- Search by date.
- Search by cashier.
- View sale details.
- View receipt snapshot.
- Reprint receipt.
- Void sale.

Avoid:

- Editing old completed sale directly.

---

### 1.11 Void sale

Needed:

- Void completed sale.
- Require reason.
- Require authorized user.
- Mark sale as voided.
- Return stock to inventory.
- Create stock movement reversal.
- Save audit log.

Important:
Never delete sale records.

---

### 1.12 Reports

Needed:

- Daily sales summary.
- Monthly sales summary.
- Top-selling products.
- Low-stock products.
- Void/refund log.
- Sales by cashier.
- Sales by category.
- Inventory value estimate.

Report artifacts and PDF exports remain internal proof surfaces until explicitly accepted.

---

### 1.13 Backup and restore

Needed in its own approved lane:

- Manual backup.
- Backup folder.
- Backup timestamp.
- Backup verification.
- Restore warning.
- Restore requires admin.
- Audit log for restore.

Avoid:

- Copying database while app is writing.
- No backup before restore.

---

## 2. Good-to-have features outside current beta release

Useful later, but not beta blockers:

- Supplier management.
- Purchase orders.
- Multiple payment methods.
- Customer records.
- Barcode label printing.
- Expiry date tracking.
- Stock count mode.
- Multi-terminal mode.
- Cloud backup.
- Licensing system.

Hardware peripherals are different: they are not active future beta roadmap items in this release plan.

---

## 3. Avoid for current release

Avoid:

- Web app architecture drift beyond current local host shell.
- Cloud-first database.
- Multi-branch sync.
- Online accounts.
- E-commerce integration.
- Full accounting system.
- Payroll.
- Hardware peripherals beyond Windows print queue.
- Hardware bundle/support claims.

Status: `SCOPE_LOCKED_OUT_OF_BETA_HARDWARE_EXTRAS`
