namespace Salryn.Core.Models;

public static class CafeReportCostCoverageStates
{
    public const string Complete = "Complete";
    public const string Incomplete = "Incomplete";
}

public static class CafeReportSyncCompletenessStates
{
    public const string Complete = "Complete";
    public const string Incomplete = "Incomplete";
    public const string Unknown = "Unknown";
}

public static class CafeReportProfitabilityAuthorityStates
{
    public const string Authoritative = "Authoritative";
    public const string ProvisionalCostIncomplete = "ProvisionalCostIncomplete";
    public const string ProvisionalSyncIncomplete = "ProvisionalSyncIncomplete";
    public const string ProvisionalUnknownSync = "ProvisionalUnknownSync";
}

public sealed record CafeManagementReportRequest(
    Guid StoreId,
    Guid ActorUserId,
    DateTimeOffset RangeStartUtc,
    DateTimeOffset RangeEndUtc,
    DateTimeOffset GeneratedAtUtc);

public sealed record CafeReportCostCoverage(
    string State,
    int RevenueSaleCount,
    int CostCompleteSaleCount,
    int CostIncompleteSaleCount,
    int ConsumptionLineCount,
    int CostedConsumptionLineCount,
    int UncostedConsumptionLineCount,
    long AffectedRevenueCents,
    IReadOnlyList<Guid> AffectedSaleIds,
    IReadOnlyList<Guid> UncostedConsumptionLineIds,
    IReadOnlyList<string> Reasons);

public sealed record CafeReportSalesSummary(
    int CompletedSaleCount,
    int VoidedSaleCount,
    int FinancialDecisionSaleCount,
    int LegacyBasisSaleCount,
    long GrossSubtotalCents,
    long LineDiscountCents,
    long OrderDiscountCents,
    long FeeCents,
    long RecognizedCompletedSaleRevenueCents,
    long CommittedRefundCents,
    long RecognizedNetRevenueCents);

public sealed record CafeReportProfitabilitySummary(
    long RecognizedNetRevenueCents,
    long HistoricalCogsCents,
    long GrossProfitCents,
    long OperationalExpenseCents,
    long RecognizedWasteCostCents,
    long OperationalContributionCents,
    string Authority,
    IReadOnlyList<string> AuthorityReasons);

public sealed record CafeReportCashSummary(
    long SaleCashReceiptCents,
    long ReceivableCashCollectionCents,
    long CashInCents,
    long InventoryPurchaseCashOutCents,
    long OperationalExpenseCashOutCents,
    long CashRefundCents,
    long GenericCashOutCents,
    long ReversalNetCents,
    long NetCashDeltaCents);

public sealed record CafeReportArSummary(
    long DebitCentsInRange,
    long CreditCentsInRange,
    long CashCollectionCentsInRange,
    long OutstandingBalanceCents);

public sealed record CafeReportPurchaseSummary(
    int ReceiptCount,
    int CorrectionReceiptCount,
    long NetAcquisitionCostCents,
    long IngredientQuantityX1000,
    long ProductQuantityX1000);

public sealed record CafeReportValuationCoverage(
    string State,
    int ValuedLineCount,
    int MissingCostLineCount,
    long KnownValueCents,
    IReadOnlyList<string> MissingLabels);

public sealed record CafeReportWasteSummary(
    int WasteEventCount,
    int WasteLineCount,
    long IngredientWasteQuantityX1000,
    long ProductWasteQuantityX1000,
    CafeReportValuationCoverage CostCoverage);

public sealed record CafeReportInventorySummary(
    long IngredientStockQuantityX1000,
    long ProductStockQuantityX1000,
    CafeReportValuationCoverage Valuation);

public sealed record CafeReportRecipeCostRow(
    string RecipeId,
    string RecipeName,
    string RecipeBookRevisionId,
    long YieldX1000,
    long KnownCostCents,
    string CostCoverage,
    int IngredientLineCount,
    int MissingCostLineCount);

public sealed record CafeReportPerformanceRow(
    string DimensionType,
    string DimensionId,
    string DisplayName,
    long QuantityX1000,
    long GrossSalesCents,
    long DiscountCents,
    long NetProductRevenueCents,
    long HistoricalCogsCents,
    string CostCoverage,
    long GrossProfitCents);

public sealed record CafeReportSessionRow(
    Guid SessionId,
    Guid CashierUserId,
    string CashierDisplayName,
    DateTimeOffset OpenedAtUtc,
    DateTimeOffset ClosedAtUtc,
    long GrossSalesCents,
    long NetSalesCents,
    long CashSalesCents,
    long NoncashSalesCents,
    long ExpectedClosingCashCents,
    long? DeclaredClosingCashCents,
    long? VarianceCents,
    string ReconciliationBasis,
    long? LedgerNetDeltaCents);

public sealed record CafeReportBookingSummary(
    int BookingCount,
    int EventCount,
    int CoffeeCartCount,
    int DraftCount,
    int ConfirmedCount,
    int ReadyForFulfillmentCount,
    int FulfilledCount,
    int CancelledCount,
    long QuotedTotalCents,
    long DepositReceivedCents,
    long DepositRefundedCents,
    long DepositAppliedCents);

public sealed record CafeReportDeliverySummary(
    int DeliveryCount,
    int PendingCount,
    int DispatchedCount,
    int DeliveredCount,
    int FailedCount,
    int CancelledCount,
    long RecognizedDeliveryFeeCents);

public sealed record CafeReportSyncIssue(
    string Code,
    string Description,
    long Count);

public sealed record CafeReportSyncCompleteness(
    string State,
    long PendingUploadCount,
    long NeedsReviewOrRejectedCount,
    long NonReadyRemoteStateCount,
    long CursorLagCount,
    long UnappliedInboxCount,
    long ActiveTerminalCount,
    long ActiveTerminalMissingSyncEvidenceCount,
    IReadOnlyList<CafeReportSyncIssue> Issues);

public sealed record CafeManagementReport(
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset RangeStartUtc,
    DateTimeOffset RangeEndUtc,
    string OfficialBoundary,
    CafeReportSalesSummary Sales,
    CafeReportCostCoverage CostCoverage,
    CafeReportProfitabilitySummary Profitability,
    CafeReportCashSummary Cash,
    CafeReportArSummary AccountsReceivable,
    CafeReportPurchaseSummary Purchases,
    CafeReportWasteSummary Waste,
    CafeReportInventorySummary Inventory,
    IReadOnlyList<CafeReportRecipeCostRow> RecipeCosts,
    IReadOnlyList<CafeReportPerformanceRow> Performance,
    IReadOnlyList<CafeReportSessionRow> CashierSessions,
    CafeReportBookingSummary Bookings,
    CafeReportDeliverySummary Delivery,
    CafeReportSyncCompleteness SyncCompleteness);
