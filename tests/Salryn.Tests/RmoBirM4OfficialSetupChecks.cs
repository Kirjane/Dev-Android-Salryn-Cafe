namespace Salryn.Tests;

/// <summary>
/// RMO-BIR-M4-BATCH: official setup APIs, schema, UI, readiness wiring.
/// </summary>
public static class RmoBirM4OfficialSetupChecks
{
    public static void Run()
    {
        string root = FindProjectRoot(Directory.GetCurrentDirectory());

        string setupContracts = Read(Path.Combine(root, "src", "Salryn.Core", "Official", "OfficialSetupContracts.cs"));
        string setupService = Read(Path.Combine(root, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteHostOfficialSetupService.cs"));
        string setupEndpoints = Read(Path.Combine(root, "src", "Salryn.Api.Local", "LocalOfficialSetupEndpoints.cs"));
        string readinessService = Read(Path.Combine(root, "src", "Salryn.Core", "Services", "HostOfficialReadinessService.cs"));
        string readinessTypes = Read(Path.Combine(root, "src", "Salryn.Core", "Official", "HostOfficialReadiness.cs"));
        string factory = Read(Path.Combine(root, "src", "Salryn.Api.Local", "LocalApiApplicationFactory.cs"));
        string migration = Read(Path.Combine(root, "src", "Salryn.Infrastructure", "Data", "Migrations", "OfficialReceiptProfileSchemaMigration.cs"));
        string initFactory = Read(Path.Combine(root, "src", "Salryn.Infrastructure", "Data", "Initialization", "DefaultDatabaseInitializationFactory.cs"));
        string setupPage = Read(Path.Combine(root, "src", "Salryn.App.Web", "src", "pages", "OfficialSetupPage.tsx"));
        string navigation = Read(Path.Combine(root, "src", "Salryn.App.Web", "src", "navigation.ts"));
        string hostCheckout = Read(Path.Combine(root, "src", "Salryn.Api.Local", "Services", "HostOperatorCheckoutApiService.cs"));
        string handoff = Read(Path.Combine(root, "00-HANDOFF", "RMO_BIR_M4_BATCH_OFFICIAL_SETUP.md"));

        AssertContains(setupContracts, "OfficialSetupBusinessProfileContract", "Business setup contract must exist.");
        AssertContains(setupContracts, "OfficialSetupDocumentSeriesContract", "Series setup contract must exist.");
        AssertContains(setupContracts, "LiveOfficialPostingAllowed", "Setup snapshot must expose live posting flag.");

        AssertContains(setupService, "NOT_ACCREDITED", "Setup must force NOT_ACCREDITED.");
        AssertContains(setupService, "official_output_enabled = 0", "Setup must force official_output_enabled off.");
        AssertContains(setupService, "StartNumber - 1", "Series setup must not treat save as sale allocation (current = start-1).");
        AssertNotContains(setupService, "HostOperatorCheckout", "Setup must not call checkout.");

        AssertContains(setupEndpoints, "/api/official/setup", "Setup GET must exist.");
        AssertContains(setupEndpoints, "/api/official/setup/business", "Business PUT must exist.");
        AssertContains(setupEndpoints, "/api/official/setup/machine", "Machine PUT must exist.");
        AssertContains(setupEndpoints, "/api/official/setup/receipt", "Receipt PUT must exist.");
        AssertContains(setupEndpoints, "/api/official/setup/series", "Series PUT must exist.");
        AssertContains(setupEndpoints, "RequireLoopbackOperatorRequest", "Setup endpoints must be host-local.");
        AssertContains(setupEndpoints, "liveOfficialPostingAllowed = false", "Setup responses must deny live posting.");
        AssertNotContains(setupEndpoints, "/api/lan-control", "Setup must not be under lan-control.");
        AssertNotContains(setupEndpoints, "remote-pos", "Setup must not be Remote POS.");

        AssertContains(factory, "MapLocalOfficialSetupEndpoints", "Factory must map setup endpoints.");

        AssertContains(readinessService, "ConfiguredForInternalDryRun", "Readiness must support ConfiguredForInternalDryRun.");
        AssertContains(readinessService, "LiveOfficialPostingAllowed: false", "Readiness must keep live posting false.");
        AssertContains(readinessService, "LiveOfficialCheckoutNotImplemented", "Readiness must keep product spine blockers.");
        AssertContains(readinessService, "OfficialOutputEngineNotImplemented", "Readiness must block missing output engine.");
        AssertContains(readinessService, "LiveElectronicJournalBindingMissing", "Readiness must block missing EJ bind.");
        AssertContains(readinessService, "VoidRefundIntegrityPathIncomplete", "Readiness must block void/refund gap.");
        AssertContains(readinessService, "UnresolvedAnnexFRetentionESales", "Readiness must keep Annex F unresolved blocker.");
        AssertContains(readinessTypes, "ConfiguredForInternalDryRun", "Status enum must include ConfiguredForInternalDryRun.");
        AssertContains(readinessTypes, "SetupIncomplete", "Status enum must include SetupIncomplete.");

        AssertContains(migration, "official_receipt_profiles", "Receipt profile table migration required.");
        AssertContains(migration, "Version => 13", "Migration version 13 expected.");
        AssertContains(initFactory, "OfficialReceiptProfileSchemaMigration", "Migration must be registered.");
        AssertNotContains(migration, "CREATE TABLE sales", "Setup migration must not alter sales tables.");

        string migrationCatalog = Read(Path.Combine(root, "src", "Salryn.Infrastructure", "Data", "SalrynDatabaseMigrationCatalog.cs"));
        AssertContains(migrationCatalog, "SupportedMigrationVersion = 18", "Backup catalog must accept current max migration version.");
        AssertContains(migrationCatalog, "official_receipt_profiles", "Backup critical-table catalog must include official_receipt_profiles.");

        AssertContains(setupPage, "Not compliance-ready", "Setup page must warn not compliance-ready.");
        AssertContains(setupPage, "Official output is not implemented", "Setup page must state official output not implemented.");
        AssertContains(setupPage, "BIR-compliant", "Setup page must deny BIR-compliant claim.");
        AssertContains(navigation, "official-setup", "Nav must include official-setup.");
        AssertNotContains(setupPage, "Generate official receipt", "Setup page must not generate official receipts.");
        AssertNotContains(setupPage, "Print official", "Setup page must not print official.");
        AssertNotContains(setupPage, "Generate Crystal", "Setup page must not offer report-engine generation.");

        AssertNotContains(hostCheckout, "SqliteHostOfficialSetupService", "Checkout must not depend on setup service.");
        AssertNotContains(hostCheckout, "SaveSeriesAsync", "Checkout must not save/allocate series via setup.");

        AssertContains(handoff, "liveOfficialPostingAllowed remains false", "Handoff must document no live posting.");
        AssertContains(handoff, "Crystal", "Handoff must keep Crystal deferred.");
        AssertNotContains(handoff, "BIR compliant", "Handoff must not claim BIR compliant.");
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
            throw new FileNotFoundException("Required M4 file missing: " + path);
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
