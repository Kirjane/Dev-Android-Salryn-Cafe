# Phase 50 - Explicit Checkout Session ID Enforcement

## Goal

Carry the active cashier session ID explicitly through the checkout draft and application request path.

This phase strengthens session correctness before sale completion is unlocked.

## Branch

KIRCH-APP-SHELL-PERMISSION-ROUTING

## Result

Checkout sale completion request contracts now explicitly require CashierSessionId.

Flow strengthened:

CheckoutSessionContext
  -> CheckoutPresenter
  -> PosCheckoutDraftTransactionBuilder
  -> PosCheckoutApplicationRequest
  -> PosCheckoutApplicationService
  -> Sale.CashierSessionId
  -> PosCheckoutService mismatch enforcement
  -> Sale write repository

## Updated Files

src/Salryn.App.WinForms/Features/Checkout/CheckoutPresenter.cs

- Binds CheckoutSessionContext.CashierSessionId into the draft builder.
- Keeps checkout initialized only with the active shift context when opened through the shell.

src/Salryn.Core/Services/PosCheckoutDraftTransactionBuilder.cs

- Adds nullable CashierSessionId to PosCheckoutDraftTransaction.
- Adds _cashierSessionId state.
- Adds SetCashierSessionId(Guid cashierSessionId).
- Requires cashier session ID before BuildApplicationRequest succeeds.
- Passes cashier session ID into PosCheckoutApplicationRequest.

src/Salryn.Core/Services/PosCheckoutApplicationService.cs

- Adds CashierSessionId to PosCheckoutApplicationRequest.
- Validates CashierSessionId is not Guid.Empty.
- Sets Sale.CashierSessionId from the request instead of leaving it null.

tools/Salryn.Tools.InfrastructureSmoke/PosCheckoutDraftTransactionBuilderSmokeChecks.cs

- Updated smoke test to set and verify cashier session ID.

tools/Salryn.Tools.InfrastructureSmoke/PosCheckoutApplicationBoundarySmokeChecks.cs

- Updated boundary smoke request construction for explicit cashier session ID.
- Updated sale mapping verification to expect a non-null cashier session ID.

tools/Salryn.Tools.InfrastructureSmoke/PosCheckoutApplicationRepositorySmokeChecks.cs

- Updated repository smoke request construction to pass explicit cashier session IDs.
- Existing repository verification still confirms cashier_session_id is stored.

## Verified Smoke Checks

Targeted smoke checks passed:

- pos-checkout-draft-transaction-builder
- pos-checkout-application-boundary
- pos-checkout-application-repository
- pos-checkout-ui-presenter-viewmodel

Build passed.

Only NU1900 remained from NuGet vulnerability metadata lookup.

## Safety Notes

Complete Sale remains locked.

This phase does not implement:

- receipt number generation
- completion boundary wiring from WinForms
- Complete Sale button enablement
- sale submit UX
- receipt printing
- post-sale draft reset
- double-submit guard

## Why This Matters

Before this phase, PosCheckoutApplicationRequest did not carry CashierSessionId.

The lower PosCheckoutService could still inject the currently open session, but the UI-to-application contract was not explicit.

After this phase, stale checkout windows cannot silently build completion requests without a cashier session ID.

The lower PosCheckoutService still keeps mismatch enforcement as a second safety layer.

## Recommended Next Phase

Phase 51 - Checkout Receipt Number Readiness

Goal:

- introduce a safe receipt number generation boundary
- set receipt number before sale completion
- display receipt number in checkout state
- keep Complete Sale locked

## Locked Rule

Complete Sale remains locked after Phase 50.

## Comment Style

Implementation comments must use:

// Kirch: ...
