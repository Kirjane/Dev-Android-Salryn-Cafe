namespace Salryn.Tests;

/// <summary>
/// RMO-BIR-M3: source guards for official contracts + host readiness.
/// Does not allocate numbers or claim compliance.
/// </summary>
public static class RmoBirM3OfficialReadinessChecks
{
    public static void Run()
    {
        string root = FindProjectRoot(Directory.GetCurrentDirectory());

        string contracts = Read(Path.Combine(root, "src", "Salryn.Core", "Official", "OfficialSaleDocumentContracts.cs"));
        string readinessTypes = Read(Path.Combine(root, "src", "Salryn.Core", "Official", "HostOfficialReadiness.cs"));
        string readinessService = Read(Path.Combine(root, "src", "Salryn.Core", "Services", "HostOfficialReadinessService.cs"));
        string endpoints = Read(Path.Combine(root, "src", "Salryn.Api.Local", "LocalOfficialReadinessEndpoints.cs"));
        string factory = Read(Path.Combine(root, "src", "Salryn.Api.Local", "LocalApiApplicationFactory.cs"));
        string hostCheckout = Read(Path.Combine(root, "src", "Salryn.Api.Local", "Services", "HostOperatorCheckoutApiService.cs"));
        string hostAllocator = Read(Path.Combine(root, "src", "Salryn.Api.Local", "Services", "HostOperatorReceiptNumberAllocator.cs"));
        string study = Read(Path.Combine(root, "00-HANDOFF", "RMO_BIR_M3_IMPLEMENTATION_STUDY.md"));
        string handoff = Read(Path.Combine(root, "00-HANDOFF", "RMO_BIR_M3_OFFICIAL_CONTRACTS_AND_READINESS.md"));

        AssertContains(contracts, "OfficialSaleDocumentContract", "Official sale document contract must exist.");
        AssertContains(contracts, "OperationalPosNumber", "Contracts must distinguish operational POS number.");
        AssertContains(contracts, "OfficialDocumentNumber", "Contracts must distinguish official document number.");
        AssertContains(contracts, "OfficialNumberingKind", "Contracts must define numbering kind enum.");
        AssertContains(contracts, "IsTrainingMode", "Contracts must include training marker.");
        AssertContains(contracts, "IsReprint", "Contracts must include reprint marker.");
        AssertContains(contracts, "IsVoided", "Contracts must include void marker.");
        AssertContains(contracts, "IsRefunded", "Contracts must include refund marker.");
        AssertContains(contracts, "ContractVersion", "Contracts must be versioned.");
        AssertNotContains(contracts, "BIR compliant", "Contracts must not claim BIR compliant.");
        AssertNotContains(contracts, "RMO ready", "Contracts must not claim RMO ready.");
        AssertNotContains(contracts, "is accredited", "Contracts must not claim accreditation.");
        AssertNotContains(contracts, "is certified", "Contracts must not claim certification.");
        AssertNotContains(contracts, "compliance ready", "Contracts must not claim compliance ready.");

        AssertContains(readinessTypes, "HostOfficialReadinessStatus", "Readiness status enum must exist.");
        AssertContains(readinessTypes, "NotReady", "Readiness must include NotReady.");
        AssertContains(readinessTypes, "Disabled", "Readiness must include Disabled.");
        AssertContains(readinessTypes, "LiveOfficialPostingAllowed", "Readiness must expose live posting flag.");

        AssertContains(readinessService, "LiveOfficialPostingAllowed: false", "Readiness must never allow live official posting in M3.");
        AssertContains(readinessService, "ViewReadiness", "Readiness must only allow view readiness.");
        AssertContains(readinessService, "ComplianceNotClaimed", "Readiness must warn that compliance is not claimed.");
        AssertContains(readinessService, "LiveOfficialCheckoutNotImplemented", "Readiness must block on live checkout not implemented.");
        AssertNotContains(readinessService, "current_number", "Readiness service must not mutate sequence counters.");
        AssertNotContains(readinessService, "SequenceAllocation", "Readiness service must not call sequence allocation.");

        AssertContains(endpoints, "/api/official/readiness", "Host readiness endpoint must exist.");
        AssertContains(endpoints, "RequireLoopbackOperatorRequest", "Readiness endpoint must be host-local only.");
        AssertContains(endpoints, "RequirePermission", "Readiness endpoint must require authenticated permission.");
        AssertContains(endpoints, "MapGet", "Readiness endpoint must be GET only.");
        AssertNotContains(endpoints, "MapPost", "Readiness endpoints must not expose POST mutators.");

        AssertContains(factory, "MapLocalOfficialReadinessEndpoints", "Factory must map readiness endpoints.");

        AssertNotContains(hostCheckout, "IOfficialDocumentSequence", "Host checkout must not use official sequence allocation in M3.");
        AssertContains(hostAllocator, "POS", "Host operational allocator remains non-official POS style.");

        AssertContains(study, "GO IMPLEMENT", "M3 study must record GO.");
        AssertContains(handoff, "not implemented", "Handoff must state official output not implemented.");
        AssertContains(handoff, "host-local", "Handoff must document host-local boundary.");
        AssertContains(handoff, "Crystal", "Handoff must keep Crystal deferred.");

        // No new schema migrations in M3
        string migrationsDir = Path.Combine(root, "src", "Salryn.Infrastructure", "Data", "Migrations");
        string[] migrationFiles = Directory.GetFiles(migrationsDir, "*M3*");
        if (migrationFiles.Length > 0)
        {
            throw new InvalidOperationException("M3 must not add schema migrations.");
        }

        // Installer lane not touched (no new tools/installer changes required — ensure no accidental installer edits in this task via path checks on key files existing only as before is N/A)
        AssertNotContains(endpoints, "remote-pos", "Official readiness must not be a Remote POS route.");
        AssertNotContains(endpoints, "/api/lan-control", "Official readiness must not live under lan-control.");
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
            throw new FileNotFoundException("Required RMO-BIR-M3 file missing: " + path);
        }

        return File.ReadAllText(path);
    }

    private static void AssertContains(string haystack, string needle, string message)
    {
        if (!haystack.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertNotContains(string haystack, string needle, string message)
    {
        if (haystack.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(message);
        }
    }
}
