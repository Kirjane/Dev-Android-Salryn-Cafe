import { spawn } from "node:child_process";
import fs from "node:fs";
import path from "node:path";

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

const tabs = [
  {
    tab: "overview",
    file: "01_lpi_overview_dashboard.png",
    label: "Overview & P2P Metrics",
    expectedTexts: ["Procurement & Payables", "Accounts Payable Aging"]
  },
  {
    tab: "suppliers",
    file: "02_lpi_supplier_directory.png",
    label: "Supplier Directory & Scorecards",
    expectedTexts: ["Supplier Directory", "Terms"]
  },
  {
    tab: "pr",
    file: "03_lpi_purchase_requisitions.png",
    label: "Purchase Requisitions & Approvals",
    expectedTexts: ["Purchase Requisitions", "PR #"]
  },
  {
    tab: "quotations",
    file: "04_lpi_quotations_comparison.png",
    label: "RFQ Quotations & Price Comparison",
    expectedTexts: ["Supplier Quotations", "Unit Price"]
  },
  {
    tab: "po",
    file: "05_lpi_purchase_orders.png",
    label: "Purchase Orders Lifecycle",
    expectedTexts: ["Purchase Orders (PO)", "PO #"]
  },
  {
    tab: "rr",
    file: "06_lpi_goods_receiving.png",
    label: "Goods Receiving & Inspection",
    expectedTexts: ["Goods Receiving Reports (RR)", "RR #"]
  },
  {
    tab: "inventory",
    file: "07_lpi_inventory_stock_cards.png",
    label: "Inventory Stock Cards & Quarantine",
    expectedTexts: ["Multi-Location Stock Cards & Valuation", "Total Stock"]
  },
  {
    tab: "ap",
    file: "08_lpi_ap_3way_matching.png",
    label: "Accounts Payable 3-Way Match & Payments",
    expectedTexts: ["Supplier Invoices & 3-Way Match", "APV #"]
  }
];

async function run() {
  const unhandledExceptions = [];
  const consoleErrors = [];
  const networkFailures = [];

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

    // Passive listener for CDP runtime events
    ws.addEventListener("message", (event) => {
      try {
        const data = JSON.parse(event.data);
        if (data.method === "Runtime.exceptionThrown") {
          const exc = data.params?.exceptionDetails;
          const msg = exc?.exception?.description || exc?.text || "Unknown JS exception";
          unhandledExceptions.push({ tab: currentTab, error: msg });
          console.error(`[CDP UNHANDLED EXCEPTION] [${currentTab}]:`, msg);
        } else if (data.method === "Log.entryAdded") {
          const entry = data.params?.entry;
          if (entry?.level === "error") {
            console.error(`[CDP CONSOLE ERROR] [${currentTab}]:`, entry.text);
            consoleErrors.push({ tab: currentTab, text: entry.text });
          }
        } else if (data.method === "Network.responseReceived") {
          const res = data.params?.response;
          if (res && res.status >= 500) {
            console.error(`[CDP HTTP 5xx] [${currentTab}]: ${res.url} -> ${res.status}`);
            networkFailures.push({ tab: currentTab, url: res.url, status: res.status });
          }
        }
      } catch {
        // ignore
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

    let currentTab = "init";

    for (const t of tabs) {
      currentTab = t.tab;
      const url = `${baseUrl}/?page=procure-to-pay&tab=${t.tab}`;
      console.log(`[LPI Screenshot Runner] Navigating to ${t.label} -> ${url}`);
      await send("Page.navigate", { url });
      
      // Allow React rendering and data fetching to settle
      await sleep(2500);

      const pageTextRes = await send("Runtime.evaluate", { expression: "document.body.innerText" });
      const rawText = pageTextRes?.result?.value || "";

      console.log(`[LPI Screenshot Runner] rawText for ${t.tab} (length ${rawText.length}):\n${rawText}\n--- END ---`);

      // 1. Assert non-blank screen (fail closed on white screen)
      if (!rawText.trim()) {
        throw new Error(`FAIL-CLOSED: Tab '${t.tab}' rendered an empty / white screen!`);
      }

      // 2. Assert no React error boundary or fatal app crash text
      if (rawText.includes("This workspace could not be displayed") || rawText.includes("Something went wrong")) {
        throw new Error(`FAIL-CLOSED: Tab '${t.tab}' caught an unhandled React error boundary crash!`);
      }

      // 3. Assert tab-specific expected headings and content (case-insensitive to support CSS text-transform: uppercase)
      for (const expected of t.expectedTexts) {
        if (!rawText.toLowerCase().includes(expected.toLowerCase())) {
          throw new Error(`FAIL-CLOSED: Tab '${t.tab}' is missing expected content: '${expected}'! Content preview: ${rawText.slice(0, 200)}`);
        }
      }

      // 4. Assert no uncaught JS exceptions thrown during tab operation
      const tabExceptions = unhandledExceptions.filter((e) => e.tab === t.tab);
      if (tabExceptions.length > 0) {
        throw new Error(`FAIL-CLOSED: Tab '${t.tab}' threw ${tabExceptions.length} uncaught JS exception(s): ${JSON.stringify(tabExceptions)}`);
      }

      console.log(`[LPI Screenshot Runner] Tab '${t.tab}' verified healthy. Capturing screenshot...`);

      const shot = await send("Page.captureScreenshot", {
        format: "png",
        captureBeyondViewport: false
      });

      const dest = path.join(outputDir, t.file);
      fs.writeFileSync(dest, Buffer.from(shot.data, "base64"));
      const sizeKb = (fs.statSync(dest).size / 1024).toFixed(1);
      console.log(`[LPI Screenshot Runner] WROTE: ${dest} (${sizeKb} KB)`);
    }

    if (unhandledExceptions.length > 0) {
      throw new Error(`FAIL-CLOSED: Run completed with ${unhandledExceptions.length} uncaught exceptions: ${JSON.stringify(unhandledExceptions)}`);
    }

    if (networkFailures.length > 0) {
      throw new Error(`FAIL-CLOSED: Run completed with ${networkFailures.length} HTTP 5xx responses: ${JSON.stringify(networkFailures)}`);
    }

    console.log("[LPI Screenshot Runner] All 8 tabs verified fail-closed and screenshots captured successfully.");
    ws.close();
  } finally {
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
