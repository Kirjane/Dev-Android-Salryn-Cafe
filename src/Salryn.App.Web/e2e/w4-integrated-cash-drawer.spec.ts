import { expect, test, type Page } from "@playwright/test";
import { installMockSalrynApi } from "./fixtures/mockApi";

async function openCountDialog(page: Page) {
  await page.goto("/?page=checkout");
  await page.getByRole("button", { name: "Count & Close Shift" }).click();
  await expect(page.getByRole("dialog", { name: "Physical drawer count" })).toBeVisible();
}

async function enterCount(page: Page, value: string) {
  await page.getByLabel("Actual counted cash").fill(value);
  await page.getByRole("checkbox", { name: /physically counted/i }).check();
}

function rectanglesOverlap(
  first: { x: number; y: number; width: number; height: number },
  second: { x: number; y: number; width: number; height: number },
) {
  return first.x < second.x + second.width
    && first.x + first.width > second.x
    && first.y < second.y + second.height
    && first.y + first.height > second.y;
}

test.describe("I1 W4 integrated cash drawer UX", () => {
  test("shared dialog traps Tab, focuses inside, locks scroll, restores focus, and ignores Escape while busy", async ({ page }, testInfo) => {
    await installMockSalrynApi(page, { persona: "admin", closeShiftMode: "delayed-success" });
    await openCountDialog(page);
    const dialog = page.getByRole("dialog", { name: "Physical drawer count" });
    await page.screenshot({ path: testInfo.outputPath("w4-no-count-1366x768.png"), fullPage: true });
    await expect(page.getByLabel("Actual counted cash")).toBeFocused();
    await expect.poll(() => page.evaluate(() => document.body.style.overflow)).toBe("hidden");
    await page.keyboard.press("Shift+Tab");
    expect(await dialog.evaluate((node) => node.contains(document.activeElement))).toBe(true);
    await page.keyboard.press("Tab");
    expect(await dialog.evaluate((node) => node.contains(document.activeElement))).toBe(true);
    await enterCount(page, "50.00");
    await page.getByRole("button", { name: "Submit immutable count & close" }).click();
    await page.keyboard.press("Escape");
    await expect(dialog).toBeVisible();
    await expect(dialog).toHaveAttribute("aria-busy", "true");
    await expect(dialog).toBeHidden();
    await expect.poll(() => page.evaluate(() => document.body.style.overflow)).toBe("");
    await page.screenshot({ path: testInfo.outputPath("w4-count-zero-1366x768.png"), fullPage: true });
  });

  test("ambiguous close retries unchanged with the same request id and does not duplicate close", async ({ page }, testInfo) => {
    const requestIds: string[] = [];
    page.on("request", (request) => {
      if (request.url().endsWith("/api/shift/close") && request.method() === "POST") {
        requestIds.push((request.postDataJSON() as { clientRequestId: string }).clientRequestId);
      }
    });
    await installMockSalrynApi(page, { persona: "admin", closeShiftMode: "ambiguous-once" });
    await openCountDialog(page);
    await enterCount(page, "49.00");
    await page.getByRole("button", { name: "Submit immutable count & close" }).click();
    await expect(page.getByText(/Host response uncertain/i)).toBeVisible();
    await page.screenshot({ path: testInfo.outputPath("w4-short-uncertain-1366x768.png"), fullPage: true });
    await page.getByRole("button", { name: "Retry unchanged" }).click();
    await expect(page.getByRole("dialog", { name: "Physical drawer count" })).toBeHidden();
    expect(requestIds).toHaveLength(2);
    expect(requestIds[1]).toBe(requestIds[0]);
    await expect(page.getByText(/variance/i).first()).toBeVisible();
  });

  test("physical zero is accepted as an explicit count and remains distinct from zero variance", async ({ page }, testInfo) => {
    await installMockSalrynApi(page, { persona: "admin", closeShiftMode: "success" });
    await openCountDialog(page);
    await enterCount(page, "0.00");
    await page.getByRole("button", { name: "Submit immutable count & close" }).click();
    await expect(page.getByText(/counted PHP 0\.00; variance PHP -50\.00; PendingReview/i)).toBeVisible();
    await page.screenshot({ path: testInfo.outputPath("w4-physical-zero-shortage-1366x768.png"), fullPage: true });
  });

  for (const scenario of [
    { mode: "bad-request" as const, status: 400 },
    { mode: "unauthorized" as const, status: 401 },
    { mode: "forbidden" as const, status: 403 },
    { mode: "conflict" as const, status: 409 },
  ]) {
    test(`${scenario.status} deterministic close rejection does not silently reuse the rejected request identity`, async ({ page }) => {
      const requestIds: string[] = [];
      page.on("request", (request) => {
        if (request.url().endsWith("/api/shift/close") && request.method() === "POST") {
          requestIds.push((request.postDataJSON() as { clientRequestId: string }).clientRequestId);
        }
      });
      await installMockSalrynApi(page, { persona: "admin", closeShiftMode: scenario.mode });
      await openCountDialog(page);
      await enterCount(page, "50.00");
      await page.getByRole("button", { name: "Submit immutable count & close" }).click();
      await expect(page.getByRole("dialog", { name: "Physical drawer count" })).toBeVisible();
      await page.getByRole("button", { name: "Submit immutable count & close" }).click();
      expect(requestIds).toHaveLength(2);
      expect(requestIds[1]).not.toBe(requestIds[0]);
    });
  }

  test("conflict and missing coverage remain blocking financial errors on mobile", async ({ page }, testInfo) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await installMockSalrynApi(page, { persona: "admin", closeShiftMode: "conflict" });
    await openCountDialog(page);
    await enterCount(page, "51.00");
    await page.getByRole("button", { name: "Submit immutable count & close" }).click();
    await expect(page.locator("main").getByText(/original count was not changed/i)).toBeVisible();
    await page.screenshot({ path: testInfo.outputPath("w4-over-conflict-390x844.png"), fullPage: true });
  });

  test("mobile financial error notification does not obstruct the count-dialog title or resolution controls", async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await installMockSalrynApi(page, { persona: "admin", closeShiftMode: "conflict" });
    await openCountDialog(page);
    await enterCount(page, "51.00");
    await page.getByRole("button", { name: "Submit immutable count & close" }).click();

    const dialog = page.getByRole("dialog", { name: "Physical drawer count" });
    const title = dialog.getByRole("heading", { name: "Physical drawer count" });
    const resolution = page.getByRole("button", { name: "Submit immutable count & close" });
    // The I1 dialog correctly removes background notifications from the
    // accessibility tree while open, so inspect the rendered overlay directly.
    const notification = page.locator('[aria-label="Action notifications"] [role="alert"]');
    const [titleBox, resolutionBox, notificationBox] = await Promise.all([
      title.boundingBox(),
      resolution.boundingBox(),
      notification.boundingBox(),
    ]);

    expect(titleBox).not.toBeNull();
    expect(resolutionBox).not.toBeNull();
    expect(notificationBox).not.toBeNull();
    expect(rectanglesOverlap(notificationBox!, titleBox!)).toBe(false);
    expect(rectanglesOverlap(notificationBox!, resolutionBox!)).toBe(false);
  });

  for (const viewport of [
    { label: "mobile", width: 390, height: 844 },
    { label: "desktop", width: 1366, height: 768 },
  ]) {
    test(`toast created before the W4 dialog stays visible while the application root is inert on ${viewport.label}`, async ({ page }, testInfo) => {
      await page.setViewportSize({ width: viewport.width, height: viewport.height });
      await installMockSalrynApi(page, { persona: "admin", hasOpenShift: false });
      await page.goto("/?page=checkout");
      await page.getByRole("button", { name: "Open Shift" }).click();
      const toast = page.locator('[data-notification-viewport] [role="status"]');
      await expect(toast).toContainText("Shift opened");
      await page.getByRole("button", { name: "Count & Close Shift" }).click();

      const dialog = page.getByRole("dialog", { name: "Physical drawer count" });
      await expect(dialog).toBeVisible();
      await expect(toast).toBeVisible();
      await expect(page.locator("#root")).toHaveAttribute("inert", "");
      expect(await page.evaluate(() => document.querySelector('[data-notification-viewport]')?.parentElement === document.body)).toBe(true);
      expect(await page.evaluate(() => {
        const backgroundButton = document.querySelector<HTMLElement>("#root button");
        backgroundButton?.focus();
        return document.activeElement === backgroundButton;
      })).toBe(false);

      const toastBox = await toast.boundingBox();
      const titleBox = await dialog.getByRole("heading", { name: "Physical drawer count" }).boundingBox();
      const submitBox = await dialog.getByRole("button", { name: "Submit immutable count & close" }).boundingBox();
      expect(toastBox).not.toBeNull();
      expect(titleBox).not.toBeNull();
      expect(submitBox).not.toBeNull();
      expect(rectanglesOverlap(toastBox!, titleBox!)).toBe(false);
      expect(rectanglesOverlap(toastBox!, submitBox!)).toBe(false);
      await page.screenshot({ path: testInfo.outputPath(`toast-before-w4-${viewport.label}.png`), fullPage: true });
    });
  }

  for (const viewport of [
    { label: "390x844", width: 390, height: 844, zoom: "1" },
    { label: "1024x768", width: 1024, height: 768, zoom: "1" },
    { label: "1366x768-125pct", width: 1366, height: 768, zoom: "1.25" },
    { label: "1366x768-150pct", width: 1366, height: 768, zoom: "1.5" },
  ]) {
    test(`real stacked financial errors preserve W4 recovery UI at ${viewport.label}`, async ({ page }, testInfo) => {
      await page.setViewportSize({ width: viewport.width, height: viewport.height });
      await installMockSalrynApi(page, { persona: "admin", closeShiftMode: "conflict" });
      await openCountDialog(page);
      await page.evaluate((zoom) => { document.documentElement.style.zoom = zoom; }, viewport.zoom);
      await enterCount(page, "51.00");
      const submit = page.getByRole("button", { name: "Submit immutable count & close" });
      const dialog = page.getByRole("dialog", { name: "Physical drawer count" });
      const notifications = page.locator('[data-notification-viewport] [role="alert"]');
      for (let attempt = 0; attempt < 3; attempt += 1) {
        await submit.focus();
        await page.keyboard.press("Enter");
        await expect(notifications).toHaveCount(attempt + 1);
        await expect(submit).toBeEnabled();
      }
      await expect(notifications).toHaveCount(3);
      await expect(dialog.getByRole("heading", { name: "Physical drawer count" })).toBeVisible();
      await expect(page.getByLabel("Actual counted cash")).toBeVisible();
      await expect(submit).toBeVisible();
      await expect(dialog.getByText(/No force-close is available/i)).toBeVisible();

      const notificationBox = await page.locator("[data-notification-viewport]").boundingBox();
      expect(notificationBox).not.toBeNull();
      for (const protectedElement of [
        dialog.getByRole("heading", { name: "Physical drawer count" }),
        page.getByLabel("Actual counted cash"),
        submit,
        dialog.getByText(/No force-close is available/i),
      ]) {
        const protectedBox = await protectedElement.boundingBox();
        expect(protectedBox).not.toBeNull();
        expect(rectanglesOverlap(notificationBox!, protectedBox!)).toBe(false);
      }
      await submit.focus();
      await page.keyboard.press("Shift+Tab");
      expect(await dialog.evaluate((node) => node.contains(document.activeElement))).toBe(true);
      await page.screenshot({ path: testInfo.outputPath(`w4-stacked-errors-${viewport.label}.png`), fullPage: true });
    });
  }

  test("coverage error has no force-close and count dialog fits 1024x768", async ({ page }, testInfo) => {
    await page.setViewportSize({ width: 1024, height: 768 });
    await installMockSalrynApi(page, { persona: "admin", closeShiftMode: "coverage-unverified" });
    await openCountDialog(page);
    await enterCount(page, "50.00");
    await page.getByRole("button", { name: "Submit immutable count & close" }).click();
    await expect(page.locator("main").getByText(/Safe close is blocked/i)).toBeVisible();
    await expect(page.getByRole("button", { name: /force/i })).toHaveCount(0);
    await page.screenshot({ path: testInfo.outputPath("w4-coverage-blocked-1024x768.png"), fullPage: true });
  });

  test("owner report deep link survives reload and browser back", async ({ page }) => {
    await installMockSalrynApi(page, { persona: "admin", u1Entitled: true });
    await page.goto("/?page=owner-reports");
    await page.getByRole("button", { name: /2026-04-19/i }).first().click();
    await expect(page).toHaveURL(/page=sales.*salesFrom=2026-04-19/);
    await page.reload();
    await expect(page.getByRole("heading", { name: "Sales & Receipts" })).toBeVisible();
    await page.goBack();
    await expect(page.getByText("Sales trend", { exact: true })).toBeVisible();
    await expect(page).toHaveURL(/page=owner-reports/);
  });
});
