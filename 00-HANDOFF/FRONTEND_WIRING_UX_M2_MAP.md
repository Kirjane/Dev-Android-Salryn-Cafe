# FRONTEND WIRING + UX M2 — Wiring Map

Branch: `KIRCH-FRONTEND-WIRING-UX-M2`

Base branch: `KIRCH-FRONTEND-CHECKOUT-UX-M1`

Base SHA: `6ce6317f7e132d4b44be2ff8d82c23db3d76b878`

Status of this map: connector-authored audit. Local `rg` commands were not run in this ChatGPT connector session. This map is based on repository files fetched through the GitHub connector and must be treated as candidate evidence until Sol/Kirch verifies locally.

## Global shell / navigation

| Area | Repo truth |
| --- | --- |
| App shell | `App.tsx` renders authenticated local console pages and separate remote-client entry when browser location is remote-client. |
| Navigation | Enabled: Dashboard, POS Checkout, Products, Inventory, Sales History, Users, Audit, Host LAN Control, Official Setup, Traditional Reporting, Production Reports. |
| Disabled nav | Official Output Text File, Settings, Backup stay disabled because React shell has no approved API contracts for those surfaces. |
| Permission gate | Pages are blocked before render by `canAccessPage`. Dashboard is authenticated-only. Other pages use explicit permissions. |
| No product images | Product API and Checkout/Product views use text/data fields only. No image/media wiring was found or added in this lane. |

## Page-by-page matrix

### Dashboard

| Item | Finding |
| --- | --- |
| Page path | `src/Salryn.App.Web/src/pages/DashboardPage.tsx` |
| API wrappers | `fetchDashboardSummary()` |
| Backend endpoints | `GET /api/dashboard/summary` |
| Permission | Authenticated session only |
| Wired features | Real summary metrics, backend status, warnings, LAN management shortcut when allowed. |
| Not wired | No fake charts, no dashboard-only revenue invention. |
| Hidden/parked | None in nav; dashboard does not unlock disabled surfaces. |
| UX defects | Already serviceable; retained as real-data summary. |
| Safe action | Leave wired metrics; keep LAN warning/host ownership explanation. |
| Manual proof needed | Load dashboard after login; refresh summary; confirm no fake cards. |

### POS Checkout

| Item | Finding |
| --- | --- |
| Page path | `src/Salryn.App.Web/src/pages/CheckoutPage.tsx` |
| API wrappers | `fetchSession`, `fetchHostShift`, `openHostShift`, `closeHostShift`, `fetchProducts`, `submitCheckout` |
| Backend endpoints | `GET /api/session`, `GET /api/shift`, `POST /api/shift/open`, `POST /api/shift/close`, `GET /api/products`, `POST /api/checkout` |
| Permission | `Sales.Create` |
| Wired features | M1 workstation UX: product search, paged text-first product cards, cart, stock cap, cash-only tender, receipt-like result, diagnostics collapsed. |
| Not wired | Discounts, non-cash payments, receipt printing, hardware scanner integration, official posting. |
| Hidden/parked | Not applicable. |
| UX defects | M1 remains pending independent verification; this M2 did not rewrite it. |
| Safe action | Preserve M1 Checkout candidate as stacked base. |
| Manual proof needed | Complete checkout flow with seeded dev DB. |

### Products

| Item | Finding |
| --- | --- |
| Page path | `src/Salryn.App.Web/src/pages/ProductsPage.tsx` |
| API wrappers | `fetchProducts`, `fetchProductDetail`, `fetchProductCategories`, `fetchProductBrands` |
| Backend endpoints | `GET /api/products`, `GET /api/products/{id}`, `GET /api/products/catalog/categories`, `GET /api/products/catalog/brands` |
| Permission | `Products.View` |
| Wired features | Search, category filter, brand filter, active/inactive filter, paged table, product detail. |
| Not wired | Product create/edit/import/deactivate. No React product write API wrapper exists in this lane. |
| Hidden/parked | Product write actions are parked with explicit read-only explanation. |
| UX defects found | Stock state was numeric-only; no clear read-only/product-image boundary. |
| Safe action implemented | Added text-first identity, SKU/barcode, unit/category/brand, price, stock/reorder, active/tracked state, low/out/untracked page summary, and read-only no-image message. |
| Things not to invent | Product write API, product media, placeholder photos. |
| Manual proof needed | Search seeded products; use filters; select product; confirm detail loads and no image UI exists. |

### Inventory

| Item | Finding |
| --- | --- |
| Page path | `src/Salryn.App.Web/src/pages/InventoryPage.tsx` |
| API wrappers | `fetchLowStockProducts`, `fetchStockMovements` |
| Backend endpoints | `GET /api/inventory/low-stock`, `GET /api/inventory/movements` |
| Permission | `Inventory.View` |
| Wired features | Low-stock list, movement history, movement paging. |
| Not wired | Stock adjustment mutation; no approved React adjustment wrapper exists in this lane. |
| Hidden/parked | Adjustment action stays disabled/absent with read-only reason. |
| UX defects found | No movement filters surfaced despite wrapper supporting movement type/from/to; low/out state not strongly separated. |
| Safe action implemented | Added stock attention summary, out/low distinction, movement type/from/to filters, reference column, read-only inventory notice. |
| Things not to invent | Stock mutation, fake adjustment forms, image fields. |
| Manual proof needed | Load inventory; inspect low/out items; filter movement history. |

### Sales History

| Item | Finding |
| --- | --- |
| Page path | `src/Salryn.App.Web/src/pages/SalesHistoryPage.tsx` |
| API wrappers | `fetchSalesHistory`, `fetchSaleDetail`, `postLifecycleAction` |
| Backend endpoints | `GET /api/sales/history`, `GET /api/sales/{id}`, existing official document lifecycle endpoint wrapper |
| Permission | `Sales.ViewHistory` |
| Wired features | Receipt/status filters, paged sales list, sale detail, line items, wired lifecycle actions already present. |
| Not wired | Receipt printing, BIR/eSales production posting claim, official approval claim. |
| Hidden/parked | Lifecycle area is host-only engineering flow; activation OFF messaging must stay. |
| UX defects | Lifecycle controls are visually heavy and require careful manual proof. |
| Safe action | No backend changes. Leave lifecycle warnings and wired actions intact. |
| Things not to invent | Void/refund/reprint beyond existing wrapper, printer integration, official legal claim. |
| Manual proof needed | Select sale, view detail; do not run destructive lifecycle action unless checklist authorizes. |

### Users

| Item | Finding |
| --- | --- |
| Page path | `src/Salryn.App.Web/src/pages/UsersPage.tsx` |
| API wrappers | `fetchUsers`, `fetchUserRoles`, `fetchUserDetail`, `createUser` |
| Backend endpoints | `GET /api/users`, `GET /api/users/roles`, `GET /api/users/{id}`, `POST /api/users` |
| Permission | `Users.Manage` |
| Wired features | User list, roles, detail, create user for allowed roles. |
| Not wired | Edit user, deactivate/reactivate, reset password. |
| Hidden/parked | Mutations beyond create remain absent because no safe wrapper was audited for this lane. |
| UX defects | Create form is serviceable; needs manual proof for validation/errors. |
| Safe action | Leave real create flow; do not invent edit/deactivate/reset. |
| Manual proof needed | Load users, inspect detail, optionally create test user only if allowed by proof plan. |

### Audit

| Item | Finding |
| --- | --- |
| Page path | `src/Salryn.App.Web/src/pages/AuditPage.tsx` |
| API wrappers | `fetchAuditLogs`, `fetchErrorLogs` |
| Backend endpoints | `GET /api/audit/logs`, `GET /api/audit/errors` |
| Permission | `Audit.View` |
| Wired features | Audit/error tabs, search, paging. |
| Not wired | Audit export/delete/edit. |
| Hidden/parked | None. |
| UX defects found | Wrapper supports date range but UI did not expose it. |
| Safe action implemented | Added from/to filters, clear filters, entity id display, read-only boundary notice. |
| Things not to invent | Fake audit records, export, mutation. |
| Manual proof needed | Load audit/errors; search/filter; verify no fake records. |

### Host LAN Control

| Item | Finding |
| --- | --- |
| Page path | `src/Salryn.App.Web/src/pages/HostLanControlPage.tsx` |
| API wrappers/hooks | `useLocalLanControl()` and LAN control utilities |
| Backend endpoints | Existing LAN status/runtime/pairing/terminal control contracts through hook. |
| Permission | Any of `StoreTerminal.View`, `StoreTerminal.Manage`, `StoreDeployment.Manage` |
| Wired features | Runtime status, explicit LAN activation/repair/disable, Remote POS runtime enable/disable, join code rotation, pending request accept/reject, terminal lifecycle controls. |
| Not wired | No default unsafe wildcard bind. |
| Hidden/parked | Troubleshooting collapsed by state; hidden terminal list is local UI state. |
| UX defects | Large but safety-heavy; no change in this lane. |
| Safe action | Preserve stale-bind safety and explicit activation. |
| Manual proof needed | Refresh status; verify unsafe LAN remains opt-in. |

### Remote POS client

| Item | Finding |
| --- | --- |
| Page path | `src/Salryn.App.Web/src/pages/RemotePosClientPage.tsx` |
| API wrappers/hooks | `useRemotePosClient()` |
| Backend endpoints | Existing remote POS pairing/login/cart/checkout contracts through hook. |
| Permission | Remote client path; host grants terminal pairing and operator login. |
| Wired features | Host address confirmation, pairing request, pairing activation, cashier login, shift open, cart, checkout. |
| Not wired | Direct localhost DB access, unsafe LAN exposure. |
| Hidden/parked | Depends on host LAN runtime and pairing. |
| UX defects | State-machine page is diagnostic-heavy but safety preserving. |
| Safe action | No change in this lane. |
| Manual proof needed | Remote client proof only after host LAN checklist authorizes. |

### Remote Read-only client

| Item | Finding |
| --- | --- |
| Page path | `src/Salryn.App.Web/src/pages/RemoteReadOnlyClientPage.tsx` |
| API wrappers/hooks | Existing remote read-only client hook/API. |
| Backend endpoints | Existing read-only bridge contracts. |
| Permission | Remote read-only trust/session requirements. |
| Wired features | Read-only client entry/handshake surface. |
| Not wired | Remote writes. |
| Hidden/parked | Requires host LAN and trust state. |
| UX defects | Not modified in M2. |
| Safe action | Preserve read-only boundary. |
| Manual proof needed | Separate LAN read-only proof. |

### Official Setup

| Item | Finding |
| --- | --- |
| Page path | `src/Salryn.App.Web/src/pages/OfficialSetupPage.tsx` |
| API wrappers | `officialSetup.ts` wrappers for setup/readiness/numbering/integrity/spine/dry-run/internal preview/invoice projection. |
| Backend endpoints | Existing official setup/dry-run/internal output endpoints. |
| Permission | `Settings.Manage` |
| Wired features | Business/machine/receipt/series saves, readiness, numbering, dry-run package, report preview, invoice projection. |
| Not wired | Production activation, legal approval, eSales live submission. |
| Hidden/parked | All official outputs remain internal/test/dry-run with activation OFF messaging. |
| UX defects | Large engineering surface; no backend change in this lane. |
| Safe action | Keep boundaries explicit; no production claim. |
| Manual proof needed | Readiness/profile save only if checklist authorizes. |

### Traditional Reporting

| Item | Finding |
| --- | --- |
| Page path | `src/Salryn.App.Web/src/pages/TraditionalReportingPage.tsx` |
| API wrappers | `fetchTraditionalReportingStatus`, `previewXReading`, `finalizeZReading`, `exportEj`, `exportESalesDraft` |
| Backend endpoints | Existing traditional reporting endpoints. |
| Permission | Any of `Reports.View`, `OfficialReading.View`, `OfficialEvidence.Export` |
| Wired features | Status, range, X preview, Z finalize, EJ export, eSales draft. |
| Not wired | eSales production submission, BIR approval, legal production activation. |
| Hidden/parked | eSales is draft; activation OFF. |
| UX defects | JSON/status preview remains engineering-style. |
| Safe action | Leave legal boundaries. |
| Manual proof needed | Preview-only first; finalize/export only per official checklist. |

### Production Reports

| Item | Finding |
| --- | --- |
| Page path | `src/Salryn.App.Web/src/pages/ProductionReportsPage.tsx` |
| API wrappers | `fetchProductionReportCatalog`, `fetchProductionReportRendererStatus`, `generateProductionReportDataset`, `exportProductionReportNativePdf` |
| Backend endpoints | Existing production-report catalog/status/dataset/native-PDF export endpoints. |
| Permission | Any of `Reports.View`, `OfficialReading.View` |
| Wired features | Catalog, renderer status, dataset generation, native PDF export artifact metadata. |
| Not wired | Crystal activation, caller-selected paths, printer integration, cloud upload, production legal submission. |
| Hidden/parked | Crystal parked; native PDF primary. |
| UX defects | Already boundary-heavy; no change in this lane. |
| Safe action | Keep controlled artifact root and no legal/production claim. |
| Manual proof needed | Generate dataset/export only under reports checklist. |

## Disabled nav items kept disabled

| Nav item | Reason |
| --- | --- |
| Official Output Text File | React shell has no approved API contract for this direct surface. Existing official setup/reporting pages remain the approved internal paths. |
| Settings | No approved settings API contract was established for this React shell lane. |
| Backup | No approved backup API contract was established for this React shell lane. |

## Safe implementation selected for M2

1. Products UX/readability pass using existing product GET wrappers.
2. Inventory filter/readability pass using existing low-stock and movement wrappers.
3. Audit filter/readability pass using existing audit/error wrappers.
4. Reports and other pages documented but not rewritten to avoid backend/legal/activation drift.

## Explicit non-inventions

No product images, no media fields, no fake rows, no fake summary endpoints, no product write APIs, no stock adjustment APIs, no backup/settings API enabling, no production activation, no eSales live posting, no BIR/legal approval claim, no installer changes, and no backend changes.
