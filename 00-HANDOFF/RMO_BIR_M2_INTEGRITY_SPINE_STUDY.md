# RMO-BIR-M2 Integrity Spine Study

**Date:** 2026-07-09  
**Branch:** `KIRCH-RMO-BIR-M2`  
**Sources:** S2 F-b, F-c, F-e, F-i; existing EJ hardening; LAN official-remote forbid  
**Disclaimer:** Design only.

---

## 1. Goals

Protect **official** sales data so that:

- Posted documents cannot be silently edited.  
- Corrections are **traceable reversals**.  
- Electronic journal and audit capture critical events.  
- Only the **Store Host** may post official documents.  
- Failure mid-post does not leak orphan official numbers or half-written sales.

---

## 2. Proposed invariants

| ID | Invariant |
|----|-----------|
| I-1 | Posted official sale body is **immutable**. |
| I-2 | Corrections only via **void / refund / cancel records** linking original official number. |
| I-3 | Void/refund requires **privileged role + reason code**. |
| I-4 | Every official post appends **EJ SALE_POSTED_OFFICIAL** in same TX. |
| I-5 | Reprint increments reprint count + EJ event; **same** official number. |
| I-6 | EJ tables remain **append-only** (existing triggers). |
| I-7 | No official post from LAN remote clients. |
| I-8 | No Admin/Owner remote official commands. |
| I-9 | Training mode cannot set official markers or allocate official numbers. |
| I-10 | Sequence allocate + sale insert + EJ append are **atomic**. |
| I-11 | Backup/restore preserves sequences, sales, EJ, Z readings together. |
| I-12 | Reports regenerate from **canonical posted data**, not editable UI state. |

---

## 3. Void / refund / cancel authorization

| Action | Min role (proposal) | Requires reason | Creates new official #? |
|--------|---------------------|-----------------|-------------------------|
| Void posted official sale | Manager/Admin (not plain cashier alone) | Yes | No (void marker/doc) |
| Refund | Manager/Admin | Yes | Refund doc may need **own series** — **open** |
| Cancel draft | Cashier | Optional | N/A |
| Reprint | Cashier+ | Optional reason | No |

**Open (compliance):** Whether refund is supplementary document under separate series — needs human review; engineering must not invent legal form.

---

## 4. Hash / tamper-evident options

| Option | Pros | Cons | M3? |
|--------|------|------|-----|
| Snapshot content hash (SHA-256 of canonical JSON) | Detects bit-flip / silent edit | Not full chain | **Yes recommended** |
| Hash chain over EJ sequence | Stronger examiner story | Complexity | Later |
| Signed seal (F-k) | Matches “validation seal” language | Spec **unresolved** | **No** until Annex F |

---

## 5. Failure cases

| Case | Expected behavior |
|------|-------------------|
| Crash mid-post TX | Rollback; no sale; no number advance |
| Print fails after post | Sale posted; reprint; no re-allocate |
| Power loss after commit | Data durable on disk (F-d software interpretation) |
| DB restore from backup | Sequences/EJ/sales consistent as of backup time |
| Malicious UPDATE on sale | Blocked by app + future triggers if official |
| Remote client posts “official” | Rejected by gate |

---

## 6. Minimum future schema / service requirements

| Component | Need |
|-----------|------|
| Official post orchestrator | Host-only service coordinating sale + allocate + EJ |
| Reason codes table or enum | Void/refund/config |
| Snapshot store or immutable columns | Denormalized header at post |
| Optional sale content hash | Column or EJ payload |
| Grand total accumulator | Separate counter table (later) |
| Profile change audit | History rows |

**M2 implements none of these.** M3 may add **types and readiness checks** without migrations if possible.

---

## 7. Test gates needed later

| Gate idea | Asserts |
|-----------|---------|
| `official-number-no-reuse` | Post twice → distinct numbers |
| `official-draft-no-allocate` | Cart never touches sequence |
| `official-post-atomic` | Forced failure → no partial |
| `official-reprint-same-number` | Reprint count++ same # |
| `official-remote-forbidden` | LAN cannot call official post |
| `official-ej-append-on-post` | EJ row exists after post |
| `official-immutable-body` | UPDATE sale blocked or fails service |

---

## 8. Shared SQLite / LAN

| Rule | Status |
|------|--------|
| Clients must not open shared SQLite over LAN | Architecture lock — keep |
| Host owns writes | Keep |
| Remote POS non-official only | Keep |

---

## 9. Report regeneration

X/Z/backend reports must read **posted sales + payments + voids + sequences**, never live cart UI.  
Z finalized rows are first-class sources for closed periods.
