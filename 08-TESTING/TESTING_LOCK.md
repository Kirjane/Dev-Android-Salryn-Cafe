# Salryn POS Testing Lock

Status:
PLANNING LOCK ONLY.

Purpose:
Define what must be tested before Salryn is trusted by a client.

---

# 1. Product Tests

Test:
- Add product
- Add product with barcode
- Add product without barcode
- Duplicate barcode blocked
- Edit product name
- Edit product price
- Deactivate product
- Inactive product not shown in cashier search
- Product used in sale cannot be hard deleted

---

# 2. Fast Add Tests

Test:
- F2 opens Fast Add
- Unknown barcode opens Fast Add
- Barcode is prefilled
- Save creates product
- Initial stock creates stock movement
- Save and add another works
- Save returns to cashier screen
- Product can be sold immediately

---

# 3. Barcode Tests

Test:
- Known barcode adds item to cart
- Repeated scan increases quantity
- Unknown barcode opens Fast Add
- Manual barcode typing works
- Empty scan ignored
- Scan box refocuses after action

---

# 4. Cart Tests

Test:
- Add product
- Merge same product
- Change quantity
- Remove item
- Discount line
- Total calculation
- Empty cart cannot checkout

---

# 5. Checkout Tests

Test:
- Cash less than total blocked
- Exact cash accepted
- Change calculated
- Sale saved
- Sale items saved
- Payment saved
- Stock reduced
- Stock movement created
- Receipt snapshot saved
- Print job created
- Cart clears after commit

---

# 6. Printer Tests

Test:
- Test print
- Receipt print
- Reprint receipt
- Printer offline
- Wrong printer IP
- Sale remains saved when print fails
- Failed print job can be retried

---

# 7. Void Tests

Test:
- Completed sale can be voided
- Voided sale cannot be voided again
- Reason required
- Stock returned
- VOID_SALE movement created
- Audit log created
- Voided sale excluded from main sales total

---

# 8. Inventory Tests

Test:
- Stock-in increases stock
- Stock adjustment requires reason
- Negative adjustment decreases stock
- Low-stock list works
- Stock movement history shows correct entries

---

# 9. Report Tests

Test:
- Daily total correct
- Monthly total correct
- Voided sales excluded from completed total
- Top products correct
- Low-stock report correct
- PDF exports
- CSV exports

---

# 10. Backup Tests

Test:
- Manual backup creates file
- Backup file size > 0
- Backup log created
- Restore requires admin
- Backup before restore
- Restore audit log created

---

# 11. Security Tests

Test:
- Wrong password rejected
- Inactive user rejected
- Cashier cannot access settings
- Cashier cannot restore backup
- Void requires permission
- Audit logs created for important actions

---

# FINAL REVIEW PATCH: Additional Tests

## Receipt Number Tests

Test:
- Receipt number is unique.
- Receipt number increments.
- Receipt number does not duplicate after void.
- Receipt number survives app restart.

---

## Shift Tests

Test:
- Open shift with opening cash.
- Prevent duplicate open shift for same cashier.
- Sale links to active shift if shift mode enabled.
- Close shift calculates expected cash.
- Variance is calculated correctly.
- Closed shift cannot accept new sale.

---

## Product Import/Export Tests

Test:
- Export product CSV.
- Import valid CSV.
- Block duplicate barcode.
- Report invalid price.
- Report missing product name.
- Do not silently overwrite products.

---

## Error Log Tests

Test:
- Printer failure creates friendly error.
- Technical detail is logged.
- Cashier does not see stack trace.
- Failed transaction rolls back cleanly.

