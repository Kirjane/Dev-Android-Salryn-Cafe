import { readJson } from "./http";

export type OwnerSalesPeriodSummary = {
  periodKey: string;
  label: string;
  rangeStart: string;
  rangeEnd: string;
  salesCount: number;
  salesAmountCents: number;
  averageSaleCents: number;
  voidedCount: number;
  voidedAmountCents: number;
};

export type OwnerTopProductRow = {
  productId: string;
  productName: string;
  barcode: string | null;
  quantitySold: number;
  salesAmountCents: number;
};

export type OwnerCashierTotalRow = {
  cashierUserId: string;
  cashierDisplayName: string;
  salesCount: number;
  salesAmountCents: number;
  voidedCount: number;
  voidedAmountCents: number;
};

export type OwnerCategoryTotalRow = {
  categoryName: string;
  quantitySold: number;
  salesAmountCents: number;
};

export type OwnerLowStockRow = {
  productId: string;
  productName: string;
  barcode: string | null;
  unitName: string;
  stockOnHand: number;
  reorderLevel: number;
};

export type OwnerInventoryValueSummary = {
  isAvailable: boolean;
  estimatedValueCents: number | null;
  valuedProductCount: number;
  missingCostProductCount: number;
  message: string;
};

export type CafeReportCostCoverage = {
  state: "Complete" | "Incomplete";
  revenueSaleCount: number;
  costCompleteSaleCount: number;
  costIncompleteSaleCount: number;
  consumptionLineCount: number;
  costedConsumptionLineCount: number;
  uncostedConsumptionLineCount: number;
  affectedRevenueCents: number;
  affectedSaleIds: string[];
  uncostedConsumptionLineIds: string[];
  reasons: string[];
};

export type CafeReportSalesSummary = {
  completedSaleCount: number;
  voidedSaleCount: number;
  financialDecisionSaleCount: number;
  legacyBasisSaleCount: number;
  grossSubtotalCents: number;
  lineDiscountCents: number;
  orderDiscountCents: number;
  feeCents: number;
  recognizedCompletedSaleRevenueCents: number;
  committedRefundCents: number;
  recognizedNetRevenueCents: number;
};

export type CafeReportProfitabilitySummary = {
  recognizedNetRevenueCents: number;
  historicalCogsCents: number;
  grossProfitCents: number;
  operationalExpenseCents: number;
  recognizedWasteCostCents: number;
  operationalContributionCents: number;
  authority:
    | "Authoritative"
    | "ProvisionalCostIncomplete"
    | "ProvisionalSyncIncomplete"
    | "ProvisionalUnknownSync";
  authorityReasons: string[];
};

export type CafeReportCashSummary = {
  saleCashReceiptCents: number;
  receivableCashCollectionCents: number;
  cashInCents: number;
  inventoryPurchaseCashOutCents: number;
  operationalExpenseCashOutCents: number;
  cashRefundCents: number;
  genericCashOutCents: number;
  reversalNetCents: number;
  netCashDeltaCents: number;
};

export type CafeReportArSummary = {
  debitCentsInRange: number;
  creditCentsInRange: number;
  cashCollectionCentsInRange: number;
  outstandingBalanceCents: number;
};

export type CafeReportPurchaseSummary = {
  receiptCount: number;
  correctionReceiptCount: number;
  netAcquisitionCostCents: number;
  ingredientQuantityX1000: number;
  productQuantityX1000: number;
};

export type CafeReportValuationCoverage = {
  state: "Complete" | "Incomplete";
  valuedLineCount: number;
  missingCostLineCount: number;
  knownValueCents: number;
  missingLabels: string[];
};

export type CafeReportWasteSummary = {
  wasteEventCount: number;
  wasteLineCount: number;
  ingredientWasteQuantityX1000: number;
  productWasteQuantityX1000: number;
  costCoverage: CafeReportValuationCoverage;
};

export type CafeReportInventorySummary = {
  ingredientStockQuantityX1000: number;
  productStockQuantityX1000: number;
  valuation: CafeReportValuationCoverage;
};

export type CafeReportRecipeCostRow = {
  recipeId: string;
  recipeName: string;
  recipeBookRevisionId: string;
  yieldX1000: number;
  knownCostCents: number;
  costCoverage: "Complete" | "Incomplete";
  ingredientLineCount: number;
  missingCostLineCount: number;
};

export type CafeReportPerformanceRow = {
  dimensionType: "Product" | "Variant" | "ModifierOption" | "Category" | string;
  dimensionId: string;
  displayName: string;
  quantityX1000: number;
  grossSalesCents: number;
  discountCents: number;
  netProductRevenueCents: number;
  historicalCogsCents: number;
  costCoverage: "Complete" | "Incomplete";
  grossProfitCents: number;
};

export type CafeReportSessionRow = {
  sessionId: string;
  cashierUserId: string;
  cashierDisplayName: string;
  openedAtUtc: string;
  closedAtUtc: string;
  grossSalesCents: number;
  netSalesCents: number;
  cashSalesCents: number;
  noncashSalesCents: number;
  expectedClosingCashCents: number;
  declaredClosingCashCents: number | null;
  varianceCents: number | null;
  reconciliationBasis: string;
  ledgerNetDeltaCents: number | null;
};

export type CafeReportBookingSummary = {
  bookingCount: number;
  eventCount: number;
  coffeeCartCount: number;
  draftCount: number;
  confirmedCount: number;
  readyForFulfillmentCount: number;
  fulfilledCount: number;
  cancelledCount: number;
  quotedTotalCents: number;
  depositReceivedCents: number;
  depositRefundedCents: number;
  depositAppliedCents: number;
};

export type CafeReportDeliverySummary = {
  deliveryCount: number;
  pendingCount: number;
  dispatchedCount: number;
  deliveredCount: number;
  failedCount: number;
  cancelledCount: number;
  recognizedDeliveryFeeCents: number;
};

export type CafeReportSyncIssue = {
  code: string;
  description: string;
  count: number;
};

export type CafeReportSyncCompleteness = {
  state: "Complete" | "Incomplete" | "Unknown";
  pendingUploadCount: number;
  needsReviewOrRejectedCount: number;
  nonReadyRemoteStateCount: number;
  cursorLagCount: number;
  unappliedInboxCount: number;
  activeTerminalCount: number;
  activeTerminalMissingSyncEvidenceCount: number;
  issues: CafeReportSyncIssue[];
};

export type CafeManagementReport = {
  generatedAtUtc: string;
  rangeStartUtc: string;
  rangeEndUtc: string;
  officialBoundary: string;
  sales: CafeReportSalesSummary;
  costCoverage: CafeReportCostCoverage;
  profitability: CafeReportProfitabilitySummary;
  cash: CafeReportCashSummary | null;
  accountsReceivable: CafeReportArSummary | null;
  purchases: CafeReportPurchaseSummary;
  waste: CafeReportWasteSummary;
  inventory: CafeReportInventorySummary;
  recipeCosts: CafeReportRecipeCostRow[];
  performance: CafeReportPerformanceRow[];
  cashierSessions: CafeReportSessionRow[];
  bookings: CafeReportBookingSummary;
  delivery: CafeReportDeliverySummary;
  syncCompleteness: CafeReportSyncCompleteness;
};

export type OwnerReportsResponse = {
  generatedAt: string;
  weekRule: string;
  officialBoundary: string;
  daily: OwnerSalesPeriodSummary;
  weekly: OwnerSalesPeriodSummary;
  monthly: OwnerSalesPeriodSummary;
  topProducts: OwnerTopProductRow[];
  cashierTotals: OwnerCashierTotalRow[];
  categoryTotals: OwnerCategoryTotalRow[];
  lowStock: OwnerLowStockRow[];
  inventoryValue: OwnerInventoryValueSummary;
  cafeManagement: CafeManagementReport | null;
  receivables: {
    creditSalesCents: number;
    cashCollectedOnSalesCents: number;
    collectionsCents: number;
    outstandingCents: number;
    overdueCents: number;
  } | null;
};

export async function fetchOwnerReportsSummary(
  range?: { fromUtc?: string; toUtc?: string },
): Promise<OwnerReportsResponse> {
  const query = new URLSearchParams();
  if (range?.fromUtc) query.set("fromUtc", range.fromUtc);
  if (range?.toUtc) query.set("toUtc", range.toUtc);
  const suffix = query.toString() ? "?" + query.toString() : "";
  const response = await fetch("/api/owner-reports/summary" + suffix, {
    credentials: "include",
  });
  return readJson<OwnerReportsResponse>(response);
}
