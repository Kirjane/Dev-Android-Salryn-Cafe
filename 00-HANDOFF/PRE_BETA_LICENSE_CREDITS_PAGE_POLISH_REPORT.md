# PRE-BETA — License & Credits Page Polish Report

## 1. Branch / base / final SHA

| Item | Value |
| --- | --- |
| Branch | `KIRCH-PRE-BETA-LICENSE-CREDITS-PAGE-POLISH` |
| Base | `KIRCH-PRE-BETA-CDFGH-STACK-REVIEW-AND-REPAIR` @ `dee4f230595d0abdf72d8a84b4962da23192b5a9` |
| Final SHA | *(branch tip after push)* |
| Protected SANDBOX | untouched |
| Protected INSTALLER-M1 | untouched |

## 2. Decision

**`LICENSE_CREDITS_PAGE_POLISH_PASSED`**

## 3. Source studied

- `LicensePage.tsx`, `TerminalActivationPage.tsx`
- Lane B / E / C / CDFGH reports and beta capability matrix
- `LocalLicensingBetaManualHostTerminalActivationChecks`, `PreI1FreshLicenseFirstFlowChecks`, `PreI1LanTerminalActivationManagementBetaChecks`
- AppShell / TerminalShell (no behavior change)

## 4. Design reference summary

Pigstep-style **two-card** product screen:

- Light `#f4f6f9` canvas
- Left card(s) = licensing / activation / machine code / import
- Right card = product identity / credits / org / support
- Navy + emerald accents (existing Salryn tokens)
- Desktop `lg:grid-cols-2`; stacks on small screens
- Sticky credits on large screens

## 5. Files changed

| File | Role |
| --- | --- |
| `src/Salryn.App.Web/src/components/ProductCreditsCard.tsx` | **New** shared credits / identity card |
| `src/Salryn.App.Web/src/pages/LicensePage.tsx` | Host two-card layout polish |
| `src/Salryn.App.Web/src/pages/TerminalActivationPage.tsx` | Terminal two-card layout polish |
| `tests/Salryn.Tests/PreBetaLicenseCreditsPagePolishChecks.cs` | **New** source guards |
| `tools/Salryn.Tools.CoreSmoke/Program.cs` | Register `pre-beta-license-credits-page-polish` |
| `00-HANDOFF/PRE_BETA_LICENSE_CREDITS_PAGE_POLISH_REPORT.md` | This report |

## 6. Licensing card changes

### Host (`LicensePage`)

- Product header: **Salryn** / **Activate Salryn** or **License**
- Active: **Status: Active**, Licensed to, Store, License type, Host Machine Code, support/update dates
- Support IDs (Host License ID, Store ID) only under **Show support details**
- Activation: Host Machine Code + **Copy Host Machine Code**
- Lane B copy retained: *Salryn generates this Machine Code. LicensingOps signs…*
- **Import Signed Host License JSON** with replace confirmation
- No terminal count / cloud activation / path-bearing jargon

### Terminal (`TerminalActivationPage`)

- Header: **Salryn Terminal** / **Terminal Activation**
- **This Terminal Machine Code** + **Copy Terminal Machine Code**
- Lane B copy retained: store admin / Kirjane Labs issues Terminal license from Host; **do not need a Host License ID**
- **Import Signed Terminal License JSON**
- Active: Licensed to / Store / License type; IDs under support details only
- Host licenses cannot activate a terminal (copy retained)

## 7. Credits card changes

- **SALRYN by Kirjane Labs**
- **Licensing & Credits**
- Publisher: Kirjane Labs
- Product: Salryn
- Build Type: **Repository-backed beta build** (not mock sample)
- Products of Kirjane Labs: Salryn only
- Support email: salryn.info@gmail.com
- Footer: © 2026 Kirjane Labs; controlled client-use statement

## 8. Org chart correctness

```
KIRJANE LABS
     |
  [three peer branches]
Business Lead          Technology Lead          Marketing Lead
Paul Gamayot           Kirch Ivan Balite        Jesse Edwin Culas
     |                      |
Business Development   Dev Ops Lead
James Karl Tara        Osiris Kedigadash Palac
```

- Dev Ops under Technology Lead (not Marketing)
- Technology Lead title retained (not Software Development)
- No Salryn-as-person; no wrong names

## 9. Host activation proof (automated)

- Machine code display + copy strings present
- Import Host license JSON path present
- Active summary + collapsed support details present
- Focused: `lic-beta-manual-host-terminal-activation` PASS
- Focused: `pre-i1-fresh-license-first-flow` PASS

## 10. Terminal activation proof (automated)

- Terminal Machine Code + copy present
- No Host License ID task for terminal users
- Import Terminal license path present
- Focused: `pre-i1-lan-terminal-activation-management-beta` PASS
- Focused: `lic-beta-manual-host-terminal-activation` PASS

## 11. Responsive / no-truncation proof

- Source: `lg:grid-cols-2` + single-column default
- Machine codes: `break-all`
- Org names: `break-words`, multi-column peer nodes
- No external fonts/network assets
- Browser screenshot not captured in this automation environment; layout is source-verified. Physical desktop/narrow recheck remains optional manual DO LATER.

## 12. RMO remained hidden / internal

- No RMO / eSales / BIR-approved client-facing copy on license or credits surfaces
- WEB-A3 / WEB-A4 PASS after removing accidental RMO mention from a code comment

## 13. Tests / gates

| Gate | Result |
| --- | --- |
| `dotnet build` (CoreSmoke project / tests) | PASS |
| `lic-beta-manual-host-terminal-activation` | PASS |
| `pre-i1-fresh-license-first-flow` | PASS |
| `pre-i1-lan-terminal-activation-management-beta` | PASS |
| `pre-beta-license-credits-page-polish` | PASS |
| `web-a3-shell-design-system` | PASS |
| `web-a4-native-host-visual-fidelity` | PASS |
| **full CoreSmoke** | **PASS** |
| web production build | PASS |
| `git diff --check` | PASS (CRLF warnings only) |

## 14. Remaining risks

- Visual polish subjective; optional human pixel review on real Host + Terminal browsers
- Credits content is static product identity (not live CMS)
- No change to license crypto / machine binding / LicensingOps

## 15. Non-claims

- Not cloud activation
- Not public MSI/installer done
- Not BIR/eSales production
- Not RMO client-facing
- Not Lane I package refresh
