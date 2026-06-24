# KIRCH-SANDBOX UI Rebuild Plan

Date: 2026-06-24
Scope: UI/UX planning only.

## UI Diagnosis

The current UI is functional enough for phase verification, but not a finished POS. It has:

- a shell with module routing
- login and shift gate
- checkout screen with product lookup and sale completion path
- products, inventory, sales history, dashboard, and settings screens
- many forms built as modal workflows
- checkout debug/guard wording still visible

The target UI should feel like a fast, practical retail tool, not a phase demo.

## Design Principles

- cashier speed first
- keyboard-first checkout
- large readable totals
- no phase/debug labels in production screens
- no hidden critical actions
- no business logic in forms
- presenter/view-model boundaries stay intact
- clear locked states with reasons
- quiet professional Windows style

## First-Run Flow

Target first screen when no deployment profile exists:

1. Welcome
2. Choose:
   - Create New Store Host
   - Join Existing Store Receiver
3. Create New Store Host:
   - store name
   - business name
   - address
   - currency
   - receipt width
   - admin account
   - backup folder
   - printer mode
   - generate connection code
4. Join Existing Store Receiver:
   - discover stores or enter host address/code
   - pair terminal
   - login through host

Do not show the normal shell until setup is complete.

## Cashier Checkout Target

Required layout:

- top status bar: store, cashier, shift, terminal, host/db/printer status
- large scan/search box
- product result area
- cart grid
- large total panel
- cash/payment panel
- action bar with shortcuts

Production actions:

- F2 Fast Add
- F3 Quantity
- F4 Price Check
- F5 New Sale
- F6 Reprint Last Receipt
- F9 Pay
- Esc Cancel/Back

Remove from production cashier UI:

- Validate Completion Dry Run
- Prepare Submit Guard
- Complete Sale Locked wording
- "repository-backed" and phase language

Keep the guard logic internally. Do not expose it as separate cashier steps.

## Checkout Payment Flow

Target flow:

1. Scan/add items.
2. Press F9 or click Pay.
3. Payment panel/modal focuses cash input.
4. Enter cash.
5. Change updates live.
6. Enter completes sale.
7. Sale saves first.
8. Receipt print job is created.
9. Print attempt runs.
10. Result shows:
    - sale complete
    - receipt number
    - change
    - print status
11. New sale starts with F5 or after cashier confirmation.

## Product Management

Required screens:

- product list
- product detail/edit
- fast add
- CSV import preview
- CSV export
- deactivate product
- stock movement view for product

Fast Add must support:

- barcode prefilled from unknown scan
- name
- category
- unit
- cost
- price
- initial stock
- reorder level
- save and add to cart
- save and add another

## Inventory

Required screens:

- stock-in
- adjustment add/remove
- low-stock list
- movement history

Rules:

- adjustment requires reason
- stock-in can include cost
- movement history is read-only

## Sales History

Required screens:

- search by receipt number
- filter by date
- filter by cashier
- sale detail
- receipt snapshot preview
- reprint
- void with reason and authorization

Rules:

- no edit completed sale
- void is separate workflow
- reprint reads saved snapshot

## Reports

Required screens:

- daily sales summary
- monthly summary
- low stock
- top products
- sales by cashier
- CSV export
- PDF export
- X-reading
- Z-reading

Reports should be readable by store owners and printable without app-specific explanation.

## Settings

Required sections:

- store profile
- receipt template
- printer
- backup
- users and roles
- RMO machine profile, when compliance mode is enabled
- official sequence setup, when compliance mode is enabled
- host/receiver terminal settings

## Host/Receiver UI

Host dashboard must show:

- host mode
- machine name
- host address
- connected terminals
- backup health
- DB status
- printer status
- connection code generation
- revoke terminal action

Receiver dashboard must show:

- connected host
- terminal name
- user session
- sync/connection status
- offline/unavailable state

## UI Acceptance Checklist

The UI is production-ready only when:

- cashier can complete a normal sale by keyboard.
- checkout no longer shows phase/debug controls.
- unknown barcode can lead to fast add.
- sale completion failure messages are friendly.
- print failure does not look like sale failure.
- all locked modules explain why they are locked.
- first-run setup is not skipped by accident.
- Host/Receiver mode is explicit.
- no screen directly constructs repositories.
- no screen writes business truth by itself.
