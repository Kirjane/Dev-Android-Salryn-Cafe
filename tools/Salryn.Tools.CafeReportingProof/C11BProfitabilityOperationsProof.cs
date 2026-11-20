using Salryn.Core.Models;

namespace Salryn.Tools.CafeReportingProof;

internal static class C11BProfitabilityOperationsProof
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

        Guid currentRecipe =
            fixture.SeedPublishedRecipe(
                "current",
                2_000);

        fixture.SeedCashierSessionReport(
            C11ProofFixture.Day.AddHours(17));

        Guid sale =
            fixture.SeedSale(
                "profitability",
                1_000,
                C11ProofFixture.Day.AddHours(12),
                appliedRecipe: true,
                consumptionX1000: 2_000);

        fixture.SeedPerformanceSnapshots(
            "profitability",
            sale,
            C11ProofFixture.Day.AddHours(12));

        fixture.SeedCommittedRefund(
            "profitability",
            sale,
            200,
            C11ProofFixture.Day.AddHours(13));

        fixture.SeedWaste(
            "ingredient-waste",
            500,
            C11ProofFixture.Day.AddHours(12).AddMinutes(30));

        fixture.SeedCashMovement(
            "operational-expense",
            CashMovementType.OperationalExpense,
            -100,
            C11ProofFixture.Day.AddHours(14));

        fixture.SeedCashMovement(
            "generic-cash-out",
            CashMovementType.CashOut,
            -50,
            C11ProofFixture.Day.AddHours(14).AddMinutes(5));

        fixture.SeedArDebit(
            "uncollected",
            300,
            C11ProofFixture.Day.AddHours(15));

        CafeManagementReport report =
            fixture.ReadReport();

        ProofAssert.That(
            report.Sales.RecognizedCompletedSaleRevenueCents == 1_000 &&
            report.Sales.CommittedRefundCents == 200 &&
            report.Sales.RecognizedNetRevenueCents == 800,
            "Committed refund must reduce recognized net revenue exactly once.");

        ProofAssert.That(
            report.Profitability.HistoricalCogsCents == 225 &&
            fixture.ScalarLong(
                """
                SELECT COUNT(*)
                FROM ingredient_stock_movements
                WHERE movement_kind='RecipeConsumptionReversal'
                  AND sale_id=$sale;
                """,
                ("$sale", sale.ToString("D"))) == 0,
            "Refund must not imply ingredient-consumption reversal or reduce historical COGS.");

        ProofAssert.That(
            report.Waste.WasteEventCount == 1 &&
            report.Waste.WasteLineCount == 1 &&
            report.Waste.IngredientWasteQuantityX1000 == 500 &&
            report.Waste.CostCoverage.State ==
                CafeReportCostCoverageStates.Complete &&
            report.Waste.CostCoverage.KnownValueCents == 56,
            "Waste must be separately recognized from factual waste evidence using historical acquisition basis.");

        ProofAssert.That(
            report.Profitability.RecognizedNetRevenueCents == 800 &&
            report.Profitability.HistoricalCogsCents == 225 &&
            report.Profitability.GrossProfitCents == 575 &&
            report.Profitability.OperationalExpenseCents == 100 &&
            report.Profitability.RecognizedWasteCostCents == 56 &&
            report.Profitability.OperationalContributionCents == 419 &&
            report.Profitability.Authority ==
                CafeReportProfitabilityAuthorityStates.Authoritative,
            "C11 profitability arithmetic must preserve revenue/COGS/expense/waste boundaries.");

        ProofAssert.That(
            report.Cash.SaleCashReceiptCents == 1_000 &&
            report.Cash.OperationalExpenseCashOutCents == 100 &&
            report.Cash.GenericCashOutCents == 50 &&
            report.Cash.NetCashDeltaCents == 850,
            "Sale cash, operational expense, and generic CashOut must remain distinct physical-cash categories.");

        ProofAssert.That(
            report.AccountsReceivable.DebitCentsInRange == 300 &&
            report.AccountsReceivable.CreditCentsInRange == 0 &&
            report.AccountsReceivable.CashCollectionCentsInRange == 0 &&
            report.AccountsReceivable.OutstandingBalanceCents == 300 &&
            report.Cash.ReceivableCashCollectionCents == 0,
            "AR debit must remain receivable authority and must not be reported as cash.");

        ProofAssert.That(
            report.Purchases.ReceiptCount == 2 &&
            report.Purchases.CorrectionReceiptCount == 1 &&
            report.Purchases.NetAcquisitionCostCents == 900 &&
            report.Purchases.IngredientQuantityX1000 == 8_000,
            "Purchase reporting must preserve signed acquisition/correction evidence.");

        ProofAssert.That(
            report.Inventory.IngredientStockQuantityX1000 == 8_000 &&
            report.Inventory.Valuation.State ==
                CafeReportCostCoverageStates.Complete &&
            report.Inventory.Valuation.KnownValueCents == 900,
            "Current ingredient inventory must use accepted acquisition basis rather than Product.CostPrice.");

        ProofAssert.That(
            report.RecipeCosts.Count == 1 &&
            report.RecipeCosts[0].RecipeId == currentRecipe &&
            report.RecipeCosts[0].KnownCostCents == 225 &&
            report.RecipeCosts[0].CostCoverage ==
                CafeReportCostCoverageStates.Complete,
            "Current published recipe cost must use the same current historical acquisition basis without rewriting old Sale COGS.");

        ProofAssert.That(
            report.CashierSessions.Count == 1 &&
            report.CashierSessions[0].SessionId == fixture.SessionId &&
            report.CashierSessions[0].NetSalesCents == 800 &&
            report.CashierSessions[0].ExpectedClosingCashCents == 800 &&
            report.CashierSessions[0].ReconciliationBasis ==
                "LedgerAuthoritative",
            "C11 must expose immutable cashier-session close report authority without recomputation.");

        ProofAssert.That(
            report.Performance.Any(x =>
                x.DimensionType == "Product" &&
                x.DisplayName == "C11 Product" &&
                x.CostCoverage == CafeReportCostCoverageStates.Complete) &&
            report.Performance.Any(x =>
                x.DimensionType == "Variant" &&
                x.DisplayName == "C11 Snapshot Large") &&
            report.Performance.Any(x =>
                x.DimensionType == "ModifierOption" &&
                x.DisplayName == "C11 Snapshot Extra Shot" &&
                x.GrossSalesCents == 50) &&
            report.Performance.Any(x =>
                x.DimensionType == "Category" &&
                x.DisplayName == "C11 Snapshot Category"),
            "C11 performance must derive Product, Variant, ModifierOption, and Category dimensions from immutable sale/order/menu snapshots.");

        using (C11ProofFixture reversal =
               C11ProofFixture.Create(Path.Combine(root, "factual-reversal")))
        {
            reversal.SeedSyncComplete();
            reversal.SeedAcquisition(
                "basis",
                10_000,
                1_000,
                C11ProofFixture.Day.AddHours(10));

            Guid reversalSale =
                reversal.SeedSale(
                    "reversed-consumption",
                    1_000,
                    C11ProofFixture.Day.AddHours(12),
                    appliedRecipe: true,
                    consumptionX1000: 2_000);

            reversal.SeedConsumptionReversal(
                "reversed-consumption",
                reversalSale,
                2_000,
                C11ProofFixture.Day.AddHours(13));

            CafeManagementReport reversed =
                reversal.ReadReport();

            ProofAssert.That(
                reversed.Sales.CompletedSaleCount == 1 &&
                reversed.Profitability.HistoricalCogsCents == 0 &&
                reversed.CostCoverage.State ==
                    CafeReportCostCoverageStates.Complete,
                "Accepted RecipeConsumptionReversal evidence must remove the reversed consumption line from C11 historical COGS.");

            reversal.Execute(
                """
                UPDATE sales
                SET status=$voided,
                    voided_by_user_id=$actor,
                    void_reason='C11 factual void proof',
                    voided_at=$at
                WHERE id=$sale;
                """,
                ("$voided", Convert.ToInt32(SaleStatus.Voided)),
                ("$actor", reversal.ManagerUserId.ToString("D")),
                ("$at", C11ProofFixture.Day.AddHours(14).ToString("O")),
                ("$sale", reversalSale.ToString("D")));

            CafeManagementReport voided =
                reversal.ReadReport();

            ProofAssert.That(
                voided.Sales.CompletedSaleCount == 0 &&
                voided.Sales.VoidedSaleCount == 1 &&
                voided.Sales.RecognizedNetRevenueCents == 0 &&
                voided.Profitability.HistoricalCogsCents == 0,
                "Voided Sale must be excluded from recognized revenue while factual reversal evidence preserves zero active COGS.");
        }

        using (C11ProofFixture delivery =
               C11ProofFixture.Create(Path.Combine(root, "delivery-fee-recognition")))
        {
            delivery.SeedSyncComplete();

            Guid completedDeliverySale =
                delivery.SeedSale(
                    "delivery-completed",
                    1_000,
                    C11ProofFixture.Day.AddHours(12),
                    appliedRecipe: false,
                    feeCents: 100);

            Guid voidedDeliverySale =
                delivery.SeedSale(
                    "delivery-voided",
                    800,
                    C11ProofFixture.Day.AddHours(13),
                    appliedRecipe: false,
                    feeCents: 80);

            delivery.Execute(
                """
                UPDATE sales
                SET status=$voided,
                    voided_by_user_id=$actor,
                    void_reason='C11 delivery reporting proof void',
                    voided_at=$at
                WHERE id=$sale;
                """,
                ("$voided", Convert.ToInt32(SaleStatus.Voided)),
                ("$actor", delivery.ManagerUserId.ToString("D")),
                ("$at", C11ProofFixture.Day.AddHours(14).ToString("O")),
                ("$sale", voidedDeliverySale.ToString("D")));

            CafeManagementReport deliveryReport =
                delivery.ReadReport();

            ProofAssert.That(
                completedDeliverySale != Guid.Empty &&
                deliveryReport.Delivery.RecognizedDeliveryFeeCents == 100 &&
                deliveryReport.Sales.FeeCents == 100,
                "Recognized Delivery fees must include completed-sale fee authority and exclude immutable fees retained by later-voided Sales.");
        }

        ProofAssert.That(
            !report.OfficialBoundary.Contains(
                "audited profit",
                StringComparison.OrdinalIgnoreCase) ||
            report.OfficialBoundary.Contains(
                "not",
                StringComparison.OrdinalIgnoreCase),
            "C11 must retain explicit non-official reporting boundary.");

        Console.WriteLine(
            "C11B PROFITABILITY / OPERATIONS GATE: PASS");
    }
}
