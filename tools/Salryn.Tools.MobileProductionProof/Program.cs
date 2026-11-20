using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Bootstrap;
using Salryn.Mobile.Runtime;

string root = Path.Combine(
    Path.GetTempPath(),
    "salryn-cafeyza-f1a-production-" + Guid.NewGuid().ToString("N"));

Directory.CreateDirectory(root);

try
{
    MobileProductionComposition composition = MobileProductionComposition.Create(root);
    await composition.InitializeDatabaseAsync();

    MobileProductionReadiness readiness = composition.InspectReadiness();

    Require(readiness.IsReady, "F1A production schema readiness failed.");
    Require(readiness.MaxMigrationVersion == SalrynDatabaseMigrationCatalog.SupportedMigrationVersion, "Fresh production DB must reach the current supported migration.");
    Require(readiness.Migration23Present, "Cafe reconciled kernel migration23 must be recorded.");
    Require(!readiness.HasP0ProofTables, "Production DB must not require P0 proof tables.");
    Require(readiness.RequiredSchemaPresent, "Production R1-R4 plus F1B/F1D sync tables/triggers/indexes are incomplete.");
    Require(
        string.Equals(
            Path.GetFullPath(readiness.DatabasePath),
            Path.GetFullPath(Path.Combine(root, "data", "salryn-cafe.db")),
            StringComparison.Ordinal),
        "Production DB path must be <root>/data/salryn-cafe.db.");

    AdminBootstrapSummary admin = RequireValue(
        composition.DatabaseInitialization.CreateFirstAdmin(new AdminBootstrapRequest(
            "Cafe Yza F1A",
            null,
            null,
            null,
            null,
            80,
            "f1a-admin",
            "F1A Admin",
            "StrongAdmin" + "123!")),
        "first-admin bootstrap");

    CashierSessionReadModel opened = RequireValue(
        await composition.CashierSessions.OpenAsync(new OpenCashierSessionRequest(
            admin.AdminUserId,
            Money.FromCents(12_345),
            "F1A production local authority proof")),
        "cashier session open");

    Require(opened.Status == CashierSessionStatus.Open, "Real production session did not open.");
    Require(opened.OpeningCash.Cents == 12_345, "Opening cash did not persist through real repository path.");

    ReceivableCustomer customer = RequireValue(
        await composition.AccountsReceivable.CreateCustomerAsync(
            new CreateReceivableCustomerRequest(
                "F1A Production Customer",
                null,
                null,
                "Host-independent production persistence proof",
                null,
                admin.AdminUserId)),
        "AR customer write");

    Require(customer.Id != Guid.Empty, "Real production AR customer write returned no ID.");
    Require(File.Exists(readiness.DatabasePath), "Production SQLite database file was not created.");

    // Reconstruct the complete mobile composition from the same app-private root.
    // Repository operations open/close SQLite connections per call, so this is the
    // process-reopen equivalent used by the host-independent proof.
    SqliteConnectionFactory.ClearAllPools();

    MobileProductionComposition reopened = MobileProductionComposition.Create(root);
    await reopened.InitializeDatabaseAsync();

    MobileProductionReadiness reopenedReadiness = reopened.InspectReadiness();
    Require(reopenedReadiness.IsReady, "Production DB was not ready after composition reopen.");

    CashierSessionReadModel? persistedSession = RequireValue(
        await reopened.CashierSessions.FindByIdAsync(opened.Id),
        "reopened session read");

    Require(persistedSession is not null, "Production session disappeared after reopen.");
    Require(persistedSession.Status == CashierSessionStatus.Open, "Session status changed across reopen.");
    Require(persistedSession.OpeningCash.Cents == 12_345, "Session money changed across reopen.");

    ReceivableCustomer? persistedCustomer = RequireValue(
        await reopened.AccountsReceivable.FindCustomerAsync(customer.Id),
        "reopened AR customer read");

    Require(persistedCustomer is not null, "Production AR customer disappeared after reopen.");
    Require(
        persistedCustomer.FullName == "F1A Production Customer",
        "Production AR customer data changed across reopen.");

    Console.WriteLine("F1A/F1B/F1D/C1/C2/C3/C4 PRODUCTION SQLITE MIGRATION " + SalrynDatabaseMigrationCatalog.SupportedMigrationVersion + ": PASS");
    Console.WriteLine("F1A RECONCILED MIGRATION 23: PASS");
    Console.WriteLine("F1A R1-R4 + F1B/F1D SYNC SCHEMA/TRIGGER AUTHORITY: PASS");
    Console.WriteLine("F1A REAL CASHIER SESSION WRITE: PASS");
    Console.WriteLine("F1A REAL AR BUSINESS WRITE: PASS");
    Console.WriteLine("F1A CLOSE/REOPEN PERSISTENCE: PASS");
    Console.WriteLine("F1A P0 STORE NOT PRODUCTION AUTHORITY: PASS");
    Console.WriteLine($"F1A ASSERTIONS EXECUTED: {ProofCounter.Assertions}");
    Console.WriteLine("F1A DATABASE: " + readiness.DatabasePath);
}
finally
{
    SqliteConnectionFactory.ClearAllPools();
    try
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
    catch (IOException)
    {
    }
    catch (UnauthorizedAccessException)
    {
    }
}

static T RequireValue<T>(Result<T> result, string area)
{
    ProofCounter.Assertions++;
    if (result.IsFailure)
    {
        throw new InvalidOperationException(
            area + " failed: " + result.ErrorDetail.Code + " / " + result.ErrorDetail.Message);
    }

    return result.Value;
}

static void Require(bool condition, string message)
{
    ProofCounter.Assertions++;
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

internal static class ProofCounter { public static int Assertions; }
