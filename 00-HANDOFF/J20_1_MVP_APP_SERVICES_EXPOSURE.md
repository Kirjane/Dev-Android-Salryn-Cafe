# J20-1 - MVP App Services Exposure

Status: June 20 MVP backend exposure only.

## Scope

This phase exposes already-existing MVP repositories through the WinForms composition root so minimal June 20 screens can be built without bypassing AppServices.

The repository factory already creates sale read, inventory read/write, and store settings read repositories. This phase makes those services available to the app layer in a controlled way.

## Added / changed

- Exposed ISaleReadRepository from SalrynAppServices.
- Exposed IInventoryReadRepository from SalrynAppServices.
- Exposed IInventoryWriteRepository from SalrynAppServices.
- Exposed IStoreSettingsReadRepository from SalrynAppServices.
- Wired those services from SqliteRepositoryFactory through the existing composition root.
- Added a J20 source guard.
- CoreSmoke now checks the MVP AppServices exposure shape.

## Preserved boundaries

- No Shell routing was changed.
- No UI screens were added in this phase.
- No CheckoutPresenter behavior was changed.
- No ProductLookup behavior was changed.
- No Local Host / LAN / transport behavior was added.
- No direct SQLite access was added to WinForms.
- MVP screens must still consume backend through AppServices and presenters/forms.

## Why this exists

The June 20 MVP needs Sales History, Inventory, Dashboard, and Store Settings skeleton screens. Those screens need access to existing sale, inventory, and settings repositories. This phase exposes those services first so UI work can stay clean instead of creating repositories directly from forms.
