# Phase 54 - Checkout Sale Submit Guard Preparation

## Goal

Prepare a checkout sale submit guard before the Complete Sale execution path is unlocked.

This phase adds one-shot submit preparation state, but it does not execute sale submission.

It does not call CompleteSaleAsync.

It does not write a sale.

It does not unlock the Complete Sale button.

## Branch

KIRCH-APP-SHELL-PERMISSION-ROUTING

## Result

Checkout now has a sale submit guard preparation path.

Flow:

Successful completion dry run
  -> CheckoutPresenter.PrepareSaleSubmitGuard
  -> PosCheckoutDraftTransactionBuilder.BuildApplicationRequest
  -> one-shot attempt token
  -> CheckoutSaleSubmitGuardViewModel
  -> CheckoutViewModel.SaleSubmitGuard
  -> CheckoutShellForm displays Submit guard: <status>

## Updated Files

src/Salryn.App.WinForms/Features/Checkout/CheckoutViewModels.cs

- Adds CheckoutSaleSubmitGuardViewModel.
- Adds SaleSubmitGuard to CheckoutViewModel.
- Provides CheckoutSaleSubmitGuardViewModel.NotPrepared().
- Keeps CanExecuteSubmit false.

src/Salryn.App.WinForms/Features/Checkout/CheckoutPresenter.cs

- Adds _saleSubmitGuard state.
- Adds _isSaleSubmitInProgress flag.
- Adds PrepareSaleSubmitGuard().
- Adds FailSaleSubmitGuard().
- Adds ResetSaleSubmitGuard().
- Requires successful dry run before guard preparation.
- Rebuilds the application request through BuildApplicationRequest for safety.
- Generates a one-shot attempt token.
- Keeps CanExecuteSubmit false.
- Does not call CompleteSaleAsync.
- Keeps CanCompleteSale false.

src/Salryn.App.WinForms/Features/Checkout/CheckoutShellForm.cs

- Adds Submit guard status label.
- Adds Prepare Submit Guard button.
- Enables guard preparation only after successful dry run.
- Keeps Complete Sale button disabled.

tools/Salryn.Tools.InfrastructureSmoke/PosCheckoutUiPresenterViewModelSmokeChecks.cs

- Verifies submit guard is not prepared on initial state.
- Verifies submit guard requires successful dry run.
- Verifies prepared guard receives an attempt token.
- Verifies prepared guard still cannot execute submit.
- Verifies ClearDraft resets submit guard state.

## Verified Checks

Build passed.

Smoke checks passed:

- pos-checkout-ui-presenter-viewmodel
- pos-checkout-draft-transaction-builder
- pos-checkout-application-boundary
- pos-checkout-application-repository

Tests passed:

- dotnet test .\Salryn.sln --no-build

Only NU1900 remained from NuGet vulnerability metadata lookup.

## Consistency Assurance

Current focus:

Prepare the final Complete Sale execution path without unlocking sale execution yet.

Guaranteed by Phase 54 checks:

- Checkout can still build.
- Checkout UI presenter state still passes smoke.
- Draft transaction builder still passes smoke.
- Application boundary still passes smoke.
- Application repository write path still passes smoke.
- Existing tests still pass.
- CheckoutPresenter does not call CompleteSaleAsync.
- Complete Sale button remains disabled.
- Submit guard can be prepared only after successful dry run.
- Submit guard creates an attempt token but CanExecuteSubmit remains false.
- No sale is written from the UI in this phase.
- No stock movement is written from the UI in this phase.
- No product stock is updated from the UI in this phase.

Meaning:

The backend sale path is proven, but the WinForms UI is still locked from executing it.

Phase 54 only prepares the safety guard before the dangerous final submit phase.

## Safety Verification

Presenter does not call:

- CompleteSaleAsync

Presenter guard preparation only calls:

- BuildApplicationRequest

Complete Sale remains locked.

Verified lock:

_completeSaleButton.Enabled = false;

Submit execution remains locked.

Verified submit state:

CanExecuteSubmit: false

## This Phase Does Not Implement

- actual sale submit execution
- Complete Sale button enablement
- final sale write from UI
- success dialog
- receipt printing
- post-sale reset after submit
- live double-submit execution guard

## Why This Matters

Before this phase, checkout could dry-run completion request construction but did not have a submit preparation guard.

After this phase, checkout can prepare a guarded, one-shot submit attempt token while still preventing final database writes.

This keeps the project safe before the actual Complete Sale execution phase.

## Recommended Next Phase

Phase 55 - Checkout Sale Submit Execution Wiring

Goal:

- wire guarded Complete Sale execution path
- require prepared submit guard
- call completion boundary only through the guarded path
- prevent double submit
- show success/failure state
- decide explicitly whether to unlock Complete Sale in that phase

## Locked Rule

Complete Sale remains locked after Phase 54.

## Comment Style

Implementation comments must use:

// Kirch: ...
