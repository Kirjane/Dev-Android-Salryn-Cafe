# Cafe cross-system coverage — Codex resume

Source examined: `77ed22b3df62a86091583ff1558a129560cf0d05`; executable Host/publication repairs: `b19f9b9739141bfa1df9f0133d00e043ee5a4dc2`; scoped offline credential enrollment: `1edfcd97`.

This is an operation-support inventory, not a parity or release PASS. `NeedsReview` is retained evidence, not canonical application. Android source wiring and emulator execution are distinct.

| Domain | Executable Local Host | Central canonical application | Android surface / transport limit |
|---|---|---|---|
| C1 menu/modifiers | Existing menu endpoints now mapped through the guarded Cafe route group | Authenticated scoped menu publication read added; no inbound menu mutation processor | Native published menu/variants/modifiers; current setup applies menu publication |
| C2 recipes/BOM | Ingredient/recipe endpoints mapped | IngredientConsumptionCommittedV1 applies; scoped recipe publication read added | Shared recipes and consumption; publication applied at setup, no recipe-management UI |
| C3 purchasing/inventory/waste/count | Existing Cafe inventory endpoints mapped | PurchaseReceiptCommittedV1, InventoryWasteCommittedV1, InventoryAdjustmentCommittedV1, InventoryCountFinalizedV1 apply | Shared services; no inventory-management UI |
| C4 orders | Revision/park/resume/submit/preparation/cancel/complete mapped; live web save/preparation/payment wired | Revision, park, resume, preparation submit/status, cancel and completion admitted/applied | Native save/preparation/ready/checkout; park/resume/cancel UI absent |
| C5 payments/discounts/refunds | Cash/AR/partial only; live completion now enforces credit permission and POS/AR license before write/cache; shared coordinator checks current credit permission before replay | SaleCommittedV1/V2 and refund request/resolve/commit apply. SaleCommittedV3 is admitted but returns NeedsReview / DOMAIN_PROCESSOR_UNAVAILABLE | Native Cash/AR/partial; controller refund methods, no refund UI found. Actual receipt DTOs exposed for durable outbox reconciliation |
| C6 cash/expenses | Cash movement and shift routes exist | CashMovementPostedV1 applies; shift-open/close operations not admitted | Native shift intents persist locally; independent shift synchronization remains absent |
| C7 print | Cafe print routes mapped | No print processor/admission | Receipt/kitchen job retry and unknown-outcome handling; physical printer remains HUMAN_REQUIRED |
| C8 delivery | Delivery routes mapped | All four admitted delivery operations remain NeedsReview / DOMAIN_PROCESSOR_UNAVAILABLE | Delivery advancement UI; no create UI found |
| C9 bookings/coffee cart | Booking routes mapped | Five admitted booking operations and booking-aware Sale V3 remain NeedsReview | Shared booking persistence; no booking UI |
| C10 attendance/incidents | Attendance/incident routes mapped | Four admitted operation types remain NeedsReview | Shared persistence; no UI |
| C11 COGS/profitability | Existing authenticated reporting route/service retained | Reports depend on applied source families; unsupported families mean consolidated coverage remains partial | Shared reporting service; no reporting UI |

## Observed boundaries and recovery evidence

- At `77ed22b`, bounded nonauthor execution ran four financial recovery/bootstrap cases (cash-sale atomic rollback/retry two theory cases; two-device bootstrap/feed/epoch recovery; unsupported delivery): 4 passed, zero skipped, exit 0. This is in-process Central service proof, not second-client SQLite projection or native E2E. Artifact: restricted `codex-resume/independent-review/cross-system-review.trx`.
- At `b19f9b97`, primary and independent real Host HTTP route/financial tests passed 8/8. Tests cover missing POS/AR entitlement, numeric AR method, revoked credit authority, guarded domain routes and excluded retail procurement/quotation routes. Artifact: `live-authority-r2.trx` and `independent-review/b19f-primary-review.trx`.
- The previous acceptance matrix claim that Central applies Sale V3 was incorrect. Admission is separate from processor support; the dispatcher currently only applies Sale V1/V2.
- The previous executable Host omitted MapLocalCafeDomainEndpoints entirely. Source presence did not establish route reachability. `b19f9b97` maps the existing guarded group and removes retail-only procurement/quotation routes.
- Fresh-device base bootstrap exports roles/users/units/products/permissions/sessions/customers. Menu and recipes now use separate authenticated publication reads and existing deterministic ApplyPublication validation. Other Cafe domain bootstrap and cursor-consistent snapshot recovery are not established by these reads.
- No percentage, complete-parity, physical acceptance, Release-license PASS or release-ready claim is made.
