# RMO-BIR-M9 Live Official Posting Design

**Date:** 2026-07-10  
**Branch:** `KIRCH-RMO-BIR-M9-FULL-CLOSURE`  

**Status:** Design only + always-blocked candidate skeleton. **Not production-enabled.**  
**Legal gate:** Incomplete (see `RMO_BIR_M9_LEGAL_SOURCE_CLOSURE.md`). Live checkout binding is **STOP**.

**Disclaimer:** Internal design. No compliance claim. No official issuance.

---

## Target future flow (host-local only)

1. **Host checkout final post point**  
   Operational sale is posted first (existing POS path: sale + inventory + operational receipt number + shift linkage).  
   *Official path is a separate subsequent unit of work — not draft/cart allocation.*

2. **Validate official setup**  
   Business + machine + active series + receipt profile complete; compliance mode not falsely claiming accreditation; output not enabled without gates.

3. **Validate active document series**  
   Active, not exhausted, padding ≥ 6, document kind matches, host-local scope only.

4. **Allocate official number atomically**  
   Single SQLite transaction; sequence row locked/updated; number never previewed as issued until commit.

5. **Create immutable official sale record**  
   Header + lines + tax buckets + tender snapshot + official number + link to operational `sale_id`. No update-in-place after commit.

6. **Append EJ event in same transaction**  
   EJ row references official sale id + official number + hash/evidence payload as designed by EJ ports.

7. **Link audit event**  
   Host audit/evidence trail for who/when/terminal.

8. **Generate output package**  
   Only after commit of official sale + EJ. Failure here **must not** re-allocate a new number; retry uses same official sale.

9. **Mark output generated**  
   Delivery/reprint ledger flags; original content immutable.

10. **Crash / failure states**  
    - Fail before allocate: no official number consumed.  
    - Fail after allocate+sale+EJ commit, before output: official sale exists; output retry only.  
    - Never leave “orphan” allocated numbers without official sale row (allocate + insert official sale + EJ in one transaction).

11. **Reprint**  
    Same official number; mark **REPRINT** + reprint timestamp; no sequence advance.

12. **Void / refund**  
    New adjustment document series (distinct from SI/OR); reason + authorization; reference original official number; EJ append; original remains immutable.

13. **Z-reading**  
    Range of official numbers + day totals + grand total / reset counter from official accumulator; not from operational-only sales alone once official mode is live.

14. **eSales / export hooks**  
    Outbox from committed official sales only; format TBD after eSales class closure.

---

## Invariants

| Invariant | Rule |
|-----------|------|
| Number reuse | Official number never reused after committed allocation |
| Draft allocation | No draft/cart allocation |
| Remote allocation | No remote client / Admin-Owner remote official posting |
| Immutability | Posted official sale immutable; correction by reversal only |
| EJ link | EJ append transactionally linked to official post |
| Output failure | Does not allocate a new number |
| Reprint | Does not allocate |
| Void/refund | Reason + authorization required; distinct series |
| Backup/restore | Must preserve sequence + official sale integrity |

---

## Guarded candidate (M9)

- Contracts: `OfficialLivePostingCandidateContracts.cs`
- Service: `IHostOfficialLivePostingCandidateService` / `HostOfficialLivePostingCandidateService`
- **Disabled by default**
- **Not called by checkout**
- **No UI live button**
- Always returns **blocked** while legal/source gates incomplete and activation flag absent
- `liveOfficialPostingAllowed = false` always in responses
- Source-gated: no Remote POS / LAN official route

---

## What M9 does not implement

- Schema for official sale tables  
- Atomic allocate+EJ live path  
- Checkout wiring  
- Printer/PDF/Crystal legal output  
- eSales push  
