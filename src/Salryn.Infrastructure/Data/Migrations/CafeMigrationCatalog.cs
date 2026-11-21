namespace Salryn.Infrastructure.Data.Migrations;

/// <summary>
/// Canonical Salryn Cafe migration catalog (v1-v41, Issue #4 / ADR-I2).
/// Combines shared v1-v22 + Kernel v23 + shared v24 + Yza Cafe v25-v39 + v40 lineage/Kernel-parity + v41 idempotency/device-trust.
/// </summary>
public static class CafeMigrationCatalog
{
    public const int CurrentCafeSchemaVersion = 43;

    public static IReadOnlyList<IDatabaseMigration> CreateCanonicalCafeMigrations()
    {
        return new IDatabaseMigration[]
        {
            new InitialSchemaMigration(),
            new BirMachineProfileSchemaMigration(),
            new BirOfficialDocumentSequenceSchemaMigration(),
            new BirElectronicJournalSchemaMigration(),
            new BirElectronicJournalSchemaHardeningMigration(),
            new BirZReadingSchemaMigration(),
            new BirElectronicJournalCorrectionEventSchemaMigration(),
            new ProductTaxClassificationSchemaMigration(),
            new DeploymentIdentitySchemaMigration(),
            new TerminalRegistrySchemaMigration(),
            new LanRemoteTransportSchemaMigration(),
            new TerminalCredentialHandshakeSchemaMigration(),
            new OfficialReceiptProfileSchemaMigration(),
            new OfficialDryRunPackageSchemaMigration(),
            new VatRegistrationModeSchemaMigration(),
            new OfficialDocumentLifecycleCompletionSchemaMigration(),
            new OfficialDocumentLifecycleAccountingSchemaMigration(),
            new StorePrinterModeSchemaMigration(),
            new WindowsPrinterNameSchemaMigration(),
            new StoreLogoBrandingSchemaMigration(),
            new RemotePosCheckoutRequestSchemaMigration(),
            new AccountsReceivableSchemaMigration(),
            new HostCheckoutRequestSchemaMigration(),
            new ReceivableCollectionRequestSchemaMigration(),
            new CashierSessionReportSchemaMigration(),
            new CashMovementLedgerSchemaMigration(),
            new CashierSessionReportV2SchemaMigration(),
            new SyncClientOutboxFoundationMigration(),
            new SyncClientPullInboxFoundationMigration(),
            new MenuCatalogSchemaMigration(),
            new IngredientRecipeSchemaMigration(),
            new CafeInventorySchemaMigration(),
            new CafeOrderSchemaMigration(),
            new CafeFinancialSchemaMigration(),
            new CafeCashOperationsSchemaMigration(),
            new CafePrintDomainSchemaMigration(),
            new CafeDeliverySchemaMigration(),
            new CafeBookingSchemaMigration(),
            new CafeAttendanceIncidentSchemaMigration(),
            new CafeLineageAdoptionAndKernelParityMigration(),
            new CafeMutationIdempotencyAndDeviceTrustMigration(),
            new CafeRemoteCheckoutOperatorBindingMigration(),
            new CafeMutationExecutionFenceMigration()
        };
    }

    /// <summary>
    /// Exact legacy Cafe Yza v1-v39 sequence (skipping v23) used for synthetic v39 upgrade tests.
    /// </summary>
    public static IReadOnlyList<IDatabaseMigration> CreateLegacyYzaV39Migrations()
    {
        return CreateCanonicalCafeMigrations()
            .Where(m => m.Version <= 39 && m.Version != 23)
            .ToArray();
    }

    /// <summary>
    /// Exact Kernel Retail v1-v32 sequence used for negative wrong-product fail-closed tests.
    /// </summary>
    public static IReadOnlyList<IDatabaseMigration> CreateLegacyRetailKernelV32Migrations()
    {
        return new IDatabaseMigration[]
        {
            new InitialSchemaMigration(),
            new BirMachineProfileSchemaMigration(),
            new BirOfficialDocumentSequenceSchemaMigration(),
            new BirElectronicJournalSchemaMigration(),
            new BirElectronicJournalSchemaHardeningMigration(),
            new BirZReadingSchemaMigration(),
            new BirElectronicJournalCorrectionEventSchemaMigration(),
            new ProductTaxClassificationSchemaMigration(),
            new DeploymentIdentitySchemaMigration(),
            new TerminalRegistrySchemaMigration(),
            new LanRemoteTransportSchemaMigration(),
            new TerminalCredentialHandshakeSchemaMigration(),
            new OfficialReceiptProfileSchemaMigration(),
            new OfficialDryRunPackageSchemaMigration(),
            new VatRegistrationModeSchemaMigration(),
            new OfficialDocumentLifecycleCompletionSchemaMigration(),
            new OfficialDocumentLifecycleAccountingSchemaMigration(),
            new StorePrinterModeSchemaMigration(),
            new WindowsPrinterNameSchemaMigration(),
            new StoreLogoBrandingSchemaMigration(),
            new RemotePosCheckoutRequestSchemaMigration(),
            new AccountsReceivableSchemaMigration(),
            new HostCheckoutRequestSchemaMigration(),
            new ReceivableCollectionRequestSchemaMigration(),
            new CashDrawerReconciliationSchemaMigration(),
            new CashDrawerCloseReplaySchemaMigration(),
            new SaleVoidAuditReconciliationMigration(),
            new ProcureToPaySchemaMigration(),
            new ProcureToPayR2HardeningMigration(),
            new CustomerOrdersAndQuotationsSchemaMigration(),
            new CustomerOrdersHardeningSchemaMigration(),
            new SupplierPaymentActualDisbursementDateMigration()
        };
    }
}
