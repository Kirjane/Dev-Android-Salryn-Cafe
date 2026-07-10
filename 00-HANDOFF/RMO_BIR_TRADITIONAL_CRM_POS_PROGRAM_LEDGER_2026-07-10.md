# SALRYN TRADITIONAL CRM/POS ACCREDITATION-READINESS — PROGRAM LEDGER

**Date:** 2026-07-10  
**Role:** Running program ledger for Grok CLI Traditional CRM/POS completion program  
**Disclaimer:** Engineering program status only. Not BIR compliance, accreditation, CoA, PTU, or authority to issue official invoices.

---

## 1. Program stamp (observed)

| Item | Value |
|------|--------|
| Local repo | `C:\Users\kirch\Salryn` |
| Predecessor base | `KIRCH-RMO-BIR-QA1-REAL-MACHINE-DB-UPGRADE-BACKUP-RESTORE` |
| Predecessor SHA | `ef36a3f0a3816b44c4b2b13bbfa34460eab29daf` |
| Active engineering branch | `KIRCH-RMO-BIR-TRAD-M1-TAXPAYER-MODE-AND-INVOICE-POLICY-SPINE` |
| origin/M11 | `6d113697c6ae308cc423c0bd35c59381dd74089b` |
| origin/Sandbox | `1f74cd8bcaca1a5df487aa61677ee9829dd7fedb` |
| origin/Installer | `6b9c46676390b8a86a989eca600cb02e406ce865` |
| Sandbox promotion | **Not performed** |
| BIR submission | **Not performed** |

Verified at program start (after fetch):

- QA1 HEAD = origin/QA1 = `ef36a3f0…`
- M11 → QA1: **0 behind / 3 ahead**
- Sandbox → QA1: **0 behind / 14 ahead**
- Worktree clean before TRAD-M1 branch create

---

## 2. Roadmap status

| Stage | Milestone | Status |
|-------|-----------|--------|
| 0 | Program ledger + source stamp + source register | **DONE** |
| 0.5 | QA1 manual Official Setup UI acceptance | **PENDING HUMAN (Kirch)** — automated QA1 green |
| 1 | TRAD-M1 taxpayer mode + invoice policy spine | **DONE (engineering)** — see implementation report |
| 2 | TRAD-M2 atomic official posting | Pending |
| 3 | TRAD-M3 VAT/Non-VAT Invoice output | Pending |
| 4 | TRAD-M4 reprint/void/refund/adjustment lifecycle | Pending |
| 5 | TRAD-M5 X/Z, EJ export, traditional eSales | Pending |
| 6 | TRAD-M6 installer/demo candidate validation | Pending |
| 7 | TRAD-M7 internal Annex F demonstration | Pending |
| 8 | TRAD-M8 eAccReg package prep (no submit) | Pending |

---

## 3. Stage 0.5 — QA1 manual UI

Automated engineering (migrations, backup/restore, non-mutation, CoreSmoke) is **green** on QA1 (`ef36a3f0…`).

Manual Official Setup checklist remains **NOT EXECUTED** by CLI (requires Kirch visual confirmation on host UI). Engineering milestones may continue with honest labeling; **full program completion and Annex F still require Kirch PASS** on this checklist (see `RMO_BIR_QA1_FINAL_REPORT.md` §7).

---

## 4. Branch chain (to be updated as stages complete)

1. `KIRCH-RMO-BIR-M11-GUARDED-LIVE-POSTING-CANDIDATE-HARDENING` @ `6d113697…`
2. `KIRCH-RMO-BIR-QA1-REAL-MACHINE-DB-UPGRADE-BACKUP-RESTORE` @ `ef36a3f0…`
3. `KIRCH-RMO-BIR-TRAD-M1-TAXPAYER-MODE-AND-INVOICE-POLICY-SPINE` @ *(see git tip after push)*

---

## 5. Non-claims (always)

- Not BIR compliant / accredited / certified  
- Not authorized to issue official invoices  
- Not waiting for BIR inspector/TWG  
- No Sandbox promotion  
- No real eAccReg submission from this program without Kirch authorization  

---

## 6. Next action

TRAD-M2 atomic official posting on a new branch from TRAD-M1 tip.  
Kirch still owes QA1 manual Official Setup UI PASS/FAIL when available.
