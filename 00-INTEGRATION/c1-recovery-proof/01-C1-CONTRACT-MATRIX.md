# 01 — C1 Menu Catalog Contract Matrix

**Canonical Reference:** `Kirjane/Salryn-CafeYza@4241ea1ec0a8cebf552d93f25a7ac134d7d4d6f3` (identical on `f3768bc0c85028e7d67c19a2f1053fda7f89b209`)  
**Current Candidate Host:** `Kirjane/Dev-Salryn-Cafe@9417f3ad7af7e5f957ae5b6d2adee15d4e8b9ac5`

## 1. Operation-by-Operation Classification

Legend:
- **Prompt Classification:** `DIRECT_REUSE` | `TYPED_ADAPTER` | `SMALL_EXTENSION` | `INCOMPATIBLE`
- **Issue #44 Classification:** `DIRECT` | `ADAPTER` | `REIMPLEMENT` | `UNSUPPORTED`

| C1 Operation | Canonical Source (`4241ea1`) | Current Host Contract (`9417f3a`) | Prompt / Issue Class | Auth, Idempotency & Persistence Semantics |
|---|---|---|---|---|
| Load catalog draft | `menuCatalog.ts:102-104` `fetchMenuCatalog()` (`GET /api/menu/catalog`) | `LocalMenuCatalogEndpoints.cs:12-24` -> `SqliteMenuCatalogStore.GetDraft()` (`17-30,1087-1237`) | `DIRECT_REUSE` / `DIRECT` | Requires `Products.View` (`401`/`403` when unauthorized). GET bypasses `CafeMutationIdempotencyEndpointFilter.cs:30-33`. Exact JSON DTO match (`MenuCatalogDraft`). |
| Load current publication | `menuCatalog.ts:106-108` `fetchCurrentMenuPublication()` (`GET /api/menu/publication/current`) | `LocalMenuCatalogEndpoints.cs:26-38` -> `GetCurrentPublication()` (`731-751`) | `DIRECT_REUSE` / `DIRECT` | Requires `Products.View`. Returns `MenuPublicationSnapshot \| null`. Used by Cashier workstation (`hostClient.ts:239-246`). |
| Load product units | `products.ts:105-108` `fetchProductUnits()` (`GET /api/products/catalog/units`) | `ReadOnlyEndpointExtensions.cs:107-123` (`CatalogOptionResponse[]`) | `DIRECT_REUSE` / `DIRECT` | Requires `Products.View`. Returns `{ id, name, isActive }[]` matching `MenuUnitOption`. |
| Save / toggle menu category | `menuCatalog.ts:110-116` `saveMenuCategory` (`POST /api/menu/categories`) | `LocalMenuCatalogEndpoints.cs:40-52` -> `SaveCategory()` (`32-81`) | `TYPED_ADAPTER` / `ADAPTER` | Requires `Products.Manage` + `Idempotency-Key: <guid>`. Missing key returns `400 CafeMutation.OperationIdRequired` (`CafeMutationIdempotencyEndpointFilter.cs:115-118`). Upserts `categories` preserving `id`; duplicate name returns `409 Menu.DuplicateCategory`. |
| Create / edit / toggle menu item | `menuCatalog.ts:118-132` `saveMenuItem` (`POST /api/menu/items`) | `LocalMenuCatalogEndpoints.cs:54-66` -> `SaveItem()` (`83-296`) | `TYPED_ADAPTER` / `ADAPTER` | Requires `Products.Manage` + `Idempotency-Key: <guid>`. Updates `products` in-place when `productId` exists (`117-198`), upserts `menu_item_availability` (`200-217`), auto-creates `'Standard'` row in `menu_variants` on insert (`219-261`). |
| Create / edit / toggle size variant | `menuCatalog.ts:134-147` `saveMenuVariant` (`POST /api/menu/variants`) | `LocalMenuCatalogEndpoints.cs:68-80` -> `SaveVariant()` (`298-417`) | `TYPED_ADAPTER` / `ADAPTER` | Requires `Products.Manage` + `Idempotency-Key: <guid>`. Upserts `menu_variants` with `EnsureVariantIdentifiersAvailable` SKU/barcode uniqueness check (`1019-1085`). |
| Save modifier group | `menuCatalog.ts:149-159` `saveMenuModifierGroup` (`POST /api/menu/modifier-groups`) | `LocalMenuCatalogEndpoints.cs:82-94` -> `SaveModifierGroup()` (`419-510`) | `TYPED_ADAPTER` / `ADAPTER` | Requires `Products.Manage` + `Idempotency-Key: <guid>`. Enforces `minSelections >= 0`, `maxSelections >= minSelections`, and `isRequired => minSelections >= 1`. |
| Save modifier option | `menuCatalog.ts:161-170` `saveMenuModifierOption` (`POST /api/menu/modifier-options`) | `LocalMenuCatalogEndpoints.cs:96-108` -> `SaveModifierOption()` (`512-598`) | `TYPED_ADAPTER` / `ADAPTER` | Requires `Products.Manage` + `Idempotency-Key: <guid>`. Verifies `groupId` exists (`530-536`). |
| Link modifier groups to item | `menuCatalog.ts:172-178` `setMenuItemModifierGroups` (`PUT /api/menu/items/{productId}/modifier-groups`) | `LocalMenuCatalogEndpoints.cs:110-124` -> `SetItemModifierGroups()` (`600-615`) | `TYPED_ADAPTER` / `ADAPTER` | Requires `Products.Manage` + `Idempotency-Key: <guid>`. Body is bare JSON array `string[]` (`groupIds`). Replaces `menu_item_modifier_groups` rows for `productId`. |
| Link modifier groups to variant | Helper in `menuCatalog.ts:180-186` (`PUT /api/menu/variants/{variantId}/modifier-groups`); **no UI in `CafeMenuManager.tsx:545-579`** | `LocalMenuCatalogEndpoints.cs:126-140` -> `SetVariantModifierGroups()` (`617-632`) | `SMALL_EXTENSION` / `ADAPTER` + New UI Control | Requires `Products.Manage` + `Idempotency-Key: <guid>`. Body is bare JSON array `string[]` (`groupIds`). New bounded variant selector + link checkboxes + save action added in `CafeMenuManagerRecovery.tsx`. |
| Publish menu revision | `menuCatalog.ts:188-190` `publishMenu()` (`POST /api/menu/publish`, no body) | `LocalMenuCatalogEndpoints.cs:142-154` (`MenuPublishRequest? request`) -> `PublishCurrent()` (`634-729`) | `TYPED_ADAPTER` / `ADAPTER` | Requires `Products.Manage` + `Idempotency-Key: <guid>` + JSON body `{ operationId }` (matching `Idempotency-Key`; conflicting header vs body rejected by `TryResolveOperationId` at `CafeMutationIdempotencyGuard.cs:31-73`). Creates immutable `menu_revisions` + snapshot tables + updates `menu_current_publication`. |

## 2. Error, Retry & Unknown-Outcome Fencing Contract

| Host Condition | HTTP Status / Code | Required Frontend Adapter Behavior |
|---|---|---|
| Unauthenticated / revoked session | `401` (`Auth.OperatorSessionRevoked`) | Surface failed-load / auth-required UI cleanly; never treat as empty catalog; release in-flight lock. |
| Missing `Products.Manage` permission | `403` (`Auth.OperatorPermissionsChanged` or Forbidden) | Show explicit authorization rejection; zero client state mutation; fence is not retained because `CafeMutationIdempotencyEndpointFilter` / `CafeMutationIdempotencyGuard:158-182` does not persist 401/403 fences. |
| Missing/invalid `Idempotency-Key` | `400` (`CafeMutation.OperationIdRequired`) | Adapter always supplies valid UUID v4 via `Idempotency-Key`; deterministic 400 validation errors propagate directly (`useBackofficeOperation` clears operation slot so operator can fix input). |
| Changed payload with reused `operationId` | `409` (`ERR_IDEMPOTENCY_CONFLICT`) | Propagate deterministic rejection message; never overwrite existing operation silently. |
| Duplicate business entity | `409` (`Menu.DuplicateCategory`, `Menu.DuplicateIdentifier`, `Menu.DuplicateVariant*`, `Menu.DuplicateModifierOption`) | Show deterministic validation/conflict error so operator can rename/correct; `HostApiError.status === 409` without `ERR_IDEMPOTENCY_OUTCOME_UNKNOWN` / `ERR_IDEMPOTENCY_IN_FLIGHT` is treated as deterministic rejection. |
| Identical replay | `200` + header `X-Idempotency-Replayed: true` | Return cached JSON response transparently with zero duplicate SQLite rows. |
| Network drop / `5xx` / server fence `409 ERR_IDEMPOTENCY_OUTCOME_UNKNOWN` or `ERR_IDEMPOTENCY_IN_FLIGHT` | `0`, `>=500`, or `409 ERR_IDEMPOTENCY_OUTCOME_UNKNOWN` / `ERR_IDEMPOTENCY_IN_FLIGHT` | Throw `BackofficeUnknownOutcomeError(operationId, ...)` so `useBackofficeOperation` retains exact `operationId` & `intentKey` in `localStorage`, blocks changed-payload submissions, and lets the operator inspect the catalog or retry the identical intent with the same `operationId`. |

## 3. Component Reuse & Dependency Delta vs Canonical `CafeMenuManager.tsx`

- **Reused verbatim in semantics:** All C1 state forms, validation rules, pesos-to-cents conversion (`centsFromPesos` / `pesosFromCents`), item selection & `resetItemEditor` ("Create another item"), separate `isActive` vs `isTemporarilyUnavailable` toggles, category activation chips, variant creation/toggling, required modifier group (`min`/`max`) creation & option creation, item-level modifier link checkboxes, and publication summary card (`revisionId`, `contentHash`, `publicationHash`, `publishedAtUtc`).
- **Adapted without adding dependencies:**
  - Replaced `@tanstack/react-query` (`useQuery` / `useQueryClient`) with React `useState` + `useCallback` + `useEffect` (`refreshCatalog`).
  - Replaced legacy `./ui` Tailwind wrappers with existing `@salryn/cafe-ui` primitives (`Alert`, `Button`, `Card`, `CardBody`, `CardHeader`, `FormField`, `SelectInput`, `StatusBadge`, `TextInput`) + `cafe-tokens.css` variables.
  - Replaced canonical generic `run()` (`mutationInFlightRef`) with `useBackofficeOperation("c1-menu-recovery")` + `intentKey` serialization + `BackofficeUnknownOutcomeError` UI alert.
  - Added bounded `SMALL_EXTENSION` variant-level modifier group selector & `"Save variant modifier links"` control.
  - Added explicit empty-catalog notice (`items.length === 0 && categories.length === 0`) distinct from load errors, plus inline manager sign-in when unauthenticated (`401`).
