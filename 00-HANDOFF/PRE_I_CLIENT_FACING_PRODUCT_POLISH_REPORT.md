# PRE-I Client-Facing Product Polish Report

## 1. Branch, base, and final SHA

- Branch: `KIRCH-PRE-I-CLIENT-FACING-PRODUCT-POLISH`
- Accepted base branch: `KIRCH-PRE-I-ACTION-NOTIFICATION-SYSTEM`
- Accepted base SHA: `0106ee40a2b6d8cd812703612e8039807905dc48`
- Final implementation SHA: pending final commit after gate completion.
- Promotion: not performed.

## 2. Credits and team truth

The Host license surface and shared activation credits now identify Salryn as a Kirjane Labs product, use the title **Licensing & Credits**, show the five supplied team members and exact roles, and carry the 2026 rights line. The former repository/build wording was removed from client-facing credits.

## 3. Terms & Conditions

The Host license flow now presents a readable 13-section **Terms & Conditions** contract covering product use, local licensing, device binding, data ownership, backup responsibility, hardware limits, receipts/compliance, LAN terminals, operator responsibility, support, beta limitations, rights reservation, and acceptance. Initial install and replacement remain terms-gated.

## 4. Current-beta and forbidden-claim boundaries

- BIR accreditation — Current beta status: Not included. Target: future compliance lane.
- eSales transmission — Current beta status: Not included. Target: future compliance lane.
- ESC/POS, cutter, cash drawer, and dedicated thermal control — Current beta status: Not included.
- Windows print queue internal receipts — Available only when configured and verified.
- Hardware bundle and hardware warranty — Not included; client-supplied hardware unless separately agreed.
- Cloud activation — Current beta status: Not required; signed local licenses are used.

No accreditation, eSales production, hardware support, printer support, cloud activation, or bundled-hardware readiness claim was added.

## 5. Product Catalog and Stock Movements clarity

Product Catalog now states that it creates and manages product identity. Stock Movements now states that it changes quantity through a ledger entry. Both surfaces carry the direct rule: **Product Catalog defines the product. Stock Movements changes the quantity.** Existing stock-in and adjustment write paths were preserved.

## 6. Barcode and SKU guidance

The product editor explains that the barcode belongs to the sellable item or package. Separately sold 500 ml and 1.5 L sizes, or a separately sold 6-pack, require separate product records. Items without a printed barcode can be found with their internal SKU/code. No multi-pack conversion was claimed or introduced; packaging/variant support remains a future lane.

## 7. Product profile layout

The selected-product panel has a larger desktop allocation. Name, status, current stock, selling price, barcode, SKU/internal code, and reorder level remain primary. Category, brand, unit, stock-tracking state, supplier, cost, wholesale, timestamps, description, and notes are placed under **More details**. Existing edit, stock, movement, and deactivate actions remain visible.

## 8. Audit review and export decision

The Audit page now positions the trail as a review and investigation surface without claiming complete coverage.

| Requested category | Logged now | Missing | Recommended next |
|---|---|---|---|
| Login/logout/session | First-run admin creation is logged | Normal login, logout, and session lifecycle events | Append actor/session audit events at authentication/session boundaries |
| Sale completed | Remote POS checkout submit is logged best-effort | Host sale completion audit event | Append a durable post-commit audit record with sale and receipt identity |
| Sale voided / receipt reprint | Separate official lifecycle records exist outside `audit_logs` | Operator Audit-page events | Bridge completed lifecycle actions into the operator audit trail |
| Product created/updated/disabled | Product deactivation is logged | Product creation and update | Add transactional create/update audit events with before/after identity fields |
| Stock-in / adjustment | Quantity movements exist in the inventory ledger | Operator Audit-page events | Append audit references to committed movement IDs and reasons |
| Users and roles | User create/update/password-reset/deactivate/reactivate are logged | Role and permission changes | Extend the same transactional user audit pattern to role/permission writes |
| Terminal lifecycle | Remote read attempts and remote checkout are logged | Pair/disable/enable/remove/session-clear | Add Host actor, terminal ID, old/new status, and reason at terminal command completion |
| Backup create/restore | Backup logs exist separately | Operator Audit-page events | Append audit records after verified create/restore completion |
| License install/replace | License state exists outside the audit writer | Install and replacement events | Append signed-license identity metadata without storing license secrets |
| Settings changed | First-run setup is logged | Subsequent settings changes | Add old/new safe settings snapshots at committed settings writes |

The read endpoint is paged and has no complete export contract. A browser-only export of the visible page would be misleading, so no partial CSV button was added. **Audit CSV export remains required before beta seal.** Recommended next: add a bounded server-side filtered CSV endpoint with the current filters, stable ordering, authorization, date/time/user/action/entity/reason fields, safe filename/encoding, and success/failure notifications.

## 9. Terminal Host IP and link clarity

Terminal Management uses the runtime-detected LAN address list, marks the first detected address as recommended, provides a chooser when multiple addresses exist, and gives a support fallback when none are detected. The normal connection card says **Use this terminal link on other devices:** before the generated link. Existing advanced troubleshooting remains collapsed.

## 10. Notifications

Existing action-notification wiring remains in place for licensing, product writes, stock movements, LAN start/stop, terminal-link copy, terminal lifecycle, checkout/receipts, and backup/restore. This lane did not add a fake export notification because it did not add export behavior.

## 11. Files changed

- `src/Salryn.App.Web/src/components/ProductCreditsCard.tsx`
- `src/Salryn.App.Web/src/pages/AuditPage.tsx`
- `src/Salryn.App.Web/src/pages/HostLanControlPage.tsx`
- `src/Salryn.App.Web/src/pages/InventoryPage.tsx`
- `src/Salryn.App.Web/src/pages/LicensePage.tsx`
- `src/Salryn.App.Web/src/pages/ProductsPage.tsx`
- `tests/Salryn.Tests/PreIClientFacingProductPolishChecks.cs`
- `tools/Salryn.Tools.CoreSmoke/Program.cs`
- Nine PRE-I/PRE-BETA source guards updated only where prior wording contracts were superseded by this lane's explicit credits, terms, and claims language.
- `tests/Salryn.Tests/WebA3ShellDesignSystemChecks.cs` and `WebA4NativeHostVisualFidelityChecks.cs` updated to forbid positive ESC/POS claims while allowing the required explicit not-included boundary.
- `tests/Salryn.Tests/PosManual1R3FrontendSafetyUxRepairChecks.cs` updated to guard the new catalog/stock purpose copy.
- `00-HANDOFF/PRE_I_CLIENT_FACING_PRODUCT_POLISH_REPORT.md`

## 12. Automated gates

| Gate | Result |
|---|---|
| `dotnet build Salryn.sln` | Passed; 0 errors, 6 existing warnings |
| `pre-i-client-facing-product-polish` | Passed |
| `pre-i-action-notification-system` | Passed |
| `pre-i-lan-setup-ux-clarity` | Passed |
| `pre-i-license-page-minimalist-rebuild` | Passed |
| `pre-i-code-proven-qa-hardening` | Passed |
| Full CoreSmoke | Passed |
| Web production build | Passed; known non-failing Vite chunk-size advisory observed |
| `git diff --check` | Passed before gate run |

Automated source checks are not browser proof and do not verify receipt hardware, printer queues, BIR accreditation, eSales, LAN reachability from another device, or visual layout on Kirch's target display.

Full CoreSmoke initially encountered a stale external LicensingOps generated-license fixture whose signature did not match the POS public key. The optional sibling `generated/licenses` directory was temporarily renamed for the candidate regression run and restored in a `finally` block. No LicensingOps file was deleted or modified. This remains a separate LicensingOps key/fixture parity issue, not proof that the generated fixture is valid.

## 13. Manual browser QA still required

1. Verify Licensing & Credits and all team cards at desktop and mobile widths.
2. Read all 13 Terms & Conditions sections and verify initial/replacement acceptance gates.
3. Verify catalog-versus-stock guidance, barcode examples, and stock lookup labels.
4. Verify the selected-product profile has no confusing tiny nested scrollbar at the normal desktop viewport and that actions remain easy to reach.
5. Verify Audit positioning and filters; confirm no misleading CSV action is shown.
6. Verify recommended/detected Host addresses, multiple-address selection, terminal link copy, start/stop notifications, and the no-address fallback.
7. Re-run the accepted Action Notification System manual browser checklist.

## 14. Risks and remaining boundaries

- Audit coverage is incomplete for several requested categories, as recorded above.
- Complete filtered audit CSV export is still required before beta seal.
- Product SKU and stock-tracking behavior were not expanded because the current product write API does not accept those fields; existing safe contracts were preserved.
- LAN address detection and link presentation still require real multi-device network QA.
- Browser layout and notification stacking remain manual proof.

## 15. Decision

`PRE_I_CLIENT_FACING_PRODUCT_POLISH_REPAIRED_AND_PASSED`

All required automated gates passed. Kirch manual browser QA remains required. No promotion is authorized or performed by this report.
