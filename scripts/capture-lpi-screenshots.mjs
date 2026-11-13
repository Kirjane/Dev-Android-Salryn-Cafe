import { spawn } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { createHash } from "node:crypto";
import { inflateSync } from "node:zlib";

const baseUrl = process.argv[2] || "http://127.0.0.1:5299";
const sessionId = process.argv[3] || "";
const outputDir = process.argv[4] || path.resolve("00-HANDOFF/evidence/lpi");

if (!fs.existsSync(outputDir)) {
  fs.mkdirSync(outputDir, { recursive: true });
}

const edgePath = "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe";
const chromePath = "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe";
const browserExe = fs.existsSync(edgePath) ? edgePath : chromePath;

const cdpPort = 9444;
const profileDir = path.join(process.env.TEMP || "C:\\Temp", `salryn-cdp-profile-${Date.now()}`);

console.log(`[LPI Screenshot Runner] Spawning browser: ${browserExe}`);
console.log(`[LPI Screenshot Runner] Target Base URL: ${baseUrl}`);
console.log(`[LPI Screenshot Runner] Session ID: ${sessionId ? "PRESENT" : "NONE"}`);
console.log(`[LPI Screenshot Runner] Output dir: ${outputDir}`);

const browserProc = spawn(browserExe, [
  "--headless=new",
  `--remote-debugging-port=${cdpPort}`,
  `--user-data-dir=${profileDir}`,
  "--window-size=1440,900",
  "--hide-scrollbars",
  "--disable-gpu",
  "--no-first-run",
  "--no-default-browser-check"
]);

browserProc.on("error", (err) => {
  console.error("[LPI Screenshot Runner] Browser process error:", err);
});

async function sleep(ms) {
  return new Promise((res) => setTimeout(res, ms));
}

async function waitForCdp(retries = 30) {
  for (let i = 0; i < retries; i++) {
    try {
      const res = await fetch(`http://127.0.0.1:${cdpPort}/json/list`);
      if (res.ok) {
        const list = await res.json();
        const page = list.find((item) => item.type === "page");
        if (page && page.webSocketDebuggerUrl) {
          return page.webSocketDebuggerUrl;
        }
      }
    } catch {
      // retry
    }
    await sleep(300);
  }
  throw new Error("Failed to connect to browser CDP within timeout.");
}

// The first seven captures are conditional tabs; AP is a separate protected route.
// Each marker below is rendered in the current UI at the pinned baseline SHA.
const procurementHeading = "Logistics, Procurement & Inventory";
const targets = [
  {
    page: "procure-to-pay", tab: "overview",
    file: "01_lpi_overview_dashboard.png", label: "Overview & P2P Metrics",
    headings: [procurementHeading, "Accounts Payable Aging Schedule"],
    text: "Total Accounts Payable"
  },
  {
    page: "procure-to-pay", tab: "suppliers",
    file: "02_lpi_supplier_directory.png", label: "Supplier Directory & Scorecards",
    headings: [procurementHeading, "Supplier Directory", "Terms"]
  },
  {
    page: "procure-to-pay", tab: "pr",
    file: "03_lpi_purchase_requisitions.png", label: "Purchase Requisitions & Approvals",
    headings: [procurementHeading, "Purchase Requisitions (PR)", "PR #"]
  },
  {
    page: "procure-to-pay", tab: "quotations",
    file: "04_lpi_quotations_comparison.png", label: "RFQ Quotations & Price Comparison",
    headings: [procurementHeading, "Supplier Quotations & Quote Comparison"],
    text: "Please select a Purchase Requisition above to view or compare supplier quotations."
  },
  {
    page: "procure-to-pay", tab: "po",
    file: "05_lpi_purchase_orders.png", label: "Purchase Orders Lifecycle",
    headings: [procurementHeading, "Purchase Orders (PO)", "PO #"]
  },
  {
    page: "procure-to-pay", tab: "rr",
    file: "06_lpi_goods_receiving.png", label: "Goods Receiving & Inspection",
    headings: [procurementHeading, "Goods Receiving Reports (RR)", "RR #"]
  },
  {
    page: "procure-to-pay", tab: "inventory",
    file: "07_lpi_inventory_stock_cards.png", label: "Inventory Stock Cards & Quarantine",
    headings: [procurementHeading, "Multi-Location Stock Cards & Valuation", "Per-Product Stock Cards & Location Buckets"]
  },
  {
    page: "accounts-payable", tab: null,
    file: "08_lpi_ap_3way_matching.png", label: "Accounts Payable 3-Way Match & Payments",
    headings: ["Accounts Payable", "Supplier Invoice Register", "Supplier Payments & Disbursements"],
    text: "APV / Invoice"
  }
];

const targetKeys = targets.map((t) => t.page + ":" + (t.tab ?? ""));
if (targets.length !== 8 ||
    new Set(targetKeys).size !== 8 ||
    new Set(targets.map((t) => t.file)).size !== 8) {
  throw new Error("FAIL-CLOSED: LPI evidence targets must identify exactly eight unique page/tab and filename pairs.");
}

const normalize = (value) => String(value ?? "").replace(/\s+/g, " ").trim().toLowerCase();

function targetUrl(target) {
  const url = new URL("/", baseUrl);
  url.searchParams.set("page", target.page);
  if (target.tab) url.searchParams.set("tab", target.tab);
  return url;
}

function inspectPage(snapshot, target, expectedUrl) {
  const faults = [];
  if (!snapshot || snapshot.readyState !== "complete") faults.push("document not completely loaded");
  if (!snapshot?.hasMain || !snapshot.mainVisible || !snapshot.mainText?.trim()) {
    faults.push("main content missing, hidden, or blank");
  }
  if (/This workspace could not be displayed|Something went wrong|Salryn POS unavailable|Access denied/i.test(snapshot?.bodyText ?? "")) {
    faults.push("error boundary, unavailable app, or access-denied page");
  }

  try {
    const actual = new URL(snapshot?.url ?? "");
    if (actual.origin !== expectedUrl.origin || actual.pathname !== expectedUrl.pathname ||
        actual.searchParams.getAll("page").length !== 1 ||
        actual.searchParams.get("page") !== target.page ||
        actual.searchParams.getAll("tab").length !== (target.tab ? 1 : 0) ||
        (target.tab && actual.searchParams.get("tab") !== target.tab)) {
      faults.push("wrong location: " + actual.href + " (expected " + expectedUrl.href + ")");
    }
  } catch {
    faults.push("invalid browser location");
  }

  // Only visible headings and table headers inside <main> count: never sidebar/menu labels.
  const renderedHeadings = new Set((snapshot?.headings ?? []).map(normalize));
  for (const heading of target.headings) {
    if (!renderedHeadings.has(normalize(heading))) faults.push("missing main heading/header: " + heading);
  }
  if (target.text && !normalize(snapshot?.mainText).includes(normalize(target.text))) {
    faults.push("missing page-specific content: " + target.text);
  }
  return faults;
}

function inspectPng(base64, label) {
  if (typeof base64 !== "string" || !base64.length) throw new Error("FAIL-CLOSED: " + label + " did not return screenshot data.");
  const png = Buffer.from(base64, "base64");
  if (png.length < 4096 || !png.subarray(0, 8).equals(Buffer.from([137,80,78,71,13,10,26,10]))) {
    throw new Error("FAIL-CLOSED: " + label + " screenshot is empty or not a valid PNG signature.");
  }
  let offset = 8, width = 0, height = 0, complete = false;
  const idat = [];
  while (offset + 12 <= png.length) {
    const length = png.readUInt32BE(offset);
    const type = png.toString("ascii", offset + 4, offset + 8);
    if (length > png.length - offset - 12) throw new Error("FAIL-CLOSED: " + label + " PNG chunk is truncated.");
    if (type === "IHDR") {
      if (length !== 13) throw new Error("FAIL-CLOSED: " + label + " PNG IHDR invalid.");
      width = png.readUInt32BE(offset + 8);
      height = png.readUInt32BE(offset + 12);
    }
    if (type === "IDAT") idat.push(png.subarray(offset + 8, offset + 8 + length));
    offset += length + 12;
    if (type === "IEND") { complete = true; break; }
  }
  if (!complete || offset !== png.length || width < 1000 || height < 650 || idat.length === 0) {
    throw new Error("FAIL-CLOSED: " + label + " PNG dimensions/chunks are invalid.");
  }
  try {
    if (inflateSync(Buffer.concat(idat)).length < 100000) throw new Error("insufficient decoded raster data");
  } catch (err) {
    throw new Error("FAIL-CLOSED: " + label + " PNG raster cannot be inflated: " + err);
  }
  return { png, width, height, sha256: createHash("sha256").update(png).digest("hex") };
}

async function run() {
  const unhandledExceptions = [];
  const consoleErrors = [];
  const networkFailures = [];
  const pendingApiRequests = new Set();
  let lastApiActivityAt = Date.now();
  let currentTab = "init";
  const stagedDir = path.join(outputDir, ".staging-lpi-" + process.pid + "-" + Date.now());
  let completed = false;
  const hashes = new Set();
  // Stale evidence must not survive a failed capture attempt.
  for (const t of targets) fs.rmSync(path.join(outputDir, t.file), { force: true });
  fs.mkdirSync(stagedDir, { recursive: true });

  try {
    const wsUrl = await waitForCdp();
    console.log(`[LPI Screenshot Runner] Connected to CDP page: ${wsUrl}`);

    const ws = new WebSocket(wsUrl);
    await new Promise((resolve, reject) => {
      ws.onopen = resolve;
      ws.onerror = reject;
    });

    let msgId = 1;
    function send(method, params = {}) {
      return new Promise((resolve, reject) => {
        const currentId = msgId++;
        const onMessage = (event) => {
          try {
            const data = JSON.parse(event.data);
            if (data.id === currentId) {
              ws.removeEventListener("message", onMessage);
              if (data.error) {
                reject(new Error(`CDP ${method} error: ${JSON.stringify(data.error)}`));
              } else {
                resolve(data.result);
              }
            }
          } catch (e) {
            // ignore
          }
        };
        ws.addEventListener("message", onMessage);
        ws.send(JSON.stringify({ id: currentId, method, params }));
      });
    }

    // Passive listeners preserve global fail-closed JavaScript, console and HTTP gates.
    ws.addEventListener("message", (event) => {
      try {
        const data = JSON.parse(event.data);
        if (data.method === "Runtime.exceptionThrown") {
          const exc = data.params?.exceptionDetails;
          const msg = exc?.exception?.description || exc?.text || "Unknown JS exception";
          unhandledExceptions.push({ tab: currentTab, error: msg });
          console.error("[CDP UNHANDLED EXCEPTION] [" + currentTab + "]:", msg);
        } else if (data.method === "Log.entryAdded") {
          const entry = data.params?.entry;
          if (entry?.level === "error" && !entry.text?.includes("favicon.ico")) {
            consoleErrors.push({ tab: currentTab, text: entry.text });
            console.error("[CDP CONSOLE ERROR] [" + currentTab + "]:", entry.text);
          }
        } else if (data.method === "Network.requestWillBeSent") {
          const requestUrl = data.params?.request?.url ?? "";
          if (requestUrl.startsWith(new URL(baseUrl).origin + "/api/")) {
            pendingApiRequests.add(data.params.requestId);
            lastApiActivityAt = Date.now();
          }
        } else if (data.method === "Network.responseReceived") {
          const res = data.params?.response;
          if (res) {
            const url = new URL(res.url);
            if (res.status >= 500 ||
                (res.status >= 400 && url.origin === new URL(baseUrl).origin &&
                 !url.pathname.endsWith("/favicon.ico"))) {
              networkFailures.push({ tab: currentTab, url: res.url, status: res.status });
              console.error("[CDP HTTP FAILURE] [" + currentTab + "]: " + res.url + " -> " + res.status);
            }
            if (pendingApiRequests.has(data.params?.requestId)) lastApiActivityAt = Date.now();
          }
        } else if (data.method === "Network.loadingFinished" || data.method === "Network.loadingFailed") {
          if (pendingApiRequests.delete(data.params?.requestId)) {
            lastApiActivityAt = Date.now();
            if (data.method === "Network.loadingFailed") {
              networkFailures.push({ tab: currentTab, error: data.params?.errorText ?? "API request failed" });
            }
          }
        }
      } catch {
        // Ignore malformed CDP event envelopes only; captured faults remain fatal.
      }
    });

    await send("Page.enable");
    await send("Runtime.enable");
    await send("Log.enable");
    await send("Network.enable");

    if (sessionId) {
      console.log("[LPI Screenshot Runner] Setting authentication cookie salryn.sid...");
      await send("Network.setCookie", {
        name: "salryn.sid",
        value: sessionId,
        domain: "127.0.0.1",
        path: "/",
        httpOnly: true,
        sameSite: "Lax"
      });
    }

    const pageStateExpression = '(() => { const main = document.querySelector("main"); return { readyState: document.readyState, url: location.href, hasMain: !!main, mainVisible: !!main && main.getClientRects().length > 0, mainText: main?.innerText ?? "", bodyText: document.body?.innerText ?? "", headings: main ? Array.from(main.querySelectorAll("h1,h2,h3,h4,th")).filter((el) => el.getClientRects().length > 0 && getComputedStyle(el).visibility !== "hidden").map((el) => el.innerText || el.textContent || "") : [] }; })()';

    for (const t of targets) {
      currentTab = t.tab ?? t.page;
      const url = targetUrl(t);
      console.log("[LPI Screenshot Runner] Navigating to " + t.label + " -> " + url.href);
      const navigation = await send("Page.navigate", { url: url.href });
      if (navigation?.errorText) throw new Error("FAIL-CLOSED: " + currentTab + " navigation failed: " + navigation.errorText);

      // Wait for the requested route, visible content and API network settling.
      // A fixed sleep alone is not evidence of a rendered page.
      const startedAt = Date.now();
      let verified = false;
      let stableSamples = 0;
      let lastFaults = [];
      while (Date.now() - startedAt < 25000) {
        if (unhandledExceptions.length || consoleErrors.length || networkFailures.length) {
          throw new Error("FAIL-CLOSED: browser/runtime/network errors: " +
            JSON.stringify({ unhandledExceptions, consoleErrors, networkFailures }));
        }
        const evaluated = await send("Runtime.evaluate", {
          expression: pageStateExpression, returnByValue: true
        });
        if (evaluated?.exceptionDetails) {
          throw new Error("FAIL-CLOSED: page inspection threw: " + JSON.stringify(evaluated.exceptionDetails));
        }
        const snapshot = evaluated?.result?.value;
        lastFaults = inspectPage(snapshot, t, url);
        const idle = pendingApiRequests.size === 0 && Date.now() - lastApiActivityAt >= 600;
        if (lastFaults.length === 0 && idle) {
          if (++stableSamples >= 2) { verified = true; break; }
        } else {
          stableSamples = 0;
        }
        await sleep(250);
      }
      if (!verified) {
        throw new Error("FAIL-CLOSED: " + currentTab + " did not render its unique page within 25s; " +
          JSON.stringify({ faults: lastFaults, pendingApiRequests: pendingApiRequests.size }));
      }
      if (unhandledExceptions.length || consoleErrors.length || networkFailures.length) {
        throw new Error("FAIL-CLOSED: " + currentTab + " logged runtime/network errors before capture.");
      }

      const shot = await send("Page.captureScreenshot", {
        format: "png", captureBeyondViewport: false
      });
      const { png, width, height, sha256 } = inspectPng(shot?.data, currentTab);
      if (hashes.has(sha256)) {
        throw new Error("FAIL-CLOSED: " + currentTab + " duplicated another screenshot (" + sha256 + ").");
      }
      hashes.add(sha256);
      const dest = path.join(stagedDir, t.file);
      fs.writeFileSync(dest, png);
      console.log("[LPI Screenshot Runner] STAGED " + currentTab + " " + width + "x" + height +
        " " + png.length + " bytes SHA256=" + sha256 + " URL=" + url.href);
    }

    if (hashes.size !== 8) throw new Error("FAIL-CLOSED: expected eight distinct, validated PNGs.");
    if (unhandledExceptions.length || consoleErrors.length || networkFailures.length) {
      throw new Error("FAIL-CLOSED: capture completed with browser errors: " +
        JSON.stringify({ unhandledExceptions, consoleErrors, networkFailures }));
    }
    for (const t of targets) {
      const dest = path.join(outputDir, t.file);
      fs.renameSync(path.join(stagedDir, t.file), dest);
      console.log("[LPI Screenshot Runner] WROTE: " + dest + " (" + (fs.statSync(dest).size / 1024).toFixed(1) + " KB)");
    }
    completed = true;

    if (unhandledExceptions.length > 0) {
      throw new Error(`FAIL-CLOSED: Run completed with ${unhandledExceptions.length} uncaught exceptions: ${JSON.stringify(unhandledExceptions)}`);
    }

    if (consoleErrors.length > 0) {
      throw new Error(`FAIL-CLOSED: Run completed with ${consoleErrors.length} console error(s): ${JSON.stringify(consoleErrors)}`);
    }

    if (networkFailures.length > 0) {
      throw new Error(`FAIL-CLOSED: Run completed with ${networkFailures.length} HTTP 5xx responses: ${JSON.stringify(networkFailures)}`);
    }

    console.log("[LPI Screenshot Runner] All 8 unique page targets verified fail-closed and PNGs captured successfully.");
    ws.close();
  } finally {
    if (!completed) for (const t of targets) fs.rmSync(path.join(outputDir, t.file), { force: true });
    fs.rmSync(stagedDir, { recursive: true, force: true });
    browserProc.kill();
    try {
      fs.rmSync(profileDir, { recursive: true, force: true });
    } catch {
      // ignore
    }
  }
}

run()
  .then(() => process.exit(0))
  .catch((err) => {
    console.error("[LPI Screenshot Runner] FATAL:", err);
    process.exit(1);
  });
