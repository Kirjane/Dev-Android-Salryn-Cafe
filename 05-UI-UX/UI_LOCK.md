# Salryn POS UI/UX Lock

Status:
PLANNING LOCK ONLY.

Design goal:
Fast native cashier workflow.

UI style:
- Boring but strong
- Large readable text
- Keyboard-friendly
- Minimal clicks
- No web-app feel
- No overdesigned dashboard
- No fancy animations

---

# 1. Main Navigation

Primary screens:
1. Cashier
2. Products
3. Fast Add Product
4. Inventory
5. Sales History
6. Reports
7. Settings
8. Backup
9. Users
10. Audit Logs

---

# 2. Cashier Screen Layout

Required layout:

┌────────────────────────────────────────────────────────────────────┐
│ Salryn POS        Cashier: Admin      Printer: OK      DB: OK       │
├────────────────────────────────────────────────────────────────────┤
│ SCAN / SEARCH: [___________________________________________] [F2 Add]│
├───────────────────────────────┬────────────────────────────────────┤
│ Product Search / Suggestions  │ CART                               │
│                               │ Item       Qty  Price   Total      │
│                               │ Coke 1.5L   1   75.00   75.00      │
│                               │ Noodles     3   15.00   45.00      │
│                               │                                    │
│                               │ Subtotal:                 120.00   │
│                               │ Discount:                   0.00   │
│                               │ TOTAL:                    120.00   │
├───────────────────────────────┴────────────────────────────────────┤
│ F2 Add | F3 Qty | F4 Price | F5 New | F6 Reprint | F9 Pay | Esc     │
└────────────────────────────────────────────────────────────────────┘

---

# 3. Cashier Screen Rules

Required:
- Scan/search box is always easy to reach.
- Cart total is large.
- Payment is one key away.
- Cashier can operate mostly by keyboard.
- Unknown barcode opens Fast Add Product.
- After every action, focus returns to scan box.
- Printer status visible.
- Low-stock warning can show after item is added.

Avoid:
- Too many tabs
- Tiny text
- Hidden totals
- Mouse-only workflow
- Fancy dashboard-first screen

---

# 4. Keyboard Shortcuts

Core:
- F2 = Fast Add Product
- F3 = Change Quantity
- F4 = Price Check
- F5 = New Sale
- F6 = Reprint Last Receipt
- F9 = Pay
- Esc = Cancel/Back
- Enter = confirm / next field
- Ctrl+F = focus search

---

# 5. Fast Add Product Screen

Required fields:
- Barcode
- Product name
- Category
- Unit
- Cost price
- Selling price
- Initial stock
- Low-stock level

Optional:
- Supplier
- Notes

Layout:

┌────────────────────────────────────────┐
│ FAST ADD PRODUCT                       │
├────────────────────────────────────────┤
│ Barcode:       [4801234567890]         │
│ Product Name:  [____________________]  │
│ Category:      [General___________ v]  │
│ Unit:          [pcs_______________ v]  │
│ Cost Price:    [_____]                 │
│ Selling Price: [_____]                 │
│ Initial Stock: [_____]                 │
│ Low Stock At:  [_____]                 │
│ Supplier:      [optional__________ v]  │
│ Notes:         [____________________]  │
│                                        │
│ [Enter] Save                           │
│ [F8] Save and Add Another              │
│ [Esc] Cancel                           │
└────────────────────────────────────────┘

Rules:
- Barcode prefilled if opened from unknown scan.
- Required fields only.
- Save should be fast.
- Save returns ProductSnapshot to cashier screen if opened during sale.

---

# 6. Payment Screen

Layout:

┌──────────────────────────────┐
│ PAYMENT                      │
├──────────────────────────────┤
│ Total:        PHP 120.00        │
│ Cash:        [PHP 150.00]       │
│ Change:       PHP 30.00         │
│                              │
│ [Enter] Complete Sale        │
│ [Esc] Cancel                 │
└──────────────────────────────┘

Rules:
- Payment amount cannot be less than total.
- Change updates immediately.
- Enter completes sale.
- Sale is saved before printing.

---

# 7. Product List Screen

Required:
- Search by name/barcode/SKU
- Product table
- Add product
- Edit product
- Deactivate product
- Stock-in shortcut
- Stock movement shortcut
- Low-stock filter

Columns:
- Barcode
- SKU
- Name
- Category
- Unit
- Cost
- Price
- Stock
- Low Stock At
- Active

---

# 8. Inventory Screen

Sections:
- Stock-in
- Stock adjustment
- Stock movement history
- Low-stock list

Rules:
- Stock adjustment requires reason.
- Stock-in should be faster than full product edit.
- All stock changes must create ledger entries.

---

# 9. Sales History Screen

Required:
- Search by receipt number
- Filter by date
- Filter by cashier
- View sale details
- Reprint receipt
- Void sale

Columns:
- Receipt No
- Date
- Cashier
- Total
- Status

---

# 10. Reports Screen

Required:
- Daily report
- Monthly report
- Export monthly PDF
- Export CSV
- Low-stock report
- Top products

Rules:
- Reports should be readable by store owner.
- PDF should look clean and printable.
- CSV should open in Excel.

---

# 11. Settings Screen

Sections:
- Store profile
- Receipt template
- Printer settings
- Barcode scanner notes
- Backup settings
- User settings

---

# 12. Visual Style

Recommended:
- Light mode first
- High contrast
- Big total display
- Simple buttons
- Clear table grid
- Minimal colors
- Professional but not flashy

Avoid:
- Glassmorphism
- Heavy animations
- Hidden sidebars
- Overdesigned charts
- Web-dashboard feel

---

# FINAL REVIEW PATCH: Additional UI Screens

## Shift Open Screen

Fields:
- Cashier
- Opening cash
- Notes optional

Buttons:
- Open Shift
- Cancel

Rules:
- Required if shift mode is enabled.
- Cashier cannot sell without open shift if shift mode is enabled.

---

## Shift Close Screen

Fields:
- Opening cash
- Cash sales
- Expected cash
- Counted cash
- Variance
- Notes

Buttons:
- Close Shift
- Print/Export Summary optional

Rules:
- Show variance clearly.
- Require confirmation if variance is not zero.

---

## Product CSV Import Screen

Required:
- Choose CSV file
- Preview rows
- Show errors
- Import valid rows

Common errors:
- Missing product name
- Invalid price
- Duplicate barcode
- Invalid stock quantity

---

## Product CSV Export Screen

Required:
- Choose export folder
- Export active products
- Show exported file path

