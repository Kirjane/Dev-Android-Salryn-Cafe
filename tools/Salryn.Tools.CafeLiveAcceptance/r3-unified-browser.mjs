import { createRequire } from "node:module";
import { readFileSync } from "node:fs";
import path from "node:path";

const playwrightPkg = process.env.CAFE_LIVE_PLAYWRIGHT_PACKAGE_JSON;
const axePath = process.env.CAFE_LIVE_AXE_PATH;
const baseUrl = process.env.CAFE_R3_BASE_URL;
const expectedNonce = process.env.CAFE_R3_NONCE;
const root = process.env.CAFE_R3_ROOT;
const phase = process.env.CAFE_R3_PHASE;
const ownerUser = process.env.CAFE_R3_OWNER_USER;
const ownerPass = process.env.CAFE_R3_OWNER_PASS;
const cashierUser = process.env.CAFE_R3_CASHIER_USER;
const cashierPass = process.env.CAFE_R3_CASHIER_PASS;
const auditorUser = process.env.CAFE_R3_AUDITOR_USER;
const auditorPass = process.env.CAFE_R3_AUDITOR_PASS;

const require = createRequire(playwrightPkg);
const { chromium, expect } = require("@playwright/test");

function assert(cond, msg) {
  if (!cond) throw new Error(msg);
}

async function runAxe(page, label) {
  await page.addScriptTag({ content: readFileSync(axePath, "utf8") });
  const violations = await page.evaluate(async () => {
    const r = await window.axe.run();
    return r.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
  });
  assert(
    violations.length === 0,
    `axe-core critical/serious violations on ${label}: ${violations.map((v) => `${v.id}:${v.help}`).join("; ")}`,
  );
}

async function ensureAdminSectionExpanded(nav) {
  const usersBtn = nav.getByRole("button", { name: "Users & Roles" });
  if ((await usersBtn.count()) === 0) {
    await nav.getByRole("button", { name: /Administration/i }).click();
  }
}

const browser = await chromium.launch({ headless: true });
let assertions = 0;

try {
  // Confirm real disposable Host identity directly (NO Vite dev proxy!).
  const idRes = await fetch(`${baseUrl}/api/test/host-identity`);
  assert(idRes.ok, `Host identity probe failed: ${idRes.status}`);
  const idBody = await idRes.json();
  assert(idBody.testHostNonce === expectedNonce, "Host identity nonce mismatch");
  assert(
    String(idBody.databasePath ?? "").toLowerCase().startsWith(root.toLowerCase()),
    "Host DB must remain inside disposable root",
  );
  assertions += 2;

  if (phase === "unlicensed") {
    const ctx = await browser.newContext({ viewport: { width: 1366, height: 768 } });
    const page = await ctx.newPage();
    page.setDefaultTimeout(60000);
    await page.goto(`${baseUrl}/cafe/r3`, { waitUntil: "networkidle" });
    await expect(page.getByRole("heading", { name: /Host License Required/i })).toBeVisible();
    await expect(page.getByRole("button", { name: "Sign In" })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Initialize Cafe Host" })).toHaveCount(0);
    await runAxe(page, "unlicensed-gate");
    await page.screenshot({ path: path.join(root, "r3-unified-unlicensed-1366.png"), fullPage: true });
    assertions += 4;
    await ctx.close();
  } else if (phase === "first-admin") {
    // Real First-Run Cafe Setup via unified R3 UI on a clean empty licensed database!
    const ctx = await browser.newContext({ viewport: { width: 1366, height: 768 } });
    const page = await ctx.newPage();
    page.setDefaultTimeout(60000);
    await page.goto(`${baseUrl}/cafe/r3`, { waitUntil: "networkidle" });
    await expect(page.getByRole("heading", { name: /First-Run Cafe Setup/i })).toBeVisible();
    await page.getByLabel("Store Name").fill("Salryn Cafe Gate D First Run");
    await page.getByLabel("Admin Username").fill("gate-d-admin");
    await page.getByLabel("Admin Display Name").fill("Gate D Store Owner");
    await page.getByLabel("Admin Password").fill("GateD-Admin!2026");
    await page.getByRole("button", { name: "Initialize Cafe Host" }).click();

    // After bootstrap completes, operator signs in with the newly created first-admin credentials
    await expect(page.getByRole("heading", { name: /Salryn Cafe Sign In/i })).toBeVisible();
    await page.getByLabel("Username").fill("gate-d-admin");
    await page.getByLabel("Password").fill("GateD-Admin!2026");
    await page.getByRole("button", { name: "Sign In" }).click();
    await expect(page.getByRole("heading", { name: /Today — Live Operations Overview/i })).toBeVisible();
    assertions += 4;
    await ctx.close();
  } else if (phase === "licensed-main") {
    // 1. Cashier Sign-In & Role-Aware Navigation (fail-closed)
    {
      const ctx = await browser.newContext({ viewport: { width: 1366, height: 768 } });
      const page = await ctx.newPage();
      page.setDefaultTimeout(60000);
      await page.goto(`${baseUrl}/cafe/r3`, { waitUntil: "networkidle" });
      await expect(page.getByRole("heading", { name: /Salryn Cafe Sign In/i })).toBeVisible();
      await page.getByLabel("Username").fill(cashierUser);
      await page.getByLabel("Password").fill(cashierPass);
      await page.getByRole("button", { name: "Sign In" }).click();

      await expect(page.getByRole("heading", { name: "POS Counter" })).toBeVisible();
      await expect(page.getByRole("button", { name: "Cash & Shifts" })).toBeVisible();
      await expect(page.getByRole("button", { name: "Reports & COGS" })).toHaveCount(0);
      await expect(page.getByRole("button", { name: "Inventory" })).toHaveCount(0);
      await expect(page.getByRole("button", { name: "Users & Roles" })).toHaveCount(0);
      assertions += 5;

      await page.getByRole("button", { name: "Sign Out", exact: true }).click();
      await expect(page.getByRole("heading", { name: /Salryn Cafe Sign In/i })).toBeVisible();
      assertions += 1;
      await ctx.close();
    }

    // 2. Owner Full Real Operator UI Workflow across C1–C11 (ZERO page.request shortcuts!)
    {
      const ctx = await browser.newContext({ viewport: { width: 1366, height: 768 } });
      const page = await ctx.newPage();
      page.setDefaultTimeout(60000);
      const pageErrors = [];
      page.on("pageerror", (err) => pageErrors.push(err.message));

      await page.goto(`${baseUrl}/cafe/r3`, { waitUntil: "networkidle" });
      await page.getByLabel("Username").fill(ownerUser);
      await page.getByLabel("Password").fill(ownerPass);
      await page.getByRole("button", { name: "Sign In" }).click();
      await expect(page.getByRole("heading", { name: /Today — Live Operations Overview/i })).toBeVisible();
      await runAxe(page, "desktop-today");
      await page.screenshot({ path: path.join(root, "r3-unified-desktop-1366.png"), fullPage: true });
      assertions += 2;

      const nav = page.getByRole("navigation", { name: "Cafe sections" });

      // --- C7: Configure Customer Receipt & Kitchen Print Stations via R3 UI ---
      await ensureAdminSectionExpanded(nav);
      await nav.getByRole("button", { name: "Print Stations" }).click();
      await expect(page.getByRole("heading", { name: /Print Stations/i })).toBeVisible();
      await page.getByLabel("Station Name").fill("Counter Thermal 80mm");
      await page.getByLabel("Station Kind").selectOption("CustomerReceipt");
      await page.getByRole("button", { name: "Add Print Station" }).click();
      await expect(page.getByText("Counter Thermal 80mm").first()).toBeVisible();
      assertions += 1;

      // --- C6: Cash & Shifts: Open Shift -> CashIn -> OperationalExpense -> Blind Close -> Auditor Variance Review ---
      await nav.getByRole("button", { name: "Cash & Shifts" }).click();
      await expect(page.getByRole("heading", { name: /Cash Drawer & Shift Reconciliation/i })).toBeVisible();
      await page.getByLabel("Opening Cash Float").fill("1000.00");
      await page.getByRole("button", { name: "Open Shift" }).click();
      await expect(page.getByRole("button", { name: "Close Shift Blind" })).toBeVisible();

      await page.getByLabel("Movement Amount").fill("50.00");
      await page.getByLabel("Movement Reason").fill("Unified proof float top-up");
      await page.getByRole("button", { name: "Record Cash Movement" }).click();
      await expect(page.getByText(/CashIn movement recorded/i).first()).toBeVisible();

      // C6 Operational Expense (25.00 PHP) via /api/cafe/cash/movements
      await page.getByLabel("C6 Movement Type").selectOption("OperationalExpense");
      await page.getByLabel("C6 Amount").fill("25.00");
      await page.getByLabel("C6 Reason").fill("Bag of ice for cold brew");
      await page.getByRole("button", { name: "Post C6 Cash / Expense" }).click();
      await expect(page.getByText(/C6 OperationalExpense recorded/i).first()).toBeVisible();

      // Blind Close with -30.00 variance (expected 1025.00 -> counted 995.00)
      await page.getByLabel("Physical Counted Cash").fill("995.00");
      await page.getByLabel("Shift Close Note").fill("Unified blind close variance");
      await page.getByRole("checkbox").check();
      await page.getByRole("button", { name: "Close Shift Blind" }).click();
      await expect(page.getByRole("button", { name: "Open Shift" })).toBeVisible();
      assertions += 4;

      // Independent Auditor acknowledges variance
      await page.getByRole("button", { name: "Sign Out", exact: true }).click();
      await page.getByLabel("Username").fill(auditorUser);
      await page.getByLabel("Password").fill(auditorPass);
      await page.getByRole("button", { name: "Sign In" }).click();
      await expect(page.getByRole("heading", { name: /Today — Live Operations Overview/i })).toBeVisible();

      await nav.getByRole("button", { name: "Cash & Shifts" }).click();
      await page.getByRole("tab", { name: /Closed History & Variance Review/i }).click();
      await expect(page.getByText("PendingReview").first()).toBeVisible();
      await page.getByLabel("Shift Pending Review").selectOption({ index: 1 });
      await page.getByLabel("Independent Review Reason").fill("Independent auditor verified -30 PHP variance");
      await page.getByRole("button", { name: "Acknowledge Variance" }).click();
      await expect(page.getByText(/Variance acknowledged/i).first()).toBeVisible();
      assertions += 2;

      // Sign back in as Owner and re-open shift for POS sales, refunds, and cash deposits
      await page.getByRole("button", { name: "Sign Out", exact: true }).click();
      await page.getByLabel("Username").fill(ownerUser);
      await page.getByLabel("Password").fill(ownerPass);
      await page.getByRole("button", { name: "Sign In" }).click();
      await expect(page.getByRole("heading", { name: /Today — Live Operations Overview/i })).toBeVisible();

      await nav.getByRole("button", { name: "Cash & Shifts" }).click();
      await page.getByLabel("Opening Cash Float").fill("500.00");
      await page.getByRole("button", { name: "Open Shift" }).click();
      await expect(page.getByRole("button", { name: "Close Shift Blind" })).toBeVisible();

      // --- C4 / C5 / C7: REAL POS UI Journeys (Cash+Discount+ServiceFee+Print/Reprint, AR, Partial Cash+AR) ---
      let posOrderFetchCount = 0;
      const countOrdersReq = (req) => {
        if (req.url().endsWith("/api/cafe/orders")) posOrderFetchCount += 1;
      };
      page.on("request", countOrdersReq);
      await nav.getByRole("button", { name: "POS Counter" }).click();
      await expect(page.getByRole("heading", { name: "POS Counter" })).toBeVisible();
      await page.waitForTimeout(1000);
      page.off("request", countOrdersReq);
      assert(posOrderFetchCount <= 4, `Expected bounded /api/cafe/orders fetches on POS Counter, observed ${posOrderFetchCount}`);

      // Helper: add menu item (handling Variant/Modifier config dialog) and advance one POS order to Ready through real UI clicks
      async function addCoffeeToDraft() {
        await page.getByRole("button", { name: /C4 Coffee/i }).first().click();
        const addToTicketBtn = page.getByRole("button", { name: "Add to Ticket" });
        if ((await addToTicketBtn.count()) > 0) {
          await addToTicketBtn.click();
        }
      }

      async function createAndReadyPosOrder(refText) {
        await addCoffeeToDraft();
        const refInput = page.getByPlaceholder(/Table 4|Takeout name/i);
        if ((await refInput.count()) > 0) {
          await refInput.first().fill(refText);
        }
        await page.getByRole("button", { name: "Save order" }).click();
        await expect(page.getByRole("button", { name: "Send to preparation" })).toBeVisible();
        await page.getByRole("button", { name: "Send to preparation" }).click();
        await expect(page.getByRole("button", { name: "Mark preparing" })).toBeVisible();
        await page.getByRole("button", { name: "Mark preparing" }).click();
        await expect(page.getByRole("button", { name: "Mark ready" })).toBeVisible();
        await page.getByRole("button", { name: "Mark ready" }).click();
        await expect(page.getByRole("button", { name: "Complete payment" })).toBeVisible();
      }

      // Sale 1: Cash + Order Discount (0.50) + Service Fee (1.00) + Original Receipt Print + Reprint!
      await createAndReadyPosOrder("POS-CASH-1");
      await page.getByLabel("Order discount (PHP)").fill("0.50");
      await page.getByLabel("Discount reason").fill("Regular customer discount");
      await page.getByLabel("Service fee (PHP)").fill("1.00");
      await page.getByLabel("Cash amount (PHP)").fill("10.00");
      await page.getByRole("button", { name: "Complete payment" }).click();
      await expect(page.getByText(/Payment confirmed/i).first()).toBeVisible();
      await page.getByRole("button", { name: "Print Receipt", exact: true }).click();
      await page.waitForTimeout(400);
      await page.getByRole("button", { name: "Reprint Receipt", exact: true }).click();
      await page.waitForTimeout(400);
      await page.screenshot({ path: path.join(root, "r3-unified-pos-1366.png"), fullPage: true });
      await page.getByRole("button", { name: "Start another order" }).click();
      assertions += 4;

      // Sale 2: Accounts Receivable (Utang)
      await createAndReadyPosOrder("POS-AR-2");
      await page.getByLabel("Tender").selectOption("AccountsReceivable");
      await page.getByLabel("Customer").selectOption({ label: "Maria Santos" });
      await page.getByRole("button", { name: "Complete payment" }).click();
      await expect(page.getByText(/Payment confirmed/i).first()).toBeVisible();
      await page.getByRole("button", { name: "Start another order" }).click();
      assertions += 2;

      // Sale 3: Partial Cash (2.00) + AR (3.00)
      await createAndReadyPosOrder("POS-PARTIAL-3");
      await page.getByLabel("Tender").selectOption("Partial");
      await page.getByLabel("Cash amount (PHP)").fill("2.00");
      await page.getByLabel("Customer").selectOption({ label: "Maria Santos" });
      await page.getByRole("button", { name: "Complete payment" }).click();
      await expect(page.getByText(/Payment confirmed/i).first()).toBeVisible();
      await page.getByRole("button", { name: "Start another order" }).click();
      assertions += 2;

      // --- C5: Customers & AR Cash Collection + Full R3 Refund Request -> Approve -> Commit ---
      await nav.getByRole("button", { name: "Customers & AR" }).click();
      await expect(page.getByRole("heading", { name: /Customers, Accounts Receivable/i })).toBeVisible();
      await page.getByLabel("Cash Collection Amount").fill("1.00");
      await page.getByLabel("Collection Note").fill("Counter partial Utang payment");
      await page.getByRole("button", { name: "Record Cash Collection" }).click();
      await expect(page.getByText(/Cash AR collection recorded/i).first()).toBeVisible();

      // Request -> Approve -> Commit Cash Refund directly inside R3 on the Full-Cash sale (SLRN-2026-000001)!
      const fullCashOptionValue = await page.getByLabel("Refundable Sale").evaluate((sel) => {
        const opt = Array.from(sel.options).find((o) => /SLRN-\d+-000001|Cash ₱5\.50/.test(o.textContent || ""));
        return opt ? opt.value : sel.options[sel.options.length - 1]?.value || "";
      });
      if (fullCashOptionValue) {
        await page.getByLabel("Refundable Sale").selectOption(fullCashOptionValue);
      }
      await page.getByLabel("Refund Request Reason").fill("Customer requested refund in R3 UI");
      await page.getByRole("button", { name: "Request Refund" }).click();
      await expect(page.getByText(/Refund requested/i).first()).toBeVisible();
      await page.getByRole("button", { name: "Approve Refund" }).first().click();
      await expect(page.getByText(/Approved/i).first()).toBeVisible();
      await page.getByRole("button", { name: "Commit Cash Refund" }).first().click();
      await expect(page.getByText(/committed/i).first()).toBeVisible();
      assertions += 4;

      // --- C8: Order-Bound Delivery 100% through REAL R3 UI (NO page.request!) ---
      await nav.getByRole("button", { name: "POS Counter" }).click();
      await addCoffeeToDraft();
      await page.getByRole("button", { name: "Save order" }).click();
      await expect(page.getByRole("button", { name: "Send to preparation" })).toBeVisible();

      await nav.getByRole("button", { name: "Deliveries" }).click();
      await expect(page.getByRole("heading", { name: /Bookings & Order-Bound Deliveries/i })).toBeVisible();
      await page.getByLabel("Recipient Name").fill("Elena Cruz");
      await page.getByLabel("Delivery Address").fill("42 Ayala Ave, Makati");
      await page.getByLabel("Delivery Fee").fill("50.00");
      await page.getByRole("button", { name: "Create Delivery" }).click();
      await expect(page.getByText(/Delivery created/i).first()).toBeVisible();

      // Revise Pending Delivery fee from 50.00 -> 60.00 via UI!
      await page.getByLabel("Pending Delivery to Revise").selectOption({ index: 1 });
      await page.getByLabel("Revised Recipient Name").fill("Elena Cruz Revised");
      await page.getByLabel("Revised Delivery Address").fill("88 Paseo de Roxas, Makati");
      await page.getByLabel("Revised Delivery Fee").fill("60.00");
      await page.getByRole("button", { name: "Save Delivery Revision" }).click();
      await expect(page.getByText(/revised to v2/i).first()).toBeVisible();

      // Open that Takeout Order from Orders Board -> advance to Ready -> POS auto-binds 60.00 Delivery fee -> Complete Cash 65.00!
      await nav.getByRole("button", { name: "Orders" }).click();
      await expect(page.getByRole("heading", { name: "Orders Board" })).toBeVisible();
      await page.getByRole("group", { name: "Filter orders by status" }).getByRole("button", { name: "Open", exact: true }).click();
      await page.locator("main table").getByRole("button", { name: "Open", exact: true }).first().click();
      await expect(page.getByRole("button", { name: "Send to preparation" })).toBeVisible();
      await page.getByRole("button", { name: "Send to preparation" }).click();
      await expect(page.getByRole("button", { name: "Mark preparing" })).toBeVisible();
      await page.getByRole("button", { name: "Mark preparing" }).click();
      await expect(page.getByRole("button", { name: "Mark ready" })).toBeVisible();
      await page.getByRole("button", { name: "Mark ready" }).click();
      await expect(page.getByText(/Attached Pending Delivery/i)).toBeVisible();
      await page.getByLabel("Cash amount (PHP)").fill("65.00");
      await page.getByRole("button", { name: "Complete payment" }).click();
      await expect(page.getByText(/Payment confirmed/i).first()).toBeVisible();

      // Return to Deliveries -> Dispatch -> Mark Delivered via UI!
      await nav.getByRole("button", { name: "Deliveries" }).click();
      await page.getByRole("button", { name: "Dispatch" }).first().click();
      await expect(page.getByText("Dispatched").first()).toBeVisible();
      await page.getByRole("button", { name: "Mark Delivered" }).first().click();
      await expect(page.getByText("Delivered").first()).toBeVisible();
      assertions += 6;

      // --- C9: Bookings Create -> Revise -> Confirm -> 2 Cash Deposits -> Reload -> Partial Multi-Deposit Refund -> Ready -> Start Fulfillment + Second Booking Cancel ---
      await nav.getByRole("button", { name: "Bookings & Cart" }).click();
      await page.getByLabel("Event Name").fill("Unified Cafe Launch Event");
      await page.getByLabel("Deposit Requirement").fill("1.00");
      await page.getByRole("button", { name: "Create Booking" }).click();
      await expect(page.getByText(/Booking created/i).first()).toBeVisible();

      // Revise Booking #1 before confirming
      await page.getByLabel("Revised Event Name").fill("Unified Cafe Launch Event v2");
      await page.getByRole("button", { name: "Save Booking Revision" }).click();
      await expect(page.getByText(/revised to v2/i).first()).toBeVisible();

      await page.getByRole("button", { name: "Confirm" }).first().click();
      await expect(page.getByText(/Confirmed/i).first()).toBeVisible();

      await page.getByLabel("Cash Deposit Amount").fill("2.00");
      await page.getByLabel("Deposit Reference").fill("UNIFIED-DEP-1");
      await page.getByRole("button", { name: "Receive Cash Deposit" }).click();
      await expect(page.getByText("UNIFIED-DEP-1").first()).toBeVisible();

      await page.getByLabel("Cash Deposit Amount").fill("2.00");
      await page.getByLabel("Deposit Reference").fill("UNIFIED-DEP-2");
      await page.getByRole("button", { name: "Receive Cash Deposit" }).click();
      await expect(page.getByText("UNIFIED-DEP-2").first()).toBeVisible();

      // Full page reload to prove historical deposit facts load strictly from Host SQLite!
      await page.reload({ waitUntil: "networkidle" });
      await expect(page.getByText("UNIFIED-DEP-1").first()).toBeVisible();
      await expect(page.getByText("UNIFIED-DEP-2").first()).toBeVisible();

      await page.getByLabel("Refund allocation for UNIFIED-DEP-1").fill("1.00");
      await page.getByLabel("Refund allocation for UNIFIED-DEP-2").fill("0.50");
      await page.getByLabel("Refund Reason").fill("Partial multi-deposit refund proof");
      await page.getByLabel("Refund Reference").fill("UNIFIED-REF-1");
      await page.getByRole("button", { name: "Commit Allocated Refund" }).click();
      await expect(page.getByText(/Allocated refund committed/i).first()).toBeVisible();

      // Mark Ready for Fulfillment -> Start Fulfillment (binds Order!)
      await page.getByRole("button", { name: "Ready for Fulfillment" }).first().click();
      await expect(page.getByRole("button", { name: "Start Fulfillment" }).first()).toBeVisible();
      await page.getByRole("button", { name: "Start Fulfillment" }).first().click();
      await expect(page.getByText(/Booking fulfillment started/i).first()).toBeVisible();

      // Create Booking #2 -> Cancel Booking via UI!
      await page.getByLabel("Event Name", { exact: true }).fill("Cancelled Coffee Cart Event");
      await page.getByLabel("Deposit Requirement", { exact: true }).fill("1.00");
      await page.getByRole("button", { name: "Create Booking" }).click();
      await expect(page.getByText(/Booking created/i).first()).toBeVisible();
      await page.getByRole("button", { name: "Cancel Booking" }).last().click();
      await expect(page.getByText(/cancelled/i).first()).toBeVisible();
      assertions += 8;

      await page.evaluate(() => window.scrollTo(0, 0));
      await page.screenshot({ path: path.join(root, "r3-unified-bookings-1366.png"), fullPage: true });

      // --- C2 Recipes & C3 Inventory + Unknown-Outcome Reload Recovery + Cross-Tab Concurrency ---
      await nav.getByRole("button", { name: "Recipes & BOM" }).click();
      await expect(page.getByRole("heading", { name: /Recipes & BOM Costing/i })).toBeVisible();
      await page.getByRole("button", { name: "Publish Recipes" }).click();
      await expect(page.getByText(/Recipes published/i).first()).toBeVisible();

      await nav.getByRole("button", { name: "Inventory" }).click();
      await expect(page.getByRole("heading", { name: /Inventory & Stock Control/i })).toBeVisible();
      await page.getByRole("tab", { name: /Manual Adjustment/i }).click();

      // Simulate unknown network drop on first POST /api/cafe/inventory/adjust -> verify durable intent survives reload -> retry succeeds!
      let abortAdjustOnce = true;
      await page.route("**/api/cafe/inventory/adjust", async (route) => {
        if (abortAdjustOnce) {
          abortAdjustOnce = false;
          await route.abort("failed");
        } else {
          await route.continue();
        }
      });
      await page.getByLabel("Signed Quantity Delta").fill("3");
      await page.getByLabel("Adjustment Reason").fill("Gate D unknown-outcome reload proof");
      await page.getByRole("button", { name: /Commit Adjustment/i }).click();
      await expect(page.getByRole("button", { name: /Retry Exact Adjustment/i })).toBeVisible();
      const storedBeforeReload = await page.evaluate(() =>
        window.localStorage.getItem("salryn.cafe.r3.pending.inventory-adjust.v2"),
      );
      assert(storedBeforeReload && storedBeforeReload.includes("operationId"), "Expected v2 durable adjustment intent stored before reload");

      await page.reload({ waitUntil: "networkidle" });
      await page.getByRole("tab", { name: /Manual Adjustment/i }).click();
      await expect(page.getByRole("button", { name: /Retry Exact Adjustment/i })).toBeVisible();
      await page.getByRole("button", { name: /Retry Exact Adjustment/i }).click();
      await expect(page.getByRole("button", { name: /Retry Exact Adjustment/i })).toHaveCount(0);
      await page.unroute("**/api/cafe/inventory/adjust");
      assertions += 4;

      // Cross-Tab Concurrency Proof: Tab 1 holds an in-flight delayed adjustment while Tab 2 attempts concurrent commit -> Tab 2 MUST be fenced!
      const page2 = await ctx.newPage();
      page2.setDefaultTimeout(60000);
      await page2.goto(`${baseUrl}/cafe/r3/inventory`, { waitUntil: "networkidle" });
      await page2.getByRole("tab", { name: /Manual Adjustment/i }).click();

      let releaseTab1;
      const tab1Gate = new Promise((r) => { releaseTab1 = r; });
      await page.route("**/api/cafe/inventory/adjust", async (route) => {
        await tab1Gate;
        await route.continue();
      });
      await page.getByLabel("Signed Quantity Delta").fill("2");
      await page.getByLabel("Adjustment Reason").fill("Tab 1 exclusive lease");
      const tab1Click = page.getByRole("button", { name: /Commit Adjustment/i }).click();
      await page.waitForTimeout(350);

      // Tab 2 tries to dispatch while Tab 1 holds the exclusive Web Lock / storage lease
      await page2.getByLabel("Signed Quantity Delta").fill("4");
      await page2.getByLabel("Adjustment Reason").fill("Tab 2 competing write");
      await page2.getByRole("button", { name: /Commit Adjustment/i }).click();
      await expect(page2.getByText(/Another browser tab|already in flight|unresolved outcome/i).first()).toBeVisible();
      releaseTab1();
      await tab1Click;
      await page.unroute("**/api/cafe/inventory/adjust");
      await page2.close();
      assertions += 2;

      // --- C10: Staff Attendance & Incident Create + Resolve ---
      await nav.getByRole("button", { name: "Staff & Incidents" }).click();
      await expect(page.getByRole("heading", { name: /Staff Attendance & Operational Incidents/i })).toBeVisible();
      await page.getByRole("button", { name: "Clock In" }).click();
      await expect(page.getByText(/Recorded ClockIn/i).first()).toBeVisible();
      await page.getByLabel("Incident Title").fill("Gate D Grinder Calibration");
      await page.getByLabel("Incident Description").fill("Dialed in espresso burr spacing");
      await page.getByRole("button", { name: "Record Incident" }).click();
      await expect(page.getByText("Gate D Grinder Calibration").first()).toBeVisible();
      await page.getByRole("button", { name: "Resolve Incident" }).first().click();
      await expect(page.getByText("Resolved").first()).toBeVisible();
      assertions += 3;

      // --- C11: Reports & COGS + axe-core ---
      await nav.getByRole("button", { name: "Reports & COGS" }).click();
      await expect(page.getByRole("heading", { name: /Reports, COGS & Profitability/i })).toBeVisible();
      await expect(page.getByText(/Contribution Waterfall/i)).toBeVisible();
      await runAxe(page, "desktop-reports");
      assertions += 3;

      // --- Tablet (1024x768) Screenshot & No Horizontal Overflow ---
      await page.setViewportSize({ width: 1024, height: 768 });
      await page.waitForTimeout(250);
      await page.screenshot({ path: path.join(root, "r3-unified-tablet-1024.png"), fullPage: true });
      const tabletOverflow = await page.evaluate(
        () => document.documentElement.scrollWidth > document.documentElement.clientWidth + 2,
      );
      assert(!tabletOverflow, "Unexpected horizontal overflow at tablet 1024px");
      assertions += 1;

      // --- Mobile (390x844) Screenshot + Open Mobile Navigation Drawer ---
      await page.setViewportSize({ width: 390, height: 844 });
      await page.waitForTimeout(250);
      await page.screenshot({ path: path.join(root, "r3-unified-mobile-390.png"), fullPage: true });
      await page.getByRole("button", { name: "Open navigation menu" }).click();
      await page.waitForTimeout(250);
      await page.screenshot({ path: path.join(root, "r3-unified-mobile-drawer-390.png"), fullPage: false });
      const mobileOverflow = await page.evaluate(
        () => document.documentElement.scrollWidth > document.documentElement.clientWidth + 2,
      );
      assert(!mobileOverflow, "Unexpected horizontal overflow at mobile 390px");
      assertions += 2;

      assert(pageErrors.length === 0, `Uncaught page errors: ${pageErrors.join("; ")}`);
      await ctx.close();
    }
  } else if (phase === "post-restart") {
    // Prove state persisted cleanly across full Host process restart!
    const ctx = await browser.newContext({ viewport: { width: 1366, height: 768 } });
    const page = await ctx.newPage();
    page.setDefaultTimeout(60000);

    await page.goto(`${baseUrl}/cafe/r3`, { waitUntil: "networkidle" });
    await page.getByLabel("Username").fill(ownerUser);
    await page.getByLabel("Password").fill(ownerPass);
    await page.getByRole("button", { name: "Sign In" }).click();
    await expect(page.getByRole("heading", { name: /Today — Live Operations Overview/i })).toBeVisible();

    const nav = page.getByRole("navigation", { name: "Cafe sections" });
    await nav.getByRole("button", { name: "Bookings & Cart" }).click();
    await expect(page.getByText("Cancelled").first()).toBeVisible();
    await expect(page.getByText("UNIFIED-DEP-1").first()).toBeVisible();
    await expect(page.getByText("UNIFIED-DEP-2").first()).toBeVisible();

    await nav.getByRole("button", { name: "Deliveries" }).click();
    await expect(page.getByText("Delivered").first()).toBeVisible();

    await nav.getByRole("button", { name: "Customers & AR" }).click();
    await expect(page.getByText("Committed").first()).toBeVisible();

    await nav.getByRole("button", { name: "Staff & Incidents" }).click();
    await expect(page.getByText("Gate D Grinder Calibration").first()).toBeVisible();
    assertions += 6;
    await ctx.close();
  }

  console.log(`PASS r3-unified-browser phase=${phase} (${assertions} assertions)`);
} finally {
  await browser.close();
}
