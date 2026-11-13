import { spawn } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import os from "node:os";
import { fileURLToPath } from "node:url";
import { createHash } from "node:crypto";
import { inflateSync } from "node:zlib";

const baseUrl = process.argv[2] || "http://127.0.0.1:5299";
const sessionId = process.argv[3] || "";
const outputDir = process.argv[4] || path.resolve("00-HANDOFF/evidence/lpi");

const edgePath = "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe";
const chromePath = "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe";
const linuxBrowserPaths = ["/usr/bin/chromium", "/usr/bin/google-chrome", "/usr/bin/chromium-browser"];
export function resolveBrowserExecutable() {
  return [edgePath, chromePath, ...(process.platform === "win32" ? [] : linuxBrowserPaths)]
    .find((candidate) => fs.existsSync(candidate)) ?? chromePath;
}

const browserFlags = [
  "--headless=new",
  "--remote-debugging-port=0",
  "--window-size=1440,900",
  "--hide-scrollbars",
  "--disable-gpu",
  "--no-first-run",
  "--no-default-browser-check"
];

// The profile is created by this process, unpredictable and private (0700 on POSIX).
// No default profile or global fixed debugging port is ever discovered or touched.
export function launchOwnedBrowser(executable = resolveBrowserExecutable(), extraFlags = []) {
  const profileDir = fs.mkdtempSync(path.join(os.tmpdir(), "salryn-lpi-cdp-"));
  try {
    fs.chmodSync(profileDir, 0o700);
    const child = spawn(executable, [
      ...extraFlags, ...browserFlags, "--user-data-dir=" + profileDir
    ], { windowsHide: true, stdio: "ignore" });
    child.ownedLaunchError = null;
    child.ownedExit = null;
    child.on("error", (error) => { child.ownedLaunchError = error; });
    child.on("exit", (code, signal) => { child.ownedExit = { code, signal }; });
    return { child, profileDir };
  } catch (error) {
    fs.rmSync(profileDir, { recursive: true, force: true });
    throw error;
  }
}

function requireLiveOwnedChild(owner) {
  const child = owner?.child;
  if (!child || child.ownedLaunchError) {
    throw new Error("FAIL-CLOSED: owned browser failed to spawn: " +
      (child?.ownedLaunchError?.code ?? "no child"));
  }
  if (!Number.isInteger(child.pid) || child.pid <= 0 ||
      child.exitCode !== null || child.signalCode !== null || child.ownedExit) {
    throw new Error("FAIL-CLOSED: owned browser did not remain alive.");
  }
  try { process.kill(child.pid, 0); }
  catch { throw new Error("FAIL-CLOSED: owned browser PID is no longer running."); }
}

export function readOwnedDevToolsPort(profileDir) {
  const portFile = path.join(profileDir, "DevToolsActivePort");
  if (!fs.existsSync(portFile)) return null;
  const stat = fs.lstatSync(portFile);
  if (!stat.isFile() || stat.isSymbolicLink() || stat.size > 512 || stat.size < 30) {
    throw new Error("FAIL-CLOSED: owned DevToolsActivePort is not a regular bounded file.");
  }
  const contents = fs.readFileSync(portFile, "utf8");
  const match = /^(\d{1,5})\r?\n(\/devtools\/browser\/[A-Za-z0-9-]{16,128})\r?\n?$/.exec(contents);
  if (!match) throw new Error("FAIL-CLOSED: malformed owned DevToolsActivePort file.");
  const port = Number(match[1]);
  if (!Number.isInteger(port) || port < 1 || port > 65535) {
    throw new Error("FAIL-CLOSED: invalid owned CDP port.");
  }
  return { port, browserPath: match[2],
    browserWebSocket: "ws://127.0.0.1:" + port + match[2] };
}

export async function waitForOwnedCdp(owner, timeoutMs = 12000) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    requireLiveOwnedChild(owner);
    const portInfo = readOwnedDevToolsPort(owner.profileDir);
    if (portInfo) {
      try {
        const response = await fetch("http://127.0.0.1:" + portInfo.port + "/json/version", {
          signal: AbortSignal.timeout(1200),
        });
        if (!response.ok) throw new Error("CDP version returned HTTP " + response.status);
        const version = await response.json();
        requireLiveOwnedChild(owner);
        if (!/^(?:Chrome|Chromium|HeadlessChrome|Edg)\//.test(version?.Browser ?? "") ||
            version?.webSocketDebuggerUrl !== portInfo.browserWebSocket) {
          throw new Error("FAIL-CLOSED: browser-owned CDP endpoint or identity mismatch.");
        }
        console.log("[LPI Screenshot Runner] CDP_OWNERSHIP_VERIFIED pid=" + owner.child.pid +
          " profile=" + owner.profileDir + " port=" + portInfo.port +
          " browserIdSha256=" + createHash("sha256").update(portInfo.browserPath).digest("hex").slice(0, 16));
        return portInfo;
      } catch (error) {
        if (/FAIL-CLOSED: browser-owned CDP endpoint or identity mismatch|owned browser/i.test(String(error))) {
          throw error;
        }
        // A newly spawned browser can write the file before its private listener is ready.
      }
    }
    await sleep(100);
  }
  requireLiveOwnedChild(owner);
  throw new Error("FAIL-CLOSED: timed out waiting for the spawned browser's owned CDP endpoint.");
}

async function connectOwnedWebSocket(url, owner, timeoutMs = 5000) {
  requireLiveOwnedChild(owner);
  const socket = new WebSocket(url);
  try {
    await new Promise((resolve, reject) => {
      const timeout = setTimeout(() => reject(new Error("FAIL-CLOSED: owned CDP socket timeout.")), timeoutMs);
      socket.addEventListener("open", () => { clearTimeout(timeout); resolve(); }, { once: true });
      socket.addEventListener("error", () => { clearTimeout(timeout); reject(new Error("FAIL-CLOSED: owned CDP socket failed.")); }, { once: true });
      socket.addEventListener("close", () => { clearTimeout(timeout); reject(new Error("FAIL-CLOSED: owned CDP socket closed during startup.")); }, { once: true });
    });
    requireLiveOwnedChild(owner);
    return socket;
  } catch (error) {
    socket.close();
    throw error;
  }
}

// Create a page from the verified *browser* CDP socket. No /json/list first-page trust.
export async function createOwnedPageEndpoint(owner, portInfo) {
  requireLiveOwnedChild(owner);
  const browserSocket = await connectOwnedWebSocket(portInfo.browserWebSocket, owner);
  try {
    const targetId = await new Promise((resolve, reject) => {
      const timeout = setTimeout(() => reject(new Error("FAIL-CLOSED: owned CDP Target.createTarget timeout.")), 5000);
      const listener = (event) => {
        let reply;
        try { reply = JSON.parse(event.data); } catch { return; }
        if (reply.id !== 1) return;
        clearTimeout(timeout);
        browserSocket.removeEventListener("message", listener);
        if (reply.error || !/^[A-Fa-f0-9]{16,64}$/.test(reply.result?.targetId ?? "")) {
          reject(new Error("FAIL-CLOSED: owned browser returned invalid target ID."));
        } else {
          resolve(reply.result.targetId);
        }
      };
      browserSocket.addEventListener("message", listener);
      browserSocket.send(JSON.stringify({
        id: 1, method: "Target.createTarget", params: { url: "about:blank" }
      }));
    });
    requireLiveOwnedChild(owner);
    return "ws://127.0.0.1:" + portInfo.port + "/devtools/page/" + targetId;
  } finally {
    browserSocket.close();
  }
}

export async function disposeOwnedBrowser(owner) {
  if (!owner) return;
  const child = owner.child;
  if (child?.pid && child.exitCode === null && child.signalCode === null) {
    try { child.kill(); } catch { /* already exited */ }
    await Promise.race([
      new Promise((resolve) => child.once("close", resolve)),
      sleep(2500)
    ]);
  }
  // Never delete, signal or enumerate any browser/profile not created by this run.
  fs.rmSync(owner.profileDir, {
    recursive: true, force: true, maxRetries: 10, retryDelay: 200
  });
}

async function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

// The first seven captures are conditional tabs; AP is a separate protected route.
// Each marker below is rendered in the current UI at the pinned baseline SHA.
// The first seven captures are conditional tabs; AP is a separate protected route.
const procurementHeading = "Logistics, Procurement & Inventory";
const targets = [
  {
    page: "procure-to-pay", tab: "overview",
    file: "01_lpi_overview_dashboard.png", label: "Overview & P2P Metrics",
    headings: [procurementHeading, "Accounts Payable Aging Schedule"],
    text: "Apex Roasters & Dairy Supply Inc."
  },
  {
    page: "procure-to-pay", tab: "suppliers",
    file: "02_lpi_supplier_directory.png", label: "Supplier Directory & Scorecards",
    headings: [procurementHeading, "Supplier Directory", "Terms"],
    text: "SUP-APEX-01"
  },
  {
    page: "procure-to-pay", tab: "pr",
    file: "03_lpi_purchase_requisitions.png", label: "Purchase Requisitions & Approvals",
    headings: [procurementHeading, "Purchase Requisitions (PR)", "PR #"],
    text: "Store Operations"
  },
  {
    page: "procure-to-pay", tab: "quotations",
    file: "04_lpi_quotations_comparison.png", label: "RFQ Quotations & Price Comparison",
    headings: [procurementHeading, "Supplier Quotations & Quote Comparison", "Received Supplier Quotations", "Unit Price"],
    text: "Apex Roasters & Dairy Supply Inc."
  },
  {
    page: "procure-to-pay", tab: "po",
    file: "05_lpi_purchase_orders.png", label: "Purchase Orders Lifecycle",
    headings: [procurementHeading, "Purchase Orders (PO)", "PO #"],
    text: "Apex Roasters & Dairy Supply Inc."
  },
  {
    page: "procure-to-pay", tab: "rr",
    file: "06_lpi_goods_receiving.png", label: "Goods Receiving & Inspection",
    headings: [procurementHeading, "Goods Receiving Reports (RR)", "RR #"],
    text: "Metro Packaging Solutions Corp."
  },
  {
    page: "procure-to-pay", tab: "inventory",
    file: "07_lpi_inventory_stock_cards.png", label: "Inventory Stock Cards & Quarantine",
    headings: [procurementHeading, "Multi-Location Stock Cards & Valuation", "Per-Product Stock Cards & Location Buckets"],
    text: "Alpha Coffee Beans 1kg"
  },
  {
    page: "accounts-payable", tab: null,
    file: "08_lpi_ap_3way_matching.png", label: "Accounts Payable 3-Way Match & Payments",
    headings: ["Accounts Payable", "Supplier Invoice Register", "Supplier Payments & Disbursements"],
    text: "SI-APEX-2026-001"
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

export async function run(options = {}) {
  const browserExecutable = options.browserExecutable ?? resolveBrowserExecutable();
  const additionalBrowserFlags = options.additionalBrowserFlags ?? [];
  let owner = null;
  let pageSocket = null;
  if (!fs.existsSync(outputDir)) fs.mkdirSync(outputDir, { recursive: true });
  console.log("[LPI Screenshot Runner] Browser executable: " + browserExecutable);
  console.log("[LPI Screenshot Runner] Target base: " + baseUrl);
  console.log("[LPI Screenshot Runner] Synthetic session: " + (sessionId ? "PRESENT" : "NONE"));
  console.log("[LPI Screenshot Runner] Output: " + outputDir);
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
    owner = launchOwnedBrowser(browserExecutable, additionalBrowserFlags);
    const portInfo = await waitForOwnedCdp(owner);
    const wsUrl = await createOwnedPageEndpoint(owner, portInfo);
    // This gate must precede Network.setCookie and every navigation.
    requireLiveOwnedChild(owner);
    const ws = await connectOwnedWebSocket(wsUrl, owner);
    pageSocket = ws;

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
      requireLiveOwnedChild(owner);
      const cookieResult = await send("Network.setCookie", {
        name: "salryn.sid",
        value: sessionId,
        domain: "127.0.0.1",
        path: "/",
        httpOnly: true,
        sameSite: "Lax"
      });
      if (cookieResult?.success !== true) {
        throw new Error("FAIL-CLOSED: owned browser refused synthetic auth cookie.");
      }
      console.log("[LPI Screenshot Runner] SYNTHETIC_COOKIE_SET_OWNED_BROWSER");
    }

    // In the seeded fixture, a quote belongs to the seeded requisition. Select it
    // through the real React <select> before requiring quotation-row evidence.
    const chooseQuoteExpression = '(() => { const main = document.querySelector("main"); if (!main) return { selected: false, reason: "main not mounted" }; const title = Array.from(main.querySelectorAll("h2")).find((el) => el.textContent?.trim() === "Supplier Quotations & Quote Comparison"); const select = title?.parentElement?.querySelector("select"); if (!select) return { selected: false, reason: "quotation selection unavailable" }; const option = Array.from(select.options).find((el) => el.value && el.textContent?.includes("Store Operations")); if (!option) return { selected: false, reason: "synthetic requisition not loaded" }; const setter = Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, "value")?.set; if (!setter) return { selected: false, reason: "native select setter unavailable" }; setter.call(select, option.value); select.dispatchEvent(new Event("change", { bubbles: true })); return { selected: true, requisition: option.textContent }; })()';

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
      let quoteSelected = t.tab !== "quotations";
      while (Date.now() - startedAt < 25000) {
        if (unhandledExceptions.length || consoleErrors.length || networkFailures.length) {
          throw new Error("FAIL-CLOSED: browser/runtime/network errors: " +
            JSON.stringify({ unhandledExceptions, consoleErrors, networkFailures }));
        }
        if (!quoteSelected) {
          const choice = await send("Runtime.evaluate", {
            expression: chooseQuoteExpression, returnByValue: true
          });
          if (choice?.exceptionDetails) {
            throw new Error("FAIL-CLOSED: synthetic quote selection threw: " + JSON.stringify(choice.exceptionDetails));
          }
          const result = choice?.result?.value;
          if (!result?.selected) {
            lastFaults = ["cannot select fixture requisition: " + (result?.reason ?? "unknown")];
            await sleep(250);
            continue;
          }
          quoteSelected = true;
          console.log("[LPI Screenshot Runner] Selected fixture requisition: " + result.requisition);
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
      throw new Error(`FAIL-CLOSED: Run completed with ${networkFailures.length} HTTP failures: ${JSON.stringify(networkFailures)}`);
    }

    console.log("[LPI Screenshot Runner] All 8 unique page targets verified fail-closed and PNGs captured successfully.");
    ws.close();
  } finally {
    if (!completed) for (const t of targets) fs.rmSync(path.join(outputDir, t.file), { force: true });
    fs.rmSync(stagedDir, { recursive: true, force: true });
    try { pageSocket?.close(); } catch { /* already closed */ }
    await disposeOwnedBrowser(owner);
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  run().then(() => process.exit(0)).catch((error) => {
    console.error("[LPI Screenshot Runner] FATAL:", error);
    process.exit(1);
  });
}
