# RMO-BIR-M1 Decision Document

**Date:** 2026-07-09  
**Branch:** `KIRCH-RMO-BIR-M1`  
**Base Sandbox:** `1f74cd8b`  

---

## 1. Is Salryn currently RMO/BIR ready?

**No.**

Salryn is **not** BIR-accredited, **not** RMO 24-2023 complete, and **must not** be described as compliance-ready or certifiable.

There is a substantial **engineering foundation** (machine profile, official sequences, electronic journal, X/Z services, snapshot drafts), but:

- Live host checkout does **not** issue official accountable documents.  
- E-journal is **not** proven on live official sales.  
- Product UI/reports/print path for official samples are incomplete.  
- External accreditation (eAccReg, TWG demo, Certificate of Accreditation) is outside software alone.

---

## 2. Can Reports/Crystal start now?

**Crystal Reports: no — decision not ready.**

**Reports lane: only after data contracts.**

| Question | Answer |
|----------|--------|
| Start Crystal now? | **No** |
| Start REPORTS-M1 templates now? | **No** until RMO-BIR-M2 freezes official snapshot / X-Z / EJ export contracts |
| Prefer engine? | Defer; likely text/HTML/PDF first unless requirements force Crystal |

Roadmap remains: matrix (done) → contracts/integrity (M2) → reports.

---

## 3. Is there an implementation blocker?

**Yes — multiple product blockers before official output:**

| Blocker | Why |
|---------|-----|
| Official numbering not on live checkout | POS allocator ≠ BIR sequential series |
| No official document generation path | No issued SI/OR product path |
| E-journal not live-integrated | F-e incomplete |
| Void/refund/cancel incomplete for official integrity | F-b / supplementary docs |
| Unresolved Annex F items (push, seal, retention years) | Need full official text before coding those features |

**Non-blockers for continued internal POS:** LAN/Remote POS and host checkout for **non-official** retail use remain on Sandbox as product candidates, with explicit non-claim of BIR official output.

---

## 4. What should be the next implementation task?

**Only one recommended next task:**

### RMO-BIR-M2 — Official transaction numbering, taxpayer/machine profile gate, and receipt/report data contract study (with integrity spine)

**In scope (study-first; minimal implementation only if contracts require):**

1. Official document number series contract vs host POS numbers.  
2. Active `bir_machine_profiles` gate for any future official mode.  
3. Official document snapshot / journal append contracts tied to sale completion (design).  
4. Void/refund/cancel + reprint event contracts.  
5. Grand total and training-mode policy notes.  
6. Explicit **no official claim** UI/docs guards if missing.

**Out of scope for M2:**

- Crystal / PDF / printer productization  
- Full REPORTS-M1 template library  
- Licensing  
- Sandbox promotion  
- Installer work on installer branch  

---

## 5. Decision table (exact style)

| Decision | Value |
|----------|--------|
| Salryn RMO/BIR ready | **no** |
| Official output implemented | **no** |
| Crystal Reports decision ready | **no** |
| Implementation blocker | **yes** |
| Next task | **RMO-BIR-M2 — Official transaction numbering, taxpayer/business/machine configuration, and receipt/report data contract study** |

---

## 6. Honest limits of this decision

- M1 used **official BIR CDN sources** (RMO 24-2023 full + digest, related RRs/RMCs listed in register).  
- Full Annex F line-by-line TWG checklist was **not** completely transcribed.  
- Retention periods and eSales “push” details remain **unresolved** until deeper official read.  
- This is **not** a legal opinion.
