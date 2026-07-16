# PRE-BETA — License Credits LAN Scope Repair Report

## 1. Branch / base / final SHA

| Item | Value |
| --- | --- |
| Branch | `KIRCH-PRE-BETA-LICENSE-CREDITS-LAN-SCOPE-REPAIR` |
| Base | `KIRCH-PRE-BETA-LICENSE-CREDITS-CARD-CLEANUP` @ `95095f93f8e7ed953e3930e1e7c23d6ac376b812` |
| Final SHA | `c835a5fad26c13d9f7e9c3b5e826186fcca99699` |
| Protected refs | Untouched |

## 2. Manual QA issue

Cleanup lane focused on shared `ProductCreditsCard` without explicit LAN coverage. Kirch required Host + Terminal Activation + Terminal Management alignment.

## 3. Files changed

| File | Change |
| --- | --- |
| `src/Salryn.App.Web/src/pages/HostLanControlPage.tsx` | Cleaned terminal license request card/copy |
| `tests/Salryn.Tests/PreBetaLicenseCreditsPagePolishChecks.cs` | LAN/Terminal/Management guards |
| `tests/Salryn.Tests/PreI1LanTerminalActivationManagementBetaChecks.cs` | Match cleaned Host guidance strings |
| `00-HANDOFF/PRE_BETA_LICENSE_CREDITS_LAN_SCOPE_REPAIR_REPORT.md` | This report |

`ProductCreditsCard` / `TerminalActivationPage` already used the cleaned shared card — verified, no logic change required.

## 4. Host License page result

Unchanged: two-card layout + cleaned credits card from prior cleanup lane.

## 5. Terminal Activation page result

Already renders shared `ProductCreditsCard` (cleaned credits). Confirmed:

- no Publisher/Product/Build Type/Products metadata boxes  
- no end-user Host License ID / Store ID tasks  
- Terminal Machine Code + import paths intact  

## 6. Terminal Management result

Request card reworded:

- “Terminal licenses are issued from the Host license context.”  
- “Copy this request for LicensingOps.”  
- “Each LAN terminal needs its own Terminal license.”  

Admin/support IDs retained (Store, Store ID, Host License ID, Host Machine Code). No full credits card duplication.

## 7. Copy removed / preserved

| Removed / avoided | Preserved |
| --- | --- |
| Debug-sounding “internal only” primary framing | Copy Terminal License Request |
| End-user Host License ID burden | Machine codes + one-license-per-device |
| Credits metadata boxes on terminal UI | Admin Store/Host IDs for issuance |

## 8. License behavior unchanged

No crypto, validation, pairing, or machine-code generation changes.

## 9. RMO hidden/internal

Guards: no RMO / eSales / BIR-approved on HostLanControl / credits / terminal paths.

## 10. Tests / gates

| Gate | Result |
| --- | --- |
| pre-beta-license-credits-page-polish | PASS |
| lic-beta-manual-host-terminal-activation | PASS |
| pre-i1-fresh-license-first-flow | PASS |
| pre-i1-lan-terminal-activation-management-beta | PASS |
| web-a3 / web-a4 | PASS |
| full CoreSmoke | PASS |
| web build | PASS |

## 11. Manual visual check

Source-verified. Live screenshots not taken in this environment. Optional recheck: `/license`, `/terminal-activation`, Host Terminal Management.

## 12. Remaining risks

Subjective polish; optional pixel recheck of Terminal Management card.

## 13. Final decision

**`LICENSE_CREDITS_LAN_SCOPE_REPAIR_PASSED`**
