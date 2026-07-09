namespace Salryn.Tests;

/// <summary>
/// RMO-BIR-M6: official EJ + void/refund integrity skeleton, disconnected from checkout.
/// </summary>
public static class RmoBirM6OfficialIntegrityChecks
{
    public static void Run()
    {
        string root = FindProjectRoot(Directory.GetCurrentDirectory());

        string journalContracts = Read(Path.Combine(root, "src", "Salryn.Core", "Official", "OfficialJournalContracts.cs"));
        string voidContracts = Read(Path.Combine(root, "src", "Salryn.Core", "Official", "OfficialVoidRefundIntegrityContracts.cs"));
        string integrityContracts = Read(Path.Combine(root, "src", "Salryn.Core", "Official", "OfficialIntegrityContracts.cs"));
        string iface = Read(Path.Combine(root, "src", "Salryn.Core", "Services", "IHostOfficialIntegrityService.cs"));
        string service = Read(Path.Combine(root, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteHostOfficialIntegrityService.cs"));
        string endpoints = Read(Path.Combine(root, "src", "Salryn.Api.Local", "LocalOfficialIntegrityEndpoints.cs"));
        string factory = Read(Path.Combine(root, "src", "Salryn.Api.Local", "LocalApiApplicationFactory.cs"));
        string hostCheckout = Read(Path.Combine(root, "src", "Salryn.Api.Local", "Services", "HostOperatorCheckoutApiService.cs"));
        string remoteCheckout = Read(Path.Combine(root, "src", "Salryn.Api.Local", "RemotePos", "RemotePosCheckoutService.cs"));
        string readiness = Read(Path.Combine(root, "src", "Salryn.Core", "Services", "HostOfficialReadinessService.cs"));
        string setupPage = Read(Path.Combine(root, "src", "Salryn.App.Web", "src", "pages", "OfficialSetupPage.tsx"));
        string study = Read(Path.Combine(root, "00-HANDOFF", "RMO_BIR_M6_INTEGRITY_STUDY.md"));
        string handoff = Read(Path.Combine(root, "00-HANDOFF", "RMO_BIR_M6_OFFICIAL_INTEGRITY_SKELETON.md"));

        AssertContains(journalContracts, "OfficialJournalStatusContract", "Journal status contract required.");
        AssertContains(journalContracts, "OfficialJournalEventType", "Journal event type enum required.");
        AssertContains(journalContracts, "OfficialJournalAppendPlanContract", "Journal append plan contract required.");
        AssertContains(journalContracts, "SalePosted", "SalePosted event type required.");
        AssertContains(journalContracts, "VoidPosted", "VoidPosted event type required.");
        AssertContains(journalContracts, "RefundPosted", "RefundPosted event type required.");

        AssertContains(voidContracts, "OfficialVoidRefundStatusContract", "Void/refund status contract required.");
        AssertContains(voidContracts, "OfficialVoidRefundReasonContract", "Void/refund reason contract required.");
        AssertContains(voidContracts, "OfficialVoidRefundValidationResult", "Void/refund validation result required.");

        AssertContains(integrityContracts, "OfficialIntegrityStatusContract", "Combined integrity status contract required.");
        AssertContains(integrityContracts, "LiveOfficialPostingAllowed", "Integrity status must expose live posting flag.");
        AssertContains(integrityContracts, "CheckoutIntegrated", "Integrity status must expose checkout integration flag.");

        AssertContains(iface, "GetStatusAsync", "Integrity service must expose status.");
        AssertNotContains(iface, "Append", "Integrity interface must not expose live append.");
        AssertNotContains(iface, "VoidSale", "Integrity interface must not expose void sale.");
        AssertNotContains(iface, "RefundSale", "Integrity interface must not expose refund sale.");

        AssertContains(service, "bir_electronic_journal_entries", "Integrity service must probe EJ tables.");
        AssertContains(service, "LiveOfficialPostingAllowed: false", "Integrity service must keep live posting false.");
        AssertContains(service, "CheckoutIntegrated: false", "Integrity service must keep checkout off.");
        AssertContains(service, "LiveBound: false", "Integrity service must keep EJ liveBound false.");
        AssertContains(service, "LiveIntegrated: false", "Integrity service must keep void/refund liveIntegrated false.");
        AssertContains(service, "SkeletonPresent: true", "Void/refund skeleton must be reported present.");
        AssertNotContains(service, "INSERT INTO bir_electronic_journal", "Integrity status must not append EJ rows.");
        AssertNotContains(service, "UPDATE sales", "Integrity status must not mutate sales.");
        AssertNotContains(service, "AllocateNextAsync", "Integrity status must not allocate official numbers.");

        AssertContains(endpoints, "/api/official/integrity/status", "Integrity status endpoint required.");
        AssertContains(endpoints, "RequireLoopbackOperatorRequest", "Integrity endpoint must be host-local.");
        AssertContains(endpoints, "liveOfficialPostingAllowed = false", "Endpoint must deny live posting.");
        AssertContains(endpoints, "checkoutIntegrated = false", "Endpoint must deny checkout integration.");
        AssertContains(endpoints, "remotePosOfficialAllowed = false", "Endpoint must deny remote POS official.");
        AssertContains(endpoints, "officialOutputImplemented = false", "Endpoint must deny official output.");
        AssertContains(endpoints, "complianceClaimed = false", "Endpoint must deny compliance claim.");
        AssertNotContains(endpoints, "/api/lan-control", "Integrity must not be under lan-control.");
        AssertNotContains(endpoints, "remote-pos", "Integrity must not be Remote POS.");
        AssertNotContains(endpoints, "MapPost", "Integrity M6 surface is GET-only.");

        AssertContains(factory, "MapLocalOfficialIntegrityEndpoints", "Factory must map integrity endpoints.");

        AssertNotContains(hostCheckout, "IHostOfficialIntegrityService", "Host checkout must not reference integrity service.");
        AssertNotContains(hostCheckout, "SqliteHostOfficialIntegrityService", "Host checkout must not use integrity service.");
        AssertNotContains(hostCheckout, "IHostOfficialNumberingService", "Host checkout must not reference numbering service.");
        AssertNotContains(hostCheckout, "OfficialDocumentSequenceAllocationService", "Host checkout must not use official sequence allocator.");
        AssertNotContains(hostCheckout, "bir_electronic_journal", "Host checkout must not write official journal.");

        AssertNotContains(remoteCheckout, "IHostOfficialIntegrityService", "Remote POS must not reference integrity service.");
        AssertNotContains(remoteCheckout, "SqliteHostOfficialIntegrityService", "Remote POS must not use integrity service.");
        AssertNotContains(remoteCheckout, "OfficialDocumentSequenceAllocationService", "Remote POS must not use official sequence allocator.");

        AssertContains(readiness, "LiveOfficialPostingAllowed: false", "Readiness must keep live posting false.");
        AssertContains(readiness, "LiveOfficialCheckoutNotImplemented", "Live checkout blocker must remain.");
        AssertContains(readiness, "OfficialOutputEngineNotImplemented", "Output engine blocker must remain.");
        AssertContains(readiness, "LiveElectronicJournalBindingMissing", "EJ live bind blocker must remain.");
        AssertContains(readiness, "VoidRefundIntegrityPathIncomplete", "Void/refund live blocker must remain.");
        AssertContains(readiness, "UnresolvedAnnexFRetentionESales", "Annex F blocker must remain.");
        AssertContains(readiness, "not live-bound", "EJ readiness text must state not live-bound.");
        AssertContains(readiness, "IntegritySkeletonStatusOnly", "Readiness must warn integrity is status-only.");

        AssertContains(setupPage, "Official integrity status", "UI must show integrity status card.");
        AssertContains(setupPage, "Not live-bound", "UI must state not live-bound.");
        AssertContains(setupPage, "No official posting", "UI must state no official posting.");
        AssertNotContains(setupPage, "Generate official receipt", "UI must not generate official receipts.");
        AssertNotContains(setupPage, "Generate Crystal", "UI must not offer Crystal.");
        AssertNotContains(setupPage, "Void sale", "UI must not offer void sale action.");
        AssertNotContains(setupPage, "Refund sale", "UI must not offer refund sale action.");

        AssertContains(study, "GO IMPLEMENT", "Study must record GO.");
        AssertContains(study, "No", "Study must discuss schema decision.");
        AssertContains(handoff, "liveOfficialPostingAllowed remains false", "Handoff must document no live posting.");
        AssertContains(handoff, "Crystal", "Handoff must keep Crystal deferred.");
        AssertNotContains(handoff, "BIR compliant", "Handoff must not claim BIR compliant.");
        AssertNotContains(handoff, "RMO ready", "Handoff must not claim RMO ready.");
        AssertNotContains(study, "BIR ready", "Study must not claim BIR ready.");

        AssertNotContains(journalContracts, "compliant", "Journal contracts must not use compliant label.");
        AssertNotContains(voidContracts, "accredited", "Void contracts must not use accredited label.");
        AssertNotContains(integrityContracts, "certified", "Integrity contracts must not use certified label.");

        string installerPackage = Path.Combine(root, "installer");
        // Installer lane files must remain untouched by this batch (no new installer paths in status).
        AssertTrue(!Directory.Exists(Path.Combine(root, "src", "Salryn.Installer")), "No installer project under src should appear for M6.");
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
            throw new FileNotFoundException("Required M6 file missing: " + path);
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

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
