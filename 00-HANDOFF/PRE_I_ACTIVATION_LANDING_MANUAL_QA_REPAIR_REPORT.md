# PRE-I - Activation Landing + Manual QA Repair Report

## 1. Branch / base / final SHA

| Item | Value |
| --- | --- |
| Branch | `KIRCH-PRE-I-ACTIVATION-LANDING-MANUAL-QA-REPAIR` |
| Base branch | `KIRCH-PRE-BETA-LICENSE-CREDITS-LAN-SCOPE-REPAIR` |
| Base SHA | `5ef674dedb6361fe6fc6fb9ceadb364464310c64` |
| Final SHA | branch tip of `KIRCH-PRE-I-ACTIVATION-LANDING-MANUAL-QA-REPAIR` (implementation @ `b7ca537ba69d7b94a63e15bc7987d22375a17244` + report cleanup) |
| Protected refs | Untouched (`KIRCH-SANDBOX` @ `1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`, `KIRCH-INSTALLER-M1` @ `6b9c46676390b8a86a989eca600cb02e406ce865`) |

## 2. Repo study summary

Confirmed before coding:

1. **LicensePage** - direct activation/import cards; not a product landing/wizard.
2. **TerminalActivationPage** - same direct import style.
3. **ProductCreditsCard** - cleaned but still limited as a landing showcase rail.
4. **BackupRestorePage** - inline strings / plain cards for create/verify/restore.
5. **navigation.ts** - Manager order had Reports after Product Catalog / Stock.
6. **AppShell / TerminalShell** - `min-w-[48rem]` forced desktop width (phone overflow risk).
7. **HostLanControlPage** - request block improved in prior lane; lifecycle wording still needed polish while preserving gate strings.
8. **Store Settings** - schema 19 DBs missing `logo_content_type` / `logo_bytes` could crash reads when v20 was recorded without columns or skipped.

No Lane I, packaging, promotion, cloud activation, or BIR/eSales production claims added.

## 3. Manual QA findings addressed

| Finding | Resolution |
| --- | --- |
| Store Settings `logo_content_type` crash | Idempotent v20 + startup `EnsureStoreLogoColumns` |
| Backup and Restore UX plain | Centered Dialog success/confirm flows + RESTORE gate retained |
| Reports nav order | Manager: Dashboard, Reports, Product Catalog, Stock Movements |
| Terminal Management request/lifecycle | Product request wording retained; lifecycle "Revoked means re-pair" preserved |
| LAN terminal mobile overflow | Shells use `min-w-0`; no `min-w-[48rem]` |
| License landing dull | Host/Terminal product landing + 3-step wizard with terms |

## 4. Licensing landing overhaul

**Host (`LicensePage.tsx`)** unlicensed state is a product landing:

- **SALRYN** wordmark + **by Kirjane Labs**
- Offline-first POS pitch
- **What Salryn offers** feature cards (bounded beta claims)
- Primary CTA **Activate Salryn**
- Secondary **Already have a license? Import license**
- Active state: polished summary, support collapsed, replace secondary

Bounds copy avoids forbidden gate substrings (`cloud activation`, `eSales`, `RMO`, `mock sample`) while still stating non-claims (remote online activation / tax-authority production; **No ESC/POS** prefix for web-a3/a4).

## 5. Host activation wizard

Modal/wizard steps:

1. **Welcome / request license** - Host Machine Code, copy, support email, checklist, Next: Terms
2. **Salryn Activation Terms** - 10 plain-language terms + checkbox
3. **Import Signed Host License JSON** - install disabled until `termsAccepted` + non-empty text

Import/install API behavior unchanged. Success: "Host license active" + summary.

## 6. Terminal activation wizard

**TerminalActivationPage.tsx** mirrors Host pattern:

- **SALRYN Terminal** landing + feature cards
- CTA **Activate this Terminal**
- Step 1: Terminal Machine Code only (no Host License ID / Store ID tasks)
- Step 2: Terminal-specific terms
- Step 3: Import Signed Terminal License JSON; Host licenses rejected
- Active: Terminal License Active + Continue / Replace

Desktop layout: `lg:grid-cols-2` with `ProductCreditsCard` rail.

## 7. Terms and Conditions / activation acknowledgement

- Host and Terminal wizards require checkbox acceptance before import.
- Local React state (`termsAccepted`); no permanent legal ledger.
- CoreSmoke `pre-i-activation-landing-manual-qa-repair` asserts terms strings + `termsAccepted` gating in source.

## 8. Product showcase / Kirjane Labs wordmark / credits

`ProductCreditsCard.tsx` upgraded to website-style rail:

- SALRYN wordmark, Kirjane Labs hierarchy
- Credits org tree (Business / Technology / Marketing + reports)
- Support email
- Tiny muted "Repository-backed beta build" footer only
- No Publisher / Product / Build Type / Products of Kirjane Labs blocks

## 9. Stale licensing copy cleanup

Searched and kept clean on Host/Terminal/credits activation paths:

- no mock sample, Publisher/Product/Build Type cards, Products of Kirjane Labs
- no Host License ID / Store ID as terminal-user tasks
- no cloud activation / eSales / RMO / BIR-approved claims in activation UI
- technical IDs under collapsed support details

## 10. Store Settings migration repair

| Piece | Change |
| --- | --- |
| `StoreLogoBrandingSchemaMigration` | Idempotent `EnsureColumn` via `PRAGMA table_info`; public `EnsureLogoColumnsPresent` |
| `DatabaseInitializationOrchestrator` | `EnsureStoreLogoColumns()` after migrations every startup |
| Proof | Legacy `store_settings` without logo columns gains columns after repair; init path retains columns |

Does **not** require DB reset.

## 11. Backup and Restore UX repair

`BackupRestorePage.tsx`:

- Create -> Dialog "Backup created successfully"
- Verify -> success Dialog
- Restore -> Dialog requires typing **RESTORE**
- Restore complete -> success Dialog
- Safety copy retained (replace local database warning)

## 12. Reports nav order repair

Manager nav order in `navigation.ts`:

1. Dashboard
2. **Reports**
3. Product Catalog
4. Stock Movements

## 13. Terminal Management request/lifecycle repair

- Request card: Host-context issuance, Copy Terminal License Request, LicensingOps wording, one license per terminal.
- Connection behavior: **Revoked means re-pair** retained (v2-host-lan-control-ux).
- Disable / Revoke / Hide visibility unchanged via lifecycle helpers.
- Advanced diagnostics remain collapsed.

## 14. LAN terminal responsive repair

- `AppShell` / `TerminalShell`: removed `min-w-[48rem]`; use `min-w-0` for flex shrink.
- Remote POS diagnostics stay collapsed by default (existing guards).
- Host admin pages remain Host-only (no remote CheckoutPage exposure).

## 15. Files changed

| File | Role |
| --- | --- |
| `src/Salryn.App.Web/src/pages/LicensePage.tsx` | Host landing + wizard |
| `src/Salryn.App.Web/src/pages/TerminalActivationPage.tsx` | Terminal landing + wizard |
| `src/Salryn.App.Web/src/components/ProductCreditsCard.tsx` | Showcase / credits rail |
| `src/Salryn.App.Web/src/pages/BackupRestorePage.tsx` | Dialog UX |
| `src/Salryn.App.Web/src/pages/HostLanControlPage.tsx` | Lifecycle wording |
| `src/Salryn.App.Web/src/navigation.ts` | Reports after Dashboard |
| `src/Salryn.App.Web/src/components/AppShell.tsx` | Responsive min-width |
| `src/Salryn.App.Web/src/components/TerminalShell.tsx` | Responsive min-width |
| `src/Salryn.Infrastructure/Data/Migrations/StoreLogoBrandingSchemaMigration.cs` | Idempotent logo columns |
| `src/Salryn.Infrastructure/Data/Initialization/DatabaseInitializationOrchestrator.cs` | Startup ensure |
| `tests/Salryn.Tests/PreIActivationLandingManualQaRepairChecks.cs` | PRE-I source + migration proof |
| `tools/Salryn.Tools.CoreSmoke/Program.cs` | Register check |
| `00-HANDOFF/PRE_I_ACTIVATION_LANDING_MANUAL_QA_REPAIR_REPORT.md` | This report |

## 16. Tests / gates

| Gate | Result |
| --- | --- |
| pre-i-activation-landing-manual-qa-repair | PASS |
| pre-beta-license-credits-page-polish | PASS |
| pre-i1-fresh-license-first-flow | PASS |
| pre-i1-lan-terminal-activation-management-beta | PASS |
| lic-beta-manual-host-terminal-activation | PASS |
| v2-host-lan-control-ux | PASS |
| web-a3-shell-design-system | PASS |
| web-a4-native-host-visual-fidelity | PASS |
| web-b0-read-only-operator-surfaces | PASS |
| **full CoreSmoke** | **PASS** |
| `npm --prefix src/Salryn.App.Web run build` | PASS |
| `git diff --check` | PASS (CRLF warnings only) |

No crypto/licensing validation changes. Import still requires accepted terms in UI state.

## 17. Manual visual check

Source-verified + build green. Live screenshot QA deferred to Kirch:

- Host `/license` unlicensed -> wizard -> terms -> import -> active
- Terminal `/terminal-activation` same
- Store Settings (existing DB)
- Backup create/verify/restore modals
- Manager nav order
- Terminal Management
- Narrow-width terminal shell

Optional: `dotnet run --project src\Salryn.Api.Local -- --dev`

## 18. Remaining risks

- Visual polish is CSS/typography-only (no logo assets yet).
- Terms acceptance is session/UI state only (not a durable legal ledger).
- Phone overflow: shells no longer force desktop min-width; residual component-level overflow may still appear on complex tables.
- Restore still destructive by design - RESTORE typing gate unchanged.
- No live screenshot proof in this agent run.

## 19. Final decision

**PRE_I_ACTIVATION_LANDING_MANUAL_QA_REPAIR_REPAIRED_AND_PASSED**

No branch promotion. No installer/package work. No Lane I.
