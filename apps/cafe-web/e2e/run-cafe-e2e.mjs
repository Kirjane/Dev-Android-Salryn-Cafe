import { createRequire } from "node:module";
import { readdirSync, readFileSync, writeFileSync } from "node:fs";
import { spawn } from "node:child_process";
import net from "node:net";
import path from "node:path";
import { fileURLToPath } from "node:url";

const jsonIdx = process.argv.indexOf("--json");
const jsonOutputPath = jsonIdx >= 0 ? process.argv[jsonIdx + 1] : null;
let executed = 0;
let passed = 0;
let failed = 0;

function recordPass() {
  executed += 1;
  passed += 1;
}

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const appRoot = path.resolve(__dirname, "..");
const repoRoot = path.resolve(appRoot, "../..");
const distAssetsDir = path.join(appRoot, "dist", "assets");

function assertZeroMocksInDist() {
  const jsFiles = readdirSync(distAssetsDir).filter((f) => f.endsWith(".js"));
  if (jsFiles.length === 0) {
    throw new Error(`No JS assets found under ${distAssetsDir}. Run npm run build first.`);
  }
  const forbidden = /\b(MOCKED_UI|mockCafeClient|mockBackofficeClient|GCash|PayMaya)\b|412,850/;
  for (const file of jsFiles) {
    const content = readFileSync(path.join(distAssetsDir, file), "utf8");
    const match = forbidden.exec(content);
    if (match) {
      throw new Error(`Forbidden mock or non-approved tender marker "${match[0]}" found in ${file}`);
    }
  }
  console.log(`PASS dist zero-mock check across ${jsFiles.length} JS bundle(s).`);
  recordPass();
}

function getFreePort() {
  return new Promise((resolve, reject) => {
    const server = net.createServer();
    server.listen(0, "127.0.0.1", () => {
      const addr = server.address();
      if (!addr || typeof addr === "string") {
        server.close(() => reject(new Error("Could not resolve ephemeral port.")));
        return;
      }
      const { port } = addr;
      server.close(() => resolve(port));
    });
    server.on("error", reject);
  });
}

async function waitForHttpReady(url, timeoutMs = 15000) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    try {
      const res = await fetch(url);
      if (res.ok) return;
    } catch {
      // Server still starting.
    }
    await new Promise((r) => setTimeout(r, 200));
  }
  throw new Error(`Timed out waiting for Vite preview at ${url}`);
}

assertZeroMocksInDist();

const require = createRequire(path.join(repoRoot, "src", "Salryn.App.Web", "package.json"));
const { chromium, expect } = require("@playwright/test");
const axeSource = readFileSync(path.join(appRoot, "node_modules", "axe-core", "axe.min.js"), "utf8");

const port = await getFreePort();
const viteBin = path.join(appRoot, "node_modules", "vite", "bin", "vite.js");
const preview = spawn(process.execPath, [viteBin, "preview", "--host", "127.0.0.1", "--port", String(port), "--strictPort"], {
  cwd: appRoot,
  stdio: "ignore",
});

const baseUrl = `http://127.0.0.1:${port}/cafe`;

try {
  await waitForHttpReady(`${baseUrl}/`);
  const browser = await chromium.launch();
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await context.newPage();
  const pageErrors = [];
  const hostCallsOnCentral = [];

  page.on("pageerror", (err) => pageErrors.push(err.message));
  page.on("request", (req) => {
    if (req.url().includes(":5399") || new URL(req.url()).pathname.startsWith("/api/backups") || new URL(req.url()).pathname.startsWith("/api/license")) {
      hostCallsOnCentral.push(req.url());
    }
  });

  try {
    // 1. Verify /cafe/ workstation sign-in, brand image integrity, responsiveness (1440/768/320px), and axe-core WCAG AA
    await page.goto(`${baseUrl}/`);
    await expect(page.getByRole("heading", { name: "Cafe workstation", exact: true })).toBeVisible();
    const logo = page.getByAltText("Salryn Cafe logo");
    await expect(logo).toBeVisible();
    const logoInfo = await logo.evaluate(async (img) => {
      if (!img.complete) {
        await new Promise((resolve) => {
          img.addEventListener("load", resolve, { once: true });
          img.addEventListener("error", resolve, { once: true });
        });
      }
      return { complete: img.complete, naturalWidth: img.naturalWidth, src: img.getAttribute("src") };
    });
    if (!logoInfo.complete || logoInfo.naturalWidth <= 0) {
      throw new Error(`Salryn Cafe logo failed to render cleanly: ${JSON.stringify(logoInfo)}`);
    }
    recordPass();

    for (const width of [1440, 768, 320]) {
      await page.setViewportSize({ width, height: 900 });
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
      if (overflow) throw new Error(`Horizontal overflow detected on /cafe/ at width ${width}px.`);
      recordPass();
    }

    await page.setViewportSize({ width: 1440, height: 900 });
    await page.addScriptTag({ content: axeSource });
    const axeWorkstation = await page.evaluate(async () => await window.axe.run(document));
    const seriousWorkstation = axeWorkstation.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
    if (seriousWorkstation.length > 0) {
      throw new Error(`Workstation axe-core violations: ${JSON.stringify(seriousWorkstation.map((v) => v.id))}`);
    }
    recordPass();

    // 2. Verify /cafe/central renders Central Owner Portal in strict surface isolation (zero local Host probes + gated Host Backup/License tab)
    hostCallsOnCentral.length = 0;
    await page.goto(`${baseUrl}/central`);
    await expect(page.getByRole("heading", { name: /Device Trust vs\. Peripheral Separation/i })).toBeVisible().catch(async () => {
      await expect(page.getByText(/Central Server Connection & Admin Sign-In/i)).toBeVisible();
    });
    if (hostCallsOnCentral.length > 0) {
      throw new Error(`Central Owner Portal illegally probed host-only endpoints: ${hostCallsOnCentral.join(", ")}`);
    }
    recordPass();
    await page.getByRole("button", { name: "Host Backup & License", exact: true }).click();
    await expect(page.getByText(/Local Windows Host Only/i)).toBeVisible();
    recordPass();

    await page.addScriptTag({ content: axeSource });
    const axeCentral = await page.evaluate(async () => await window.axe.run(document));
    const seriousCentral = axeCentral.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
    if (seriousCentral.length > 0) {
      throw new Error(`Central Portal axe-core violations: ${JSON.stringify(seriousCentral.map((v) => v.id))}`);
    }
    recordPass();

    // 3. The zero-mock preview does not provide a trusted signed Host license.
    // Verify that Backoffice FAILS CLOSED before sign-in rather than expecting a
    // login UI in a genuinely unlicensed environment.
    await page.goto(`${baseUrl}/backoffice`);
    await expect(page.getByRole("heading", { name: "Host License Activation Required", exact: true })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Back-Office Sign In", exact: true })).toHaveCount(0);
    recordPass();
    await page.getByRole("button", { name: "Open License Activation", exact: true }).click();
    await expect(page.getByRole("heading", { name: "Cafe workstation", exact: true })).toBeVisible();
    recordPass();

    if (pageErrors.length > 0) {
      throw new Error(`Uncaught page errors: ${pageErrors.join("; ")}`);
    }
    console.log(`PASS /cafe/ Playwright E2E (${passed}/${executed} checks passed: zero mocks, /cafe/, /cafe/central surface isolation, /cafe/backoffice navigation, 320-1440px responsive, axe-core 0 critical/serious violations).`);
  } catch (err) {
    failed += 1;
    executed = Math.max(executed, passed + failed);
    throw err;
  } finally {
    if (jsonOutputPath) {
      writeFileSync(jsonOutputPath, JSON.stringify({ executed, passed, failed }, null, 2), "utf8");
    }
    await browser.close();
  }
} finally {
  preview.kill();
}
