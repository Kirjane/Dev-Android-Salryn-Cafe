import { expect, test, type Page } from "@playwright/test";
import { installMockSalrynApi } from "./fixtures/mockApi";

/**
 * Jane checkout workstation layout gate.
 * MOCKED_UI only: these checks do not certify Windows WebView2, local Host,
 * printer, physical drawer, or the financial backend.
 * Screenshots are evidence captures, not approved visual baselines.
 */
function paymentCard(page: Page) {
  return page.locator("section").filter({
    has: page.getByRole("heading", { name: "Payment / checkout" }),
  });
}

const desktopViewports = [
  { width: 1366, height: 768 },
  { width: 1368, height: 900 },
  { width: 1440, height: 900 },
] as const;

for (const viewport of desktopViewports) {
  test(`@jane-layout checkout review remains in viewport at ${viewport.width}x${viewport.height}`, async ({ page }, testInfo) => {
    await page.setViewportSize(viewport);
    await installMockSalrynApi(page, { persona: "admin", hasOpenShift: true, u1Entitled: true });
    await page.goto("/?page=checkout");

    const payment = paymentCard(page);
    const review = payment.getByRole("button", { name: "Review Checkout" });
    await expect(payment.getByLabel("Sale payment")).toBeVisible();
    await expect(review).toBeVisible();
    await expect(review).toBeDisabled(); // Empty cart: visible, but not ready.
    await expect(page.getByRole("heading", { name: "Payment mode" })).toHaveCount(0);
    const emptyBox = await review.boundingBox();
    expect(emptyBox).not.toBeNull();
    expect(emptyBox!.y + emptyBox!.height).toBeLessThanOrEqual(viewport.height);

    await page.screenshot({ path: testInfo.outputPath(`empty-cash-${viewport.width}x${viewport.height}.png`) });

    await page.getByRole("button", { name: "Add", exact: true }).first().click();
    await page.getByRole("button", { name: "500", exact: true }).click();
    await expect(review).toBeEnabled();
    const populatedBox = await review.boundingBox();
    expect(populatedBox).not.toBeNull();
    expect(populatedBox!.y + populatedBox!.height).toBeLessThanOrEqual(viewport.height);
    await page.screenshot({ path: testInfo.outputPath(`populated-cash-${viewport.width}x${viewport.height}.png`) });

    // The checkout page must not create horizontal overflow when the cashier works.
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow).toBeLessThanOrEqual(1);
  });
}

test("@jane-layout U1 payment modes stay inside the payment card and preserve tender rules", async ({ page }, testInfo) => {
  await page.setViewportSize({ width: 1366, height: 768 });
  await installMockSalrynApi(page, { persona: "admin", hasOpenShift: true, u1Entitled: true });
  await page.goto("/?page=checkout");
  await page.getByRole("button", { name: "Add", exact: true }).first().click();
  const payment = paymentCard(page);
  const mode = payment.getByLabel("Sale payment");
  const tender = payment.getByLabel("Cash tendered");

  await mode.selectOption("Utang");
  await expect(tender).toBeDisabled();
  await expect(payment.getByLabel("Customer")).toBeVisible();
  await expect(payment.getByLabel("Due date")).toBeVisible();
  await expect(payment.getByText("Utang credit balance")).toBeVisible();
  await page.screenshot({ path: testInfo.outputPath("utang-1366x768.png") });

  await mode.selectOption("CashUtang");
  await expect(tender).toBeEnabled();
  await tender.fill("100");
  await expect(payment.getByLabel("Customer")).toBeVisible();
  await expect(payment.getByText("Utang credit balance")).toBeVisible();
  await page.screenshot({ path: testInfo.outputPath("cash-utang-1366x768.png") });

  await mode.selectOption("Cash");
  await expect(payment.getByLabel("Customer")).toHaveCount(0);
  await expect(tender).toBeEnabled();
});

test("@jane-layout no U1 entitlement means no credit-sale choices", async ({ page }) => {
  await installMockSalrynApi(page, { persona: "admin", hasOpenShift: true, u1Entitled: false });
  await page.goto("/?page=checkout");
  const mode = paymentCard(page).getByLabel("Sale payment");
  await expect(mode.locator('option[value="Cash"]')).toHaveCount(1);
  await expect(mode.locator('option[value="Utang"]')).toHaveCount(0);
  await expect(mode.locator('option[value="CashUtang"]')).toHaveCount(0);
});

test("@jane-layout shift count and details remain accessible from compact toolbar", async ({ page }) => {
  await installMockSalrynApi(page, { persona: "admin", hasOpenShift: true });
  await page.goto("/?page=checkout");
  const details = page.getByText("Shift details", { exact: true });
  await expect(details).toBeVisible();
  await details.click();
  await expect(page.getByText("Opening cash:")).toBeVisible();
  await page.getByRole("button", { name: "Count & Close Shift" }).click();
  await expect(page.getByRole("dialog", { name: "Physical drawer count" })).toBeVisible();
  await expect(page.getByLabel("Actual counted cash")).toHaveValue("");
  await expect(page.getByRole("button", { name: "Submit immutable count & close" })).toBeDisabled();
});

for (const viewport of [{ width: 1024, height: 768 }, { width: 390, height: 844 }]) {
  test(`@jane-layout checkout remains reachable on ${viewport.width}x${viewport.height}`, async ({ page }, testInfo) => {
    await page.setViewportSize(viewport);
    await installMockSalrynApi(page, { persona: "admin", hasOpenShift: true, u1Entitled: true });
    await page.goto("/?page=checkout");
    await page.getByRole("button", { name: "Add", exact: true }).first().click();
    await expect(paymentCard(page).getByLabel("Sale payment")).toBeVisible();
    const review = paymentCard(page).getByRole("button", { name: "Review Checkout" });
    await review.scrollIntoViewIfNeeded();
    await expect(review).toBeVisible();
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow).toBeLessThanOrEqual(1);
    await page.screenshot({ path: testInfo.outputPath(`reachable-${viewport.width}x${viewport.height}.png`) });
  });
}
