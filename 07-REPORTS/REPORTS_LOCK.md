# Salryn POS Reports Lock

Status:
PLANNING LOCK ONLY.

Goal:
Reports must help store owners understand sales, inventory, and cashier activity.

---

# 1. Daily Sales Report

Required fields:
- Date covered
- Total sales
- Total cost estimate
- Gross profit estimate
- Number of transactions
- Average transaction value
- Total discount
- Voids/refunds
- Sales by cashier
- Top products
- Low-stock warning

Source:
- sales
- sale_items
- users
- products

Default:
Only COMPLETED sales count in main sales total.

Voids:
Shown in separate section.

---

# 2. Monthly Sales Report

Required:
- Store name
- Month covered
- Generated date
- Total sales
- Total cost estimate
- Gross profit estimate
- Number of transactions
- Average transaction value
- Discounts
- Voids/refunds
- Sales by day
- Sales by category
- Top-selling products
- Low-stock products
- Cashier breakdown
- Inventory value estimate

Export:
- PDF
- CSV optional sections

---

# 3. Product Sales Report

Required:
- Product name
- Barcode
- Quantity sold
- Total sales
- Estimated cost
- Estimated profit

Filters:
- Date range
- Category
- Product search

---

# 4. Low Stock Report

Required:
- Product name
- Barcode
- Current stock
- Reorder level
- Unit
- Supplier optional

Sort:
Lowest stock first.

---

# 5. Void/Refund Report

Required:
- Receipt number
- Date
- Cashier
- Voided by
- Reason
- Total amount

Purpose:
Owner traceability.

---

# 6. Inventory Value Report

Formula:
inventory_value = stock_on_hand * cost_price

Required:
- Product
- Stock
- Cost price
- Estimated value

Warning:
This is an estimate, not full accounting.

---

# 7. PDF Export Rules

PDF must be:
- Clean
- Printable
- Owner-readable
- Not too fancy
- Include generated date
- Include store identity

Do not:
- Make PDF depend on printer
- Use current product names for old sale lines
- Mix voided sales into completed sales total unless labeled

---

# 8. CSV Export Rules

CSV must:
- Open in Excel
- Use headers
- Use date range in filename
- Use simple column names

---

# FINAL REVIEW PATCH: Additional Reports

## Shift Close Report

Required:
- Cashier name
- Shift opened time
- Shift closed time
- Opening cash
- Cash sales
- Expected cash
- Counted cash
- Variance
- Notes

Purpose:
Daily cash accountability.

---

## Product Import Report

Required:
- File name
- Total rows
- Successful rows
- Failed rows
- Error summary

---

## Tax Boundary in Reports

V1:
- Main reports are internal sales reports.
- tax_cents is reserved and defaults to zero.
- Do not label reports as official tax reports.

