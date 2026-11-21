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
    await expect(payment.getByLabel("Payment method")).toBeVisible();
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
  const mode = payment.getByLabel("Payment method");
  const tender = payment.getByLabel("Cash tendered");

  await mode.selectOption("Utang");
  await expect(tender).toHaveCount(0);
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
  const mode = paymentCard(page).getByLabel("Payment method");
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
    await expect(paymentCard(page).getByLabel("Payment method")).toBeVisible();
    const review = paymentCard(page).getByRole("button", { name: "Review Checkout" });
    await review.scrollIntoViewIfNeeded();
    await expect(review).toBeVisible();
    const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow).toBeLessThanOrEqual(1);
    await page.screenshot({ path: testInfo.outputPath(`reachable-${viewport.width}x${viewport.height}.png`) });
  });
}

test("@jane-layout locked Cafe tender policy: Cash, Utang, Cash+Utang pass and non-Cafe tenders are absent and rejected with HTTP 400", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1366, height: 768 });
  await installMockSalrynApi(page, { persona: "admin", hasOpenShift: true, u1Entitled: true });
  await page.goto("/?page=checkout");

  const payment = paymentCard(page);
  const mode = payment.getByLabel("Payment method");
  await expect(mode.locator("option")).toHaveCount(3);
  await expect(mode.locator('option[value="Cash"]')).toHaveCount(1);
  await expect(mode.locator('option[value="Utang"]')).toHaveCount(1);
  await expect(mode.locator('option[value="CashUtang"]')).toHaveCount(1);
  const paymentText = await payment.innerText();
  for (const forbidden of ["GCash", "Maya", "Debit", "Split"]) {
    await expect(mode.locator(`option[value="${forbidden}"]`)).toHaveCount(0);
    await expect(payment.getByText(new RegExp(`\\b${forbidden}\\b`, "i"))).toHaveCount(0);
    expect(paymentText).not.toMatch(new RegExp(`\\b${forbidden}\\b`, "i"));
  }

  // Positive Cash sale
  await page.getByRole("button", { name: "Add", exact: true }).first().click();
  await page.getByRole("button", { name: "500", exact: true }).click();
  await payment.getByRole("button", { name: "Review Checkout" }).click();
  await page.getByRole("button", { name: "Confirm Sale" }).click();
  await expect(page.getByRole("heading", { name: "Sale Completed" })).toBeVisible();
  await expect(page.getByText("Cash sale committed")).toBeVisible();
  await page.getByRole("button", { name: "New Sale (F5)" }).click();

  // Positive Utang sale
  await page.getByRole("button", { name: "Add", exact: true }).first().click();
  await mode.selectOption("Utang");
  await payment.getByLabel("Customer (Required for Utang)").selectOption({ index: 1 });
  await payment.getByRole("button", { name: "Review Checkout" }).click();
  await page.getByRole("button", { name: "Confirm Sale" }).click();
  await expect(page.getByRole("heading", { name: "Sale Completed" })).toBeVisible();
  await expect(page.getByText("Utang credit sale committed")).toBeVisible();
  await page.getByRole("button", { name: "New Sale (F5)" }).click();

  // Positive Cash + Utang sale
  await page.getByRole("button", { name: "Add", exact: true }).first().click();
  await mode.selectOption("CashUtang");
  await payment.getByLabel("Cash tendered").fill("100");
  await payment.getByLabel("Customer (Required for Utang)").selectOption({ index: 1 });
  await payment.getByRole("button", { name: "Review Checkout" }).click();
  await page.getByRole("button", { name: "Confirm Sale" }).click();
  await expect(page.getByRole("heading", { name: "Sale Completed" })).toBeVisible();
  await expect(page.getByText("Cash + Utang sale committed")).toBeVisible();

  // Negative API boundary check for Checkout & Receivables collections
  for (const method of ["GCash", "Maya", "Debit"]) {
    const checkoutStatus = await page.evaluate(async (m) => {
      const response = await fetch("/api/checkout", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          clientRequestId: "00000000-0000-0000-0000-000000000099",
          lines: [{ productId: "40000000-0000-0000-0000-000000000001", quantityX1000: 1000 }],
          payments: [{ method: m, amountCents: 28500 }],
        }),
      });
      return response.status;
    }, method);
    expect(checkoutStatus).toBe(400);

    const collectionStatus = await page.evaluate(async (m) => {
      const response = await fetch("/api/receivables/collections", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          customerId: "50000000-0000-0000-0000-000000000001",
          amountCents: 10000,
          method: m,
          clientRequestId: "00000000-0000-0000-0000-000000000098",
        }),
      });
      return response.status;
    }, method);
    expect(collectionStatus).toBe(400);
  }
});

for (const viewport of [
  { width: 390, height: 844 },
  { width: 1024, height: 768 },
  { width: 1366, height: 768 },
] as const) {
  test(`@jane-layout receivables layout, Cash-only collection, and non-clipped note input at ${viewport.width}x${viewport.height}`, async ({
    page,
  }) => {
    await page.setViewportSize(viewport);
    await installMockSalrynApi(page, { persona: "admin", u1Entitled: true });
    await page.goto("/?page=receivables");

    await expect(page.locator("h1")).toHaveCount(1);
    await expect(page.getByRole("heading", { level: 1, name: "Accounts Receivable" })).toBeVisible();
    await expect(page.getByText(/Utang Ledger/i)).toBeVisible();

    await page.getByText("Maria Clara Dela Cruz").click();
    const methodSelect = page.getByLabel("Collection payment method");
    await expect(methodSelect).toBeVisible();
    await expect(methodSelect.locator("option")).toHaveCount(1);
    await expect(methodSelect.locator('option[value="Cash"]')).toHaveCount(1);
    const methodText = await methodSelect.innerText();
    for (const forbidden of ["GCash", "Maya", "Debit", "Split"]) {
      await expect(methodSelect.locator(`option[value="${forbidden}"]`)).toHaveCount(0);
      expect(methodText).not.toMatch(new RegExp(`\\b${forbidden}\\b`, "i"));
    }

    const amountInput = page.getByLabel("Collection amount");
    const noteInput = page.getByLabel("Collection note");
    const collectButton = page.getByRole("button", { name: "Collect payment" });
    await expect(amountInput).toBeVisible();
    await expect(noteInput).toBeVisible();
    await expect(collectButton).toBeVisible();

    const noteCheck = await noteInput.evaluate((el) => {
      const input = el as HTMLInputElement;
      const style = window.getComputedStyle(input);
      const canvas = document.createElement("canvas");
      const ctx = canvas.getContext("2d");
      let textWidth = 0;
      if (ctx) {
        ctx.font = `${style.fontWeight} ${style.fontSize} ${style.fontFamily}`;
        textWidth = ctx.measureText(input.placeholder || "").width;
      }
      const availableWidth =
        input.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
      return {
        placeholder: input.placeholder,
        availableWidth,
        textWidth,
        overflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
      };
    });
    expect(noteCheck.placeholder).toBe("Optional collection note");
    expect(noteCheck.availableWidth).toBeGreaterThanOrEqual(noteCheck.textWidth);
    expect(noteCheck.overflow).toBeLessThanOrEqual(1);
  });
}

test("@jane-layout reports hierarchy renders single H1, single Reports.View pill, and subordinate Cafe management reports h2", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1366, height: 768 });
  await installMockSalrynApi(page, { persona: "admin", u1Entitled: true });
  await page.goto("/?page=owner-reports");

  await expect(page.locator("h1")).toHaveCount(1);
  await expect(page.getByRole("heading", { level: 1, name: "Reports" })).toBeVisible();
  await expect(page.getByText("Reports.View", { exact: true })).toHaveCount(1);

  const cafeHeading = page.getByRole("heading", { level: 2, name: "Cafe management reports" });
  await expect(cafeHeading).toBeVisible();
  await expect(page.locator('section[aria-labelledby="cafe-management-reports-heading"]')).toBeVisible();

  const orderOk = await page.evaluate(() => {
    const h1 = document.querySelector("h1");
    const h2 = document.getElementById("cafe-management-reports-heading");
    if (!h1 || !h2) return false;
    return Boolean(h1.compareDocumentPosition(h2) & Node.DOCUMENT_POSITION_FOLLOWING);
  });
  expect(orderOk).toBe(true);
});
