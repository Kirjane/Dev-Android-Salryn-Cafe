# RMO-BIR TRAD-M3-RC2 — Snapshot Capture and Wording Closeout Study

**Date:** 2026-07-10  
**Base RC1:** `b699fcab8f2b4c451d5f44bb190c208a0f3033e0`  
**Decision:** **GO**

## Defects closed
1. Wire Buyer/Statutory through HostAtomic → Sqlite persistence → SnapshotDraftBuilder → evidence JSON.
2. Explicit V2 schema when buyer/statutory present; V1 when absent (historical hashes preserved).
3. Official Setup wording distinguishes internal projection vs production issuance.
4. Canonical gate: `rmo-bir-trad-invoice-output-delivery` (+ alias `...-rc1` if useful) and new RC2 gate.

## Non-claims
No production activation; no BIR accreditation claim.
