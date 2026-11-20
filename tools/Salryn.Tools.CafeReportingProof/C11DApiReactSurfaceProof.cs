using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Initialization;
using Salryn.Infrastructure.Data.Migrations;

namespace Salryn.Tools.CafeReportingProof;

internal static class C11DApiReactSurfaceProof
{
    public static void Run()
    {
        DirectoryInfo root = FindRepositoryRoot();

        string contract = Read(
            root,
            "src",
            "Salryn.Core",
            "Services",
            "ICafeManagementReportingService.cs");
        string cogs = Read(
            root,
            "src",
            "Salryn.Infrastructure",
            "Data",
            "Services",
            "SqliteCafeManagementReportingService.Cogs.cs");
        string service = Read(
            root,
            "src",
            "Salryn.Infrastructure",
            "Data",
            "Services",
            "SqliteCafeManagementReportingService.cs");
        string operations = Read(
            root,
            "src",
            "Salryn.Infrastructure",
            "Data",
            "Services",
            "SqliteCafeManagementReportingService.Operations.cs");
        string performance = Read(
            root,
            "src",
            "Salryn.Infrastructure",
            "Data",
            "Services",
            "SqliteCafeManagementReportingService.Performance.cs");
        string sync = Read(
            root,
            "src",
            "Salryn.Infrastructure",
            "Data",
            "Services",
            "SqliteCafeManagementReportingService.Sync.cs");
        string localComposition = Read(
            root,
            "src",
            "Salryn.Composition.Local",
            "SalrynLocalComposition.cs");
        string mobileComposition = Read(
            root,
            "src",
            "Salryn.Mobile.Runtime",
            "MobileProductionComposition.cs");
        string endpoints = Read(
            root,
            "src",
            "Salryn.Api.Local",
            "LocalOwnerReportsEndpoints.cs");
        string adapter = Read(
            root,
            "src",
            "Salryn.Api.Local",
            "Services",
            "OwnerReportsApiService.cs");
        string api = Read(
            root,
            "src",
            "Salryn.App.Web",
            "src",
            "api",
            "ownerReports.ts");
        string page = Read(
            root,
            "src",
            "Salryn.App.Web",
            "src",
            "pages",
            "OwnerReportsPage.tsx");
        string dashboard = Read(
            root,
            "src",
            "Salryn.App.Web",
            "src",
            "pages",
            "DashboardPage.tsx");

        ProofAssert.Contains(
            contract,
            "Result<CafeManagementReport> GetReport(",
            "C11 shared service contract missing.");

        ProofAssert.Contains(
            localComposition,
            "ICafeManagementReportingService CafeManagementReports",
            "Local composition must expose shared C11 reporting service.");
        ProofAssert.Contains(
            localComposition,
            "new SqliteCafeManagementReportingService(connectionFactory)",
            "Local composition must bind shared SQLite C11 implementation.");

        ProofAssert.Contains(
            mobileComposition,
            "ICafeManagementReportingService CafeManagementReports",
            "Mobile composition must expose shared C11 reporting service.");
        ProofAssert.Contains(
            mobileComposition,
            "new SqliteCafeManagementReportingService(connectionFactory)",
            "Mobile composition must bind shared SQLite C11 implementation.");

        ProofAssert.Contains(
            endpoints,
            "MapGet(\"/api/owner-reports/summary\"",
            "C11 must adapt the existing Owner Reports read route.");
        ProofAssert.Contains(
            endpoints,
            "LocalApiPermissions.ReportsView",
            "Owner Reports C11 API must require Reports.View.");
        ProofAssert.That(
            !endpoints.Contains("MapPost(", StringComparison.Ordinal) &&
            !endpoints.Contains("MapPut(", StringComparison.Ordinal) &&
            !endpoints.Contains("MapDelete(", StringComparison.Ordinal) &&
            !endpoints.Contains("roleName", StringComparison.OrdinalIgnoreCase),
            "C11 API must remain read-only and permission-based.");

        ProofAssert.Contains(
            adapter,
            "_composition.CafeManagementReports.GetReport(",
            "Owner Reports adapter must delegate C11 calculations to shared service.");

        ProofAssert.Contains(
            cogs,
            "purchase_receipt_lines",
            "C11 COGS must consume accepted C3 acquisition lines.");
        ProofAssert.Contains(
            cogs,
            "r.received_at_utc <= $at",
            "C11 historical acquisition basis must be bounded by consumption time.");
        ProofAssert.Contains(
            cogs,
            "MidpointRounding.AwayFromZero",
            "C11 line COGS must use midpoint-away-from-zero rounding.");
        ProofAssert.Contains(
            cogs,
            "NO_RECIPE_APPLIED",
            "C11 missing recipe evidence must be explicitly classified.");
        ProofAssert.Contains(
            cogs,
            "lack authoritative historical COGS evidence",
            "Revenue-bearing no-recipe Sale must downgrade cost coverage.");
        ProofAssert.That(
            !cogs.Contains("cost_price_cents", StringComparison.OrdinalIgnoreCase) &&
            !cogs.Contains("CostPrice", StringComparison.Ordinal),
            "Historical C11 COGS must never fall back to current Product.CostPrice.");

        string sharedReadModel =
            string.Join(
                "\n",
                new[]
                {
                    service,
                    cogs,
                    operations,
                    performance,
                    sync
                });

        ProofAssert.That(
            !sharedReadModel.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase) &&
            !sharedReadModel.Contains("UPDATE ", StringComparison.OrdinalIgnoreCase) &&
            !sharedReadModel.Contains("DELETE FROM", StringComparison.OrdinalIgnoreCase),
            "C11 shared reporting implementation must remain read-only and create no parallel truth.");

        ProofAssert.Contains(
            sync,
            "RemoteStateEvidenceMissing",
            "C11 sync completeness must explicitly represent insufficient remote evidence.");
        ProofAssert.Contains(
            sync,
            "CafeReportSyncCompletenessStates.Unknown",
            "C11 sync completeness must support Unknown.");
        ProofAssert.Contains(
            sync,
            "CafeReportSyncCompletenessStates.Incomplete",
            "C11 sync completeness must support Incomplete.");

        foreach (string snapshotAuthority in new[]
        {
            "product_name_snapshot",
            "variant_name_snapshot",
            "option_name_snapshot",
            "menu_revision_categories",
            "PerformanceBindingKey(",
            "\"Variant\"",
            "\"ModifierOption\""
        })
        {
            ProofAssert.Contains(
                performance,
                snapshotAuthority,
                "C11 performance reporting must retain immutable menu/order snapshot authority: " +
                snapshotAuthority);
        }

        foreach (string operationalSource in new[]
        {
            "cashier_session_reports",
            "cafe_bookings",
            "cafe_booking_revisions",
            "cafe_booking_deposit_facts",
            "cafe_delivery_fulfillments",
            "cafe_sale_fees"
        })
        {
            ProofAssert.Contains(
                operations,
                operationalSource,
                "C11 operational summary source missing: " +
                operationalSource);
        }

        ProofAssert.Contains(
            operations,
            "s.status=$completed",
            "Recognized Delivery fees must remain bound to completed Sale authority.");

        foreach (string type in new[]
        {
            "CafeReportCostCoverage",
            "CafeReportProfitabilitySummary",
            "CafeReportSyncCompleteness",
            "CafeReportPerformanceRow",
            "CafeReportRecipeCostRow"
        })
        {
            ProofAssert.Contains(
                api,
                type,
                "Typed Owner Reports API is missing C11 projection: " + type);
        }

        ProofAssert.Contains(
            dashboard,
            "queryFn: () => fetchOwnerReportsSummary()",
            "Dashboard must adapt the range-aware Owner Reports client to a zero-argument React Query queryFn.");
        ProofAssert.That(
            !dashboard.Contains(
                "queryFn: fetchOwnerReportsSummary,",
                StringComparison.Ordinal),
            "Dashboard must not pass the range-aware Owner Reports client directly as React Query queryFn.");
        foreach (string label in new[]
        {
            "Recognized net revenue",
            "Historical COGS",
            "Gross profit",
            "Operational contribution",
            "Synchronization completeness",
            "Historical cost coverage incomplete"
        })
        {
            ProofAssert.Contains(
                page,
                label,
                "Owner Reports React surface is missing C11 visibility: " + label);
        }

        ProofAssert.Contains(
            page,
            "Operational metric; not net income.",
            "OperationalContribution must not be presented as net income.");
        ProofAssert.Contains(
            page,
            "It is not the historical Cafe COGS authority.",
            "Legacy current-cost inventory estimate must be disclaimed from C11 COGS authority.");

        ProofAssert.That(
            !page.Contains("historicalCogsCents =", StringComparison.Ordinal) &&
            !page.Contains("recognizedNetRevenueCents =", StringComparison.Ordinal) &&
            !page.Contains("operationalContributionCents =", StringComparison.Ordinal),
            "React must not own C11 financial arithmetic.");

        IReadOnlyList<IDatabaseMigration> plan =
            CafeMigrationCatalog.CreateCanonicalCafeMigrations();

        ProofAssert.That(
            SalrynDatabaseMigrationCatalog.SupportedMigrationVersion == 41 &&
            plan.Max(x => x.Version) == 41 &&
            plan.Any(x => x.Version == 23) &&
            plan.Any(x => x.Version == 40) && plan.Any(x => x.Version == 41),
            "C11 current surface must preserve Yza39 and reconciled Cafe40/41.");

        ProofAssert.That(
            !sharedReadModel.Contains("Reports.Manage", StringComparison.Ordinal),
            "C11 must not invent a report mutation permission.");

        Console.WriteLine(
            "C11D API / REACT FINAL CANDIDATE GATE: PASS");
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        DirectoryInfo? current =
            new(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(
                    Path.Combine(
                        current.FullName,
                        "Salryn.sln")))
                return current;

            current = current.Parent;
        }

        current =
            new DirectoryInfo(
                Directory.GetCurrentDirectory());

        while (current is not null)
        {
            if (File.Exists(
                    Path.Combine(
                        current.FullName,
                        "Salryn.sln")))
                return current;

            current = current.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate Salryn repository root for C11 proof.");
    }

    private static string Read(
        DirectoryInfo root,
        params string[] parts)
    {
        string path =
            parts.Aggregate(
                root.FullName,
                Path.Combine);

        if (!File.Exists(path))
            throw new InvalidOperationException(
                "Required C11 source file missing: " + path);

        return File.ReadAllText(path);
    }
}
