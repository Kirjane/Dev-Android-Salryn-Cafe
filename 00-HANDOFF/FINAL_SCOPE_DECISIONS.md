# Salryn Final Scope Decisions

Status:
PATCHED PLANNING DECISION.

---

# 1. V1 Includes

Salryn V1 includes:
- Login
- Admin and Cashier roles
- Store profile settings
- Product management
- Fast Add Product
- Barcode scan to cart
- Cashier cart
- Cash checkout
- Receipt snapshot
- Receipt printing
- Sales history
- Reprint receipt
- Void sale
- Stock-in
- Stock adjustment
- Stock movement ledger
- Low-stock list
- Daily report
- Monthly PDF report
- CSV export
- Product CSV import/export
- Basic cashier shift/day close
- Backup and restore
- Audit logs
- Error logs
- Client customization

---

# 2. V1 Does Not Include

Salryn V1 does not include:
- Refund workflow
- Partial refund workflow
- Multi-terminal sync
- Cloud database
- Branch sync
- Mobile app
- Full accounting
- Payroll
- BIR-accredited official receipt/invoice mode
- Full tax engine
- Drag-and-drop receipt designer
- AI features

---

# 3. Void vs Refund Rule

V1:
Use VOID SALE only.

Void means:
- Sale is cancelled.
- Stock is returned.
- Void reason is required.
- Audit log is created.
- Voided sale is excluded from completed sales totals.

V2:
Refund and partial refund may be added later.

Refund means:
- Sale remains historically valid.
- Money is returned partly or fully.
- Stock may or may not return depending on reason.
- More complex accounting/reporting rules are required.

Decision:
Do not build refunds in V1.

---

# 4. Tax/BIR Rule

V1:
- tax_cents may exist as a reserved field.
- Default tax mode is OFF.
- Reports are internal sales/inventory reports.
- Receipts are internal transaction receipts unless client handles legal compliance.

Do not claim:
- BIR-accredited
- BIR-compliant
- Official invoice/receipt system

---

# 5. Cashier Shift Rule

V1 should include a lightweight shift/day close feature.

Purpose:
- Track opening cash
- Track expected cash
- Track counted cash
- Track difference
- Help owner close the day

Do not overbuild:
- No full accounting
- No payroll
- No bank reconciliation

---

# 6. Product Import/Export Rule

V1 should support product CSV import/export.

Purpose:
- Faster initial setup
- Easier backup of product list
- Easier client migration

Rules:
- Import must validate before saving.
- Duplicate barcode must be reported.
- Import should not silently overwrite products.
- Export should be Excel-friendly CSV.
