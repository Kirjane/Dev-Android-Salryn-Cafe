namespace Salryn.Tests;

/// <summary>
/// RMO-BIR-M9 full closure: legal docs, source-sale read-only, internal reports, blocked live candidate.
/// </summary>
public static class RmoBirM9FullClosureChecks
{
    private static readonly string[] ForbiddenClaims =
    {
        "BIR compliant",
        "BIR ready",
        "RMO ready",
        "accredited",
        "certified"
    };

    public static void Run()
    {
        string root = FindProjectRoot(Directory.GetCurrentDirectory());

        string legal = Read(Path.Combine(root, "00-HANDOFF", "RMO_BIR_M9_LEGAL_SOURCE_CLOSURE.md"));
        string study = Read(Path.Combine(root, "00-HANDOFF", "RMO_BIR_M9_CODE_STUDY.md"));
        string goStop = Read(Path.Combine(root, "00-HANDOFF", "RMO_BIR_M9_GO_STOP_DECISION.md"));
        string liveDesign = Read(Path.Combine(root, "00-HANDOFF", "RMO_BIR_M9_LIVE_POSTING_DESIGN.md"));
        string outputDecision = Read(Path.Combine(root, "00-HANDOFF", "RMO_BIR_M9_OUTPUT_ENGINE_DECISION.md"));
        string qaPlan = Read(Path.Combine(root, "00-HANDOFF", "RMO_BIR_M9_REAL_QA_PLAN.md"));

        AssertContains(legal, "Blocks live posting", "Legal closure table required.");
        AssertContains(legal, "Live official posting must NOT be implemented", "Legal stop rule required.");
        AssertContains(legal, "Retention", "Retention topic required.");
        AssertContains(legal, "eSales", "eSales topic required.");
        AssertContains(goStop, "Legal closure", "GO/STOP legal decision required.");
        AssertContains(goStop, "Incomplete", "Legal closure must be incomplete for live.");
        AssertContains(goStop, "Source-sale read-only adapter", "Source-sale decision required.");
        AssertContains(goStop, "**GO**", "Source-sale GO required.");
        AssertContains(goStop, "Live checkout binding", "Live checkout decision required.");
        AssertContains(goStop, "**STOP**", "Live checkout STOP required.");
        AssertContains(liveDesign, "Allocate official number atomically", "Live posting design steps required.");
        AssertContains(liveDesign, "Always returns **blocked**", "Candidate always blocked.");
        AssertContains(outputDecision, "Crystal", "Output engine Crystal decision required.");
        AssertContains(outputDecision, "**STOP**", "Crystal STOP required.");
        AssertContains(qaPlan, "Migration QA", "QA migration section required.");
        AssertContains(qaPlan, "Source-sale read-only QA", "QA source-sale section required.");
        AssertContains(study, "source-sale read-only adapter", "Code study must address source-sale.");

        string sourceContracts = Read(Path.Combine(root, "src", "Salryn.Core", "Official", "OfficialSourceSaleReadOnlyContracts.cs"));
        string candidateContracts = Read(Path.Combine(root, "src", "Salryn.Core", "Official", "OfficialLivePostingCandidateContracts.cs"));
        string safety = Read(Path.Combine(root, "src", "Salryn.Core", "Official", "OfficialSafetyConstants.cs"));
        string sourceIface = Read(Path.Combine(root, "src", "Salryn.Core", "Services", "IHostOfficialSourceSaleReadOnlyAdapter.cs"));
        string candidateIface = Read(Path.Combine(root, "src", "Salryn.Core", "Services", "IHostOfficialLivePostingCandidateService.cs"));
        string candidateService = Read(Path.Combine(root, "src", "Salryn.Core", "Services", "HostOfficialLivePostingCandidateService.cs"));
        string sourceAdapter = Read(Path.Combine(root, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteHostOfficialSourceSaleReadOnlyAdapter.cs"));
        string packageService = Read(Path.Combine(root, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteHostOfficialDryRunPackageService.cs"));
        string packageIface = Read(Path.Combine(root, "src", "Salryn.Core", "Services", "IHostOfficialDryRunPackageService.cs"));
        string renderers = Read(Path.Combine(root, "src", "Salryn.Core", "Services", "OfficialInternalReportRenderers.cs"));
        string previewService = Read(Path.Combine(root, "src", "Salryn.Infrastructure", "Data", "Services", "HostOfficialReportPreviewService.cs"));
        string endpoints = Read(Path.Combine(root, "src", "Salryn.Api.Local", "LocalOfficialDryRunPackageEndpoints.cs"));
        string hostCheckout = Read(Path.Combine(root, "src", "Salryn.Api.Local", "Services", "HostOperatorCheckoutApiService.cs"));
        string remoteCheckout = Read(Path.Combine(root, "src", "Salryn.Api.Local", "RemotePos", "RemotePosCheckoutService.cs"));
        string setupPage = Read(Path.Combine(root, "src", "Salryn.App.Web", "src", "pages", "OfficialSetupPage.tsx"));
        string catalog = Read(Path.Combine(root, "src", "Salryn.Infrastructure", "Data", "SalrynDatabaseMigrationCatalog.cs"));
        string dbA2 = Read(Path.Combine(root, "tests", "Salryn.Tests", "DbA2DatabaseSafetyChecks.cs"));
        string program = Read(Path.Combine(root, "tools", "Salryn.Tools.CoreSmoke", "Program.cs"));

        AssertContains(sourceContracts, "OfficialSourceSaleReadOnlyRequest", "Source-sale request contract required.");
        AssertContains(sourceContracts, "OfficialSourceSaleReadOnlySnapshot", "Source-sale snapshot contract required.");
        AssertContains(sourceContracts, "SourceSaleReadOnly", "Source-sale flag required.");
        AssertContains(sourceContracts, "LiveOfficialPostingAllowed", "Source-sale must expose live posting flag.");
        AssertContains(sourceIface, "IHostOfficialSourceSaleReadOnlyAdapter", "Source-sale adapter interface required.");
        AssertContains(sourceAdapter, "FindSaleByIdAsync", "Adapter must read sale by id.");
        AssertContains(sourceAdapter, "SourceSaleReadOnly: true", "Adapter must set source-sale read-only.");
        AssertContains(sourceAdapter, "SequenceWillMutate: false", "Adapter must deny sequence mutation.");
        AssertContains(sourceAdapter, "SalesWillMutate: false", "Adapter must deny sales mutation.");
        AssertContains(sourceAdapter, "EjWillAppendLive: false", "Adapter must deny live EJ append.");
        AssertContains(sourceAdapter, "OutputWillGenerate: false", "Adapter must deny output generate.");
        AssertContains(sourceAdapter, "OfficialReceiptIssued: false", "Adapter must deny official receipt issued.");
        AssertContains(sourceAdapter, "ComplianceClaimed: false", "Adapter must deny compliance claim.");
        AssertNotContains(sourceAdapter, "UPDATE sales", "Adapter must not update sales.");
        AssertNotContains(sourceAdapter, "INSERT INTO sales", "Adapter must not insert sales.");
        AssertNotContains(sourceAdapter, "UPDATE bir_official_document_sequences", "Adapter must not mutate sequences.");
        AssertNotContains(sourceAdapter, "INSERT INTO bir_electronic_journal", "Adapter must not append EJ.");
        AssertNotContains(sourceAdapter, "AllocateNextAsync", "Adapter must not allocate official numbers.");

        AssertContains(packageIface, "CreateFromSaleAsync", "Package service must support from-sale create.");
        AssertContains(packageService, "CreateFromSaleAsync", "Package service must implement from-sale create.");
        AssertContains(packageService, "OperationalSaleReadOnly", "Package service must support operational source mode.");
        AssertContains(packageService, "ForcedNonLivePackageFlags", "Package service must force non-live flags.");
        AssertNotContains(packageService, "UPDATE bir_official_document_sequences", "Package service must not mutate sequences.");
        AssertNotContains(packageService, "INSERT INTO bir_electronic_journal", "Package service must not append EJ.");

        AssertContains(renderers, "OfficialInternalReportRenderer", "Internal report renderer required.");
        AssertContains(renderers, "OfficialInternalReceiptPreviewRenderer", "Receipt preview renderer required.");
        AssertContains(renderers, "OfficialInternalXReadingPreviewRenderer", "X reading renderer required.");
        AssertContains(renderers, "OfficialInternalZReadingPreviewRenderer", "Z reading renderer required.");
        AssertContains(renderers, "OfficialInternalJournalExportPreviewRenderer", "Journal export renderer required.");
        AssertContains(renderers, "OfficialInternalVoidRefundPreviewRenderer", "Void/refund renderer required.");
        AssertContains(renderers, "OfficialInternalSalesSummaryPreviewRenderer", "Sales summary renderer required.");
        AssertContains(safety, "INTERNAL DRY-RUN ONLY — NOT AN OFFICIAL RECEIPT OR INVOICE", "Watermark constant required.");
        AssertContains(renderers, "INTERNAL DRY-RUN ONLY — NOT AN OFFICIAL RECEIPT OR INVOICE", "Renderers must contain watermark text.");
        AssertContains(renderers, "OfficialSafetyConstants.InternalWatermark", "Renderers must reference safety watermark constant.");
        AssertContains(previewService, "OfficialInternalReceiptPreviewRenderer", "Preview service must use internal renderers.");
        AssertNotContains(renderers, "using Crystal", "Renderers must not import Crystal.");
        AssertNotContains(previewService, "using Crystal", "Preview service must not import Crystal.");
        AssertNotContains(renderers, "GeneratePdf", "Renderers must not generate PDF.");
        AssertNotContains(renderers, "SendToPrinter", "Renderers must not send to printer.");

        AssertContains(candidateContracts, "OfficialLivePostingCandidateResult", "Live posting candidate contracts required.");
        AssertContains(candidateIface, "IHostOfficialLivePostingCandidateService", "Candidate service interface required.");
        AssertContains(candidateService, "Blocked: true", "Candidate must be blocked.");
        AssertContains(candidateService, "LiveOfficialPostingAllowed: false", "Candidate must deny live posting.");
        AssertContains(candidateService, "ProductionActivation: false", "Candidate must deny production activation.");
        AssertContains(candidateService, "DisabledByDefault: true", "Candidate must be disabled by default.");
        AssertContains(candidateService, "SequenceWillMutate: false", "Candidate must not mutate sequence.");
        AssertContains(candidateService, "SalesWillMutate: false", "Candidate must not mutate sales.");
        AssertContains(candidateService, "EjWillAppendLive: false", "Candidate must not append EJ live.");
        AssertContains(candidateService, "OutputWillGenerate: false", "Candidate must not generate output.");
        AssertContains(candidateService, "OfficialReceiptIssued: false", "Candidate must not issue official receipt.");
        AssertContains(candidateService, "ComplianceClaimed: false", "Candidate must not claim compliance.");

        AssertContains(endpoints, "/api/official/source-sale/{saleId:guid}/dry-run-readonly", "Source-sale dry-run endpoint required.");
        AssertContains(endpoints, "/api/official/dry-run/packages/from-sale", "From-sale package endpoint required.");
        AssertContains(endpoints, "/api/official/internal-reports/{packageId:guid}", "Internal reports get endpoint required.");
        AssertContains(endpoints, "/api/official/internal-reports/preview", "Internal reports preview endpoint required.");
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

        AssertNotContains(hostCheckout, "IHostOfficialSourceSaleReadOnlyAdapter", "Host checkout must not reference source-sale adapter.");
        AssertNotContains(hostCheckout, "IHostOfficialLivePostingCandidateService", "Host checkout must not reference live posting candidate.");
        AssertNotContains(hostCheckout, "IHostOfficialDryRunPackageService", "Host checkout must not reference package service.");
        AssertNotContains(hostCheckout, "IHostOfficialReportPreviewService", "Host checkout must not reference preview service.");
        AssertNotContains(hostCheckout, "IHostOfficialNumberingService", "Host checkout must not reference numbering.");
        AssertNotContains(hostCheckout, "OfficialDocumentSequenceAllocationService", "Host checkout must not use official allocator.");
        AssertNotContains(hostCheckout, "HostOfficialLivePostingCandidateService", "Host checkout must not call live posting candidate.");

        AssertNotContains(remoteCheckout, "IHostOfficialSourceSaleReadOnlyAdapter", "Remote POS must not reference source-sale adapter.");
        AssertNotContains(remoteCheckout, "IHostOfficialLivePostingCandidateService", "Remote POS must not reference live posting candidate.");
        AssertNotContains(remoteCheckout, "IHostOfficialDryRunPackageService", "Remote POS must not reference package service.");
        AssertNotContains(remoteCheckout, "OfficialDocumentSequenceAllocationService", "Remote POS must not use official allocator.");

        AssertContains(setupPage, "Internal dry-run only", "UI must warn internal dry-run.");
        AssertContains(setupPage, "Not an official receipt or invoice", "UI must deny official receipt.");
        AssertContains(setupPage, "Source-sale read-only dry-run", "UI must include source-sale section.");
        AssertContains(setupPage, "Internal reports", "UI must include internal reports section.");
        AssertContains(setupPage, "Legal/source closure", "UI must include legal closure status.");
        AssertContains(setupPage, "Live posting blocked", "UI must state live posting blocked.");
        AssertContains(setupPage, "Internal preview only", "UI must state internal preview only.");
        AssertContains(setupPage, "Legal closure unresolved", "UI must state legal closure unresolved.");
        AssertNotContains(setupPage, "Print official", "UI must not print official.");
        AssertNotContains(setupPage, "Issue official", "UI must not issue official.");
        AssertNotContains(setupPage, "Generate official", "UI must not generate official.");
        AssertNotContains(setupPage, "Go live", "UI must not go live.");
        AssertNotContains(setupPage, "Activate compliance", "UI must not activate compliance.");
        AssertNotContains(setupPage, "BIR ready", "UI must not claim BIR ready.");
        AssertNotContains(setupPage, "RMO", "WEB-A4: UI must not contain RMO token.");
        AssertNotContains(setupPage, "Crystal", "WEB-A3: UI must not contain Crystal token.");
        // Allow denial phrase "BIR-compliant" (M4); block positive marketing forms.
        AssertNotContains(setupPage, "is compliant", "UI must not claim is compliant.");
        AssertNotContains(setupPage, "is accredited", "UI must not claim is accredited.");
        AssertNotContains(setupPage, "is certified", "UI must not claim is certified.");
        AssertNotContains(setupPage, "Salryn is accredited", "UI must not claim Salryn is accredited.");
        AssertNotContains(setupPage, "Salryn is certified", "UI must not claim Salryn is certified.");

        AssertContains(catalog, "SupportedMigrationVersion = 14", "Catalog must remain at v14 (no new migration).");
        AssertContains(dbA2, "futureVersion = 15", "DB-A2 future probe must remain supported+1.");
        AssertContains(program, "rmo-bir-full-closure", "CoreSmoke must wire full-closure check.");

        AssertNoForbiddenClaims(legal, "legal doc");
        AssertNoForbiddenClaims(goStop, "go/stop doc");
        AssertNoForbiddenClaims(liveDesign, "live design doc");
        AssertNoForbiddenClaims(outputDecision, "output decision doc");
        AssertNoForbiddenClaims(qaPlan, "QA plan doc");
        AssertNoForbiddenClaims(sourceContracts, "source contracts");
        AssertNoForbiddenClaims(candidateContracts, "candidate contracts");
        AssertNoForbiddenClaims(setupPage, "setup page");

        // Installer lane must remain untouched — no installer project edits in this branch scope.
        AssertNotContains(program, "KIRCH-INSTALLER", "CoreSmoke must not target installer lane.");
    }

    private static void AssertNoForbiddenClaims(string content, string label)
    {
        // Block positive product claims only. Explicit denials ("does not claim", "not …") are allowed.
        string[] positiveClaims =
        {
            "Salryn is BIR compliant",
            "Salryn is compliant",
            "Salryn is BIR ready",
            "Salryn is RMO ready",
            "Salryn is accredited",
            "Salryn is certified",
            "is BIR ready",
            "is RMO ready",
            "we are accredited",
            "we are certified",
            "product is compliant"
        };

        foreach (string claim in positiveClaims)
        {
            int index = 0;
            while (index < content.Length)
            {
                int found = content.IndexOf(claim, index, StringComparison.OrdinalIgnoreCase);
                if (found < 0)
                {
                    break;
                }

                string windowStart = content.Substring(Math.Max(0, found - 48), Math.Min(48, found));
                bool isDenial =
                    windowStart.Contains("not claim", StringComparison.OrdinalIgnoreCase) ||
                    windowStart.Contains("does not", StringComparison.OrdinalIgnoreCase) ||
                    windowStart.Contains("do not", StringComparison.OrdinalIgnoreCase) ||
                    windowStart.Contains("must not", StringComparison.OrdinalIgnoreCase) ||
                    windowStart.Contains("not make", StringComparison.OrdinalIgnoreCase) ||
                    windowStart.Contains("**not**", StringComparison.OrdinalIgnoreCase) ||
                    windowStart.Contains("not ", StringComparison.OrdinalIgnoreCase);

                if (!isDenial)
                {
                    throw new InvalidOperationException("Forbidden claim in " + label + ": " + claim);
                }

                index = found + claim.Length;
            }
        }

        _ = ForbiddenClaims;
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
            throw new FileNotFoundException("Required M9 file missing: " + path);
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
