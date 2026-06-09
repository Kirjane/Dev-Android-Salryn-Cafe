# Salryn Phase 36 POS Checkout Draft Transaction Builder Read-Only Audit

Status:
PHASE 36 READ-ONLY AUDIT COMPLETED.

Target:
POS Checkout Draft Transaction Builder.

Purpose:
- Inspect existing checkout core service.
- Inspect existing checkout application boundary.
- Inspect available sale, item, payment, stock movement, money, quantity, barcode, and receipt models.
- Inspect available product/read model shape for future UI cart usage.
- Inspect smoke command flow before adding builder smoke coverage.

Planned Phase 36 Scope:
- Add a draft transaction/cart builder that lives before PosCheckoutApplicationService.
- Builder should hold draft checkout lines.
- Builder should support adding lines.
- Builder should support removing lines.
- Builder should support changing quantity.
- Builder should support line discount.
- Builder should support sale-level discount.
- Builder should support tax amount.
- Builder should support payment inputs.
- Builder should calculate subtotal, total, amount paid, and change.
- Builder should produce PosCheckoutApplicationRequest.
- Builder should not write to database.
- Builder should not know SQLite.
- Builder should not enforce cashier sessions directly.
- Builder should not create checkout UI.
- Builder should not print receipts.
- Builder should not create reports.

Recommended Phase 36 Files:
- src/Salryn.Core/Services/PosCheckoutDraftTransactionBuilder.cs
- tools/Salryn.Tools.InfrastructureSmoke/PosCheckoutDraftTransactionBuilderSmokeChecks.cs
- 00-HANDOFF/PHASE36_POS_CHECKOUT_DRAFT_TRANSACTION_BUILDER_AUDIT.md

Recommended Builder Concepts:
- PosCheckoutDraftLine
- PosCheckoutDraftPayment
- PosCheckoutDraftTotals
- PosCheckoutDraftTransaction
- PosCheckoutDraftTransactionBuilder

Recommended Smoke Coverage:
- Empty draft has zero totals.
- Add tracked stock line.
- Add untracked stock line.
- Merge same product/price/discount line or explicitly keep separate depending chosen rule.
- Update quantity.
- Remove line.
- Set line discount.
- Reject discount greater than line gross.
- Set sale discount.
- Set tax.
- Add cash payment.
- Reject insufficient payment when building final request.
- Build PosCheckoutApplicationRequest.
- Verify request maps correct line inputs and payment inputs.
- Verify tracked/untracked flag is preserved.
- No database touched.

Rules:
- No production database touched.
- No database schema changes.
- No SQL files created.
- No UI created.
- No receipt printer logic created.
- No reports logic created.
- This audit is read-only except this handoff file.
