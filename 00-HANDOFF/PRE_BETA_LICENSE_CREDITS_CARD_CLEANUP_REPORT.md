# PRE-BETA — License Credits Card Cleanup Report

## 1. Branch / base / final SHA

| Item | Value |
| --- | --- |
| Branch | `KIRCH-PRE-BETA-LICENSE-CREDITS-CARD-CLEANUP` |
| Base | `KIRCH-PRE-BETA-LICENSE-CREDITS-PAGE-POLISH` @ `b2483c7abcfa36cd259dfadb0009fb9e2d93be42` |
| Final SHA | `1a4964acc556113b47c8e5b6d898b81b6a295e6a` |
| Protected refs | Untouched |

## 2. Manual QA issue

Kirch visual QA: right panel worked functionally but felt like admin metadata (Publisher / Product / Build Type / Products of Kirjane Labs stacked boxes). Needed an obvious clean **Credits** card.

## 3. Files changed

| File | Change |
| --- | --- |
| `src/Salryn.App.Web/src/components/ProductCreditsCard.tsx` | Simplified credits-first layout |
| `tests/Salryn.Tests/PreBetaLicenseCreditsPagePolishChecks.cs` | Guards for cleanup (no metadata boxes) |
| `00-HANDOFF/PRE_BETA_LICENSE_CREDITS_CARD_CLEANUP_REPORT.md` | This report |

## 4. Removed metadata clutter

Removed large visible blocks:

- Publisher
- Product
- Build Type (as a card)
- Products of Kirjane Labs

## 5. Credits card final layout

1. Header: **SALRYN** · by Kirjane Labs · Licensing & Credits · “A Kirjane Labs product”  
2. **Credits** org chart (main body)  
3. Support: salryn.info@gmail.com  
4. Footer: © 2026 Kirjane Labs · tiny muted “Repository-backed beta build”

## 6. Org chart correctness

```
Kirjane Labs
├─ Business Lead — Paul Gamayot
│  └─ Business Development — James Karl Tara
├─ Technology Lead — Kirch Ivan Balite
│  └─ Dev Ops Lead — Osiris Kedigadash Palac
└─ Marketing Lead — Jesse Edwin Culas
```

## 7. Licensing behavior unchanged

Left Host/Terminal licensing cards untouched (machine code, import, active display, support details collapsed).

## 8. RMO status

Remains hidden/internal; no BIR/eSales/cloud/installer claims.

## 9. Tests / gates

| Gate | Result |
| --- | --- |
| pre-beta-license-credits-page-polish | PASS |
| lic-beta / pre-i1 host+terminal | PASS |
| web-a3 / web-a4 | PASS |
| full CoreSmoke | PASS |
| web build | PASS |

## 10. Manual visual check

Source-level cleanup verified. Optional live browser recheck of `/license` and `/terminal-activation` recommended for pixel confirmation.

## 11. Remaining risks

Subjective polish; optional real-browser glance.

## 12. Final decision

**`LICENSE_CREDITS_CARD_CLEANUP_PASSED`**
