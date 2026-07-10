namespace Salryn.Tests;

/// <summary>
/// RMO-BIR-M8 endgame: internal dry-run package + report previews; live issuance blocked.
/// </summary>
public static class RmoBirM8EndgameChecks
{
    public static void Run()
    {
        string root = FindProjectRoot(Directory.GetCurrentDirectory());

        string packageContracts = Read(Path.Combine(root, "src", "Salryn.Core", "Official", "OfficialDryRunPackageContracts.cs"));
        string previewContracts = Read(Path.Combine(root, "src", "Salryn.Core", "Official", "OfficialReportPreviewContracts.cs"));
        string packageIface = Read(Path.Combine(root, "src", "Salryn.Core", "Services", "IHostOfficialDryRunPackageService.cs"));
        string previewIface = Read(Path.Combine(root, "src", "Salryn.Core", "Services", "IHostOfficialReportPreviewService.cs"));
        string packageService = Read(Path.Combine(root, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteHostOfficialDryRunPackageService.cs"));
        string previewService = Read(Path.Combine(root, "src", "Salryn.Infrastructure", "Data", "Services", "HostOfficialReportPreviewService.cs"));
        string safetyConstants = Read(Path.Combine(root, "src", "Salryn.Core", "Official", "OfficialSafetyConstants.cs"));
        string internalRenderers = Read(Path.Combine(root, "src", "Salryn.Core", "Services", "OfficialInternalReportRenderers.cs"));
        string migration = Read(Path.Combine(root, "src", "Salryn.Infrastructure", "Data", "Migrations", "OfficialDryRunPackageSchemaMigration.cs"));
        string catalog = Read(Path.Combine(root, "src", "Salryn.Infrastructure", "Data", "SalrynDatabaseMigrationCatalog.cs"));
        string initFactory = Read(Path.Combine(root, "src", "Salryn.Infrastructure", "Data", "Initialization", "DefaultDatabaseInitializationFactory.cs"));
        string endpoints = Read(Path.Combine(root, "src", "Salryn.Api.Local", "LocalOfficialDryRunPackageEndpoints.cs"));
        string factory = Read(Path.Combine(root, "src", "Salryn.Api.Local", "LocalApiApplicationFactory.cs"));
        string hostCheckout = Read(Path.Combine(root, "src", "Salryn.Api.Local", "Services", "HostOperatorCheckoutApiService.cs"));
        string remoteCheckout = Read(Path.Combine(root, "src", "Salryn.Api.Local", "RemotePos", "RemotePosCheckoutService.cs"));
        string spineContracts = Read(Path.Combine(root, "src", "Salryn.Core", "Official", "OfficialSpineStatusContracts.cs"));
        string spineService = Read(Path.Combine(root, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteHostOfficialSpineStatusService.cs"));
        string readiness = Read(Path.Combine(root, "src", "Salryn.Core", "Services", "HostOfficialReadinessService.cs"));
        string setupPage = Read(Path.Combine(root, "src", "Salryn.App.Web", "src", "pages", "OfficialSetupPage.tsx"));
        string dbA2 = Read(Path.Combine(root, "tests", "Salryn.Tests", "DbA2DatabaseSafetyChecks.cs"));
        string study = Read(Path.Combine(root, "00-HANDOFF", "RMO_BIR_M8_ENDGAME_STUDY.md"));
        string handoff = Read(Path.Combine(root, "00-HANDOFF", "RMO_BIR_M8_ENDGAME_INTERNAL_COMPLETION.md"));

        AssertContains(packageContracts, "OfficialDryRunPackageContract", "Package contract required.");
        AssertContains(packageContracts, "OfficialDryRunPackageFlags", "Package flags required.");
        AssertContains(packageContracts, "OfficialReceiptIssued", "Package flags must include official receipt issued.");
        AssertContains(previewContracts, "OfficialReportPreviewPackageContract", "Report preview package required.");
        AssertContains(previewContracts, "OfficialReceiptPreviewContract", "Receipt preview required.");
        AssertContains(previewContracts, "OfficialXReadingPreviewContract", "X reading preview required.");
        AssertContains(previewContracts, "OfficialZReadingPreviewContract", "Z reading preview required.");
        AssertContains(previewContracts, "OfficialJournalExportPreviewContract", "Journal export preview required.");
        AssertContains(previewContracts, "OfficialVoidRefundPreviewContract", "Void/refund preview required.");
        AssertContains(previewContracts, "InternalOnly", "Preview format must include InternalOnly.");

        AssertContains(packageIface, "CreateAsync", "Package service create required.");
        AssertContains(packageIface, "ListRecentAsync", "Package service list required.");
        AssertContains(packageIface, "GetByIdAsync", "Package service get required.");
        AssertContains(previewIface, "BuildPreviewAsync", "Preview service build required.");

        AssertContains(migration, "Version => 14", "Migration v14 required.");
        AssertContains(migration, "official_dry_run_packages", "Dry-run package table required.");
        AssertContains(migration, "live_official_posting_allowed INTEGER NOT NULL DEFAULT 0", "Migration must force live posting off.");
        AssertContains(migration, "sequence_will_mutate INTEGER NOT NULL DEFAULT 0", "Migration must force sequence no-mutate.");
        AssertContains(initFactory, "OfficialDryRunPackageSchemaMigration", "Migration must be registered.");
        AssertContains(catalog, "SupportedMigrationVersion = 16", "Catalog must support v16 (M4-RC1).");
        AssertContains(catalog, "official_dry_run_packages", "Catalog critical tables must include dry-run packages.");
        AssertContains(dbA2, "futureVersion = 17", "DB-A2 future probe must be supported+1.");

        AssertContains(packageService, "official_dry_run_packages", "Package service must persist to table.");
        AssertContains(packageService, "OfficialDryRunPackageSourceMode.Synthetic", "Packages must support synthetic source.");
        AssertContains(safetyConstants, "SequenceWillMutate: false", "Package flags must deny sequence mutation.");
        AssertContains(safetyConstants, "SalesWillMutate: false", "Package flags must deny sales mutation.");
        AssertContains(safetyConstants, "EjWillAppendLive: false", "Package flags must deny live EJ append.");
        AssertContains(safetyConstants, "OutputWillGenerate: false", "Package flags must deny output generate.");
        AssertContains(safetyConstants, "OfficialReceiptIssued: false", "Package flags must deny official receipt issued.");
        AssertContains(safetyConstants, "ComplianceClaimed: false", "Package flags must deny compliance claim.");
        AssertContains(packageService, "ForcedNonLivePackageFlags", "Package service must use forced non-live flags.");
        AssertNotContains(packageService, "UPDATE bir_official_document_sequences", "Package service must not mutate sequences.");
        AssertNotContains(packageService, "INSERT INTO bir_electronic_journal", "Package service must not append EJ.");
        AssertNotContains(packageService, "AllocateNextAsync", "Package service must not allocate official numbers.");

        AssertContains(safetyConstants, "INTERNAL DRY-RUN ONLY — NOT AN OFFICIAL RECEIPT OR INVOICE", "Preview watermark required.");
        AssertContains(previewService, "OfficialReceiptIssued: false", "Preview must not issue official receipt.");
        AssertContains(internalRenderers, "ResetsCounters: false", "X/Z preview must not reset counters.");
        AssertNotContains(previewService, "GeneratePdf", "Preview must not generate PDF payloads.");
        AssertNotContains(previewService, "SendToPrinter", "Preview must not send to printer.");
        AssertNotContains(internalRenderers, "GeneratePdf", "Internal renderers must not generate PDF payloads.");
        AssertNotContains(internalRenderers, "SendToPrinter", "Internal renderers must not send to printer.");

        AssertContains(endpoints, "/api/official/dry-run/packages", "Packages list/create endpoint required.");
        AssertContains(endpoints, "/api/official/dry-run/report-preview", "Report preview endpoint required.");
        AssertContains(endpoints, "RequireLoopbackOperatorRequest", "Endpoints must be host-local.");
        AssertContains(endpoints, "liveOfficialPostingAllowed = false", "Endpoints must deny live posting.");
        AssertContains(endpoints, "officialReceiptIssued = false", "Endpoints must deny official receipt issued.");
        AssertContains(endpoints, "sequenceWillMutate = false", "Endpoints must deny sequence mutation.");
        AssertContains(endpoints, "salesWillMutate = false", "Endpoints must deny sales mutation.");
        AssertContains(endpoints, "ejWillAppendLive = false", "Endpoints must deny live EJ append.");
        AssertContains(endpoints, "outputWillGenerate = false", "Endpoints must deny output generate.");
        AssertContains(endpoints, "complianceClaimed = false", "Endpoints must deny compliance claim.");
        AssertNotContains(endpoints, "/api/lan-control", "Must not be under lan-control.");
        AssertNotContains(endpoints, "remote-pos", "Must not be Remote POS.");

        AssertContains(factory, "MapLocalOfficialDryRunPackageEndpoints", "Factory must map package endpoints.");

        AssertNotContains(hostCheckout, "IHostOfficialDryRunPackageService", "Host checkout must not reference package service.");
        AssertNotContains(hostCheckout, "IHostOfficialReportPreviewService", "Host checkout must not reference preview service.");
        AssertNotContains(hostCheckout, "IHostOfficialDryRunPostingPlanner", "Host checkout must not reference dry-run planner.");
        AssertNotContains(hostCheckout, "IHostOfficialNumberingService", "Host checkout must not reference numbering.");
        AssertNotContains(hostCheckout, "IHostOfficialIntegrityService", "Host checkout must not reference integrity.");
        AssertNotContains(hostCheckout, "OfficialDocumentSequenceAllocationService", "Host checkout must not use official allocator.");

        AssertNotContains(remoteCheckout, "IHostOfficialDryRunPackageService", "Remote POS must not reference package service.");
        AssertNotContains(remoteCheckout, "IHostOfficialReportPreviewService", "Remote POS must not reference preview service.");
        AssertNotContains(remoteCheckout, "IHostOfficialNumberingService", "Remote POS must not reference numbering.");
        AssertNotContains(remoteCheckout, "OfficialDocumentSequenceAllocationService", "Remote POS must not use official allocator.");

        AssertContains(spineContracts, "DryRunPersistence", "Spine must include dry-run persistence component.");
        AssertContains(spineContracts, "ReportPreviewPackage", "Spine must include report preview component.");
        AssertContains(spineContracts, "LegalSourceClosure", "Spine must include legal source closure component.");
        AssertContains(spineService, "DryRunPersistence", "Spine service must report persistence.");
        AssertContains(spineService, "ReportPreviewPackage", "Spine service must report preview package.");
        AssertContains(spineService, "LegalSourceClosure", "Spine service must report legal closure.");
        AssertContains(spineService, "LiveOfficialPostingAllowed: false", "Spine must keep live posting false.");

        AssertContains(readiness, "LiveOfficialCheckoutNotImplemented", "Live checkout blocker must remain.");
        AssertContains(readiness, "OfficialOutputEngineNotImplemented", "Output blocker must remain.");
        AssertContains(readiness, "LiveElectronicJournalBindingMissing", "EJ live bind blocker must remain.");
        AssertContains(readiness, "VoidRefundIntegrityPathIncomplete", "Void/refund live blocker must remain.");
        AssertContains(readiness, "UnresolvedAnnexFRetentionESales", "Annex F blocker must remain.");
        AssertContains(readiness, "InternalDryRunPackageAvailable", "Readiness must note internal package.");
        AssertContains(readiness, "SourceSaleReadOnlyAvailable", "Readiness must note source-sale read-only available.");

        AssertContains(setupPage, "Internal dry-run only", "UI must warn internal dry-run.");
        AssertContains(setupPage, "Not an official receipt or invoice", "UI must deny official receipt.");
        AssertContains(setupPage, "No official number is issued", "UI must deny number issuance.");
        AssertContains(setupPage, "Checkout is not connected", "UI must state checkout disconnected.");
        AssertContains(setupPage, "Build internal dry-run package", "UI must offer package build.");
        AssertContains(setupPage, "Build report preview", "UI must offer report preview.");
        AssertContains(setupPage, "Receipt / invoice preview", "UI must show receipt preview section.");
        AssertContains(setupPage, "X reading preview", "UI must show X reading section.");
        AssertContains(setupPage, "Z reading preview", "UI must show Z reading section.");
        AssertNotContains(setupPage, "Print official", "UI must not print official.");
        AssertNotContains(setupPage, "Issue official", "UI must not issue official.");
        AssertNotContains(setupPage, "Generate official", "UI must not generate official.");
        AssertNotContains(setupPage, "Go live", "UI must not go live.");
        AssertNotContains(setupPage, "Activate compliance", "UI must not activate compliance.");
        AssertNotContains(setupPage, "BIR ready", "UI must not claim BIR ready.");
        AssertNotContains(setupPage, "RMO", "WEB-A4: UI must not contain RMO token.");
        AssertNotContains(setupPage, "Crystal", "WEB-A3: UI must not contain Crystal token.");

        AssertContains(study, "GO IMPLEMENT", "Study must record GO.");
        AssertContains(study, "DEFER", "Study must record source-sale deferral.");
        AssertContains(handoff, "liveOfficialPostingAllowed remains false", "Handoff must document no live posting.");
        AssertContains(handoff, "v14", "Handoff must document migration v14.");
        AssertNotContains(handoff, "BIR compliant", "Handoff must not claim BIR compliant.");
        AssertNotContains(packageContracts, "accredited", "Package contracts must not use accredited.");
        AssertNotContains(previewContracts, "certified", "Preview contracts must not use certified.");
    }

    private static string FindProjectRoot(string startPath)
    {
        DirectoryInfo? current = new(startPath);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not find Salryn.sln from " + startPath);
    }

    private static string Read(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Required M8 file missing: " + path);
        }

        return File.ReadAllText(path);
    }

    private static void AssertContains(string haystack, string needle, string message)
    {
        if (!haystack.Contains(needle, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message + " Missing: " + needle);
        }
    }

    private static void AssertNotContains(string haystack, string needle, string message)
    {
        if (haystack.Contains(needle, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message + " Forbidden: " + needle);
        }
    }
}
