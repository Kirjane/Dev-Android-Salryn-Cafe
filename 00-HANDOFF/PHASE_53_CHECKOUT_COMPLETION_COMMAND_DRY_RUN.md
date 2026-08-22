# Phase 53 - Checkout Completion Command Dry Run

## Goal

Add a checkout completion command dry run before sale completion is unlocked.

This phase validates whether the presenter can build a PosCheckoutApplicationRequest from the current draft.

It does not call CompleteSaleAsync.

It does not write a sale.

It does not unlock the Complete Sale button.

## Branch

KIRCH-APP-SHELL-PERMISSION-ROUTING

## Result

Checkout now has a dry-run validation path for completion request construction.

Flow:

Checkout UI button
  -> CheckoutPresenter.DryRunCompletionRequest
  -> PosCheckoutDraftTransactionBuilder.BuildApplicationRequest
  -> CheckoutCompletionDryRunViewModel
  -> CheckoutViewModel.CompletionDryRun
  -> CheckoutShellForm displays Dry run: <status>

## Updated Files

src/Salryn.App.WinForms/Features/Checkout/CheckoutViewModels.cs

- Adds CheckoutCompletionDryRunViewModel.
- Adds CompletionDryRun to CheckoutViewModel.
- Provides CheckoutCompletionDryRunViewModel.NotRun().

src/Salryn.App.WinForms/Features/Checkout/CheckoutPresenter.cs

- Adds _completionDryRun state.
- Adds DryRunCompletionRequest().
- Calls BuildApplicationRequest() only.
- Surfaces validation failures into presenter state.
- Reports successful request construction without submitting the sale.
- Resets dry-run state whenever draft inputs change.
- Does not call CompleteSaleAsync.
- Keeps CanCompleteSale false.

src/Salryn.App.WinForms/Features/Checkout/CheckoutShellForm.cs

- Adds Dry run status label.
- Adds Validate Completion Dry Run button.
- Displays dry-run result as not executed, failed, or passed.
- Keeps Complete Sale button disabled.

tools/Salryn.Tools.InfrastructureSmoke/PosCheckoutUiPresenterViewModelSmokeChecks.cs

- Verifies dry run fails when cashier user is missing.
- Verifies dry run fails when cashier session is missing.
- Verifies session-bound paid draft can build request.
- Verifies successful dry run still leaves sale submit locked.
- Verifies ClearDraft resets dry-run state.

## Verified Checks

Build passed.

Smoke checks passed:

- pos-checkout-ui-presenter-viewmodel
- pos-checkout-draft-transaction-builder
- pos-checkout-application-boundary

Tests passed:

- dotnet test .\Salryn.sln --no-build

Only NU1900 remained from NuGet vulnerability metadata lookup.

## Safety Verification

Presenter dry run does not call:

- CompleteSaleAsync

Presenter dry run only calls:

- BuildApplicationRequest

Complete Sale remains locked.

Verified lock:

_completeSaleButton.Enabled = false;

## This Phase Does Not Implement

- sale submit execution
- Complete Sale button enablement
- final sale write from UI
- success dialog
- receipt printing
- post-sale reset after submit
- double-submit guard

## Why This Matters

Before this phase, checkout could evaluate readiness but could not prove that the application request would actually build.

After this phase, checkout can run the same builder validation that future sale completion will depend on, while still preventing database writes.

## Recommended Next Phase

Phase 54 - Checkout Sale Submit Guard Preparation

Goal:

- add submit guard state
- add in-progress lock flag
- prepare one-shot execution safety
- still keep Complete Sale locked unless explicitly unlocked in a later phase

## Locked Rule

Complete Sale remains locked after Phase 53.

## Comment Style

Implementation comments must use:

// Kirch: ...
