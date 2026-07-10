# RMO-BIR TRAD-M1 — GO/STOP Decision

**Date:** 2026-07-10  
**Decision:** **GO WITH MIGRATION v15**

## Why GO

1. `VatRegistrationMode` is already in contracts but hard-coded null on read — persistence is mandatory before atomic posting.  
2. Non-VAT MSMEs require SSPT vs exempt separation; collapsing all non-VAT lines is not defensible against sample invoice formats.  
3. Invoice-first policy must be an explicit versioned object, not tribal knowledge in UI strings.  
4. Migration is a single nullable column on `bir_machine_profiles` with fail-closed app validation — low blast radius.  

## Why not STOP

No unresolved legal invention is required for this spine:

- modes and buckets map to known VAT / non-VAT invoice distinctions under RR 7-2024 / RMC 77-2024 orientation  
- non-VAT statutory threshold is **not** buried as a permanent checkout constant  
- EIS remains out of scope  

## Stop conditions remaining for later stages

- Traditional eSales exact schema unavailable (TRAD-M5)  
- Physical printer evidence unavailable (TRAD-M3/M6)  
- Applicant identity documents missing (TRAD-M8)  
- QA1 manual UI FAIL that cannot be narrow-fixed  

## Non-claims

Implementation of TRAD-M1 does **not** authorize official invoice issuance, accreditation, or PTU use.
