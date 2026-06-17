# Phase 66G4 - Local Host ProductLookup Checkout-Ready Payload

Status: Local Host ProductLookup checkout-ready payload only.

## Scope

This phase enriches the existing Local Host ProductLookup response so it can carry checkout-ready product data.

This phase does not wire checkout UI to Local Host yet.

## Added / changed

- LocalHostProductLookupResponse now carries:
  - ProductId
  - Sku
  - Barcode
  - DisplayName
  - UnitName
  - SellingPriceCents
  - CostPriceCents
  - StockOnHandX1000
  - TrackStock
  - AllowDiscount
  - IsActive
- LocalHostProductLookupHandler now maps the full ProductLookupReadModel into the Local Host response.
- AppLocalHostProductLookupProduct now carries checkout-ready fields.
- CompositionLocalHostProductLookupGatewayAdapter now maps the enriched Local Host response into the app gateway result.
- Legacy compile handlers were updated to satisfy the enriched response constructor.
- CoreSmoke now runs the 66G4 guard.

## Preserved boundaries

- No Shell checkout wiring was changed.
- CheckoutPresenter still depends on ICheckoutProductSearchBoundary.
- Shell still passes CheckoutProductSearch into checkout.
- LocalHostProductLookupGateway is still not passed into checkout UI.
- Runtime lifecycle remains owned by AppServices and the lifecycle holder.
- Adapters and providers still do not start or stop runtime.
- No transport was added.
- No LAN behavior was added.
- No HTTP behavior was added.
- No socket behavior was added.
- No two-machine behavior was added.
- No direct SQLite access was added outside Infrastructure.

## Current state after this phase

Local Host ProductLookup can now return enough product fields for a future checkout-facing adapter.

The next phase may add an ICheckoutProductSearchBoundary implementation backed by IAppLocalHostProductLookupGateway.