import { createRequire } from "node:module";
import { readFileSync, writeFileSync } from "node:fs";

const require = createRequire(process.env.CAFE_QA_PLAYWRIGHT_PACKAGE);
const { chromium, expect } = require("@playwright/test");
const axeSource = readFileSync(process.env.CAFE_QA_AXE_PATH, "utf8");

const url = process.env.CAFE_QA_URL;
const nonce = process.env.CAFE_QA_NONCE;
const managerPassword = process.env.CAFE_QA_PASSWORD;
const cashierPassword = process.env.CAFE_QA_CASHIER_PASSWORD;
const root = process.env.CAFE_QA_ROOT;
const phase = process.env.CAFE_QA_PHASE;
const fencedOpId = process.env.CAFE_QA_FENCED_OP_ID;

if (!/^http:\/\/127\.0\.0\.1:\d+$/.test(url ?? "") || !nonce) {
  throw new Error("Disposable loopback Host required");
}

const browser = await chromium.launch();
const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
const page = await context.newPage();

const errors = [];
const httpLog = [];
page.on("pageerror", (err) => errors.push(`pageerror: ${err.message}`));
page.on("response", (res) => {
  const req = res.request();
  const pathname = new URL(res.url()).pathname;
  if (pathname.startsWith("/api/")) {
    httpLog.push({
      phase,
      method: req.method(),
      path: pathname,
      status: res.status(),
      hasIdempotencyKey: Boolean(req.headers()["idempotency-key"]),
      replayed: res.headers()["x-idempotency-replayed"] ?? null,
    });
  }
});

let assertions = 0;
function assert(cond, msg) {
  if (!cond) throw new Error(msg);
  assertions++;
}

async function waitForSuccessMessage(text) {
  await expect(page.getByText(text, { exact: true })).toBeVisible();
  assertions++;
}

try {
  const identityRes = await context.request.get(`${url}/api/test/host-identity`);
  assert(identityRes.ok(), "Host identity endpoint failed");
  const identity = await identityRes.json();
  assert(identity.testHostNonce === nonce, "Host identity nonce mismatch");
  assert(identity.databasePath.startsWith(root), "Host database path must stay inside disposable root");

  if (phase === "1") {
    // 1. Unauthenticated access must show failed load (401), never empty catalog
    await page.goto(`${url}/cafe/recovery/c1`);
    await expect(page.getByRole("heading", { name: "Salryn Cafe — Canonical C1 Recovery Proof", exact: true })).toBeVisible();
    await expect(page.getByText("C1 menu catalog unavailable", { exact: true })).toBeVisible();
    await expect(page.getByText("Empty menu catalog", { exact: true })).toHaveCount(0);
    assertions += 2;

    // 2. Sign in as manager c4-proof -> authenticated empty catalog appears cleanly
    await page.getByLabel(/Username/i).fill("c4-proof");
    await page.getByLabel(/Password/i).fill(managerPassword);
    await page.getByRole("button", { name: "Sign in", exact: true }).click();
    await expect(page.getByText("Empty menu catalog", { exact: true })).toBeVisible();
    await expect(page.getByText("C1 menu catalog unavailable", { exact: true })).toHaveCount(0);
    assertions += 2;

    // 3. Create two categories and test category deactivate / reactivate
    await page.getByLabel("Category name", { exact: true }).fill("Signature Espresso");
    await page.getByRole("button", { name: "Add category", exact: true }).click();
    await waitForSuccessMessage("Menu category saved.");

    await page.getByLabel("Category name", { exact: true }).fill("Iced Classics");
    await page.getByRole("button", { name: "Add category", exact: true }).click();
    await waitForSuccessMessage("Menu category saved.");

    await page.getByRole("button", { name: "Signature Espresso", exact: true }).click();
    await waitForSuccessMessage("Category deactivated.");
    await page.getByRole("button", { name: "Signature Espresso · inactive", exact: true }).click();
    await waitForSuccessMessage("Category reactivated.");

    // 4. Create menu item "Spanish Latte" -> auto-selects and creates Standard variant
    await page.getByLabel(/Menu item name/i).fill("Spanish Latte");
    await page.getByLabel("Category", { exact: true }).selectOption({ label: "Signature Espresso" });
    await page.getByLabel(/Unit/i).selectOption({ label: "Piece" });
    await page.getByLabel(/Base price \(PHP\)/i).fill("145.00");
    await page.getByLabel("SKU", { exact: true }).fill("CAFE-SL-001");
    await page.getByRole("button", { name: "Create menu item", exact: true }).click();
    await waitForSuccessMessage("Menu item and Standard variant created.");

    const afterCreateDraft = await (await context.request.get(`${url}/api/menu/catalog`)).json();
    assert(afterCreateDraft.items.length === 1, "Expected 1 menu item after creation");
    const createdProductId = afterCreateDraft.items[0].productId;

    // 5. Edit item name, price, SKU, and category without changing stable productId
    await page.getByLabel(/Menu item name/i).fill("Iced Spanish Latte");
    await page.getByLabel("Category", { exact: true }).selectOption({ label: "Iced Classics" });
    await page.getByLabel(/Base price \(PHP\)/i).fill("160.00");
    await page.getByLabel("SKU", { exact: true }).fill("CAFE-ISL-001");
    await page.getByRole("button", { name: "Save item changes", exact: true }).click();
    await waitForSuccessMessage("Menu item changes saved.");

    const afterEditDraft = await (await context.request.get(`${url}/api/menu/catalog`)).json();
    assert(afterEditDraft.items.length === 1, "Editing must not create a duplicate item");
    assert(afterEditDraft.items[0].productId === createdProductId, "Product ID must remain stable on edit");

    // 6. Add two variants/sizes: 12oz Hot and 16oz Iced
    await page.getByLabel(/Variant name/i).fill("12oz Hot");
    await page.getByLabel(/Variant price \(PHP\)/i).fill("150.00");
    await page.getByLabel("Variant SKU", { exact: true }).fill("CAFE-ISL-12H");
    await page.getByRole("button", { name: "Add variant", exact: true }).click();
    await waitForSuccessMessage("Variant saved.");

    await page.getByLabel(/Variant name/i).fill("16oz Iced");
    await page.getByLabel(/Variant price \(PHP\)/i).fill("175.00");
    await page.getByLabel("Variant SKU", { exact: true }).fill("CAFE-ISL-16I");
    await page.getByRole("button", { name: "Add variant", exact: true }).click();
    await waitForSuccessMessage("Variant saved.");

    // 7. Create required modifier group "Milk Choice" + 3 options, and optional variant-only group "Iced Sweetness"
    await page.getByLabel(/Modifier group name/i).fill("Milk Choice");
    const reqBox = page.getByLabel("Required group", { exact: true });
    if (!(await reqBox.isChecked())) await reqBox.check();
    await page.getByLabel("Min selections", { exact: true }).fill("1");
    await page.getByLabel("Max selections", { exact: true }).fill("2");
    await page.getByRole("button", { name: "Add modifier group", exact: true }).click();
    await waitForSuccessMessage("Modifier group saved.");

    for (const [optName, optDelta] of [["Whole Milk", "0.00"], ["Oat Milk", "35.00"], ["Soy Milk", "25.00"]]) {
      await page.getByLabel("Option name", { exact: true }).fill(optName);
      await page.getByLabel("Price delta (PHP)", { exact: true }).fill(optDelta);
      await page.getByRole("button", { name: "Add option", exact: true }).click();
      await waitForSuccessMessage("Modifier option saved.");
    }

    await page.getByLabel(/Modifier group name/i).fill("Iced Sweetness");
    if (await reqBox.isChecked()) await reqBox.uncheck();
    await page.getByLabel("Min selections", { exact: true }).fill("0");
    await page.getByLabel("Max selections", { exact: true }).fill("1");
    await page.getByRole("button", { name: "Add modifier group", exact: true }).click();
    await waitForSuccessMessage("Modifier group saved.");

    await page.getByLabel("Option name", { exact: true }).fill("Less Sugar");
    await page.getByLabel("Price delta (PHP)", { exact: true }).fill("0.00");
    await page.getByRole("button", { name: "Add option", exact: true }).click();
    await waitForSuccessMessage("Modifier option saved.");

    // 8. Link Milk Choice at item level, and Iced Sweetness ONLY to 16oz Iced variant via new control
    await page.getByRole("checkbox", { name: "Item link: Milk Choice", exact: true }).check();
    await page.getByRole("button", { name: "Save item modifier links", exact: true }).click();
    await waitForSuccessMessage("Modifier links saved for menu item.");

    const variantsDraft = await (await context.request.get(`${url}/api/menu/catalog`)).json();
    const icedVariant = variantsDraft.variants.find((v) => v.name === "16oz Iced");
    assert(Boolean(icedVariant), "Expected 16oz Iced variant in draft");
    await page.getByLabel("Target variant for modifier links", { exact: true }).selectOption(icedVariant.id);
    await page.getByRole("checkbox", { name: "Variant link: Iced Sweetness", exact: true }).check();
    await page.getByRole("button", { name: "Save variant modifier links", exact: true }).click();
    await waitForSuccessMessage("Modifier links saved for variant.");

    // 9. Separate temporary availability (sold out) vs lifecycle (inactive)
    await page.getByRole("button", { name: "Mark sold out", exact: true }).click();
    await waitForSuccessMessage("Item marked temporarily unavailable.");
    const soldOutDraft = await (await context.request.get(`${url}/api/menu/catalog`)).json();
    assert(soldOutDraft.items[0].isActive === true && soldOutDraft.items[0].isTemporarilyUnavailable === true, "Sold out must preserve isActive=true");

    await page.getByRole("button", { name: "Mark available", exact: true }).click();
    await waitForSuccessMessage("Item marked available.");

    await page.getByRole("button", { name: "Deactivate", exact: true }).first().click();
    await waitForSuccessMessage("Menu item deactivated.");
    const deactivatedDraft = await (await context.request.get(`${url}/api/menu/catalog`)).json();
    assert(deactivatedDraft.items[0].isActive === false && deactivatedDraft.items[0].isTemporarilyUnavailable === false, "Deactivate must preserve isTemporarilyUnavailable=false");

    await page.getByRole("button", { name: "Reactivate", exact: true }).first().click();
    await waitForSuccessMessage("Menu item reactivated.");

    // 10. Publish revision r1 and verify hashes/snapshot
    await page.getByRole("button", { name: "Publish menu", exact: true }).click();
    await waitForSuccessMessage("Current menu content published as an immutable revision.");
    await expect(page.getByText("Published r1", { exact: true })).toBeVisible();
    assertions++;

    const pubSnap = await (await context.request.get(`${url}/api/menu/publication/current`)).json();
    assert(pubSnap && pubSnap.revisionNumber === 1, "Expected published revision 1");
    assert(pubSnap.items.length === 1 && pubSnap.items[0].name === "Iced Spanish Latte", "Published item mismatch");
    assert(pubSnap.variants.length === 3, "Expected 3 published variants (Standard, 12oz Hot, 16oz Iced)");
    assert(pubSnap.itemModifierGroups.length === 1 && pubSnap.variantModifierGroups.length === 1, "Expected item and variant modifier links in published snapshot");

    // 11. Desktop screenshot + axe-core + keyboard check on /cafe/recovery/c1 BEFORE draft edit
    await page.setViewportSize({ width: 1440, height: 900 });
    assert(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), "Desktop 1440px horizontal overflow");
    await page.addScriptTag({ content: axeSource });
    const axeDesktop = await page.evaluate(async () => await window.axe.run(document));
    const seriousDesktop = axeDesktop.violations.filter((v) => v.impact === "critical" || v.impact === "serious");
    assert(seriousDesktop.length === 0, `Desktop axe violations: ${JSON.stringify(seriousDesktop.map((v) => v.id))}`);
    await page.keyboard.press("Tab");
    const focusedTag = await page.evaluate(() => document.activeElement?.tagName ?? "");
    assert(Boolean(focusedTag && focusedTag !== "BODY"), "Keyboard Tab did not move focus");
    await page.screenshot({ path: `${root}/c1-recovery-desktop-1440.png`, fullPage: true });

    // Mobile 390px and 320px overflow checks + 390px screenshot
    for (const width of [390, 320]) {
      await page.setViewportSize({ width, height: 844 });
      assert(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), `Mobile ${width}px horizontal overflow`);
    }
    await page.setViewportSize({ width: 390, height: 844 });
    await page.screenshot({ path: `${root}/c1-recovery-mobile-390.png`, fullPage: true });
    await page.setViewportSize({ width: 1440, height: 900 });

    // 12. Make an unpublished draft edit and confirm Cashier reads published r1, NOT draft
    await page.getByLabel(/Menu item name/i).fill("Iced Spanish Latte DRAFT UNPUBLISHED");
    await page.getByLabel(/Base price \(PHP\)/i).fill("999.00");
    await page.getByRole("button", { name: "Save item changes", exact: true }).click();
    await waitForSuccessMessage("Menu item changes saved.");

    await page.getByRole("button", { name: "Open Cashier (/cafe/)", exact: true }).click();
    await expect(page.getByRole("heading", { name: "Cafe workstation", exact: true })).toBeVisible();
    await expect(page.getByRole("button", { name: /Iced Spanish Latte/i })).toBeVisible();
    await expect(page.getByText("₱160.00", { exact: true })).toBeVisible();
    await expect(page.getByText(/DRAFT UNPUBLISHED/i)).toHaveCount(0);
    await expect(page.getByText("₱999.00", { exact: true })).toHaveCount(0);
    assertions += 5;

    await page.getByRole("button", { name: /Iced Spanish Latte/i }).click();
    const cfgDialog = page.getByRole("dialog", { name: "Configure Iced Spanish Latte" });
    await expect(cfgDialog).toBeVisible();
    await expect(cfgDialog.getByText(/Milk Choice/)).toBeVisible();
    // Without 16oz Iced selected, Iced Sweetness must not appear
    await expect(cfgDialog.getByText(/Iced Sweetness/)).toHaveCount(0);
    // Select 16oz Iced variant -> variant-linked Iced Sweetness appears
    await cfgDialog.getByLabel("Variant", { exact: true }).selectOption({ label: "16oz Iced (₱175.00)" });
    await expect(cfgDialog.getByText(/Iced Sweetness/)).toBeVisible();
    assertions += 4;
    await page.screenshot({ path: `${root}/c1-cashier-published-vs-draft.png`, fullPage: true });
    await cfgDialog.getByRole("button", { name: "Cancel", exact: true }).click();

    writeFileSync(
      `${root}/c1-phase1-evidence.json`,
      JSON.stringify({ phase: 1, assertions, createdProductId, publication: pubSnap, httpLog }, null, 2),
    );
    console.log(`PASS Phase 1 C1 recovery browser (${assertions} assertions)`);
  } else {
    // PHASE 2: Restarted Host over identical SQLite DB
    // 1. Negative RBAC: signed-out write -> 401
    const unauthWrite = await context.request.post(`${url}/api/menu/categories`, {
      headers: { "Idempotency-Key": crypto.randomUUID() },
      data: { id: null, name: "Signed Out Category", isActive: true },
    });
    assert(unauthWrite.status() === 401, `Expected 401 for signed-out write, got ${unauthWrite.status()}`);

    // 2. Sign in as manager c4-proof -> verify persistence after restart
    await page.goto(`${url}/cafe/recovery/c1`);
    await page.getByLabel(/Username/i).fill("c4-proof");
    await page.getByLabel(/Password/i).fill(managerPassword);
    await page.getByRole("button", { name: "Sign in", exact: true }).click();
    await expect(page.getByText("Published r1", { exact: true })).toBeVisible();
    await expect(page.getByRole("button", { name: /Iced Spanish Latte DRAFT UNPUBLISHED/i })).toBeVisible();
    assertions += 2;

    const restartPub = await (await context.request.get(`${url}/api/menu/publication/current`)).json();
    const phase1Data = JSON.parse(readFileSync(`${root}/c1-phase1-evidence.json`, "utf8"));
    assert(restartPub.revisionId === phase1Data.publication.revisionId, "Publication revisionId drifted across restart");
    assert(restartPub.contentHash === phase1Data.publication.contentHash, "Publication contentHash drifted across restart");
    assert(restartPub.publicationHash === phase1Data.publication.publicationHash, "Publication hash drifted across restart");

    // 3. Idempotency matrix via real authenticated Host
    const missingKeyRes = await context.request.post(`${url}/api/menu/categories`, {
      data: { id: null, name: "Missing Key Category", isActive: true },
    });
    assert(missingKeyRes.status() === 400, `Expected 400 missing key, got ${missingKeyRes.status()}`);
    const missingBody = await missingKeyRes.json();
    assert(missingBody.code === "CafeMutation.OperationIdRequired", `Expected CafeMutation.OperationIdRequired, got ${missingBody.code}`);

    const validKey = crypto.randomUUID();
    const validPayload = { id: null, name: "Idempotency Valid Category", isActive: true };
    const validRes = await context.request.post(`${url}/api/menu/categories`, {
      headers: { "Idempotency-Key": validKey },
      data: validPayload,
    });
    assert(validRes.status() === 200, `Expected 200 valid key, got ${validRes.status()}`);

    const replayRes = await context.request.post(`${url}/api/menu/categories`, {
      headers: { "Idempotency-Key": validKey },
      data: validPayload,
    });
    assert(replayRes.status() === 200, `Expected 200 identical replay, got ${replayRes.status()}`);
    assert(replayRes.headers()["x-idempotency-replayed"] === "true", "Expected X-Idempotency-Replayed: true on replay");

    const conflictRes = await context.request.post(`${url}/api/menu/categories`, {
      headers: { "Idempotency-Key": validKey },
      data: { id: null, name: "Conflicting Payload Category", isActive: true },
    });
    assert(conflictRes.status() === 409, `Expected 409 changed-payload conflict, got ${conflictRes.status()}`);
    const conflictBody = await conflictRes.json();
    assert(conflictBody.code === "ERR_IDEMPOTENCY_CONFLICT", `Expected ERR_IDEMPOTENCY_CONFLICT, got ${conflictBody.code}`);

    // 4. Real server-side 409 ERR_IDEMPOTENCY_OUTCOME_UNKNOWN fencing in the UI
    await page.evaluate((opId) => {
      window.localStorage.setItem(
        "salryn.cafe.backoffice.op.c1-menu-recovery.v1",
        JSON.stringify({ operationId: opId, intentKey: "category:add:Fenced Category" }),
      );
    }, fencedOpId);
    await page.reload();
    await expect(page.getByText("Published r1", { exact: true })).toBeVisible();
    await page.getByLabel("Category name", { exact: true }).fill("Fenced Category");
    await page.getByRole("button", { name: "Add category", exact: true }).click();
    await expect(page.getByText("C1 mutation outcome uncertain", { exact: true })).toBeVisible();
    await expect(page.getByText(new RegExp(fencedOpId, "i"))).toBeVisible();
    assertions += 2;

    // Changing input while fenced must block client-side without calling Host or generating a new key
    const reqCountBefore = httpLog.length;
    await page.getByLabel("Category name", { exact: true }).fill("Changed After Fence");
    await page.getByRole("button", { name: "Add category", exact: true }).click();
    await expect(page.getByText(/Resolve the previous uncertain operation before submitting changed details/i)).toBeVisible();
    assert(httpLog.length === reqCountBefore, "Changed payload while fenced must not send a new HTTP request");
    await page.screenshot({ path: `${root}/c1-recovery-unknown-outcome-fence.png`, fullPage: true });
    await page.getByRole("button", { name: "Refresh & clear fence", exact: true }).click();

    // 5. Underprivileged actor (c1-cashier: Products.View only, no Products.Manage) -> 403 + visible UI error
    await page.getByRole("button", { name: /Sign out/i }).click();
    await page.getByLabel(/Username/i).fill("c1-cashier");
    await page.getByLabel(/Password/i).fill(cashierPassword);
    await page.getByRole("button", { name: "Sign in", exact: true }).click();
    await expect(page.getByText("Published r1", { exact: true })).toBeVisible();
    await page.getByLabel("Category name", { exact: true }).fill("Unauthorized Category");
    await page.getByRole("button", { name: "Add category", exact: true }).click();
    await expect(page.getByText("Permission denied.", { exact: true })).toBeVisible();
    assert(
      httpLog.some((e) => e.phase === "2" && e.method === "POST" && e.path === "/api/menu/categories" && e.status === 403),
      "Expected real HTTP 403 response from Host on underprivileged category write",
    );
    assertions++;
    await page.screenshot({ path: `${root}/c1-recovery-rbac-403.png`, fullPage: true });

    writeFileSync(
      `${root}/c1-phase2-evidence.json`,
      JSON.stringify({
        phase: 2,
        assertions,
        idempotencyChecks: {
          missingKeyStatus: missingKeyRes.status(),
          missingKeyCode: missingBody.code,
          validStatus: validRes.status(),
          replayStatus: replayRes.status(),
          replayHeader: replayRes.headers()["x-idempotency-replayed"],
          conflictStatus: conflictRes.status(),
          conflictCode: conflictBody.code,
          fencedOpId,
        },
        unauthStatus: unauthWrite.status(),
        httpLog,
      }, null, 2),
    );
    console.log(`PASS Phase 2 C1 restart/idempotency/RBAC browser (${assertions} assertions)`);
  }

  if (errors.length > 0) {
    throw new Error(`Uncaught browser errors: ${errors.join("; ")}`);
  }
} catch (err) {
  await page.screenshot({ path: `${root}/c1-failure-phase${phase}.png`, fullPage: true }).catch(() => {});
  writeFileSync(`${root}/c1-failure-phase${phase}.txt`, String(err));
  throw err;
} finally {
  await browser.close();
}
