# Salryn Final Review Findings

Status:
FINAL PLANNING REVIEW PATCH.

Date:
2026-06-07

Result:
Planning pack is structurally clean and still no-code, but several product and engineering gaps should be patched before creating the C# solution skeleton.

---

# 1. Confirmed Good

The following are already strong:
- Native offline-first direction
- C# + WinForms + SQLite stack
- Layered architecture
- Services/repositories separation
- Fast Add Product as core feature
- Inventory ledger design
- Receipt snapshot rule
- Sale saved before printing
- No-code planning gate
- Build order exists

---

# 2. Issues Found

## 2.1 Receipt Number Sequencing Missing

Problem:
Receipt number generation was mentioned in algorithms, but the database did not define a durable sequence mechanism.

Risk:
Receipt numbers may duplicate, skip unpredictably, or become hard to audit.

Fix:
Add receipt_sequences table and receipt number generation rules.

---

## 2.2 Cashier Shift / Day Close Missing

Problem:
Salryn has users and cash payments, but no shift/session concept.

Risk:
Owner cannot compare expected cash vs counted cash at the end of the day.

Fix:
Add lightweight cashier_sessions and cash count workflow.

---

## 2.3 Error Logs Missing

Problem:
Audit logs track business actions, but technical errors need separate logging.

Risk:
Support/debugging becomes painful.

Fix:
Add error_logs planning and friendly error handling.

---

## 2.4 Product CSV Import/Export Missing

Problem:
Fast Add is good, but entering hundreds of products manually is painful.

Risk:
Initial client setup becomes slow.

Fix:
Add product CSV import/export as V1 support feature.

---

## 2.5 Refund vs Void Boundary Unclear

Problem:
Documents mention REFUNDED/PARTIAL_REFUND, but V1 feature lock only truly supports void sale.

Risk:
Developer may half-build refunds and complicate v1.

Fix:
V1 supports VOID only. Refunds are V2 unless explicitly pulled forward later.

---

## 2.6 Tax/BIR Boundary Needs Clarity

Problem:
Database has tax_cents, but tax behavior is not defined.

Risk:
Accidental claim of tax compliance or incorrect invoice behavior.

Fix:
V1 tax mode is OFF/SIMPLE DISPLAY ONLY. No BIR-compliance claim.

---

## 2.7 Build Order Needs Patch Note

Problem:
Build order has 26 milestones but detailed sections group some work into 21 larger phases.

Risk:
Confusion during implementation.

Fix:
Clarify that the 26-item milestone list is the source of truth, and detailed phases are grouped sections.

---

# 3. Final Review Decision

Patch docs now.
Do not create code yet.
Do not create database schema yet.
Do not create .sln yet.

After patch:
Review READY_TO_CODE_CHECKLIST.md again.
Then proceed to project skeleton only.
