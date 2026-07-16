# PRE-I1 — License Page Product UX Polish Report

**Branch:** `KIRCH-PRE-I1-LICENSE-PAGE-PRODUCT-UX-POLISH`  
**Base:** `KIRCH-PRE-I1-FRESH-LICENSE-FIRST-FLOW` @ `c0529697c49aafbc275ca5d9e907e25f939ccbae`  
**Final SHA:** *(branch tip after commit)*  
**Status:** `LICENSE_PAGE_PRODUCT_UX_POLISHED`

**Protected refs (unchanged):**
- `KIRCH-SANDBOX` = `1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`
- `KIRCH-INSTALLER-M1` = `6b9c46676390b8a86a989eca600cb02e406ce865`

---

## 1. Branch / base / final SHA

See header. Final SHA filled at commit/push.

## 2. UI/copy removed from main client view

- Admin-only offline signed license status and import subtitle
- Offline-first license boundary alert
- Not cloud / subscription / payment / installer / BIR/RMO / eSales disclaimer wall
- LAN entitlement boundary / “production use is not claimed here” wall
- Technical “Current license” / “Store and device binding” / “Entitlements” cards as primary layout
- Features list as a main field
- Application / Store / Machine matched as primary fields
- path-bearing / empty-binding / type-invalid install warning jargon
- Host terminal-count purchase instruction (already forbidden; re-guarded)

## 3. UI/copy added

- Title: **Activate Salryn**
- Subtitle: needs a valid license before this device can be used
- Primary contact: **salryn.info@gmail.com**
- Promoted **Machine Code** block with copy button
- Separated **Host POS** vs **LAN Terminal** “what to send” lists
- Simple **Import License** section, checkbox, **Install License** button
- Soft replace warning (no technical reject catalog)
- **License Active** summary for valid Host/Terminal installs
- Collapsed optional support details (License ID, Store ID, Application, Machine matched)
- Friendlier install error messages (machine mismatch, signature, expired, terminal/host misuse)

## 4. Fresh launch proof

Source/flow guards in `pre-i1-fresh-license-first-flow` still require:

- Host license readiness before bootstrap
- License status/install pre-login reachability
- Terminal cannot unlock Host first-run setup

UI now titled Activate Salryn for that first gate screen.

## 5. Invalid license proof

- Backend validation unchanged
- UI maps reject messages to plain language
- CoreSmoke licensing suites still cover reject paths

## 6. Valid Host license flow

Not re-run as full manual browser session in this agent pass. Automated smoke continues to cover Host install validation, machine match, and Host-only first-run gate source contracts.

## 7. Host / Terminal copy proof

| Rule | Result |
|------|--------|
| No Host terminal-count field | Pass (source guards) |
| Host list: store, contact, contact number/email, Host Machine Code | Pass |
| Terminal list: store, Host License ID, terminal name/number, Terminal Machine Code | Pass |
| Separate Terminal license language | Pass |
| Terminal cannot unlock Host first-run | Pass (alert retained) |

## 8. Responsive layout check

Layout constrained with `max-w-3xl` + two-column grid that stacks on small widths (`md:grid-cols-2`). Source structure supports 1024px and 1366px without wide three-column tech cards. Full interactive pixel review not automated here.

## 9. Gates

| Gate | Result |
|------|--------|
| `dotnet build Salryn.sln` | PASS |
| lic-closeout | PASS |
| lic-beta | PASS |
| lic-lan | PASS |
| lic-m3 | PASS |
| web-a3 | PASS |
| web-a4 | PASS |
| web-b0 | PASS |
| pre-i1-fresh-license-first-flow | PASS |
| full CoreSmoke | PASS |
| web build | PASS |
| `git diff --check` | PASS |

## 10. Files changed

- `src/Salryn.App.Web/src/pages/LicensePage.tsx`
- `tests/Salryn.Tests/LocalLicensingBetaManualHostTerminalActivationChecks.cs`
- `tests/Salryn.Tests/LocalLicensingM3LocalEnforcementBoundaryChecks.cs`
- `tests/Salryn.Tests/PreI1FreshLicenseFirstFlowChecks.cs`
- `00-HANDOFF/PRE_I1_LICENSE_PAGE_PRODUCT_UX_POLISH_REPORT.md`

## 11. Remaining risks

- Backend error strings may still briefly surface if not mapped by `friendlyInstallError`
- Support-details collapse is optional secondary UI; operators can still open technical IDs
- No automated screenshot/visual regression at 1024/1366
- I1 installer not started (intentional)

## 12. Final decision

`LICENSE_PAGE_PRODUCT_UX_POLISHED`
