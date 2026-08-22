# PRE-I Product SKU / Barcode Contract Report

## 1. Branch, base, and implementation SHA

- Branch: `KIRCH-PRE-I-PRODUCT-SKU-BARCODE-CONTRACT`
- Accepted base branch: `KIRCH-PRE-I-CANONICAL-LICENSE-ACTIVATION-PROOF`
- Accepted base SHA: `7e74fbcdac41dbc75a77a3897f1beab2519343b5`
- Implementation SHA: `c442af9061e8ae33834c1147fb36550e62143d16`
- The report closure commit is documentation-only; remote final SHA is recorded in the final handoff.
- Promotion: not performed.

## 2. Barcode policy

One barcode identifies one sellable item or package. Separately sold package sizes and bundles are separate product records: a 500 ml item, a 1.5 L item, and a separately sold 6-pack each have their own identity and barcode when one exists. Product Catalog defines identity; Stock Movements records quantity changes.

## 3. Duplicate barcode enforcement and proof

The existing filtered database unique index remains the race-safe enforcement boundary: `products(barcode)` is unique where `barcode IS NOT NULL AND is_active = 1`. Product create and update also perform an operator-friendly active-product precheck. Both the service precheck and repository constraint mapping now return: `Barcode is already assigned to another active product.`

The focused runtime gate proves duplicate active barcode rejection on create and update. The selected policy allows reuse only after the former product is inactive; the gate deactivates the original owner, creates a replacement with that barcode, and proves exact lookup returns the sole active replacement.

## 4. Empty barcode behavior

Normal Product Catalog create and update accept a blank or whitespace-only barcode and persist it as `NULL`. The editor labels Barcode as optional and explains when it may be blank. Nonblank values still pass the existing barcode value-object validation; invalid input returns `Enter a valid barcode or leave it blank if this product does not use one.` Checkout Fast Add remains barcode-required because it is entered only from an explicit unknown-barcode scan.

## 5. SKU / internal code actual support

SKU assignment is deferred in this beta. The database and read models retain nullable legacy SKU data, and general repository search can encounter it, but the product write contract cannot create or edit SKU and there is no enforced SKU uniqueness contract. Product Catalog, Stock Movements, and Checkout therefore do not advertise SKU lookup as a supported workflow. Existing SKU data is shown only under **More details** as read-only, with explicit deferred copy.

## 6. Product Catalog copy

Product Catalog states: `Product Catalog defines the product. Stock Movements changes the quantity.` The editor states the one-barcode/one-sellable-package policy, gives the 500 ml, 1.5 L, and 6-pack examples, explains optional blank barcodes, and does not claim working SKU assignment.

## 7. Stock Movements copy

Stock Movements states: `Scan or enter the product barcode to select the product, then record the quantity movement.` Its no-result state says: `No active product was found for that barcode.` The prior barcode/SKU working-language claim was removed.

## 8. Product profile layout

The selected-product profile now follows the page's natural scroll instead of a forced viewport-height panel with its own small scrollbar. Name, active/tracked status, current stock, selling price, barcode, and reorder level remain primary. Category, brand, unit, read-only legacy SKU, tracking state, supplier, cost, wholesale price, timestamps, description, and notes remain under **More details**. Edit, Stock-In, adjustment, movement, and deactivate actions are unchanged.

## 9. Checkout compatibility

Checkout keeps exact active-barcode submission before product-name search and Fast Add. Exact lookup is deterministic because the read repository uses equality plus `is_active = 1`, backed by the filtered unique index. Checkout help now states that barcode identifies the sellable item or package, and unsupported SKU lookup wording was removed. Existing sale behavior was not changed.

## 10. Stock movement compatibility

Stock-In and Stock Adjustment continue to use the active product search path. An exact barcode can identify at most one active product because of the database constraint. Quantity is entered separately and written through the existing inventory ledger service; no product identity write was added to Stock Movements.

## 11. Non-goals and future boundaries

No product variants, multipack conversion, barcode aliases, GS1 validation, weighted/price-embedded barcode parsing, unit conversion, supplier purchasing, bulk import, automatic package splitting, parent/child SKUs, scanner hardware API, printer behavior, audit behavior, licensing behavior, or LAN behavior was added. Client copy describes multipack conversion, variant automation, and SKU creation/editing as not included or deferred.

## 12. Files changed

- `src/Salryn.Api.Local/Contracts/ProductManagementContracts.cs`
- `src/Salryn.Api.Local/Services/FastAddProductApiService.cs`
- `src/Salryn.Api.Local/Services/ProductManagementApiService.cs`
- `src/Salryn.Infrastructure/Data/Repositories/Products/SqliteProductWriteRepository.cs`
- `src/Salryn.App.Web/src/pages/ProductsPage.tsx`
- `src/Salryn.App.Web/src/pages/InventoryPage.tsx`
- `src/Salryn.App.Web/src/pages/CheckoutPage.tsx`
- `tests/Salryn.Tests/PreIProductSkuBarcodeContractChecks.cs`
- `tests/Salryn.Tests/PosM5ProductWriteManagementChecks.cs`
- `tests/Salryn.Tests/PosManual1R3FrontendSafetyUxRepairChecks.cs`
- `tests/Salryn.Tests/PreIClientFacingProductPolishChecks.cs`
- `tools/Salryn.Tools.CoreSmoke/Program.cs`
- `00-HANDOFF/PRE_I_PRODUCT_SKU_BARCODE_CONTRACT_REPORT.md`

## 13. Gates run

- `dotnet build Salryn.sln -c Release` — passed with existing warnings only
- `pre-i-product-sku-barcode-contract` — passed, including runtime create/update/blank/reuse/exact-lookup proof
- `pre-i-canonical-license-activation-proof` — passed using a fresh temp LicensingOps manifest
- `pre-i-license-key-rotation-canonical-beta` — passed
- `pre-i-audit-csv-export` — passed
- `pre-i-audit-trail-coverage` — passed
- `pre-i-client-facing-product-polish` — passed
- `pre-i-action-notification-system` — passed
- `pos-m3-barcode-scanner-cashier-flow` — passed
- `pos-m4-fast-add-unknown-barcode-inventory` — passed
- `pos-m5-product-write-management` — passed
- `pos-m6-stock-in-stock-adjustment` — passed
- Full CoreSmoke — passed after one stale stock-copy verifier was repaired; the existing local license was reversibly isolated and restored
- `npm --prefix src/Salryn.App.Web run build` — passed with the unchanged Vite chunk-size advisory
- `git diff --check` — passed

## 14. Manual QA checklist

1. Product Catalog shows the identity and package guidance.
2. Stock Movements shows the barcode/quantity guidance.
3. Duplicate active barcode create rejects with the friendly message.
4. Updating to another active product's barcode rejects with the friendly message.
5. A product without a barcode can be created and explains the blank state.
6. Checkout scan finds the intended active product.
7. Stock Movement barcode lookup finds the intended active product.
8. The selected-product profile no longer feels trapped in a small nested scrollbar.
9. No variants, multipack, GS1, aliases, or working SKU claims appear.

## 15. Remaining risks

- Browser rendering and operator feel remain manual; source/build gates cannot prove viewport comfort.
- SKU remains a partial legacy read/search field below the supported client contract until a dedicated write, uniqueness, and lookup lane is implemented.
- Inactive-barcode reuse is intentional; historical sales retain snapshots, but operators should distinguish replacement records clearly by name.
- The known production bundle chunk-size advisory remains unchanged and non-failing.

## 16. Final decision

`PRE_I_PRODUCT_SKU_BARCODE_CONTRACT_REPAIRED_AND_PASSED`

No branch promotion was performed.
