import { expect, test, type Page } from "@playwright/test";
import { installMockSalrynApi } from "./fixtures/mockApi";

function sidebarNavButton(page: Page, label: string) {
  return page.locator("aside").getByRole("button", { name: label, exact: true });
}

test.describe("MOCKED_UI Keyboard, Navigation, Accessibility & UX Defect Verification", () => {
  test("Verifies zero unexpected runtime pageerrors across core workflow navigation", async ({ page }) => {
    // This deliberately traverses every primary module. A cold Windows/Vite run
    // can exceed the suite default even when the final route has rendered.
    test.setTimeout(60_000);
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

  test("UX-004 FIXED: ProductsPage Dialog closes cleanly on Escape key", async ({ page }) => {
    await installMockSalrynApi(page, { persona: "admin" });
    await page.goto("/");
    await sidebarNavButton(page, "Product Catalog").click();

    await page.getByRole("button", { name: "Create Product" }).click();
    const dialogHeading = page.getByRole("heading", { name: "Create Product" });
    await expect(dialogHeading).toBeVisible();

    // Press Escape - shared accessible Dialog closes modal
    await page.keyboard.press("Escape");
    await expect(dialogHeading).toHaveCount(0);
  });

  test("UX-005 FIXED: ProductsPage 'View Movements' button navigates directly to Stock Movements filtered by product", async ({
    page,
  }) => {
    await installMockSalrynApi(page, { persona: "admin" });
    await page.goto("/");
    await sidebarNavButton(page, "Product Catalog").click();

    await page.getByText("Sinandomeng Premium Rice 5kg").first().click();
    await expect(page.getByRole("button", { name: "View Movements" })).toBeVisible();
    await page.getByRole("button", { name: "View Movements" }).click();

    // Navigates directly to Stock Movements filtered by selected product
    await expect(page.getByRole("heading", { name: "Stock Movements" })).toBeVisible();
    await expect(page.getByText(/Filtered: Sinandomeng Premium Rice 5kg/i)).toBeVisible();
    expect(page.url()).toContain("page=inventory");
  });

  test("UX-008 FIXED: Sidebar navigation synchronizes URL query parameter and supports browser Back navigation", async ({
    page,
  }) => {
    await installMockSalrynApi(page, { persona: "admin" });
    await page.goto("/");
    await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();

    await sidebarNavButton(page, "Product Catalog").click();
    await expect(page.getByRole("heading", { name: "Product Catalog" })).toBeVisible();
    expect(page.url()).toContain("page=products");

    await page.goBack();
    await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
  });

  test("OwnerReportsPage drilldown to Sales & Receipts populates URL query parameters", async ({ page }) => {
    await installMockSalrynApi(page, { persona: "admin", u1Entitled: true });
    await page.goto("/");
    await sidebarNavButton(page, "Reports").click();

    await page.getByRole("button", { name: /2026-04-19/i }).first().click();
    await expect(page.getByRole("heading", { name: "Sales & Receipts" })).toBeVisible();
    expect(page.url()).toContain("salesFrom=2026-04-19");
  });

  test("UX-001 & UX-002 FIXED: Utang mode disables Cash tendered and never shows false 'Insufficient cash' warning", async ({
    page,
  }) => {
    await installMockSalrynApi(page, { persona: "admin", hasOpenShift: true, u1Entitled: true });
    await page.goto("/");
    await sidebarNavButton(page, "POS Checkout").click();

    await page.getByRole("button", { name: "Add" }).first().click();
    await expect(page.getByText("PHP 285.00").first()).toBeVisible();

    const paymentSelect = page.getByLabel("Sale payment");
    await paymentSelect.selectOption("Utang");

    // In Utang mode with 0 cash tendered, Cash tendered is disabled and no false 'Insufficient cash' warning is shown
    await expect(page.getByLabel("Cash tendered")).toBeDisabled();
    await expect(page.getByText("Insufficient cash")).toHaveCount(0);
    await expect(page.getByText("Utang credit balance")).toBeVisible();
  });

  test("UX-010 FIXED: Backup verification completes inline without blocking modal OK popup", async ({ page }) => {
    await installMockSalrynApi(page, { persona: "admin" });
    await page.goto("/");
    await sidebarNavButton(page, "Backup & Restore").click();

    await page.getByRole("button", { name: "Verify Backup" }).first().click();
    await expect(page.getByText(/Backup verified: salryn-backup-20260419-020000/i)).toBeVisible();
    // Restore Backup button is immediately enabled without needing to dismiss a blocking OK dialog first
    await expect(page.getByRole("button", { name: "Restore Backup" }).first()).toBeEnabled();
  });
});
