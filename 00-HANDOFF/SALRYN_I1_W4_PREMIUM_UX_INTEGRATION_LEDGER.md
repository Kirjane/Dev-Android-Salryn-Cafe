# Salryn I1 W4 + Premium UX Integration Ledger

Date: 2026-11-08 Asia/Manila  
Branch: `integration/salryn-w4-premium-ux-r1`  
Tested implementation SHA: `9bedd0819125ba643badbe6c8c54340b0563ce12`

## Source custody

| Input | Exact SHA | Custody |
|---|---|---|
| W4 / draft PR #22 | `62eadb201d344516f3c6e7bb491b18db0fc0b7ff` | integration base; untouched |
| Premium UX / draft PR #21 | `0ffa2674733fc9bba00629f6b17f3335625a4467` | merged by exact SHA; source branch untouched |
| Analytics / draft PR #18 | `ee16c9cc89055bcf7c2c155d20398c0bf43011ec` | ancestor; untouched |

The merge was performed only in `C:\Salryn-Integration-I1`. Git found no textual conflict, so the semantic conflict was resolved by reviewing and editing the auto-merged result. `CheckoutPage.tsx` retains the premium layout while using the W4 physical-count close body, permissions, immutable variance status, and no force-close. `api/checkout.ts` has zero bodyless close calls. `Dialog.tsx` adds initial focus, forward/reverse Tab trapping, inert background, scroll lock, trigger restoration, and busy dismissal protection. `App.tsx` makes owner-report and permission redirects durable URLs. The W4 runner now restores first and gates native tools on process exit code rather than treating non-fatal stderr warnings as termination.

## Exact-SHA executable evidence

Evidence root: `C:\Salryn-Evidence\I1\RUN_9bedd08`  
Manifest: `C:\Salryn-Evidence\I1\RUN_9bedd08\SHA256SUMS.txt` (61 files)

| Gate | Command | Result |
|---|---|---|
| W4 acceptance | `powershell -NoProfile -ExecutionPolicy Bypass -File tools/Run-W4Acceptance.ps1 -OutputDirectory C:\Salryn-Evidence\I1\RUN_9bedd08` | PASS, all 15 runner gates, exit 0 |
| Restore | `dotnet restore Salryn.sln` | PASS |
| Release build | `dotnet build Salryn.sln -c Release --no-restore` | PASS |
| W4 reconciliation | CoreSmoke `--only w4-cash-drawer-reconciliation` | PASS |
| Shift and checkout | CoreSmoke `v2-host-shift`, `v2-host-checkout` | PASS |
| U1 AR and entitlement | CoreSmoke `u1-accounts-receivable`, `u1-entitlement-gating` | PASS |
| Analytics and reports | CoreSmoke `owner-analytics-w0-w3`, `pos-r1-owner-reports` | PASS |
| Backup/migration boundary | CoreSmoke `pre-beta-d-portable-backup-import-export` | PASS |
| Cashier/session edge | InfrastructureSmoke `cashier-session-service-edge` | PASS |
| Dependency gates | `npm audit --audit-level=high`; `dotnet list Salryn.sln package --vulnerable --include-transitive` | PASS |
| Mocked browser suite | `npm run test:e2e` | PASS, 29/29; raw log in `frontend/npm-test-e2e.log` |
| Strict visual comparison | `npm run test:visual` | PASS, 16/16; raw log in `frontend/npm-test-visual.log` |
| Source hygiene | `git diff --check` | PASS |

The browser suite covers unchanged close replay using the same request ID, no duplicate close, payload conflict, missing coverage without force-close, initial focus, Tab/Shift+Tab trap, scroll restoration, busy Escape denial, mobile/1024 layout, and report reload/back deep links. A cold all-module test reached its final route but exceeded 30 seconds; its intentionally broad traversal received a scoped 60-second timeout and passed in 25.1 seconds on the final SHA. Assertions were not relaxed.

## Screenshot and prior-proof custody

- Five genuine integrated W4 screenshots and their Playwright artifacts are under `frontend/test-results-e2e`; the contact sheet is `frontend/SALRYN_I1_SCREENSHOT_CONTACT_SHEET.png`, SHA-256 `c9b35ed577dddf27e64dd092d333e371cf58425275ddfc9693cf27e7e87ca36`.
- All 42 committed premium baselines are in `src/Salryn.App.Web/e2e/__screenshots__`. Original Antigravity images were preserved before reviewed updates at `C:\Salryn-Evidence\I1\baseline-before-0ffa267`.
- W4 checkpoint ZIP: `C:\Salryn-Evidence\W4\SALRYN_W4_R3_PROOF_5f0d2d0.zip`, SHA-256 `44bb36f0da8bdc735f6ec81a03669875f5d89c09f0713056819e04ac82a0eea1`; it is not the R3 run.
- Separately named R3 run ZIP: `C:\Salryn-Evidence\W4\SALRYN_W4_R3_RUN_a8d7737.zip`, 15,454 bytes, SHA-256 `1459e108abe01623d05ad045ade0432bc05e11402913da97d336d4eab7d5d248`.
- Antigravity A0 ZIP exists locally with recorded SHA-256 `406bbde8fc79a5cca27e3183dc1b1c424524970dc47fc284d6350c229eb664a6`; A1-A4 ZIP exists with `b713007bd718c63dff3219456087b8f76d7837b8d7eab4be8c870d0e4c3a12f7`. Presence and hash were verified; contents were not independently approved.

## Verdict

Automated integrated candidate gates: **PASS**. Integrated licensed Host: **BLOCKED_NO_HOST**. Physical drawer/operator acceptance: **NOT_RUN**. Financial policies and dedicated reviewer role: **PENDING KIRCH APPROVAL / NOT APPROVED**. LicensingOps canonical activation: **NOT_RUN_LAST_GATE**. This is a draft engineering candidate only, not production acceptance.
