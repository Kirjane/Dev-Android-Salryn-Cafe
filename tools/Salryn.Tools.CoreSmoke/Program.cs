using Salryn.Tests;

namespace Salryn.Tools.CoreSmoke;

// Kirch: Runs compile-check safety methods as executable smoke checks without adding a test framework package.
public static class Program
{
    // Kirch: Executes the Host/client boundary smoke checks that dotnet test only compiles today.
    public static void Main(string[] args)
    {
        if (args.Length == 2 &&
            string.Equals(args[0], "--only", StringComparison.OrdinalIgnoreCase))
        {
            RunOnly(args[1]);
            Console.WriteLine("SALRYN Core smoke checks passed.");
            CoreSmokeDevelopmentDatabaseIsolation.Cleanup();
            return;
        }

        Phase61BStoreDeploymentCompileChecks.Run();
        Phase61CHostClientContractsCompileChecks.Run();
        Phase61DBackupMobileReadinessCompileChecks.Run();
        Phase62AHostBoundaryCompileChecks.Run();
        Phase62BHostOperationRegistrationCompileChecks.Run();
        Phase62CDescriptorAwareBoundaryCompileChecks.Run();
        Phase62EHostBoundaryRuntimeSafetyCompileChecks.Run();
        Phase63LocalHostRuntimeCompileChecks.Run();
        Phase63BLocalHostDispatchCompileChecks.RunAsync().GetAwaiter().GetResult();
        Phase63CLocalHostLifecycleFaultSafetyCompileChecks.RunAsync().GetAwaiter().GetResult();
        Phase63DLocalHostOperationContractMappingCompileChecks.RunAsync().GetAwaiter().GetResult();
        Phase63ELocalHostHandlerBootstrapCompileChecks.RunAsync().GetAwaiter().GetResult();
        Phase63FLocalHostBootstrapCompositionSmokeChecks.RunAsync().GetAwaiter().GetResult();
        Phase63GLocalHostOperationSurfaceReadinessSealChecks.Run();
        Phase64ALocalHostProductLookupHandlerCompileChecks.RunAsync().GetAwaiter().GetResult();
        Phase64BLocalHostProductLookupRuntimeRegistrationChecks.RunAsync().GetAwaiter().GetResult();
        Phase64CLocalHostProductLookupCompositionChecks.RunAsync().GetAwaiter().GetResult();
        Phase64DLocalHostProductLookupCompositionReadinessSealChecks.RunAsync().GetAwaiter().GetResult();
        Phase65AAppCompositionBoundaryPlanningChecks.Run();
        Phase65BAppLocalHostCompositionReferenceSliceChecks.Run();
        Phase65CAppLocalHostCompositionCreationGuardChecks.Run();
        Phase65DAppLocalHostCompositionReadinessSealChecks.Run();
        Phase65EAppLocalHostCompositionHolderSliceChecks.Run();
        Phase65FAppLocalHostCompositionHolderReadinessSealChecks.Run();
        Phase65GAppLocalHostHolderConsumptionPlanningChecks.Run();
        Phase65HAppLocalHostHolderStartupWiringSliceChecks.Run();
        Phase65IAppLocalHostHolderStartupWiringReadinessSealChecks.Run();
        Phase65JAppLocalHostHolderConsumptionBoundaryPlanningChecks.Run();
        Phase65KAppLocalHostHolderConsumptionAdapterContractPlanningChecks.Run();
        Phase65LAppLocalHostHolderConsumptionAdapterContractShellChecks.Run();
        Phase65MAppLocalHostHolderConsumptionAdapterImplementationShellChecks.Run();
        Phase65NAppLocalHostProductLookupGatewayAdapterShellBehaviorSealChecks.Run();
        Phase65OAppServicesGatewayRegistrationBoundaryPlanningChecks.Run();
        Phase65PPassiveAppServicesGatewayExposureChecks.Run();
        Phase65QHolderAwareGatewayWiringPlanningChecks.Run();
        Phase65RHolderAwareAdapterConstructorBoundaryShellChecks.Run();
        Phase65SAppServicesHolderAwareAdapterWiringShellChecks.Run();
        Phase65TAppLocalHostProductLookupDispatchBridgeChecks.Run();
        Phase65ULocalHostRuntimeLifecycleAndRequestContextOwnershipPlanningSealChecks.Run();
        Phase65VLocalHostRuntimeLifecycleHolderShellChecks.Run();
        Phase65XPassiveLocalHostProvidersAppServicesWiringChecks.Run();
        Phase65ZIdentityBoundaryWiringChecks.Run();
        Phase66BAdapterRequestContextProviderConsumptionChecks.Run();
        Phase66DSessionIdentityProviderAdapterWiringChecks.Run();
        Phase66G0AuthenticatedRoleIdPropagationChecks.Run();
        Phase66G1LocalHostRuntimeLifecycleActivationChecks.Run();
        Phase66G2AppServicesLocalHostStartupActivationChecks.Run();
        Phase66G4LocalHostProductLookupCheckoutReadyPayloadChecks.Run();
        Phase66G5CheckoutLocalHostProductSearchBoundaryChecks.Run();
        Phase66G6CheckoutLocalHostProductSearchWiringChecks.Run();
        Phase66G7CheckoutLocalHostLookupSmokeVerificationChecks.Run();
        Phase66G8CheckoutLocalHostDatabaseLookupSmokeVerificationChecks.Run();
        Phase66G8ACheckoutLocalHostDatabaseSmokeCleanupHardeningChecks.Run();
        J20MvpAppServicesExposureChecks.Run();
        J20MvpSkeletonUiChecks.Run();
        UiA1VisualSystemFoundationChecks.Run();
        UiA1RProductManagementVisualPilotChecks.Run();
        UiA1RProductMockupAlignmentChecks.Run();
        UiA1RProductVisualParityChecks.Run();
        UiA1RInventoryVisualAlignmentChecks.Run();
        UiA1RAdminTablesVisualAlignmentChecks.Run();
        UiA1RSettingsBackupAuditVisualAlignmentChecks.Run();
        J20DevelopmentLaunchModeChecks.Run();
        WebA1LocalApiReactDashboardChecks.Run();
        WebA2LocalApiAuthSessionChecks.Run();
        WebA3ShellDesignSystemChecks.Run();
        WebA4NativeHostVisualFidelityChecks.Run();
        WebB0ReadOnlyOperatorSurfacesChecks.Run();
        PreI1FreshLicenseFirstFlowChecks.Run();
        RoleM1OperatorRolePermissionUxCompileChecks.Run();
        RoleM2BackendPermissionEnforcementChecks.Run();
        RoleM3HttpApiPermissionRuntimeChecks.RunAsync().GetAwaiter().GetResult();
        RoleM4PermissionBoundaryRegressionMatrixChecks.RunAsync().GetAwaiter().GetResult();
        V2HostOperatorShiftApiChecks.RunAsync().GetAwaiter().GetResult();
        V2HostOperatorCheckoutApiChecks.RunAsync().GetAwaiter().GetResult();
        V2ReactCheckoutSourceChecks.Run();
        V2LanBindTruthChecks.Run();
        V2LanClientPairingPersistenceChecks.Run();
        V2TerminalLifecycleUxChecks.Run();
        V2HostLanControlUxChecks.Run();
        RmoBirM3OfficialReadinessChecks.Run();
        RmoBirM4OfficialSetupChecks.Run();
        RmoBirM5OfficialNumberingChecks.Run();
        RmoBirM6OfficialIntegrityChecks.Run();
        RmoBirM7OfficialSpineSprintChecks.Run();
        RmoBirM8EndgameChecks.Run();
        RmoBirM9FullClosureChecks.Run();
        RmoBirM10OfficialSourceAcquisitionChecks.Run();
        RmoBirM11GuardedLivePostingCandidateChecks.Run();
        RmoBirQa1RealMachineDatabaseChecks.Run();
        RmoBirTradM1TaxInvoicePolicyChecks.Run();
        RmoBirTradM2AtomicOfficialPostingChecks.Run();
        RmoBirTradM3InvoiceOutputChecks.Run();
        RmoBirTradM3Rc1InvoiceOutputDeliveryChecks.Run();
        RmoBirTradM3Rc2SnapshotCaptureChecks.Run();
        RmoBirTradM4DocumentLifecycleChecks.Run();
        RmoBirTradM4Rc1LifecycleRuntimeChecks.Run();
        RmoBirTradM4Rc2LifecycleAccountingRuntimeChecks.Run();
        RmoBirTradM5Rc2XzEjEsalesCompletionChecks.Run();
        PreCrystalM0FullReportingSpineChecks.Run();
        CrystalM1RuntimeDecisionMinimalProofChecks.Run();
        NativePdfM2PackageProbeChecks.Run();
        NativePdfM3RendererCoreChecks.Run();
        NativePdfM4CoreReportFamilyProofChecks.Run();
        NativePdfM5ArtifactExportHardeningChecks.Run();
        NativePdfM6IntegrationFreezeCandidateChecks.Run();
        LocalLicensingM1FoundationChecks.Run();
        LocalLicensingM2SignedInstallImportChecks.Run();
        LocalLicensingM3LocalEnforcementBoundaryChecks.Run();
        LocalLicensingBetaManualHostTerminalActivationChecks.Run();
        LocalLicensingLanTerminalEntitlementEnforcementChecks.Run();
        LocalLicensingCloseoutHardGatesAndTerminalProofChecks.Run();
        PreI1LanTerminalActivationManagementBetaChecks.Run();
        PreBetaELanClientShellParityChecks.Run();
        PreBetaCStoreLogoBrandingChecks.Run();
        PreBetaDPortableBackupImportExportChecks.Run();
        PreBetaFScannerFirstCheckoutChecks.Run();
        PreBetaGPrinterHardeningChecks.Run();
        PreBetaHCapabilityMatrixSealChecks.Run();
        PreBetaLicenseCreditsPagePolishChecks.Run();
        PreIActivationLandingManualQaRepairChecks.Run();
        PreILicenseLandingSellUpgradeChecks.Run();
        PreITerminalLifecycleUxRepairChecks.Run();
        PreILanSetupUxClarityChecks.Run();
        PreIActionNotificationSystemChecks.Run();
        PreIP1CopyGuardHonestyChecks.Run();
        PreIAutomatedQaProofChecks.Run();
        PreICodeProvenQaHardeningChecks.Run();
        PreIDevStateResetTruthChecks.Run();
        PreILicensePageMinimalistRebuildChecks.Run();
        PreIClientFacingProductPolishChecks.Run();
        PreIAuditTrailCoverageChecks.Run();
        PreIAuditCsvExportChecks.RunAsync().GetAwaiter().GetResult();
        HardenA1A6PreBetaFoundationChecks.Run();
        ManualM1BootstrapAuthRepairChecks.Run();
        UserM1HostOnlyUserCreationChecks.RunAsync().GetAwaiter().GetResult();
        M5VerifiedCandidateStackLedgerChecks.Run();
        M6CandidateStackPromotionDryRunChecks.Run();
        M7CandidateStackPromotionDebtMeterChecks.Run();
        M8MSeriesCompletionLanTransitionGateChecks.Run();
        SecA1LocalApiWebViewSecurityChecks.Run();
        CleanupA1OperationalHardeningChecks.RunAsync().GetAwaiter().GetResult();
        DbA2DatabaseSafetyChecks.Run();
        J20UiWorkflowReadinessChecks.Run();
        PosStabA1StartupLoginReadinessChecks.Run();
        PosStabA2ShiftCloseChecks.Run();
        PosStabA3SalesVoidChecks.Run();
        PosStabA4InventoryAdjustmentChecks.Run();
        PosStabA5ReceiptPrintingChecks.Run();
        PosM1ReceiptSnapshotFileReceiptChecks.Run();
        PosM2PrinterReprintFailureChecks.Run();
        PosM3BarcodeScannerCashierFlowChecks.Run();
        PosM4FastAddUnknownBarcodeInventoryChecks.Run();
        PosM5ProductWriteManagementChecks.Run();
        PosM6StockInStockAdjustmentChecks.Run();
        PosM7VoidSaleStockReversalChecks.Run();
        PosSurface1RolePageLockStoreNavChecks.Run();
        PosR1OwnerReportsChecks.Run();
        PosS1StoreSettingsIdentityPrinterChecks.Run();
        PosB1BackupRestoreSafetyChecks.Run();
        PosT1FullStoreRuntimeTortureChecks.Run();
        PosManual1FrontendRepairChecks.Run();
        PosManual1R3FrontendSafetyUxRepairChecks.Run();
        PosPrinter1WindowsPrinterWiringChecks.Run();
        PosStabB2BackupUiChecks.Run();
        PosStabB2AuditViewerChecks.Run();
        PosStabB3ProductionDbWritabilityChecks.Run();
        PosStabB3ProductMaintenanceChecks.Run();
        PosStabB3StoreSettingsEditChecks.Run();
        PosStabB4UsersAdminChecks.Run();
        PosStabB5BackupRecoveryChecks.Run();
        PosStabB6AuditOperationsChecks.Run();
        PosStabB7PresentationUxChecks.Run();
        PosStabB8SalesHistoryOperationsChecks.Run();
        J21Rmo242023ComplianceMatrixGuardChecks.Run();
        J21Rmo242023MachineProfileSchemaChecks.Run();
        J21Rmo242023OfficialDocumentSequenceSchemaChecks.Run();
        J21Rmo242023OfficialDocumentSequenceAllocationChecks.Run();
        J21Rmo242023OfficialDocumentSnapshotDraftChecks.Run();
        J21Rmo242023OfficialDocumentSnapshotDraftBuilderChecks.Run();
        J21Rmo242023OfficialDocumentSnapshotEvidenceBuilderChecks.Run();
        J21Rmo242023OfficialDocumentJournalAppendContractBuilderChecks.Run();
        J21Rmo242023OfficialDocumentTaxBucketDraftChecks.Run();
        J21Rmo242023OfficialDocumentTaxBucketDraftValidationChecks.Run();
        J21Rmo242023ElectronicJournalPlanningChecks.Run();
        J21Rmo242023ElectronicJournalSchemaPlanningChecks.Run();
        J21Rmo242023ElectronicJournalSchemaFoundationChecks.Run();
        J21Rmo242023ElectronicJournalDatabaseRiskAuditChecks.Run();
        J21Rmo242023ElectronicJournalSchemaHardeningChecks.Run();
        J21Rmo242023ElectronicJournalAppendContractPlanningChecks.Run();
        J21Rmo242023ElectronicJournalAppendAtomicBoundaryChecks.Run();
        J21Rmo242023ElectronicJournalAppendPersistencePortChecks.RunAsync().GetAwaiter().GetResult();
        J21Rmo242023ElectronicJournalAppendOrchestrationShellChecks.RunAsync().GetAwaiter().GetResult();
        J21Rmo242023ElectronicJournalAppendRegistrationBoundaryChecks.RunAsync().GetAwaiter().GetResult();
        J21Rmo242023ElectronicJournalAppendBoundaryDocumentationSealChecks.Run();
        J21Rmo242023SharedSqliteTransactionBoundaryContractChecks.Run();
        J21Rmo242023SharedSqliteTransactionBoundaryImplementationChecks.Run();
        J21Rmo242023SharedTransactionOfficialSequenceAllocationParticipantChecks.Run();
        J21Rmo242023OfficialSequenceAllocationServiceSharedBoundaryReuseChecks.Run();
        J21Rmo242023SqliteElectronicJournalAppendParticipantSkeletonChecks.Run();
        J21Rmo242023SqliteElectronicJournalAppendParticipantInsertChecks.Run();
        J21Rmo242023SqliteElectronicJournalAppendAtomicPersistenceAdapterChecks.RunAsync().GetAwaiter().GetResult();
        J21Rmo242023SqliteElectronicJournalAppendServiceCompositionChecks.Run();
        J21Rmo242023SqliteElectronicJournalAppendRuntimeSmokeChecks.RunAsync().GetAwaiter().GetResult();
        J21Rmo242023SharedTransactionSequenceAndJournalAppendRuntimeSmokeChecks.RunAsync().GetAwaiter().GetResult();
        J21Rmo242023OfficialCheckoutSharedTransactionRuntimeSmokeChecks.RunAsync().GetAwaiter().GetResult();
        J21Rmo242023OfficialCheckoutReadinessGateChecks.RunAsync().GetAwaiter().GetResult();
        J21Rmo242023CheckoutOfficialModeUiReadinessChecks.RunAsync().GetAwaiter().GetResult();
        J21Rmo242023OfficialEvidenceExportFoundationChecks.Run();
        J21Rmo242023OfficialXReadingFoundationChecks.Run();
        J21Rmo242023OfficialZReadingFoundationChecks.Run();
        J21Rmo242023OfficialDocumentAuditEventFoundationChecks.Run();
        J21Rmo242023OfficialDocumentLifecycleReadModelChecks.Run();
        J21Rmo242023OfficialOutputSourceContractChecks.Run();
        J21Rmo242023OfficialDocumentReprintAuditRulesChecks.Run();
        J21Rmo242023OfficialPrintableDocumentDtoFoundationChecks.Run();
        J21Rmo242023OfficialReportBindingProofChecks.Run();
        J21Rmo242023OfficialRendererAdapterContractProofChecks.Run();
        J21Rmo242023OfficialRendererImplementationShellChecks.Run();
        J21Rmo242023OfficialReceiptInvoiceLayoutProofChecks.Run();
        J21Rmo242023OfficialTextPreviewRendererChecks.Run();
        J21Rmo242023OfficialReadingReportLayoutProofChecks.Run();
        J21Rmo242023OfficialAuditEvidenceReportLayoutProofChecks.Run();
        J21Rmo242023OfficialOutputArtifactManifestChecks.Run();
        J21Rmo242023OfficialOutputExportPackageChecks.Run();
        J21Rmo242023OfficialInMemoryOutputDeliveryAdapterChecks.Run();
        J21Rmo242023OfficialOutputDeliveryReceiptLedgerChecks.Run();
        J21Rmo242023OfficialExternalOutputAdapterContractChecks.Run();
        J21Rmo242023OfficialOutputPipelineCompletionSealChecks.Run();
        J22OfficialOutputTextFileWorkflowExposureContractChecks.Run();
        J22OfficialOutputTextFileWorkflowAppCompositionChecks.Run();
        J22OfficialOutputTextFileWorkflowLocalHostContractChecks.RunAsync().GetAwaiter().GetResult();
        J22OfficialOutputTextFileWorkflowExecutionPortChecks.Run();
        J22OfficialOutputTextFileWorkflowLocalHostHandlerChecks.RunAsync().GetAwaiter().GetResult();
        J22OfficialOutputTextFileWorkflowLocalHostGatewayChecks.RunAsync().GetAwaiter().GetResult();
        J22OfficialOutputTextFileWorkflowLocalHostSealChecks.Run();
        J22OfficialOutputTextFileWorkflowReadinessChecks.Run();
        J22OfficialOutputTextFileWorkflowUiTriggerShellChecks.Run();
        J22OfficialOutputTextFileSourceIdentityBoundaryChecks.RunAsync().GetAwaiter().GetResult();
        J22OfficialOutputTextFileSourceSelectionBoundaryChecks.Run();
        J22OfficialOutputTextFileSourceSelectionPackageSupplyBridgeChecks.Run();
        J22OfficialOutputTextFilePackageSupplyBoundaryChecks.Run();
        J22OfficialOutputTextFileTrustedPackageProducerChecks.RunAsync().GetAwaiter().GetResult();
        J22OfficialOutputTextFileVisibleSelectionSurfaceChecks.Run();
        RmoA2OfficialOutputSmokeRoutingChecks.Run();
        RmoA3OfficialOutputRootPolicyChecks.Run();
        RmoA4OfficialOutputRepeatRunPolicyChecks.Run();
        RmoA5OfficialOutputPermissionChecks.Run();
        RmoA6OfficialOutputAuditTrailChecks.Run();
        RmoA7InternalOfficialOutputTextFileWorkflowSealChecks.Run();
        RmoB1OfficialCheckoutDataIntegrityCurrentStateSealChecks.Run();
        RmoEngineeringVatTaxBucketEngineChecks.Run();
        RmoEngineeringCompletionCandidateChecks.Run();
        RmoOfficialLifecycleOperationBoundaryPermissionChecks.RunAsync().GetAwaiter().GetResult();
        PrelanHarden1ProductTaxClassificationChecks.Run();
        DatabaseInitializationStatusReadOnlyChecks.Run();
        PrelanHarden2EvidenceAndZPersistenceChecks.Run();
        PrelanHarden3OfficialOperatorSurfaceChecks.Run();
        PrelanHarden4VerificationHygieneChecks.Run();
        LanM1DeploymentRoleAndStoreIdentityChecks.Run();
        LanM2M3HostCommandLoopbackChecks.Run();
        LanM4TerminalRegistryPairingPreparationChecks.Run();
        LanM5ControlledRemoteTransportGateChecks.Run();
        LanM6PairingActivationRemoteHandshakeChecks.Run();
        LanM7SafeRemoteReadOnlyClientChecks.Run();
        LanM14APostPromotionScopeAuditChecks.Run();
        LanM14BExplicitHostBindReadinessChecks.Run();
        LanM14CRemoteReadonlySessionBridgeCutlineChecks.Run();
        LanM14DBridgeSessionContractStudyChecks.Run();
        LanM14EAuditReadAttemptPolicyCutlineChecks.Run();
        LanM14FHostBindConfigSeamChecks.Run();
        LanM14GRemoteReadonlyBridgeRuntimeChecks.Run();
        LanM14HPairingOperatorUxSurfaceChecks.Run();
        LanM14IEndToEndLanReadOnlyClosureChecks.Run();
        LanM15ARemoteReadonlyLanFieldHardeningChecks.Run();
        LanM15BRemoteReadonlyLanPreflightChecks.Run();
        LanM15CLocalControlChecks.Run();
        LanM15DSessionResilienceChecks.Run();
        LanM15ENegativePathHardeningChecks.Run();
        LanM15FFinalAcceptanceChecks.Run();
        LanM16HostOwnedRemoteOperatorSessionChecks.Run();
        LanM16RemotePosCommandAuthorizationChecks.Run();
        LanM16RemoteCheckoutIdempotencyAndHostWriteChecks.Run();
        LanM16HostClientIdentityUxChecks.Run();
        LanM16OriginBindingChecks.Run();
        LanM16PostCommitAuditSafetyChecks.Run();
        LanM16FinalMultiTerminalAcceptanceChecks.Run();
        LanM16QaBRemotePosPairingGateChecks.Run();
        LanM16RcBEntryBoundaryChecks.Run();
        J21Rmo242023ElectronicJournalPersistenceRuntimeDocumentationSealChecks.Run();
        Console.WriteLine("SALRYN Core smoke checks passed.");
        CoreSmokeDevelopmentDatabaseIsolation.Cleanup();
    }

    private static void RunOnly(string checkName)
    {
        switch (checkName.ToLowerInvariant())
        {
            case "pos-m1-receipt-snapshot-file-receipt":
                PosM1ReceiptSnapshotFileReceiptChecks.Run();
                return;
            case "pos-m2-printer-reprint-failure":
                PosM2PrinterReprintFailureChecks.Run();
                return;
            case "pos-m3-barcode-scanner-cashier-flow":
                PosM3BarcodeScannerCashierFlowChecks.Run();
                return;
            case "pos-m4-fast-add-unknown-barcode-inventory":
                PosM4FastAddUnknownBarcodeInventoryChecks.Run();
                return;
            case "pos-m5-product-write-management":
                PosM5ProductWriteManagementChecks.Run();
                return;
            case "pos-m6-stock-in-stock-adjustment":
                PosM6StockInStockAdjustmentChecks.Run();
                return;
            case "pos-m7-void-sale-stock-reversal":
                PosM7VoidSaleStockReversalChecks.Run();
                return;
            case "pos-surface1-role-page-lock-store-nav":
                PosSurface1RolePageLockStoreNavChecks.Run();
                return;
            case "pos-r1-owner-reports":
                PosR1OwnerReportsChecks.Run();
                return;
            case "pos-s1-store-settings-identity-printer":
                PosS1StoreSettingsIdentityPrinterChecks.Run();
                return;
            case "web-a3-shell-design-system":
                WebA3ShellDesignSystemChecks.Run();
                return;
            case "web-a4-native-host-visual-fidelity":
                WebA4NativeHostVisualFidelityChecks.Run();
                return;
            case "web-b0-read-only-operator-surfaces":
                WebB0ReadOnlyOperatorSurfacesChecks.Run();
                return;
            case "pre-i1-fresh-license-first-flow":
                PreI1FreshLicenseFirstFlowChecks.Run();
                return;
            case "pos-b1-backup-restore-safety":
                PosB1BackupRestoreSafetyChecks.Run();
                return;
            case "pos-t1-full-store-runtime-torture":
                PosT1FullStoreRuntimeTortureChecks.Run();
                return;
            case "pos-manual1-frontend-repair":
                PosManual1FrontendRepairChecks.Run();
                return;
            case "pos-manual1-r3-frontend-safety-ux-repair":
                PosManual1R3FrontendSafetyUxRepairChecks.Run();
                return;
            case "pos-printer1-windows-printer-wiring":
                PosPrinter1WindowsPrinterWiringChecks.Run();
                return;
            case "v2-host-shift":
                V2HostOperatorShiftApiChecks.RunAsync().GetAwaiter().GetResult();
                return;
            case "v2-host-checkout":
                V2HostOperatorCheckoutApiChecks.RunAsync().GetAwaiter().GetResult();
                return;
            case "v2-react-checkout":
                V2ReactCheckoutSourceChecks.Run();
                return;
            case "v2-lan-client-pairing":
                V2LanClientPairingPersistenceChecks.Run();
                return;
            case "v2-lan-bind-truth":
                V2LanBindTruthChecks.Run();
                return;
            case "v2-terminal-lifecycle":
                V2TerminalLifecycleUxChecks.Run();
                return;
            case "v2-host-lan-control-ux":
                V2HostLanControlUxChecks.Run();
                return;
            case "rmo-bir-official-readiness":
                RmoBirM3OfficialReadinessChecks.Run();
                return;
            case "rmo-bir-official-setup":
                RmoBirM4OfficialSetupChecks.Run();
                return;
            case "rmo-bir-official-numbering":
                RmoBirM5OfficialNumberingChecks.Run();
                return;
            case "rmo-bir-official-integrity":
                RmoBirM6OfficialIntegrityChecks.Run();
                return;
            case "rmo-bir-official-spine":
                RmoBirM7OfficialSpineSprintChecks.Run();
                return;
            case "rmo-bir-endgame":
                RmoBirM8EndgameChecks.Run();
                return;
            case "rmo-bir-full-closure":
                RmoBirM9FullClosureChecks.Run();
                return;
            case "rmo-bir-source-acquisition":
                RmoBirM10OfficialSourceAcquisitionChecks.Run();
                return;
            case "rmo-bir-live-posting-candidate":
                RmoBirM11GuardedLivePostingCandidateChecks.Run();
                return;
            case "rmo-bir-qa1-real-db":
                RmoBirQa1RealMachineDatabaseChecks.Run();
                return;
            case "rmo-bir-trad-tax-invoice-policy":
                RmoBirTradM1TaxInvoicePolicyChecks.Run();
                return;
            case "rmo-bir-trad-atomic-posting":
                RmoBirTradM2AtomicOfficialPostingChecks.Run();
                return;
            case "rmo-bir-trad-invoice-output":
                RmoBirTradM3InvoiceOutputChecks.Run();
                return;
            case "rmo-bir-trad-invoice-output-delivery":
            case "rmo-bir-trad-invoice-output-delivery-rc1":
                RmoBirTradM3Rc1InvoiceOutputDeliveryChecks.Run();
                return;
            case "rmo-bir-trad-snapshot-capture-rc2":
                RmoBirTradM3Rc2SnapshotCaptureChecks.Run();
                return;
            case "rmo-bir-trad-document-lifecycle":
                RmoBirTradM4DocumentLifecycleChecks.Run();
                return;
            case "rmo-bir-trad-document-lifecycle-runtime":
                RmoBirTradM4Rc1LifecycleRuntimeChecks.Run();
                return;
            case "rmo-bir-trad-document-lifecycle-accounting-runtime":
                RmoBirTradM4Rc2LifecycleAccountingRuntimeChecks.Run();
                return;
            case "rmo-bir-trad-m5-rc2-xz-ej-esales-completion":
                RmoBirTradM5Rc2XzEjEsalesCompletionChecks.Run();
                return;
            case "reports-precrystal-m0-full-reporting-spine":
                PreCrystalM0FullReportingSpineChecks.Run();
                return;
            case "reports-crystal-m1-runtime-decision-minimal-proof":
                CrystalM1RuntimeDecisionMinimalProofChecks.Run();
                return;
            case "reports-native-pdf-m2-package-probe":
                NativePdfM2PackageProbeChecks.Run();
                return;
            case "reports-native-pdf-m3-renderer-core":
                NativePdfM3RendererCoreChecks.Run();
                return;
            case "reports-native-pdf-m4-core-report-family-proof":
                NativePdfM4CoreReportFamilyProofChecks.Run();
                return;
            case "reports-native-pdf-m5-artifact-export-hardening":
                NativePdfM5ArtifactExportHardeningChecks.Run();
                return;
            case "reports-native-pdf-m6-integration-freeze-candidate":
                NativePdfM6IntegrationFreezeCandidateChecks.Run();
                return;
            case "licensing-m1-local-license-foundation":
                LocalLicensingM1FoundationChecks.Run();
                return;
            case "licensing-m2-signed-license-install-import":
                LocalLicensingM2SignedInstallImportChecks.Run();
                return;
            case "licensing-m3-local-enforcement-boundaries":
                LocalLicensingM3LocalEnforcementBoundaryChecks.Run();
                return;
            case "lic-beta-manual-host-terminal-activation":
                LocalLicensingBetaManualHostTerminalActivationChecks.Run();
                return;
            case "lic-lan-terminal-entitlement-enforcement":
                LocalLicensingLanTerminalEntitlementEnforcementChecks.Run();
                return;
            case "lic-closeout-hard-gates-and-terminal-proof":
                LocalLicensingCloseoutHardGatesAndTerminalProofChecks.Run();
                return;
            case "pre-i1-lan-terminal-activation-management-beta":
                LocalLicensingLanTerminalEntitlementEnforcementChecks.Run();
                LocalLicensingCloseoutHardGatesAndTerminalProofChecks.Run();
                PreI1LanTerminalActivationManagementBetaChecks.Run();
                return;
            case "pre-beta-e-lan-client-shell-parity":
                PreBetaELanClientShellParityChecks.Run();
                return;
            case "pre-beta-c-store-logo-branding":
                PreBetaCStoreLogoBrandingChecks.Run();
                return;
            case "pre-beta-d-portable-backup-import-export":
                PreBetaDPortableBackupImportExportChecks.Run();
                return;
            case "pre-beta-f-scanner-first-checkout":
                PreBetaFScannerFirstCheckoutChecks.Run();
                return;
            case "pre-beta-g-printer-hardening":
                PreBetaGPrinterHardeningChecks.Run();
                return;
            case "pre-beta-h-capability-matrix-seal":
                PreBetaHCapabilityMatrixSealChecks.Run();
                return;
            case "pre-beta-license-credits-page-polish":
                PreBetaLicenseCreditsPagePolishChecks.Run();
                return;
            case "pre-i-activation-landing-manual-qa-repair":
                PreIActivationLandingManualQaRepairChecks.Run();
                return;
            case "pre-i-license-landing-sell-upgrade":
                PreILicenseLandingSellUpgradeChecks.Run();
                return;
            case "pre-i-terminal-lifecycle-ux-repair":
                PreITerminalLifecycleUxRepairChecks.Run();
                return;
            case "pre-i-lan-setup-ux-clarity":
                PreILanSetupUxClarityChecks.Run();
                return;
            case "pre-i-action-notification-system":
                PreIActionNotificationSystemChecks.Run();
                return;
            case "pre-i-p1-copy-guard-honesty":
                PreIP1CopyGuardHonestyChecks.Run();
                return;
            case "pre-i-automated-qa-proof":
                PreIAutomatedQaProofChecks.Run();
                return;
            case "pre-i-code-proven-qa-hardening":
                PreICodeProvenQaHardeningChecks.Run();
                return;
            case "pre-i-dev-state-reset-truth":
                PreIDevStateResetTruthChecks.Run();
                return;
            case "pre-i-license-page-minimalist-rebuild":
                PreILicensePageMinimalistRebuildChecks.Run();
                return;
            case "pre-i-client-facing-product-polish":
                PreIClientFacingProductPolishChecks.Run();
                return;
            case "pre-i-audit-trail-coverage":
                PreIAuditTrailCoverageChecks.Run();
                return;
            case "pre-i-audit-csv-export":
                PreIAuditCsvExportChecks.RunAsync().GetAwaiter().GetResult();
                return;
            case "lan-m16-remote-pos":
                LanM16HostOwnedRemoteOperatorSessionChecks.Run();
                LanM16RemotePosCommandAuthorizationChecks.Run();
                LanM16RemoteCheckoutIdempotencyAndHostWriteChecks.Run();
                LanM16HostClientIdentityUxChecks.Run();
                LanM16OriginBindingChecks.Run();
                LanM16PostCommitAuditSafetyChecks.Run();
                LanM16FinalMultiTerminalAcceptanceChecks.Run();
                LanM16QaBRemotePosPairingGateChecks.Run();
                LanM16RcBEntryBoundaryChecks.Run();
                return;
            case "harden-a1-a6-pre-beta-foundation":
                HardenA1A6PreBetaFoundationChecks.Run();
                return;
            case "manual-m1-bootstrap-auth-repair":
                ManualM1BootstrapAuthRepairChecks.Run();
                return;
            case "user-m1-host-only-user-creation":
                UserM1HostOnlyUserCreationChecks.RunAsync().GetAwaiter().GetResult();
                return;
            default:
                throw new InvalidOperationException("Unknown CoreSmoke --only check: " + checkName);
        }
    }
}
