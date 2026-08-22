namespace Salryn.Tests;

/// <summary>
/// RMO-BIR-M10: official source acquisition for seal / push / eSales dual-track.
/// Live posting remains blocked.
/// </summary>
public static class RmoBirM10OfficialSourceAcquisitionChecks
{
    public static void Run()
    {
        string root = FindProjectRoot(Directory.GetCurrentDirectory());

        string acquisition = Read(Path.Combine(root, "00-HANDOFF", "RMO_BIR_M10_OFFICIAL_SOURCE_ACQUISITION.md"));
        string delta = Read(Path.Combine(root, "00-HANDOFF", "RMO_BIR_M10_LEGAL_SOURCE_DELTA.md"));
        string goStop = Read(Path.Combine(root, "00-HANDOFF", "RMO_BIR_M10_GO_STOP_DECISION.md"));
        string readiness = Read(Path.Combine(root, "src", "Salryn.Core", "Services", "HostOfficialReadinessService.cs"));
        string spine = Read(Path.Combine(root, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteHostOfficialSpineStatusService.cs"));
        string candidate = Read(Path.Combine(root, "src", "Salryn.Core", "Services", "HostOfficialLivePostingCandidateService.cs"));
        string hostCheckout = Read(Path.Combine(root, "src", "Salryn.Api.Local", "Services", "HostOperatorCheckoutApiService.cs"));
        string program = Read(Path.Combine(root, "tools", "Salryn.Tools.CoreSmoke", "Program.cs"));

        AssertContains(acquisition, "Verification or Validation Seal", "Acquisition must document validation seal.");
        AssertContains(acquisition, "Quick Response (QR) Codes", "Seal examples must include QR codes from official text.");
        AssertContains(acquisition, "Push", "Acquisition must document push.");
        AssertContains(acquisition, "JSON", "Acquisition must document JSON format.");
        AssertContains(acquisition, "real-time", "Acquisition must document real-time/near real-time push.");
        AssertContains(acquisition, "EIS Unique ID", "Acquisition must document EIS Unique ID.");
        AssertContains(acquisition, "RR No. 8-2022", "Acquisition must cite RR 8-2022.");
        AssertContains(acquisition, "RMO No. 15-2018", "Acquisition must cite RMO 15-2018.");
        AssertContains(acquisition, "RMO No. 12-2012", "Acquisition must cite RMO 12-2012.");
        AssertContains(acquisition, "Dual-track", "Acquisition must document dual-track model.");
        AssertContains(acquisition, "Live official posting remains STOP", "Acquisition must keep live posting STOP.");
        AssertContains(acquisition, "claimed BIR compliant", "Acquisition must discuss BIR compliant claim denial.");
        AssertContains(acquisition, "Source-closed", "Acquisition must record source-closed rows.");

        AssertContains(delta, "Closed as source text", "Legal delta must record source-closed rows.");
        AssertContains(delta, "Still no", "Legal delta must keep live posting no.");
        AssertContains(delta, "schema still open", "Legal delta must note EIS schema open.");

        AssertContains(goStop, "Live official posting implementation", "GO/STOP must decide live posting.");
        AssertContains(goStop, "**STOP**", "Live posting STOP required.");
        AssertContains(goStop, "No migration", "Must not add migration in M10.");

        AssertContains(readiness, "OfficialSourceAcquisitionM10", "Readiness must note M10 source acquisition.");
        AssertContains(readiness, "Live posting stays blocked", "Readiness must keep live posting blocked.");
        AssertContains(readiness, "LiveOfficialCheckoutNotImplemented", "Live checkout blocker must remain.");
        AssertContains(spine, "Seal/push/eSales process source text acquired", "Spine must reflect source progress.");
        AssertContains(spine, "LiveOfficialPostingAllowed: false", "Spine must keep live posting false.");
        AssertContains(spine, "LegalSourceClosureIncomplete", "Spine legal incomplete blocker must remain.");

        AssertContains(candidate, "Blocked: true", "Live posting candidate must remain blocked.");
        AssertContains(candidate, "LiveOfficialPostingAllowed: false", "Candidate must deny live posting.");

        AssertNotContains(hostCheckout, "IHostOfficialLivePostingCandidateService", "Checkout must not reference live posting candidate.");
        AssertNotContains(hostCheckout, "OfficialDocumentSequenceAllocationService", "Checkout must not use official allocator.");

        AssertContains(program, "rmo-bir-source-acquisition", "CoreSmoke must wire M10 check.");

        AssertNotContains(acquisition, "Salryn is BIR ready", "Acquisition must not claim BIR ready.");
        AssertNotContains(delta, "Salryn is RMO ready", "Delta must not claim RMO ready.");
        AssertNotContains(goStop, "production activation enabled", "GO/STOP must not enable production.");
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
            throw new FileNotFoundException("Required M10 file missing: " + path);
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
