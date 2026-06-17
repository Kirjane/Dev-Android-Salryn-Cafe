# Phase 66G7 - Checkout Local Host Lookup Smoke Verification

Status: End-to-end local checkout lookup smoke verification only.

## Scope

This phase adds a local smoke tool that verifies the checkout lookup chain after 66G6 wiring.

The smoke uses a controlled in-process fake IAppLocalHostProductLookupGateway so it can exercise the checkout boundary and presenter without requiring a seeded database, LAN, HTTP, sockets, or a second device.

## Added / changed

- Added Salryn.Tools.CheckoutLocalHostSmoke.
- Registered the smoke tool in Salryn.sln.
- The smoke tool verifies:
  - Local Host-backed checkout boundary construction.
  - CheckoutPresenter search through ICheckoutProductSearchBoundary.
  - Enriched Local Host product payload mapping.
  - Prepared product view model state.
  - Add prepared product into checkout draft.
  - Checkout line state.
  - Checkout totals state.
  - Not-found lookup path.
  - Local Host unavailable failure path.
- Added a 66G7 source guard.
- CoreSmoke now checks the 66G7 smoke tool shape.

## Preserved boundaries

- Shell still passes CheckoutProductSearch.
- Shell does not directly instantiate CheckoutLocalHostProductSearchBoundary.
- Shell does not directly pass LocalHostProductLookupGateway.
- CheckoutPresenter still depends only on ICheckoutProductSearchBoundary.
- CheckoutPresenter does not know IAppLocalHostProductLookupGateway.
- Runtime lifecycle remains owned by AppServices and the lifecycle holder.
- No direct dispatch was added to Shell or CheckoutPresenter.
- No database seed requirement was added.
- No transport was added.
- No LAN behavior was added.
- No HTTP behavior was added.
- No socket behavior was added.
- No two-machine behavior was added.
- No direct SQLite access was added outside Infrastructure.

## Current state after this phase

The local checkout lookup path has a runnable smoke verification proving that a Local Host product payload can flow through the checkout boundary, prepare a product, add the line into the draft, and update checkout totals.

The next phase may perform a real local database-backed smoke path after the development database has a controlled seed or first-run fixture.