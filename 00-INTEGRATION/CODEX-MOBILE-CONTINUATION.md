# Mobile authoritative-source recovery plan (read-only reconnaissance)

Source pins: Yza `4241ea1ec0a8cebf552d93f25a7ac134d7d4d6f3`; inspected candidate
`ae169420`; Android `1f34b694`. No builds/tests ran for this plan; not acceptance.

All 187 original Yza Infrastructure/Data .cs files already exist in candidate
(208 Data sources total); all original Core sources remain. Preserve current
I1 identity, I2 v41 lineage and I3 financial changes. Do not copy an old Data tree.

Integrator-only shared assembly changes on explicit authorized resume:

1. Recover original `src/Salryn.Persistence.Sqlite/Salryn.Persistence.Sqlite.csproj`
   and `Properties/AssemblyInfo.cs` from the read-only Yza clone. It links the
   authoritative `Infrastructure/Data/**/*.cs` and `Paths/DbPaths.cs` into one
   mobile-safe assembly; Infrastructure references it and excludes those sources.
   Also include/exclude current `Paths/SalrynProductIdentity.cs` with DbPaths to
   avoid a cycle. Retain PDF/Pkcs security fixes and existing source namespaces.
2. Recover original `src/Salryn.Mobile.Runtime/MobileProductionComposition.cs`;
   reference Persistence from Mobile.Runtime. Its C1–C11 service dependencies
   already exist. Add validated `DbPaths.CreateAtRoot` retaining Cafe identity and
   filename. Keep canonical current factory/catalog; adapt old readiness rejection
   of v23 to accepted Cafe41, include v40/v41/trust/immutable schema requirements,
   inspect read-only after lineage admission, reject P0/mobile_* production tables.
3. Replace WIP `CafeMobileDatabase`/`MobileCashierEngine` production ledger with
   adapters over canonical composition; preserve its Git history. MainActivity
   uses app-private Cafe root and shared filename. Do not silently adopt synthetic
   WIP databases. Complete independent device/operator trust, lease/RBAC and UI.
4. Add projects to solution via sole integrator. First recover original complete
   tools `Salryn.Tools.MobileProductionProof`, `MenuCatalogProof`, `CafePrintProof`,
   `CafeReportingProof`; adapt filename/v23/catalog assumptions while preserving
   substantive financial assertions. Original `CreateMigrationPlan()` callers must
   use `CafeMigrationCatalog`; only historical fixtures use explicit legacy plans.
5. Remaining IngredientRecipe/CafeInventory/CafeOrder/CafeFinancial/CafeCash/
   CafeDelivery/CafeBooking/CafeAttendanceIncident proofs require omitted original
   `src/Salryn.Sync.Central.Sqlite` semantic sale/Cafe processors and bootstrap
   readers. Reconcile with current Server; do not simply retarget project refs.
   CafeFinancialProof references CafeBookingProof. Then recover SyncFoundation,
   SaleCommittedSync, ChangeFeedPull and BootstrapProjection F1B–E proofs.

Android security/mobile dependency qualification remains required. Do not remove
SQLitePCLRaw3.0.3 or Pkcs pins simply to obtain packaging success. Restored services
are implementation reuse, not proof that native authorization/pairing executes.
