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
page.on("request", request => {
  if(request.method() === "POST" && /\/api\/cafe\/orders\/[^/]+\/complete$/.test(request.url())) {
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
  await page.getByLabel("Username",{exact:true}).fill("c4-proof");
  await page.getByLabel("Password",{exact:true}).fill(process.env.CAFE_QA_PASSWORD);
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
    console.log(`PASS HTTP restart replay: ${assertions} assertions`);
    await browser.close();
    process.exit(0);
  }
  for (const tender of ["Cash","AccountsReceivable","Partial"]) {
    await page.getByLabel("Menu item",{exact:true}).selectOption({label:"C4 Coffee · ₱5.00"});
    await page.getByRole("button",{name:"Add item",exact:true}).click();
    await page.getByLabel("Table / reference",{exact:true}).fill(`QA ${tender}`);
    await page.getByRole("button",{name:"Save order",exact:true}).click();
    await expect(page.getByRole("heading",{name:"Current order · Open",exact:true})).toBeVisible(); assertions++;
    await page.getByRole("button",{name:"Send to preparation",exact:true}).click();
    await expect(page.getByRole("button",{name:"Mark preparing",exact:true})).toBeVisible();
    await page.getByRole("button",{name:"Mark preparing",exact:true}).click();
    await expect(page.getByRole("button",{name:"Mark ready",exact:true})).toBeVisible();
    await page.getByRole("button",{name:"Mark ready",exact:true}).click();
    await expect(page.getByRole("heading",{name:"Current order · Ready",exact:true})).toBeVisible(); assertions++;
    await page.getByLabel("Tender",{exact:true}).selectOption(tender);
    if (tender === "Cash") {
      await page.getByLabel("Cash amount (PHP)",{exact:true}).fill("0.00");
      await page.getByRole("button",{name:"Complete payment",exact:true}).click();
      await expect(page.getByRole("button",{name:"Archive rejected request",exact:true})).toBeVisible(); assertions++;
      await page.getByLabel("Reason to archive rejected request",{exact:true}).fill("Synthetic rejected zero cash; correcting tender");
      await page.getByRole("button",{name:"Archive rejected request",exact:true}).click();
      await expect(page.getByRole("heading",{name:"Action needs confirmation",exact:true})).toHaveCount(0); assertions++;
    }
    if (tender !== "AccountsReceivable") await page.getByLabel("Cash amount (PHP)",{exact:true}).fill(tender === "Cash" ? "5.00" : "2.50");
    if (tender !== "Cash") await page.getByLabel("Customer",{exact:true}).selectOption({label:"Synthetic Cafe Account"});
    await page.getByRole("button",{name:"Complete payment",exact:true}).click();
    await expect(page.getByRole("heading",{name:"Current order · Completed",exact:true})).toBeVisible(); assertions++;
    await page.getByRole("button",{name:"Start another order",exact:true}).click();
  }
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
