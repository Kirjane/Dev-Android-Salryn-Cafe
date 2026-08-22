namespace Salryn.Tests;

/// <summary>
/// RMO-BIR-M5: official numbering preview/validate skeleton, disconnected from checkout.
/// </summary>
public static class RmoBirM5OfficialNumberingChecks
{
    public static void Run()
    {
        string root = FindProjectRoot(Directory.GetCurrentDirectory());

        string contracts = Read(Path.Combine(root, "src", "Salryn.Core", "Official", "OfficialNumberingContracts.cs"));
        string numberingIface = Read(Path.Combine(root, "src", "Salryn.Core", "Services", "IHostOfficialNumberingService.cs"));
        string numberingService = Read(Path.Combine(root, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteHostOfficialNumberingService.cs"));
        string endpoints = Read(Path.Combine(root, "src", "Salryn.Api.Local", "LocalOfficialNumberingEndpoints.cs"));
        string factory = Read(Path.Combine(root, "src", "Salryn.Api.Local", "LocalApiApplicationFactory.cs"));
        string hostCheckout = Read(Path.Combine(root, "src", "Salryn.Api.Local", "Services", "HostOperatorCheckoutApiService.cs"));
        string remoteCheckout = Read(Path.Combine(root, "src", "Salryn.Api.Local", "RemotePos", "RemotePosCheckoutService.cs"));
        string remoteAllocator = Read(Path.Combine(root, "src", "Salryn.Api.Local", "RemotePos", "RemotePosReceiptNumberAllocator.cs"));
        string hostAllocator = Read(Path.Combine(root, "src", "Salryn.Api.Local", "Services", "HostOperatorReceiptNumberAllocator.cs"));
        string setupService = Read(Path.Combine(root, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteHostOfficialSetupService.cs"));
        string readiness = Read(Path.Combine(root, "src", "Salryn.Core", "Services", "HostOfficialReadinessService.cs"));
        string setupPage = Read(Path.Combine(root, "src", "Salryn.App.Web", "src", "pages", "OfficialSetupPage.tsx"));
        string study = Read(Path.Combine(root, "00-HANDOFF", "RMO_BIR_M5_NUMBERING_ALLOCATOR_STUDY.md"));
        string handoff = Read(Path.Combine(root, "00-HANDOFF", "RMO_BIR_M5_OFFICIAL_NUMBERING_ALLOCATOR.md"));

        AssertContains(contracts, "OfficialNumberingPreviewContract", "Preview contract must exist.");
        AssertContains(contracts, "OfficialNumberingValidationResult", "Validation contract must exist.");
        AssertContains(contracts, "OfficialNumberingSeriesStateContract", "Series state contract must exist.");
        AssertContains(contracts, "OfficialNumberingAllocationMode", "Allocation mode enum must exist.");
        AssertContains(contracts, "PreviewOnly", "PreviewOnly mode required.");
        AssertContains(contracts, "LiveOfficialBlocked", "LiveOfficialBlocked mode required.");
        AssertContains(contracts, "LiveOfficialPostingAllowed", "Preview must expose live posting flag.");

        AssertContains(numberingIface, "GetPreviewAsync", "Numbering service must expose preview.");
        AssertContains(numberingIface, "ValidateSeriesAsync", "Numbering service must expose validate.");
        AssertNotContains(numberingIface, "TryAllocateLiveOfficialAsync", "Host interface must not expose live allocate.");
        AssertNotContains(numberingIface, "TryAllocateInternalDryRunAsync", "M5 preferred scope: no dry-run allocate on interface.");

        AssertContains(numberingService, "bir_official_document_sequences", "Service must read document series.");
        AssertContains(numberingService, "PreviewMutatesSequence: false", "Preview must not mutate sequence.");
        AssertContains(numberingService, "LiveOfficialPostingAllowed: false", "Service must keep live posting false.");
        AssertContains(numberingService, "OfficialCheckoutAllocationEnabled: false", "Service must keep checkout allocation off.");
        AssertContains(numberingService, "CheckoutUsesAllocator: false", "Service must state checkout does not use allocator.");
        AssertNotContains(numberingService, "UPDATE bir_official_document_sequences", "Preview/validate must not update series.");
        AssertNotContains(numberingService, "AllocateNextAsync", "Host numbering must not call J21 mutator allocate.");

        AssertContains(endpoints, "/api/official/numbering/preview", "Preview endpoint required.");
        AssertContains(endpoints, "/api/official/numbering/validate", "Validate endpoint required.");
        AssertContains(endpoints, "RequireLoopbackOperatorRequest", "Numbering endpoints must be host-local.");
        AssertContains(endpoints, "liveOfficialPostingAllowed = false", "Endpoints must deny live posting.");
        AssertContains(endpoints, "officialCheckoutAllocationEnabled = false", "Endpoints must deny checkout allocation.");
        AssertContains(endpoints, "checkoutUsesAllocator = false", "Endpoints must deny checkout allocator use.");
        AssertNotContains(endpoints, "dry-run-allocate", "Dry-run allocate endpoint must not exist in M5.");
        AssertNotContains(endpoints, "/api/lan-control", "Numbering must not be under lan-control.");
        AssertNotContains(endpoints, "remote-pos", "Numbering must not be Remote POS.");

        AssertContains(factory, "MapLocalOfficialNumberingEndpoints", "Factory must map numbering endpoints.");

        AssertNotContains(hostCheckout, "IHostOfficialNumberingService", "Host checkout must not reference numbering service.");
        AssertNotContains(hostCheckout, "SqliteHostOfficialNumberingService", "Host checkout must not use numbering service.");
        AssertNotContains(hostCheckout, "OfficialDocumentSequenceAllocationService", "Host checkout must not use official sequence allocator.");
        AssertNotContains(hostCheckout, "bir_official_document_sequences", "Host checkout must not touch official sequences.");
        AssertContains(hostAllocator, "AllocateNext", "Host operational allocator must remain separate.");

        AssertNotContains(remoteCheckout, "IHostOfficialNumberingService", "Remote POS must not reference numbering service.");
        AssertNotContains(remoteCheckout, "OfficialDocumentSequenceAllocationService", "Remote POS must not use official sequence allocator.");
        AssertNotContains(remoteAllocator, "bir_official_document_sequences", "Remote POS allocator must stay non-official.");

        AssertContains(setupService, "request.StartNumber - 1", "Series setup must keep current = start - 1.");
        AssertContains(readiness, "LiveOfficialPostingAllowed: false", "Readiness must keep live posting false.");
        AssertContains(readiness, "NumberingPreviewOnly", "Readiness must warn numbering is preview-only.");
        AssertContains(readiness, "LiveOfficialCheckoutNotImplemented", "Product spine blockers must remain.");

        AssertContains(setupPage, "This preview does not issue an official number", "UI must state preview does not issue numbers.");
        AssertContains(setupPage, "Checkout is not connected to official numbering", "UI must state checkout disconnected.");
        AssertNotContains(setupPage, "Allocate official", "UI must not offer live allocate.");
        AssertNotContains(setupPage, "Generate official receipt", "UI must not generate official receipts.");
        AssertNotContains(setupPage, "Generate Crystal", "UI must not offer Crystal.");

        AssertContains(study, "GO IMPLEMENT", "Study must record GO.");
        AssertContains(study, "No migration", "Study must record no schema migration.");
        AssertContains(handoff, "liveOfficialPostingAllowed remains false", "Handoff must document no live posting.");
        AssertContains(handoff, "Crystal", "Handoff must keep Crystal deferred.");
        AssertNotContains(handoff, "BIR compliant", "Handoff must not claim BIR compliant.");
        AssertNotContains(handoff, "RMO ready", "Handoff must not claim RMO ready.");
        AssertNotContains(study, "BIR ready", "Study must not claim BIR ready.");
        AssertNotContains(contracts, "compliant", "Contracts must not use compliant label.");
        AssertNotContains(contracts, "accredited", "Contracts must not use accredited label.");
        AssertNotContains(contracts, "certified", "Contracts must not use certified label.");

        AssertNotContains(
            Read(Path.Combine(root, "src", "Salryn.App.Web", "src", "pages", "OfficialSetupPage.tsx")),
            "RMO ready",
            "Web UI must not claim RMO ready.");
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
            throw new FileNotFoundException("Required M5 file missing: " + path);
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
