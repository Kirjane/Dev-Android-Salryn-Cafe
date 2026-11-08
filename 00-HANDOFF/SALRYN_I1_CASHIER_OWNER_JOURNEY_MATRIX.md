# Salryn I1 Cashier / Owner Journey Matrix

Tested SHA: `9bedd0819125ba643badbe6c8c54340b0563ce12`

| Journey | Persona / viewport | Evidence | Result |
|---|---|---|---|
| Cash checkout and over-tender/change | Cashier, desktop | W4 checkout CoreSmoke plus premium mocked checkout | PASS |
| Full Utang and Cash + Utang presentation/invariants | entitled cashier/admin | U1 AR/entitlement CoreSmoke and visual baselines 11, 16-18 | PASS |
| Linked cash collection vs noncash exclusion | disposable database | W4/source-event and U1 CoreSmoke | PASS |
| Open shift then physical count close | admin mock, 1366x768 | `w4-no-count`, `w4-count-zero` | PASS (mocked UI + backend regression; not physical proof) |
| Short count and ambiguous committed response | admin mock, 1366x768 | `w4-short-uncertain`; same ID observed twice | PASS |
| Changed close payload conflict | admin mock, 390x844 | `w4-over-conflict` | PASS |
| Missing source coverage | admin mock, 1024x768 | `w4-coverage-blocked`; no force-close control | PASS |
| Valid over/short/zero financial calculation | disposable database | W4 reconciliation CoreSmoke | PASS; integrated screenshots show zero/short/conflict, not every successful variance |
| Distinct reviewer authorization/self-review denial | disposable HTTP/CoreSmoke | W4 reconciliation CoreSmoke | PASS backend; **NO INTEGRATED REVIEW UI**, therefore denied-review screenshot NOT RUN |
| Shared modal keyboard/accessibility | desktop mocked UI | initial focus, Tab/Shift+Tab, Escape while busy, focus/scroll restoration | PASS |
| Owner dashboard/report Manila store-day money | admin/owner | analytics/report CoreSmoke and baselines 7, 17 | PASS |
| Sales drilldown reload/back | admin/owner | integrated Playwright deep-link test | PASS |
| Permission change/deep-link containment | restricted personas | persona baselines and URL replacement logic | PASS |
| 125% / 150% scale and responsive shell | 1024, 1280, 1920, 390 | strict baselines 32-37 | PASS |
| Licensed disposable Host journey | licensed dev Host | no lawful fixture present | BLOCKED_NO_HOST |
| Real drawer count/operator workflow | store hardware/operator | no physical hardware/operator in this environment | NOT_RUN |

Screenshot root: `C:\Salryn-Evidence\I1\RUN_9bedd08\frontend\test-results-e2e`. Contact sheet: `C:\Salryn-Evidence\I1\RUN_9bedd08\frontend\SALRYN_I1_SCREENSHOT_CONTACT_SHEET.png`.

The UI intentionally exposes no discrepancy-review workflow yet. Backend role denial is proven, but manufacturing a screenshot of a nonexistent surface would be false evidence. Adding that workflow requires an approved review-role policy and a separately scoped product decision.
