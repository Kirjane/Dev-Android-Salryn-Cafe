import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { randomUUID } from "node:crypto";
import fs from "node:fs";
import http from "node:http";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { fileURLToPath, pathToFileURL } from "node:url";
import {
  launchOwnedBrowser, disposeOwnedBrowser, readOwnedDevToolsPort,
  waitForOwnedCdp, resolveBrowserExecutable
} from "../capture-lpi-screenshots.mjs";

const runnerPath = fileURLToPath(new URL("../capture-lpi-screenshots.mjs", import.meta.url));
const noSandbox = process.platform !== "win32" &&
  typeof process.getuid === "function" && process.getuid() === 0;

const listen = (server, port) => new Promise((resolve, reject) => {
  server.once("error", reject);
  server.listen(port, "127.0.0.1", () => {
    server.removeListener("error", reject);
    resolve(server.address().port);
  });
});
const close = (server) => new Promise((resolve) => server.close(resolve));

function runIsolated(executable, appBase, output, session) {
  const driver = [
    "process.argv[2]=process.env.LPI_TEST_HOST;",
    "process.argv[3]=process.env.LPI_TEST_SESSION;",
    "process.argv[4]=process.env.LPI_TEST_OUTPUT;",
    "const m=await import(process.env.LPI_TEST_RUNNER);",
    "try { await m.run({",
    "browserExecutable: process.env.LPI_TEST_BROWSER,",
    "additionalBrowserFlags: process.env.LPI_TEST_NOSANDBOX==='yes'?['--no-sandbox']:[]",
    "}); process.exitCode=0;",
    "} catch (error) { console.error('EXPECTED_FAIL_CLOSED:',error.message); process.exitCode=1; }"
  ].join("\n");
  return spawn(process.execPath, ["--input-type=module", "-e", driver], {
    env: { ...process.env, LPI_TEST_HOST: appBase, LPI_TEST_SESSION: session,
      LPI_TEST_OUTPUT: output, LPI_TEST_RUNNER: pathToFileURL(runnerPath).href,
      LPI_TEST_BROWSER: executable, LPI_TEST_NOSANDBOX: noSandbox ? "yes" : "no" },
    windowsHide: true, stdio: ["ignore", "pipe", "pipe"]
  });
}
const collect = (child, limitMs = 40000) => new Promise((resolve, reject) => {
  let stdout = "", stderr = "";
  const timer = setTimeout(() => { child.kill(); reject(new Error("test child timeout")); }, limitMs);
  child.stdout.on("data", (c) => { stdout += c.toString(); });
  child.stderr.on("data", (c) => { stderr += c.toString(); });
  child.once("error", (err) => { clearTimeout(timer); reject(err); });
  child.once("close", (code) => { clearTimeout(timer); resolve({ code, stdout, stderr }); });
});
const pngs = (dir) => fs.readdirSync(dir).filter((f) => f.endsWith(".png"));
const staging = (dir) => fs.readdirSync(dir).filter((f) => f.includes(".staging-lpi-"));

test("owned DevToolsActivePort rejects malformed and out-of-range data", () => {
  const profile = fs.mkdtempSync(path.join(os.tmpdir(), "lpi-malformed-"));
  try {
    for (const body of [
      "invalid\n/devtools/browser/01234567-89ab-cdef-0123-456789abcdef\n",
      "70000\n/devtools/browser/01234567-89ab-cdef-0123-456789abcdef\n",
      "9444\n/devtools/page/01234567890123456789012345678901\n",
      "9444\n/devtools/browser/short\n"
    ]) {
      fs.writeFileSync(path.join(profile, "DevToolsActivePort"), body);
      assert.throws(() => readOwnedDevToolsPort(profile), /FAIL-CLOSED/);
    }
  } finally { fs.rmSync(profile, { recursive: true, force: true }); }
});

test("failed child spawn aborts ownership discovery", async () => {
  const owner = launchOwnedBrowser(path.join(os.tmpdir(), "missing-browser-" + randomUUID()));
  try { await assert.rejects(waitForOwnedCdp(owner, 1500), /FAIL-CLOSED/); }
  finally { await disposeOwnedBrowser(owner); }
});

test("early-exit browser aborts ownership discovery", async () => {
  // Node rejects Chromium flags and exits immediately.
  const owner = launchOwnedBrowser(process.execPath);
  try { await assert.rejects(waitForOwnedCdp(owner, 2500), /FAIL-CLOSED/); }
  finally { await disposeOwnedBrowser(owner); }
});

test("mismatched browser-level WebSocket ID fails closed", async () => {
  const server = http.createServer((_req, res) => {
    res.writeHead(200, { "content-type": "application/json" });
    res.end(JSON.stringify({ Browser: "Chrome/999.0",
      webSocketDebuggerUrl: "ws://127.0.0.1:1/devtools/browser/aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee" }));
  });
  const port = await listen(server, 0);
  const profileDir = fs.mkdtempSync(path.join(os.tmpdir(), "lpi-mismatch-"));
  const child = spawn(process.execPath, ["-e", "setInterval(() => {}, 3000)"], { stdio: "ignore" });
  const owner = { child, profileDir };
  try {
    fs.writeFileSync(path.join(profileDir, "DevToolsActivePort"),
      port + "\n/devtools/browser/01234567-89ab-cdef-0123-456789abcdef\n");
    await assert.rejects(waitForOwnedCdp(owner, 2000), /identity mismatch/);
  } finally { await disposeOwnedBrowser(owner); await close(server); }
});

test("occupied 9444 cannot receive cookie; spawned browser owns independent endpoint", { timeout: 45000 }, async () => {
  let connections = 0, requests = 0;
  const unrelated = http.createServer((_req, res) => {
    requests++;
    res.writeHead(200, { "content-type": "application/json" });
    res.end(JSON.stringify([{ type: "page", webSocketDebuggerUrl: "ws://127.0.0.1:9444/devtools/page/fake" }]));
  });
  unrelated.on("connection", () => { connections++; });
  assert.equal(await listen(unrelated, 9444), 9444);
  const host = http.createServer((_req, res) => {
    res.writeHead(404, { "content-type": "text/html" });
    res.end("<h1>Not a real Salryn host</h1>");
  });
  const hostPort = await listen(host, 0);
  const output = fs.mkdtempSync(path.join(os.tmpdir(), "lpi-occupied-"));
  const session = "SYNTHETIC_NOT_REAL_" + randomUUID();
  try {
    const executable = resolveBrowserExecutable();
    assert.ok(fs.existsSync(executable), "A real Chromium/Edge executable is required");
    const result = await collect(runIsolated(executable, "http://127.0.0.1:" + hostPort, output, session));
    assert.notEqual(result.code, 0, "deliberate invalid host must fail closed");
    assert.match(result.stdout, /CDP_OWNERSHIP_VERIFIED pid=\d+ profile=.* port=\d+/);
    assert.match(result.stdout, /SYNTHETIC_COOKIE_SET_OWNED_BROWSER/);
    assert.match(result.stderr, /EXPECTED_FAIL_CLOSED:/);
    assert.equal(connections, 0);
    assert.equal(requests, 0);
    assert.equal(pngs(output).length, 0);
    assert.equal(staging(output).length, 0);
    assert.ok(!result.stdout.includes(session) && !result.stderr.includes(session));
    console.log("OCCUPIED_9444_ISOLATION_PASS: listener connections=0 requests=0 screenshots=0");
  } finally {
    fs.rmSync(output, { recursive: true, force: true });
    await close(host); await close(unrelated);
  }
});

test("failed runner launch with 9444 occupied never sets session cookie", { timeout: 12000 }, async () => {
  let connections = 0;
  const unrelated = http.createServer((_req, res) => { res.writeHead(200); res.end("fake"); });
  unrelated.on("connection", () => { connections++; });
  await listen(unrelated, 9444);
  const output = fs.mkdtempSync(path.join(os.tmpdir(), "lpi-spawn-fail-"));
  const session = "SYNTHETIC_NOT_REAL_" + randomUUID();
  try {
    const missing = path.join(os.tmpdir(), "missing-browser-" + randomUUID());
    const result = await collect(runIsolated(missing, "http://127.0.0.1:5299", output, session), 8000);
    assert.notEqual(result.code, 0);
    assert.match(result.stderr, /EXPECTED_FAIL_CLOSED:/);
    assert.doesNotMatch(result.stdout, /SYNTHETIC_COOKIE_SET_OWNED_BROWSER/);
    assert.equal(connections, 0);
    assert.equal(pngs(output).length, 0);
    assert.equal(staging(output).length, 0);
    assert.ok(!result.stdout.includes(session) && !result.stderr.includes(session));
    console.log("FAILED_SPAWN_ISOLATION_PASS: listener connections=0 screenshots=0");
  } finally {
    fs.rmSync(output, { recursive: true, force: true });
    await close(unrelated);
  }
});
