# Salryn Phase 37 POS Checkout Product Picker Boundary Read-Only Audit

Status:
PHASE 37 READ-ONLY AUDIT COMPLETED.

Target:
POS Checkout Product Picker Boundary.

Purpose:
- Inspect the Phase 36 draft transaction builder.
- Inspect the checkout application boundary.
- Inspect product read models and product repository contracts.
- Inspect barcode lookup and product lookup surfaces.
- Inspect current smoke flow before adding product picker boundary coverage.

Planned Phase 37 Scope:
- Add a product picker boundary for future POS checkout UI.
- Boundary should convert product read models into PosCheckoutDraftLineInput.
- Boundary should support product selection by product lookup data.
- Boundary should support barcode/product lookup preparation without UI.
- Boundary should validate inactive or invalid products if the read model exposes such data.
- Boundary should preserve product name snapshot.
- Boundary should preserve barcode snapshot.
- Boundary should preserve unit name snapshot.
- Boundary should preserve selling price.
- Boundary should preserve cost price when available.
- Boundary should preserve track-stock flag.
- Boundary should use default quantity of 1 unit unless caller specifies quantity.
- Boundary should not write to database.
- Boundary should not call SQLite directly.
- Boundary should not complete a sale.
- Boundary should not enforce cashier sessions.
- Boundary should not create checkout UI.
- Boundary should not print receipts.
- Boundary should not create reports.

Recommended Phase 37 Files:
- src/Salryn.Core/Services/PosCheckoutProductPickerBoundary.cs
- tools/Salryn.Tools.InfrastructureSmoke/PosCheckoutProductPickerBoundarySmokeChecks.cs
- 00-HANDOFF/PHASE37_POS_CHECKOUT_PRODUCT_PICKER_BOUNDARY_AUDIT.md

Recommended Concepts:
- PosCheckoutProductPick
- PosCheckoutProductPickerBoundary
- Product-to-draft-line mapping

Recommended Smoke Coverage:
- Convert tracked product lookup into draft line input.
- Convert untracked product lookup into draft line input.
- Default quantity is 1000 x1000.
- Custom quantity is preserved.
- Product name snapshot is trimmed.
- Barcode snapshot is preserved or null.
- Unit name snapshot is preserved.
- Selling price maps to UnitPriceCents.
- Cost price maps to CostPriceCents.
- TrackStock maps correctly.
- Invalid product ID fails.
- Empty product name fails.
- Empty unit name fails.
- Invalid quantity fails.
- Negative price fails.
- No database touched.

Rules:
- No production database touched.
- No database schema changes.
- No SQL files created.
- No UI created.
- No receipt printer logic created.
- No reports logic created.
- This audit is read-only except this handoff file.
