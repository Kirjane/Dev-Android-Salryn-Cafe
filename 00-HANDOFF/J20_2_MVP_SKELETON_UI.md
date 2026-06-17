# J20-2 - MVP Skeleton UI

Status: Minimal presentable MVP skeleton UI.

## Scope

This phase adds minimal, presentable WinForms skeleton screens for the June 20 local MVP.

The screens are intentionally simple. They are meant to expose the existing backend services to a user-facing flow while leaving final visual polish to the frontend implementation pass.

## Added / changed

- Added shared J20MvpUi helper for simple cards, headers, grids, and buttons.
- Added DashboardForm.
- Added InventoryForm.
- Added SalesHistoryForm.
- Added ReceiptPreviewForm.
- Added StoreSettingsForm.
- Routed Dashboard, Inventory, Sales History, and Settings from the Shell.
- Presented the Reports module as Dashboard for the MVP.
- Added Kirch guide comments inside the skeleton UI files.
- Added a J20-2 source guard.
- CoreSmoke now checks the MVP skeleton UI shape.

## Preserved boundaries

- No LAN behavior was added.
- No two-machine behavior was added.
- No transport behavior was added.
- No direct SQLite access was added to WinForms.
- No official BIR accreditation claim was added.
- Receipt preview uses RMO-aware wording only.
- Product and Checkout screens keep their existing workflows.
- UI screens consume backend through AppServices.

## Frontend implementation guidance

The frontend polish pass may improve layout, spacing, typography, colors, and Figma alignment. It should not bypass AppServices, create repositories directly, or add transport behavior.
