/**
 * Canonical source attribution:
 *   Repository: Kirjane/Salryn-CafeYza
 *   Commit: 4241ea1ec0a8cebf552d93f25a7ac134d7d4d6f3
 *   Original path: src/Salryn.App.Web/src/api/menuCatalog.ts
 *
 * Adapted for Dev-Salryn-Cafe Host authority (CafeMutationIdempotencyEndpointFilter):
 *   - Every mutating request supplies a client-generated GUID via Idempotency-Key
 *     (and body { operationId } for POST /api/menu/publish).
 *   - Network failures, HTTP 5xx, ERR_IDEMPOTENCY_OUTCOME_UNKNOWN, and
 *     ERR_IDEMPOTENCY_IN_FLIGHT throw BackofficeUnknownOutcomeError so the caller
 *     retains the same operationId across identical retries and fences changes.
 *   - Reuses existing hostRequest / HostApiError and BackofficeUnknownOutcomeError primitives.
 */

import { HostApiError, hostRequest } from "../api/hostClient";
import { BackofficeUnknownOutcomeError } from "../backoffice/useBackofficeOperation";
import type { MenuUnitOption } from "../backoffice/types";
import type { HostSession } from "../cashier/types";

export type MenuCategory = {
  id: string;
  name: string;
  isActive: boolean;
};

export type MenuItem = {
  productId: string;
  categoryId: string | null;
  name: string;
  description: string | null;
  unitId: string;
  sku: string | null;
  barcode: string | null;
  sellingPriceCents: number;
  costPriceCents: number | null;
  isActive: boolean;
  isTemporarilyUnavailable: boolean;
};

export type MenuVariant = {
  id: string;
  productId: string;
  name: string;
  sku: string | null;
  barcode: string | null;
  sellingPriceCents: number;
  costPriceCents: number | null;
  isActive: boolean;
  isTemporarilyUnavailable: boolean;
  displayOrder: number;
};

export type MenuModifierGroup = {
  id: string;
  name: string;
  isRequired: boolean;
  minSelections: number;
  maxSelections: number;
  isActive: boolean;
  displayOrder: number;
};

export type MenuModifierOption = {
  id: string;
  groupId: string;
  name: string;
  priceDeltaCents: number;
  isActive: boolean;
  displayOrder: number;
};

export type MenuModifierLink = {
  ownerId: string;
  groupId: string;
  displayOrder: number;
};

export type MenuPublicationSummary = {
  revisionId: string;
  storeId: string;
  revisionNumber: number;
  contentHash: string;
  publicationHash: string;
  status: string;
  publishedAtUtc: string;
};

export type MenuCatalogDraft = {
  categories: MenuCategory[];
  items: MenuItem[];
  variants: MenuVariant[];
  modifierGroups: MenuModifierGroup[];
  modifierOptions: MenuModifierOption[];
  itemModifierGroups: MenuModifierLink[];
  variantModifierGroups: MenuModifierLink[];
  currentPublication: MenuPublicationSummary | null;
};

export type MenuPublicationSnapshot = MenuPublicationSummary & {
  categories: unknown[];
  items: unknown[];
  variants: unknown[];
  modifierGroups: unknown[];
  modifierOptions: unknown[];
  itemModifierGroups: unknown[];
  variantModifierGroups: unknown[];
};

export type TestHostIdentity = {
  status: string;
  testHostNonce: string;
  processId: number;
  databasePath: string;
};

export async function withUncertainOutcomeGuard<T>(
  operationId: string,
  runner: () => Promise<T>,
): Promise<T> {
  try {
    return await runner();
  } catch (err) {
    if (
      (err instanceof HostApiError && (
        err.status === 0 ||
        err.status >= 500 ||
        err.code === "ERR_IDEMPOTENCY_OUTCOME_UNKNOWN" ||
        err.code === "ERR_IDEMPOTENCY_IN_FLIGHT" ||
        err.code === "ERR_NETWORK_UNKNOWN"
      )) ||
      err instanceof TypeError ||
      (err instanceof Error && err.name === "AbortError")
    ) {
      throw new BackofficeUnknownOutcomeError(
        operationId,
        "The server outcome for this C1 menu change is uncertain. Do not create a duplicate record; refresh and inspect the saved draft before retrying.",
      );
    }
    throw err;
  }
}

export function fetchTestHostIdentity(): Promise<TestHostIdentity> {
  return hostRequest<TestHostIdentity>("/api/test/host-identity");
}

export function fetchSession(): Promise<HostSession> {
  return hostRequest<HostSession>("/api/session");
}

export function loginOperator(username: string, password: string): Promise<HostSession> {
  return hostRequest<HostSession>("/api/auth/login", { username, password }, "POST");
}

export function logoutOperator(): Promise<{ status?: string }> {
  return hostRequest<{ status?: string }>("/api/auth/logout", {}, "POST");
}

export function fetchMenuCatalog(): Promise<MenuCatalogDraft> {
  return hostRequest<MenuCatalogDraft>("/api/menu/catalog");
}

export function fetchCurrentMenuPublication(): Promise<MenuPublicationSnapshot | null> {
  return hostRequest<MenuPublicationSnapshot | null>("/api/menu/publication/current");
}

export function fetchProductUnits(): Promise<MenuUnitOption[]> {
  return hostRequest<MenuUnitOption[]>("/api/products/catalog/units");
}

export function saveMenuCategory(
  operationId: string,
  request: {
    id: string | null;
    name: string;
    isActive: boolean;
  },
): Promise<MenuCategory> {
  return withUncertainOutcomeGuard(operationId, () =>
    hostRequest<MenuCategory>("/api/menu/categories", request, "POST", operationId),
  );
}

export function saveMenuItem(
  operationId: string,
  request: {
    productId: string | null;
    categoryId: string | null;
    name: string;
    description: string | null;
    unitId: string;
    sku: string | null;
    barcode: string | null;
    sellingPriceCents: number;
    costPriceCents: number | null;
    isActive: boolean;
    isTemporarilyUnavailable: boolean;
  },
): Promise<MenuItem> {
  return withUncertainOutcomeGuard(operationId, () =>
    hostRequest<MenuItem>("/api/menu/items", request, "POST", operationId),
  );
}

export function saveMenuVariant(
  operationId: string,
  request: {
    id: string | null;
    productId: string;
    name: string;
    sku: string | null;
    barcode: string | null;
    sellingPriceCents: number;
    costPriceCents: number | null;
    isActive: boolean;
    isTemporarilyUnavailable: boolean;
    displayOrder: number;
  },
): Promise<MenuVariant> {
  return withUncertainOutcomeGuard(operationId, () =>
    hostRequest<MenuVariant>("/api/menu/variants", request, "POST", operationId),
  );
}

export function saveMenuModifierGroup(
  operationId: string,
  request: {
    id: string | null;
    name: string;
    isRequired: boolean;
    minSelections: number;
    maxSelections: number;
    isActive: boolean;
    displayOrder: number;
  },
): Promise<MenuModifierGroup> {
  return withUncertainOutcomeGuard(operationId, () =>
    hostRequest<MenuModifierGroup>("/api/menu/modifier-groups", request, "POST", operationId),
  );
}

export function saveMenuModifierOption(
  operationId: string,
  request: {
    id: string | null;
    groupId: string;
    name: string;
    priceDeltaCents: number;
    isActive: boolean;
    displayOrder: number;
  },
): Promise<MenuModifierOption> {
  return withUncertainOutcomeGuard(operationId, () =>
    hostRequest<MenuModifierOption>("/api/menu/modifier-options", request, "POST", operationId),
  );
}

export function setMenuItemModifierGroups(
  operationId: string,
  productId: string,
  groupIds: string[],
): Promise<{ status: string }> {
  return withUncertainOutcomeGuard(operationId, () =>
    hostRequest<{ status: string }>(
      `/api/menu/items/${encodeURIComponent(productId)}/modifier-groups`,
      groupIds,
      "PUT",
      operationId,
    ),
  );
}

export function setMenuVariantModifierGroups(
  operationId: string,
  variantId: string,
  groupIds: string[],
): Promise<{ status: string }> {
  return withUncertainOutcomeGuard(operationId, () =>
    hostRequest<{ status: string }>(
      `/api/menu/variants/${encodeURIComponent(variantId)}/modifier-groups`,
      groupIds,
      "PUT",
      operationId,
    ),
  );
}

export function publishMenu(operationId: string): Promise<MenuPublicationSnapshot> {
  return withUncertainOutcomeGuard(operationId, () =>
    hostRequest<MenuPublicationSnapshot>(
      "/api/menu/publish",
      { operationId },
      "POST",
      operationId,
    ),
  );
}
