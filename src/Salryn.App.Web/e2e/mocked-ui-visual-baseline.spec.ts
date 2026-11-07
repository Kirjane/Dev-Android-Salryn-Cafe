import { expect, test, type Page } from "@playwright/test";
import * as fs from "node:fs";
import * as path from "node:path";
import { fileURLToPath } from "node:url";
import { installMockSalrynApi } from "./fixtures/mockApi";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const SCREENSHOT_DIR = path.resolve(__dirname, "__screenshots__");

fs.mkdirSync(SCREENSHOT_DIR, { recursive: true });

async function saveBaselineScreenshot(page: Page, fileName: string, fullPage = true) {
  const targetPath = path.join(SCREENSHOT_DIR, fileName);
  await page.screenshot({ path: targetPath, fullPage, animations: "disabled" });
  expect(fs.existsSync(targetPath)).toBe(true);
}

function sidebarNavButton(page: Page, label: string) {
  return page.locator("aside").getByRole("button", { name: label, exact: true });
}

test.describe("MOCKED_UI Visual Baseline & Screen Inventory @visual", () => {
  test("01 - Startup error gate (Host API unavailable)", async ({ page }) => {
    await installMockSalrynApi(page, { startupUnavailable: true });
    await page.goto("/");
    await expect(page.getByRole("heading", { name: "Salryn POS unavailable" })).toBeVisible();
    await saveBaselineScreenshot(page, "01-startup-unavailable.png");
  });

  test("02 & 03 - Unlicensed Host gate and Activate License wizard modal", async ({ page }) => {
    await installMockSalrynApi(page, { licenseReady: false, licenseStatusOverride: "Unlicensed" });
    await page.goto("/");
    await expect(page.getByRole("heading", { name: "Activate Salryn" })).toBeVisible();
    await saveBaselineScreenshot(page, "02-license-unlicensed.png");

    await page.getByRole("button", { name: "Import license" }).first().click();
    await expect(page.getByRole("heading", { name: "Terms & Conditions" }).first()).toBeVisible();
    await saveBaselineScreenshot(page, "03-license-wizard-modal.png");
  });

  test("04 - First-run Bootstrap Setup page", async ({ page }) => {
    await installMockSalrynApi(page, { licenseReady: true, firstRunRequired: true });
    await page.goto("/");
    await expect(page.getByRole("heading", { name: "Create the first store admin" })).toBeVisible();
    await saveBaselineScreenshot(page, "04-bootstrap-setup.png");
  });

  test("05 & 06 - Login page (idle and invalid credentials error)", async ({ page }) => {
    await installMockSalrynApi(page, { licenseReady: true, persona: "unauthenticated" });
    await page.goto("/");
    await expect(page.getByRole("heading", { name: "Sign in to Salryn POS" })).toBeVisible();
    await saveBaselineScreenshot(page, "05-login-page.png");

    await page.getByLabel("Username").fill("owner.admin");
    await page.locator("#login-password").fill("wrong-password");
    await page.getByRole("button", { name: /Sign in/i }).click();
    await expect(page.getByText("Invalid username or password.")).toBeVisible();
    await saveBaselineScreenshot(page, "06-login-error.png");
  });

  test("07 & 08 - Dashboard page (healthy vs operational warnings)", async ({ page }) => {
    await installMockSalrynApi(page, { persona: "admin", dashboardVariant: "healthy" });
    await page.goto("/");
    await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
    await expect(page.getByText("All reads healthy")).toBeVisible();
    await saveBaselineScreenshot(page, "07-dashboard-healthy.png");

    await installMockSalrynApi(page, { persona: "admin", dashboardVariant: "warnings" });
    await page.reload();
    await expect(page.getByText("Read warnings present")).toBeVisible();
    await saveBaselineScreenshot(page, "08-dashboard-warnings.png");
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
    await saveBaselineScreenshot(page, "09-checkout-shift-closed.png");

    // Open shift -> 10: Cash cart
    await page.getByRole("button", { name: "Open Shift" }).click();
    await expect(page.getByRole("button", { name: "Add" }).first()).toBeEnabled();
    await page.getByRole("button", { name: "Add" }).first().click();
    await page.getByRole("button", { name: "Add" }).nth(2).click();
    await page.getByRole("button", { name: "1000", exact: true }).click();
    await saveBaselineScreenshot(page, "10-checkout-cash-cart.png");

    // 11: Utang payment mode
    const paymentSelect = page.locator("select").first();
    await paymentSelect.selectOption("Utang");
    await expect(page.getByText("Balance due")).toBeVisible();
    await saveBaselineScreenshot(page, "11-checkout-utang-mode.png");

    // Switch back to Cash for 12: Review Checkout modal
    await paymentSelect.selectOption("Cash");
    await page.getByRole("button", { name: "1000", exact: true }).click();
    await page.getByRole("button", { name: "Review Checkout" }).click();
    await expect(page.getByRole("heading", { name: "Review Checkout" })).toBeVisible();
    await saveBaselineScreenshot(page, "12-checkout-review-modal.png");
    await page.getByRole("button", { name: "Cancel" }).click();

    // 13: Fast Add Unknown Barcode modal (triggered by scanning unknown barcode + Enter)
    const scanInput = page.getByPlaceholder("Scan barcode or search product name...");
    await scanInput.fill("9999999999999");
    await scanInput.press("Enter");
    await expect(page.getByRole("heading", { name: "Fast Add Unknown Barcode" })).toBeVisible();
    await saveBaselineScreenshot(page, "13-checkout-fast-add-modal.png");
  });

  test("14 & 15 - Sales & Receipts list and Sale Detail / Receipt / Void modal", async ({ page }) => {
    await installMockSalrynApi(page, { persona: "admin" });
    await page.goto("/");
    await sidebarNavButton(page, "Sales & Receipts").click();
    await expect(page.getByRole("heading", { name: "Sales & Receipts" })).toBeVisible();
    await expect(page.getByText("OR-20260419-0024")).toBeVisible();
    await saveBaselineScreenshot(page, "14-sales-history-list.png");

    await page.getByRole("button", { name: "Sale & Receipt Details" }).first().click();
    await expect(page.getByRole("heading", { name: "Receipt OR-20260419-0024" })).toBeVisible();
    await expect(page.getByText("BALITE NEIGHBORHOOD MART").first()).toBeVisible();
    await saveBaselineScreenshot(page, "15-sales-receipt-detail-modal.png");
  });

  test("16 - Utang Ledger (Accounts Receivable summary, customer list, customer ledger & collection)", async ({
    page,
  }) => {
    await installMockSalrynApi(page, { persona: "admin", u1Entitled: true });
    await page.goto("/");
    await sidebarNavButton(page, "Utang Ledger").click();
    await expect(page.getByRole("heading", { name: "Utang Ledger" })).toBeVisible();
    await expect(page.getByText("Maria Clara Dela Cruz")).toBeVisible();
    await page.getByText("Maria Clara Dela Cruz").click();
    await expect(page.getByText("OR-20260418-0018 Weekly rice & oil stock utang")).toBeVisible();
    await saveBaselineScreenshot(page, "16-receivables-utang-ledger.png");
  });

  test("17 & 18 - Owner Reports & Financial Analytics (with U1 AR aging vs non-entitled 403)", async ({ page }) => {
    await installMockSalrynApi(page, { persona: "admin", u1Entitled: true });
    await page.goto("/");
    await sidebarNavButton(page, "Reports").click();
    await expect(page.getByRole("heading", { name: "Reports" })).toBeVisible();
    await expect(page.getByText("Accounts Receivable aging")).toBeVisible();
    await saveBaselineScreenshot(page, "17-owner-reports-analytics.png");

    await installMockSalrynApi(page, { persona: "admin", u1Entitled: false, arAgingForbidden: true });
    await page.reload();
    await sidebarNavButton(page, "Reports").click();
    await expect(page.getByText("AR analytics unavailable")).toBeVisible();
    await saveBaselineScreenshot(page, "18-owner-reports-no-u1.png");
  });

  test("19 & 20 - Products catalog, detail card, and Create Product modal", async ({ page }) => {
    await installMockSalrynApi(page, { persona: "admin" });
    await page.goto("/");
    await sidebarNavButton(page, "Product Catalog").click();
    await expect(page.getByRole("heading", { name: "Product Catalog" })).toBeVisible();
    await expect(page.getByText("Sinandomeng Premium Rice 5kg").first()).toBeVisible();
    await saveBaselineScreenshot(page, "19-products-catalog-detail.png");

    await page.getByRole("button", { name: "Create Product" }).click();
    await expect(page.getByRole("heading", { name: "Create Product" })).toBeVisible();
    await saveBaselineScreenshot(page, "20-products-create-modal.png");
  });

  test("21 & 22 - Stock Movements page and Record Stock-In modal", async ({ page }) => {
    await installMockSalrynApi(page, { persona: "admin" });
    await page.goto("/");
    await sidebarNavButton(page, "Stock Movements").click();
    await expect(page.getByRole("heading", { name: "Stock Movements" })).toBeVisible();
    await expect(page.getByText("Weekly supplier delivery DR#8841")).toBeVisible();
    await saveBaselineScreenshot(page, "21-inventory-movements.png");

    await page.getByRole("button", { name: "Stock-In" }).first().click();
    await expect(page.getByRole("heading", { name: "Stock-In" })).toBeVisible();
    await saveBaselineScreenshot(page, "22-inventory-stockin-modal.png");
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
    await saveBaselineScreenshot(page, "23-users-roles-permissions.png");

    // 24: Audit
    await sidebarNavButton(page, "Audit").click();
    await expect(page.getByRole("heading", { name: "Audit" })).toBeVisible();
    await expect(page.getByText("Sale.Completed")).toBeVisible();
    await saveBaselineScreenshot(page, "24-audit-logs.png");

    // 25: Store Settings
    await sidebarNavButton(page, "Store Settings").click();
    await expect(page.getByRole("heading", { name: "Store Settings" })).toBeVisible();
    await saveBaselineScreenshot(page, "25-store-settings.png");

    // 26: Backup & Restore
    await sidebarNavButton(page, "Backup & Restore").click();
    await expect(page.getByRole("heading", { name: "Backup & Restore" })).toBeVisible();
    await page.getByRole("button", { name: "Verify" }).first().click();
    await expect(page.getByRole("heading", { name: "Backup verified" })).toBeVisible();
    await saveBaselineScreenshot(page, "26-backup-restore-verified.png");
    await page.getByRole("button", { name: "OK" }).click();

    // 27: LAN Terminals · Future Feature
    await sidebarNavButton(page, "LAN Terminals · Future Feature").click();
    await expect(page.getByRole("heading", { name: /LAN Terminals/i })).toBeVisible();
    await saveBaselineScreenshot(page, "27-lan-terminals-future-feature.png");

    // 28: License inside AppShell
    await sidebarNavButton(page, "License").click();
    await expect(page.getByRole("heading", { name: "Salryn is activated" })).toBeVisible();
    await saveBaselineScreenshot(page, "28-license-authenticated.png");
  });

  test("29 to 31 - Persona & Entitlement navigation matrices (Cashier, Manager, Non-U1 Admin)", async ({ page }) => {
    // 29: Cashier persona auto-redirects from Dashboard to POS Checkout and sees only 2 nav items
    await installMockSalrynApi(page, { persona: "cashier", u1Entitled: true });
    await page.goto("/");
    await expect(page.getByRole("heading", { name: "POS Checkout" })).toBeVisible();
    await saveBaselineScreenshot(page, "29-persona-cashier-shell.png");

    // 30: Manager persona sees Cashier + Manager items, no Admin config items
    await installMockSalrynApi(page, { persona: "manager", u1Entitled: true });
    await page.reload();
    await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
    await saveBaselineScreenshot(page, "30-persona-manager-shell.png");

    // 31: Admin without U1 entitlement does not see Utang Ledger in sidebar
    await installMockSalrynApi(page, { persona: "admin", u1Entitled: false });
    await page.reload();
    await expect(sidebarNavButton(page, "Utang Ledger")).toHaveCount(0);
    await saveBaselineScreenshot(page, "31-persona-no-u1-shell.png");
  });

  test("32 to 37 - Responsive viewport matrix and 125% / 150% scale stress tests", async ({ page }) => {
    await installMockSalrynApi(page, { persona: "admin", u1Entitled: true });

    // 32: 1024x768 Checkout
    await page.setViewportSize({ width: 1024, height: 768 });
    await page.goto("/");
    await sidebarNavButton(page, "POS Checkout").click();
    await expect(page.getByRole("heading", { name: "POS Checkout" })).toBeVisible();
    await saveBaselineScreenshot(page, "32-responsive-1024x768-checkout.png");

    // 33: 1280x720 Dashboard
    await page.setViewportSize({ width: 1280, height: 720 });
    await sidebarNavButton(page, "Dashboard").click();
    await expect(page.getByRole("heading", { name: "Dashboard" })).toBeVisible();
    await saveBaselineScreenshot(page, "33-responsive-1280x720-dashboard.png");

    // 34: 1920x1080 Owner Reports
    await page.setViewportSize({ width: 1920, height: 1080 });
    await sidebarNavButton(page, "Reports").click();
    await expect(page.getByRole("heading", { name: "Reports" })).toBeVisible();
    await saveBaselineScreenshot(page, "34-responsive-1920x1080-owner-reports.png");

    // 35: 390x844 Mobile Owner Reports
    await page.setViewportSize({ width: 390, height: 844 });
    await saveBaselineScreenshot(page, "35-responsive-390x844-mobile-owner-reports.png");

    // 36 & 37: 1366x768 at 125% and 150% zoom on Checkout
    await page.setViewportSize({ width: 1366, height: 768 });
    await sidebarNavButton(page, "POS Checkout").click();
    await page.evaluate(() => {
      document.documentElement.style.zoom = "1.25";
    });
    await saveBaselineScreenshot(page, "36-scale-125pct-checkout.png");

    await page.evaluate(() => {
      document.documentElement.style.zoom = "1.5";
    });
    await saveBaselineScreenshot(page, "37-scale-150pct-checkout.png");
  });

  test("38 & 39 - Internal / Unreachable Host surfaces (OfficialSetupPage & TraditionalReportingPage via popstate)", async ({
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
    await saveBaselineScreenshot(page, "38-internal-official-setup.png");

    // 39: Navigate to TraditionalReportingPage via synthetic popstate
    await page.evaluate(() => {
      window.history.pushState({ page: "traditional-reporting" }, "", "/?page=traditional-reporting");
      window.dispatchEvent(new PopStateEvent("popstate", { state: { page: "traditional-reporting" } }));
    });
    await expect(page.getByRole("heading", { name: "Internal Compliance Reporting Lab" })).toBeVisible();
    await saveBaselineScreenshot(page, "39-internal-traditional-reporting.png");
  });

  test("40 to 42 - Terminal / Remote Client flow (TerminalActivationPage, RemoteClientLandingPage, RemotePosClientPage)", async ({
    page,
  }) => {
    await installMockSalrynApi(page, { persona: "admin" });

    // 40: Unlicensed Terminal device gate on non-loopback hostname (*.localhost)
    await page.goto("http://salryn-terminal.localhost:5173/");
    await expect(page.getByRole("heading", { name: "SALRYN Terminal" })).toBeVisible();
    await saveBaselineScreenshot(page, "40-terminal-activation-gate.png");

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
    await saveBaselineScreenshot(page, "41-terminal-remote-landing.png");

    // 42: Open Terminal POS pairing screen
    await page.getByRole("button", { name: /Open Salryn POS terminal/i }).click();
    await expect(page.getByRole("heading", { name: "Connect terminal to Host" })).toBeVisible();
    await saveBaselineScreenshot(page, "42-terminal-remote-pos-pairing.png");
  });
});
