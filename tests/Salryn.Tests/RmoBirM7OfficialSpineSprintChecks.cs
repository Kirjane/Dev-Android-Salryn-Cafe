namespace Salryn.Tests;

/// <summary>
/// RMO-BIR-M7: pre-live spine status, dry-run planner, report contracts; checkout disconnected.
/// </summary>
public static class RmoBirM7OfficialSpineSprintChecks
{
    public static void Run()
    {
        string root = FindProjectRoot(Directory.GetCurrentDirectory());

        string spineContracts = Read(Path.Combine(root, "src", "Salryn.Core", "Official", "OfficialSpineStatusContracts.cs"));
        string dryRunContracts = Read(Path.Combine(root, "src", "Salryn.Core", "Official", "OfficialDryRunPostingContracts.cs"));
        string reportContracts = Read(Path.Combine(root, "src", "Salryn.Core", "Official", "OfficialReportDataContracts.cs"));
        string spineIface = Read(Path.Combine(root, "src", "Salryn.Core", "Services", "IHostOfficialSpineStatusService.cs"));
        string dryRunIface = Read(Path.Combine(root, "src", "Salryn.Core", "Services", "IHostOfficialDryRunPostingPlanner.cs"));
        string spineService = Read(Path.Combine(root, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteHostOfficialSpineStatusService.cs"));
        string dryRunService = Read(Path.Combine(root, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteHostOfficialDryRunPostingPlanner.cs"));
        string endpoints = Read(Path.Combine(root, "src", "Salryn.Api.Local", "LocalOfficialSpineEndpoints.cs"));
        string factory = Read(Path.Combine(root, "src", "Salryn.Api.Local", "LocalApiApplicationFactory.cs"));
        string hostCheckout = Read(Path.Combine(root, "src", "Salryn.Api.Local", "Services", "HostOperatorCheckoutApiService.cs"));
        string remoteCheckout = Read(Path.Combine(root, "src", "Salryn.Api.Local", "RemotePos", "RemotePosCheckoutService.cs"));
        string readiness = Read(Path.Combine(root, "src", "Salryn.Core", "Services", "HostOfficialReadinessService.cs"));
        string setupPage = Read(Path.Combine(root, "src", "Salryn.App.Web", "src", "pages", "OfficialSetupPage.tsx"));
        string study = Read(Path.Combine(root, "00-HANDOFF", "RMO_BIR_M7_SPINE_SPRINT_STUDY.md"));
        string handoff = Read(Path.Combine(root, "00-HANDOFF", "RMO_BIR_M7_SPINE_SPRINT_AND_QUALITY.md"));

        AssertContains(spineContracts, "OfficialSpineStatusContract", "Spine status contract required.");
        AssertContains(spineContracts, "OfficialSpineComponentName", "Spine component enum required.");
        AssertContains(spineContracts, "DryRunPlanner", "DryRunPlanner component required.");
        AssertContains(spineContracts, "OfficialOutputEngine", "Output engine component required.");
        AssertContains(spineContracts, "LiveCheckoutBinding", "Live checkout component required.");
        AssertContains(spineContracts, "LiveOfficialPostingAllowed", "Spine must expose live posting flag.");

        AssertContains(dryRunContracts, "OfficialDryRunPostingPlan", "Dry-run plan contract required.");
        AssertContains(dryRunContracts, "OfficialDryRunNumberingPlan", "Dry-run numbering plan required.");
        AssertContains(dryRunContracts, "OfficialDryRunJournalPlan", "Dry-run journal plan required.");
        AssertContains(dryRunContracts, "OfficialDryRunVoidRefundPlan", "Dry-run void/refund plan required.");
        AssertContains(dryRunContracts, "OfficialDryRunReportPackagePlan", "Dry-run report package plan required.");
        AssertContains(dryRunContracts, "SequenceWillMutate", "Dry-run must declare sequence mutation flag.");
        AssertContains(dryRunContracts, "SalesWillMutate", "Dry-run must declare sales mutation flag.");
        AssertContains(dryRunContracts, "EjWillAppendLive", "Dry-run must declare EJ append flag.");
        AssertContains(dryRunContracts, "OutputWillGenerate", "Dry-run must declare output flag.");
        AssertContains(dryRunContracts, "SyntheticInternal", "Synthetic dry-run source required.");

        AssertContains(reportContracts, "OfficialReportDataPackageContract", "Report package contract required.");
        AssertContains(reportContracts, "OfficialReceiptReportDataContract", "Receipt report data contract required.");
        AssertContains(reportContracts, "CrystalSelected", "Report readiness must track Crystal selection.");
        AssertContains(reportContracts, "OfficialReceiptIssued", "Report readiness must track official receipt issuance.");

        AssertContains(spineIface, "GetStatusAsync", "Spine service must expose status.");
        AssertContains(dryRunIface, "BuildPlanAsync", "Dry-run planner must expose BuildPlanAsync.");
        AssertNotContains(dryRunIface, "Allocate", "Dry-run interface must not allocate.");
        AssertNotContains(dryRunIface, "Append", "Dry-run interface must not append EJ.");

        AssertContains(spineService, "LiveOfficialPostingAllowed: false", "Spine service must keep live posting false.");
        AssertContains(spineService, "OfficialOutputImplemented: false", "Spine service must keep output false.");
        AssertContains(spineService, "ComplianceClaimed: false", "Spine service must keep compliance false.");
        AssertContains(spineService, "DryRunReady", "Spine must mark dry-run planner ready.");
        AssertContains(spineService, "OfficialSpineReadinessLevel.Missing", "Output/live checkout must remain missing.");

        AssertContains(dryRunService, "SequenceWillMutate: false", "Dry-run must not mutate sequence.");
        AssertContains(dryRunService, "SalesWillMutate: false", "Dry-run must not mutate sales.");
        AssertContains(dryRunService, "EjWillAppendLive: false", "Dry-run must not append live EJ.");
        AssertContains(dryRunService, "OutputWillGenerate: false", "Dry-run must not generate output.");
        AssertContains(dryRunService, "LiveOfficialPostingAllowed: false", "Dry-run must keep live posting false.");
        AssertContains(dryRunService, "GetPreviewAsync", "Dry-run must integrate numbering preview.");
        AssertContains(dryRunService, "GetStatusAsync", "Dry-run must integrate integrity status.");
        AssertNotContains(dryRunService, "UPDATE bir_official_document_sequences", "Dry-run must not update sequences.");
        AssertNotContains(dryRunService, "INSERT INTO bir_electronic_journal", "Dry-run must not append EJ.");
        AssertNotContains(dryRunService, "AllocateNextAsync", "Dry-run must not call official allocate.");

        AssertContains(endpoints, "/api/official/spine/status", "Spine status endpoint required.");
        AssertContains(endpoints, "/api/official/dry-run/posting-plan", "Dry-run plan endpoint required.");
        AssertContains(endpoints, "RequireLoopbackOperatorRequest", "Spine endpoints must be host-local.");
        AssertContains(endpoints, "liveOfficialPostingAllowed = false", "Endpoints must deny live posting.");
        AssertContains(endpoints, "officialCheckoutAllocationEnabled = false", "Endpoints must deny checkout allocation.");
        AssertContains(endpoints, "checkoutUsesAllocator = false", "Endpoints must deny checkout allocator.");
        AssertContains(endpoints, "sequenceWillMutate = false", "Endpoints must deny sequence mutation.");
        AssertContains(endpoints, "salesWillMutate = false", "Endpoints must deny sales mutation.");
        AssertContains(endpoints, "ejWillAppendLive = false", "Endpoints must deny live EJ append.");
        AssertContains(endpoints, "outputWillGenerate = false", "Endpoints must deny output generation.");
        AssertContains(endpoints, "complianceClaimed = false", "Endpoints must deny compliance claim.");
        AssertNotContains(endpoints, "/api/lan-control", "Spine must not be under lan-control.");
        AssertNotContains(endpoints, "remote-pos", "Spine must not be Remote POS.");

        AssertContains(factory, "MapLocalOfficialSpineEndpoints", "Factory must map spine endpoints.");

        AssertNotContains(hostCheckout, "IHostOfficialSpineStatusService", "Host checkout must not reference spine service.");
        AssertNotContains(hostCheckout, "IHostOfficialDryRunPostingPlanner", "Host checkout must not reference dry-run planner.");
        AssertNotContains(hostCheckout, "IHostOfficialNumberingService", "Host checkout must not reference numbering service.");
        AssertNotContains(hostCheckout, "IHostOfficialIntegrityService", "Host checkout must not reference integrity service.");
        AssertNotContains(hostCheckout, "OfficialReportDataPackageContract", "Host checkout must not reference report contracts.");
        AssertNotContains(hostCheckout, "OfficialDocumentSequenceAllocationService", "Host checkout must not use official allocator.");
        AssertNotContains(hostCheckout, "bir_electronic_journal", "Host checkout must not write official journal.");

        AssertNotContains(remoteCheckout, "IHostOfficialSpineStatusService", "Remote POS must not reference spine service.");
        AssertNotContains(remoteCheckout, "IHostOfficialDryRunPostingPlanner", "Remote POS must not reference dry-run planner.");
        AssertNotContains(remoteCheckout, "IHostOfficialNumberingService", "Remote POS must not reference numbering service.");
        AssertNotContains(remoteCheckout, "IHostOfficialIntegrityService", "Remote POS must not reference integrity service.");
        AssertNotContains(remoteCheckout, "OfficialDocumentSequenceAllocationService", "Remote POS must not use official allocator.");

        AssertContains(readiness, "LiveOfficialPostingAllowed: false", "Readiness must keep live posting false.");
        AssertContains(readiness, "LiveOfficialCheckoutNotImplemented", "Live checkout blocker must remain.");
        AssertContains(readiness, "OfficialOutputEngineNotImplemented", "Output engine blocker must remain.");
        AssertContains(readiness, "LiveElectronicJournalBindingMissing", "EJ live bind blocker must remain.");
        AssertContains(readiness, "VoidRefundIntegrityPathIncomplete", "Void/refund live blocker must remain.");
        AssertContains(readiness, "UnresolvedAnnexFRetentionESales", "Annex F blocker must remain.");
        AssertContains(readiness, "DryRunPlannerAvailableNotLiveBound", "Readiness must note dry-run planner availability.");
        AssertContains(readiness, "ReportDataContractsDefinedNoEngine", "Readiness must note report contracts without engine.");

        AssertContains(setupPage, "Internal dry-run only", "UI must state internal dry-run only.");
        AssertContains(setupPage, "No official receipt is issued", "UI must state no official receipt is issued.");
        AssertContains(setupPage, "Checkout is not connected to official numbering", "UI must state checkout disconnected.");
        AssertContains(setupPage, "No compliance claim is made", "UI must deny compliance claim.");
        AssertContains(setupPage, "Build dry-run plan", "UI must offer dry-run plan button.");
        AssertContains(setupPage, "Official spine status", "UI must show spine status card.");
        AssertNotContains(setupPage, "Print official", "UI must not print official.");
        AssertNotContains(setupPage, "Generate official", "UI must not generate official.");
        AssertNotContains(setupPage, "Issue official", "UI must not issue official.");
        AssertNotContains(setupPage, "Activate compliance", "UI must not activate compliance.");
        AssertNotContains(setupPage, "BIR ready", "UI must not claim BIR ready.");
        AssertNotContains(setupPage, "RMO", "WEB-A4: UI must not contain RMO token.");
        AssertNotContains(setupPage, "Generate Crystal", "UI must not offer Crystal.");

        AssertContains(study, "GO IMPLEMENT", "Study must record GO.");
        AssertContains(study, "No migration", "Study must record no migration.");
        AssertContains(handoff, "liveOfficialPostingAllowed remains false", "Handoff must document no live posting.");
        AssertContains(handoff, "Crystal", "Handoff must keep Crystal deferred.");
        AssertNotContains(handoff, "BIR compliant", "Handoff must not claim BIR compliant.");
        AssertNotContains(study, "BIR ready", "Study must not claim BIR ready.");

        AssertNotContains(spineContracts, "compliant", "Spine contracts must not use compliant label.");
        AssertNotContains(dryRunContracts, "accredited", "Dry-run contracts must not use accredited label.");
        AssertNotContains(reportContracts, "certified", "Report contracts must not use certified label.");
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
            throw new FileNotFoundException("Required M7 file missing: " + path);
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
