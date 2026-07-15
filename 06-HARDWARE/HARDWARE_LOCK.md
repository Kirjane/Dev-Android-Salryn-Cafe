# Salryn POS Hardware Scope Lock

Status:
BETA / RELEASE SCOPE LOCK.

Purpose:
Prevent hardware-drift from entering active beta/release planning.

---

## Current allowed printer scope

Allowed current claim:

- Windows print queue internal receipt printing.
- Valid Windows printer/manual test proof may prove only that Windows print queue path.
- Internal receipt only.
- No official/BIR output.

The app may preserve internal receipt snapshots and submit saved receipt text to a configured Windows printer queue.

---

## Hardware peripherals outside beta scope

Hardware peripherals beyond the Windows print queue are outside the beta scope.

Banned from beta/release scope:

- cash drawer
- drawer kick
- receipt cutter
- cut receipt command
- ESC/POS
- thermal printer profile
- hardware receipt width/profile support
- thermal receipt formatting
- hardware bundle/support claims

These are not active roadmap items, not release blockers, and not tracked for this release.

---

## Barcode scanner boundary

Allowed:

- Keyboard-wedge barcode scanner mode.
- Scanner behaves like keyboard input into the focused scan/search field.
- Enter suffix may submit/confirm the scan.

Out of scope for this release:

- direct USB HID scanner API
- Bluetooth special handling
- scanner driver dependency
- complex device enumeration

---

## Printer failure rules

If printing fails:

- Sale remains saved.
- Receipt snapshot remains stored.
- User sees a warning.
- User can attempt reprint from the persisted snapshot.

Never:

- Roll back a completed sale because printer submission failed.
- Lose receipt snapshot.
- Create duplicate sale on reprint.
- Create duplicate payment on reprint.
- Mutate stock because of print or reprint.

---

## Client setup boundary

For beta/release planning, do not require a hardware bundle.

Acceptable beta setup:

- Windows PC or laptop.
- Keyboard-wedge barcode scanner if scanning is tested.
- Any valid Windows-installed printer only if Kirch performs and records manual print proof.

No beta claim may require or advertise cash drawer, cutter, ESC/POS, thermal profile, or bundled POS hardware support.

---

## Test cases in active scope

Printer:

- Windows printer disabled path.
- Windows printer name missing path.
- Windows print queue submission path on Windows host.
- Print failure leaves sale unchanged.
- Reprint uses persisted receipt snapshot and leaves sale/payment/stock unchanged.

Scanner:

- Manual barcode typing.
- Keyboard-wedge scan of known barcode.
- Keyboard-wedge scan of unknown barcode.
- Enter suffix behavior.

Status: `SCOPE_LOCKED_OUT_OF_BETA_HARDWARE_EXTRAS`
