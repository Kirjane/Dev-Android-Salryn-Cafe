# Phase 47 - Cashier Shift Gate

## Goal

Add a cashier shift gate before the POS checkout workspace opens.

Phase 46 added authenticated sessions.

Phase 47 uses the authenticated CurrentAppSession to require an open cashier shift before entering checkout.

## Branch

KIRCH-APP-SHELL-PERMISSION-ROUTING

## Result

The POS Checkout module now routes through a shift gate.

Flow:

Login
  -> Open SALRYN App Shell
  -> Click POS Checkout
  -> Check Sales.Create permission
  -> Check Shift.OpenClose permission
  -> Check open cashier session for current user
  -> If no shift exists, show Cashier Shift Gate
  -> User opens shift with opening cash
  -> Enter Checkout becomes enabled
  -> Checkout shell opens

## New App Files

src/Salryn.App.WinForms/Features/Shifts/CashierShiftGatePresenter.cs

Adds the presenter that checks permissions, checks open cashier sessions, and opens cashier shifts.

src/Salryn.App.WinForms/Features/Shifts/CashierShiftGateForm.cs

Adds the modal WinForms gate used before checkout.

## Updated Shell File

src/Salryn.App.WinForms/Features/Shell/SalrynShellForm.cs

POS Checkout no longer opens directly from the shell.

It now opens CashierShiftGateForm first.

## Permission Rules

The shift gate requires:

- Sales.Create
- Shift.OpenClose
- active authenticated user session

## Checkout Rules

Checkout can only open after a cashier shift exists.

Complete Sale remains locked in this phase.

This phase does not implement sale completion, receipt saving, stock deduction, payment finalization, or shift close UI.

## Verified Behavior

Manual UI test passed:

- Admin login works
- POS Checkout opens the Cashier Shift Gate
- No open shift state is detected
- Open Shift with PHP 0.00 succeeds
- Enter Checkout becomes enabled
- Checkout shell opens after shift gate approval
- Sale completion remains locked

## Current Limitations

This phase does not yet implement:

- cashier shift closing UI
- counted cash entry
- end-of-day reconciliation screen
- sale completion
- receipt printing
- checkout-to-session ID binding in the UI
- logout/session switching

## Comment Style

Implementation comments use:

// Kirch: ...
