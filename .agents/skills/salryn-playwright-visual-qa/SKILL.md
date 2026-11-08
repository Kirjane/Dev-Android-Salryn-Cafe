---
name: salryn-playwright-visual-qa
description: Build and run reproducible Playwright screenshot, accessibility, keyboard and workflow checks for Salryn POS every time frontend modules change, with safe synthetic data and real Host integration separated.
---

# Salryn Playwright Rendered QA Skill

This project currently has no `@playwright/test` test script in `src/Salryn.App.Web/package.json`; add it as a dev dependency on the isolated branch and lock it. Follow https://playwright.dev/docs/test-snapshots.

## Two-tier testing (never conflate)
**MOCKED_UI**: intercept the existing local API with deterministic **synthetic** fixtures per license, bootstrap, session, role and state. This proves DOM/interactions/layout only; does NOT prove backend auth, license verification or financial semantics. No production data. No fake signed real license secrets.
**INTEGRATED_HOST**: real local loopback Host, disposable database, valid explicitly authorized QA entitlement, verify live UX flows and API permissions when available. If not possible, report BLOCKED with exact cause, not PASS. Do not bypass signing/security checks.

## Determinism and baseline
- Reproducible `npx playwright install chromium` only when necessary; specify `playwright.config.ts` with `baseURL`, `trace: on-first-retry`, `screenshot: only-on-failure`, no arbitrary `waitForTimeout`, and local webServer with ready health expectation. Capture browser/OS/font details.
- Test Chromium at 1366×768, 1440×900, 1920×1080, 1280×720, 1024×768; secondary 390×844. `timezoneId: Asia/Manila`, `reducedMotion: reduce`, deterministic clocks/data and CSS stability where needed.
- Use `await expect(page).toHaveScreenshot('screen-state.png')`; review first reference snapshots intentionally. Follow-up changes **fail on unreviewed differences**; never blanket `--update-snapshots` to pass.
- Track screenshots by module, persona and state: licensing, onboarding, login/error, cashier checkout empty/product/full cash/split Utang, sales/void/receipt, AR denied/entitled/empty/long list, owner graphs, stock/products, admin/users/audit, backup/restore, terminal support/future-only, dialogs/toasts.
- Test no horizontal page overflow, no clipped modal/CTA, focus visibility and return, keyboard tab order, Enter/Escape where appropriate, scanner-wedge field focus, chart/table accessible alternative, contrast and status color independence, touch targets and helpful empty/error/retry content. Tables may scroll within their labeled viewport.
- Collect `console.error`, `pageerror`, failed requests, trace/video/screenshots on failure; explain allowed expected failures explicitly. No secrets, real customer details or private QR/key materials in captures.
- Re-run after **each** frontend slice and with `npm ci`, `npm run build`; have proof at each exact SHA. Distinguish a screenshot PASS from true complete POS/hardware acceptance.

## Output contract
Store test source under a dedicated `src/Salryn.App.Web/e2e/` (or equivalently clear dedicated test folder), `playwright.config.ts`, npm `test:e2e` and `test:visual` scripts, committed reviewed baseline snapshots (no secret-bearing screenshots), and a report listing exact commands, exits, coverage, fixture tier, failures, Chrome revision/OS and evidence paths.

## Example
```ts
import { test, expect } from '@playwright/test';
test('verified UI state', async ({ page }) => {
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Reports' })).toBeVisible();
  await expect(page).toHaveScreenshot('reports-verified.png');
});
```
Do NOT run this example without a lawful working fixture: Salryn's app gates actual license/bootstrap/auth first.
