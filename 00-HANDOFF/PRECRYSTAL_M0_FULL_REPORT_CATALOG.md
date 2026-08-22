# PRE-CRYSTAL M0 Full Report Catalog

This catalog is an internal Salryn production-reporting catalog. It is not a BIR approval claim and does not imply Crystal runtime availability.

## P0 — required for release/application readiness

| Kind | Title | Boundary | Dataset status | Renderer status |
|---|---|---|---|---|
| `traditional-x-reading` | Traditional X Reading | M5 canonical projection | Available | Crystal unavailable |
| `traditional-z-reading` | Traditional Z Reading dataset | M5 canonical projection, no finalize side effect | Available | Crystal unavailable |
| `ej-export` | EJ examiner export dataset | M5 canonical projection | Available | Crystal unavailable |
| `internal-esales-draft` | Internal eSales draft dataset | M5 canonical projection, schema unconfirmed, transmission OFF | Available | Crystal unavailable |
| `official-invoice-presentation` | Official invoice presentation dataset | Existing official output source boundary; no Crystal template yet | Unavailable placeholder | Crystal unavailable |
| `reprint-adjustment-presentation` | Reprint / adjustment presentation dataset | Existing lifecycle records; presentation path not locked | Unavailable placeholder | Crystal unavailable |

## P1 — core operations

| Kind | Title | Boundary | Dataset status | Renderer status |
|---|---|---|---|---|
| `sales-refund-void-summary` | Sales, refund, void summary | M5 canonical projection | Available | Crystal unavailable |
| `tax-bucket-summary` | VAT / non-VAT tax bucket summary | M5 canonical projection | Available | Crystal unavailable |
| `cashier-tender-summary` | Cashier / tender summary | Source boundary unsafe for M0; no direct DB query | Unavailable placeholder | Crystal unavailable |
| `audit-operator-activity` | Audit / operator activity | Audit projection boundary not locked for report spine | Unavailable placeholder | Crystal unavailable |

## P2 — management and analytics

| Kind | Title | Boundary | Dataset status | Renderer status |
|---|---|---|---|---|
| `sales-by-product-category` | Sales by product/category | Needs safe sales analytics projection | Unavailable placeholder | Crystal unavailable |
| `inventory-on-hand` | Inventory on hand | Needs safe inventory report projection | Unavailable placeholder | Crystal unavailable |
| `stock-movement` | Stock movement | Needs safe stock ledger projection | Unavailable placeholder | Crystal unavailable |
| `low-stock-reorder` | Low stock / reorder | Needs safe reorder projection | Unavailable placeholder | Crystal unavailable |
| `backup-restore-evidence` | Backup / restore evidence | Needs evidence projection boundary | Unavailable placeholder | Crystal unavailable |

## Permission families

- Official/reporting outputs: `Reports.View`, `OfficialReading.View`, `OfficialEvidence.Export` depending on report family.
- Inventory reports: `Inventory.View` once source boundary is safe.
- Audit/evidence reports: `Audit.View` or `OfficialEvidence.Export` depending on report family.

## Crystal boundary

Every catalog item may be report-dataset ready before Crystal is available. Crystal rendering remains unavailable until Sol verifies runtime support, licensing, bitness, installation, and actual render/export behavior on a correctly licensed Windows machine.
