# Phase 52 - Checkout Completion Boundary Preparation

## Goal

Prepare a WinForms-side checkout completion boundary and completion readiness state before sale completion is unlocked.

This phase wires readiness evaluation only. It does not unlock the Complete Sale button and does not submit sales from the UI.

## Branch

KIRCH-APP-SHELL-PERMISSION-ROUTING

## Result

Checkout can now evaluate whether the current draft can build a completion request.

Flow:

Checkout draft state
  -> PosCheckoutDraftTransaction
  -> CheckoutCompletionBoundary.Evaluate
  -> CheckoutCompletionReadiness
  -> CheckoutCompletionReadinessViewModel
  -> CheckoutViewModel.CompletionReadiness
  -> CheckoutShellForm displays Completion: <status>

## New File

src/Salryn.App.WinForms/Features/Checkout/CheckoutCompletionBoundary.cs

Adds:

- CheckoutCompletionReadiness
- ICheckoutCompletionBoundary
- CheckoutCompletionBoundary

The boundary exposes:

- Evaluate(PosCheckoutDraftTransaction transaction)
- CompleteSaleAsync(PosCheckoutApplicationRequest request, CancellationToken cancellationToken = default)

The submit path exists behind the boundary, but the UI remains locked.

## Updated Files

src/Salryn.App.WinForms/Features/Checkout/CheckoutViewModels.cs

- Adds CheckoutCompletionReadinessViewModel.
- Adds CompletionReadiness to CheckoutViewModel.
- Empty checkout state now includes a safe default readiness model.

src/Salryn.App.WinForms/Features/Checkout/CheckoutPresenter.cs

- Adds ICheckoutCompletionBoundary dependency.
- Evaluates completion readiness during state refresh.
- Maps CheckoutCompletionReadiness into CheckoutCompletionReadinessViewModel.
- Keeps CanCompleteSale false.

src/Salryn.App.WinForms/Features/Checkout/CheckoutShellForm.cs

- Adds completion readiness label to the Actions panel.
- Displays Completion: Completion not ready when missing requirements.
- Displays Completion: Completion request ready · submit locked when draft is ready.
- Complete Sale button remains disabled.

src/Salryn.App.WinForms/Features/Shell/SalrynShellForm.cs

- Injects CheckoutCompletionBoundary with _services.PosCheckoutApplication into CheckoutPresenter.

src/Salryn.Core/Services/PosCheckoutDraftTransactionBuilder.cs

- Adds explicit BuildApplicationRequest guard for missing cashier session ID.
- Prevents request construction when CashierSessionId is null or Guid.Empty.

tools/Salryn.Tools.InfrastructureSmoke/PosCheckoutUiPresenterViewModelSmokeChecks.cs

- Verifies initial completion readiness is not ready.
- Verifies paid draft without session is still not build-ready.
- Verifies session-bound paid draft becomes build-ready but still submit-locked.

## Verified Checks

Build passed.

Smoke checks passed:

- pos-checkout-draft-transaction-builder
- pos-checkout-ui-presenter-viewmodel
- pos-checkout-application-boundary
- pos-checkout-application-repository

Tests passed:

- dotnet test .\Salryn.sln --no-build

Only NU1900 remained from NuGet vulnerability metadata lookup.

## Safety Notes

Complete Sale remains locked.

Verified lock:

_completeSaleButton.Enabled = false;

Readiness may say the completion request can be built, but CanSubmitSale remains false.

This phase does not implement:

- Complete Sale button enablement
- click handler for final sale submit
- UI sale write execution
- success dialog
- receipt printing
- post-sale draft reset
- double-submit guard

## Why This Matters

Before this phase, the UI had draft totals, payments, receipt number, and cashier session context, but no WinForms-side completion boundary.

After this phase, checkout can safely report when the draft has the data required to build a completion request while still preventing final submit.

The lower application and repository smoke checks continue to prove the backend completion path remains valid.

## Recommended Next Phase

Phase 53 - Checkout Completion Command Dry Run

Goal:

- add presenter method that builds the application request
- dry-run only through builder validation
- surface validation errors in UI state
- keep Complete Sale locked

## Locked Rule

Complete Sale remains locked after Phase 52.

## Comment Style

Implementation comments must use:

// Kirch: ...
