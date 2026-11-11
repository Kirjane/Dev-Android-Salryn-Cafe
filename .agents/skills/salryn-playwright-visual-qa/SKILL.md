---
name: salryn-playwright-visual-qa
description: Use existing Salryn Playwright suites for a changed frontend path or explicitly requested visual QA; never regenerate all screenshots or reinstall Playwright merely because a UI file changed.
---

# Salryn Playwright QA — Existing Infrastructure

The current R2.1 source already contains `src/Salryn.App.Web/playwright.config.ts`, `e2e/`, `integrated-host-lpi-acceptance.spec.ts` and runner code. **Do not reinstall tools or recreate suites by default.** Check actual files on current SHA rather than trusting old documentation.

Testing tiers:
- `MOCKED_UI`: synthetic fixtures and isolated visual contracts; does not prove real persistence, license or payments.
- `INTEGRATED_HOST`: authenticated real Local API against disposable SQLite, explicit host identity/nonce; operator DB forbidden.
- `WINDOWS_INSTALLED_READ_ONLY`: actual licensed WebView2 plus non-mutating navigation and startup.
- `WINDOWS_TEST_DATABASE_MUTATIONS`: deliberately isolated installed path for business transactions, never production data.

For a targeted change, run the closest existing test(s), assert actual clicked button→request→result and browser exceptions, and capture one relevant screenshot/trace. After code freeze run final affected regression once. Report exact SHA, command, exit, environment, evidence. No automatic full matrix (all roles, sizes, modules), no unrelated 39-test repetitions, no false `PASS_INSTALLED` from test Host screenshots.
