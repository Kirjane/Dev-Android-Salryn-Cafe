# PRE-I License Page Minimalist Rebuild Report

## 1. Branch / base / final SHA

| Item | Value |
| --- | --- |
| Branch | `KIRCH-PRE-I-LICENSE-PAGE-MINIMALIST-REBUILD` |
| Base branch | `KIRCH-PRE-I-DEV-STATE-RESET-TRUTH` |
| Base SHA | `f965a02b976076ec51c67afe3947bc94111ba82f` |
| Final tip SHA | `59aab66d4bb04e058f685180f25557f1790951a3` |
| Protected | `KIRCH-SANDBOX`, `KIRCH-INSTALLER-M1` not modified |

## 2. Design lock implemented

Implemented from:

- `PRE_I_LICENSE_PAGE_MINIMALIST_DESIGN_SPEC.md`
- `PRE_I_SURGICAL_STATE_LICENSE_UX_REVIEW.md`

Visual tokens:

- page `#F4F5F7`, cards white, text `#111827` / `#4B5563` / `#6B7280`
- border `#E5E7EB`, accent `#1F4B7A`, radius 12px / 8px
- max width 920px, 24px padding, no heavy shadow

## 3. What was removed from old design

- Dark `#050815` launch stage
- `LandingBackdrop` radial gradients / grid / contour rings
- Gradient wordmark and gradient CTAs
- Sparkles / marketing icons
- “What Salryn offers” value columns
- Feature pill wall (`HOST_FEATURES`)
- Glass / blur cards
- Loud “Ready in three steps” marketing rail
- Competing `ProductCreditsCard` on Host License page

## 4. Fresh page structure

- Header: Salryn · by Kirjane Labs · status “Not activated”
- Left card: Activate Salryn, locked body copy, Import license, Copy Machine Code
- Right card: Machine Code mono, copy, support email, terminal license note
- Footer: Salryn by Kirjane Labs · Technology Lead: Kirch Ivan Balite

## 5. Active page structure

- Single white card
- Title: Salryn is activated
- Body: This Host is licensed for local use on this device
- Summary fields + device binding
- Technical IDs under “Show technical details”
- Replace license opens careful replace panel

## 6. Modal / terms flow

1. Machine Code  
2. Beta License Terms (checkbox required)  
3. Import Signed Host License JSON  

Import CTA still routes through terms (`step === "import" ? "terms" : step`).

## 7. Terms text update

Nine beta sections: Local license, Device binding, Data ownership, Backups, Hardware limits, Receipts and compliance, LAN terminals, Support boundaries, Beta acceptance.

Checkbox: “I understand and accept these beta license terms.”

Replace still requires re-ack + “replaces the active local license” confirmation.

## 8. Product credits / footer handling

Host page: quiet footer only (no `ProductCreditsCard`).  
Terminal page: still uses shared `ProductCreditsCard` (unchanged).

## 9. Behavior preserved

- Machine code copy
- Terms gate before install
- Replace re-ack + consequence
- Host vs Terminal messaging
- Support email
- Friendly install errors
- Offline-after-install install success message
- Install/status API usage unchanged

## 10. Files changed

| File | Change |
| --- | --- |
| `src/Salryn.App.Web/src/pages/LicensePage.tsx` | Full minimalist rebuild |
| `tests/Salryn.Tests/PreILicensePageMinimalistRebuildChecks.cs` | New gate |
| `tests/Salryn.Tests/PreILicenseLandingSellUpgradeChecks.cs` | Point to minimalist contract |
| `tests/Salryn.Tests/PreIAutomatedQaProofChecks.cs` | Updated CTA/wordmark expectations |
| `tests/Salryn.Tests/PreI1FreshLicenseFirstFlowChecks.cs` | Updated copy expectations |
| `tests/Salryn.Tests/PreIActivationLandingManualQaRepairChecks.cs` | Host section updated |
| `tests/Salryn.Tests/PreBetaLicenseCreditsPagePolishChecks.cs` | Host credits expectation = footer |
| `tests/Salryn.Tests/LocalLicensingBetaManualHostTerminalActivationChecks.cs` | Host UI copy updated |
| `tools/Salryn.Tools.CoreSmoke/Program.cs` | Register new gate |
| `00-HANDOFF/PRE_I_LICENSE_PAGE_MINIMALIST_REBUILD_REPORT.md` | This report |

## 11. Gates run

| Gate | Result |
| --- | --- |
| `pre-i-license-page-minimalist-rebuild` | PASS |
| `pre-i-code-proven-qa-hardening` | PASS |
| `pre-i-automated-qa-proof` | PASS |
| `pre-i-p1-copy-guard-honesty` | PASS |
| `pre-i1-fresh-license-first-flow` | PASS |
| `lic-beta-manual-host-terminal-activation` | PASS |
| `pre-i-license-landing-sell-upgrade` | PASS |
| `pre-i-activation-landing-manual-qa-repair` | PASS |
| `pre-beta-license-credits-page-polish` | PASS |
| `pre-i-dev-state-reset-truth` | PASS |
| Full CoreSmoke | PASS |
| `npm run build` | PASS (chunk-size advisory only) |
| `git diff --check` | PASS |

## 12. Manual visual checklist (for Kirch)

- Fresh `/license`: light, calm, no glow/gradient  
- Machine Code visible without marketing chrome  
- Modal: Machine Code → Terms → Import; terms required  
- Active: Salryn is activated; technical details collapsed  
- Replace requires re-ack  
- 390px: stack cards, no horizontal overflow  

## 13. Remaining risks

- Terminal Activation still uses previous visual language (out of scope)  
- Browser residual state if site data not cleared after reset  
- Design is quiet; some operators may want slightly stronger brand accent later  

## 14. Final decision

**`PRE_I_LICENSE_PAGE_MINIMALIST_REBUILD_PASSED`**
