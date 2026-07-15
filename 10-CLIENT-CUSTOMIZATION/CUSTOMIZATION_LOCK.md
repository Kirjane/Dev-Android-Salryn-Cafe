# Salryn POS Client Customization Lock

Status:
BETA / RELEASE SCOPE LOCK.

Goal:
Clients should customize Salryn without editing code while avoiding hardware-scope drift.

Hardware policy:
Hardware peripherals beyond the Windows print queue are outside the beta scope.

---

## 1. Store profile customization

Fields:

- Store name.
- Business name.
- Address line 1.
- Address line 2.
- Contact number.
- Optional TIN field stored as identity text only.
- Receipt header.
- Receipt footer.
- Currency symbol / label.

---

## 2. Receipt customization

Allowed settings:

- Header text.
- Footer text.
- Store identity text.
- Cashier name display when already supported by receipt snapshot.
- Barcode text display when already supported by receipt snapshot.
- Business/TIN identity text when configured.

Out of beta scope:

- hardware receipt width/profile support.
- thermal receipt formatting.
- ESC/POS formatting.
- cutter or drawer commands.
- hardware-specific templates.

Template variables may exist as internal receipt text concepts only. They do not imply official/BIR output or hardware profile support.

---

## 3. Product custom fields

Purpose:
Different clients sell different products.

Custom field examples:

- Rack location.
- Color.
- Size.
- Flavor.
- Batch number.
- Expiry date.
- Warranty months.
- Supplier code.

Supported field types when implemented:

- TEXT.
- NUMBER.
- DATE.
- BOOLEAN.
- CHOICE.

---

## 4. Inventory behavior settings

Settings:

- Allow negative stock yes/no.
- Enable low-stock warnings yes/no.
- Default reorder level.
- Default unit.
- Track stock by default yes/no.

Recommended defaults:

- `allow_negative_stock = false`.
- `low_stock_warning_enabled = true`.
- `track_stock = true`.

---

## 5. User/role customization

Default roles:

- Admin.
- Manager.
- Cashier.

Admin can manage everything.

Cashier can create sales and use allowed cashier workflows only.

Recommended:
Fast Add Product may be allowed for cashier, but stock adjustment should require admin/manager permission.

---

## 6. Report customization

Settings:

- Store name shown on report.
- Date range.
- Include void section yes/no.
- Include low-stock section yes/no.
- Include cashier breakdown yes/no.

Report artifacts remain internal proof until explicitly accepted.

---

## 7. Avoid too much customization in beta

Avoid:

- Drag-and-drop receipt designer.
- Full report designer.
- Custom SQL reports.
- Theme engine.
- Plugin system.
- Hardware profile designer.
- Cash drawer/cutter/ESC/POS configuration.

Reason:
Too much customization early will delay the POS core and create hardware-support claims outside the beta scope.

Status: `SCOPE_LOCKED_OUT_OF_BETA_HARDWARE_EXTRAS`
