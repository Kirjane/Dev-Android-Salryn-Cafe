# RMO-BIR TRAD-M3 — GO/STOP Decision

**Date:** 2026-07-10  
**Decision:** **GO WITH NARROW FIX — NO MIGRATION**

## Why GO

1. Immutable snapshot already committed in EJ payload with SHA-256.  
2. Tax buckets (VAT and SSPT/exempt) are already in snapshot when M1/M2 path is used.  
3. Renderer can be pure Core + SQLite read adapter without inventing legal identifiers.  
4. Activation remains OFF; checkout remains unbound.

## Why not STOP

- Primary field matrix is sufficiently known for engineering projection with fail-closed missing-data rules.  
- Incomplete historical rows without tax buckets are rejected for production-capable mode.

## Non-claims

Rendering does not authorize official invoice issuance or accreditation.
