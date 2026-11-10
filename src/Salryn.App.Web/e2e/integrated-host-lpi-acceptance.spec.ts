import fs from "node:fs";
import { expect, test, type Page } from "@playwright/test";

/**
 * INTEGRATED_HOST Real-Host Acceptance Test Suite:
 * Procure-to-Pay End-to-End Vertical Acceptance (R21-REVIEW-01 / SAFETY-01).
 *
 * Requirements:
 * - Real live Local API Host on dedicated port with disposable SQLite database.
 * - Zero mocking of /api/lpi/**.
 * - Complete user interactions through actual UI forms, modals, and buttons:
 *   1. Requester login & PR creation and submission.
 *   2. Independent Manager login & PR approval (separation of duties).
 *   3. Quotation recording & winning selection.
 *   4. PR to PO split/consolidated conversion.
 *   5. Partial Goods Receiving (RR) into location with accepted/rejected routing.
 *   6. Multi-location stock inspection, count adjustment (ADJ), and supplier return (SRET).
 *   7. AP Clerk login & 3-way invoice intake (price variance triggered).
 *   8. Independent Manager login & variance authorization.
 *   9. AP Clerk login & payment record with POS cash drawer protection.
 *   10. Independent Manager login & settlement approval (dual-control authorization).
 *   11. UI refresh durability and data persistence.
 * - Fail-closed: zero unhandled JS exceptions, zero console errors, zero 500s.
 */

function validateTestEnvironmentSafety(): string {
  const rawHostUrl = process.env.SALRYN_INTEGRATED_HOST_URL;
  if (!rawHostUrl || !rawHostUrl.trim()) {
    throw new Error(
      "FAIL-CLOSED SAFETY VIOLATION: SALRYN_INTEGRATED_HOST_URL environment variable is required. Acceptance suite cannot run against an implicit or default host."
    );
  }

  const hostUrl = rawHostUrl.trim().replace(/\/+$/, "");
  let parsedUrl: URL;
  try {
    parsedUrl = new URL(hostUrl);
  } catch {
    throw new Error(`FAIL-CLOSED SAFETY VIOLATION: Invalid SALRYN_INTEGRATED_HOST_URL '${hostUrl}'.`);
  }

  // Reject production operator default port 5299
  if (parsedUrl.port === "5299" || (!parsedUrl.port && parsedUrl.protocol === "http:")) {
    throw new Error(
      `FAIL-CLOSED SAFETY VIOLATION: Acceptance suite cannot target port 5299 or default port ('${hostUrl}'). It must target an ephemeral, test-owned loopback host.`
    );
  }

  // Must be loopback
  if (parsedUrl.hostname !== "127.0.0.1" && parsedUrl.hostname !== "localhost") {
    throw new Error(
      `FAIL-CLOSED SAFETY VIOLATION: Acceptance suite cannot target non-loopback host ('${hostUrl}').`
    );
  }

  const testToken = process.env.SALRYN_DISPOSABLE_TEST_TOKEN;
  const markerPath = process.env.SALRYN_DISPOSABLE_MARKER_PATH;
  if (!testToken || !markerPath) {
    throw new Error(
      "FAIL-CLOSED SAFETY VIOLATION: Both SALRYN_DISPOSABLE_TEST_TOKEN and SALRYN_DISPOSABLE_MARKER_PATH must be provided by the acceptance runner."
    );
  }

  if (!fs.existsSync(markerPath)) {
    throw new Error(
      `FAIL-CLOSED SAFETY VIOLATION: Disposable test environment marker file does not exist at '${markerPath}'.`
    );
  }

  let markerData: any;
  try {
    markerData = JSON.parse(fs.readFileSync(markerPath, "utf-8"));
  } catch (e) {
    throw new Error(
      `FAIL-CLOSED SAFETY VIOLATION: Failed to parse test marker file at '${markerPath}': ${(e as Error).message}`
    );
  }

  if (
    !markerData ||
    markerData.token !== testToken ||
    !markerData.isDisposableTest ||
    !markerData.databasePath
  ) {
    throw new Error(
      "FAIL-CLOSED SAFETY VIOLATION: Disposable test environment marker token mismatch or missing isDisposableTest declaration."
    );
  }

  // Ensure the database path does not point to operator's production directory
  const dbPath = String(markerData.databasePath).toLowerCase();
  if (dbPath.includes("programdata\\salryn") || dbPath.includes("program files\\")) {
    throw new Error(
      `FAIL-CLOSED SAFETY VIOLATION: Test marker database path targets production/installed location: '${markerData.databasePath}'.`
    );
  }

  // Verify marker hostUrl matches environment hostUrl (HOST-ID-02)
  if (markerData.hostUrl) {
    const markerHostUrl = String(markerData.hostUrl).trim().replace(/\/+$/, "").toLowerCase();
    if (markerHostUrl !== hostUrl.toLowerCase()) {
      throw new Error(
        `FAIL-CLOSED SAFETY VIOLATION: Host URL mismatch. Env specifies '${hostUrl}', but marker binds '${markerData.hostUrl}'.`
      );
    }
  } else {
    throw new Error(
      "FAIL-CLOSED SAFETY VIOLATION: Disposable marker must include bound hostUrl."
    );
  }

  // Verify processId (HOST-ID-02)
  if (!markerData.processId || typeof markerData.processId !== "number") {
    throw new Error(
      "FAIL-CLOSED SAFETY VIOLATION: Disposable marker must include valid processId."
    );
  }

  return hostUrl;
}

const HOST_URL = validateTestEnvironmentSafety();
const REQUESTER_USER = process.env.SALRYN_QA_REQUESTER_USER ?? "lpi-requester";
const REQUESTER_PASS = process.env.SALRYN_QA_REQUESTER_PASS ?? "Requester123!";
const APCLERK_USER = process.env.SALRYN_QA_APCLERK_USER ?? "lpi-apclerk";
const APCLERK_PASS = process.env.SALRYN_QA_APCLERK_PASS ?? "ApClerk123!";
const MANAGER_USER = process.env.SALRYN_QA_MANAGER_USER ?? "lpi-manager";
const MANAGER_PASS = process.env.SALRYN_QA_MANAGER_PASS ?? "Manager123!";

async function login(page: Page, username: string, pass: string) {
  await page.goto(`${HOST_URL}/`);
  await expect(page.getByRole("heading", { name: "Sign in to Salryn POS" })).toBeVisible({ timeout: 15000 });
  await page.locator("#login-username").fill(username);
  await page.locator("#login-password").fill(pass);
  await page.getByRole("button", { name: /Sign in/i }).click();
  await expect(page.getByRole("button", { name: "Sign out" })).toBeVisible({ timeout: 15000 });
}

async function logout(page: Page) {
  await page.getByRole("button", { name: "Sign out" }).click();
  const confirmBtn = page.getByRole("dialog").getByRole("button", { name: "Sign out" });
  if (await confirmBtn.isVisible({ timeout: 2000 }).catch(() => false)) {
    await confirmBtn.click();
  }
  await expect(page.getByRole("heading", { name: "Sign in to Salryn POS" })).toBeVisible({ timeout: 10000 });
}

test.describe.configure({ mode: "serial" });

test.describe("INTEGRATED_HOST LPI End-to-End Acceptance Suite @integrated", () => {
  const uncaughtErrors: string[] = [];

  test.beforeAll(async ({ request }) => {
    // Authenticated Host Identity probe (SAFETY-01 / HOST-ID-02)
    const res = await request.get(`${HOST_URL}/api/test/host-identity`);
    if (!res.ok()) {
      throw new Error(
        `FAIL-CLOSED SAFETY VIOLATION: Host identity check failed with HTTP ${res.status()}. Target '${HOST_URL}' is not a verified test host.`
      );
    }
    const identity = await res.json();
    const testToken = process.env.SALRYN_DISPOSABLE_TEST_TOKEN;
    if (identity.testHostNonce !== testToken) {
      throw new Error(
        `FAIL-CLOSED SAFETY VIOLATION: Host identity nonce mismatch. Target is not the runner's ephemeral host.`
      );
    }
  });

  test.beforeEach(({ page }) => {
    uncaughtErrors.length = 0;
    page.on("pageerror", (err) => {
      uncaughtErrors.push(`[PageError] ${err.message}`);
    });
    page.on("console", (msg) => {
      if (msg.type() === "error") {
        if (msg.text().includes("403") || msg.text().includes("401")) return;
        uncaughtErrors.push(`[ConsoleError] ${msg.text()}`);
      }
    });
  });

  test.afterEach(() => {
    expect(uncaughtErrors, `Browser reported uncaught errors: ${uncaughtErrors.join("; ")}`).toEqual([]);
  });

  test("Complete End-to-End Real-Host Procure-to-Pay Lifecycle", async ({ page }) => {
    test.setTimeout(90000);

    // =========================================================================
    // STEP 1: Requester Logs In, Creates PR, and Submits for Approval
    // =========================================================================
    await login(page, REQUESTER_USER, REQUESTER_PASS);
    await page.goto(`${HOST_URL}/?page=procure-to-pay&tab=pr`);
    await expect(page.getByRole("heading", { name: "Purchase Requisitions (PR)" })).toBeVisible({ timeout: 10000 });

    // Open PR Modal
    await page.getByRole("button", { name: "+ Create Requisition" }).click();
    const prDialog = page.getByRole("dialog", { name: "Create Purchase Requisition (PR)" });
    await expect(prDialog).toBeVisible();

    // Fill PR form
    await prDialog.locator("input").first().fill("Store Operations");
    await prDialog.locator("input").nth(2).fill("Real-Host Acceptance Coffee Restock");

    // Select Product (Alpha Coffee Beans) and set quantity
    const productSelect = prDialog.locator("select").first();
    const productOptions = await productSelect.locator("option").all();
    expect(productOptions.length).toBeGreaterThan(1);
    await productSelect.selectOption({ index: 1 });

    const qtyInput = prDialog.locator("input[type='number']").first();
    await qtyInput.fill("10");

    await prDialog.getByRole("button", { name: "Save Requisition" }).click();
    await expect(prDialog).toBeHidden();

    // Verify PR row created in Draft status
    const prTable = page.locator("table");
    await expect(prTable).toContainText("Store Operations", { timeout: 10000 });
    await expect(prTable).toContainText("Draft");

    // Submit PR
    await page.getByRole("button", { name: "Submit" }).first().click();
    await expect(prTable).toContainText("Submitted", { timeout: 10000 });

    // Verify separation of duties: Requester cannot approve PR (Approve button is not present)
    await expect(page.getByRole("button", { name: "Approve" })).toHaveCount(0);

    await logout(page);

    // =========================================================================
    // STEP 2: Manager Logs In, Approves PR, Records Quotation, and Generates PO
    // =========================================================================
    await login(page, MANAGER_USER, MANAGER_PASS);
    await page.goto(`${HOST_URL}/?page=procure-to-pay&tab=pr`);
    await expect(page.getByRole("heading", { name: "Purchase Requisitions (PR)" })).toBeVisible({ timeout: 10000 });

    // Manager approves the submitted PR
    await expect(page.getByRole("button", { name: "Approve" }).first()).toBeVisible();
    await page.getByRole("button", { name: "Approve" }).first().click();
    await expect(page.locator("table")).toContainText("Approved", { timeout: 10000 });

    // Navigate to Quotations tab
    await page.getByRole("button", { name: "📋 Quotations" }).click();
    await expect(page.getByRole("heading", { name: "Supplier Quotations & Quote Comparison" })).toBeVisible();

    // Select the PR from the dropdown if needed
    const prQuoteSelect = page.locator("select").first();
    const prQuoteOptions = await prQuoteSelect.locator("option").all();
    if (prQuoteOptions.length > 1) {
      await prQuoteSelect.selectOption({ index: 1 });
    }

    // Record Quotation
    await page.getByRole("button", { name: "+ Record Quotation" }).click();
    const quoteDialog = page.getByRole("dialog", { name: "Record Supplier Quotation" });
    await expect(quoteDialog).toBeVisible();

    // Select supplier and line
    const quoteSupplierSelect = quoteDialog.locator("select").nth(0);
    await quoteSupplierSelect.selectOption({ index: 1 });
    const quoteLineSelect = quoteDialog.locator("select").nth(1);
    await quoteLineSelect.selectOption({ index: 1 });

    // Fill quotation parameters
    await quoteDialog.locator("input[type='number']").nth(0).fill("450.00");
    await quoteDialog.locator("input[type='number']").nth(1).fill("5");
    await quoteDialog.locator("input[type='number']").nth(2).fill("30");

    // Ensure winner is selected
    const winnerCheckbox = quoteDialog.locator("input[type='checkbox']");
    if (!(await winnerCheckbox.isChecked())) {
      await winnerCheckbox.check();
    }
    await quoteDialog.getByRole("button", { name: "Save Quotation" }).click();
    await expect(quoteDialog).toBeHidden();

    // Verify quote table displays winning selection
    await expect(page.locator("table")).toContainText("🏆 Selected Winner", { timeout: 10000 });

    // Return to PR tab and generate PO
    await page.getByRole("button", { name: "📝 Requisitions (PR)" }).click();
    await page.getByRole("button", { name: "Generate POs" }).first().click();
    const splitDialog = page.getByRole("dialog", { name: /Generate Purchase Orders/i });
    await expect(splitDialog).toBeVisible();

    await splitDialog.getByRole("button", { name: "Generate PO(s)" }).click();
    await expect(splitDialog).toBeHidden();

    // Verify PO generated in Purchase Orders tab
    await expect(page.getByRole("heading", { name: "Purchase Orders (PO)" })).toBeVisible({ timeout: 10000 });
    const poTable = page.locator("table");
    await expect(poTable).toContainText("Approved", { timeout: 10000 });

    // =========================================================================
    // STEP 3: Goods Receiving (RR) Against Generated PO
    // =========================================================================
    await page.getByRole("button", { name: "🚚 Receiving (RR)" }).click();
    await expect(page.getByRole("heading", { name: "Goods Receiving Reports (RR)" })).toBeVisible();

    // Select PO to receive against
    const receivePoSelect = page.locator("select").first();
    const receivePoOptions = await receivePoSelect.locator("option").all();
    expect(receivePoOptions.length).toBeGreaterThan(1);
    await receivePoSelect.selectOption({ index: 1 });

    const rrDialog = page.getByRole("dialog", { name: /Receive Goods against PO/i });
    await expect(rrDialog).toBeVisible();

    // Fill receiving details
    await rrDialog.locator("input").first().fill("DR-REALHOST-2026-001");
    await rrDialog.locator("input").nth(1).fill("Acceptance Logistics Carrier");

    // Line inspection: Delivered 10, Accepted 8, Rejected 2
    const deliveredInput = rrDialog.locator("input[type='number']").nth(0);
    await deliveredInput.fill("10");
    const acceptedInput = rrDialog.locator("input[type='number']").nth(1);
    await acceptedInput.fill("8");
    const rejectedInput = rrDialog.locator("input[type='number']").nth(2);
    await rejectedInput.fill("2");

    // Rejection reason
    const rejectionReasonInput = rrDialog.locator("input").last();
    await rejectionReasonInput.fill("Damaged vacuum seals during courier transport");

    await rrDialog.getByRole("button", { name: "Post Receiving Report" }).click();
    await expect(rrDialog).toBeHidden();

    // Verify RR posted
    const rrTable = page.locator("table");
    await expect(rrTable).toContainText("Posted", { timeout: 10000 });
    await expect(rrTable).toContainText("RR-");

    // =========================================================================
    // STEP 4: Inventory Stock Cards, Count Adjustment & Supplier Return
    // =========================================================================
    await page.getByRole("button", { name: "📈 Stock & Returns" }).click();
    await expect(page.getByRole("heading", { name: "Multi-Location Stock Cards & Valuation" })).toBeVisible();

    // Verify stock table displays Alpha Coffee Beans
    const stockTable = page.locator("table").first();
    await expect(stockTable).toContainText("Alpha Coffee Beans 1kg", { timeout: 10000 });

    // Perform Physical Count Adjustment
    await page.getByRole("button", { name: "+ Count Adjustment (ADJ)" }).click();
    const adjDialog = page.getByRole("dialog", { name: /Physical Inventory Count Variance Adjustment/i });
    await expect(adjDialog).toBeVisible();

    const actualCountInput = adjDialog.locator("input[type='number']").nth(1);
    const currentActual = await actualCountInput.inputValue();
    const adjustedCount = (parseFloat(currentActual || "28") + 1).toString();
    await actualCountInput.fill(adjustedCount);
    await adjDialog.locator("input").last().fill("Physical cycle count audit verification");

    await adjDialog.getByRole("button", { name: "Post Adjustment" }).click();
    await expect(adjDialog).toBeHidden();

    // Perform Supplier Return (SRET)
    await page.getByRole("button", { name: "+ Supplier Return (SRET)" }).click();
    const sretDialog = page.getByRole("dialog", { name: "Supplier Return & Debit Memo (SRET)" });
    await expect(sretDialog).toBeVisible();

    await sretDialog.locator("input").first().fill("Latent warehouse leak discovered upon opening package");
    await sretDialog.locator("input[type='number']").first().fill("1");

    await sretDialog.getByRole("button", { name: "Post Return & Issue Debit Memo" }).click();
    await expect(sretDialog).toBeHidden();

    // Verify return row exists in returns table
    const sretTable = page.locator("table").nth(1);
    await expect(sretTable).toContainText("Posted", { timeout: 10000 });

    await logout(page);

    // =========================================================================
    // STEP 5: AP Clerk Logs In & Intakes Supplier Invoice (Triggers Price Variance)
    // =========================================================================
    await login(page, APCLERK_USER, APCLERK_PASS);
    await page.goto(`${HOST_URL}/?page=procure-to-pay&tab=ap`);
    await expect(page.getByRole("heading", { name: "Supplier Invoices & 3-Way Match" })).toBeVisible();

    await page.getByRole("button", { name: "+ Intake Supplier Invoice" }).click();
    const invDialog = page.getByRole("dialog", { name: /Supplier Invoice Intake/i });
    await expect(invDialog).toBeVisible();

    // Select Supplier: Apex Roasters
    await invDialog.getByTestId("invoice-supplier-select").selectOption({ index: 0 });
    // Fill Invoice #
    await invDialog.getByTestId("invoice-number-input").fill("INV-REALHOST-2026-001");
    // Dates
    await invDialog.getByTestId("invoice-date-input").fill("2026-11-10");
    await invDialog.getByTestId("invoice-due-date-input").fill("2026-11-25");

    // Product line: Alpha Coffee Beans, 8 units @ 465.00 (triggers price variance against PO 450.00)
    await invDialog.getByTestId("invoice-item-0-product-select").selectOption({ index: 0 });
    await invDialog.getByTestId("invoice-item-0-qty-input").fill("8");
    await invDialog.getByTestId("invoice-item-0-cost-input").fill("465.00");

    await invDialog.getByTestId("invoice-submit-button").click();
    await expect(invDialog).toBeHidden();

    // Verify invoice created with price variance
    const invTable = page.locator("table").first();
    await expect(invTable).toContainText("INV-REALHOST-2026-001", { timeout: 10000 });
    await expect(invTable).toContainText("PriceVariance");

    // Verify AP clerk cannot approve variance
    await expect(page.getByRole("button", { name: "Approve Variance" })).toHaveCount(0);

    await logout(page);

    // =========================================================================
    // STEP 6: Manager Approves Variance (Separation of Duties)
    // =========================================================================
    await login(page, MANAGER_USER, MANAGER_PASS);
    await page.goto(`${HOST_URL}/?page=procure-to-pay&tab=ap`);
    await expect(page.getByRole("heading", { name: "Supplier Invoices & 3-Way Match" })).toBeVisible();

    // Manager approves variance
    await expect(page.getByRole("button", { name: "Approve Variance" }).first()).toBeVisible();
    await page.getByRole("button", { name: "Approve Variance" }).first().click();

    const varDialog = page.getByRole("dialog", { name: /Approve 3-Way Match Variance/i });
    await expect(varDialog).toBeVisible();

    await varDialog.locator("input").first().fill("Authorized supplier fuel price increase per contract addendum");
    await varDialog.getByRole("button", { name: "Approve Variance" }).click();
    await expect(varDialog).toBeHidden();

    // Verify match status changed to VarianceApproved and status is PostedOpen
    await expect(invTable).toContainText("VarianceApproved", { timeout: 10000 });
    await expect(invTable).toContainText("PostedOpen");

    await logout(page);

    // =========================================================================
    // STEP 7: AP Clerk Records Supplier Payment (Protected Cash Drawer Source)
    // =========================================================================
    await login(page, APCLERK_USER, APCLERK_PASS);
    await page.goto(`${HOST_URL}/?page=procure-to-pay&tab=ap`);
    await expect(page.getByRole("heading", { name: "Supplier Invoices & 3-Way Match" })).toBeVisible();

    // Click Record Payment on the invoice
    await page.getByRole("button", { name: "Record Payment" }).first().click();
    const pmtDialog = page.getByRole("dialog", { name: /Record Payment for/i });
    await expect(pmtDialog).toBeVisible();

    // Verify cash drawer protection notice is visible
    await expect(pmtDialog).toContainText("Supplier disbursements are isolated from the POS cashier cash drawer");

    // Reference and payment method
    await pmtDialog.locator("input").nth(1).fill("TRF-REALHOST-8899");
    await pmtDialog.locator("select").first().selectOption("BankTransfer");

    // Verify non-POS disbursement source
    await expect(pmtDialog.locator("input").nth(2)).toHaveValue(/OperatingAccount/);

    await pmtDialog.getByRole("button", { name: "Submit for Approval" }).click();
    await expect(pmtDialog).toBeHidden();

    // In payments table, verify payment recorded as Pending Approval
    const pmtTable = page.locator("table").nth(1);
    await expect(pmtTable).toContainText("TRF-REALHOST-8899", { timeout: 10000 });
    await expect(pmtTable).toContainText("Pending Approval");

    // Separation of duties: AP clerk cannot approve settlement
    const approveSettlementButtons = page.getByRole("button", { name: "Approve Settlement" });
    if ((await approveSettlementButtons.count()) > 0) {
      await expect(approveSettlementButtons.first()).toBeDisabled();
    }

    await logout(page);

    // =========================================================================
    // STEP 8: Manager Authorizes Payment Settlement (Dual-Control Authorization)
    // =========================================================================
    await login(page, MANAGER_USER, MANAGER_PASS);
    await page.goto(`${HOST_URL}/?page=procure-to-pay&tab=ap`);
    await expect(page.getByRole("heading", { name: "Supplier Invoices & 3-Way Match" })).toBeVisible();

    const managerPmtTable = page.locator("table").nth(1);
    await expect(managerPmtTable).toContainText("TRF-REALHOST-8899");

    const settleBtn = page.getByRole("button", { name: "Approve Settlement" }).first();
    await expect(settleBtn).toBeEnabled();
    await settleBtn.click();

    const authDialog = page.getByRole("dialog", { name: "Authorize Supplier Payment Settlement" });
    await expect(authDialog).toBeVisible();

    await authDialog.locator("input").first().fill("Authorized by Finance Manager per electronic fund transfer release");
    await authDialog.getByRole("button", { name: "Authorize Settlement" }).click();
    await expect(authDialog).toBeHidden();

    // Verify payment settled and invoice fully paid
    await expect(managerPmtTable).toContainText("Settled", { timeout: 10000 });
    const managerInvTable = page.locator("table").first();
    await expect(managerInvTable).toContainText("Paid", { timeout: 10000 });

    // =========================================================================
    // STEP 9: UI Refresh Persistence Verification
    // =========================================================================
    await page.reload();
    await expect(page.getByRole("heading", { name: "Supplier Invoices & 3-Way Match" })).toBeVisible({ timeout: 15000 });
    await expect(page.locator("table").first()).toContainText("INV-REALHOST-2026-001");
    await expect(page.locator("table").first()).toContainText("Paid");
    await expect(page.locator("table").nth(1)).toContainText("Settled");
  });

  test("Focused UI & DB Regression: Duplicate-Product Same-PO Line Disambiguation & Line Isolation", async ({ page, request }) => {
    test.setTimeout(90000);

    // 1. Authenticate as Manager via API to seed a PO with TWO lines for the same product at different costs
    const loginRes = await request.post(`${HOST_URL}/api/auth/login`, {
      data: { username: MANAGER_USER, password: MANAGER_PASS },
    });
    expect(loginRes.ok()).toBeTruthy();

    const foundationRes = await request.get(`${HOST_URL}/api/lpi/foundation`);
    expect(foundationRes.ok()).toBeTruthy();
    const foundation = await foundationRes.json();
    const sup = foundation.suppliers[0]; // Apex Roasters
    const prod = foundation.products[0]; // Alpha Coffee Beans
    const uom = foundation.unitsOfMeasure[0];
    const loc = foundation.locations[0];

    // Create PO with 2 distinct lines for the same product:
    // Line A: 10 units @ 400.00 (40000 cents)
    // Line B: 20 units @ 500.00 (50000 cents)
    const poRes = await request.post(`${HOST_URL}/api/lpi/purchase-orders`, {
      data: {
        supplierId: sup.id,
        expectedDeliveryDate: new Date(Date.now() + 86400000 * 7).toISOString().slice(0, 10),
        deliveryLocationId: loc.id,
        paymentTermsDays: 30,
        notes: "Dual-tier same-product PO test",
        submitAndApproveImmediately: true,
        lines: [
          {
            productId: prod.id,
            itemDescription: `${prod.name} (Tier A)`,
            orderedQtyX1000: 10000,
            unitCostCents: 40000,
            notes: "Tier A lot",
          },
          {
            productId: prod.id,
            itemDescription: `${prod.name} (Tier B)`,
            orderedQtyX1000: 20000,
            unitCostCents: 50000,
            notes: "Tier B lot",
          },
        ],
      },
    });
    if (!poRes.ok()) {
      const err = await poRes.text();
      throw new Error(`PO creation failed: HTTP ${poRes.status()} - ${err}`);
    }
    const createdPo = await poRes.json();
    const poLines = createdPo.lines ?? createdPo.items;
    const lineA = poLines[0];
    const lineB = poLines[1];

    // Create Receiving Report matching both lines
    const rrRes = await request.post(`${HOST_URL}/api/lpi/receiving-reports`, {
      data: {
        purchaseOrderId: createdPo.id,
        supplierId: sup.id,
        receivedDate: new Date().toISOString().slice(0, 10),
        deliveryReceiptNumber: "DR-DUP-TEST-001",
        carrierOrDriver: "Apex Logistics / Juan Driver",
        locationId: loc.id,
        postImmediately: true,
        notes: "Received both lots",
        lines: [
          {
            purchaseOrderLineId: lineA.id,
            actualDeliveredQtyX1000: 10000,
            acceptedQtyX1000: 10000,
            rejectedQtyX1000: 0,
          },
          {
            purchaseOrderLineId: lineB.id,
            actualDeliveredQtyX1000: 20000,
            acceptedQtyX1000: 20000,
            rejectedQtyX1000: 0,
          },
        ],
      },
    });
    if (!rrRes.ok()) {
      const err = await rrRes.text();
      throw new Error(`RR creation failed: HTTP ${rrRes.status()} - ${err}`);
    }
    const createdRr = await rrRes.json();
    const rrLines = createdRr.lines ?? createdRr.items;
    const rrLineA = rrLines[0];
    const rrLineB = rrLines[1];

    // 2. AP Clerk logs in and opens Invoice Intake Dialog
    await login(page, APCLERK_USER, APCLERK_PASS);
    await page.goto(`${HOST_URL}/?page=procure-to-pay&tab=ap`);
    await expect(page.getByRole("heading", { name: "Supplier Invoices & 3-Way Match" })).toBeVisible();

    await page.getByRole("button", { name: "+ Intake Supplier Invoice" }).click();
    const invDialog = page.getByRole("dialog", { name: /Supplier Invoice Intake/i });
    await expect(invDialog).toBeVisible();

    // Select the duplicate PO and RR
    await invDialog.getByTestId("invoice-po-select").selectOption(createdPo.id);
    await invDialog.getByTestId("invoice-rr-select").selectOption(createdRr.id);

    // Verify initial form state for duplicate product:
    // Form detects ambiguity, does NOT auto-bind an arbitrary line ID, and disables submission
    const poLineSelect = invDialog.getByTestId("invoice-item-0-po-line-select");
    const rrLineSelect = invDialog.getByTestId("invoice-item-0-rr-line-select");
    await expect(poLineSelect).toBeVisible();
    await expect(rrLineSelect).toBeVisible();
    expect(await poLineSelect.inputValue()).toBe("");
    expect(await rrLineSelect.inputValue()).toBe("");

    // Submit button must be disabled due to ambiguity
    const submitBtn = invDialog.getByTestId("invoice-submit-button");
    await expect(submitBtn).toBeDisabled();
    await expect(invDialog.getByTestId("invoice-ambiguity-warning")).toBeVisible();

    // Verify changing product resets/invalidates line bindings
    const prodSelect = invDialog.getByTestId("invoice-item-0-product-select");
    await prodSelect.selectOption({ index: 1 });
    await prodSelect.selectOption({ index: 0 });
    expect(await poLineSelect.inputValue()).toBe("");
    expect(await rrLineSelect.inputValue()).toBe("");
    await expect(submitBtn).toBeDisabled();

    // 3. Operator explicitly selects Line B (20 pcs @ 500.00) in both PO and RR selectors
    await poLineSelect.selectOption(lineB.id);
    await rrLineSelect.selectOption(rrLineB.id);

    // Fill Invoice details for Line B
    await invDialog.getByTestId("invoice-number-input").fill("INV-DUP-LINEB-001");
    await invDialog.getByTestId("invoice-item-0-qty-input").fill("20");
    await invDialog.getByTestId("invoice-item-0-cost-input").fill("500.00");

    // Submit button is now enabled
    await expect(submitBtn).toBeEnabled();
    await expect(invDialog.getByTestId("invoice-ambiguity-warning")).toHaveCount(0);

    // Submit the disambiguated invoice
    await submitBtn.click();
    await expect(invDialog).toBeHidden();

    // Verify invoice appeared in table with status Matched (0 variance)
    const invTable = page.locator("table").first();
    await expect(invTable).toContainText("INV-DUP-LINEB-001", { timeout: 10000 });
    await expect(invTable).toContainText("Matched");

    // 4. Verify DB / API: invoice line is linked strictly to Line B, and Line A is completely unbilled
    const invoicesRes = await request.get(`${HOST_URL}/api/lpi/invoices`);
    expect(invoicesRes.ok()).toBeTruthy();
    const invoicesList = await invoicesRes.json();
    const targetInv = invoicesList.find(
      (i: any) => i.supplierInvoiceNo === "INV-DUP-LINEB-001" || i.invoiceNumber === "INV-DUP-LINEB-001"
    );
    expect(targetInv).toBeDefined();
    expect(targetInv.matchStatus).toBe("Matched");
    const invLines = targetInv.lines ?? targetInv.items;
    const invLine0 = invLines[0];
    const poLineId = invLine0.purchaseOrderLineId ?? invLine0.poItemId;
    const rrLineId = invLine0.receivingReportLineId ?? invLine0.rrItemId;
    expect(poLineId).toBe(lineB.id);
    expect(rrLineId).toBe(rrLineB.id);
    expect(poLineId).not.toBe(lineA.id);

    // 5. Negative check: API rejects ambiguous intake without explicit line IDs
    const apClerkAuthRes = await request.post(`${HOST_URL}/api/auth/login`, {
      data: { username: APCLERK_USER, password: APCLERK_PASS },
    });
    expect(apClerkAuthRes.ok()).toBeTruthy();

    const negAmbiguousRes = await request.post(`${HOST_URL}/api/lpi/invoices`, {
      data: {
        supplierId: sup.id,
        invoiceNumber: "INV-DUP-REJECT-001",
        invoiceDate: "2026-11-10",
        dueDate: "2026-11-25",
        purchaseOrderId: createdPo.id,
        receivingReportId: createdRr.id,
        subtotalCents: 500000,
        totalCents: 500000,
        items: [
          {
            productId: prod.id,
            invoicedQuantityX1000: 10000,
            unitCostCents: 40000,
          },
        ],
      },
    });
    expect(negAmbiguousRes.status()).toBe(400);

    // Negative check: API rejects cross-mismatched line pairing (Line B PO with Line A RR)
    const negMismatchRes = await request.post(`${HOST_URL}/api/lpi/invoices`, {
      data: {
        supplierId: sup.id,
        invoiceNumber: "INV-DUP-MISMATCH-001",
        invoiceDate: "2026-11-10",
        dueDate: "2026-11-25",
        purchaseOrderId: createdPo.id,
        receivingReportId: createdRr.id,
        subtotalCents: 500000,
        totalCents: 500000,
        items: [
          {
            productId: prod.id,
            invoicedQuantityX1000: 10000,
            unitCostCents: 40000,
            poItemId: lineB.id,
            rrItemId: rrLineA.id,
          },
        ],
      },
    });
    expect(negMismatchRes.status()).toBe(400);

    await logout(page);
  });
});
