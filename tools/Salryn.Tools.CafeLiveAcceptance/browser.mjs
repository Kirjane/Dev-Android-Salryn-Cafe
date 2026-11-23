import { createRequire } from "node:module";
import { readFileSync, writeFileSync } from "node:fs";
const require = createRequire(process.env.CAFE_QA_PLAYWRIGHT_PACKAGE);
const { chromium, expect } = require("@playwright/test");
const url = process.env.CAFE_QA_URL;
if (!/^http:\/\/127\.0\.0\.1:\d+$/.test(url ?? "") || !process.env.CAFE_QA_NONCE) throw new Error("Disposable loopback Host required");
const browser = await chromium.launch();
const context = await browser.newContext({ viewport: {width:1366,height:900} });
const page = await context.newPage();
let assertions=0;
const errors=[];
const completions=[];
page.on("response", response => {
  const request = response.request();
  if(response.status() === 200 && request.method() === "POST" && /\/api\/cafe\/orders\/[^/]+\/complete$/.test(request.url())) {
    const body=request.postDataJSON();
    if(body.payments?.length) completions.push({path:new URL(request.url()).pathname,body});
  }
});
page.on("pageerror", error => errors.push(error.message));
try {
  const identity = await context.request.get(`${url}/api/test/host-identity`);
  if (!identity.ok() || (await identity.json()).testHostNonce !== process.env.CAFE_QA_NONCE) throw new Error("Disposable Host identity mismatch");
  assertions++;
  await page.goto(`${url}/cafe/`);
  await expect(page.getByRole("heading", {name:"Cafe workstation",exact:true})).toBeVisible(); assertions++;
  await page.getByLabel(/Username/i).fill("c4-proof");
  await page.getByLabel(/Password/i).fill(process.env.CAFE_QA_PASSWORD);
  await page.getByRole("button",{name:"Sign in",exact:true}).click();
  await expect(page.getByRole("button",{name:"Sign out",exact:true})).toBeVisible(); assertions++;
  if(process.env.CAFE_QA_REPLAY === "1") {
    const retained=JSON.parse(readFileSync(`${process.env.CAFE_QA_ROOT}/completion-requests.json`,"utf8"));
    for(const intent of retained) {
      const response=await context.request.post(`${url}${intent.path}`,{data:intent.body});
      if(response.status() !== 200) throw new Error(`Restart replay failed HTTP ${response.status()}`);
      const body=await response.json();
      if(!body.receiptNumber) throw new Error("Restart replay lost receipt identity");
      assertions++;
    }
    const persistedSettings = await context.request.get(`${url}/api/settings/store`);
    if (persistedSettings.status() !== 200) throw new Error(`Post-restart GET /api/settings/store failed HTTP ${persistedSettings.status()}`);
    const settingsJson = await persistedSettings.json();
    if (settingsJson.storeName !== "Salryn Cafe Live QA" || settingsJson.receiptFooter !== "Verified Live QA!") {
      throw new Error(`Post-restart store settings mismatch: ${JSON.stringify(settingsJson)}`);
    }
    assertions++;
    console.log(`PASS HTTP restart replay + settings persistence: ${assertions} assertions`);
    await browser.close();
    process.exit(0);
  }
  for (const tender of ["Cash","AccountsReceivable","Partial"]) {
    await page.getByRole("button",{name:/C4 Coffee/i}).click();
    const cfgDialog = page.getByRole("dialog",{name:"Configure C4 Coffee"});
    await expect(cfgDialog).toBeVisible();
    await cfgDialog.getByRole("button",{name:"Add to Ticket",exact:true}).click();
    await page.getByLabel("Table / reference",{exact:true}).fill(`QA ${tender}`);
    await page.getByRole("button",{name:"Save order",exact:true}).click();
    await expect(page.getByRole("heading",{name:"Current order · Open",exact:true})).toBeVisible(); assertions++;
    await page.getByRole("button",{name:"Send to preparation",exact:true}).click();
    await expect(page.getByRole("button",{name:"Mark preparing",exact:true})).toBeVisible();
    if (tender === "Cash") {
      // B2 Live Check: Kitchen display printing via real /api/cafe/print/jobs/kitchen + /attempt with durable operationId
      await page.getByRole("button",{name:/Kitchen & Barista/i}).click();
      await expect(page.getByRole("button",{name:"Print Kitchen Ticket",exact:true}).first()).toBeVisible();
      await page.getByRole("button",{name:"Print Kitchen Ticket",exact:true}).first().click();
      await expect(page.getByText(/Printer disabled · Barista Kitchen/i)).toBeVisible(); assertions++;
      await page.screenshot({path:`${process.env.CAFE_QA_ROOT}/live-b2-kitchen-print.png`,fullPage:true});
      await page.getByRole("button",{name:/Cafe Cashier/i}).click();
      await expect(page.getByRole("button",{name:"Mark preparing",exact:true})).toBeVisible();
    }
    await page.getByRole("button",{name:"Mark preparing",exact:true}).click();
    await expect(page.getByRole("button",{name:"Mark ready",exact:true})).toBeVisible();
    await page.getByRole("button",{name:"Mark ready",exact:true}).click();
    await expect(page.getByRole("heading",{name:"Current order · Ready",exact:true})).toBeVisible(); assertions++;
    await page.getByLabel("Tender",{exact:true}).selectOption(tender);
    if (tender === "Cash") {
      // Insufficient cash is blocked in the cashier before any payment mutation.
      // Do not click a disabled button or manufacture a rejected intent to archive.
      await page.getByLabel("Cash amount (PHP)",{exact:true}).fill("0.00");
      await expect(page.getByText("Still to collect (estimated)",{exact:true})).toBeVisible(); assertions++;
      await expect(page.getByRole("button",{name:"Complete payment",exact:true})).toBeDisabled(); assertions++;
      await expect(page.getByRole("button",{name:"Archive rejected request",exact:true})).toHaveCount(0); assertions++;
    }
    if (tender !== "AccountsReceivable") await page.getByLabel("Cash amount (PHP)",{exact:true}).fill(tender === "Cash" ? "5.00" : "2.50");
    if (tender !== "Cash") await page.getByLabel(/Customer/i).selectOption({label:"Synthetic Cafe Account"});
    await page.getByRole("button",{name:"Complete payment",exact:true}).click();
    await expect(page.getByText(/Payment confirmed · Receipt/i)).toBeVisible(); assertions++;
    if (tender === "Cash") {
      // B1 Live Check: Cashier receipt printing from persisted receipt snapshot
      await expect(page.getByRole("button",{name:"Print Receipt",exact:true})).toBeVisible();
      await page.getByRole("button",{name:"Print Receipt",exact:true}).click();
      await expect(page.getByText(/Printer disabled · Counter Receipt/i)).toBeVisible(); assertions++;
      await page.screenshot({path:`${process.env.CAFE_QA_ROOT}/live-b1-receipt-print.png`,fullPage:true});
    }
    await page.getByRole("button",{name:"Start another order",exact:true}).click();
  }
  await page.getByRole("button",{name:"Back Office & Reports",exact:true}).click();
  // Live Host intentionally lands on Getting Started. Reports require explicit navigation.
  await expect(page.getByRole("heading",{name:/Getting Started/})).toBeVisible(); assertions++;
  await page.getByRole("button",{name:"Owner Reports & COGS",exact:true}).click();
  await expect(page.getByText("Daily Sales",{exact:true})).toBeVisible(); assertions++;
  await expect(page.getByText("₱15.00").first()).toBeVisible(); assertions++;
  // B3 Live Check: Backoffice Menu & Recipes publish with durable operationId
  await page.getByRole("button",{name:"Menu, Recipes & BOM",exact:true}).click();
  await expect(page.getByRole("button",{name:"Publish Menu & Recipes",exact:true})).toBeVisible();
  await page.getByRole("button",{name:"Publish Menu & Recipes",exact:true}).click();
  await expect(page.getByText(/Menu & Recipes published/i)).toBeVisible(); assertions++;
  await page.screenshot({path:`${process.env.CAFE_QA_ROOT}/live-b3-menu-publish.png`,fullPage:true});
  // B4 Live Check: Backoffice Store Settings read + update
  await page.getByRole("button",{name:"Audit, Users & Settings",exact:true}).click();
  await expect(page.getByText("Cafe Store & Receipt Settings",{exact:true})).toBeVisible(); assertions++;
  await page.getByLabel(/Store Name/i).fill("Salryn Cafe Live QA");
  await page.getByLabel(/Receipt Footer/i).fill("Verified Live QA!");
  await page.getByRole("button",{name:"Save Store Settings",exact:true}).click();
  await expect(page.getByText("Store settings updated", { exact: true })).toBeVisible(); assertions++;
  await expect(page.getByRole("cell", { name: "Store settings updated." }).first()).toBeVisible(); assertions++;
  await page.screenshot({path:`${process.env.CAFE_QA_ROOT}/live-b4-store-settings.png`,fullPage:true});
  await page.getByRole("button",{name:"Cash, Expense, AR & Refunds",exact:true}).click();
  await expect(page.getByRole("table",{name:"Customer AR Balances"})).toContainText("₱7.50"); assertions++;
  await expect(page.getByRole("heading",{name:"C5 Audited Refunds & Reversals",exact:true})).toBeVisible(); assertions++;
  await page.getByRole("button",{name:"Host Backup & License",exact:true}).click();
  await expect(page.getByRole("button",{name:/Create Verified Backup/i})).toBeVisible(); assertions++;
  await page.getByRole("button",{name:"Central Owner Portal",exact:true}).click();
  // Central lands on Device Trust; select the restricted Host-only module to
  // prove the local backup/licensing controls cannot execute on Central.
  await page.getByRole("button",{name:"Host Backup & License",exact:true}).click();
  await expect(page.getByText(/Local Windows Host Only/i)).toBeVisible(); assertions++;
  await page.getByRole("button",{name:"Windows Host Console",exact:true}).click();
  await page.getByRole("button",{name:"POS Workstation",exact:true}).click();
  await expect(page.getByRole("heading",{name:"Cafe workstation",exact:true})).toBeVisible(); assertions++;
  writeFileSync(`${process.env.CAFE_QA_ROOT}/completion-requests.json`,JSON.stringify(completions,null,2));
  await page.screenshot({path:`${process.env.CAFE_QA_ROOT}/live-cafe-desktop.png`,fullPage:true});
  await page.setViewportSize({width:320,height:900});
  await expect(page.getByRole("heading",{name:"Cafe workstation",exact:true})).toBeVisible();
  if (await page.evaluate(() => document.documentElement.scrollWidth > innerWidth)) throw new Error("320px horizontal overflow"); assertions++;
  await page.screenshot({path:`${process.env.CAFE_QA_ROOT}/live-cafe-mobile.png`,fullPage:true});
  if(errors.length) throw new Error(errors.join(";"));
  writeFileSync(`${process.env.CAFE_QA_ROOT}/browser-result.json`,JSON.stringify({verdict:"PASS",assertions,networkMocks:0,licenseScope:"DEBUG_DEVELOPMENT_LICENSE"},null,2));
  console.log(`PASS live browser: ${assertions} assertions; zero network mocks`);
} catch(error) {
  await page.screenshot({path:`${process.env.CAFE_QA_ROOT}/failure.png`,fullPage:true});
  writeFileSync(`${process.env.CAFE_QA_ROOT}/failure.txt`,String(error)+"\n"+await page.locator("body").innerText());
  throw error;
} finally { await browser.close(); }
