namespace Salryn.Tests;

/// <summary>
/// RMO-BIR-M11: guarded live-posting candidate dual-track hardening; always blocked before mutation.
/// </summary>
public static class RmoBirM11GuardedLivePostingCandidateChecks
{
    public static void Run()
    {
        string root = FindProjectRoot(Directory.GetCurrentDirectory());

        string study = Read(Path.Combine(root, "00-HANDOFF", "RMO_BIR_M11_GUARDED_LIVE_POSTING_CANDIDATE_STUDY.md"));
        string goStop = Read(Path.Combine(root, "00-HANDOFF", "RMO_BIR_M11_GO_STOP_DECISION.md"));
        string design = Read(Path.Combine(root, "00-HANDOFF", "RMO_BIR_M11_LIVE_POSTING_CANDIDATE_DESIGN.md"));
        string contracts = Read(Path.Combine(root, "src", "Salryn.Core", "Official", "OfficialLivePostingCandidateContracts.cs"));
        string iface = Read(Path.Combine(root, "src", "Salryn.Core", "Services", "IHostOfficialLivePostingCandidateService.cs"));
        string service = Read(Path.Combine(root, "src", "Salryn.Core", "Services", "HostOfficialLivePostingCandidateService.cs"));
        string sqliteService = Read(Path.Combine(root, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteHostOfficialLivePostingCandidateService.cs"));
        string endpoints = Read(Path.Combine(root, "src", "Salryn.Api.Local", "LocalOfficialLivePostingCandidateEndpoints.cs"));
        string factory = Read(Path.Combine(root, "src", "Salryn.Api.Local", "LocalApiApplicationFactory.cs"));
        string hostCheckout = Read(Path.Combine(root, "src", "Salryn.Api.Local", "Services", "HostOperatorCheckoutApiService.cs"));
        string remoteCheckout = Read(Path.Combine(root, "src", "Salryn.Api.Local", "RemotePos", "RemotePosCheckoutService.cs"));
        string setupPage = Read(Path.Combine(root, "src", "Salryn.App.Web", "src", "pages", "OfficialSetupPage.tsx"));
        string readiness = Read(Path.Combine(root, "src", "Salryn.Core", "Services", "HostOfficialReadinessService.cs"));
        string spine = Read(Path.Combine(root, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteHostOfficialSpineStatusService.cs"));
        string program = Read(Path.Combine(root, "tools", "Salryn.Tools.CoreSmoke", "Program.cs"));

        AssertContains(study, "Can a guarded candidate service be added without checkout binding?", "Study Q1 required.");
        AssertContains(study, "**Yes.**", "Study must allow candidate without checkout bind.");
        AssertContains(study, "Is EIS JSON schema complete?", "Study Q6 required.");
        AssertContains(study, "**No.**", "EIS schema must remain incomplete.");
        AssertContains(goStop, "Normal checkout binding", "GO/STOP checkout decision required.");
        AssertContains(goStop, "**STOP**", "Checkout STOP required.");
        AssertContains(design, "Atomic transaction design", "Design atomic section required.");
        AssertContains(design, "Why live posting remains blocked", "Design blocked section required.");
        AssertContains(design, "What must be true before checkout binding", "Design pre-bind section required.");

        AssertContains(contracts, "OfficialLivePostingCandidateTrack", "Track enum required.");
        AssertContains(contracts, "TraditionalCrmPos", "Traditional track required.");
        AssertContains(contracts, "EisInvoiceReceipt", "EIS track required.");
        AssertContains(contracts, "OfficialLivePostingCandidateStage", "Stage enum required.");
        AssertContains(contracts, "BlockBeforeMutation", "BlockBeforeMutation stage required.");
        AssertContains(contracts, "OfficialLivePostingCandidateAtomicPlan", "Atomic plan contract required.");
        AssertContains(contracts, "OfficialLivePostingCandidateFailurePlan", "Failure plan contract required.");
        AssertContains(contracts, "OfficialLivePostingCandidateOutputPlan", "Output plan contract required.");
        AssertContains(contracts, "OfficialLivePostingCandidateTransmissionPlan", "Transmission plan contract required.");
        AssertContains(contracts, "OfficialLivePostingCandidateFlags", "Flags contract required.");
        AssertContains(contracts, "EisWillTransmit", "EisWillTransmit flag required.");
        AssertContains(contracts, "CheckoutBound", "CheckoutBound flag required.");

        AssertContains(iface, "EvaluateAsync", "EvaluateAsync required.");
        AssertContains(iface, "GetStatusAsync", "GetStatusAsync required.");
        AssertContains(service, "Blocked: true", "Candidate must be blocked.");
        AssertContains(service, "LiveOfficialPostingAllowed: false", "Candidate must deny live posting.");
        AssertContains(service, "DisabledByDefault: true", "Candidate must be disabled by default.");
        AssertContains(service, "SequenceWillMutate: false", "Candidate must not mutate sequence.");
        AssertContains(service, "SalesWillMutate: false", "Candidate must not mutate sales.");
        AssertContains(service, "EjWillAppendLive: false", "Candidate must not append EJ live.");
        AssertContains(service, "OutputWillGenerate: false", "Candidate must not generate output.");
        AssertContains(service, "EisWillTransmit: false", "Candidate must not transmit EIS.");
        AssertContains(service, "ComplianceClaimed: false", "Candidate must not claim compliance.");
        AssertContains(service, "ProductionActivation: false", "Candidate must deny production activation.");
        AssertContains(service, "CheckoutBound: false", "Candidate must not bind checkout.");
        AssertContains(service, "EisJsonSchemaUnresolved", "EIS JSON schema unresolved blocker required.");
        AssertContains(service, "LiveCheckoutBindingNotImplemented", "Live checkout binding blocker required.");
        AssertContains(service, "OutputEngineMissing", "Output engine missing blocker required.");
        AssertContains(service, "ESalesLivePushNotImplemented", "eSales live push blocker required.");
        AssertContains(service, "TraditionalCrmPos", "Service must model traditional track.");
        AssertContains(service, "EisInvoiceReceipt", "Service must model EIS track.");
        AssertContains(service, "BlockBeforeMutation", "Service must terminal-block before mutation.");
        AssertNotContains(service, "AllocateNextAsync", "Candidate service must not allocate numbers.");
        AssertNotContains(service, "GeneratePdf", "Candidate must not generate PDF.");
        AssertNotContains(service, "SendToPrinter", "Candidate must not send to printer.");

        AssertContains(sqliteService, "SqliteHostOfficialSourceSaleReadOnlyAdapter", "Sqlite candidate may probe source-sale read-only.");
        AssertContains(sqliteService, "SqliteHostOfficialNumberingService", "Sqlite candidate may probe numbering preview.");
        AssertNotContains(sqliteService, "UPDATE bir_official_document_sequences", "Sqlite candidate must not mutate sequences.");
        AssertNotContains(sqliteService, "INSERT INTO bir_electronic_journal", "Sqlite candidate must not append EJ.");
        AssertNotContains(sqliteService, "INSERT INTO sales", "Sqlite candidate must not insert sales.");

        AssertContains(endpoints, "/api/official/live-posting-candidate/plan", "Plan endpoint required.");
        AssertContains(endpoints, "/api/official/live-posting-candidate/status", "Status endpoint required.");
        AssertContains(endpoints, "RequireLoopbackOperatorRequest", "Endpoints must be host-local.");
        AssertContains(endpoints, "liveOfficialPostingAllowed = false", "Endpoints must deny live posting.");
        AssertContains(endpoints, "checkoutBound = false", "Endpoints must deny checkout bind.");
        AssertContains(endpoints, "sequenceWillMutate = false", "Endpoints must deny sequence mutation.");
        AssertContains(endpoints, "salesWillMutate = false", "Endpoints must deny sales mutation.");
        AssertContains(endpoints, "ejWillAppendLive = false", "Endpoints must deny live EJ append.");
        AssertContains(endpoints, "outputWillGenerate = false", "Endpoints must deny output generate.");
        AssertContains(endpoints, "eisWillTransmit = false", "Endpoints must deny EIS transmit.");
        AssertContains(endpoints, "complianceClaimed = false", "Endpoints must deny compliance claim.");
        AssertContains(endpoints, "eisJsonSchemaResolved = false", "Endpoints must report EIS schema unresolved.");
        AssertNotContains(endpoints, "/api/lan-control", "Must not be under lan-control.");
        AssertNotContains(endpoints, "remote-pos", "Must not be Remote POS.");

        AssertContains(factory, "MapLocalOfficialLivePostingCandidateEndpoints", "Factory must map candidate endpoints.");

        AssertNotContains(hostCheckout, "IHostOfficialLivePostingCandidateService", "Host checkout must not reference candidate.");
        AssertNotContains(hostCheckout, "HostOfficialLivePostingCandidateService", "Host checkout must not call candidate.");
        AssertNotContains(hostCheckout, "IHostOfficialNumberingService", "Host checkout must not reference numbering.");
        AssertNotContains(hostCheckout, "OfficialDocumentSequenceAllocationService", "Host checkout must not use official allocator.");
        AssertNotContains(hostCheckout, "IHostOfficialReportPreviewService", "Host checkout must not reference report preview.");
        AssertNotContains(remoteCheckout, "IHostOfficialLivePostingCandidateService", "Remote POS must not reference candidate.");
        AssertNotContains(remoteCheckout, "OfficialDocumentSequenceAllocationService", "Remote POS must not use official allocator.");

        AssertContains(setupPage, "Live posting candidate: blocked", "UI must show candidate blocked.");
        AssertContains(setupPage, "Internal candidate plan only", "UI must say internal candidate plan only.");
        AssertContains(setupPage, "No official number is consumed", "UI must deny number consumption.");
        AssertContains(setupPage, "Checkout is not connected", "UI must state checkout not connected.");
        AssertContains(setupPage, "EIS JSON schema unresolved", "UI must note EIS schema unresolved.");
        AssertNotContains(setupPage, "Go live", "UI must not go live.");
        AssertNotContains(setupPage, "Activate compliance", "UI must not activate compliance.");
        AssertNotContains(setupPage, "Issue official", "UI must not issue official.");
        AssertNotContains(setupPage, "Print official", "UI must not print official.");
        AssertNotContains(setupPage, "BIR ready", "UI must not claim BIR ready.");
        AssertNotContains(setupPage, "RMO", "WEB-A4: UI must not contain RMO token.");
        AssertNotContains(setupPage, "Crystal", "WEB-A3: UI must not contain Crystal token.");
        AssertNotContains(setupPage, "printer", "WEB-A3: UI must not contain printer token.");

        AssertContains(readiness, "LivePostingCandidateBlocked", "Readiness must note candidate blocked.");
        AssertContains(spine, "LivePostingCandidateBlockedBeforeMutation", "Spine must note candidate blocked.");
        AssertContains(spine, "LiveOfficialPostingAllowed: false", "Spine must keep live posting false.");
        AssertContains(program, "rmo-bir-live-posting-candidate", "CoreSmoke must wire M11 check.");

        AssertNotContains(study, "Salryn is BIR ready", "Study must not claim BIR ready.");
        AssertNotContains(design, "production activation enabled", "Design must not enable production.");
        AssertNotContains(contracts, "using Crystal", "Contracts must not depend on Crystal.");
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
            throw new FileNotFoundException("Required M11 file missing: " + path);
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
