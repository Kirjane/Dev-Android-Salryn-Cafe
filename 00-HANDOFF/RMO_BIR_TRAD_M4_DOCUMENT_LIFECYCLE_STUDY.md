# RMO-BIR TRAD-M4 — Document Lifecycle Study

**Base:** RC2 `d7d5224a103b08e650d61d0ce0dc0ca9e7ce44a1`  
**Decision:** **GO — HOST LIFECYCLE ON EXISTING EJ APPEND-ONLY EVENTS**

## Architecture
- Reprint: existing `OfficialDocumentReprintService` + invoice re-render with REPRINT marker; **same official number**.
- Void/Refund: append-only EJ events (`VOID`/`REFUND` markers) with reason + actor + original journal identity; **never rewrite original entry**; **do not advance SI/OR sequence**.
- Adjustment document series: reserved for future separate series allocation when setup provides ADJUSTMENT kind; M4 records lifecycle events first.
- Activation OFF: lifecycle mutations remain host-local engineering paths; production issuance still blocked.

## Non-claims
Not BIR accreditation. Not live production void/refund accounting module complete for all industries.
