# POS-R1 Owner Reports Report

Date: 2026-07-14  
Branch: `KIRCH-POS-R1-OWNER-REPORTS`  
Base branch: `KIRCH-POS-SURFACE1-ROLE-PAGE-LOCK-STORE-NAV`  
Base SHA: `c5b61a9d7a8adf90c474db73f23b86ead7b270f3`

Authoring mode: repo-backed implementation with local automated self-checks. Manual browser proof remains deferred to Kirch.

---

## 1. Purpose

POS-R1 adds read-only Owner Reports / Store Reports for store-owner and manager operational visibility.

These reports are store-operational and are not official or regulatory filings.

---

## 2. Feature implemented

Owner Reports answer:

- how much the store sold today
- how much the store sold this current calendar week
- how much the store sold this current calendar month
- top products by active completed sales
- cashier totals
- category totals, using `Uncategorized` when no product category is available
- low-stock products
- inventory value only when product cost data is complete enough to estimate safely

---

## 3. Date / period rule

Daily report uses the host machine local calendar day.

Weekly report uses the current calendar week: Monday 00:00 through next Monday 00:00 using the host machine local date.

Monthly report uses the current host-machine local calendar month.

---

## 4. Void handling rule

Main sales totals include completed non-voided sales only.

Voided sales are excluded from active sales totals and shown separately through voided count and voided amount fields.

Top products and category totals include completed non-voided sales only.

Cashier totals show active totals and voided count/amount separately.

POS-R1 does not rewrite original sales or receipt snapshots.

---

## 5. Permission rule

Backend endpoint is guarded by `Reports.View`.

Frontend page access is guarded by `Reports.View` through the host page permission map.

Manager and Admin roles can access because seeded roles already include `Reports.View`.

Cashier cannot access Owner Reports because cashier role/page gating excludes `owner-reports` and the cashier role does not carry `Reports.View`.

---

## 6. Read-only guarantees

POS-R1 owner report paths only read from existing tables.

The feature does not call sale write repositories, payment writers, receipt snapshot writers, stock movement writers, product write paths, user writers, settings writers, printer paths, scanner paths, backup paths, installer paths, or RMO/BIR/eSales paths.

The POS-R1 smoke records sales/payment/stock movement counts and product stock before the report read, reads the report, then verifies those values did not change.

---

## 7. Backend changes

Added:

- `src/Salryn.Api.Local/Contracts/OwnerReportsContracts.cs`
- `src/Salryn.Api.Local/Services/OwnerReportsApiService.cs`
- `src/Salryn.Api.Local/LocalOwnerReportsEndpoints.cs`

Updated:

- `src/Salryn.Api.Local/Auth/LocalApiPermissionGuards.cs`
- `src/Salryn.Api.Local/LocalApiApplicationFactory.cs`

Endpoint:

```text
GET /api/owner-reports/summary
```

---

## 8. Frontend changes

Added:

- `src/Salryn.App.Web/src/api/ownerReports.ts`
- `src/Salryn.App.Web/src/pages/OwnerReportsPage.tsx`

Updated:

- `src/Salryn.App.Web/src/navigation.ts`
- `src/Salryn.App.Web/src/security/permissions.ts`
- `src/Salryn.App.Web/src/App.tsx`

The page is labeled `Owner Reports` and presents itself as store operational reporting only.

---

## 9. Automated proof added

Added:

- `tests/Salryn.Tests/PosR1OwnerReportsChecks.cs`

The smoke creates a temporary database, seeds products and completed/voided sales, runs the owner report service, and asserts:

- daily active total matches seeded non-voided sales
- weekly active total matches seeded non-voided sales
- monthly active total matches seeded non-voided sales
- voided sale count and amount are reported separately
- top products exclude voided quantity and amount
- cashier totals exclude voided sales from active totals and show voided count/amount separately
- category totals include category and `Uncategorized` rows
- low stock includes products at or below reorder threshold
- inventory value equals current stock multiplied by cost when cost data is complete
- owner report read does not mutate sales, payments, stock movements, or product stock
- Admin/Manager report permission is available and Cashier is denied
- frontend exposes Owner Reports copy and direct page guard

Updated:

- `tools/Salryn.Tools.CoreSmoke/Program.cs`

Target:

```text
--only pos-r1-owner-reports
```

---

## 10. Gates

Executed locally:

```powershell
dotnet build Salryn.sln
dotnet run --project tools\Salryn.Tools.CoreSmoke
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-r1-owner-reports
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m1-receipt-snapshot-file-receipt
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m2-printer-reprint-failure
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m3-barcode-scanner-cashier-flow
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m4-fast-add-unknown-barcode-inventory
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m5-product-write-management
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m6-stock-in-stock-adjustment
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-m7-void-sale-stock-reversal
dotnet run --project tools\Salryn.Tools.CoreSmoke -- --only pos-surface1-role-page-lock-store-nav
npm --prefix src\Salryn.App.Web run build
git diff --check
git status
```

Results:

- Solution build: PASS (0 errors; existing warnings only).
- Full CoreSmoke: PASS.
- POS-R1 focused CoreSmoke: PASS.
- POS-M1 through POS-M7 and POS-SURFACE1 focused CoreSmoke: PASS.
- Web build: PASS with the existing Vite chunk-size advisory.
- `git diff --check`: PASS.

The first full-smoke run caught a forbidden capability token in the Owner Reports disclaimer. The copy and its UI-copy assertion were narrowed to `Not an official or regulatory filing`; report behavior was unchanged.

---

## 11. Manual proof

Manual runtime/UX proof is deferred to Kirch.

Manual proof should cover owner readability, layout clarity, business wording preference, and browser visual approval.

---

## 12. Known limitations

Inventory value is unavailable when any active stock-tracked product is missing cost data. POS-R1 intentionally does not fake or infer missing cost values.

Category totals use current product category relationships where product IDs are still available. Products without category are grouped as `Uncategorized`.

No PDF export, official output generation, Crystal rendering, printer integration, scanner integration, backup/restore, Store Settings, cloud reports, installer, RMO/BIR/eSales, or branch promotion was implemented.

---

## 13. Protected refs

Expected unchanged:

- `KIRCH-SANDBOX = 1f74cd8bcaca1a5df487aa61677ee9829dd7fedb`
- `KIRCH-INSTALLER-M1 = 6b9c46676390b8a86a989eca600cb02e406ce865`

---

## 14. Status

`CANDIDATE_AUTOMATED_PROVEN_MANUAL_PENDING`

No `GREEN`, `ACCEPTED`, `MANUAL_PROVEN`, `STORE_READY`, `BETA_READY`, `PRODUCTION_READY`, `BIR_READY`, `PHYSICAL_PRINTER_PROVEN`, `PHYSICAL_SCANNER_PROVEN`, or `INSTALLER_PROVEN` claim is made.
