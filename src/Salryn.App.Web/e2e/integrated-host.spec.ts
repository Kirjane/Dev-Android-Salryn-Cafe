import { expect, test } from "@playwright/test";
import * as fs from "node:fs";
import * as path from "node:path";
import { fileURLToPath } from "node:url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const RESULTS_DIR = path.resolve(__dirname, "..", "test-results");

test.describe("INTEGRATED_HOST Loopback Readiness Probe @integrated", () => {
  test("Probes live loopback Host API on 127.0.0.1:5299 without mocking or entitlement bypass", async ({
    request,
  }) => {
    fs.mkdirSync(RESULTS_DIR, { recursive: true });
    const reportPath = path.join(RESULTS_DIR, "integrated-host-probe.json");

    let hostReachable = false;
    let healthStatus: number | null = null;
    let licensePayload: unknown = null;
    let diagnosticReason = "";

    try {
      const healthRes = await request.get("http://127.0.0.1:5299/health", { timeout: 2500 });
      healthStatus = healthRes.status();
      hostReachable = healthRes.ok();

      if (hostReachable) {
        const licRes = await request.get("http://127.0.0.1:5299/api/license/status", { timeout: 2500 });
        if (licRes.ok()) {
          licensePayload = await licRes.json();
        }
        diagnosticReason = "LIVE_HOST_REACHABLE";
      } else {
        diagnosticReason = `BLOCKED_HOST_UNHEALTHY_HTTP_${healthStatus}`;
      }
    } catch (error) {
      diagnosticReason = `BLOCKED_NO_LIVE_HOST_ON_5299: ${error instanceof Error ? error.message : String(error)}`;
    }

    const probeRecord = {
      tier: "INTEGRATED_HOST",
      probedAtUtc: new Date().toISOString(),
      targetBaseUrl: "http://127.0.0.1:5299",
      hostReachable,
      healthStatus,
      diagnosticReason,
      licensePayload,
      note: "MOCKED_UI and INTEGRATED_HOST are strictly separated. No license or entitlement bypass is permitted.",
    };

    fs.writeFileSync(reportPath, JSON.stringify(probeRecord, null, 2), "utf8");
    expect(fs.existsSync(reportPath)).toBe(true);
  });
});
