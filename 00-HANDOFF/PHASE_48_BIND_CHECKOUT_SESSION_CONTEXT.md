# Phase 48 - Bind Checkout Session Context

## Goal

Bind the active cashier shift session into the POS checkout UI after the shift gate approves entry.

Phase 47 added the cashier shift gate.

Phase 48 makes checkout aware of the active cashier session context.

## Branch

KIRCH-APP-SHELL-PERMISSION-ROUTING

## Result

Checkout now receives an active CheckoutSessionContext from the shell.

Flow:

Login
  -> Open SALRYN App Shell
  -> Click POS Checkout
  -> Cashier Shift Gate checks/open shift
  -> Shift gate returns OpenSession
  -> Shell creates CheckoutSessionContext
  -> Shell passes context into CheckoutPresenter
  -> Checkout UI displays bound shift context

## New File

src/Salryn.App.WinForms/Features/Checkout/CheckoutSessionContext.cs

Adds the UI-level checkout context created from CurrentAppSession and CashierSessionReadModel.

## Updated Files

src/Salryn.App.WinForms/Features/Checkout/CheckoutViewModels.cs

Adds cashier session context fields to CheckoutViewModel:

- CashierSessionId
- CashierDisplayName
- CashierRoleName
- CashierSessionText
- CheckoutModeText

src/Salryn.App.WinForms/Features/Checkout/CheckoutPresenter.cs

Adds optional CheckoutSessionContext support.

When context is present, Initialize binds the cashier user ID into the draft transaction builder and displays the active shift reference.

src/Salryn.App.WinForms/Features/Checkout/CheckoutShellForm.cs

Displays the active shift context in the checkout header and status bar.

src/Salryn.App.WinForms/Features/Shell/SalrynShellForm.cs

Creates CheckoutSessionContext from the current app session and the open cashier session returned by the shift gate.

## Verified Behavior

Manual UI test passed:

- Admin login works
- POS Checkout still opens through Cashier Shift Gate
- Open cashier shift is detected
- Checkout opens after shift approval
- Checkout header displays shift short ID
- Checkout header displays shift opened time
- Checkout header displays opening cash
- Checkout mode label displays shift bound
- Status bar displays checkout bound to cashier shift
- Complete Sale remains locked

## Locked Scope

This phase does not implement:

- sale completion
- receipt saving
- stock deduction
- final payment posting
- receipt printing
- close shift UI
- counted cash reconciliation
- end-of-day report

Complete Sale remains intentionally disabled.

## Comment Style

Implementation comments use:

// Kirch: ...
