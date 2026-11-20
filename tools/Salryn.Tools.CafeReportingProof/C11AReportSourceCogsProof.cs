using Salryn.Core.Models;

namespace Salryn.Tools.CafeReportingProof;

internal static class C11AReportSourceCogsProof
{
    public static void Run(string root)
    {
        using C11ProofFixture fixture =
            C11ProofFixture.Create(root);

        fixture.SeedSyncComplete();

        fixture.SeedAcquisition(
            "purchase-1",
            10_000,
            1_000,
            C11ProofFixture.Day.AddHours(10));

        fixture.SeedAcquisition(
            "purchase-correction",
            -2_000,
            -100,
            C11ProofFixture.Day.AddHours(11),
            "purchase-1");

        Guid completeSale =
            fixture.SeedSale(
                "complete",
                1_000,
                C11ProofFixture.Day.AddHours(12),
                appliedRecipe: true,
                consumptionX1000: 2_000);

        CafeManagementReport first =
            fixture.ReadReport();

        ProofAssert.That(
            first.Sales.CompletedSaleCount == 1 &&
            first.Sales.RecognizedNetRevenueCents == 1_000 &&
            first.CostCoverage.State ==
                CafeReportCostCoverageStates.Complete &&
            first.CostCoverage.ConsumptionLineCount == 1 &&
            first.CostCoverage.CostedConsumptionLineCount == 1 &&
            first.CostCoverage.UncostedConsumptionLineCount == 0 &&
            first.Profitability.HistoricalCogsCents == 225 &&
            first.Profitability.GrossProfitCents == 775 &&
            first.Profitability.Authority ==
                CafeReportProfitabilityAuthorityStates.Authoritative,
            "C11 historical COGS must use signed acquisition basis 8000/900 at exact consumption time (2000 => 225 cents).");

        ProofAssert.That(
            first.Performance.Any(x =>
                x.DimensionType == "Product" &&
                x.DimensionId == fixture.ProductId.ToString("D") &&
                x.HistoricalCogsCents == 225 &&
                x.CostCoverage ==
                    CafeReportCostCoverageStates.Complete),
            "C11 product performance must carry historical Sale COGS.");

        fixture.SeedAcquisition(
            "purchase-later",
            8_000,
            1_600,
            C11ProofFixture.Day.AddHours(14));

        CafeManagementReport afterLaterPurchase =
            fixture.ReadReport();

        ProofAssert.That(
            afterLaterPurchase.Profitability.HistoricalCogsCents == 225 &&
            afterLaterPurchase.Profitability.GrossProfitCents == 775,
            "Later purchase evidence must not rewrite historical Sale COGS.");

        Guid noRecipeSale =
            fixture.SeedSale(
                "no-recipe",
                500,
                C11ProofFixture.Day.AddHours(13),
                appliedRecipe: false);

        CafeManagementReport incomplete =
            fixture.ReadReport();

        ProofAssert.That(
            incomplete.Sales.RecognizedNetRevenueCents == 1_500 &&
            incomplete.Profitability.HistoricalCogsCents == 225 &&
            incomplete.CostCoverage.State ==
                CafeReportCostCoverageStates.Incomplete &&
            incomplete.CostCoverage.CostIncompleteSaleCount == 1 &&
            incomplete.CostCoverage.AffectedRevenueCents == 500 &&
            incomplete.CostCoverage.AffectedSaleIds.Contains(noRecipeSale) &&
            !incomplete.CostCoverage.AffectedSaleIds.Contains(completeSale) &&
            incomplete.Profitability.Authority ==
                CafeReportProfitabilityAuthorityStates.ProvisionalCostIncomplete &&
            incomplete.Profitability.AuthorityReasons.Any(x =>
                x.Contains(
                    "NO_RECIPE_APPLIED",
                    StringComparison.Ordinal)),
            "Revenue-bearing Sale without recipe/cost evidence must stay visible while downgrading cost/profit authority.");

        var denied =
            fixture.Service.GetReport(
                new(
                    fixture.StoreId,
                    fixture.CashierUserId,
                    C11ProofFixture.Day.AddHours(8),
                    C11ProofFixture.Day.AddHours(20),
                    C11ProofFixture.Day.AddHours(21)));

        ProofAssert.That(
            denied.IsFailure &&
            denied.ErrorDetail.Code ==
                "CafeReporting.ReportsViewRequired",
            "C11 shared reporting authority must require Reports.View.");

        using (C11ProofFixture lineIntegrity =
               C11ProofFixture.Create(Path.Combine(root, "line-integrity")))
        {
            lineIntegrity.SeedSyncComplete();
            lineIntegrity.SeedAcquisition(
                "basis",
                10_000,
                1_000,
                C11ProofFixture.Day.AddHours(10));

            Guid missingLineSale =
                lineIntegrity.SeedSale(
                    "missing-consumption-line",
                    700,
                    C11ProofFixture.Day.AddHours(12),
                    appliedRecipe: true,
                    consumptionX1000: 2_000,
                    retainConsumptionLine: false);

            CafeManagementReport lineIntegrityReport =
                lineIntegrity.ReadReport();

            ProofAssert.That(
                lineIntegrityReport.CostCoverage.State ==
                    CafeReportCostCoverageStates.Incomplete &&
                lineIntegrityReport.CostCoverage.AffectedSaleIds.Contains(
                    missingLineSale) &&
                lineIntegrityReport.Profitability.HistoricalCogsCents == 0 &&
                lineIntegrityReport.Profitability.Authority ==
                    CafeReportProfitabilityAuthorityStates.ProvisionalCostIncomplete &&
                lineIntegrityReport.CostCoverage.Reasons.Any(x =>
                    x.Contains(
                        "line_count",
                        StringComparison.Ordinal)),
                "APPLIED decision with missing retained consumption lines must never become authoritative zero COGS.");
        }

        ProofAssert.That(
            fixture.ScalarLong(
                "SELECT COALESCE(MAX(version),0) FROM schema_migrations;") == 41 &&
            fixture.ScalarLong(
                "SELECT COUNT(*) FROM schema_migrations WHERE version=40;") == 1 &&
            fixture.ScalarLong(
                """
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type='table'
                  AND (
                    LOWER(name) LIKE 'cafe_report%'
                    OR LOWER(name) LIKE 'c11_report%');
                """) == 0,
            "C11 must remain on Cafe41 with report-owned persistence absent.");

        Console.WriteLine(
            "C11A REPORT SOURCE / COGS METHOD GATE: PASS");
    }
}
