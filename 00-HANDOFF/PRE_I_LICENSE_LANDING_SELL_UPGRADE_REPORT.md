# PRE-I - License Landing Sell Upgrade Report

## 1. Branch / base / final SHA

| Item | Value |
| --- | --- |
| Branch | `KIRCH-PRE-I-LICENSE-LANDING-SELL-UPGRADE` |
| Base branch | `KIRCH-PRE-I-ACTIVATION-LANDING-MANUAL-QA-REPAIR` |
| Base SHA | `13b78361f0d75ec541954b45ec8afc580456ca51` |
| Final implementation SHA | `aa2defcc357352fb74d7e9cc9feede631683d3cb` |
| Protected refs | Untouched: `KIRCH-SANDBOX` at `1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`; `KIRCH-INSTALLER-M1` at `6b9c46676390b8a86a989eca600cb02e406ce865` |

The report-only commit follows the implementation commit; the pushed branch tip is recorded in the final Codex handoff.

## 2. Source studied

Read before implementation:

- `00-HANDOFF/PRE_I_ACTIVATION_LANDING_MANUAL_QA_REPAIR_REPORT.md`
- `00-HANDOFF/BETA_CAPABILITY_MATRIX.md`
- `00-HANDOFF/PRE_BETA_LICENSE_CREDITS_LAN_SCOPE_REPAIR_REPORT.md`
- `00-HANDOFF/PRE_BETA_LICENSE_CREDITS_CARD_CLEANUP_REPORT.md`
- Host and Terminal activation pages, shared `ProductCreditsCard`, `App`, `AppShell`, UI primitives, Tailwind configuration, and global CSS
- Existing licensing, first-run, credits, LAN Terminal, and web visual source guards

## 3. Manual QA design issue

The pre-existing Host License page was functionally sound but still read as two white admin cards with a gradient header. The product story, activation path, CTA priority, and Kirjane Labs identity did not have enough visual rhythm or confidence for Salryn's first landing page. A hidden source-only copy block also carried legacy guard phrases that were not part of the real user experience.

## 4. Visual direction implemented

- Dark black/navy landing canvas with restrained blue, violet, cyan, and emerald accents
- CSS-only radial light, contour rings, and fading grid artwork; no image assets or network dependency
- Large typographic `SALRYN` wordmark and `by Kirjane Labs` identity
- Stronger scale, spacing, contrast, glass surfaces, and rounded product-facing composition
- Responsive single-column flow on narrow screens and balanced two-column hero on desktop

## 5. Host landing improvements

- Replaced the boxed two-card first impression with a continuous premium landing composition
- Added a clear store-owner outcome statement: less manual work and more local control
- Integrated the Host Machine Code and three-step activation explanation into the hero
- Added `Sell clearly`, `Know your stock`, and `Stay recoverable` value pillars
- Retained a polished dark active-license state with licensed customer/store/type, offline truth, support dates, and collapsed technical details

## 6. Product/value copy improvements

The page now describes Salryn as an offline-first local POS for checkout, products, stock, receipts, backups, and LAN terminals. Feature chips use bounded capability wording:

- Offline Host POS
- Checkout and receipts
- Product catalog
- Stock movements
- Portable backup files
- LAN Cashier/Manager terminals
- Keyboard-wedge barcode workflow
- Windows print queue receipts

Normal landing copy no longer carries negative protocol/hardware disclaimers or developer-facing metadata blocks.

## 7. CTA improvements

- Primary: `Activate Salryn` with a blue-to-violet emphasis and directional affordance
- Secondary: `Import existing license` in a quieter glass treatment
- Utility: `Refresh license status` integrated under the CTA pair with an in-progress spinner
- Direct import enters the terms step first, preserving acceptance before installation instead of opening a disabled import dead end

## 8. Activation wizard preservation/improvement

Preserved:

- Three-step request, terms, and signed Host license import flow
- Existing status fetch, machine code copy, `installLicense` API call, friendly install errors, and success refresh
- Terms acceptance plus non-empty license document required before install
- Host/Terminal license separation and device-bound activation truth

Improved:

- Dark branded modal header, stronger hierarchy, softer overlay, and mobile bottom-sheet behavior
- Clear request checklist, Host Machine Code surface, Infodesk contact, and signed-license return explanation
- Concise terms with local data, backup, device-license, hardware-workflow, compliance, and authorized-use boundaries
- Removed the hidden legacy copy anchor and updated only the stale source guards that depended on it

## 9. Credits/showcase improvements

`ProductCreditsCard` now supports an integrated dark landing variant while retaining its existing default card variant for Terminal Activation. The Host trust section is a wide part of the landing rhythm instead of a competing admin rail. It preserves the required hierarchy:

- Business Lead: Paul Gamayot
  - Business Development: James Karl Tara
- Technology Lead: Kirch Ivan Balite
  - Dev Ops Lead: Osiris Kedigadash Palac
- Marketing Lead: Jesse Edwin Culas

The Infodesk email and tiny repository-backed beta footer remain. No Publisher, Product, Build Type, or product-list metadata boxes were introduced.

## 10. Stale copy cleanup

Targeted user-facing Host landing/credits scan found no remaining matches for:

- mock sample
- Products of Kirjane Labs
- Build Type
- cloud activation
- BIR-ready
- official receipt accreditation
- ESC/POS
- cash drawer
- cutter
- hardware bundle
- Store ID / Host License ID discovery tasks

Technical Host License ID and Store ID values remain only in the collapsed active-license support details. Internal error classification strings remain implementation details and are translated to friendly messages.

## 11. What was intentionally not changed

- License cryptography, schema, signatures, validation, machine-code generation, wrapper rejection, database, and endpoints
- Signed license install behavior or active license truth
- Backup & Restore, Store Settings, Terminal Management, LAN shell, package/I1, LicensingOps, or compliance implementation
- No cloud activation, BIR/eSales production, official receipt, direct printer-protocol, drawer/cutter, or hardware-bundle claim
- No broad Terminal Activation redesign; only compatibility verification of the unchanged default shared credits card

## 12. Tests/gates

| Gate | Result |
| --- | --- |
| `dotnet build Salryn.sln` | PASS - 0 warnings, 0 errors |
| `pre-i-license-landing-sell-upgrade` | PASS |
| `pre-i-activation-landing-manual-qa-repair` | PASS |
| `pre-beta-license-credits-page-polish` | PASS |
| `lic-beta-manual-host-terminal-activation` | PASS |
| `pre-i1-fresh-license-first-flow` | PASS |
| `pre-i1-lan-terminal-activation-management-beta` | PASS |
| `web-a3-shell-design-system` | PASS |
| `web-a4-native-host-visual-fidelity` | PASS |
| Full CoreSmoke | PASS |
| `npm --prefix src/Salryn.App.Web run build` | PASS |
| `git diff --check` | PASS; line-ending notices only |

Vite retains its existing advisory that the main JavaScript chunk exceeds 500 kB; this lane added no package or code-splitting work.

## 13. Manual visual check

Live browser QA was completed against `dotnet run --project src\Salryn.Api.Local -- --dev`:

- Fresh unlicensed Host landing at `/license`: PASS at the default 1280 px viewport
- Activate Salryn request step: PASS
- Terms step: PASS; Next disabled before acceptance and enabled after acceptance
- Import step: PASS; Install disabled with an empty license document
- Narrow Host landing and request modal at 390 x 844: PASS; document width 380 px with no horizontal overflow
- Existing active Terminal Activation page through the LAN client entry: PASS; default shared credits card rendered and no browser console errors

The active Host state could not be live-visualized because the development Host had no signed Host license. It was source-reviewed and covered by the production build and source guards. No fake license was installed.

## 14. Remaining risks

- Active Host visual state still needs Kirch's signed-license screenshot pass.
- Final aesthetic acceptance remains subjective; the desktop and narrow browser checks show no structural or overflow defect.
- The existing Vite chunk-size advisory remains outside this narrow visual lane.

## 15. Final decision

**LICENSE_LANDING_SELL_UPGRADE_REPAIRED_AND_PASSED**

No branch promotion. No package/I1 work. No protected ref changes.
