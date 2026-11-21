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
      "Accounts Receivable",
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

    const salesRequest = page.waitForRequest((request) => request.url().includes("/api/sales/history"));
    await page.getByRole("button", { name: /2026-04-19/i }).first().click();
    await expect(page.getByRole("heading", { name: "Sales & Receipts" })).toBeVisible();
    expect(page.url()).toContain("salesFrom=2026-04-19");
    const requestUrl = new URL((await salesRequest).url());
    expect(requestUrl.searchParams.get("from")).toBe("2026-04-19T00:00:00+08:00");
    expect(requestUrl.searchParams.get("to")).toBe("2026-04-19T23:59:59.9999999+08:00");
  });

  test("UX-001 & UX-002 FIXED: Utang mode hides Cash tendered and never shows false 'Insufficient cash' warning", async ({
    page,
  }) => {
    await installMockSalrynApi(page, { persona: "admin", hasOpenShift: true, u1Entitled: true });
    await page.goto("/");
    await sidebarNavButton(page, "POS Checkout").click();

    await page.getByRole("button", { name: "Add" }).first().click();
    await expect(page.getByText("PHP 285.00").first()).toBeVisible();

    const paymentSelect = page.getByLabel("Payment method");
    await paymentSelect.selectOption("Utang");

    // In Utang mode, Cash tendered is absent and no false 'Insufficient cash' warning is shown
    await expect(page.getByLabel("Cash tendered")).toHaveCount(0);
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

  test("Dialog accessibility: sets background inert containment, traps Tab/Shift+Tab focus, and restores origin focus on close", async ({
    page,
  }) => {
    await installMockSalrynApi(page, { persona: "admin" });
    await page.goto("/");
    await sidebarNavButton(page, "Product Catalog").click();

    const triggerBtn = page.getByRole("button", { name: "Create Product" });
    await triggerBtn.click();

    // Verify background navigation and shell are effectively contained by an
    // inert ancestor (the portal implementation may inert the shared app root).
    for (const background of [
      page.getByRole("complementary", { name: "Primary navigation" }),
      page.locator("header"),
    ]) {
      await expect.poll(() => background.evaluate((node) => Boolean(node.closest("[inert]")))).toBe(true);
    }

    const dialog = page.getByRole("dialog", { name: "Create Product" });
    await expect(dialog).toBeVisible();

    // Verify focus is trapped inside dialog
    const activeIsInside = await dialog.evaluate((el) => el.contains(document.activeElement));
    expect(activeIsInside).toBe(true);

    // Tab through and verify containment
    await page.keyboard.press("Tab");
    const activeAfterTab = await dialog.evaluate((el) => el.contains(document.activeElement));
    expect(activeAfterTab).toBe(true);

    await page.keyboard.press("Shift+Tab");
    const activeAfterShiftTab = await dialog.evaluate((el) => el.contains(document.activeElement));
    expect(activeAfterShiftTab).toBe(true);

    // Close dialog and verify inert is removed and focus is restored
    await page.keyboard.press("Escape");
    await expect(dialog).toHaveCount(0);
    for (const background of [
      page.getByRole("complementary", { name: "Primary navigation" }),
      page.locator("header"),
    ]) {
      await expect.poll(() => background.evaluate((node) => Boolean(node.closest("[inert]")))).toBe(false);
    }
    await expect(triggerBtn).toBeFocused();
  });

  test("OwnerReports deep link navigation persists on reload and supports browser Back", async ({ page }) => {
    await installMockSalrynApi(page, { persona: "admin", u1Entitled: true });
    await page.goto("/?page=owner-reports");

    // Click sales trend drilldown
    await page.getByRole("button", { name: /2026-04-19/i }).first().click();
    await expect(page).toHaveURL(/page=sales.*salesFrom=2026-04-19/);
    await expect(page.getByRole("heading", { name: "Sales & Receipts" })).toBeVisible();

    // Reload page - state must remain preserved
    await page.reload();
    await expect(page.getByRole("heading", { name: "Sales & Receipts" })).toBeVisible();

    // Browser back returns to owner-reports
    await page.goBack();
    await expect(page.getByRole("heading", { name: "Reports", exact: true })).toBeVisible();
    await expect(page).toHaveURL(/page=owner-reports/);
  });

  test("mobile Reports content remains usable after the navigation drawer is closed", async ({ page }, testInfo) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await installMockSalrynApi(page, { persona: "admin", u1Entitled: true });
    await page.goto("/?page=owner-reports");
    await expect(page.getByRole("heading", { name: "Reports", exact: true })).toBeVisible();
    await expect(page.getByText("Sales trend", { exact: true })).toBeVisible();
    await expect(page.locator("aside")).toBeHidden();
    await page.screenshot({ path: testInfo.outputPath("mobile-reports-drawer-closed.png"), fullPage: true });
  });

  test("nested dialogs keep the application inert until the last dialog closes", async ({ page }) => {
    await installMockSalrynApi(page, { persona: "admin" });
    await page.goto("/");
    await page.evaluate(async () => {
      const reactModule = await import("/node_modules/.vite/deps/react.js");
      const React = reactModule.default ?? reactModule;
      const reactDomClient = await import("/node_modules/.vite/deps/react-dom_client.js");
      const createRoot = reactDomClient.createRoot ?? reactDomClient.default.createRoot;
      const { Dialog } = await import("/src/components/ui/Dialog.tsx");
      const mount = document.createElement("div");
      mount.id = "nested-dialog-regression-root";
      document.body.appendChild(mount);
      function Harness() {
        const [outerOpen, setOuterOpen] = React.useState(true);
        const [innerOpen, setInnerOpen] = React.useState(false);
        return React.createElement(
          Dialog,
          {
            open: outerOpen,
            title: "Outer regression dialog",
            onClose: () => setOuterOpen(false),
          },
          React.createElement("button", { type: "button", onClick: () => setInnerOpen(true) }, "Open nested dialog"),
          React.createElement(
            Dialog,
            {
              open: innerOpen,
              title: "Inner regression dialog",
              onClose: () => setInnerOpen(false),
            },
            React.createElement("p", null, "Nested content"),
          ),
        );
      }
      createRoot(mount).render(React.createElement(Harness));
    });

    const outer = page.getByRole("dialog", { name: "Outer regression dialog" });
    await expect(outer).toBeVisible();
    await outer.getByRole("button", { name: "Open nested dialog" }).click();
    const inner = page.getByRole("dialog", { name: "Inner regression dialog" });
    await expect(inner).toBeVisible();
    await expect(outer.locator("..")).toHaveAttribute("inert", "");
    await expect(page.locator("#root")).toHaveAttribute("inert", "");

    await page.keyboard.press("Escape");
    await expect(inner).toBeHidden();
    await expect(outer).toBeVisible();
    await expect(outer.locator("..")).not.toHaveAttribute("inert", "");
    await expect(page.locator("#root")).toHaveAttribute("inert", "");

    await page.keyboard.press("Escape");
    await expect(outer).toBeHidden();
    await expect(page.locator("#root")).not.toHaveAttribute("inert", "");
    await expect.poll(() => page.evaluate(() => document.body.style.overflow)).toBe("");
  });
});
