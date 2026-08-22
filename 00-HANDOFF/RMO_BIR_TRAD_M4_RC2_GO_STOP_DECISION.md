# M4-RC2 GO/STOP Decision

**Base:** M4-RC1 `115ad23f62ab64e0d8adb62e9c6cda3e131bfaa5`  
**Date:** 2026-07-10

## Decision

**GO WITH SOURCE-LOCKED ADJUSTMENT OUTPUT**

## Rationale

1. Operational lifecycle (reprint, void, line-level refund/return, payment settlement, tax reversal evidence, concurrency, cancel-before-commit) can be completed with deterministic engineering rules already used by M1 tax spine.
2. Legal **official** names/series for adjustment documents (credit memo vs debit note vs return invoice, etc.) are **not** productized from an authoritative BIR CRM/POS rule set in-repo.
3. Therefore:
   - Produce immutable **INTERNAL ADJUSTMENT DOCUMENT** snapshots with hashes and original-invoice linkage;
   - **Do not invent** legal document kind/series/permit wording;
   - Fail closed on any claim of CoA/PTU/legal adjustment naming.

## Stop conditions (do not advance to M5)

- Authoritative BIR eSales schema still unconfirmed (M5 concern — out of scope here).
- Sandbox/installer must remain untouched.
- Production activation remains OFF.

## Primary fix targets from M4-RC1

| Defect | RC2 response |
|--------|----------------|
| Money-ratio stock return | Line/qty selection + disposition |
| Placeholder refund lines | Real sale_item_id + qty + tax components |
| No payment accounting | Settlement table + cash expected-cash update |
| No tax reversal persistence | Line + bucket reversal columns |
| Cancel not wired | Pre-commit only via draft path; post-commit reject |
| Adjustment absent | Internal adjustment document + hash |
| Fake optimistic concurrency | WHERE state_version = expected |
| Idempotent reprint loses output | Deterministic re-render from immutable snapshot |
| Single VAT test only | Full VAT/non-VAT runtime matrix |
