# Phase 66G8 - Checkout Local Host Database Lookup Smoke

Status: Real local database-backed checkout lookup smoke.

## Scope

This phase extends the existing checkout Local Host smoke tool with a database-backed mode.

The smoke creates an isolated temporary development database root, initializes the database, writes one controlled product through the existing product write repository, sets the passive app session identity provider, and verifies that checkout search flows through:

AppServices CheckoutProductSearch -> CheckoutPresenter -> CheckoutLocalHostProductSearchBoundary -> CompositionLocalHostProductLookupGatewayAdapter -> Local Host runtime dispatch -> ProductLookup handler -> product read repository.

## Added / changed

- Extended Salryn.Tools.CheckoutLocalHostSmoke with command modes:
  - fake
  - database
- The default command still runs the 66G7 in-process fake gateway smoke.
- The database command verifies:
  - isolated development database initialization
  - controlled product repository write
  - passive app session identity setup
  - AppServices checkout boundary wiring
  - real Local Host holder dispatch
  - ProductLookup repository lookup by barcode
  - CheckoutPresenter prepared product state
  - Add prepared product into checkout draft
  - Checkout line state
  - Checkout totals state
  - database-backed not-found path
- Added a 66G8 source guard.
- CoreSmoke now checks the 66G8 database smoke shape.

## Preserved boundaries

- No Shell wiring was changed.
- Shell still receives CheckoutProductSearch through AppServices.
- Shell does not directly instantiate CheckoutLocalHostProductSearchBoundary.
- Shell does not directly pass LocalHostProductLookupGateway.
- CheckoutPresenter still depends only on ICheckoutProductSearchBoundary.
- CheckoutPresenter does not know IAppLocalHostProductLookupGateway.
- Runtime lifecycle remains owned by AppServices and the lifecycle holder.
- No transport was added.
- No LAN behavior was added.
- No HTTP behavior was added.
- No socket behavior was added.
- No two-machine behavior was added.
- No direct SQLite access was added outside Infrastructure.
- No production database path was touched by the smoke.

## Current state after this phase

The local checkout lookup path now has both a fake-gateway smoke and a real isolated database-backed smoke.

The database-backed smoke proves that a product written into an initialized local database can flow through the Local Host ProductLookup runtime path into checkout search, prepare a product, add a checkout line, and update checkout totals.
