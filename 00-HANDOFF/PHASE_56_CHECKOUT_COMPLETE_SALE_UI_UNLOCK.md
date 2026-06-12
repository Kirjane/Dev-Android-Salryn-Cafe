# Phase 56 - Checkout Complete Sale UI Unlock

## Goal

Unlock the Complete Sale button only through the guarded checkout submit rule.

This phase enables live cashier-facing sale completion after the dry run and submit guard have both passed.

## Branch

KIRCH-APP-SHELL-PERMISSION-ROUTING

## Result

Complete Sale is now enabled only when the presenter state allows it.

Unlock rule:

CanCompleteSale = _saleSubmitGuard.CanExecuteSubmit && !_isSaleSubmitInProgress && !_hasSaleSubmitCompleted

Flow:

Cashier shift required
  -> checkout opens with session context
  -> product added
  -> cash payment added
  -> completion dry run passes
  -> submit guard prepared
  -> Complete Sale button becomes enabled
  -> button calls ExecutePreparedSaleSubmitAsync
  -> guarded path calls ICheckoutCompletionBoundary.CompleteSaleAsync
  -> duplicate submit is blocked
  -> button locks after completion

## Updated Files

src/Salryn.App.WinForms/Features/Checkout/CheckoutPresenter.cs

- Changes CanCompleteSale from permanently false to guarded unlock rule.
- Updates submit guard prepared text to indicate Complete Sale readiness.
- Keeps duplicate submit protection through _hasSaleSubmitCompleted.
- Keeps in-progress protection through _isSaleSubmitInProgress.
- Keeps sale execution routed through ExecutePreparedSaleSubmitAsync.

src/Salryn.App.WinForms/Features/Checkout/CheckoutShellForm.cs

- Complete Sale button now follows state.CanCompleteSale.
- Complete Sale click disables the button immediately before execution.
- Button shows Completing Sale while execution is running.
- Button shows Sale Completed after successful completion.
- Button still routes through ExecutePreparedSaleSubmitAsync.

tools/Salryn.Tools.InfrastructureSmoke/PosCheckoutUiPresenterViewModelSmokeChecks.cs

- Updates smoke expectation so prepared submit guard unlocks CanCompleteSale.
- Verifies guarded execution still completes through fake boundary.
- Verifies duplicate submit remains blocked.
- Verifies boundary call count remains exactly one.

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

Enable cashier-facing Complete Sale only after the staged checkout safety path is satisfied.

Guaranteed by Phase 56 checks:

- Checkout can still build.
- Checkout UI presenter state still passes smoke.
- Draft transaction builder still passes smoke.
- Application boundary still passes smoke.
- Application repository write path still passes smoke.
- Existing tests still pass.
- Complete Sale is not globally enabled.
- Complete Sale only follows state.CanCompleteSale.
- state.CanCompleteSale depends on submit guard readiness.
- Submit still requires successful dry run.
- Submit still requires prepared guard.
- Submit still blocks duplicate completion.
- Button disables immediately while submitting.
- Smoke proves the completion boundary is called exactly once.

Meaning:

The checkout UI can now complete a sale, but only after the staged dry-run and submit-guard path.

This is the first phase where the cashier-facing Complete Sale button can reach the guarded completion boundary.

## Safety Verification

Presenter unlock rule:

CanCompleteSale: _saleSubmitGuard.CanExecuteSubmit && !_isSaleSubmitInProgress && !_hasSaleSubmitCompleted

Shell button rule:

_completeSaleButton.Enabled = state.CanCompleteSale;

Execution route:

ExecutePreparedSaleSubmitAsync

Duplicate submit block:

CheckoutSubmit.AlreadyCompleted

## This Phase Does Not Implement

- receipt printing
- receipt preview dialog
- automatic new-sale reset after success
- cashier confirmation modal before final submit
- post-sale inventory summary UI
- end-of-shift sales summary UI

## Risk Note

Because the shell already wires CheckoutCompletionBoundary with _services.PosCheckoutApplication, this phase allows the live button to reach the real application completion service when the guard path is satisfied.

Use test data or a safe local database while manually validating the live UI.

## Recommended Next Phase

Phase 57 - Checkout Sale Completion UX Finalization

Goal:

- add confirmation before final submit
- show completed receipt summary
- decide whether to reset to a new sale automatically
- prepare receipt print/export workflow
- improve cashier-facing success and failure messages

## Comment Style

Implementation comments must use:

// Kirch: ...
