using Salryn.Tests;

namespace Salryn.Tools.CoreSmoke;

// Kirch: Runs compile-check safety methods as executable smoke checks without adding a test framework package.
public static class Program
{
    // Kirch: Executes the Host/client boundary smoke checks that dotnet test only compiles today.
    public static void Main()
    {
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

        J20DevelopmentLaunchModeChecks.Run();

        J20UiWorkflowReadinessChecks.Run();

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
        J21Rmo242023ElectronicJournalAppendServiceBoundaryChecks.Run();
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
        J21Rmo242023ElectronicJournalPersistenceRuntimeDocumentationSealChecks.Run();

        Console.WriteLine("SALRYN Core smoke checks passed.");
    }
}
