# FRONTEND WIRING + UX M2 — Implementation Report

## Branch / base

Branch: `KIRCH-FRONTEND-WIRING-UX-M2`

Base branch: `KIRCH-FRONTEND-CHECKOUT-UX-M1`

Base SHA: `6ce6317f7e132d4b44be2ff8d82c23db3d76b878`

Base caveat: `KIRCH-FRONTEND-CHECKOUT-UX-M1` remains a stacked candidate pending independent Sol/Kirch reverify. This M2 branch is stacked on that candidate.

Protected refs supplied by Kirch and not intentionally touched:

- `KIRCH-SANDBOX = 1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`
- `KIRCH-INSTALLER-M1 = 6b9c46676390b8a86a989eca600cb02e406ce865`

Final SHA note: this report cannot contain the SHA of the commit that contains itself. Use the final response or branch head after this report commit for the exact final SHA.

## Scope

Frontend wiring/UX only. No backend rewrites, no database changes, no seed changes, no installer changes, no licensing changes, no RMO/eSales/reporting backend changes, and no protected ref changes.

This lane does not claim independent verification. Any local gates remain Freehand self-check only if run locally outside this connector session. This connector session did not run local commands.

## Files changed

- `src/Salryn.App.Web/src/pages/ProductsPage.tsx`
- `src/Salryn.App.Web/src/pages/InventoryPage.tsx`
- `src/Salryn.App.Web/src/pages/AuditPage.tsx`
- `00-HANDOFF/FRONTEND_WIRING_UX_M2_MAP.md`
- `00-HANDOFF/FRONTEND_WIRING_UX_M2_REPORT.md`

## Page-by-page wiring map summary

Full matrix: `00-HANDOFF/FRONTEND_WIRING_UX_M2_MAP.md`.

Summary:

| Page | Current wiring | M2 action |
| --- | --- | --- |
| Dashboard | Real `GET /api/dashboard/summary`; authenticated-only. | Documented and left intact. No fake metrics added. |
| POS Checkout | M1 candidate already wires session, shift, products, checkout. | Preserved as stacked base; not rewritten here. |
| Products | Real product list/detail/category/brand GET wrappers. | Improved read-only product UX, stock state, details, no-image/read-only boundary. |
| Inventory | Real low-stock and stock movement wrappers. | Added movement type/from/to filters, low/out distinction, reference column, read-only boundary. |
| Sales History | Real sales list/detail wrappers plus existing lifecycle action wrapper. | Documented; no additional lifecycle invention. |
| Users | Real list/roles/detail/create wrappers. | Documented; edit/deactivate/reset left unimplemented. |
| Audit | Real audit/error logs wrappers. | Added from/to filters, clear filters, entity-id display, read-only boundary. |
| Host LAN Control | Existing LAN hook/control contracts. | Documented; stale-bind and explicit activation safety preserved. |
| Remote POS / Read-only | Existing remote-client hooks. | Documented; no unsafe LAN exposure added. |
| Official Setup | Existing official setup/dry-run/internal projection wrappers. | Documented; activation/legal boundaries preserved. |
| Traditional Reporting | Existing X/Z/EJ/eSales draft wrappers. | Documented; no eSales live posting or approval claim. |
| Production Reports | Existing catalog/status/dataset/native-PDF wrappers. | Documented; Crystal/printing/cloud/production claim remain parked. |

## Hidden / parked features found

Kept parked:

- Official Output Text File nav item: disabled because the React shell has no approved direct API contract.
- Settings nav item: disabled because no approved settings API contract is wired for this shell lane.
- Backup nav item: disabled because no approved backup API contract is wired for this shell lane.
- Product create/edit/deactivate/import: not wired in React because this lane only audited product GET wrappers.
- Inventory adjustment: not wired because this lane only audited read-side low-stock and movement wrappers.
- User edit/deactivate/reactivate/reset password: not wired because current wrapper set exposes list/roles/detail/create only.
- Crystal Reports, printer integration, arbitrary output paths, cloud upload, eSales live submission, BIR/legal approval: parked by existing reports/official boundaries.

## Features wired / improved in this lane

### Products

- Kept `fetchProducts`, `fetchProductDetail`, `fetchProductCategories`, and `fetchProductBrands` as the only data sources.
- Added clearer read-only boundary message.
- Added text-first product identity: name, SKU, barcode, unit, category, brand.
- Added stock state classification: inactive, untracked, out of stock, low stock, in stock.
- Added current-page low/out/untracked summary.
- Expanded detail panel with cost, wholesale, stock, reorder, active/tracked state, notes, and no image/media UI.

### Inventory

- Kept `fetchLowStockProducts` and `fetchStockMovements` as the only data sources.
- Added read-only inventory boundary message.
- Added low/out distinction in stock attention.
- Added movement type/from/to filters because the existing wrapper already supports those params.
- Added movement reference display using existing movement fields.
- Left stock mutation absent.

### Audit

- Kept `fetchAuditLogs` and `fetchErrorLogs` as the only data sources.
- Added read-only audit boundary message.
- Added from/to filters because the existing wrappers already support those params.
- Added clear filters action.
- Added entity id display when present.
- Left export/delete/edit absent.

## Features left disabled and why

| Feature | Decision |
| --- | --- |
| Official Output Text File nav | Left disabled. No approved direct React API contract established in this lane. |
| Settings nav | Left disabled. No approved React settings API contract established in this lane. |
| Backup nav | Left disabled. No approved React backup API contract established in this lane. |
| Product write actions | Left disabled/parked. Product page uses read-side wrappers only. |
| Inventory adjustments | Left disabled/absent. No mutation wrapper audited for this lane. |
| User edit/deactivate/reset | Left disabled/absent. Current wrapper set supports create only. |
| Receipt printing | Not added. No approved print contract used in this lane. |
| eSales live posting | Not added. Existing surfaces remain draft/internal where applicable. |
| BIR/legal approval | Not claimed. |
| Product images/media | Not added. Hard no-image rule preserved. |

## No-product-image confirmation

No product images, thumbnails, image upload, image URLs, media fields, placeholder product photos, seeded images, database image columns, image cards, or ecommerce-style product tiles were added.

Products and Checkout remain text/data-first using existing fields only: `id`, `sku`, `barcode`, `name`, `unitName`, `categoryName`, `brandName`, `sellingPrice`, `costPrice`, `stockOnHand`, `reorderLevel`, `trackStock`, `isActive`.

## Backend changes

None.

No backend endpoints, services, migrations, data seeders, reporting backend, RMO/eSales backend, installer, launcher, or licensing files were changed.

## Known limitations

- This connector session cannot run `rg`, `dotnet`, `npm`, app launch, or manual WebView proof.
- Audit command output files such as `frontend-wiring-endpoint-scan.txt` were not generated by this connector session because the required `rg` commands were not run locally.
- Checkout M1 base remains pending independent reverify.
- Products remains read-only in React.
- Inventory remains read-only in React.
- Sales lifecycle actions already exist but remain sensitive; do not run destructive actions without explicit checklist authority.
- Traditional Reporting and Production Reports remain engineering/controlled export surfaces, not legal/production activation.

## Gates

No local gates were run by this ChatGPT connector session.

Required local Freehand self-check gates:

```powershell
cd C:\Users\kirch\Salryn
git fetch origin
git checkout KIRCH-FRONTEND-WIRING-UX-M2
git rev-parse HEAD

dotnet restore Salryn.sln
dotnet build Salryn.sln

dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only manual-m1-bootstrap-auth-repair
dotnet run --project tools/Salryn.Tools.CoreSmoke -- --only v2-lan-bind-truth
dotnet run --project tools/Salryn.Tools.CoreSmoke

npm --prefix src/Salryn.App.Web run build

git diff --check
git status
```

## Manual proof instructions

Optional manual self-check after automated gates:

1. Launch local API app using the accepted development profile.
2. Login with the accepted dev admin account.
3. Click through: Dashboard, Checkout, Products, Inventory, Sales History, Users, Audit, Host LAN Control, Official Setup, Traditional Reporting, Production Reports.
4. Confirm each page loads without a white screen.
5. Confirm Products filters/search/detail work with real data and no images.
6. Confirm Inventory low stock and movement filters load real data only.
7. Confirm Audit/error tabs and filters load real data only.
8. Confirm disabled nav items remain disabled with reasons.
9. Confirm no product images appear anywhere in Checkout or Products.
10. Stop app cleanly.

Do not run destructive official lifecycle actions, Z finalization, confirmed seed/reset, BIR/eSales submission, installer proof, or production activation in this lane.

## Non-claims

This lane does not claim:

- Sol verification
- Kirch verification
- GREEN status
- local restore/build pass
- CoreSmoke pass
- npm build pass
- app launch proof
- manual click-through proof
- beta readiness
- production readiness
- BIR/eSales/legal approval
- installer proof
- branch promotion

## Status

Connector-authored status: `CANDIDATE_PENDING_VERIFICATION`.

If Freehand/Sol later runs gates locally, status may move to `CANDIDATE_SELF_CHECKED_ONLY` for Freehand self-check or an independently accepted status per Kirch/Sol proof. Do not mark this branch GREEN from this connector-only session.
