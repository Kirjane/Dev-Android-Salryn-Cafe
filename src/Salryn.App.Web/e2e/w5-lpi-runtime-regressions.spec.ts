import { expect, test } from "@playwright/test";
import { installMockSalrynApi } from "./fixtures/mockApi";

const MOCK_FOUNDATION = {
  suppliers: [
    {
      id: "sup-1",
      code: "SUP-01",
      name: "Apex Roasters & Dairy Supply Inc.",
      contactPerson: "Maria Santos",
      phone: "09171234501",
      email: "orders@apexroasters.ph",
      address: "Makati Industrial Park",
      tin: "123-456-789-000",
      paymentTermsDays: 30,
      leadTimeDays: 5,
      notes: "Primary coffee vendor",
      isActive: true,
    },
  ],
  locations: [
    { id: "loc-main", code: "MAIN-STORE", name: "Main Store", isActive: true },
    { id: "loc-q", code: "QUARANTINE-HOLD", name: "Quarantine", isActive: true },
  ],
  purchaseClassifications: [
    { code: "Resale", name: "Goods for Resale" },
    { code: "Consumable", name: "Store Consumable" },
  ],
  sequenceCounters: [],
  unitsOfMeasure: [
    { id: "uom-pc", name: "Piece", abbreviation: "pc", allowsFractional: false, quantityScale: 1000 },
  ],
  products: [
    {
      id: "prod-1",
      sku: "QA-001",
      barcode: "QA-001",
      name: "QA Premium Coffee Beans",
      unitId: "uom-pc",
      unitName: "Piece",
      costPriceCents: 4200,
      sellingPriceCents: 7500,
      stockOnHandX1000: 7000,
      reorderLevelX1000: 2000,
      trackStock: true,
    },
  ],
};

const MOCK_OVERVIEW = {
  activeSupplierCount: 1,
  pendingRequisitionCount: 1,
  openPurchaseOrderCount: 1,
  varianceFlaggedInvoiceCount: 1,
  totalOpenPayablesCents: 420000,
  overduePayablesCents: 0,
  totalInventoryValuationCents: 29400,
  costCoverageIncomplete: false,
  missingCostProductCount: 0,
  quarantineStockQuantityX1000: 1000,
  recentOrders: [],
  apAging: {
    openInvoiceCount: 1,
    currentBucketCents: 420000,
    days1To30Cents: 0,
    days31To60Cents: 0,
    days61To90Cents: 0,
    daysOver90Cents: 0,
    totalOpenPayablesCents: 420000,
    openInvoices: [
      {
        invoiceId: "inv-1",
        apVoucherNumber: "APV-202611-0001",
        invoiceNumber: "SI-APEX-001",
        supplierId: "sup-1",
        supplierName: "Apex Roasters & Dairy Supply Inc.",
        invoiceDate: "2026-11-10",
        dueDate: "2026-12-10",
        daysOverdue: 0,
        totalAmountCents: 420000,
        remainingBalanceCents: 420000,
        agingBucket: "Current",
      },
    ],
  },
};

const MOCK_STOCK_CARDS = [
  {
    productId: "prod-1",
    productName: "QA Premium Coffee Beans",
    barcode: "QA-001",
    unitName: "Piece",
    currentStockOnHandX1000: 7000,
    currentCostPriceCents: 4200,
    inventoryValuationCents: 29400,
    buckets: [
      { locationId: "loc-main", locationCode: "MAIN-STORE", locationName: "Main Store", quantityX1000: 6000 },
      { locationId: "loc-q", locationCode: "QUARANTINE-HOLD", locationName: "Quarantine", quantityX1000: 1000 },
    ],
  },
];

const MOCK_REQUISITIONS = [
  {
    id: "req-1",
    prNumber: "PR-202611-0001",
    department: "Store Operations",
    requiredDate: "2026-11-20",
    priority: "Normal",
    purpose: "Monthly restock",
    status: "Approved",
    totalEstimatedAmountCents: 450000,
    requestedByDisplayName: "Maria Requester",
    lines: [
      {
        id: "reql-1",
        requisitionId: "req-1",
        lineNumber: 1,
        productId: "prod-1",
        itemCode: "QA-001",
        itemDescription: "QA Premium Coffee Beans",
        purchaseClassification: "Resale",
        unitName: "Piece",
        requestedQtyX1000: 10000,
        estimatedUnitCostCents: 4500,
        estimatedTotalCents: 45000,
        preferredSupplierId: "sup-1",
        preferredSupplierName: "Apex Roasters & Dairy Supply Inc.",
      },
    ],
  },
];

const MOCK_QUOTATIONS = [
  {
    id: "quote-1",
    requisitionId: "req-1",
    requisitionLineId: "reql-1",
    supplierId: "sup-1",
    supplierName: "Apex Roasters & Dairy Supply Inc.",
    productId: "prod-1",
    productName: "QA Premium Coffee Beans",
    quotedUnitCostCents: 4200,
    minimumOrderQtyX1000: 5000,
    leadTimeDays: 5,
    paymentTermsDays: 30,
    validityDate: "2027-01-31",
    isSelected: true,
    remarks: "Best negotiated rate",
  },
];

const MOCK_PURCHASE_ORDERS = [
  {
    id: "po-1",
    poNumber: "PO-202611-0001",
    supplierId: "sup-1",
    supplierName: "Apex Roasters & Dairy Supply Inc.",
    requisitionId: "req-1",
    requisitionNumber: "PR-202611-0001",
    orderDate: "2026-11-10",
    expectedDeliveryDate: "2026-11-15",
    deliveryLocationId: "loc-main",
    deliveryLocationName: "Main Store",
    paymentTermsDays: 30,
    status: "Sent",
    totalAmountCents: 420000,
    lines: [
      {
        id: "pol-1",
        purchaseOrderId: "po-1",
        lineNumber: 1,
        requisitionLineId: "reql-1",
        productId: "prod-1",
        itemCode: "QA-001",
        itemDescription: "QA Premium Coffee Beans",
        purchaseClassification: "Resale",
        unitName: "Piece",
        orderedQtyX1000: 10000,
        receivedAcceptedQtyX1000: 0,
        receivedRejectedQtyX1000: 0,
        outstandingQtyX1000: 10000,
        unitCostCents: 4200,
        lineTotalCents: 420000,
      },
    ],
  },
];

const MOCK_RECEIVING_REPORTS = [
  {
    id: "rr-1",
    rrNumber: "RR-202611-0001",
    purchaseOrderId: "po-1",
    poNumber: "PO-202611-0001",
    supplierId: "sup-1",
    supplierName: "Apex Roasters & Dairy Supply Inc.",
    receivingLocationId: "loc-main",
    receivingLocationName: "Main Store",
    deliveryReceiptNo: "DR-APEX-001",
    supplierInvoiceRef: "SI-APEX-001",
    carrierOrDriver: "Apex Delivery Van",
    receivedDate: "2026-11-12",
    status: "Posted",
    totalAcceptedQuantityX1000: 10000,
    totalRejectedQuantityX1000: 0,
    receivedByDisplayName: "Carlos Receiver",
    lines: [
      {
        id: "rrl-1",
        receivingReportId: "rr-1",
        purchaseOrderLineId: "pol-1",
        itemDescription: "QA Premium Coffee Beans",
        unitName: "Piece",
        deliveredQtyX1000: 10000,
        acceptedQtyX1000: 10000,
        rejectedQtyX1000: 0,
      },
    ],
  },
];

const MOCK_INVOICES = [
  {
    id: "inv-1",
    apVoucherNumber: "APV-202611-0001",
    supplierInvoiceNo: "SI-APEX-001",
    supplierId: "sup-1",
    supplierName: "Apex Roasters & Dairy Supply Inc.",
    purchaseOrderId: "po-1",
    poNumber: "PO-202611-0001",
    invoiceDate: "2026-11-12",
    dueDate: "2026-12-12",
    status: "VarianceReview",
    matchStatus: "PriceVariance",
    totalInvoiceCents: 450000,
    remainingBalanceCents: 450000,
    varianceNotes: "Minor price verification flag",
    lines: [
      {
        id: "invl-1",
        supplierInvoiceId: "inv-1",
        poItemId: "pol-1",
        itemDescription: "QA Premium Coffee Beans",
        invoicedQtyX1000: 10000,
        invoicedUnitCostCents: 4500,
        lineTotalCents: 450000,
      },
    ],
  },
  {
    id: "inv-2",
    apVoucherNumber: "APV-202611-0002",
    supplierInvoiceNo: "SI-APEX-002",
    supplierId: "sup-1",
    supplierName: "Apex Roasters & Dairy Supply Inc.",
    purchaseOrderId: "po-1",
    poNumber: "PO-202611-0001",
    invoiceDate: "2026-11-13",
    dueDate: "2026-12-13",
    status: "Approved",
    matchStatus: "Matched",
    totalInvoiceCents: 420000,
    remainingBalanceCents: 420000,
    varianceNotes: null,
    lines: [
      {
        id: "invl-2",
        supplierInvoiceId: "inv-2",
        poItemId: "pol-1",
        itemDescription: "QA Premium Coffee Beans",
        invoicedQtyX1000: 10000,
        invoicedUnitCostCents: 4200,
        lineTotalCents: 420000,
      },
    ],
  },
];

const MOCK_PAYMENTS = [
  {
    id: "pmt-1",
    paymentNumber: "PMT-202611-0001",
    supplierInvoiceId: "inv-1",
    supplierInvoiceNo: "SI-APEX-001",
    supplierId: "sup-1",
    supplierName: "Apex Roasters & Dairy Supply Inc.",
    paymentDate: "2026-11-14",
    amountCents: 200000,
    paymentMethod: "Check",
    checkNo: "CHK-004412",
    bankName: "Metrobank",
    referenceNo: "REF-004412",
    remarks: "Partial payment check settlement",
    status: "Approved",
  },
];

function registerLpiRoutes(page: any, onDirectPo?: (body: any) => void) {
  return page.route("**/api/lpi/**", async (route: any) => {
    const url = new URL(route.request().url());
    const path = url.pathname;

    if (path === "/api/lpi/foundation") return route.fulfill({ json: MOCK_FOUNDATION });
    if (path === "/api/lpi/overview") return route.fulfill({ json: MOCK_OVERVIEW });
    if (path === "/api/lpi/stock-cards") return route.fulfill({ json: MOCK_STOCK_CARDS });
    if (path === "/api/lpi/suppliers") return route.fulfill({ json: MOCK_FOUNDATION.suppliers });
    if (path === "/api/lpi/requisitions") return route.fulfill({ json: MOCK_REQUISITIONS });
    if (path.includes("/quotations")) return route.fulfill({ json: MOCK_QUOTATIONS });
    if (path === "/api/lpi/purchase-orders") {
      if (route.request().method() === "POST") {
        const body = route.request().postDataJSON();
        onDirectPo?.(body);
        return route.fulfill({
          status: 201,
          json: {
            ...MOCK_PURCHASE_ORDERS[0],
            id: "po-direct-1",
            poNumber: "PO-202611-0002",
            sourceRequisitionId: null,
            status: body.submitAndApproveImmediately ? "Approved" : "Draft",
            notes: body.notes,
            lines: body.lines,
          },
        });
      }
      return route.fulfill({ json: MOCK_PURCHASE_ORDERS });
    }
    if (path === "/api/lpi/receiving-reports") return route.fulfill({ json: MOCK_RECEIVING_REPORTS });
    if (path === "/api/lpi/supplier-returns") return route.fulfill({ json: [] });
    if (path === "/api/lpi/invoices") return route.fulfill({ json: MOCK_INVOICES });
    if (path === "/api/lpi/payments") return route.fulfill({ json: MOCK_PAYMENTS });

    return route.fulfill({ json: [] });
  });
}

test.describe("W5 LPI runtime contract regressions", () => {
  test("R3 A1A direct PO requires a reason and sends authoritative approval intent", async ({ page }) => {
    let directPoRequest: any = null;
    await installMockSalrynApi(page, { persona: "admin", u1Entitled: true });
    await registerLpiRoutes(page, (body) => { directPoRequest = body; });

    await page.goto("/?page=procure-to-pay&tab=po");
    await page.getByRole("button", { name: "+ Direct Purchase Order" }).click();
    const directPoDialog = page.getByRole("dialog", { name: "Create Direct Purchase Order" });
    await expect(directPoDialog).toBeVisible();
    await expect(directPoDialog.getByRole("button", { name: "Create Direct PO" })).toBeDisabled();
    await directPoDialog.locator("select").nth(0).selectOption("sup-1");
    await directPoDialog.locator("textarea").fill("Urgent notebook replenishment before school opening");
    await directPoDialog.locator("select").nth(1).selectOption("prod-1");
    await directPoDialog.getByText("Approve immediately using my purchase-order approval authority").click();
    await directPoDialog.getByRole("button", { name: "Create Direct PO" }).click();
    await expect(directPoDialog).toBeHidden();

    expect(directPoRequest).toMatchObject({
      supplierId: "sup-1",
      notes: "Urgent notebook replenishment before school opening",
      submitAndApproveImmediately: true,
    });
    expect(directPoRequest).not.toHaveProperty("sourceRequisitionId");
    expect(directPoRequest.lines).toHaveLength(1);
    expect(directPoRequest.lines[0]).toMatchObject({
      productId: "prod-1",
      itemDescription: "QA Premium Coffee Beans",
      unitName: "Piece",
      orderedQtyX1000: 1000,
      unitCostCents: 4200,
    });
  });

  test("All eight procurement tabs render cleanly with zero errors, non-zero amounts, and modal stability", async ({ page }) => {
    test.setTimeout(60_000);
    const pageErrors: string[] = [];
    const consoleErrors: string[] = [];
    page.on("pageerror", (err) => pageErrors.push(err.message));
    page.on("console", (msg) => {
      if (msg.type() === "error") consoleErrors.push(msg.text());
    });

    await installMockSalrynApi(page, { persona: "admin", u1Entitled: true });
    await registerLpiRoutes(page);

    await page.goto("/?page=procure-to-pay");
    await expect(page.getByRole("heading", { name: "Logistics, Procurement & Inventory" })).toBeVisible();

    // 1. Overview tab
    await expect(page.getByText("Active Suppliers")).toBeVisible();
    await expect(page.getByText("PHP 4,200.00").first()).toBeVisible();
    await expect(page.getByText("Apex Roasters & Dairy Supply Inc.").first()).toBeVisible();

    // 2. Suppliers tab
    await page.getByRole("button", { name: /Suppliers/ }).click();
    await expect(page.getByRole("button", { name: "+ Add Supplier" })).toBeVisible();
    await expect(page.getByText("SUP-01")).toBeVisible();

    // 3. Requisitions tab
    await page.getByRole("button", { name: /Requisitions/ }).click();
    await expect(page.getByRole("button", { name: "+ Create Requisition" })).toBeVisible();
    await expect(page.getByText("PR-202611-0001")).toBeVisible();

    // 4. Quotations tab (select requisition to view quotes)
    await page.getByRole("button", { name: /Quotations/ }).click();
    await page.locator("select").first().selectOption("req-1");
    await expect(page.getByText(/Selected Winner/i)).toBeVisible();
    await expect(page.getByText("PHP 42.00")).toBeVisible();

    // 5. Purchase Orders tab
    await page.getByRole("button", { name: /Purchase Orders/ }).click();
    await expect(page.getByText("PO-202611-0001")).toBeVisible();
    await expect(page.getByText("2026-11-15")).toBeVisible();
    await expect(page.getByText("PHP 4,200.00").first()).toBeVisible();

    // 6. Receiving (RR) tab & Receive Goods modal (Crash regression: po.items.map)
    await page.getByRole("button", { name: /Receiving \(RR\)/ }).click();
    await expect(page.getByText("RR-202611-0001")).toBeVisible();

    // Select PO to open Receiving Goods modal (validates lines/items normalization without crash)
    const poSelect = page.locator("select").filter({ hasText: "Receive Goods Against PO..." });
    await expect(poSelect).toBeVisible();
    await poSelect.selectOption("po-1");
    await expect(page.getByText(/Receive Goods against PO:/i)).toBeVisible();
    await expect(page.getByText("QA Premium Coffee Beans")).toBeVisible();
    await page.getByRole("button", { name: "Cancel" }).click();

    // 7. Stock & Returns tab
    await page.getByRole("button", { name: /Stock & Returns/ }).click();
    await expect(page.getByRole("button", { name: "+ Count Adjustment" })).toBeVisible();
    await expect(page.getByRole("button", { name: "+ Supplier Return" })).toBeVisible();
    await expect(page.getByText("QA Premium Coffee Beans")).toBeVisible();
    await expect(page.getByText("Main Store").first()).toBeVisible();
    await expect(page.getByText("Quarantine").first()).toBeVisible();

    // 9. Accounts Payable tab
    await page.getByRole("button", { name: /Accounts Payable/ }).click();
    await expect(page.getByRole("button", { name: "+ Intake Supplier Invoice" })).toBeVisible();
    await expect(page.getByText("APV-202611-0001")).toBeVisible();
    await expect(page.getByText("SI-APEX-001")).toBeVisible();
    await expect(page.getByRole("button", { name: /Approve Variance/i })).toBeVisible();
    await expect(page.getByRole("button", { name: /Record Payment/i })).toBeVisible();

    // 10. Refresh & reload durability
    await page.reload();
    await expect(page.getByRole("heading", { name: "Logistics, Procurement & Inventory" })).toBeVisible();

    // Ensure zero page crashes or unhandled exceptions
    await expect(page.getByText("This workspace could not be displayed")).toHaveCount(0);
    expect(pageErrors).toEqual([]);
    expect(consoleErrors).toEqual([]);
  });

  test("Read-only manager persona with View permissions cannot see mutation controls", async ({ page }) => {
    test.setTimeout(60_000);
    const pageErrors: string[] = [];
    page.on("pageerror", (err) => pageErrors.push(err.message));

    // Manager with read-only View permissions (no request, manage, adjust, approve, or payment permissions)
    await installMockSalrynApi(page, {
      persona: "manager",
      customPermissions: ["Procurement.View", "Receiving.View", "Payables.View"],
      u1Entitled: true,
    });
    await registerLpiRoutes(page);

    await page.goto("/?page=procure-to-pay");
    await expect(page.getByRole("heading", { name: "Logistics, Procurement & Inventory" })).toBeVisible();

    // Suppliers tab: + Add Supplier must NOT be visible
    await page.getByRole("button", { name: /Suppliers/ }).click();
    await expect(page.getByRole("button", { name: "+ Add Supplier" })).toHaveCount(0);

    // Requisitions tab: + Create Requisition must NOT be visible
    await page.getByRole("button", { name: /Requisitions/ }).click();
    await expect(page.getByRole("button", { name: "+ Create Requisition" })).toHaveCount(0);

    // Receiving tab: Receive Goods dropdown must NOT be visible
    await page.getByRole("button", { name: /Receiving \(RR\)/ }).click();
    await expect(page.locator("select").filter({ hasText: "Receive Goods Against PO..." })).toHaveCount(0);

    // Stock & Returns tab: Count Adjustment and Supplier Return must NOT be visible
    await page.getByRole("button", { name: /Stock & Returns/ }).click();
    await expect(page.getByRole("button", { name: "+ Count Adjustment" })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "+ Supplier Return" })).toHaveCount(0);

    // Accounts Payable tab: mutation buttons must NOT be visible
    await page.getByRole("button", { name: /Accounts Payable/ }).click();
    await expect(page.getByRole("button", { name: "+ Intake Supplier Invoice" })).toHaveCount(0);
    await expect(page.getByRole("button", { name: /Approve Variance/i })).toHaveCount(0);
    await expect(page.getByRole("button", { name: /Record Payment/i })).toHaveCount(0);

    expect(pageErrors).toEqual([]);
  });
});
