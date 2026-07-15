# Salryn POS Architecture Lock

Status:
BETA / RELEASE SCOPE LOCK.

Project:
Salryn POS

Product Type:
Native offline-first Windows POS for general merchandise and small retail stores.

Current beta stack:

- Language: C#.
- Runtime: .NET.
- UI: local Windows/WebView host shell used by current repo lanes.
- Database: SQLite.
- Database access: Microsoft.Data.Sqlite.
- Receipt output: internal receipt snapshot plus optional Windows print queue submission.
- Barcode scanner: keyboard-wedge mode.
- Reports: internal operational reports and report artifacts as explicitly gated.
- Architecture: layered local transaction system.

Hardware policy:
Hardware peripherals beyond the Windows print queue are outside the beta scope.

---

## 1. Architecture principle

Salryn must be coded as a local transaction system.

It is not:

- A simple database editor.
- A web dashboard pretending to be production cloud POS.
- A receipt-printing toy.
- A random UI with direct database mutation.
- A hardware bundle/support product.

It is:

- A native/local Windows POS.
- Offline-first.
- Transaction-safe.
- Inventory-ledger based.
- Client-customizable within approved software scope.
- Report-ready only inside the explicit internal/report-artifact boundaries.

---

## 2. Layer responsibilities

### UI / app layer

Allowed:

- Display screens.
- Capture keyboard input.
- Handle button clicks.
- Call local API/service methods.
- Show results/errors.
- Refresh UI grids.
- Open modals.

Not allowed:

- Direct SQL.
- Direct stock computation.
- Direct sale saving logic.
- Direct receipt-number generation.
- Direct audit handling.
- Direct backup restore logic.
- Direct hardware peripheral control beyond the approved Windows print queue path.

Rule:
UI does not own truth.

---

### Core layer

Owns:

- Business rules.
- Models.
- Value objects.
- Service interfaces.
- Validation.
- Transaction flow rules.
- Receipt snapshot and printer adapter contracts.

Examples:

- Product.
- Cart.
- Sale.
- SaleItem.
- Payment.
- StockMovement.
- Money.
- Quantity.

---

### Infrastructure layer

Owns:

- SQLite connection.
- SQL repositories.
- Database migrations.
- Windows print queue adapter.
- Report artifact rendering/export under controlled scope.
- Backup and restore implementation when approved.
- File system paths.

Not owned in beta/release scope:

- ESC/POS implementation.
- cash drawer control.
- cutter control.
- thermal receipt profile/layout support.
- hardware bundle/support abstraction.

---

## 3. Hardware boundary

Allowed current printer claim:

- Windows print queue internal receipt printing.

Required exclusions:

- no cash drawer.
- no cutter.
- no ESC/POS.
- no thermal width/profile support.
- no hardware bundle/support claim.
- no official/BIR output.

These exclusions are not roadmap blockers for beta. They are out of beta scope.

---

## 4. Core service examples

Auth service controls login and role permission checking.

Product/catalog service controls product creation, edit, search, and barcode lookup.

Checkout service controls sale completion, payment storage, stock reduction, and receipt snapshot creation.

Receipt print service submits persisted internal receipt text to the configured printer adapter after sale commit. It must not create sales, payments, stock movements, or official/BIR output.

Report services compute operational summaries or explicitly gated internal report artifacts.

Status: `SCOPE_LOCKED_OUT_OF_BETA_HARDWARE_EXTRAS`
