# PRE-BETA-B — POS Activation Copy Cleanup Report

## 1. Branch / base / final SHA

| Item | Value |
| --- | --- |
| Branch | `KIRCH-PRE-BETA-B-POS-ACTIVATION-COPY-CLEANUP` |
| Base | `KIRCH-BETA-READINESS-R1-MASTER-PLAN` @ `ce7ae7d841a39b8c8ef42d0a879c7a941b26d6b1` |
| Final SHA | `a90a98ec7b8f506999a392f88be3340c26c9cbf3` |
| Protected `KIRCH-SANDBOX` | `1f74cd8bcaca1a5df487aa61677ee9829dd7fedb` (unchanged) |
| Protected `KIRCH-INSTALLER-M1` | `6b9c46676390b8a86a989eca600cb02e406ce865` (unchanged) |

## 2. Lane A studied result

LicensingOps branch `KIRCH-LICOPS-M2-CLIENT-FRIENDLY-ISSUANCE` @ `4ec66e46e18b0638d4832ed4c91d0706bd481505`.

Studied: report, admin UI, store catalog, ledger, host parse, issue, machine code, CLI, selfTest.

Verified gates: `npm test` PASS, `npm run build` PASS, Host issue+verify PASS, Terminal-from-Host issue+verify PASS, invalid machine code rejected, self-test covers terminal-as-host / flat JSON / no private key.

## 3. Lane A accepted or repaired

**Accepted — no repairs.**  
Decision: **`LICOPS_CLIENT_FRIENDLY_ISSUANCE_REVIEW_PASSED`**

## 4. POS source studied

- Beta readiness master plan + lane matrix
- `LicensePage.tsx`, `TerminalActivationPage.tsx`, `HostLanControlPage.tsx`
- Related PRE-I1 / beta licensing smoke checks
- `terminalLicenseStorage.ts` (logic preserved)

## 5. Copy problems found

| Surface | Problem |
| --- | --- |
| Host License activation | Dual Host/Terminal instruction block still told terminal path to include **Host License ID** |
| Terminal Activation | Explicit “Include … Host License ID …” |
| Host Terminal Management | No admin copy-ready request block with internal Host License ID / Store ID |
| Normal path | Store ID shown as primary on active terminal (now Store display + internal under details) |

## 6. Host License page changes

- Title remains **Activate Salryn** / active **License**
- **Copy Host Machine Code**
- LicensingOps store + paste machine code steps
- “Salryn generates this Machine Code. LicensingOps signs…”
- Terminal path no longer asks end-user for Host License ID
- Host License ID / Store under support details only when active
- Import remains secondary when active

## 7. Terminal Activation page changes

- **Copy Terminal Machine Code**
- Send code + terminal name only to admin / salryn.info@gmail.com
- Explicit: do not need Host License ID or Store ID
- Import: signed Terminal JSON only; Host cannot activate terminal
- Active: Licensed to, Terminal ID, Store (customer); Host License ID under support details

## 8. Host Terminal Management copy changes

- Alert: one Terminal license per device; terminal users need only machine code + name
- Admin card: Store name, Store ID, Host License ID, Host Machine Code
- Copy-ready Terminal License Request block

## 9. Intentionally not changed

- License validation / type boundary / wrapper rejection / machine binding logic
- LicensingOps (Stage 1 clean)
- LAN shell parity (Lane E), logo, backup, scanner, printer, package refresh
- Installer / protected refs / cloud

## 10. Tests run

| Gate | Result |
| --- | --- |
| `dotnet build Salryn.sln` | PASS |
| `lic-beta-manual-host-terminal-activation` | PASS |
| `lic-lan-terminal-entitlement-enforcement` | PASS |
| `pre-i1-fresh-license-first-flow` | PASS |
| `pre-i1-lan-terminal-activation-management-beta` | PASS |
| `licensing-m3-local-enforcement-boundaries` | PASS |
| `npm --prefix src/Salryn.App.Web run build` | PASS |
| `git diff --check` | PASS |
| Full CoreSmoke | NOT RUN (focused gates green; full suite optional) |

Smoke checks updated to prevent regression of Host License ID terminal-facing copy.

## 11. Manual app proof

Source/static proof via updated smoke guards + web build. Runtime Host/Terminal import with LicensingOps M2 JSON is logic-unchanged; recommended Kirch browser walkthrough on package when convenient.

## 12. Post-write review result

| Check | Result |
| --- | --- |
| No terminal-facing Host License ID request | PASS |
| No terminal-facing StoreId invent instruction | PASS |
| Host Machine Code clear | PASS |
| Terminal Machine Code clear | PASS |
| Salryn generates / LicensingOps signs | PASS |
| Host active page not stale | PASS |
| Terminal active page clean | PASS |
| Admin internal details present | PASS |
| Logic unchanged | PASS |
| Tests updated | PASS |

## 13. Remaining risks

1. Full CoreSmoke not re-run end-to-end in this lane.
2. Host Terminal Management request block needs a Host license loaded for IDs to populate.
3. Lane E still owns full LAN shell parity.

## 14. Final decision

**`POS_ACTIVATION_COPY_CLEANUP_PASSED`**
