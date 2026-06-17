# Phase 66G6 - Checkout Local Host Product Search Wiring

Status: Checkout product search now uses the Local Host-backed boundary through AppServices.

## Scope

This phase wires the existing checkout product search service to use CheckoutLocalHostProductSearchBoundary.

The wiring is done through AppServices so Shell can keep passing CheckoutProductSearch into CheckoutPresenter.

## Added / changed

- AppServices now assigns CheckoutProductSearch to CheckoutLocalHostProductSearchBoundary.
- CheckoutLocalHostProductSearchBoundary receives LocalHostProductLookupGateway.
- The old direct repository-backed checkout product search composition is no longer the active checkout search wiring.
- The Phase 66G5 guard was advanced only to allow this approved AppServices wiring.
- CoreSmoke now runs the 66G6 guard.

## Preserved boundaries

- Shell still passes CheckoutProductSearch.
- Shell does not directly instantiate CheckoutLocalHostProductSearchBoundary.
- Shell does not directly pass LocalHostProductLookupGateway.
- CheckoutPresenter still depends only on ICheckoutProductSearchBoundary.
- CheckoutPresenter does not know IAppLocalHostProductLookupGateway.
- Runtime lifecycle remains owned by AppServices and the lifecycle holder.
- No direct dispatch was added to Shell or CheckoutPresenter.
- No transport was added.
- No LAN behavior was added.
- No HTTP behavior was added.
- No socket behavior was added.
- No two-machine behavior was added.
- No direct SQLite access was added outside Infrastructure.

## Current state after this phase

Checkout product search is now routed through the Local Host-backed ProductLookup path while keeping the checkout presenter and shell routing shape stable.

The next phase may perform an end-to-end local checkout lookup smoke path or add UI-facing verification around lookup behavior.