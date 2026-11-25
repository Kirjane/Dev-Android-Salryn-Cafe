import { createRequire } from "node:module";
import { readFileSync } from "node:fs";
import path from "node:path";

const playwrightPkg = process.env.CAFE_LIVE_PLAYWRIGHT_PACKAGE_JSON;
const axePath = process.env.CAFE_LIVE_AXE_PATH;
const baseUrl = process.env.CAFE_R3_BASE_URL;
const expectedNonce = process.env.CAFE_R3_NONCE;
const expectedRoot = process.env.CAFE_R3_ROOT;
const ownerUser = process.env.CAFE_R3_OWNER_USER;
const ownerPass = process.env.CAFE_R3_OWNER_PASS;
const cashierUser = process.env.CAFE_R3_CASHIER_USER;
const cashierPass = process.env.CAFE_R3_CASHIER_PASS;

const require = createRequire(playwrightPkg);
const { chromium, expect } = require("@playwright/test");

function assert(cond, msg) {
  if (!cond) throw new Error(msg);
}

const browser = await chromium.launch({ headless: true });
let assertions = 0;

try {
  // Verify disposable Host identity via Vite proxy.
  const idRes = await fetch(`${baseUrl}/api/test/host-identity`);
  assert(idRes.ok, `Host identity probe failed: ${idRes.status}`);
  const idBody = await idRes.json();
  assert(idBody.testHostNonce === expectedNonce, "Host identity nonce mismatch");
  assert(
    String(idBody.databasePath ?? "").toLowerCase().startsWith(expectedRoot.toLowerCase()),
    "Host database must remain inside disposable root",
  );
  assertions += 2;

  // 1. Cashier RBAC fail-closed check.
  {
    const ctx = await browser.newContext({ viewport: { width: 1366, height: 768 } });
    const page = await ctx.newPage();
    page.setDefaultTimeout(60000);
    const login = await page.request.post(`${baseUrl}/api/auth/login`, {
      data: { username: cashierUser, password: cashierPass },
    });
    assert(login.ok(), `Cashier login failed: ${login.status()}`);
    await page.goto(`${baseUrl}/cafe/integration/r3/inventory`, { waitUntil: "domcontentloaded" });
    await expect(page.getByText(/Insufficient permissions/i)).toBeVisible();
    assertions += 1;
    await ctx.close();
  }

  // 2. Owner live INT-01 (unknown outcome -> reload -> exact operationId retry) + INT-02 (count draft -> finalize -> immutable) + axe-core + screenshot.
  {
    const ctx = await browser.newContext({ viewport: { width: 1366, height: 768 } });
    const page = await ctx.newPage();
    page.setDefaultTimeout(60000);
    const login = await page.request.post(`${baseUrl}/api/auth/login`, {
      data: { username: ownerUser, password: ownerPass },
    });
    assert(login.ok(), `Owner login failed: ${login.status()}`);

    await page.goto(`${baseUrl}/cafe/integration/r3/inventory`, { waitUntil: "domcontentloaded" });
    await expect(page.getByRole("heading", { name: "Inventory & Stock Control" })).toBeVisible();
    await expect(page.getByText("Espresso Beans").first()).toBeVisible();
    assertions += 2;

    // INT-01: Simulate network drop on first adjust POST, reload browser, verify pending intent survives, then retry exact operationId.
    await page.getByRole("tab", { name: "Manual Adjustment" }).click();
    let droppedOnce = false;
    await page.route("**/api/cafe/inventory/adjust", async (route) => {
      if (!droppedOnce) {
        droppedOnce = true;
        await route.abort("failed");
        return;
      }
      await route.continue();
    });

    await page.getByLabel("Signed Quantity Delta").fill("+2.5");
    await page.getByLabel("Adjustment Reason").fill("Browser proof shelf audit");
    await page.getByRole("button", { name: "Commit Adjustment" }).click();
    await expect(page.getByText(/Adjustment: pending request has an unresolved outcome/i)).toBeVisible();
    const pendingBeforeReload = await page.evaluate(() =>
      window.localStorage.getItem("salryn.cafe.r3.pending.inventory-adjust.v2"),
    );
    assert(Boolean(pendingBeforeReload), "Pending adjustment v2 record must be persisted in localStorage");
    const parsedPending = JSON.parse(pendingBeforeReload);
    assert(Boolean(parsedPending.hostIdentityKey), "Pending intent must bind hostIdentityKey");
    assert(Boolean(parsedPending.actorUserId), "Pending intent must bind actorUserId");
    const opIdBeforeReload = parsedPending.payload.operationId;
    assertions += 4;

    // Reload browser and verify exact operationId survived.
    await page.reload({ waitUntil: "domcontentloaded" });
    await page.getByRole("tab", { name: "Manual Adjustment" }).click();
    await expect(page.getByText(/Adjustment: pending request has an unresolved outcome/i)).toBeVisible();
    await expect(page.getByText(opIdBeforeReload)).toBeVisible();
    assertions += 2;

    // Retry exact adjustment -> succeeds against real Host!
    await page.getByRole("button", { name: "Retry Exact Adjustment" }).click();
    await expect(page.getByText(/Adjustment retry confirmed/i).first()).toBeVisible();
    const pendingAfterRetry = await page.evaluate(() =>
      window.localStorage.getItem("salryn.cafe.r3.pending.inventory-adjust.v2"),
    );
    assert(pendingAfterRetry === null, "Pending adjustment must clear after Host confirmation");
    assertions += 2;

    // INT-02: Physical Count Draft -> Finalize -> Immutable.
    await page.getByRole("tab", { name: "Physical Count" }).click();
    await page.getByLabel("Physical Counted Quantity").fill("95");
    await page.getByLabel("Count Notes").fill("Browser proof closing count");
    await page.getByRole("button", { name: "Save Count Draft" }).click();
    await expect(page.getByText(/Physical count draft saved/i).first()).toBeVisible();
    await expect(page.getByRole("button", { name: "Finalize Physical Count" })).toBeVisible();
    assertions += 2;

    await page.getByRole("button", { name: "Finalize Physical Count" }).click();
    await expect(page.getByText(/Physical count finalized/i).first()).toBeVisible();
    await expect(page.getByText(/Immutable — finalized at/i)).toBeVisible();
    await expect(page.getByRole("button", { name: "Finalize Physical Count" })).toHaveCount(0);
    assertions += 3;

    await page.screenshot({ path: path.join(expectedRoot, "r3-inventory-desktop-1366.png"), fullPage: true });

    // axe-core accessibility check (0 critical/serious violations).
    await page.addScriptTag({ content: readFileSync(axePath, "utf8") });
    const axeRes = await page.evaluate(async () => {
      const r = await window.axe.run();
      return r.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
    });
    assert(
      axeRes.length === 0,
      `axe-core critical/serious violations: ${axeRes.map((v) => `${v.id}:${v.help}`).join("; ")}`,
    );
    assertions += 1;

    await ctx.close();
  }

  console.log(`PASS R3 inventory browser proof (${assertions} assertions)`);
} finally {
  await browser.close();
}
