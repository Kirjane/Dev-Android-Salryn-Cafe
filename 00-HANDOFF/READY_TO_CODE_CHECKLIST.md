# Salryn Ready-To-Code Checklist

Status:
Planning review gate.

Rule:
Do not start app code until this checklist is accepted.

---

# 1. Feature Lock

Required docs:
- 01-FEATURE-LOCK\FEATURE_LOCK.md

Check:
- Must-have features listed
- Good-to-have features separated
- Avoid list clear
- Fast Add Inventory locked as core feature

Status:
PENDING REVIEW

---

# 2. Architecture Lock

Required docs:
- 02-ARCHITECTURE\ARCHITECTURE_LOCK.md

Check:
- Final stack chosen
- Layered architecture defined
- Services named
- UI/service/repository boundaries clear

Status:
PENDING REVIEW

---

# 3. Database Lock

Required docs:
- 03-DATABASE-DESIGN\DATABASE_LOCK.md

Check:
- Core tables listed
- Inventory ledger model defined
- Receipt snapshot rule defined
- Money stored as cents
- Quantity stored as x1000
- Transaction rules defined

Status:
PENDING REVIEW

---

# 4. Algorithm Lock

Required docs:
- 04-ALGORITHMS\ALGORITHM_LOCK.md

Check:
- Barcode scan algorithm defined
- Cart merge algorithm defined
- Fast Add Product algorithm defined
- Complete Sale algorithm defined
- Void Sale algorithm defined
- Report algorithm defined
- Backup algorithm defined

Status:
PENDING REVIEW

---

# 5. UI/UX Lock

Required docs:
- 05-UI-UX\UI_LOCK.md

Check:
- Cashier screen defined
- Fast Add Product screen defined
- Payment screen defined
- Shortcuts defined
- UI style rules defined

Status:
PENDING REVIEW

---

# 6. Hardware Lock

Required docs:
- 06-HARDWARE\HARDWARE_LOCK.md

Check:
- Keyboard-wedge scanner selected
- ESC/POS printer selected
- LAN printer preferred
- FileReceiptPrinter planned
- Printer failure rules defined

Status:
PENDING REVIEW

---

# 7. Reports Lock

Required docs:
- 07-REPORTS\REPORTS_LOCK.md

Check:
- Daily report defined
- Monthly PDF report defined
- Product report defined
- Low-stock report defined
- Void/refund report defined
- CSV export defined

Status:
PENDING REVIEW

---

# 8. Testing Lock

Required docs:
- 08-TESTING\TESTING_LOCK.md

Check:
- Product tests defined
- Fast Add tests defined
- Barcode tests defined
- Checkout tests defined
- Printer tests defined
- Void tests defined
- Backup tests defined

Status:
PENDING REVIEW

---

# 9. Deployment Lock

Required docs:
- 09-DEPLOYMENT\DEPLOYMENT_LOCK.md

Check:
- Production data path defined
- First-run setup wizard defined
- Backup policy defined
- Client handoff checklist defined
- Legal/compliance caution included

Status:
PENDING REVIEW

---

# 10. Client Customization Lock

Required docs:
- 10-CLIENT-CUSTOMIZATION\CUSTOMIZATION_LOCK.md

Check:
- Store profile customization defined
- Receipt customization defined
- Product custom fields defined
- Inventory behavior settings defined
- Role customization defined

Status:
PENDING REVIEW

---

# Final Gate

Only start code after:
- All lock docs reviewed
- Any changes patched
- Final stack accepted
- V1 scope accepted
- No extra features sneaked into v1

Next coding step after approval:
Create solution/project skeleton only.

---

# FINAL REVIEW PATCH ITEMS

Before code, confirm these are accepted:

- Receipt number sequence table/rule
- Cashier shift/day close
- Product CSV import/export
- Error logs
- V1 void-only rule
- V2 refund/partial refund rule
- V1 tax mode OFF by default
- No BIR compliance claim

Status:
PATCHED, PENDING FINAL ACCEPTANCE.

