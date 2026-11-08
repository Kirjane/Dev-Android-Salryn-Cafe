# Salryn I2 Final RC2 + W4 Integration Ledger

Date: 2026-11-09 Asia/Manila  
Branch: `integration/salryn-w4-premium-ux-r1`  
Tested implementation SHA: `73deac6d16e59b58eec79e87e0dfa62c89fbe99e`

## Source custody

| Source | Exact SHA | Disposition |
|---|---|---|
| W4 PR #22 | `62eadb201d344516f3c6e7bb491b18db0fc0b7ff` | financial/session/schema source; untouched |
| Antigravity RC2 PR #21 | `48998b94567ea31d31b150ca874a01cb753a3319` | exact commit merged; source branch untouched |
| I2 preparation | `3f097d8c7dbfb0a7287356092039202f9beb41f5` | integration parent |

Manual conflict resolution covered `App.tsx`, `api/checkout.ts`, `Dialog.tsx`, and `CheckoutPage.tsx`. Durable owner-report URLs, mandatory close payload, W4 blind physical count, stable ambiguous retry ID, immutable discrepancy semantics, and no force-close were retained. RC2 nested-dialog containment, 44 baselines, page improvements and shared presentation were retained. The obsolete RC2 parameterless Close Shift modal and optional request overload were removed.

## Production/test corrections

- Real notification overlap is corrected using measured toast geometry. Only a horizontally conflicting dialog reserves bounded vertical clearance; desktop baseline layout remains unchanged. Long/three-stack real W4 API errors pass at 390×844, 1024×768, 125%, and 150%.
- The unused `salryn-test-notify` custom-event test was removed. Tests trigger the real provider through 400/401/403/409 and source-coverage responses and assert actual rectangle nonintersection.
- Physical `0.00` is tested separately from zero variance. Ambiguous replay uses the same ID; deterministic rejection requires a new deliberate request ID.
- Owner analytics CoreSmoke had a hard-coded 2026-11-08 due-today fixture but used the system clock. Its injected `TimeProvider` is now fixed to the fixture as-of instant; production accounting code is unchanged.

## Same-SHA results

Evidence root: `C:\Salryn-Evidence\I2\RUN_73deac6`.

| Gate | Result |
|---|---|
| W4 acceptance runner | PASS, all 16 recorded results / exit 0 (15 named gates plus diff check) |
| Restore + Release build | PASS |
| W4 reconciliation, shift, checkout | PASS |
| U1 AR and signed entitlement | PASS |
| Analytics, owner reports, Manila boundaries | PASS |
| Backup/restore boundary and infrastructure edge | PASS |
| npm audit + NuGet vulnerability scan | PASS |
| Complete mocked Playwright | PASS, 41/41, zero retries |
| Strict visual suite | PASS, 16/16 specs covering 44 baselines |
| Deliberate visual mismatch | EXPECTED FAIL, 712,159 pixels / 0.68 ratio; original hash restored |
| Licensed disposable Host | `BLOCKED_NO_HOST`, connection refused at approved default port; no bypass |

The earlier run at `699201e…` is retained under `C:\Salryn-Evidence\I2\RUN_699201e` and failed only at the date-dependent owner-analytics assertion. It is not represented as a passing run.

## Verdict

**FINAL INTEGRATED SOURCE CANDIDATE — READY FOR ONE ENTIRE-APP REVIEW.** This is not release or production acceptance. Physical drawer/operator acceptance is NOT_RUN; the five W4 policies and dedicated reviewer role remain pending Kirch approval; LicensingOps remains the last gate.
