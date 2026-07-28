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
  receivables: {
    creditSalesCents: number;
    cashCollectedOnSalesCents: number;
    collectionsCents: number;
    outstandingCents: number;
    overdueCents: number;
  };
};

export async function fetchOwnerReportsSummary(): Promise<OwnerReportsResponse> {
  const response = await fetch("/api/owner-reports/summary", { credentials: "include" });
  return readJson<OwnerReportsResponse>(response);
}
