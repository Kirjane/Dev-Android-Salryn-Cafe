import { expect, test } from "@playwright/test";
import { installMockSalrynApi } from "./fixtures/mockApi";

test.describe("W5 LPI runtime contract regressions", () => {
  test.beforeEach(async ({ page }) => {
    await installMockSalrynApi(page, { persona: "admin", u1Entitled: true });
    await page.route("**/api/lpi/**", async (route) => {
      const path = new URL(route.request().url()).pathname;
      const emptyCollections = [
        "/api/lpi/suppliers", "/api/lpi/requisitions", "/api/lpi/purchase-orders",
        "/api/lpi/receiving-reports", "/api/lpi/supplier-returns", "/api/lpi/invoices", "/api/lpi/payments",
      ];
      if (path === "/api/lpi/foundation") return route.fulfill({ json: {
        suppliers: [], locations: [
          { id: "loc-main", code: "MAIN-STORE", name: "Main Store", isActive: true },
          { id: "loc-q", code: "QUARANTINE-HOLD", name: "Quarantine", isActive: true },
        ], purchaseClassifications: [], sequenceCounters: [],
        unitsOfMeasure: [{ id: "uom-pc", name: "Piece", abbreviation: "pc", allowsFractional: false, quantityScale: 1000 }],
        products: [{ id: "prod-1", name: "QA Product", barcode: "QA-001", unitId: "uom-pc", unitName: "Piece", costPriceCents: 2500, sellingPriceCents: 4000, stockOnHandX1000: 7000 }],
      }});
      if (path === "/api/lpi/overview") return route.fulfill({ json: {
        activeSupplierCount: 3, pendingRequisitionCount: 2, openPurchaseOrderCount: 1,
        varianceFlaggedInvoiceCount: 1, totalOpenPayablesCents: 123400, overduePayablesCents: 3400,
        totalInventoryValuationCents: 17500, costCoverageIncomplete: false, missingCostProductCount: 0,
        quarantineStockQuantityX1000: 1000, recentOrders: [], apAging: { openInvoiceCount: 1, currentBucketCents: 120000, days1To30Cents: 3400, days31To60Cents: 0, days61To90Cents: 0, daysOver90Cents: 0, totalOpenPayablesCents: 123400 },
      }});
      if (path === "/api/lpi/stock-cards") return route.fulfill({ json: [{
        productId: "prod-1", productName: "QA Product", barcode: "QA-001", unitName: "Piece",
        currentStockOnHandX1000: 7000, currentCostPriceCents: 2500, inventoryValuationCents: 17500,
        buckets: [{ locationId: "loc-main", locationCode: "MAIN-STORE", locationName: "Main Store", quantityX1000: 6000 }, { locationId: "loc-q", locationCode: "QUARANTINE-HOLD", locationName: "Quarantine", quantityX1000: 1000 }],
      }] });
      if (emptyCollections.includes(path)) return route.fulfill({ json: [] });
      return route.fulfill({ json: [] });
    });
  });

  test("Procurement and Stock tabs render backend-shaped responses without a white screen", async ({ page }) => {
    const pageErrors: string[] = [];
    page.on("pageerror", (error) => pageErrors.push(error.message));
    await page.goto("/?page=procure-to-pay");
    await expect(page.getByRole("heading", { name: "Logistics, Procurement & Inventory" })).toBeVisible();
    await expect(page.getByText("PHP 1,234.00").first()).toBeVisible();
    await page.getByRole("button", { name: /Stock & Returns/ }).click();
    await expect(page.getByText("QA Product")).toBeVisible();
    await expect(page.getByText("PHP 175.00")).toBeVisible();
    await expect(page.getByText("This workspace could not be displayed")).toHaveCount(0);
    expect(pageErrors).toEqual([]);
  });
});
