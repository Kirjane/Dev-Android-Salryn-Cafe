# Phase 66G5 - Checkout Local Host Product Search Boundary

Status: Checkout-facing Local Host product search boundary only.

## Scope

This phase adds a checkout-facing boundary implementation that adapts IAppLocalHostProductLookupGateway into ICheckoutProductSearchBoundary.

This phase does not wire checkout UI to the new boundary yet.

## Added / changed

- Added CheckoutLocalHostProductSearchBoundary.
- The new boundary implements ICheckoutProductSearchBoundary.
- The new boundary depends on IAppLocalHostProductLookupGateway.
- The new boundary calls TryLookupByBarcodeAsync through the gateway.
- The new boundary maps AppLocalHostProductLookupProduct into ProductLookupReadModel.
- The new boundary maps gateway failures into Core Result errors.
- Legacy broad guards were updated only to allow this approved checkout adapter file.
- CoreSmoke now runs the 66G5 guard.

## Preserved boundaries

- No Shell checkout wiring was changed.
- Shell still passes CheckoutProductSearch into checkout.
- CheckoutPresenter still depends on ICheckoutProductSearchBoundary.
- AppServices does not instantiate CheckoutLocalHostProductSearchBoundary yet.
- Runtime lifecycle remains owned by AppServices and the lifecycle holder.
- The new checkout boundary does not start runtime.
- The new checkout boundary does not dispatch directly.
- No transport was added.
- No LAN behavior was added.
- No HTTP behavior was added.
- No socket behavior was added.
- No two-machine behavior was added.
- No direct SQLite access was added outside Infrastructure.

## Current state after this phase

A checkout-compatible Local Host ProductLookup boundary now exists.

The next phase may wire checkout to use this boundary while keeping Shell routing controlled.