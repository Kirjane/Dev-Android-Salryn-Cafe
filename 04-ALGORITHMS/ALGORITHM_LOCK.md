# Salryn POS Algorithm Lock

Status:
PLANNING LOCK ONLY.
No app code yet.

Purpose:
Defines how Salryn must behave internally.

---

# 1. Core Data Structures

## 1.1 ProductSnapshot

Used for fast cashier operations.

Fields:
- product_id
- barcode
- name
- unit_name
- selling_price_cents
- cost_price_cents
- stock_on_hand_x1000
- track_stock

Reason:
Cashier screen does not need full product details every scan.

---

## 1.2 Cart

In-memory only until checkout.

Data structure:
- List<CartLine> for display order
- Dictionary<ProductId, CartLine> for fast merge/search

Reason:
If same product is scanned again, increase quantity instead of creating duplicate line.

---

## 1.3 CartLine

Fields:
- product_id
- product_name
- barcode
- unit_name
- qty_x1000
- unit_price_cents
- cost_price_cents
- discount_cents
- line_total_cents

Formula:
line_total_cents = unit_price_cents * qty_x1000 / 1000 - discount_cents

---

# 2. Barcode Scan Algorithm

Trigger:
Cashier scans barcode and scanner sends Enter.

Flow:
1. Read raw scan input.
2. Normalize barcode.
3. If empty, ignore.
4. Check product cache by barcode.
5. If not cached, query product repository.
6. If found, add product to cart.
7. If not found, open Fast Add Product with barcode prefilled.
8. After Fast Add save, add product to cart.
9. Refocus scan box.

Rules:
- Exact barcode lookup first.
- Name search is fallback only.
- Scan box must be refocused after every action.
- Unknown barcode should not dead-end the cashier.

---

# 3. Cart Merge Algorithm

When adding product:
1. Check dictionary by product_id.
2. If product exists in cart:
   - increase qty_x1000
   - recalculate line total
3. Else:
   - create new CartLine
   - add to list
   - add to dictionary
4. Recalculate totals.
5. Refresh cart grid.

Rules:
- Same product should not appear multiple times unless future setting allows it.
- Quantity must be greater than zero.
- Discount cannot exceed line total.

---

# 4. Fast Add Product Algorithm

Trigger:
- F2 manual action
- Unknown barcode scan

Flow:
1. Open Fast Add Product screen.
2. If unknown scan, prefill barcode.
3. User enters required fields:
   - name
   - unit
   - selling price
   - initial stock
4. Optional fields:
   - category
   - cost price
   - low-stock level
   - supplier
   - notes
5. Validate barcode uniqueness.
6. Validate price is not negative.
7. Validate stock is not negative.
8. Begin transaction.
9. Insert product.
10. If initial stock > 0:
    - insert STOCK_IN stock movement
    - set stock_on_hand_x1000
11. Insert audit log.
12. Commit.
13. Return ProductSnapshot.
14. If opened from cashier scan, add to cart immediately.

Rules:
- Fast Add should be short.
- Full Product Editor can handle advanced details later.
- Fast Add must not require too many fields.

---

# 5. Complete Sale Algorithm

Trigger:
Cashier presses F9 / Pay and confirms payment.

Flow:
1. Validate cart is not empty.
2. Validate payment amount >= total.
3. Open SQLite connection.
4. Begin transaction.
5. Generate receipt number.
6. Insert sales row.
7. For each cart line:
   - validate product still active
   - validate stock if track_stock is true
   - insert sale_items row with snapshots
   - subtract stock
   - insert SALE stock movement
8. Insert payment row.
9. Build receipt text.
10. Save receipt snapshot to sale.
11. Insert print job as PENDING.
12. Insert audit log SALE_COMPLETED.
13. Commit transaction.
14. Print receipt.
15. If print success:
    - update print job PRINTED
16. If print failed:
    - update print job FAILED
    - show reprint option
17. Clear cart only after sale is committed.

Critical rule:
Save first, print second.

Reason:
A printed receipt without database record is dangerous.

---

# 6. Stock-In Algorithm

Trigger:
Admin/cashier receives new stock.

Flow:
1. Select product.
2. Enter quantity.
3. Enter cost price optional.
4. Enter reason/supplier optional.
5. Validate quantity > 0.
6. Begin transaction.
7. Increase products.stock_on_hand_x1000.
8. Insert STOCK_IN movement.
9. Insert audit log.
10. Commit.
11. Refresh product stock.

---

# 7. Stock Adjustment Algorithm

Trigger:
User corrects stock due to count, damage, expiry, or mistake.

Flow:
1. Select product.
2. Show current stock.
3. Enter new actual stock.
4. Require reason.
5. Compute difference:
   difference = new_stock - current_stock
6. If difference > 0:
   movement_type = ADJUSTMENT_ADD
7. If difference < 0:
   movement_type = ADJUSTMENT_REMOVE
8. If difference = 0:
   no operation
9. Begin transaction.
10. Update stock_on_hand_x1000.
11. Insert stock movement.
12. Insert audit log.
13. Commit.

Rule:
Adjustment must require a reason.

---

# 8. Void Sale Algorithm

Trigger:
Authorized user voids sale.

Flow:
1. Search sale.
2. Validate sale exists.
3. Validate sale status is COMPLETED.
4. Require reason.
5. Begin transaction.
6. Mark sale as VOIDED.
7. For every sale item:
   - add stock back if track_stock
   - insert VOID_SALE stock movement
8. Insert audit log SALE_VOIDED.
9. Commit.
10. Optional: print void receipt/slip later.

Rules:
- Never delete sale.
- Never edit completed sale directly.
- Void must be traceable.

---

# 9. Receipt Render Algorithm

Input:
- store settings
- sale
- sale items
- payment
- receipt width

Flow:
1. Load receipt snapshot if reprint.
2. For new sale, build text:
   - centered store name
   - address/contact
   - receipt number/date/cashier
   - item list
   - subtotal/discount/total
   - cash/change
   - footer
3. Wrap long item names.
4. Align money to right.
5. Save snapshot.
6. Send snapshot to printer.

Rule:
Reprint uses saved snapshot, not regenerated current product info.

---

# 10. Report Algorithm

Daily report:
1. Set start = selected date 00:00.
2. Set end = next date 00:00.
3. Query completed sales only.
4. Sum totals.
5. Count transactions.
6. Group by cashier/category/product.
7. Include separate void section if needed.

Monthly report:
1. Set start = first day of month.
2. Set end = first day of next month.
3. Build:
   - summary
   - sales by day
   - top products
   - low stock
   - cashier breakdown
   - void/refund log
4. Export to PDF.
5. Optional CSV export.

---

# 11. Product Search Algorithm

Input:
- keyword

Flow:
1. Trim keyword.
2. If empty, return nothing.
3. If keyword looks like barcode:
   - exact barcode search first
4. Else:
   - search name, SKU, barcode using LIKE
5. Limit result to 50.
6. Sort:
   - exact barcode first
   - exact SKU second
   - name prefix third
   - name contains last

Rule:
Never load all products into cashier screen.

---

# 12. Backup Algorithm

Manual backup:
1. User clicks backup.
2. Require admin permission.
3. Determine backup path.
4. Create safe SQLite backup.
5. Verify backup file exists.
6. Verify file size > 0.
7. Insert backup log.
8. Show success.

Restore:
1. Require admin.
2. Warn user.
3. Create backup before restore.
4. Close active forms if needed.
5. Restore selected backup.
6. Insert audit log.
7. Restart app if required.

---

# 13. Error Handling Algorithm

For service methods:
1. Validate input.
2. Try operation.
3. If business error, return clean Result.Fail.
4. If exception, rollback transaction.
5. Log technical error.
6. Show user-friendly message.

User should see:
- "Not enough stock"
- "Product barcode already exists"
- "Payment is not enough"
- "Printer failed. Sale saved. You can reprint."

User should not see:
- raw SQL exception
- stack trace
- confusing system errors

---

# 14. Performance Rules

Barcode scan:
- Exact indexed lookup
- Cache recently scanned products
- Refocus scan box quickly

Cart:
- Dictionary lookup by product_id
- Recalculate totals after changes only

Reports:
- Use SQL aggregation
- Do not compute huge reports in UI loops

Database:
- Index barcode, receipt_no, sale_date
- Use transactions
- Avoid loading unnecessary columns

---

# FINAL REVIEW PATCH: Additional Algorithms

## Receipt Number Generation Algorithm

Flow:
1. Begin sale transaction.
2. Load receipt sequence row for current year/prefix.
3. Increment current_number.
4. Format receipt number.
5. Insert sale using receipt number.
6. Commit with sale.

Rules:
- Never generate receipt number outside transaction.
- Never use COUNT(*) as receipt number source.
- sales.receipt_no must be unique.

---

## Open Shift Algorithm

Flow:
1. User logs in.
2. If shift mode enabled, check active cashier session.
3. If no active session, ask for opening cash.
4. Create cashier_sessions row with status OPEN.
5. Save opening_cash_cents.
6. Continue to cashier screen.

---

## Close Shift Algorithm

Flow:
1. Cashier/admin opens close shift screen.
2. System calculates expected cash:
   opening cash + cash sales - voided cash sales
3. User enters counted cash.
4. System calculates variance.
5. Mark session CLOSED.
6. Generate shift close summary.
7. Insert audit log.

Rules:
- Closed shifts cannot accept new sales.
- Admin can force-close abandoned shift.
- Shift close is not full accounting.

---

## Product CSV Import Algorithm

Flow:
1. User chooses CSV file.
2. Read headers.
3. Validate required columns.
4. Validate rows.
5. Detect duplicate barcode in file.
6. Detect duplicate active barcode in database.
7. Show preview/errors.
8. If accepted, begin transaction.
9. Insert valid products.
10. Insert initial stock movements if stock is provided.
11. Insert import_export_jobs row.
12. Commit.

Rules:
- Do not silently overwrite products.
- Failed rows should be reported.
- Import should be all-or-reviewed, not blind.

---

## Product CSV Export Algorithm

Flow:
1. User selects export location.
2. Query active products.
3. Write CSV headers.
4. Write product rows.
5. Insert import_export_jobs row.
6. Show exported file path.

---

## Technical Error Logging Algorithm

Flow:
1. Service catches exception.
2. Roll back transaction if active.
3. Insert error_logs row or write log file.
4. Return friendly Result.Fail message.
5. UI shows simple message.

User sees:
Printer failed. Sale saved. You can reprint.

User does not see:
Raw SQL exception or stack trace.

