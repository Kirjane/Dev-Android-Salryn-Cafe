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
  } else if (phase === "licensed-main") {
    // 1. Cashier Sign-In & Role-Aware Navigation
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
      // Owner/Admin routes MUST be hidden for Cashier
      await expect(page.getByRole("button", { name: "Reports & COGS" })).toHaveCount(0);
      await expect(page.getByRole("button", { name: "Inventory" })).toHaveCount(0);
      await expect(page.getByRole("button", { name: "Users & Roles" })).toHaveCount(0);
      assertions += 5;

      // Sign Out returns cleanly to Sign-In form
      await page.getByRole("button", { name: "Sign Out", exact: true }).click();
      await expect(page.getByRole("heading", { name: /Salryn Cafe Sign In/i })).toBeVisible();
      assertions += 1;
      await ctx.close();
    }

    // 2. Owner Full Unified Workflow: Shift Open -> CashIn -> Blind Close -> Auditor Variance Review -> Bookings Multi-Deposit Reload Refund -> Delivery -> Multi-Viewport Screenshots + axe-core
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
      assertions += 1;

      // Capture Desktop Today Overview screenshot + axe-core
      await runAxe(page, "desktop-today");
      await page.screenshot({ path: path.join(root, "r3-unified-desktop-1366.png"), fullPage: true });
      assertions += 1;

      const nav = page.getByRole("navigation", { name: "Cafe sections" });

      // --- Cash & Shifts: Open Shift -> CashIn -> Blind Close (with variance) ---
      await nav.getByRole("button", { name: "Cash & Shifts" }).click();
      await expect(page.getByRole("heading", { name: /Cash Drawer & Shift Reconciliation/i })).toBeVisible();
      await page.getByLabel("Opening Cash Float").fill("1000.00");
      await page.getByRole("button", { name: "Open Shift" }).click();
      await expect(page.getByRole("button", { name: "Close Shift Blind" })).toBeVisible();

      await page.getByLabel("Movement Amount").fill("50.00");
      await page.getByLabel("Movement Reason").fill("Unified proof float top-up");
      await page.getByRole("button", { name: "Record Cash Movement" }).click();
      await expect(page.getByText(/CashIn movement recorded/i).first()).toBeVisible();

      // Blind Close with -30.00 variance (expected 1050.00, counted 1020.00)
      await page.getByLabel("Physical Counted Cash").fill("1020.00");
      await page.getByLabel("Shift Close Note").fill("Unified blind close variance");
      await page.getByRole("checkbox").check();
      await page.getByRole("button", { name: "Close Shift Blind" }).click();
      await expect(page.getByRole("button", { name: "Open Shift" })).toBeVisible();
      assertions += 3;

      // Sign out -> Sign in as Independent Auditor -> Acknowledge Variance
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

      // Sign out -> Sign back in as Owner
      await page.getByRole("button", { name: "Sign Out", exact: true }).click();
      await page.getByLabel("Username").fill(ownerUser);
      await page.getByLabel("Password").fill(ownerPass);
      await page.getByRole("button", { name: "Sign In" }).click();
      await expect(page.getByRole("heading", { name: /Today — Live Operations Overview/i })).toBeVisible();

      // Re-open shift so Cash booking deposits can post
      await nav.getByRole("button", { name: "Cash & Shifts" }).click();
      await page.getByLabel("Opening Cash Float").fill("500.00");
      await page.getByRole("button", { name: "Open Shift" }).click();
      await expect(page.getByRole("button", { name: "Close Shift Blind" })).toBeVisible();

      // --- Bookings: Create -> Durable Confirm -> 2 Historical Cash Deposits -> Reload -> Partial Multi-Deposit Refund ---
      await nav.getByRole("button", { name: "Bookings & Cart" }).click();
      await expect(page.getByRole("heading", { name: /Bookings & Order-Bound Deliveries/i })).toBeVisible();

      await page.getByLabel("Event Name").fill("Unified Cafe Launch Event");
      await page.getByLabel("Deposit Requirement").fill("1.00");
      await page.getByRole("button", { name: "Create Booking" }).click();
      await expect(page.getByText(/Booking created/i).first()).toBeVisible();

      await page.getByRole("button", { name: "Confirm" }).first().click();
      await expect(page.getByText(/Confirmed/i).first()).toBeVisible();

      // Receive 2 separate Cash deposits (₱2.00 + ₱2.00 against ₱5.00 quoted total)
      await page.getByLabel("Cash Deposit Amount").fill("2.00");
      await page.getByLabel("Deposit Reference").fill("UNIFIED-DEP-1");
      await page.getByRole("button", { name: "Receive Cash Deposit" }).click();
      await expect(page.getByText("UNIFIED-DEP-1").first()).toBeVisible();

      await page.getByLabel("Cash Deposit Amount").fill("2.00");
      await page.getByLabel("Deposit Reference").fill("UNIFIED-DEP-2");
      await page.getByRole("button", { name: "Receive Cash Deposit" }).click();
      await expect(page.getByText("UNIFIED-DEP-2").first()).toBeVisible();

      // FULL BROWSER RELOAD: prove historical deposit facts load strictly from Host SQLite!
      await page.reload({ waitUntil: "networkidle" });
      await expect(page.getByText("UNIFIED-DEP-1").first()).toBeVisible();
      await expect(page.getByText("UNIFIED-DEP-2").first()).toBeVisible();

      // Partial refund allocated across BOTH historical deposits (₱1.00 + ₱0.50)
      await page.getByLabel("Refund allocation for UNIFIED-DEP-1").fill("1.00");
      await page.getByLabel("Refund allocation for UNIFIED-DEP-2").fill("0.50");
      await page.getByLabel("Refund Reason").fill("Partial multi-deposit refund proof");
      await page.getByLabel("Refund Reference").fill("UNIFIED-REF-1");
      await page.getByRole("button", { name: "Commit Allocated Refund" }).click();
      await expect(page.getByText(/Allocated refund committed/i).first()).toBeVisible();
      assertions += 5;

      await page.evaluate(() => window.scrollTo(0, 0));
      await page.screenshot({ path: path.join(root, "r3-unified-bookings-1366.png"), fullPage: true });

      // --- Order-Bound Delivery: Create Takeout Order -> Create Delivery -> Durable Dispatch -> Resolve Delivered ---
      // Seed an active Takeout order via authenticated session API so we can bind a delivery cleanly in the UI.
      const menuRes = await page.request.get(`${baseUrl}/api/menu/publication/current`);
      assert(menuRes.ok(), "Published menu fetch failed");
      const menuBody = await menuRes.json();
      const pub = menuBody.publication ?? menuBody;
      const firstItem = pub.items[0];
      const orderId = crypto.randomUUID();
      const saveOrd = await page.request.post(`${baseUrl}/api/cafe/orders/revision`, {
        data: {
          operationId: crypto.randomUUID(),
          orderId,
          expectedVersion: 0,
          serviceMode: "Takeout",
          serviceReference: "DELIV-UNIFIED-1",
          lines: [
            {
              orderLineId: crypto.randomUUID(),
              productId: firstItem.productId,
              variantId: null,
              modifierOptionIds: [],
              quantityX1000: 1000,
              lineNote: null,
              displayOrder: 0,
            },
          ],
        },
      });
      assert(saveOrd.ok(), `Takeout order creation failed: ${saveOrd.status()}`);

      await nav.getByRole("button", { name: "Deliveries" }).click();
      await page.getByRole("button", { name: "Refresh Live Data" }).click();
      await page.getByLabel("Recipient Name").fill("Elena Cruz");
      await page.getByLabel("Delivery Address").fill("42 Ayala Ave, Makati");
      await page.getByLabel("Delivery Fee").fill("50.00");
      await page.getByRole("button", { name: "Create Delivery" }).click();
      await expect(page.getByText(/Delivery created/i).first()).toBeVisible();

      // Advance bound Takeout order through canonical Submit -> Preparing -> Ready -> Complete (item 500 + delivery fee 5000 = 5500 cents)
      const subOrd = await page.request.post(`${baseUrl}/api/cafe/orders/${orderId}/submit`, {
        data: { operationId: crypto.randomUUID(), expectedVersion: 1 },
      });
      assert(subOrd.ok(), `Takeout submit failed: ${subOrd.status()} ${await subOrd.text()}`);
      const prepOrd = await page.request.post(`${baseUrl}/api/cafe/orders/${orderId}/preparation`, {
        data: { operationId: crypto.randomUUID(), expectedVersion: 2, targetState: "Preparing" },
      });
      assert(prepOrd.ok(), `Takeout Preparing failed: ${prepOrd.status()} ${await prepOrd.text()}`);
      const readyOrd = await page.request.post(`${baseUrl}/api/cafe/orders/${orderId}/preparation`, {
        data: { operationId: crypto.randomUUID(), expectedVersion: 3, targetState: "Ready" },
      });
      assert(readyOrd.ok(), `Takeout Ready failed: ${readyOrd.status()} ${await readyOrd.text()}`);

      const compOrd = await page.request.post(`${baseUrl}/api/cafe/orders/${orderId}/complete`, {
        data: {
          completionOperationId: crypto.randomUUID(),
          saleCommittedOperationId: crypto.randomUUID(),
          financialDecisionOperationId: crypto.randomUUID(),
          financialDecisionId: crypto.randomUUID(),
          expectedVersion: 4,
          customerId: null,
          fees: [{ feeId: crypto.randomUUID(), feeType: "Delivery", displayLabel: "Delivery Fee", amountCents: 5000 }],
          payments: [{ method: "Cash", amountCents: 5500 }],
        },
      });
      assert(compOrd.ok(), `Bound Takeout order completion failed: ${compOrd.status()} ${await compOrd.text()}`);

      await page.getByRole("button", { name: "Dispatch" }).first().click();
      await expect(page.getByText("Dispatched").first()).toBeVisible();

      await page.getByRole("button", { name: "Mark Delivered" }).first().click();
      await expect(page.getByText("Delivered").first()).toBeVisible();
      assertions += 3;

      // --- POS Counter Stability Check (guards against P0 infinite fetch/render loop) ---
      let posOrderFetchCount = 0;
      const countOrdersReq = (req) => {
        if (req.url().endsWith("/api/cafe/orders")) posOrderFetchCount += 1;
      };
      page.on("request", countOrdersReq);
      await nav.getByRole("button", { name: "POS Counter" }).click();
      await expect(page.getByRole("heading", { name: "POS Counter" })).toBeVisible();
      await page.waitForTimeout(1200);
      page.off("request", countOrdersReq);
      assert(
        posOrderFetchCount <= 4,
        `Expected bounded /api/cafe/orders fetches on POS Counter, observed ${posOrderFetchCount} (possible render loop)`,
      );
      assertions += 1;

      // --- Inventory & Reports Inside Unified Shell ---
      await nav.getByRole("button", { name: "Inventory" }).click();
      await expect(page.getByRole("heading", { name: /Inventory & Stock Control/i })).toBeVisible();
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

    await page.goto(`${baseUrl}/cafe/r3/bookings`, { waitUntil: "networkidle" });
    await page.getByLabel("Username").fill(ownerUser);
    await page.getByLabel("Password").fill(ownerPass);
    await page.getByRole("button", { name: "Sign In" }).click();
    await expect(page.getByRole("heading", { name: /Today — Live Operations Overview/i })).toBeVisible();

    const nav = page.getByRole("navigation", { name: "Cafe sections" });
    await nav.getByRole("button", { name: "Bookings & Cart" }).click();
    await expect(page.getByText("Confirmed").first()).toBeVisible();
    await expect(page.getByText("UNIFIED-DEP-1").first()).toBeVisible();
    await expect(page.getByText("UNIFIED-DEP-2").first()).toBeVisible();

    await nav.getByRole("button", { name: "Deliveries" }).click();
    await expect(page.getByText("Delivered").first()).toBeVisible();
    assertions += 4;
    await ctx.close();
  }

  console.log(`PASS r3-unified-browser phase=${phase} (${assertions} assertions)`);
} finally {
  await browser.close();
}
