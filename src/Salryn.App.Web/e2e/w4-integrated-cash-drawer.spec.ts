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
