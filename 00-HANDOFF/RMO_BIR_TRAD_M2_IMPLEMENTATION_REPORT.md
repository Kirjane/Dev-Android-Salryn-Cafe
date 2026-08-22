# RMO-BIR TRAD-M2 Implementation Report

**Date:** 2026-07-10  
**Branch:** `KIRCH-RMO-BIR-TRAD-M2-ATOMIC-OFFICIAL-POSTING`  
**Base:** TRAD-M1 `121b7838f09d04db0f7224fcab3d9b382d04f1ad`  

**Disclaimer:** Engineering only. Not BIR accreditation. Not authority to issue official invoices.

---

## Decision

**GO — HARDEN + WIRE TAX SPINE + ACTIVATION DEFAULT-OFF HOST FACADE**

Did **not** invent a second posting engine. Existing `SqliteOfficialCheckoutPersistenceService` shared-transaction path remains the atomic core.

---

## What shipped

1. **Tax-mode fail-closed on atomic persistence** — requires known taxpayer mode + matching line classifications before allocate/sale/EJ.  
2. **`ProductionOfficialPostingActivation`** — master switch default **OFF**.  
3. **`HostAtomicOfficialPostingService`** — host facade blocks with `AtomicOfficialPosting.ActivationDisabled` when off; freezes `OfficialTaxPolicySnapshot` when allowed; requires composed executor.  
4. **Study + gates** — `rmo-bir-trad-atomic-posting` + full CoreSmoke.  

Existing J21 runtime smoke still proves allocate+sale+EJ commit and mid-path rollback (no orphan sequence).

---

## Gates

| Gate | Result |
|------|--------|
| `rmo-bir-trad-atomic-posting` | pass |
| Full CoreSmoke | pass |

---

## Out of scope (later)

- Checkout UI wire to live posting  
- Real invoice print/PDF (TRAD-M3)  
- Void/refund docs (TRAD-M4)  
- X/Z/eSales (TRAD-M5)  
- Production activation flip  

---

## Non-claims

Activation remains OFF. No compliance/accreditation/PTU claim. Sandbox not promoted. BIR not submitted.
