# Salryn POS — A0 Playwright Truthfulness & Regression Gate Review (R1)

**Mission**: `SALRYN-AG-A0-PLAYWRIGHT-REGRESSION-HARDENING-R1`  
**Owning Issue / PR**: [Issue #19](https://github.com/Kirjane/Salryn/issues/19) / [Draft PR #21](https://github.com/Kirjane/Salryn/pull/21)  
**Triggering Review**: [PR #21 Independent Review](https://github.com/Kirjane/Salryn/pull/21#issuecomment-6712919724) & [Handoff `6712960688`](https://github.com/Kirjane/Salryn/pull/21#issuecomment-6712960688)  
**Branch / Isolated Worktree**: `antigravity/salryn-premium-pos-ux` (`C:\Salryn-Antigravity`)  

---

## 1. Summary of Test-Truth Corrections

### Defect P1-A (Resolved): Real Playwright Visual Snapshot Regressions (`toHaveScreenshot`)
- **Previous Defect**: `e2e/mocked-ui-visual-baseline.spec.ts` called `page.screenshot({ path })` and asserted `fs.existsSync(targetPath)`, which silently overwrote the 42 PNGs on every run and could never fail when UI layout or styling drifted.
- **Correction Implemented**:
  1. Replaced `page.screenshot` + `fs.existsSync` with first-party `await expect(page).toHaveScreenshot(snapshotName, { fullPage })` across all 42 captures in [`e2e/mocked-ui-visual-baseline.spec.ts`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/e2e/mocked-ui-visual-baseline.spec.ts).
  2. Configured [`playwright.config.ts`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/playwright.config.ts) with:
     - `updateSnapshots: "none"` (ordinary runs **never** write or overwrite expected images; missing baselines fail immediately).
     - `snapshotPathTemplate: "{testDir}/__screenshots__/{arg}{ext}"` (preserves the exact 42 committed paths in `src/Salryn.App.Web/e2e/__screenshots__/*.png`).
     - Tight pixel comparison tolerance (`maxDiffPixelRatio: 0.001`, `threshold: 0.2`, `animations: "disabled"`, `caret: "hide"`, `scale: "css"`).
  3. Eliminated non-deterministic clock and random-seed drift in `test.beforeEach`:
     - Frozen wall clock via `await page.clock.setFixedTime(new Date("2026-04-19T10:30:00+08:00"))`.
     - Pinned terminal machine code (`SALRYN-MC-1111-2222-3333-4444`) in `localStorage` before page load so `window.crypto.getRandomValues` does not randomize `40-terminal-activation-gate.png`.
     - Dismissed the transient 4000ms `"Shift opened"` toast before snapshot `13-checkout-fast-add-modal.png` so wall-clock execution speed cannot cause toast-timer race diffs.
  4. Separated compare vs update npm scripts in [`package.json`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/package.json):
     - `npm run test:visual` → `playwright test --project=mocked-ui --grep @visual` (strict compare/fail; never updates snapshots).
     - `npm run test:visual:update` → `playwright test --project=mocked-ui --grep @visual --update-snapshots=all` (deliberate human-reviewed baseline update only).

---

### Defect P1-B (Resolved): Strict Separation & Honest `FAIL`/`BLOCKED` Semantics for `INTEGRATED_HOST`
- **Previous Defect**: `e2e/integrated-host.spec.ts` caught `ECONNREFUSED`, wrote `integrated-host-probe.json`, and asserted `expect(fs.existsSync(reportPath)).toBe(true)`. Because `npm run test:e2e` ran all specs, an unreachable Host was counted as `1 passed` inside the 23-test total.
- **Correction Implemented**:
  1. Split [`playwright.config.ts`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/playwright.config.ts) into two isolated Playwright projects:
     - `mocked-ui` (`testIgnore: /integrated-host\.spec\.ts/`): Runs **22 `MOCKED_UI` tests** (`16` visual specs covering 42 `toHaveScreenshot` assertions + `6` interaction & known-defect specs).
     - `integrated-host` (`testMatch: /integrated-host\.spec\.ts/`): Runs **3 serial `INTEGRATED_HOST` acceptance tests** against a live Host (`SALRYN_INTEGRATED_HOST_URL`, default `http://127.0.0.1:5299`) without starting the Vite dev server or installing any network mocks.
  2. Updated [`package.json`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/package.json) scripts:
     - `npm run test:e2e` → `playwright test --project=mocked-ui` (strictly `MOCKED_UI`, 22 tests; never includes `integrated-host`).
     - `npm run test:integrated-host` → `playwright test --project=integrated-host` (explicit live Host gate).
  3. Rewrote [`e2e/integrated-host.spec.ts`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/e2e/integrated-host.spec.ts) so that requesting `npm run test:integrated-host` **exits non-zero (`EXIT_CODE=1`)** unless all 3 serial checks pass:
     - **Check 1 (`health`)**: Live `/health` HTTP `200` (otherwise `verdict: "BLOCKED"`, `code: "BLOCKED_NO_HOST"` or `"HOST_UNHEALTHY_HTTP_*"`, failing `expect(verdict).toBe("PASS")`).
     - **Check 2 (`license`)**: Live `/api/license/status` is `Licensed`, `Host` type, and `machineMatched: true` (no fake licenses; logs only redacted status/type/feature names).
     - **Check 3 (`authenticatedReadOnlyJourney`)**: Requires disposable QA operator credentials (`SALRYN_QA_HOST_USERNAME` / `SALRYN_QA_HOST_PASSWORD`), signs in through the real UI, and navigates read-only pages (`Sales & Receipts`, `Reports`) before signing out.
  4. Redacted `test-results/integrated-host-probe.json`: strips multi-line Playwright call logs/headers and never records raw license JSON, signatures, tokens, cookies, or customer data.

---

## 2. Deliberate Visual Drift Failure Proof & Post-Revert Pass

To prove that `npm run test:visual` (and `playwright test --project=mocked-ui`) genuinely fails on visual drift without overwriting baselines:

### Step A — Introduce Temporary Synthetic Fixture Drift (Uncommitted)
In `e2e/fixtures/syntheticData.ts`, `SYNTHETIC_STORE_BRANDING.storeName` was temporarily changed from `"Balite Neighborhood Mart"` to `"DELIBERATE DRIFT REGRESSION MART"`.

### Step B — Run Focused Visual Comparison (Expected `FAIL`, Exit Code `1`)
- **Command**:
  ```powershell
  npx playwright test --project=mocked-ui -g "07 & 08 - Dashboard page"
  ```
- **Observed Result**: **FAILED (`Exit Code 1`)** — expected baseline `e2e/__screenshots__/07-dashboard-healthy.png` was **not** overwritten:
  ```text
  Running 1 test using 1 worker

    x  1 [mocked-ui] › e2e\mocked-ui-visual-baseline.spec.ts:77:3 › MOCKED_UI Visual Baseline & Screen Inventory @visual › 07 & 08 - Dashboard page (healthy vs operational warnings) (4.7s)

    Error: expect(page).toHaveScreenshot(expected) failed

      1064 pixels (ratio 0.01 of all image pixels) are different.

      Snapshot: 07-dashboard-healthy.png

      Expected: e2e\__screenshots__\07-dashboard-healthy.png
      Received: test-results\mocked-ui-visual-baseline--c21e3-hy-vs-operational-warnings--mocked-ui\07-dashboard-healthy-actual.png
      Diff:     test-results\mocked-ui-visual-baseline--c21e3-hy-vs-operational-warnings--mocked-ui\07-dashboard-healthy-diff.png

    1 failed
  ```
- **Visual Inspection of Diff (`07-dashboard-healthy-diff.png`)**: Confirmed bright red/yellow diff pixels highlighting the header store title `"DELIBERATE DRIFT REGRESSION MART"` against `"Balite Neighborhood Mart"`.

### Step C — Restore Fixture & Re-Verify Clean `PASS` (Exit Code `0`)
- Restored `SYNTHETIC_STORE_BRANDING.storeName` to `"Balite Neighborhood Mart"`.
- **Command**:
  ```powershell
  npx playwright test --project=mocked-ui -g "07 & 08 - Dashboard page"
  ```
- **Observed Result**: **PASSED (`1 passed (10.4s)`, `Exit Code 0`)**.

---

## 3. Honest `INTEGRATED_HOST` Verification (`BLOCKED_NO_HOST`, Exit Code `1`)

- **Command**:
  ```powershell
  npm run test:integrated-host
  ```
- **Observed Result**: **FAILED / BLOCKED (`1 failed, 2 did not run`, `Exit Code 1`)**:
  ```text
  Running 3 tests using 1 worker

    x  1 [integrated-host] › e2e\integrated-host.spec.ts:79:3 › INTEGRATED_HOST live Host acceptance @integrated › 1. Host is reachable and healthy (no mocks) (39ms)
    -  2 [integrated-host] › e2e\integrated-host.spec.ts:100:3 › INTEGRATED_HOST live Host acceptance @integrated › 2. Host license is Licensed, Host-type and machine-matched (no fake licenses)
    -  3 [integrated-host] › e2e\integrated-host.spec.ts:127:3 › INTEGRATED_HOST live Host acceptance @integrated › 3. Disposable QA operator signs in through the real UI and opens read-only pages

    Error: INTEGRATED_HOST health: BLOCKED_NO_HOST (apiRequestContext.get: connect ECONNREFUSED 127.0.0.1:5299)
    Expected: "PASS"
    Received: "BLOCKED"
  ```
- **Redacted Probe Output (`test-results/integrated-host-probe.json`, untracked)**:
  ```json
  {
    "tier": "INTEGRATED_HOST",
    "probedAtUtc": "2026-11-07T21:16:51.370Z",
    "targetBaseUrl": "http://127.0.0.1:5299",
    "overallVerdict": "BLOCKED",
    "checks": {
      "health": {
        "verdict": "BLOCKED",
        "code": "BLOCKED_NO_HOST",
        "detail": "apiRequestContext.get: connect ECONNREFUSED 127.0.0.1:5299"
      },
      "license": {
        "verdict": "NOT_RUN",
        "code": "NOT_RUN",
        "detail": ""
      },
      "authenticatedReadOnlyJourney": {
        "verdict": "NOT_RUN",
        "code": "NOT_RUN",
        "detail": ""
      }
    },
    "note": "Redacted. MOCKED_UI and INTEGRATED_HOST are strictly separated. A reachable /health alone is NOT integrated UI acceptance."
  }
  ```

---

## 4. Known UX Defect Reproduction Contract (`e2e/mocked-ui-interactions-and-defects.spec.ts`)

Three tests in [`e2e/mocked-ui-interactions-and-defects.spec.ts`](file:///C:/Salryn-Antigravity/src/Salryn.App.Web/e2e/mocked-ui-interactions-and-defects.spec.ts) explicitly pin **known current UX defects** (`KNOWN-DEFECT UX-004`, `KNOWN-DEFECT UX-005`, `KNOWN-DEFECT UX-008`) and attach Playwright `known-defect` annotations:
- `KNOWN-DEFECT UX-004`: `ProductsPage` custom `Dialog` ignores `Escape`.
- `KNOWN-DEFECT UX-005`: `ProductsPage` `"View Movements"` stays on `Product Catalog` instead of navigating to `Stock Movements`.
- `KNOWN-DEFECT UX-008`: Sidebar navigation does not push browser `history` entries.

When Phase A1–A6 ([Issue #20](https://github.com/Kirjane/Salryn/issues/20)) fixes these defects, these three tests will fail until their assertions are inverted in the same PR.
