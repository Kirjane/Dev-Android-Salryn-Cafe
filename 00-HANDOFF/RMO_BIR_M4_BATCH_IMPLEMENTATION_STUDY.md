# RMO-BIR-M4-BATCH Implementation Study

**Date:** 2026-07-09  
**Branch:** `KIRCH-RMO-BIR-M4-BATCH`  
**Base M3:** `f0725eb5`  

**Disclaimer:** Setup/admin only. No official output. No compliance claim.

---

## Decision: **GO IMPLEMENT**

| Gate | Result |
|------|--------|
| Schema path clear | **Yes** — reuse `bir_machine_profiles` + `bir_official_document_sequences`; add `official_receipt_profiles` only |
| Host-local only | **Yes** |
| Readiness stays non-official | **Yes** — `liveOfficialPostingAllowed` always false |
| No checkout mutation | **Yes** |
| No fake compliance | **Yes** — setup forces `NOT_ACCREDITED` + `official_output_enabled=0` |

---

## A. Existing schema

| Object | Usable? | Role |
|--------|---------|------|
| `bir_machine_profiles` | **Yes** | Business + machine setup (taxpayer, TIN, serial, MIN, PTU, AN, software) |
| `bir_official_document_sequences` | **Yes** | Document series setup (prefix, year, start, current, padding) |
| New `official_receipt_profiles` | **Add** | Footer/legal/marker text (not on machine table) |

**Migration:** Version **13** — receipt profile table only. No sales/checkout table changes.

**Reuse:** `SqliteOfficialCheckoutReadinessService`, M3 contracts, host-local guard pattern.

**Audit:** Store `updated_by_user_id` on receipt profile; machine/sequence use `updated_at`. Full audit-log write deferred if no simple write port (document partial).

---

## B–F. Setup objects / API / UI / readiness / audit

See implementation handoff `RMO_BIR_M4_BATCH_OFFICIAL_SETUP.md`.

### Critical setup rules

1. Setup API **never** sets `compliance_mode = ACCREDITED`.  
2. Setup API **never** sets `official_output_enabled = 1`.  
3. Series save may set `start_number` / `current_number` (current = start − 1) but **never** allocates during checkout.  
4. Readiness may report `ConfiguredForInternalDryRun` when required setup fields filled; product spine blockers remain.  
5. UI banner: not compliant / official output not implemented.
