# Phase 49 - Checkout Sale Completion Readiness Audit

## Scope

This phase is an audit only.

No sale completion button is unlocked in this phase.

No receipt saving, stock deduction, payment finalization, or production sale write is wired from the UI in this phase.

## Branch

KIRCH-APP-SHELL-PERMISSION-ROUTING

## Current Sealed Flow

Login
  -> SALRYN App Shell
  -> POS Checkout
  -> Cashier Shift Gate
  -> Active shift required
  -> Checkout receives CheckoutSessionContext
  -> Checkout displays shift context
  -> Complete Sale remains locked

## Backend Assets Already Present

### Core sale models

The domain already has these write-side sale entities:

- Sale
- SaleItem
- Payment
- StockMovement
- CashierSession

Important sale fields already exist:

- ReceiptNumber
- SaleDate
- CashierUserId
- CashierSessionId
- Subtotal
- Discount
- Tax
- Total
- AmountPaid
- Change
- Status
- ReceiptSnapshot
- Items
- Payments

### Checkout draft builder

The checkout draft builder already supports:

- setting cashier user ID
- setting receipt number
- adding checkout lines
- adding payments
- calculating subtotal, total, amount paid, change, and balance due
- building PosCheckoutApplicationRequest

BuildApplicationRequest currently requires:

- valid cashier user ID
- receipt number
- at least one line
- at least one payment
- payment amount not less than total

### Checkout application service

The checkout application service already maps request data into:

- Sale
- SaleItem list
- Payment list
- StockMovement list for stock-tracked products

It validates:

- request presence
- cashier user ID
- receipt number
- line presence
- payment presence
- positive quantities
- non-negative money values
- line discount not exceeding gross
- payment amount greater than zero
- payment not less than total

### Checkout core service

The checkout core service already enforces open cashier session rules before writing a completed sale.

It checks:

- open cashier session exists for the sale cashier
- open session user matches the sale cashier
- provided sale CashierSessionId matches the open session if one is supplied

If the Sale has no CashierSessionId, the service clones/enforces the currently open session ID before writing.

### SQLite sale writer

The sale write repository already writes in a transaction:

- sales
- sale_items
- payments
- stock_movements
- product stock updates

It protects against:

- duplicate sale or receipt
- invalid foreign keys
- invalid totals
- invalid payment totals
- invalid sale item totals
- invalid stock movement references
- stock movement user mismatch
- negative stock when stock would drop below zero

## UI Assets Already Present

Checkout UI currently supports:

- product search
- adding prepared product lines
- draft cart display
- totals display
- cash payment draft input
- shift context display
- locked Complete Sale button

## Current Gaps Before Sale Completion Can Be Unlocked

### 1. Explicit checkout session ID should be carried into the application request

Current state:

- CheckoutSessionContext has CashierSessionId in the WinForms layer.
- PosCheckoutApplicationRequest does not yet include CashierSessionId.
- PosCheckoutApplicationService currently creates Sale with CashierSessionId = null.
- PosCheckoutService later injects the currently open session.

This works for backend safety, but it is not ideal for UI-to-backend precision.

Recommended before unlocking Complete Sale:

- add CashierSessionId to PosCheckoutApplicationRequest
- pass it from CheckoutSessionContext
- set Sale.CashierSessionId from the request
- keep PosCheckoutService mismatch enforcement

Reason:

This prevents a stale checkout window from silently completing under a different open shift later.

### 2. CheckoutPresenter needs a completion boundary

Current state:

- CheckoutPresenter owns draft cart state.
- IPosCheckoutApplicationService exists in composition.
- CheckoutPresenter does not yet call IPosCheckoutApplicationService.

Recommended:

- add a WinForms-side checkout completion boundary
- presenter calls the boundary only after UI confirmation
- presenter remains UI orchestration, not infrastructure

### 3. Receipt number generation is not wired in the UI

Current state:

- draft builder requires ReceiptNumber before BuildApplicationRequest can succeed.
- UI does not yet generate or assign receipt numbers.

Recommended:

- add a receipt number generator service or repository-safe receipt sequence
- avoid timestamp-only receipt numbers if duplicate risk exists
- display generated receipt number before completion

### 4. Complete Sale button needs guarded enablement

Current state:

- Complete Sale is always disabled.

Recommended enablement rules:

- active CheckoutSessionContext exists
- CashierUserId exists
- CashierSessionId exists
- receipt number exists
- cart has at least one line
- payment exists
- amount paid >= total
- no presenter error state
- not currently submitting

### 5. Sale completion needs double-submit protection

Recommended:

- disable all checkout actions while submitting
- disable Complete Sale immediately on click
- use one async completion path
- prevent duplicate button clicks
- reset draft only after successful completion

### 6. Sale success UX is not defined

Recommended:

- show completed receipt number
- show final total and change
- clear draft after success
- keep shift context active
- return focus to barcode input

### 7. Receipt snapshot is not defined

Current state:

- ReceiptSnapshot exists in the Sale model and request path.
- UI has no receipt snapshot builder.

Recommended:

- create simple text receipt snapshot before printing exists
- include store name later when store settings UI is ready
- keep snapshot deterministic and audit-friendly

### 8. Payment UX still needs polish

Current state:

- cash payment can be added to draft
- payment list is not visibly listed
- clear payments exists in presenter but has no visible UI button in the current checkout shell

Recommended:

- show payment count or payment list
- add clear/reset payment control
- prevent confusing repeated cash payments unless intended

## Sale Completion Risk Level

Backend readiness: HIGH

UI readiness: MEDIUM

Unlock readiness: NOT YET

Reason:

The backend write pipeline is already strong, but the UI still needs explicit session ID propagation, receipt number generation, presenter completion boundary, guarded enablement, and double-submit protection before Complete Sale can safely be unlocked.

## Recommended Next Phase

Phase 50 - Explicit Checkout Session ID Enforcement

Goal:

- add CashierSessionId to PosCheckoutApplicationRequest
- carry CheckoutSessionContext.CashierSessionId into completion request
- set Sale.CashierSessionId from request
- preserve backend mismatch enforcement
- keep Complete Sale locked

## Locked Rule

Complete Sale remains locked after Phase 49.

## Comment Style

Implementation comments must use:

// Kirch: ...
