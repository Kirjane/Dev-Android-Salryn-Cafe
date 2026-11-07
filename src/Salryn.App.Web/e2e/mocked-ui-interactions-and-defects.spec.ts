import { expect, test, type Page } from "@playwright/test";
import { installMockSalrynApi } from "./fixtures/mockApi";

function sidebarNavButton(page: Page, label: string) {
  return page.locator("aside").getByRole("button", { name: label, exact: true });
}

/**
 * Known UX defects are pinned as CURRENT behaviour so that the A0 baseline is truthful. When #20 fixes a defect,
 * the matching KNOWN-DEFECT test is expected to fail and must be inverted deliberately in the same PR.
 */
function knownDefect(id: string, description: string) {
  test.info().annotations.push({ type: "known-defect", description: `${id}: ${description}` });
}

test.describe("MOCKED_UI Keyboard, Navigation, Accessibility & UX Defect Verification", () => {
  test("Verifies zero unexpected runtime pageerrors across core workflow navigation", async ({ page }) => {
    const pageErrors: string[] = [];
    const consoleErrors: string[] = [];
    page.on("pageerror", (err) => pageErrors.push(err.message));
    page.on("console", (msg) => {
      if (msg.type() === "error") consoleErrors.push(msg.text());
    });

    await installMockSalrynApi(page, { persona: "admin", u1Entitled: true });
    await page.goto("/");

    const navLabels = [
      "POS Checkout",
      "Sales & Receipts",
      "Utang Ledger",
      "Dashboard",
      "Reports",
      "Product Catalog",
      "Stock Movements",
      "Users",
      "Audit",
      "Store Settings",
      "License",
      "Backup & Restore",
      "LAN Terminals · Future Feature",
    ];

    for (const label of navLabels) {
      await sidebarNavButton(page, label).click();
      await expect(page.locator("main h1").first()).toBeVisible();
    }

    if (consoleErrors.length > 0) {
      test.info().annotations.push({ type: "console-errors", description: consoleErrors.join(" | ") });
    }
    expect(pageErrors).toEqual([]);
  });

  test("Checkout barcode scan Enter key adds matching item and F9 opens Review Checkout modal", async ({ page }) => {
    await installMockSalrynApi(page, { persona: "admin", hasOpenShift: true, u1Entitled: true });
    await page.goto("/");
    await sidebarNavButton(page, "POS Checkout").click();

    const searchInput = page.getByPlaceholder("Scan barcode or search product name...");
    await searchInput.fill("4800011001015");
    await searchInput.press("Enter");

    // Cart should now contain Sinandomeng Premium Rice 5kg (PHP 285.00)
    await expect(page.getByText("PHP 285.00").first()).toBeVisible();

    await page.getByRole("button", { name: "500", exact: true }).click();
    await page.keyboard.press("F9");

    await expect(page.getByRole("heading", { name: "Review Checkout" })).toBeVisible();
    await page.keyboard.press("Escape");
    await expect(page.getByRole("heading", { name: "Review Checkout" })).toHaveCount(0);
  });

  test("KNOWN-DEFECT UX-004: ProductsPage custom Dialog does not close on Escape key", async ({ page }) => {
    knownDefect("UX-004", "Custom ProductsPage Dialog ignores Escape. Assertion pins CURRENT behaviour; fix belongs to #20.");
    await installMockSalrynApi(page, { persona: "admin" });
    await page.goto("/");
    await sidebarNavButton(page, "Product Catalog").click();

    await page.getByRole("button", { name: "Create Product" }).click();
    const dialogHeading = page.getByRole("heading", { name: "Create Product" });
    await expect(dialogHeading).toBeVisible();

    // Press Escape - local custom Dialog in ProductsPage.tsx lacks Escape listener
    await page.keyboard.press("Escape");
    await expect(dialogHeading).toBeVisible();
  });

  test("KNOWN-DEFECT UX-005: ProductsPage 'View Movements' button shows inline banner instead of navigating", async ({
    page,
  }) => {
    await installMockSalrynApi(page, { persona: "admin" });
    await page.goto("/");
    knownDefect("UX-005", "View Movements stays on Product Catalog. Assertion pins CURRENT behaviour; fix belongs to #20.");
    await sidebarNavButton(page, "Product Catalog").click();

    await page.getByText("Sinandomeng Premium Rice 5kg").first().click();
    await expect(page.getByRole("button", { name: "View Movements" })).toBeVisible();
    await page.getByRole("button", { name: "View Movements" }).click();

    // Remains on ProductsPage instead of navigating to Stock Movements
    await expect(page.getByRole("heading", { name: "Product Catalog" })).toBeVisible();
    await expect(page.getByText(/Stock Movements/i).first()).toBeVisible();
  });

  test("KNOWN-DEFECT UX-008: Sidebar navigation does not push browser history entries, breaking Back button", async ({
    page,
  }) => {
    await installMockSalrynApi(page, { persona: "admin" });
    await page.goto("/");
    await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();

    knownDefect("UX-008", "Sidebar navigation does not push history. Assertion pins CURRENT behaviour; fix belongs to #20.");
    const initialHistoryLength = await page.evaluate(() => window.history.length);
    await sidebarNavButton(page, "Product Catalog").click();
    await expect(page.getByRole("heading", { name: "Product Catalog" })).toBeVisible();

    const afterNavHistoryLength = await page.evaluate(() => window.history.length);
    expect(afterNavHistoryLength).toBe(initialHistoryLength);
  });

  test("OwnerReportsPage drilldown to Sales & Receipts populates URL query parameters", async ({ page }) => {
    await installMockSalrynApi(page, { persona: "admin", u1Entitled: true });
    await page.goto("/");
    await sidebarNavButton(page, "Reports").click();

    await page.getByRole("button", { name: /2026-04-19/i }).first().click();
    await expect(page.getByRole("heading", { name: "Sales & Receipts" })).toBeVisible();
    expect(page.url()).toContain("salesFrom=2026-04-19");
  });
});
