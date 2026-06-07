# Salryn POS Client Customization Lock

Status:
PLANNING LOCK ONLY.

Goal:
Clients should customize Salryn without editing code.

---

# 1. Store Profile Customization

Fields:
- Store name
- Business name
- Address line 1
- Address line 2
- Contact number
- TIN optional
- Logo path optional
- Receipt header
- Receipt footer
- Currency symbol

---

# 2. Receipt Customization

Settings:
- Receipt width: 58mm or 80mm
- Header text
- Footer text
- Show cashier name yes/no
- Show barcode yes/no
- Show TIN yes/no
- Show business name yes/no

Template variables:
- {STORE_NAME}
- {BUSINESS_NAME}
- {ADDRESS}
- {CONTACT}
- {TIN}
- {RECEIPT_NO}
- {DATE}
- {TIME}
- {CASHIER}
- {ITEMS}
- {SUBTOTAL}
- {DISCOUNT}
- {TOTAL}
- {CASH}
- {CHANGE}
- {FOOTER}

---

# 3. Product Custom Fields

Purpose:
Different clients sell different products.

Custom field examples:
- Rack location
- Color
- Size
- Flavor
- Batch number
- Expiry date
- Warranty months
- Supplier code

Supported field types:
- TEXT
- NUMBER
- DATE
- BOOLEAN
- CHOICE

---

# 4. Inventory Behavior Settings

Settings:
- Allow negative stock yes/no
- Enable low-stock warnings yes/no
- Default reorder level
- Default unit
- Track stock by default yes/no

Recommended defaults:
- allow_negative_stock = false
- low_stock_warning_enabled = true
- track_stock = true

---

# 5. User/Role Customization

Default roles:
- Admin
- Cashier

Admin can:
- Manage everything

Cashier can:
- Create sale
- Reprint receipt if allowed
- Fast add product if allowed
- Stock-in if allowed

Recommended:
Fast Add Product may be allowed for cashier, but stock adjustment should require admin.

---

# 6. Report Customization

Settings:
- Store name shown on report
- Date range
- Include void section yes/no
- Include low-stock section yes/no
- Include cashier breakdown yes/no

---

# 7. Avoid Too Much Customization in V1

Avoid:
- Drag-and-drop receipt designer
- Full report designer
- Custom SQL reports
- Theme engine
- Plugin system

Reason:
Too much customization early will delay the POS core.
