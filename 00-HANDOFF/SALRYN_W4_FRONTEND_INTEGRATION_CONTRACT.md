# Salryn W4 frontend integration contract

Status: integration brief only; no branch merge or policy approval. W4 ref inspected: `5f0d2d0e86e56525d084808163eb016c6d4edb2e`. Antigravity ref inspected read-only through `git show`: `6094016165a0664c82f5636f75d0a77f6e94815e`. No Antigravity worktree file was opened or modified.

## Collision inventory

Both branches change `src/Salryn.App.Web/src/api/checkout.ts` and `src/Salryn.App.Web/src/pages/CheckoutPage.tsx`. Antigravity currently retains the old bodyless `closeHostShift()` and plans a premium shared-dialog close confirmation. W4 changes the close contract and adds the minimum physical-count state. Integration must preserve Antigravity's responsive two-zone checkout, shared accessible dialog, semantic tokens and keyboard/focus behavior while replacing its bodyless close call with the W4 contract below. Do not choose one whole file over the other.

## Required API types

```ts
type HostShift = {
  shiftState: string;
  shiftId: string | null;
  openedAtUtc: string | null;
  openingCashCents: number | null;
  expectedCashCents?: number | null;
  countedCashCents?: number | null;
  varianceCents?: number | null;
  coverageStatus?: string | null;
  reviewStatus?: string | null;
  message: string;
};

type CloseShiftRequest = {
  countedCashCents: number;
  physicalCountConfirmed: boolean;
  clientRequestId: string;
  note?: string;
};
```

`closeHostShift(request)` must send JSON to `POST /api/shift/close` with credentials and `Content-Type: application/json`. Never restore the bodyless call and never derive `countedCashCents` from expected cash.

## Endpoints and guards

| Endpoint | UI consumer | Guard / behavior |
|---|---|---|
| `GET /api/shift` | checkout header | `Shift.OpenClose`; own shift |
| `POST /api/shift/open` | opening dialog | `Shift.OpenClose` |
| `GET /api/cash-drawer` | close dialog live source summary | `Shift.OpenClose`; own open drawer |
| `POST /api/cash-drawer/movements` | future approved cash-in/out/refund UI only | `Shift.OpenClose`; policy-sensitive controls must not be invented |
| `POST /api/shift/close` | physical count dialog | `Shift.OpenClose`; explicit count/confirmation/request ID |
| `GET /api/cash-drawer/history?limit=&offset=` | owner/audit history | `Audit.View`; limit 1–100 |
| `POST /api/cash-drawer/{sessionId}/review` | discrepancy acknowledgment | currently `Audit.View`, but final financial authority remains pending; count submitter is denied |

## Close dialog journey to preserve

1. Use Antigravity's shared accessible `Dialog`, focus management and dense counter layout.
2. Show opening float and coverage state as read-only context. Do not prefill, copy, reveal as a suggested value, or auto-balance the physical count from expected cash.
3. Require a finite, nonnegative manually entered count and explicit physical-count checkbox. Generate one stable UUID per submission attempt and retain it for network retry; do not generate a new ID after an ambiguous response.
4. While submitting, lock amount/confirmation and prevent double submission.
5. On success show expected, counted and signed variance with tabular numerals. `variance === 0` may show `Reconciled`; nonzero must show warning/danger `PendingReview`, never success.
6. Clear cart only after confirmed close success. Refresh shift/drawer queries.

## Required feedback mapping

| Backend code/status | UI state |
|---|---|
| `400 CashDrawer.PhysicalCountRequired` or omitted body | Inline danger; keep entered count; focus invalid control |
| `409 CashDrawer.RequestConflict` | Danger: request was already used with different details; do not silently issue a new request ID or overwrite the original |
| `409 CashDrawer.CoverageUnverified` | Blocking danger: source coverage is unverified; no force-close affordance |
| `409 CashDrawer.NoOpenShift` | Refresh shift; neutral closed/no-open state |
| `409 CashDrawer.SelfReviewDenied` | Danger; reviewer cannot acknowledge own count |
| `401` | Session-expired flow |
| `403` | Role-denied flow; do not expose review/history controls |

Semantic tokens from the Antigravity blueprint: verified zero variance uses emerald success; `PendingReview`/unverified uses amber warning; failed/conflicting source evidence uses rose danger; closed history is neutral. Currency values use `tabular-nums`.

## Merge verification checklist

- Search for `closeHostShift()` with no argument: zero occurrences.
- Bodyless `/api/shift/close`: zero occurrences outside negative tests.
- Cash, full Utang and split checkout behavior remains unchanged; drawer expected uses actual cash only.
- 1024×768 and 1366×768 close dialog is visible without losing submit/cancel; Escape cannot dismiss while busy.
- Retry preserves one `clientRequestId`; changed payload requires an explicit new operator action, not silent retry mutation.
- Run `tools/Run-W4Acceptance.ps1`, Antigravity Playwright checkout/visual gates and `npm run build` after integration.

