# RMO-BIR-M10 Legal Source Delta (vs M9)

**Date:** 2026-07-10  
**Branch:** `KIRCH-RMO-BIR-M10-OFFICIAL-SOURCE-ACQUISITION`  
**Supersedes open rows in:** `RMO_BIR_M9_LEGAL_SOURCE_CLOSURE.md` for seal / push / eSales **source text** only.

**Disclaimer:** Engineering delta only. Not a legal opinion. Not a compliance claim. Live posting remains blocked.

---

## Summary

| Gap (M9) | M10 outcome |
|----------|-------------|
| Validation seal technical form | **Closed as source text** for subscription e-invoice/e-receipt: QR, barcode, URL, other seals (RMO 24-2023 §k) |
| Push requirement | **Closed as source text**: real-time/near real-time EIS JSON when applicable (RMO 24-2023 §j; RR 8-2022) |
| eSales / transmission | **Dual-track closed as process**: (A) traditional monthly eSales + journal/Z extract; (B) EIS JSON ≤3 days for covered taxpayers. **EIS API/JSON field schema still open** |
| Live posting allowed? | **Still no** |

---

## Full detail

See `RMO_BIR_M10_OFFICIAL_SOURCE_ACQUISITION.md`.

---

## Engineering blockers that remain (non-source)

Even with source text improved, live issuance stays blocked by product gaps, including but not limited to:

- Live official checkout bind  
- Atomic official number allocation + immutable official sale  
- Live EJ bind to official post  
- Grand total / reset counter  
- Official output engine (layout + device)  
- Void/refund live series  
- Accreditation / PTU / EIS CERT / PTT lifecycle  
- Exact EIS JSON schema for push implementation  

---

## GO / STOP snapshot

| Item | Decision |
|------|----------|
| Legal **source text** for seal/push/eSales process | **Improved / largely complete** |
| Legal **source text** for EIS JSON field schema | **Incomplete** |
| Live official posting implementation | **STOP** |
| Live EIS push implementation | **STOP** |
| Always-blocked candidate skeleton | **Keep** |
| Source-sale read-only + internal reports | **Keep** (from M9) |
| Next recommended task | **RMO-BIR-M11-GUARDED-LIVE-POSTING-DESIGN-HARDENING** *or* **RMO-BIR-QA1-REAL-MACHINE-DB-UPGRADE-BACKUP-RESTORE** if product prefers QA before design depth |

**Preferred next (legal still incomplete for EIS schema, but traditional track process is clear):**  
**RMO-BIR-M11-GUARDED-LIVE-POSTING-CANDIDATE-HARDENING** — deepen always-blocked design against dual-track model; still no checkout bind, no production activation.
