import { expect, test, type Page } from "@playwright/test";
import { installMockSalrynApi } from "./fixtures/mockApi";

/**
 * MOCKED_UI visual regression gate (A0-R1).
 *
 * Every capture is a genuine `toHaveScreenshot` comparison against a reviewed, committed expected image at
 * `e2e/__screenshots__/<name>.png` (see `snapshotPathTemplate` in playwright.config.ts). Rendering drift FAILS
 * the test. Ordinary runs never write expected images (`updateSnapshots: "none"`); only
 * `npm run test:visual:update` may rewrite them after human review.
 */

/** Fixed wall clock used for every visual test: Sunday 19 April 2026, 10:30 Asia/Manila. */
const VISUAL_FIXED_CLOCK = new Date("2026-04-19T10:30:00+08:00");

/** Deterministic synthetic terminal machine code (replaces crypto.getRandomValues output in captures). */
const SYNTHETIC_TERMINAL_MACHINE_CODE = "SALRYN-MC-1111-2222-3333-4444";

async function expectBaseline(page: Page, snapshotName: string, fullPage = true) {
  await expect(page).toHaveScreenshot(snapshotName, { fullPage });
}

function sidebarNavButton(page: Page, label: string) {
  return page.locator("aside").getByRole("button", { name: label, exact: true });
}

test.beforeEach(async ({ page }) => {
  // Freeze Date.now()/new Date() so period presets, "generated at" stamps and default ranges are stable.
  // Timers still run normally, so React Query and UI behaviour are unaffected.
  await page.clock.setFixedTime(VISUAL_FIXED_CLOCK);
  await page.addInitScript((machineCode) => {
    if (!window.localStorage.getItem("salryn.terminalMachineCode.v1")) {
      window.localStorage.setItem("salryn.terminalMachineCode.v1", machineCode);
    }
  }, SYNTHETIC_TERMINAL_MACHINE_CODE);
});

test.describe("MOCKED_UI Visual Baseline & Screen Inventory @visual", () => {
  test("01 - Startup error gate (Host API unavailable)", async ({ page }) => {
    await installMockSalrynApi(page, { startupUnavailable: true });
    await page.goto("/");
    await expect(page.getByRole("heading", { name: "Salryn POS unavailable" })).toBeVisible();
    await expectBaseline(page, "01-startup-unavailable.png");
  });

  test("02 & 03 - Unlicensed Host gate and Activate License wizard modal", async ({ page }) => {
    await installMockSalrynApi(page, { licenseReady: false, licenseStatusOverride: "Unlicensed" });
    await page.goto("/");
    await expect(page.getByRole("heading", { name: "Activate Salryn" })).toBeVisible();
    await expectBaseline(page, "02-license-unlicensed.png");

    await page.getByRole("button", { name: "Import license" }).first().click();
    await expect(page.getByRole("heading", { name: "Terms & Conditions" }).first()).toBeVisible();
    await expectBaseline(page, "03-license-wizard-modal.png");
  });

  test("04 - First-run Bootstrap Setup page", async ({ page }) => {
    await installMockSalrynApi(page, { licenseReady: true, firstRunRequired: true });
    await page.goto("/");
    await expect(page.getByRole("heading", { name: "Create the first store admin" })).toBeVisible();
    await expectBaseline(page, "04-bootstrap-setup.png");
  });

  test("05 & 06 - Login page (idle and invalid credentials error)", async ({ page }) => {
    await installMockSalrynApi(page, { licenseReady: true, persona: "unauthenticated" });
    await page.goto("/");
    await expect(page.getByRole("heading", { name: "Sign in to Salryn POS" })).toBeVisible();
    await expectBaseline(page, "05-login-page.png");

    await page.getByLabel("Username").fill("owner.admin");
    await page.locator("#login-password").fill("wrong-password");
    await page.getByRole("button", { name: /Sign in/i }).click();
    await expect(page.getByText("Invalid username or password.")).toBeVisible();
    await expectBaseline(page, "06-login-error.png");
  });

  test("07 & 08 - Dashboard page (healthy vs operational warnings)", async ({ page }) => {
    await installMockSalrynApi(page, { persona: "admin", dashboardVariant: "healthy" });
    await page.goto("/");
    await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
    await expect(page.getByText("All reads healthy")).toBeVisible();
    await expectBaseline(page, "07-dashboard-healthy.png");

    await installMockSalrynApi(page, { persona: "admin", dashboardVariant: "warnings" });
    await page.reload();
    await expect(page.getByText("Read warnings present")).toBeVisible();
    await expectBaseline(page, "08-dashboard-warnings.png");
  });

  test("09 to 13 - Checkout workstation states (closed shift, Cash cart, Utang mode, Review modal, Fast Add modal)", async ({
    page,
  }) => {
    // 09: Closed shift
    await installMockSalrynApi(page, { persona: "admin", hasOpenShift: false, u1Entitled: true });
    await page.goto("/");
    await sidebarNavButton(page, "POS Checkout").click();
    await expect(page.getByRole("heading", { name: "POS Checkout" })).toBeVisible();
    await expect(page.getByText("Cashier workstation locked")).toBeVisible();
    await expectBaseline(page, "09-checkout-shift-closed.png");

    // Open shift -> 10: Cash cart
    await page.getByRole("button", { name: "Open Shift" }).click();
    await expect(page.getByRole("button", { name: "Add" }).first()).toBeEnabled();
    await page.getByRole("button", { name: "Add" }).first().click();
    await page.getByRole("button", { name: "Add" }).nth(2).click();
    await page.getByRole("button", { name: "1000", exact: true }).click();
    await expectBaseline(page, "10-checkout-cash-cart.png");

    // 11: Utang payment mode
    const paymentSelect = page.locator("select").first();
    await paymentSelect.selectOption("Utang");
    await expect(page.getByText("Balance due")).toBeVisible();
    await expectBaseline(page, "11-checkout-utang-mode.png");

    // Switch back to Cash for 12: Review Checkout modal
    await paymentSelect.selectOption("Cash");
    await page.getByRole("button", { name: "1000", exact: true }).click();
    await page.getByRole("button", { name: "Review Checkout" }).click();
    await expect(page.getByRole("heading", { name: "Review Checkout" })).toBeVisible();
    await expectBaseline(page, "12-checkout-review-modal.png");
    await page.getByRole("button", { name: "Cancel" }).click();
    const shiftToastDismiss = page.getByRole("button", { name: "Dismiss Shift opened" });
    if (await shiftToastDismiss.isVisible()) {
      await shiftToastDismiss.click();
    }

    // 13: Fast Add Unknown Barcode modal (triggered by scanning unknown barcode + Enter)
    const scanInput = page.getByPlaceholder("Scan barcode or search product name...");
    await scanInput.fill("9999999999999");
    await scanInput.press("Enter");
    await expect(page.getByRole("heading", { name: "Fast Add Unknown Barcode" })).toBeVisible();
    await expectBaseline(page, "13-checkout-fast-add-modal.png");
  });

  test("14 & 15 - Sales & Receipts list and Sale Detail / Receipt / Void modal", async ({ page }) => {
    await installMockSalrynApi(page, { persona: "admin" });
    await page.goto("/");
    await sidebarNavButton(page, "Sales & Receipts").click();
    await expect(page.getByRole("heading", { name: "Sales & Receipts" })).toBeVisible();
    await expect(page.getByText("OR-20260419-0024")).toBeVisible();
    await expectBaseline(page, "14-sales-history-list.png");

    await page.getByRole("button", { name: "Sale & Receipt Details" }).first().click();
    await expect(page.getByRole("heading", { name: "Receipt OR-20260419-0024" })).toBeVisible();
    await expect(page.getByText("BALITE NEIGHBORHOOD MART").first()).toBeVisible();
    await expectBaseline(page, "15-sales-receipt-detail-modal.png");
  });

  test("16 - Utang Ledger (Accounts Receivable summary, customer list, customer ledger & collection)", async ({
    page,
  }) => {
    await installMockSalrynApi(page, { persona: "admin", u1Entitled: true });
    await page.goto("/");
    await sidebarNavButton(page, "Accounts Receivable").click();
    await expect(page.getByRole("heading", { name: "Utang Ledger" })).toBeVisible();
    await expect(page.getByText("Maria Clara Dela Cruz")).toBeVisible();
    await page.getByText("Maria Clara Dela Cruz").click();
    await expect(page.getByText("OR-20260418-0018 Weekly rice & oil stock utang")).toBeVisible();
    await expectBaseline(page, "16-receivables-utang-ledger.png");
  });

  test("17 & 18 - Owner Reports & Financial Analytics (with U1 AR aging vs non-entitled 403)", async ({ page }) => {
    await installMockSalrynApi(page, { persona: "admin", u1Entitled: true });
    await page.goto("/");
    await sidebarNavButton(page, "Reports").click();
    await expect(page.getByRole("heading", { name: "Reports", exact: true })).toBeVisible();
    await expect(page.getByText("Accounts Receivable aging")).toBeVisible();
    await expectBaseline(page, "17-owner-reports-analytics.png");

    await installMockSalrynApi(page, { persona: "admin", u1Entitled: false, arAgingForbidden: true });
    await page.reload();
    await sidebarNavButton(page, "Reports").click();
    await expect(page.getByText("AR analytics unavailable")).toBeVisible();
    await expectBaseline(page, "18-owner-reports-no-u1.png");
  });

  test("19 & 20 - Products catalog, detail card, and Create Product modal", async ({ page }) => {
    await installMockSalrynApi(page, { persona: "admin" });
    await page.goto("/");
    await sidebarNavButton(page, "Product Catalog").click();
    await expect(page.getByRole("heading", { name: "Product Catalog" })).toBeVisible();
    await expect(page.getByText("Sinandomeng Premium Rice 5kg").first()).toBeVisible();
    await expectBaseline(page, "19-products-catalog-detail.png");

    await page.getByRole("button", { name: "Create Product" }).click();
    await expect(page.getByRole("heading", { name: "Create Product" })).toBeVisible();
    await expectBaseline(page, "20-products-create-modal.png");
  });

  test("21 & 22 - Stock Movements page and Record Stock-In modal", async ({ page }) => {
    await installMockSalrynApi(page, { persona: "admin" });
    await page.goto("/");
    await sidebarNavButton(page, "Stock Movements").click();
    await expect(page.getByRole("heading", { name: "Stock Movements" })).toBeVisible();
    await expect(page.getByText("Weekly supplier delivery DR#8841")).toBeVisible();
    await expectBaseline(page, "21-inventory-movements.png");

    await page.getByRole("button", { name: "Stock-In" }).first().click();
    await expect(page.getByRole("heading", { name: "Stock-In" })).toBeVisible();
    await expectBaseline(page, "22-inventory-stockin-modal.png");
  });

  test("23 to 28 - Admin & Governance screens (Users, Audit, Store Settings, Backup/Restore, LAN Terminals, License)", async ({
    page,
  }) => {
    await installMockSalrynApi(page, { persona: "admin" });
    await page.goto("/");

    // 23: Users
    await sidebarNavButton(page, "Users").click();
    await expect(page.getByRole("heading", { name: "Users" })).toBeVisible();
    await expect(page.getByText("Kirch Balite (Store Owner)").first()).toBeVisible();
    await expectBaseline(page, "23-users-roles-permissions.png");

    // 24: Audit
    await sidebarNavButton(page, "Audit").click();
    await expect(page.getByRole("heading", { name: "Audit" })).toBeVisible();
    await expect(page.getByText("Sale.Completed")).toBeVisible();
    await expectBaseline(page, "24-audit-logs.png");

    // 25: Store Settings
    await sidebarNavButton(page, "Store Settings").click();
    await expect(page.getByRole("heading", { name: "Store Settings" })).toBeVisible();
    await expectBaseline(page, "25-store-settings.png");

    // 26: Backup & Restore
    await sidebarNavButton(page, "Backup & Restore").click();
    await expect(page.getByRole("heading", { name: "Backup & Restore" })).toBeVisible();
    await page.getByRole("button", { name: "Verify" }).first().click();
    await expect(page.getByText(/Backup verified: salryn-backup-20260419-020000/i)).toBeVisible();
    await expectBaseline(page, "26-backup-restore-verified.png");

    // 27: LAN Terminals · Future Feature
    await sidebarNavButton(page, "LAN Terminals · Future Feature").click();
    await expect(page.getByRole("heading", { name: /LAN Terminals/i })).toBeVisible();
    await expectBaseline(page, "27-lan-terminals-future-feature.png");

    // 28: License inside AppShell
    await sidebarNavButton(page, "License").click();
    await expect(page.getByRole("heading", { name: "Salryn is activated" })).toBeVisible();
    await expectBaseline(page, "28-license-authenticated.png");
  });

  test("29 to 31 - Persona & Entitlement navigation matrices (Cashier, Manager, Non-U1 Admin)", async ({ page }) => {
    // 29: Cashier persona auto-redirects from Dashboard to POS Checkout and sees only 2 nav items
    await installMockSalrynApi(page, { persona: "cashier", u1Entitled: true });
    await page.goto("/");
    await expect(page.getByRole("heading", { name: "POS Checkout" })).toBeVisible();
    await expectBaseline(page, "29-persona-cashier-shell.png");

    // 30: Manager persona sees Cashier + Manager items, no Admin config items
    await installMockSalrynApi(page, { persona: "manager", u1Entitled: true });
    await page.goto("/?page=dashboard");
    await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
    await expectBaseline(page, "30-persona-manager-shell.png");

    // 31: Admin without U1 entitlement does not see Accounts Receivable in sidebar
    await installMockSalrynApi(page, { persona: "admin", u1Entitled: false });
    await page.goto("/?page=dashboard");
    await expect(sidebarNavButton(page, "Accounts Receivable")).toHaveCount(0);
    await expectBaseline(page, "31-persona-no-u1-shell.png");
  });

  test("32 to 37 - Responsive viewport matrix and 125% / 150% scale stress tests", async ({ page }) => {
    await installMockSalrynApi(page, { persona: "admin", u1Entitled: true });

    // 32: 1024x768 Checkout
    await page.setViewportSize({ width: 1024, height: 768 });
    await page.goto("/");
    await sidebarNavButton(page, "POS Checkout").click();
    await expect(page.getByRole("heading", { name: "POS Checkout" })).toBeVisible();
    await expectBaseline(page, "32-responsive-1024x768-checkout.png");

    // 33: 1280x720 Dashboard
    await page.setViewportSize({ width: 1280, height: 720 });
    await sidebarNavButton(page, "Dashboard").click();
    await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
    await expectBaseline(page, "33-responsive-1280x720-dashboard.png");

    // 34: 1920x1080 Owner Reports
    await page.setViewportSize({ width: 1920, height: 1080 });
    await sidebarNavButton(page, "Reports").click();
    await expect(page.getByRole("heading", { name: "Reports", exact: true })).toBeVisible();
    await expectBaseline(page, "34-responsive-1920x1080-owner-reports.png");

    // 35: 390x844 Mobile Owner Reports (collapsed sidebar hides cleanly on < 768px; header toggle opens slide-over nav)
    await page.setViewportSize({ width: 390, height: 844 });
    await expectBaseline(page, "35-responsive-390x844-mobile-owner-reports.png");

    // 36 & 37: 1366x768 at 125% and 150% zoom on Checkout
    await page.setViewportSize({ width: 1366, height: 768 });
    await sidebarNavButton(page, "POS Checkout").click();
    await page.evaluate(() => {
      document.documentElement.style.zoom = "1.25";
    });
    await expectBaseline(page, "36-scale-125pct-checkout.png");

    await page.evaluate(() => {
      document.documentElement.style.zoom = "1.5";
    });
    await expectBaseline(page, "37-scale-150pct-checkout.png");
  });

  test("38, 39 & 43 - Internal / Diagnostic Host surfaces (OfficialSetupPage, TraditionalReportingPage & ProductionReportsPage)", async ({
    page,
  }) => {
    await installMockSalrynApi(page, { persona: "admin", u1Entitled: true });
    await page.goto("/");
    await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();

    // 38: Navigate to OfficialSetupPage via synthetic popstate (since it has no sidebar link)
    await page.evaluate(() => {
      window.history.pushState({ page: "official-setup" }, "", "/?page=official-setup");
      window.dispatchEvent(new PopStateEvent("popstate", { state: { page: "official-setup" } }));
    });
    await expect(page.getByRole("heading", { name: "Official Setup" })).toBeVisible();
    await expectBaseline(page, "38-internal-official-setup.png");

    // 39: Navigate to TraditionalReportingPage via synthetic popstate
    await page.evaluate(() => {
      window.history.pushState({ page: "traditional-reporting" }, "", "/?page=traditional-reporting");
      window.dispatchEvent(new PopStateEvent("popstate", { state: { page: "traditional-reporting" } }));
    });
    await expect(page.getByRole("heading", { name: "Internal Compliance Reporting Lab" })).toBeVisible();
    await expectBaseline(page, "39-internal-traditional-reporting.png");

    // 43: Navigate to ProductionReportsPage via synthetic popstate
    await page.evaluate(() => {
      window.history.pushState({ page: "production-reports" }, "", "/?page=production-reports");
      window.dispatchEvent(new PopStateEvent("popstate", { state: { page: "production-reports" } }));
    });
    await expect(page.getByRole("heading", { name: "Report Artifacts" })).toBeVisible();
    await expectBaseline(page, "43-internal-production-reports.png");
  });

  test("40 to 42 & 44 - Terminal / Remote Client flow (TerminalActivationPage, RemoteClientLandingPage, RemotePosClientPage, RemoteReadOnlyClientPage)", async ({
    page,
  }) => {
    await installMockSalrynApi(page, { persona: "admin" });

    // 40: Unlicensed Terminal device gate on non-loopback hostname (*.localhost)
    await page.goto("http://salryn-terminal.localhost:5173/");
    await expect(page.getByRole("heading", { name: "SALRYN Terminal" })).toBeVisible();
    await expectBaseline(page, "40-terminal-activation-gate.png");

    // Seed valid synthetic terminal license in localStorage to advance to RemoteClientLandingPage
    await page.evaluate(() => {
      const machineCode =
        window.localStorage.getItem("salryn.terminalMachineCode.v1") || "SALRYN-MC-1111-2222-3333-4444";
      window.localStorage.setItem("salryn.terminalMachineCode.v1", machineCode);
      const licenseDoc = {
        LicenseId: "TERM-LIC-2026-001",
        CustomerName: "Balite Neighborhood Mart",
        ApplicationId: "Salryn.POS",
        StoreId: "STORE-QC-001",
        LicenseType: "Terminal",
        HostLicenseId: "LIC-2026-SALRYN-RETAIL-001",
        TerminalId: "TERM-COUNTER-2",
        MachineBinding: machineCode,
        Signature: "SYNTHETIC-QA-SIGNATURE",
        Features: ["LanTerminals"],
        ExpiresAt: "2027-04-19T00:00:00Z",
      };
      window.localStorage.setItem(
        "salryn.terminalLicense.v1",
        JSON.stringify({
          version: 1,
          licenseJson: JSON.stringify(licenseDoc),
          licenseId: "TERM-LIC-2026-001",
          customerName: "Balite Neighborhood Mart",
          storeId: "STORE-QC-001",
          hostLicenseId: "LIC-2026-SALRYN-RETAIL-001",
          terminalId: "TERM-COUNTER-2",
          machineCode,
          expiresAt: "2027-04-19T00:00:00Z",
          installedAt: "2026-04-19T05:00:00Z",
        }),
      );
    });

    await page.goto("http://salryn-terminal.localhost:5173/");
    await expect(page.getByRole("heading", { name: "Open Salryn on this terminal" })).toBeVisible();
    await expectBaseline(page, "41-terminal-remote-landing.png");

    // 42: Open Terminal POS pairing screen
    await page.getByRole("button", { name: /Open Salryn POS terminal/i }).click();
    await expect(page.getByRole("heading", { name: "Connect terminal to Host" })).toBeVisible();
    await expectBaseline(page, "42-terminal-remote-pos-pairing.png");

    // 44: Return to Landing, expand support surfaces, and open Remote Read-Only client
    await page.goto("http://salryn-terminal.localhost:5173/");
    await expect(page.getByRole("heading", { name: "Open Salryn on this terminal" })).toBeVisible();
    await page.getByText("Internal support surfaces").click();
    await page.getByRole("button", { name: /Open Remote Read-Only/i }).click();
    await expect(page.getByRole("heading", { name: "Remote Read-Only Client" })).toBeVisible();
    await expectBaseline(page, "44-terminal-remote-readonly.png");
  });
});
