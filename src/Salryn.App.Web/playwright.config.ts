import { defineConfig, devices } from "@playwright/test";

/**
 * Salryn POS Playwright harness — two strictly separated tiers.
 *
 *  - Project `mocked-ui`       : synthetic `/api/*` fixtures only (MOCKED_UI). Used by
 *                                `npm run test:e2e`, `npm run test:visual`, `npm run test:visual:update`.
 *  - Project `integrated-host` : zero mocks, live Host only (INTEGRATED_HOST). Used by
 *                                `npm run test:integrated-host`. Fails (non-zero) when the Host is
 *                                unreachable, unlicensed, or QA credentials are not provided.
 *
 * Visual regression contract:
 *  - Expected images live at `e2e/__screenshots__/<name>.png` (the original 42 A0 inventory paths).
 *  - `updateSnapshots: "none"` — ordinary runs NEVER write or overwrite expected images, and a missing
 *    expected image is a FAILURE. Only `npm run test:visual:update` (explicit `--update-snapshots=all`)
 *    may rewrite baselines, and only after human review.
 *  - Baselines are rendered on Windows (win32) with the Playwright-bundled Chromium and the system
 *    "Segoe UI" font stack. Other OS/font stacks will legitimately differ; do not widen tolerances to
 *    hide that — regenerate a separate reviewed baseline on that platform instead.
 */

const runningIntegratedOnly = process.argv.some(
  (arg) => arg === "--project=integrated-host" || arg === "integrated-host",
);

export default defineConfig({
  testDir: "./e2e",
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: 0,
  workers: 1,
  updateSnapshots: "none",
  snapshotPathTemplate: "{testDir}/__screenshots__/{arg}{ext}",
  expect: {
    toHaveScreenshot: {
      // Tight tolerance: anti-aliasing noise only. Layout/colour/copy drift must fail.
      maxDiffPixelRatio: 0.001,
      threshold: 0.2,
      animations: "disabled",
      caret: "hide",
      scale: "css",
    },
  },
  reporter: [
    ["list"],
    ["html", { outputFolder: "playwright-report", open: "never" }],
    ["json", { outputFile: "test-results/playwright-results.json" }],
  ],
  use: {
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
    timezoneId: "Asia/Manila",
    locale: "en-PH",
    colorScheme: "light",
    reducedMotion: "reduce",
  },
  projects: [
    {
      name: "mocked-ui",
      testIgnore: /integrated-host\.spec\.ts/,
      use: {
        ...devices["Desktop Chrome"],
        channel: undefined, // Playwright-bundled Chromium only (pinned by @playwright/test in package-lock.json)
        baseURL: "http://127.0.0.1:5173",
        viewport: { width: 1366, height: 768 },
        deviceScaleFactor: 1,
        hasTouch: false,
        isMobile: false,
      },
    },
    {
      name: "integrated-host",
      testMatch: /integrated-host\.spec\.ts/,
      use: {
        ...devices["Desktop Chrome"],
        baseURL: process.env.SALRYN_INTEGRATED_HOST_URL ?? "http://127.0.0.1:5299",
        viewport: { width: 1366, height: 768 },
        deviceScaleFactor: 1,
      },
    },
  ],
  webServer: runningIntegratedOnly
    ? undefined
    : {
        command: "npm run dev -- --host 127.0.0.1 --port 5173",
        url: "http://127.0.0.1:5173",
        reuseExistingServer: !process.env.CI,
        timeout: 60000,
      },
});
