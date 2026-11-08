# Salryn W4 Frontend Compatibility Final Verification

**Document Reference**: `00-HANDOFF/SALRYN_W4_FRONTEND_COMPATIBILITY_FINAL.md`  
**Author**: Antigravity Frontend Architecture & UX Lead  
**Authority**: Kirch Ivan Balite, Engineer  
**Source Contract**: `00-HANDOFF/SALRYN_W4_FRONTEND_INTEGRATION_CONTRACT.md` (ref: `62eadb201d344516f3c6e7bb491b18db0fc0b7ff`)  
**Backend Target Branch**: PR #22 (`codex/salryn-w4-drawer-reconciliation-r1`, ref `5f0d2d0e86e56525d084808163eb016c6d4edb2e`)  
**Frontend Implementation Branch**: PR #21 (`antigravity/salryn-premium-pos-ux`)  
**Integration Status**: `NOT_INTEGRATED` (Standalone PR #21 holds backwards-compatible contract scaffolding; reconciliation occurs in I1's PR #24 `i1/integration-w4-drawer-reconciliation`)

---

## 1. Compatibility Overview & Honest Scope

This document provides a factual assessment of contract alignment between Antigravity's PR #21 frontend redesign and Codex's PR #22 W4 drawer reconciliation backend work.

### 1.1 Branch & File Overlap Truth
- **PR #21 and PR #22 are NOT disjoint.**
- Both PR #21 and PR #22 touch the following frontend files:
  1. `src/Salryn.App.Web/src/pages/CheckoutPage.tsx`
  2. `src/Salryn.App.Web/src/api/checkout.ts`
- PR #21 contains the complete 22-page premium visual design system, accessibility upgrades, inert dialog containment, and responsive mobile layout.
- PR #22 introduces the W4 drawer reconciliation fields: `HostShift` model additions (`varianceCents`, `coverageStatus`, `reviewStatus`, `expectedCashCents`, `countedCashCents`), structured `CloseShiftRequest` payload, and physical count confirmation prompt.
- **Standalone PR #21 is strictly `NOT_INTEGRATED` with W4.** Do NOT merge PR #21 directly into `main` or `develop`.
- Reconciling PR #21 and PR #22 is the explicit responsibility of Integration Lead I1 on PR #24 (`i1/integration-w4-drawer-reconciliation`).

---

## 2. API Contract Mapping & Type Alignment

In `src/Salryn.App.Web/src/api/checkout.ts`, PR #21 aligns types with PR #22 while preserving backward-compatible execution:

### 2.1 Cash Drawer Status Summary
- **Endpoint**: `GET /api/cash-drawer`
- **Permission Guard**: `Shift.OpenClose`
- **Response Model**:
  ```typescript
  export interface CashDrawerSummaryResponse {
    shiftId: string;
    openedAt: string;
    openedByUsername: string;
    openingFloatCents: number;
    expectedCashCents: number;
    cashSalesCents: number;
    cashCollectionsCents: number;
    cashRefundsCents: number;
    isShiftOpen: boolean;
  }
  ```

### 2.2 Host Shift Model Alignment (`HostShift`)
```typescript
export interface HostShift {
  shiftId: string;
  openedAtUtc: string;
  closedAtUtc?: string | null;
  shiftState: "Open" | "Closed" | string;
  openingCashCents: number;
  expectedCashCents?: number | null;
  countedCashCents?: number | null;
  varianceCents?: number | null;
  coverageStatus?: string | null;
  reviewStatus?: string | null;
}
```

### 2.3 Close Shift Request & Reconciliation
- **Endpoint**: `POST /api/shift/close`
- **Permission Guard**: `Shift.OpenClose`
- **Request Model**:
  ```typescript
  export interface CloseShiftRequest {
    countedCashCents: number;
    physicalCountConfirmed: boolean;
    clientRequestId: string;
    note?: string;
  }
  ```
- **Response Model**:
  ```typescript
  export interface CloseShiftResponse {
    shiftId: string;
    closedAt: string;
    openedAt: string;
    openingFloatCents: number;
    expectedCashCents: number;
    countedCashCents?: number;
    overShortCents?: number;
    discrepancyNote?: string;
    summary: string;
  }
  ```
- **Frontend Implementation (`src/api/checkout.ts`)**:
  ```typescript
  export async function closeHostShift(payload?: CloseShiftRequest): Promise<CloseShiftResponse> {
    const response = await fetch("/api/shift/close", {
      method: "POST",
      headers: payload ? { "Content-Type": "application/json" } : undefined,
      body: payload ? JSON.stringify(payload) : undefined,
    });
    if (!response.ok) {
      const errorData = await response.json().catch(() => ({}));
      throw new Error(errorData.message || `Failed to close shift: ${response.statusText}`);
    }
    return response.json();
  }
  ```
- **Compatibility Behavior**:
  - Parameterless invocation (`closeHostShift()`) supports pre-W4 Host backends.
  - Payload-driven invocation (`closeHostShift(payload)`) sends counted cash, confirmation, and idempotency client request ID conforming to Codex's PR #22 specification.

### 2.4 Error Handling & HTTP Status Code Matrix
- `400 Bad Request`: Validation failure (negative count, unconfirmed count).
- `401 Unauthorized`: Session expired; prompts immediate re-login modal.
- `403 Forbidden`: Operator lacks `Shift.OpenClose` permission.
- `409 Conflict`: Shift is already closed or pending concurrent closure.

---

## 3. Integration Lead (I1) Merge Instructions

When reconciling PR #21 (Antigravity frontend) and PR #22 (Codex W4 backend) on PR #24:

1. **Do Not Direct Merge PR #21 to `main`**:
   PR #21 must remain a draft PR against its base branch until I1 cherry-picks / merges both PR #21 and PR #22 into `i1/integration-w4-drawer-reconciliation` (PR #24).
2. **File Overlap Resolution in `CheckoutPage.tsx`**:
   - Preserve Antigravity's responsive layout, command bar, visual tokens, accessibility, and inert modal architecture from PR #21.
   - Port Codex's counted-cash physical confirmation dialog inputs and variance calculation from PR #22 into the `showCloseShiftModal` dialog in `CheckoutPage.tsx`.
3. **Verification Commands**:
   - Run `npm run build` in `src/Salryn.App.Web` (Exit 0).
   - Run Playwright test suite `npm run test:e2e` (All 27 tests pass, 44 snapshots match).
   - Run C# CoreSmoke shift suite `dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only v2-host-shift`.
