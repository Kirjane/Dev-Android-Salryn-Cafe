# Salryn I2 Source and API Conflict Matrix

Prepared: 2026-11-09 Asia/Manila  
Integration starting HEAD: `3bd1e0957099f3109938a9fe51e057bdb99f1ae3`  
W4 source: `62eadb201d344516f3c6e7bb491b18db0fc0b7ff`  
Frontend RC2 source: **WAITING — do not substitute RC1 `735b2494f31e89c6753cdea04d4a61cacc4415cf`**

| Surface | W4/I1 invariant | RC2 import check | Resolution authority |
|---|---|---|---|
| `api/checkout.ts` | `closeHostShift(request)` requires actual `countedCashCents`, `physicalCountConfirmed`, stable `clientRequestId`, optional note; no bodyless overload | Compare exported types, request body, response/error parsing | W4 financial contract wins; frontend presentation adapts |
| `CheckoutPage.tsx` | blind physical count, no expected-as-count fallback, stable retry identity, no duplicate close, no force-close, immutable pending review | Manually merge premium layout and all cash/Utang/split states; test 0 count separately from 0 variance | W4 semantics + RC2 layout |
| `Dialog.tsx` | initial focus, forward/reverse trap, inert background, scroll lock, trigger restoration, busy-dismissal guard | Verify nested dialog stack and cleanup rather than replacing I1 primitive wholesale | Accessible behavior must pass executable tests |
| `Notifications.tsx` | financial errors remain visible but cannot obscure modal title, fields, or resolution CTA at 390/1024 and scale stress | Import RC2 shared solution and run DOM geometry assertion plus screenshots | RC2 presentation, I1 independent verification |
| `App.tsx` / navigation | meaningful `page`, dates, and `productId`; reload/back/forward; permission redirects cannot expose restricted page | Reconcile RC2 navigation changes with I1 durable URLs | Security and durable deep links win |
| Receivables | signed U1 entitlement, full Utang/split, cash collection linked to cash source, noncash excluded | Verify action/error/denial UI matches actual API classification | Backend entitlement and source-event invariants win |
| Owner reports | Manila store-day boundaries; sales/cash/AR remain distinct; drilldowns encode URLs | Compare labels and navigation against read-only source totals | Backend totals/boundaries win |
| Roles/users | `Shift.OpenClose`, review authorization, cashier/manager denial, no self-review authority invention | Check persona menus and denied actions | Existing permission service wins; policy remains pending |
| Licensing/startup | signed entitlement only; no fake license, bypass, or production activation | Preserve startup/bootstrap/unlicensed/denied screens | Canonical LicensingOps remains last |
| Migrations/backup | v25/v26 and future-schema rejection, restore verification | Frontend must not mutate schema meaning or label mocks as live proof | W4 migration/backup tests win |

## Required same-SHA checks after RC2 import

- W4 15-gate runner with cold `dotnet restore`, Release build, CoreSmoke, infrastructure, dependency scans and `git diff --check`.
- RC2 complete Playwright action/state suite, I1 W4 integration suite, strict visual comparison, and deliberate mismatch proof.
- Responsive/no-overlap checks at 390×844, 1024×768, 1366×768, 1920×1080, 125%, and 150%.
- Mocked results labeled MOCKED; authorized licensed Host remains `BLOCKED_NO_HOST` if unavailable; real drawer/operator stays NOT_RUN.

All five W4 accounting policies and the dedicated discrepancy-review role remain pending Kirch approval.
