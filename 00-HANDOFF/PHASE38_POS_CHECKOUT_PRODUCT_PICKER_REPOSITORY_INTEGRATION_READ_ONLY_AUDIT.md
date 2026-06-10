# Salryn Phase 38 POS Checkout Product Picker Repository Integration Read-Only Audit

Status:
PHASE 38 READ-ONLY AUDIT COMPLETED.

Target:
POS Checkout Product Picker Repository Integration Smoke.

Purpose:
- Inspect product write repository behavior.
- Inspect product read repository behavior.
- Inspect product lookup/search/barcode surfaces.
- Inspect Phase 37 product picker boundary.
- Inspect Phase 36 draft transaction builder.
- Inspect current smoke tool flow before adding repository integration smoke.

Planned Phase 38 Scope:
- Add smoke coverage proving real SQLite product repositories can feed checkout product picker.
- Create products using SqliteProductWriteRepository.
- Read products back using SqliteProductReadRepository.
- Find product by ID.
- Find product by barcode.
- Search product list.
- Convert ProductLookupReadModel into PosCheckoutDraftLineInput using PosCheckoutProductPickerBoundary.
- Add converted line into PosCheckoutDraftTransactionBuilder.
- Verify tracked-stock line mapping.
- Verify untracked/non-stock line mapping if supported by product model.
- Verify product name, barcode, unit, selling price, cost price, quantity, discount, and TrackStock mapping.
- Verify inactive product is not selectable through boundary if surfaced.
- Reset dev database at the end through smoke tool flow.

Out of Scope:
- No checkout UI.
- No sale completion.
- No cashier session enforcement.
- No receipt printing.
- No schema migration.
- No SQL file creation.
- No production database.

Recommended Phase 38 Files:
- tools/Salryn.Tools.InfrastructureSmoke/PosCheckoutProductPickerRepositoryIntegrationSmokeChecks.cs
- 00-HANDOFF/PHASE38_POS_CHECKOUT_PRODUCT_PICKER_REPOSITORY_INTEGRATION_AUDIT.md

Recommended Smoke Command:
- pos-checkout-product-picker-repository-integration

Recommended Smoke Coverage:
- Initialize dev database.
- Bootstrap admin/store if existing smoke patterns require it.
- Read default unit from catalog repository.
- Add tracked product with barcode and cost price.
- Add untracked product without barcode and without cost price.
- Find tracked product by ID.
- Find tracked product by barcode.
- Search product list by product name/barcode.
- Convert ProductLookupReadModel to draft line input.
- Add draft line input into PosCheckoutDraftTransactionBuilder.
- Verify totals after adding tracked line.
- Convert ProductListItemReadModel to draft line input.
- Add second draft line.
- Verify totals after adding second line.
- Verify no sale was completed.
- Verify no stock movement was created by picker/draft only.
- Cleanup/reset dev database.

Rules:
- No production database touched.
- No database schema changes.
- No SQL files created.
- No UI created.
- No receipt printer logic created.
- No reports logic created.
- This audit is read-only except this handoff file.
