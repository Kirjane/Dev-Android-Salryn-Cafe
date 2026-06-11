# Phase 51 - Checkout Receipt Number Readiness

## Goal

Prepare checkout receipt number generation before sale completion is unlocked.

This phase adds a safe receipt number boundary, assigns a receipt number to the checkout draft, exposes it in checkout state, and displays it in the checkout UI.

## Branch

KIRCH-APP-SHELL-PERMISSION-ROUTING

## Result

Checkout now generates a receipt number during presenter initialization and draft reset.

Flow:

Checkout opens
  -> CheckoutPresenter.Initialize
  -> SystemCheckoutReceiptNumberBoundary.CreateReceiptNumber
  -> ReceiptNumber.FromExisting validates generated value
  -> PosCheckoutDraftTransactionBuilder.SetReceiptNumber
  -> CheckoutViewModel.ReceiptNumber
  -> CheckoutShellForm displays Receipt: <number>

## New File

src/Salryn.App.WinForms/Features/Checkout/CheckoutReceiptNumberBoundary.cs

Adds:

- ICheckoutReceiptNumberBoundary
- SystemCheckoutReceiptNumberBoundary

Generated receipt format:

POS-yyyyMMdd-HHmmssfff-SESSIONPART-RANDOM

Example:

POS-20260611-142211123-A1B2C3D4-9F8A7B

The boundary validates generated values through the existing ReceiptNumber value object.

## Updated Files

src/Salryn.App.WinForms/Features/Checkout/CheckoutPresenter.cs

- Adds receipt number boundary dependency.
- Generates a new receipt number on Initialize.
- Assigns generated receipt number into PosCheckoutDraftTransactionBuilder.
- Keeps manual SetReceiptNumber support for smoke/testing boundary use.
- ClearDraft still reinitializes and receives a new receipt number.

src/Salryn.App.WinForms/Features/Checkout/CheckoutShellForm.cs

- Adds receipt number label to the Actions panel.
- Displays Receipt: not assigned when empty.
- Displays Receipt: <receipt number> when assigned.
- Complete Sale remains disabled.

tools/Salryn.Tools.InfrastructureSmoke/PosCheckoutUiPresenterViewModelSmokeChecks.cs

- Verifies receipt boundary generates non-empty unique receipt values.
- Verifies presenter initial state has a receipt number.
- Verifies ClearDraft generates a fresh receipt number instead of keeping the old manual placeholder.

## Verified Checks

Build passed.

Smoke check passed:

- pos-checkout-ui-presenter-viewmodel

Tests passed:

- dotnet test .\Salryn.sln --no-build

Only NU1900 remained from NuGet vulnerability metadata lookup.

## Safety Notes

Complete Sale remains locked.

Verified lock:

_completeSaleButton.Enabled = false;

This phase does not implement:

- sale completion button enablement
- sale submit boundary
- sale write from WinForms
- receipt printing
- payment finalization UI
- post-sale reset behavior
- double-submit guard

## Why This Matters

Sale completion requires a receipt number before a completed sale can be built safely.

Before this phase, the draft builder could accept a receipt number, but the checkout UI did not generate or display one automatically.

After this phase, checkout state is ready to carry a receipt number before future completion wiring.

## Recommended Next Phase

Phase 52 - Checkout Completion Boundary Preparation

Goal:

- introduce a WinForms-side checkout completion boundary
- inject IPosCheckoutApplicationService through the boundary
- allow presenter to build completion readiness state
- keep Complete Sale locked

## Locked Rule

Complete Sale remains locked after Phase 51.

## Comment Style

Implementation comments must use:

// Kirch: ...
