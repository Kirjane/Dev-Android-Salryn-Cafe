import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { isC1RecoveryRoute, resolveCafeRoute } from "../cashier/main";
import { c1MutationIntent, CafeMenuManagerRecovery, CafeMenuManagerRecoveryPage } from "./CafeMenuManagerRecovery";
import { publishMenu, saveMenuCategory, setMenuVariantModifierGroups, type MenuCatalogDraft } from "./menuCatalogAdapter";
import { BackofficeUnknownOutcomeError } from "../backoffice/useBackofficeOperation";

const UUID_REGEX = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

function makeEmptyCatalog(): MenuCatalogDraft {
  return {
    categories: [{ id: "cat-seed-1", name: "Uncategorized", isActive: true }],
    items: [],
    variants: [],
    modifierGroups: [],
    modifierOptions: [],
    itemModifierGroups: [],
    variantModifierGroups: [],
    currentPublication: null,
  };
}

describe("Canonical C1 recovery route and idempotent adapter", () => {
  beforeEach(() => {
    window.localStorage.clear();
    vi.restoreAllMocks();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("excludes /cafe/recovery/c1 when recovery flag is disabled and resolves it only when enabled", () => {
    expect(isC1RecoveryRoute("/cafe/recovery/c1", false)).toBe(false);
    expect(isC1RecoveryRoute("/cafe/recovery/c1", true)).toBe(true);
    expect(resolveCafeRoute("/cafe/recovery/c1")).toEqual({ mode: "workstation", initialNav: "cashier" });
    expect(resolveCafeRoute("/cafe/backoffice")).toEqual({ mode: "backoffice", initialNav: "reports" });
  });

  it("attaches Idempotency-Key and { operationId } on publishMenu and maps 500/ERR_IDEMPOTENCY_OUTCOME_UNKNOWN to BackofficeUnknownOutcomeError", async () => {
    const opId = "11111111-1111-4111-8111-111111111111";
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ revisionId: "s:1", revisionNumber: 1 }), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        }),
      )
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ code: "ERR_IDEMPOTENCY_OUTCOME_UNKNOWN", error: "Outcome unknown" }), {
          status: 409,
          headers: { "Content-Type": "application/json" },
        }),
      )
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ status: "ok" }), {
          status: 200,
          headers: { "Content-Type": "application/json" },
        }),
      );
    vi.stubGlobal("fetch", fetchMock);

    await publishMenu(opId);
    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      "/api/menu/publish",
      expect.objectContaining({
        method: "POST",
        headers: expect.objectContaining({
          "Content-Type": "application/json",
          "Idempotency-Key": opId,
        }),
        body: JSON.stringify({ operationId: opId }),
      }),
    );

    await expect(saveMenuCategory(opId, { id: null, name: "Coffee", isActive: true })).rejects.toBeInstanceOf(
      BackofficeUnknownOutcomeError,
    );

    await setMenuVariantModifierGroups(opId, "var-1", ["grp-1"]);
    expect(fetchMock).toHaveBeenNthCalledWith(
      3,
      "/api/menu/variants/var-1/modifier-groups",
      expect.objectContaining({
        method: "PUT",
        headers: expect.objectContaining({
          "Idempotency-Key": opId,
        }),
        body: JSON.stringify(["grp-1"]),
      }),
    );
  });

  it("retains the exact same operationId across retries after unknown outcome and blocks changed-payload submission while fenced", async () => {
    const seenKeys: string[] = [];
    const fetchMock = vi.fn(async (_input: RequestInfo | URL, init?: RequestInit) => {
      const headers = (init?.headers ?? {}) as Record<string, string>;
      seenKeys.push(headers["Idempotency-Key"]);
      if (seenKeys.length === 1) {
        return new Response(JSON.stringify({ code: "ERR_IDEMPOTENCY_OUTCOME_UNKNOWN", error: "Ambiguous fence" }), {
          status: 409,
          headers: { "Content-Type": "application/json" },
        });
      }
      return new Response(JSON.stringify({ id: "cat-1", name: "Coffee", isActive: true }), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      });
    });
    vi.stubGlobal("fetch", fetchMock);

    const onRefresh = vi.fn(async () => makeEmptyCatalog());
    const user = userEvent.setup();
    render(
      <CafeMenuManagerRecovery
        catalog={makeEmptyCatalog()}
        units={[{ id: "u-1", name: "Piece", isActive: true }]}
        onRefresh={onRefresh}
      />,
    );

    expect(screen.getByText("Empty menu catalog")).toBeInTheDocument();

    await user.type(screen.getByLabelText(/Category name/i), "Coffee");
    await user.click(screen.getByRole("button", { name: "Add category" }));

    await waitFor(() => {
      expect(screen.getByText("C1 mutation outcome uncertain")).toBeInTheDocument();
    });
    expect(seenKeys).toHaveLength(1);
    expect(seenKeys[0]).toMatch(UUID_REGEX);

    // Changing input while fenced must fail closed without issuing a second network write or minting a new key
    await user.clear(screen.getByLabelText(/Category name/i));
    await user.type(screen.getByLabelText(/Category name/i), "Changed Coffee");
    await user.click(screen.getByRole("button", { name: "Add category" }));

    await waitFor(() => {
      expect(screen.getByText(/Resolve the previous uncertain operation before submitting changed details/i)).toBeInTheDocument();
    });
    expect(seenKeys).toHaveLength(1);

    // Restoring identical intent retries with the exact same operationId
    await user.clear(screen.getByLabelText(/Category name/i));
    await user.type(screen.getByLabelText(/Category name/i), "Coffee");
    await user.click(screen.getByRole("button", { name: "Add category" }));

    await waitFor(() => {
      expect(screen.getByText("Menu category saved.")).toBeInTheDocument();
    });
    expect(seenKeys).toHaveLength(2);
    expect(seenKeys[1]).toBe(seenKeys[0]);
  });

  it("uses complete payload content and array order to distinguish durable mutation intents", () => {
    const base = { id: null, productId: "item-1", name: "Large", barcode: "A", displayOrder: 0 };
    const first = c1MutationIntent("variant:add", base);
    expect(c1MutationIntent("variant:add", { ...base, barcode: "B" })).not.toBe(first);
    expect(c1MutationIntent("variant:add", { ...base, displayOrder: 1 })).not.toBe(first);
    expect(c1MutationIntent("item-links", { productId: "item-1", groupIds: ["a", "b"] })).not.toBe(
      c1MutationIntent("item-links", { productId: "item-1", groupIds: ["b", "a"] }),
    );
  });

  it("retains an unknown operation ID on diagnostic refresh instead of clearing its fence", async () => {
    vi.stubGlobal("fetch", vi.fn(async () =>
      new Response(JSON.stringify({ code: "ERR_IDEMPOTENCY_OUTCOME_UNKNOWN", error: "Outcome uncertain" }), {
        status: 409, headers: { "Content-Type": "application/json" },
      }),
    ));
    const onRefresh = vi.fn(async () => null);
    const user = userEvent.setup();
    render(<CafeMenuManagerRecovery catalog={makeEmptyCatalog()} units={[]} onRefresh={onRefresh} />);
    await user.type(screen.getByLabelText(/Category name/i), "Coffee");
    await user.click(screen.getByRole("button", { name: "Add category" }));
    await waitFor(() => expect(screen.getByText("C1 mutation outcome uncertain")).toBeInTheDocument());
    const storageKey = "salryn.cafe.backoffice.op.c1-menu-recovery.v1";
    const before = window.localStorage.getItem(storageKey);
    expect(before).toContain("operationId");
    await user.click(screen.getByRole("button", { name: "Refresh catalog (retain operation ID)" }));
    await waitFor(() => expect(onRefresh).toHaveBeenCalledOnce());
    expect(window.localStorage.getItem(storageKey)).toBe(before);
    expect(screen.getByText("C1 mutation outcome uncertain")).toBeInTheDocument();
  });

  it("fails closed on a missing test-host endpoint even in Vite development mode", async () => {
    const fetchMock = vi.fn(async (_input: RequestInfo | URL) => new Response("Not found", { status: 404 }));
    vi.stubGlobal("fetch", fetchMock);
    render(<CafeMenuManagerRecoveryPage />);
    await waitFor(() => expect(screen.getByText("C1 recovery route blocked on non-disposable host")).toBeInTheDocument());
    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(fetchMock.mock.calls[0][0]).toBe("/api/test/host-identity");
  });

  it("blocks protected production database paths on non-disposable hosts", async () => {
    const fetchMock = vi.fn(async () =>
      new Response(
        JSON.stringify({
          status: "ok",
          testHostNonce: "abc",
          processId: 123,
          databasePath: "C:\\ProgramData\\SalrynCafe\\data\\salryn-cafe.db",
        }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      ),
    );
    vi.stubGlobal("fetch", fetchMock);

    await act(async () => {
      render(<CafeMenuManagerRecoveryPage />);
    });

    await waitFor(() => {
      expect(screen.getByText("C1 recovery route blocked on non-disposable host")).toBeInTheDocument();
    });
  });
});
