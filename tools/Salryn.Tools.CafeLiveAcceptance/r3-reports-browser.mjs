import { createRequire } from "node:module";
import { readFileSync, writeFileSync } from "node:fs";

const require = createRequire(process.env.CAFE_QA_PLAYWRIGHT_PACKAGE);
const { chromium, expect } = require("@playwright/test");
const axeSource = readFileSync(process.env.CAFE_QA_AXE_PATH, "utf8");

const url = process.env.CAFE_QA_URL;
const nonce = process.env.CAFE_QA_NONCE;
const root = process.env.CAFE_QA_ROOT;
const phase = process.env.CAFE_QA_PHASE;
const expected = JSON.parse(process.env.CAFE_QA_EXPECTED_JSON);

if (!/^http:\/\/127\.0\.0\.1:\d+$/.test(url ?? "") || !nonce) {
  throw new Error("Disposable loopback Vite proxy required.");
}

function formatPhp(cents) {
  return new Intl.NumberFormat("en-PH", {
    style: "currency",
    currency: "PHP",
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(cents / 100);
}

const browser = await chromium.launch();
const context = await browser.newContext({
  viewport: { width: 1366, height: 768 },
  acceptDownloads: true,
});
const page = await context.newPage();
page.setDefaultTimeout(60000);
page.setDefaultNavigationTimeout(90000);

const errors = [];
page.on("pageerror", (err) => errors.push(`pageerror: ${err.message}`));

let assertions = 0;
function assert(cond, msg) {
  if (!cond) throw new Error(msg);
  assertions++;
}

async function loginViaApi(username, password) {
  await context.request.post(`${url}/api/auth/logout`).catch(() => {});
  const res = await context.request.post(`${url}/api/auth/login`, {
    data: { username, password },
  });
  assert(res.status() === 200, `Login failed for ${username}: HTTP ${res.status()}`);
}

try {
  // Confirm Vite dev proxy reaches our exact disposable Host nonce.
  const identityRes = await context.request.get(`${url}/api/test/host-identity`);
  assert(identityRes.ok(), "Proxied /api/test/host-identity failed");
  const identity = await identityRes.json();
  assert(identity.testHostNonce === nonce, "Proxied Host nonce mismatch");
  assert(identity.databasePath.startsWith(root), "Host database must remain inside disposable root");

  if (phase === "unlicensed") {
    await page.goto(`${url}/cafe/integration/r3/reports`, { waitUntil: "domcontentloaded" });
    await expect(page.getByRole("heading", { name: "Reports, COGS & Profitability", exact: true })).toBeVisible({ timeout: 60000 });
    await expect(page.getByText(/Active Host license required/i)).toBeVisible({ timeout: 30000 });
    await expect(page.getByRole("button", { name: /Export CSV/i })).toBeDisabled();
    assertions += 3;
    await page.screenshot({ path: `${root}/r3-reports-unlicensed-failclosed.png`, fullPage: true });
    console.log(`PASS R3 reports browser unlicensed phase (${assertions} assertions)`);
  } else {
    // 1. Unauthenticated access must fail closed.
    await page.goto(`${url}/cafe/integration/r3/reports`, { waitUntil: "domcontentloaded" });
    await expect(page.getByText(/An authenticated operator with Reports\.View is required/i)).toBeVisible({ timeout: 60000 });
    await expect(page.getByRole("button", { name: /Export CSV/i })).toBeDisabled();
    assertions += 2;

    // 2. Cashier without Reports.View must fail closed.
    await loginViaApi(process.env.CAFE_QA_CASHIER_USER, process.env.CAFE_QA_CASHIER_PASS);
    await page.reload();
    await expect(page.getByText(/An authenticated operator with Reports\.View is required/i)).toBeVisible();
    assertions++;

    // 3. Reports-only actor without Receivables.View / Inventory.View:
    //    sees nonzero profitability + provisional banner + partial recipe cost + explicit Cash/AR redaction + low-stock warning.
    await loginViaApi(process.env.CAFE_QA_REPORTS_ONLY_USER, process.env.CAFE_QA_REPORTS_ONLY_PASS);
    await page.reload();
    await expect(page.getByText(/Provisional profitability — not final/i)).toBeVisible();
    await expect(page.getByText(formatPhp(expected.monthlyNetCents)).first()).toBeVisible();
    assertions += 2;

    await page.getByRole("tab", { name: /Recipe Costing \(2\)/i }).click();
    await expect(page.getByText("Espresso Base", { exact: true })).toBeVisible();
    await expect(page.getByText("Oat Latte Special", { exact: true })).toBeVisible();
    await expect(page.getByText(/\(partial\)/)).toBeVisible();
    assertions += 3;

    await page.getByRole("tab", { name: "Cash & AR", exact: true }).click();
    await expect(page.getByText("Unavailable or not authorized.")).toHaveCount(2);
    assertions++;
    await page.screenshot({ path: `${root}/r3-reports-redacted-cash-ar.png`, fullPage: true });

    await page.getByRole("tab", { name: /Low Stock/i }).click();
    await expect(page.getByText(/Low stock data could not be retrieved or is not permitted/i)).toBeVisible();
    assertions++;

    // 4. Owner actor (full permissions): verify Manila Today / This Week / This Month distinct nonzero numbers,
    //    unredacted Cash & AR, Low Stock item (Oat Milk), CSV download, and delayed-request stale-period guard.
    await loginViaApi(process.env.CAFE_QA_OWNER_USER, process.env.CAFE_QA_OWNER_PASS);
    await page.reload();
    await expect(page.getByText(/Provisional profitability — not final/i)).toBeVisible();
    await expect(page.getByText(formatPhp(expected.monthlyNetCents)).first()).toBeVisible();
    assertions += 2;

    // Verify Generated timestamp is NOT the future month-end (12:00 AM of next month).
    const genText = await page.getByText(/^Generated /).textContent();
    assert(Boolean(genText) && !/12:00 AM/.test(genText), `Generated timestamp looks like clamped rangeEndUtc: ${genText}`);

    // Desktop 1366x768 screenshot + axe-core + zero horizontal overflow.
    await page.setViewportSize({ width: 1366, height: 768 });
    assert(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), "1366px horizontal overflow");
    await page.addScriptTag({ content: axeSource });
    const axeDesktop = await page.evaluate(async () => await window.axe.run(document));
    const seriousDesktop = axeDesktop.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
    assert(seriousDesktop.length === 0, `1366px axe violations: ${JSON.stringify(seriousDesktop.map((v) => v.id))}`);
    await page.screenshot({ path: `${root}/r3-reports-desktop-1366.png`, fullPage: true });

    // Switch to This Week and Today and verify distinct live Host numbers.
    await page.getByRole("button", { name: "This Week", exact: true }).click();
    await expect(page.getByText(formatPhp(expected.weeklyNetCents)).first()).toBeVisible();
    assertions++;

    await page.getByRole("button", { name: "Today", exact: true }).click();
    await expect(page.getByText(formatPhp(expected.dailyNetCents)).first()).toBeVisible();
    assertions++;

    // Check unredacted Cash & AR and Low Stock tabs for Owner.
    await page.getByRole("tab", { name: "Cash & AR", exact: true }).click();
    await expect(page.getByText("Sale Cash Receipts", { exact: true })).toBeVisible();
    await expect(page.getByText("₱6.00").first()).toBeVisible();
    await expect(page.getByText("Outstanding Balance (Current)", { exact: true })).toBeVisible();
    await expect(page.getByText("₱4.00").first()).toBeVisible();
    assertions += 4;

    await page.getByRole("tab", { name: /Low Stock \(1\)/i }).click();
    await expect(page.getByText("Oat Milk", { exact: true })).toBeVisible();
    await expect(page.getByText("Below Reorder", { exact: true })).toBeVisible();
    assertions += 2;

    // Return to Profitability Waterfall & verify CSV download contents.
    await page.getByRole("tab", { name: "Profitability Waterfall", exact: true }).click();
    const [download] = await Promise.all([
      page.waitForEvent("download"),
      page.getByRole("button", { name: /Export CSV/i }).click(),
    ]);
    assert(download.suggestedFilename() === "salryn-cafe-live-daily.csv", `Unexpected CSV filename: ${download.suggestedFilename()}`);
    const csvPath = await download.path();
    const csvContent = readFileSync(csvPath, "utf8");
    assert(csvContent.includes(`"Recognized Net Revenue","${(expected.dailyNetCents / 100).toFixed(2)}","Today"`), "CSV missing daily net revenue row");
    assert(csvContent.includes(`"Committed Refunds","5.00","Today"`), "CSV missing committed refund row");
    assert(csvContent.includes("non-official"), "CSV missing official boundary");

    // Delayed-request stale-period switching guard: delay This Month request, switch immediately to Today,
    // verify Today renders and stays rendered after the delayed This Month response completes.
    let releaseDelayedMonthly;
    const delayedMonthlyGate = new Promise((resolve) => {
      releaseDelayedMonthly = resolve;
    });
    let interceptedMonthly = false;
    await page.route("**/api/owner-reports/summary?*", async (route) => {
      if (!interceptedMonthly) {
        interceptedMonthly = true;
        await delayedMonthlyGate;
      }
      await route.continue();
    });
    await page.getByRole("button", { name: "This Month", exact: true }).click();
    await page.getByRole("button", { name: "Today", exact: true }).click();
    await expect(page.getByText(formatPhp(expected.dailyNetCents)).first()).toBeVisible();
    releaseDelayedMonthly();
    await page.waitForTimeout(300);
    await expect(page.getByText(formatPhp(expected.dailyNetCents)).first()).toBeVisible();
    if (expected.monthlyNetCents !== expected.dailyNetCents) {
      await expect(page.getByText(formatPhp(expected.monthlyNetCents))).toHaveCount(0);
    }
    await page.unroute("**/api/owner-reports/summary?*");
    assertions += 2;

    // Tablet 1024x768 & Mobile 390x844 overflow checks + screenshots.
    await page.setViewportSize({ width: 1024, height: 768 });
    assert(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), "1024px horizontal overflow");
    await page.screenshot({ path: `${root}/r3-reports-tablet-1024.png`, fullPage: true });

    await page.setViewportSize({ width: 390, height: 844 });
    assert(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), "390px horizontal overflow");
    await page.screenshot({ path: `${root}/r3-reports-mobile-390.png`, fullPage: true });

    writeFileSync(
      `${root}/r3-reports-browser-evidence.json`,
      JSON.stringify({ phase, assertions, expected, genText, csvFile: download.suggestedFilename() }, null, 2),
    );
    console.log(`PASS R3 reports browser licensed phase (${assertions} assertions)`);
  }

  if (errors.length > 0) {
    throw new Error(`Uncaught browser errors: ${errors.join("; ")}`);
  }
} catch (err) {
  await page.screenshot({ path: `${root}/r3-reports-failure-${phase}.png`, fullPage: true }).catch(() => {});
  writeFileSync(`${root}/r3-reports-failure-${phase}.txt`, String(err));
  throw err;
} finally {
  await browser.close();
}
