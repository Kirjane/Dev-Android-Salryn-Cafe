# Salryn POS Database Lock

Status:
PLANNING LOCK ONLY.
No database file should be created yet.
No migration should be created yet.

Database:
SQLite

Access:
Microsoft.Data.Sqlite

Core rule:
The database must preserve business truth.

---

# 1. Database Principles

Salryn database must be:
- Offline-first
- Transaction-safe
- Auditable
- Report-friendly
- Inventory-ledger based
- Receipt-snapshot based
- Soft-delete friendly

Important:
Do not design inventory as simple editable stock numbers only.

Correct approach:
- products.stock_on_hand_x1000 = current fast balance
- stock_movements = historical truth trail

---

# 2. Storage Rules

## 2.1 Money

Never store money as floating point.

Use:
- INTEGER cents

Examples:
- 25.50 pesos = 2550
- 100.00 pesos = 10000

Field naming:
- selling_price_cents
- cost_price_cents
- total_cents
- amount_paid_cents
- change_cents

---

## 2.2 Quantity

Use scaled integer.

Rule:
- 1000 = 1 whole unit

Examples:
- 1 pc = 1000
- 0.5 kg = 500
- 1.25 liter = 1250

Field naming:
- qty_x1000
- stock_on_hand_x1000
- reorder_level_x1000

---

## 2.3 Dates

Use ISO-style text or integer timestamps consistently.

Recommended:
- TEXT in UTC/local ISO format for v1 simplicity
- created_at
- updated_at
- sale_date
- voided_at

---

# 3. Core Tables

## 3.1 app_metadata

Purpose:
Tracks database version.

Fields:
- id
- key
- value

Example:
- schema_version = 1

---

## 3.2 users

Purpose:
Stores system users.

Fields:
- id
- username
- password_hash
- full_name
- role_id
- is_active
- created_at
- updated_at

Rules:
- username unique
- password must be hashed
- users are deactivated, not deleted

---

## 3.3 roles

Purpose:
Stores role names.

Fields:
- id
- name
- description

Default roles:
- Admin
- Cashier

---

## 3.4 role_permissions

Purpose:
Maps roles to permission keys.

Fields:
- id
- role_id
- permission_key

Permission keys:
- SALE_CREATE
- SALE_VOID
- SALE_REFUND
- RECEIPT_REPRINT
- PRODUCT_CREATE
- PRODUCT_EDIT
- PRODUCT_DEACTIVATE
- STOCK_IN
- STOCK_ADJUST
- REPORT_VIEW
- SETTINGS_EDIT
- BACKUP_CREATE
- BACKUP_RESTORE
- USER_MANAGE

---

## 3.5 store_settings

Purpose:
Client/store customization.

Fields:
- id
- store_name
- business_name
- address_line_1
- address_line_2
- contact_number
- tin
- receipt_header
- receipt_footer
- logo_path
- receipt_width_chars
- currency_symbol
- default_unit_id
- allow_negative_stock
- low_stock_warning_enabled
- created_at
- updated_at

Rules:
- Single active row for v1
- editable from settings screen

---

## 3.6 categories

Purpose:
Groups products.

Fields:
- id
- name
- is_active
- created_at
- updated_at

Default:
- General

---

## 3.7 brands

Purpose:
Optional brand tracking.

Fields:
- id
- name
- is_active
- created_at
- updated_at

---

## 3.8 units

Purpose:
Product unit definitions.

Fields:
- id
- name
- short_name
- allows_decimal
- is_active

Examples:
- Piece / pcs
- Pack / pack
- Box / box
- Kilo / kg
- Gram / g
- Liter / L
- Bottle / bottle
- Sachet / sachet

---

## 3.9 suppliers

Purpose:
Supplier records.

V1 status:
Good-to-have but table can be planned early.

Fields:
- id
- name
- contact_number
- address
- notes
- is_active
- created_at
- updated_at

---

## 3.10 products

Purpose:
Main product catalog.

Fields:
- id
- sku
- barcode
- name
- description
- category_id
- brand_id
- unit_id
- supplier_id
- cost_price_cents
- selling_price_cents
- wholesale_price_cents
- stock_on_hand_x1000
- reorder_level_x1000
- track_stock
- allow_discount
- is_active
- notes
- created_at
- updated_at

Rules:
- barcode should be unique among active products
- product cannot be hard-deleted after sale exists
- inactive products should not appear in normal cashier search
- track_stock false means service/non-stock item

Indexes:
- products.barcode
- products.sku
- products.name
- products.category_id
- products.is_active

---

## 3.11 custom_field_definitions

Purpose:
Allows client-specific fields.

Fields:
- id
- entity_type
- field_key
- label
- field_type
- is_required
- sort_order
- is_active

entity_type examples:
- PRODUCT
- CUSTOMER
- SUPPLIER

field_type examples:
- TEXT
- NUMBER
- DATE
- BOOLEAN
- CHOICE

---

## 3.12 custom_field_values

Purpose:
Stores custom field values.

Fields:
- id
- entity_type
- entity_id
- field_definition_id
- value_text
- value_number
- value_date
- value_bool

Use cases:
- rack location
- color
- size
- flavor
- batch number
- warranty months
- supplier code

---

## 3.13 sales

Purpose:
Stores completed and voided sales.

Fields:
- id
- receipt_no
- sale_date
- cashier_user_id
- subtotal_cents
- discount_cents
- tax_cents
- total_cents
- amount_paid_cents
- change_cents
- status
- voided_by_user_id
- void_reason
- voided_at
- receipt_snapshot
- created_at

Status values:
- COMPLETED
- VOIDED
- REFUNDED
- PARTIAL_REFUND

Rules:
- never delete sales
- never edit completed sale item values directly
- preserve receipt_snapshot forever

Indexes:
- sales.receipt_no
- sales.sale_date
- sales.cashier_user_id
- sales.status

---

## 3.14 sale_items

Purpose:
Stores sale line items.

Fields:
- id
- sale_id
- product_id
- product_name_snapshot
- barcode_snapshot
- unit_name_snapshot
- qty_x1000
- unit_price_cents
- cost_price_cents
- discount_cents
- line_total_cents

Rules:
- snapshot fields preserve historical receipt/report data
- product price changes should not alter old sale_items

Indexes:
- sale_items.sale_id
- sale_items.product_id

---

## 3.15 payments

Purpose:
Stores sale payment records.

Fields:
- id
- sale_id
- payment_method
- amount_cents
- reference_no
- created_at

V1:
- CASH

Future:
- GCASH
- MAYA
- CARD
- BANK_TRANSFER
- SPLIT

---

## 3.16 stock_movements

Purpose:
Inventory ledger.

Fields:
- id
- product_id
- movement_type
- qty_change_x1000
- cost_price_cents
- reference_type
- reference_id
- reason
- user_id
- created_at

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

Rules:
- every stock change must insert here
- qty_change_x1000 can be positive or negative
- sale stock-out is negative
- void sale reversal is positive

Indexes:
- stock_movements.product_id
- stock_movements.created_at
- stock_movements.reference_type
- stock_movements.reference_id

---

## 3.17 print_jobs

Purpose:
Tracks receipt print attempts.

Fields:
- id
- job_type
- reference_type
- reference_id
- printer_name
- payload_text
- status
- error_message
- created_at
- printed_at

Status values:
- PENDING
- PRINTED
- FAILED

Rules:
- sale commit creates print job
- printer failure does not undo sale
- failed jobs can be retried

---

## 3.18 audit_logs

Purpose:
Security and traceability.

Fields:
- id
- user_id
- action
- entity_type
- entity_id
- old_value_json
- new_value_json
- reason
- created_at

Must audit:
- login
- failed login
- product created
- product edited
- price changed
- stock-in
- stock adjustment
- sale completed
- sale voided
- receipt reprinted
- settings changed
- backup created
- backup restored
- user created/deactivated

---

## 3.19 backup_logs

Purpose:
Tracks backup activity.

Fields:
- id
- backup_path
- backup_type
- status
- file_size_bytes
- created_by_user_id
- created_at
- error_message

---

# 4. Planned SQL Indexes

Important indexes:
- users.username unique
- products.barcode filtered/unique for active products
- products.sku
- products.name
- sales.receipt_no unique
- sales.sale_date
- sale_items.sale_id
- sale_items.product_id
- stock_movements.product_id
- stock_movements.created_at
- audit_logs.created_at

---

# 5. Transaction Rules

Use transactions for:
- Complete sale
- Void sale
- Stock-in
- Stock adjustment
- Fast add product with initial stock
- Product price update
- Backup restore log
- Settings update

---

# 6. Complete Sale Database Transaction

Inside one transaction:
1. Validate stock.
2. Insert sales row.
3. Insert sale_items.
4. Insert payments.
5. Insert stock_movements.
6. Update products.stock_on_hand_x1000.
7. Save receipt_snapshot.
8. Insert print_jobs.
9. Insert audit_logs.
10. Commit.

After commit:
1. Print receipt.
2. Update print_jobs as PRINTED or FAILED.

---

# 7. Fast Add Product Transaction

Inside one transaction:
1. Validate barcode uniqueness.
2. Insert product.
3. If initial stock > 0, insert STOCK_IN movement.
4. Update product stock_on_hand_x1000.
5. Insert audit log.
6. Commit.

After commit:
- Product can immediately be searched/scanned/sold.

---

# 8. Void Sale Transaction

Inside one transaction:
1. Load sale.
2. Validate status is COMPLETED.
3. Mark sale VOIDED.
4. For each sale item, return stock.
5. Insert VOID_SALE stock movements.
6. Insert audit log.
7. Commit.

---

# 9. Backup Rule

Never blindly copy active database while writing.

Backup service should:
1. Ensure no active critical transaction.
2. Create consistent SQLite backup.
3. Save to backups folder.
4. Verify file exists and has size.
5. Insert backup_logs row.
6. Show result to user.

---

# FINAL REVIEW PATCH: Additional Database Tables and Rules

## receipt_sequences

Purpose:
Controls safe receipt number generation.

Fields:
- id
- prefix
- year
- current_number
- padding_digits
- updated_at

Example format:
SLRN-2026-000001

Rules:
- Receipt number must be generated inside the sale transaction.
- Receipt number must be unique.
- Receipt number should not depend only on counting existing sales.
- sales.receipt_no must be unique.

---

## cashier_sessions

Purpose:
Tracks cashier shift/day close.

Fields:
- id
- user_id
- opened_at
- closed_at
- opening_cash_cents
- expected_cash_cents
- counted_cash_cents
- variance_cents
- status
- notes

Status values:
- OPEN
- CLOSED
- FORCE_CLOSED

Rules:
- A cashier should only have one active open session.
- Sales should optionally link to cashier_session_id.
- Closing a shift should calculate expected cash from cash sales.

Add to sales table:
- cashier_session_id

---

## error_logs

Purpose:
Stores technical errors separate from business audit logs.

Fields:
- id
- user_id
- area
- message
- technical_detail
- created_at

Rules:
- User sees friendly message.
- Technical detail goes to error log.
- Do not expose raw stack trace to cashier.

---

## import_export_jobs

Purpose:
Tracks product CSV imports/exports.

Fields:
- id
- job_type
- file_path
- status
- total_rows
- success_rows
- failed_rows
- created_by_user_id
- created_at
- error_summary

Job types:
- PRODUCT_IMPORT
- PRODUCT_EXPORT
- SALES_EXPORT

Status:
- PENDING
- COMPLETED
- FAILED

---

## Tax Boundary

sales.tax_cents remains reserved.

V1 default:
- tax_cents = 0
- tax mode = OFF

Do not build tax/BIR compliance logic in V1.

