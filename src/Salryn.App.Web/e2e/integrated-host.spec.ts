import { expect, test, type APIRequestContext } from "@playwright/test";
import * as fs from "node:fs";
import * as path from "node:path";
import { fileURLToPath } from "node:url";

/**
 * INTEGRATED_HOST tier (A0-R1).
 *
 * Runs ONLY via `npm run test:integrated-host` (Playwright project `integrated-host`). It is never part of
 * `npm run test:e2e` / `npm run test:visual`, so it can never inflate MOCKED_UI pass counts.
 *
 * Semantics — an explicit request for integrated testing MUST exit non-zero unless a real Host proves itself:
 *   1. Host reachable and healthy                       -> else FAIL (verdict BLOCKED_NO_HOST / HOST_UNHEALTHY)
 *   2. Host license is Licensed + Host type + matched   -> else FAIL (verdict HOST_NOT_LICENSED)
 *   3. Operator-supplied disposable QA credentials sign in through the real UI and open read-only pages
 *                                                       -> else FAIL (verdict BLOCKED_NO_QA_CREDENTIALS / JOURNEY_FAILED)
 *
 * Zero network mocks. No fake signed licenses. No auth bypass. No writes (no sales, no stock, no settings).
 * Diagnostics are redacted: HTTP status codes, license status/type/feature names only — never signatures,
 * license payloads, cookies, tokens, passwords or customer data.
 *
 * Environment:
 *   SALRYN_INTEGRATED_HOST_URL   default http://127.0.0.1:5299 (Host must serve both /api and the SPA)
 *   SALRYN_QA_HOST_USERNAME      disposable QA operator (Admin or Manager) on a disposable QA Host
 *   SALRYN_QA_HOST_PASSWORD      its password (never logged)
 */

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const RESULTS_DIR = path.resolve(__dirname, "..", "test-results");
const REPORT_PATH = path.join(RESULTS_DIR, "integrated-host-probe.json");
const HOST_URL = (process.env.SALRYN_INTEGRATED_HOST_URL ?? "http://127.0.0.1:5299").replace(/\/+$/, "");

type CheckVerdict = "PASS" | "FAIL" | "BLOCKED" | "NOT_RUN";
type CheckRecord = { verdict: CheckVerdict; code: string; detail: string };

const probe = {
  tier: "INTEGRATED_HOST",
  probedAtUtc: new Date().toISOString(),
  targetBaseUrl: HOST_URL,
  overallVerdict: "NOT_RUN" as CheckVerdict,
  checks: {
    health: { verdict: "NOT_RUN", code: "NOT_RUN", detail: "" } as CheckRecord,
    license: { verdict: "NOT_RUN", code: "NOT_RUN", detail: "" } as CheckRecord,
    authenticatedReadOnlyJourney: { verdict: "NOT_RUN", code: "NOT_RUN", detail: "" } as CheckRecord,
  },
  note:
    "Redacted. MOCKED_UI and INTEGRATED_HOST are strictly separated. A reachable /health alone is NOT integrated UI acceptance.",
};

function sanitize(message: string): string {
  // Keep the first line only (drops Playwright call logs / headers) and strip anything token-like.
  return message.split(/\r?\n/)[0].replace(/[A-Za-z0-9+/=_-]{40,}/g, "[redacted]").slice(0, 240);
}

function writeProbe() {
  const verdicts = Object.values(probe.checks).map((c) => c.verdict);
  probe.overallVerdict = verdicts.every((v) => v === "PASS")
    ? "PASS"
    : verdicts.some((v) => v === "BLOCKED")
      ? "BLOCKED"
      : verdicts.some((v) => v === "FAIL")
        ? "FAIL"
        : "NOT_RUN";
  fs.mkdirSync(RESULTS_DIR, { recursive: true });
  fs.writeFileSync(REPORT_PATH, JSON.stringify(probe, null, 2), "utf8");
}

async function getStatus(request: APIRequestContext, url: string) {
  const res = await request.get(url, { timeout: 5000, failOnStatusCode: false });
  return res;
}

test.describe.configure({ mode: "serial" });

test.describe("INTEGRATED_HOST live Host acceptance @integrated", () => {
  test.afterAll(() => writeProbe());

  test("1. Host is reachable and healthy (no mocks)", async ({ request }) => {
    try {
      const res = await getStatus(request, `${HOST_URL}/health`);
      if (!res.ok()) {
        probe.checks.health = { verdict: "FAIL", code: `HOST_UNHEALTHY_HTTP_${res.status()}`, detail: "" };
      } else {
        probe.checks.health = { verdict: "PASS", code: `HTTP_${res.status()}`, detail: "" };
      }
    } catch (error) {
      probe.checks.health = {
        verdict: "BLOCKED",
        code: "BLOCKED_NO_HOST",
        detail: sanitize(error instanceof Error ? error.message : String(error)),
      };
    }
    expect(
      probe.checks.health.verdict,
      `INTEGRATED_HOST health: ${probe.checks.health.code} (${probe.checks.health.detail})`,
    ).toBe("PASS");
  });

  test("2. Host license is Licensed, Host-type and machine-matched (no fake licenses)", async ({ request }) => {
    const res = await getStatus(request, `${HOST_URL}/api/license/status`);
    if (!res.ok()) {
      probe.checks.license = { verdict: "FAIL", code: `LICENSE_STATUS_HTTP_${res.status()}`, detail: "" };
    } else {
      const body = (await res.json()) as {
        status?: string | number;
        licenseType?: string | number;
        machineMatched?: boolean;
        enabledFeatures?: string[];
      };
      const licensed = body.status === "Licensed" || body.status === 1;
      const host = body.licenseType === "Host" || body.licenseType === 0;
      const detail = `status=${String(body.status)} licenseType=${String(body.licenseType)} machineMatched=${String(
        body.machineMatched,
      )} features=[${(body.enabledFeatures ?? []).join(",")}]`;
      probe.checks.license =
        licensed && host && body.machineMatched === true
          ? { verdict: "PASS", code: "HOST_LICENSED", detail }
          : { verdict: "FAIL", code: "HOST_NOT_LICENSED", detail };
    }
    expect(
      probe.checks.license.verdict,
      `INTEGRATED_HOST license: ${probe.checks.license.code} (${probe.checks.license.detail})`,
    ).toBe("PASS");
  });

  test("3. Disposable QA operator signs in through the real UI and opens read-only pages", async ({ page }) => {
    const username = process.env.SALRYN_QA_HOST_USERNAME;
    const password = process.env.SALRYN_QA_HOST_PASSWORD;
    if (!username || !password) {
      probe.checks.authenticatedReadOnlyJourney = {
        verdict: "BLOCKED",
        code: "BLOCKED_NO_QA_CREDENTIALS",
        detail: "Set SALRYN_QA_HOST_USERNAME / SALRYN_QA_HOST_PASSWORD for a disposable QA Host operator.",
      };
      expect(probe.checks.authenticatedReadOnlyJourney.verdict, probe.checks.authenticatedReadOnlyJourney.detail).toBe(
        "PASS",
      );
      return;
    }

    try {
      await page.goto(`${HOST_URL}/`);
      await expect(page.getByRole("heading", { name: "Sign in to Salryn POS" })).toBeVisible({ timeout: 15000 });
      await page.getByLabel("Username").fill(username);
      await page.locator("#login-password").fill(password);
      await page.getByRole("button", { name: /Sign in/i }).click();
      const aside = page.locator("aside");
      await expect(aside).toBeVisible({ timeout: 15000 });
      await aside.getByRole("button", { name: "Sales & Receipts", exact: true }).click();
      await expect(page.getByRole("heading", { name: "Sales & Receipts" })).toBeVisible();
      const reports = aside.getByRole("button", { name: "Reports", exact: true });
      if ((await reports.count()) > 0) {
        await reports.click();
        await expect(page.getByRole("heading", { name: "Reports" })).toBeVisible();
      }
      await page.getByRole("button", { name: "Sign out" }).click();
      probe.checks.authenticatedReadOnlyJourney = { verdict: "PASS", code: "READ_ONLY_JOURNEY_OK", detail: "" };
    } catch (error) {
      probe.checks.authenticatedReadOnlyJourney = {
        verdict: "FAIL",
        code: "JOURNEY_FAILED",
        detail: sanitize(error instanceof Error ? error.message : String(error)),
      };
    }
    expect(
      probe.checks.authenticatedReadOnlyJourney.verdict,
      `INTEGRATED_HOST journey: ${probe.checks.authenticatedReadOnlyJourney.code} (${probe.checks.authenticatedReadOnlyJourney.detail})`,
    ).toBe("PASS");
  });
});
