import { readJson } from "./http";

export interface LpiSupplier {
  id: string;
  code: string;
  name: string;
  contactPerson: string | null;
  contactNumber: string | null;
  email: string | null;
  address: string | null;
  tin: string | null;
  paymentTermsDays: number;
  leadTimeDays: number;
  notes: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface LpiUnitOfMeasure {
  id: string;
  code: string;
  name: string;
  isBaseUnit: boolean;
  conversionFactor: number;
}

export interface LpiInventoryLocation {
  id: string;
  code: string;
  name: string;
  locationType: string;
  description: string | null;
  isActive: boolean;
}

export interface PurchaseRequisitionItem {
  id: string;
  requisitionId: string;
  lineNumber: number;
  productId: string;
  productName: string;
  productSku: string | null;
  uomId: string;
  uomName: string;
  requestedQuantityX1000: number;
  approvedQuantityX1000: number;
  orderedQuantityX1000: number;
  receivedQuantityX1000: number;
  estimatedCostCents: number;
  estimatedTotalCents: number;
  preferredSupplierId: string | null;
  preferredSupplierName: string | null;
  notes: string | null;
}

export interface PurchaseRequisition {
  id: string;
  prNumber: string;
  requisitionDate: string;
  requiredDate: string | null;
  department: string | null;
  purpose: string | null;
  requestedByUserId: string;
  requestedByUserName: string | null;
  status: string;
  approvedByUserId: string | null;
  approvedByUserName: string | null;
  approvedAt: string | null;
  rejectionReason: string | null;
  notes: string | null;
  totalEstimatedCostCents: number;
  createdAt: string;
  updatedAt: string;
  items: PurchaseRequisitionItem[];
}

export interface SupplierQuotationItem {
  id: string;
  quotationId: string;
  prItemId: string;
  productId: string;
  productName: string;
  quantityX1000: number;
  quotedUnitCostCents: number;
  quotedTotalCents: number;
  leadTimeDays: number;
  notes: string | null;
}

export interface SupplierQuotation {
  id: string;
  requisitionId: string;
  supplierId: string;
  supplierName: string;
  quoteReference: string | null;
  quoteDate: string;
  validUntil: string | null;
  quotedUnitCostCents: number;
  minimumOrderQtyX1000: number;
  totalQuotedCostCents: number;
  leadTimeDays: number;
  paymentTermsDays: number;
  status: string;
  isWinner: boolean;
  selectionJustification: string | null;
  notes: string | null;
  createdAt: string;
  items: SupplierQuotationItem[];
}

export interface PurchaseOrderItem {
  id: string;
  purchaseOrderId: string;
  lineNumber: number;
  prItemId: string | null;
  productId: string;
  productName: string;
  productSku: string | null;
  uomId: string;
  uomName: string;
  orderedQuantityX1000: number;
  unitCostCents: number;
  totalCostCents: number;
  receivedQuantityX1000: number;
  receivedAcceptedQtyX1000?: number;
  receivedRejectedQtyX1000?: number;
  outstandingQuantityX1000?: number;
  returnedQuantityX1000: number;
  invoicedQuantityX1000: number;
  notes: string | null;
}

export interface PurchaseOrder {
  id: string;
  poNumber: string;
  poDate: string;
  requisitionId: string | null;
  supplierId: string;
  supplierName: string;
  supplierCode: string;
  expectedDeliveryDate: string | null;
  deliveryLocationId?: string | null;
  deliveryLocationName?: string | null;
  paymentTermsDays: number;
  deliveryAddress: string | null;
  status: string;
  notes: string | null;
  subtotalCents: number;
  discountCents: number;
  taxCents: number;
  shippingCents: number;
  totalCents: number;
  approvedByUserId: string | null;
  approvedByUserName: string | null;
  approvedAt: string | null;
  sentAt: string | null;
  cancelledAt: string | null;
  cancellationReason: string | null;
  createdByUserId: string;
  createdAt: string;
  updatedAt: string;
  items: PurchaseOrderItem[];
}

export interface ReceivingReportItem {
  id: string;
  receivingReportId: string;
  poItemId: string;
  productId: string;
  productName: string;
  uomId: string;
  uomName: string;
  deliveredQuantityX1000: number;
  acceptedQuantityX1000: number;
  rejectedQuantityX1000: number;
  unitCostCents: number;
  totalAcceptedCostCents: number;
  targetLocationId: string;
  targetLocationName: string;
  quarantineLocationId: string | null;
  quarantineLocationName: string | null;
  rejectionReason: string | null;
  dispositionNotes: string | null;
}

export interface ReceivingReport {
  id: string;
  rrNumber: string;
  purchaseOrderId: string;
  poNumber: string;
  supplierId: string;
  supplierName: string;
  receivedDate: string;
  deliveryReceiptNumber: string | null;
  carrier: string | null;
  driverName: string | null;
  status: string;
  totalDeliveredQuantityX1000: number;
  totalAcceptedQuantityX1000: number;
  totalRejectedQuantityX1000: number;
  totalAcceptedCostCents: number;
  isOverdelivery: boolean;
  overdeliveryApprovedByUserId: string | null;
  notes: string | null;
  receivedByUserId: string;
  receivedByUserName: string | null;
  createdAt: string;
  items: ReceivingReportItem[];
}

export interface SupplierInvoiceItem {
  id: string;
  invoiceId: string;
  poItemId: string | null;
  rrItemId: string | null;
  productId: string;
  productName: string;
  poOrderedQuantityX1000: number;
  invoicedQuantityX1000: number;
  unitCostCents: number;
  taxCents: number;
  totalCents: number;
  poUnitCostCents: number;
  rrDeliveredQuantityX1000: number;
  quantityVarianceX1000: number;
  priceVarianceCents: number;
  matchStatus: string;
}

export interface SupplierInvoice {
  id: string;
  apvNumber: string;
  supplierId: string;
  supplierName: string;
  invoiceNumber: string;
  invoiceDate: string;
  dueDate: string;
  purchaseOrderId: string | null;
  poNumber: string | null;
  receivingReportId: string | null;
  rrNumber: string | null;
  subtotalCents: number;
  discountCents: number;
  taxCents: number;
  withholdingTaxCents: number;
  totalCents: number;
  creditMemoAppliedCents: number;
  amountPaidCents: number;
  balanceDueCents: number;
  daysPastDue: number;
  agingBucket: string;
  matchStatus: string;
  status: string;
  notes: string | null;
  varianceApprovedByUserId: string | null;
  varianceApprovalReason: string | null;
  varianceApprovedAt: string | null;
  varianceNotes: string | null;
  recordedByUserId: string | null;
  postedByUserId: string | null;
  postedAt: string | null;
  createdAt: string;
  items: SupplierInvoiceItem[];
}

export interface SupplierPayableEntry {
  id: string;
  supplierId: string;
  supplierName: string;
  supplierInvoiceId: string;
  apVoucherNumber: string;
  entryType: string;
  referenceNumber: string;
  debitCents: number;
  creditCents: number;
  balanceAfterCents: number;
  note: string | null;
  actorUserId: string;
  createdAt: string;
}

export interface SupplierPayment {
  id: string;
  paymentNumber: string;
  supplierId: string;
  supplierName: string;
  invoiceId: string;
  invoiceNumber: string;
  paymentDate: string; // Original requested/recorded date; never overwritten on settlement
  actualDisbursementDate: string | null; // Operator-affirmed funds-transfer calendar date, unknown on legacy rows
  amountCents: number;
  paymentMethod: string;
  disbursementSource: string;
  status: string;
  referenceNumber: string | null;
  bankName: string | null;
  bankAccount: string | null; // Deprecated compatibility alias: this is a bank name, not a numbered account
  checkNumber: string | null;
  notes: string | null;
  recordedByUserId: string;
  approvedByUserId: string | null;
  approvedAt: string | null;
  createdAt: string;
}

export interface StockCardLocationBalance {
  locationId: string;
  locationCode: string;
  locationName: string;
  quantityX1000: number;
  valuationCents: number;
}

export interface ProductStockCard {
  productId: string;
  productName: string;
  productBarcode: string;
  unitName: string;
  totalQuantityX1000: number;
  totalValuationCents: number;
  averageUnitCostCents: number;
  locationBalances: StockCardLocationBalance[];
}

export interface ApAgingSummary {
  supplierId: string;
  supplierName: string;
  currentCents: number;
  days1To30Cents: number;
  days31To60Cents: number;
  days61To90Cents: number;
  over90DaysCents: number;
  totalOutstandingCents: number;
}

export interface ApAgingReport {
  totalOpenPayablesCents: number;
  currentBucketCents: number;
  days1To30Cents: number;
  days31To60Cents: number;
  days61To90Cents: number;
  daysOver90Cents: number;
  openInvoiceCount: number;
  overdueInvoiceCount: number;
  varianceFlaggedInvoiceCount: number;
  openInvoices: SupplierInvoice[];
  supplierSummaries: ApAgingSummary[];
}

export interface LpiDashboardOverview {
  totalActiveSuppliers: number;
  pendingPurchaseRequisitions: number;
  openPurchaseOrders: number;
  unmatchedInvoices: number;
  totalAccountsPayableCents: number;
  overduePayablesCents: number;
  totalInventoryValuationCents: number;
  costCoverageIncomplete?: boolean;
  missingCostProductCount?: number;
  quarantineStockQuantityX1000: number;
  recentOrders: PurchaseOrder[];
  agingSummary: ApAgingSummary[];
}

// PR requester catalog intentionally excludes suppliers, costs, terms, finance and stock.
export interface LpiRequesterCatalog {
  unitsOfMeasure: Array<{
    id: string;
    code: string;
    name: string;
    allowsDecimalQuantity: boolean;
  }>;
  products: Array<{
    id: string;
    name: string;
    barcode: string | null;
    unitId: string;
    unitName: string;
  }>;
}

export interface LpiFoundationCatalog {
  suppliers: LpiSupplier[];
  unitsOfMeasure: LpiUnitOfMeasure[];
  locations: LpiInventoryLocation[];
  products: Array<{
    id: string;
    name: string;
    barcode: string;
    unitId: string;
    unitName: string;
    costPriceCents: number;
    sellingPriceCents: number;
    stockOnHandX1000: number;
  }>;
}

type JsonRecord = Record<string, any>;

function normalizeRequisition(raw: JsonRecord): PurchaseRequisition {
  const lines = (raw.lines ?? raw.items ?? []).map((line: JsonRecord) => ({
    id: line.id,
    requisitionId: line.requisitionId,
    lineNumber: line.lineNumber,
    productId: line.productId ?? "",
    productName: line.itemDescription ?? line.productName ?? "Requested item",
    productSku: line.itemCode ?? null,
    uomId: line.uomId ?? "",
    uomName: line.unitName ?? line.uomName ?? "pc",
    requestedQuantityX1000: line.requestedQtyX1000 ?? line.requestedQuantityX1000 ?? 0,
    approvedQuantityX1000: line.approvedQtyX1000 ?? line.approvedQuantityX1000 ?? 0,
    orderedQuantityX1000: line.convertedPoQtyX1000 ?? line.orderedQuantityX1000 ?? 0,
    receivedQuantityX1000: line.receivedQuantityX1000 ?? 0,
    estimatedCostCents: line.estimatedUnitCostCents ?? line.estimatedCostCents ?? 0,
    estimatedTotalCents: line.estimatedLineTotalCents ?? line.estimatedTotalCents ?? 0,
    preferredSupplierId: line.preferredSupplierId ?? null,
    preferredSupplierName: line.preferredSupplierName ?? null,
    notes: line.notes ?? null,
  }));
  return {
    ...raw,
    requisitionDate: raw.dateRequested ?? raw.requisitionDate,
    requiredDate: raw.dateNeeded ?? raw.requiredDate ?? null,
    department: raw.departmentName ?? raw.department ?? null,
    requestedByUserName: raw.requestedByName ?? raw.requestedByUserName ?? null,
    approvedByUserName: raw.approvedByName ?? raw.approvedByUserName ?? null,
    rejectionReason: raw.rejectionOrRevisionReason ?? raw.rejectionReason ?? null,
    totalEstimatedCostCents: raw.estimatedTotalCents ?? raw.totalEstimatedCostCents ?? 0,
    items: lines,
  } as PurchaseRequisition;
}

function normalizeStockCard(raw: JsonRecord): ProductStockCard {
  return {
    productId: raw.productId,
    productName: raw.productName,
    productBarcode: raw.barcode ?? raw.sku ?? "",
    unitName: raw.unitName,
    totalQuantityX1000: raw.currentStockOnHandX1000 ?? raw.totalQuantityX1000 ?? 0,
    totalValuationCents: raw.inventoryValuationCents ?? raw.totalValuationCents ?? 0,
    averageUnitCostCents: raw.currentCostPriceCents ?? raw.averageUnitCostCents ?? 0,
    locationBalances: (raw.buckets ?? raw.locationBalances ?? []).map((bucket: JsonRecord) => ({
      locationId: bucket.locationId,
      locationCode: bucket.locationCode,
      locationName: bucket.locationName,
      quantityX1000: bucket.quantityX1000 ?? 0,
      valuationCents: (bucket.quantityX1000 ?? 0) * (raw.currentCostPriceCents ?? 0) / 1000,
    })),
  };
}

function normalizeReceivingReport(raw: JsonRecord): ReceivingReport {
  const lines = raw.lines ?? raw.items ?? [];
  return {
    ...raw,
    deliveryReceiptNumber: raw.deliveryReceiptNo ?? raw.deliveryReceiptNumber ?? null,
    carrier: raw.carrierOrDriver ?? raw.carrier ?? null,
    driverName: raw.carrierOrDriver ?? raw.driverName ?? null,
    totalDeliveredQuantityX1000: lines.reduce((sum: number, line: JsonRecord) => sum + (line.actualDeliveredQtyX1000 ?? line.deliveredQuantityX1000 ?? 0), 0),
    totalAcceptedQuantityX1000: lines.reduce((sum: number, line: JsonRecord) => sum + (line.acceptedQtyX1000 ?? line.acceptedQuantityX1000 ?? 0), 0),
    totalRejectedQuantityX1000: lines.reduce((sum: number, line: JsonRecord) => sum + (line.rejectedQtyX1000 ?? line.rejectedQuantityX1000 ?? 0), 0),
    totalAcceptedCostCents: lines.reduce((sum: number, line: JsonRecord) => sum + (line.acceptedLineTotalCents ?? line.totalAcceptedCostCents ?? 0), 0),
    isOverdelivery: raw.hasOverdelivery ?? raw.isOverdelivery ?? false,
    receivedByUserName: raw.receivedByName ?? raw.receivedByUserName ?? null,
    items: lines.map((line: JsonRecord) => ({
      id: line.id,
      receivingReportId: line.receivingReportId,
      poItemId: line.purchaseOrderLineId ?? line.poItemId,
      productId: line.productId ?? "",
      productName: line.itemDescription ?? line.productName ?? "Received item",
      uomId: line.uomId ?? "",
      uomName: line.unitName ?? line.uomName ?? "pc",
      deliveredQuantityX1000: line.actualDeliveredQtyX1000 ?? line.deliveredQuantityX1000 ?? 0,
      acceptedQuantityX1000: line.acceptedQtyX1000 ?? line.acceptedQuantityX1000 ?? 0,
      rejectedQuantityX1000: line.rejectedQtyX1000 ?? line.rejectedQuantityX1000 ?? 0,
      unitCostCents: line.unitCostCents ?? 0,
      totalAcceptedCostCents: line.acceptedLineTotalCents ?? line.totalAcceptedCostCents ?? 0,
      targetLocationId: raw.locationId ?? "",
      targetLocationName: raw.locationName ?? "",
      quarantineLocationId: null,
      quarantineLocationName: null,
      rejectionReason: line.rejectionReason ?? null,
      dispositionNotes: line.rejectionDisposition ?? line.dispositionNotes ?? null,
    })),
  } as ReceivingReport;
}

function normalizeSupplierReturn(raw: JsonRecord): SupplierReturnSummary {
  return {
    id: raw.id,
    returnNumber: raw.returnNumber,
    supplierId: raw.supplierId,
    supplierName: raw.supplierName,
    purchaseOrderId: raw.purchaseOrderId ?? null,
    receivingReportId: raw.receivingReportId ?? null,
    returnDate: raw.returnDate,
    totalCostCents: raw.totalReturnAmountCents ?? raw.totalCostCents ?? 0,
    reason: raw.reason ?? "",
    status: raw.status,
    items: (raw.lines ?? raw.items ?? []).map((line: JsonRecord) => ({
      productId: line.productId,
      productName: line.productName,
      quantityX1000: line.returnQtyX1000 ?? line.quantityX1000 ?? 0,
      unitCostCents: line.unitCostCents ?? 0,
      totalCostCents: line.lineTotalCents ?? line.totalCostCents ?? 0,
      sourceLocationId: line.sourceLocationId ?? "",
      reason: line.reason ?? raw.reason ?? "",
    })),
  };
}

function normalizeQuotation(raw: JsonRecord): SupplierQuotation {
  const quotedUnitCostCents = raw.quotedUnitCostCents ?? raw.unitCostCents ?? 0;
  const minimumOrderQtyX1000 = raw.minimumOrderQtyX1000 ?? raw.quantityX1000 ?? 1000;
  const totalQuotedCostCents = raw.totalQuotedCostCents ?? Math.round(quotedUnitCostCents * Math.max(minimumOrderQtyX1000, 1000) / 1000);
  return {
    id: raw.id,
    requisitionId: raw.requisitionId ?? "",
    supplierId: raw.supplierId ?? "",
    supplierName: raw.supplierName ?? "Supplier",
    quoteReference: raw.selectionReason ?? raw.quoteReference ?? ("Q-" + (raw.id?.slice(0, 8) ?? "REF")),
    quoteDate: raw.createdAt ? new Date(raw.createdAt).toISOString().slice(0, 10) : (raw.quoteDate ?? ""),
    validUntil: raw.validityDate ?? raw.validUntil ?? null,
    quotedUnitCostCents,
    minimumOrderQtyX1000,
    totalQuotedCostCents,
    leadTimeDays: raw.leadTimeDays ?? 0,
    paymentTermsDays: raw.paymentTermsDays ?? 0,
    status: raw.isSelected ? "Selected" : (raw.status ?? "Submitted"),
    isWinner: raw.isSelected ?? raw.isWinner ?? false,
    selectionJustification: raw.selectionReason ?? raw.selectionJustification ?? null,
    notes: raw.notes ?? null,
    createdAt: raw.createdAt ?? "",
    items: (raw.items ?? []).map((it: JsonRecord) => ({
      id: it.id,
      quotationId: raw.id,
      prItemId: it.requisitionLineId ?? it.prItemId ?? "",
      productId: it.productId ?? "",
      productName: it.itemDescription ?? it.productName ?? "Quoted Item",
      quantityX1000: it.quantityX1000 ?? minimumOrderQtyX1000,
      quotedUnitCostCents: it.quotedUnitCostCents ?? quotedUnitCostCents,
      quotedTotalCents: it.quotedTotalCents ?? totalQuotedCostCents,
      leadTimeDays: it.leadTimeDays ?? raw.leadTimeDays ?? 0,
      notes: it.notes ?? null,
    })),
  };
}

function normalizePurchaseOrder(raw: JsonRecord): PurchaseOrder {
  const lines = raw.lines ?? raw.items ?? [];
  // Required authoritative cents must stay explicit. Never derive or fabricate them.
  // Missing or malformed amounts normalize to NaN so the print gate fails closed.
  const amount = (value: unknown): number =>
    typeof value === "number" && Number.isSafeInteger(value) && value >= 0 ? value : Number.NaN;
  const totalCents = amount(raw.totalAmountCents ?? raw.totalCents);
  const taxCents = amount(raw.taxCents ?? raw.totalTaxCents);
  const subtotalCents = amount(raw.subtotalCents ?? raw.subtotalAmountCents);
  const discountCents = amount(raw.discountCents);
  const shippingCents = amount(raw.shippingCents);
  return {
    id: raw.id,
    poNumber: raw.poNumber ?? "",
    supplierId: raw.supplierId ?? "",
    supplierName: raw.supplierName ?? "Supplier",
    supplierCode: raw.supplierCode ?? null,
    sourceRequisitionId: raw.sourceRequisitionId ?? null,
    sourcePrNumber: raw.sourcePrNumber ?? null,
    poDate: raw.orderDate ?? raw.poDate ?? (raw.createdAt ? new Date(raw.createdAt).toISOString().slice(0, 10) : ""),
    expectedDeliveryDate: raw.expectedDeliveryDate ?? null,
    deliveryLocationId: raw.deliveryLocationId ?? "",
    deliveryLocationName: raw.deliveryLocationName ?? "Main Store",
    paymentTermsDays: raw.paymentTermsDays ?? 0,
    status: raw.status ?? "Draft",
    subtotalCents,
    discountCents,
    taxCents,
    shippingCents,
    totalCents,
    items: lines.map((line: JsonRecord) => {
      const ordered = line.orderedQtyX1000 ?? line.orderedQuantityX1000 ?? 0;
      const accepted = line.receivedAcceptedQtyX1000 ?? line.receivedQtyX1000 ?? line.receivedQuantityX1000 ?? 0;
      const rejected = line.receivedRejectedQtyX1000 ?? 0;
      const outstanding = line.outstandingQtyX1000 ?? Math.max(0, ordered - accepted);
      return {
        id: line.id,
        poId: raw.id,
        prItemId: line.requisitionLineId ?? line.prItemId ?? null,
        productId: line.productId ?? "",
        productName: line.itemDescription ?? line.productName ?? "Item",
        productSku: line.itemCode ?? line.productSku ?? null,
        uomId: line.unitId ?? line.uomId ?? "",
        uomName: line.unitName ?? line.uomName ?? "pc",
        orderedQuantityX1000: ordered,
        receivedQuantityX1000: accepted,
        receivedAcceptedQtyX1000: accepted,
        receivedRejectedQtyX1000: rejected,
        outstandingQuantityX1000: outstanding,
        unitCostCents: amount(line.unitCostCents),
        totalCostCents: amount(line.lineTotalCents ?? line.totalCostCents),
      };
    }),
  } as unknown as PurchaseOrder;
}

function normalizeSupplierInvoice(raw: JsonRecord): SupplierInvoice {
  const lines = raw.lines ?? raw.items ?? [];
  return {
    id: raw.id,
    apvNumber: raw.apVoucherNumber ?? raw.apvNumber ?? ("APV-" + (raw.id?.slice(0, 8) ?? "0000")),
    supplierId: raw.supplierId ?? "",
    supplierName: raw.supplierName ?? "Supplier",
    invoiceNumber: raw.supplierInvoiceNo ?? raw.invoiceNumber ?? ("INV-" + (raw.id?.slice(0, 8) ?? "0000")),
    purchaseOrderId: raw.purchaseOrderId ?? null,
    poNumber: raw.poNumber ?? null,
    receivingReportId: raw.receivingReportId ?? null,
    rrNumber: raw.rrNumber ?? null,
    invoiceDate: raw.invoiceDate ?? "",
    dueDate: raw.dueDate ?? "",
    subtotalCents: raw.subtotalCents ?? 0,
    discountCents: raw.discountCents ?? 0,
    taxCents: raw.taxCents ?? 0,
    withholdingTaxCents: raw.withholdingTaxCents ?? 0,
    totalCents: raw.totalInvoiceCents ?? raw.totalCents ?? 0,
    creditMemoAppliedCents: raw.creditMemoAppliedCents ?? 0,
    amountPaidCents: raw.paidAmountCents ?? raw.amountPaidCents ?? 0,
    balanceDueCents: raw.remainingBalanceCents ?? raw.balanceDueCents ?? 0,
    daysPastDue: raw.daysPastDue ?? 0,
    agingBucket: raw.agingBucket ?? "Current",
    matchStatus: raw.matchStatus ?? "PendingDocuments",
    status: raw.status ?? "Draft",
    notes: raw.notes ?? raw.varianceNotes ?? null,
    varianceApprovedByUserId: raw.varianceApprovedByUserId ?? null,
    varianceApprovalReason: raw.varianceApprovalReason ?? null,
    varianceApprovedAt: raw.varianceApprovedAt ?? null,
    varianceNotes: raw.varianceNotes ?? null,
    recordedByUserId: raw.recordedByUserId ?? null,
    postedByUserId: raw.postedByUserId ?? null,
    postedAt: raw.postedAt ?? null,
    createdAt: raw.createdAt ?? "",
    items: lines.map((line: JsonRecord) => {
      const lineTotal = line.lineTotalCents ?? line.totalCostCents ?? line.totalCents ?? 0;
      return {
        id: line.id,
        invoiceId: raw.id,
        poItemId: line.purchaseOrderLineId ?? line.poItemId ?? null,
        rrItemId: line.receivingReportLineId ?? line.rrItemId ?? null,
        productId: line.productId ?? "",
        productName: line.itemDescription ?? line.productName ?? "Item",
        poOrderedQuantityX1000: line.poOrderedQtyX1000 ?? line.poOrderedQuantityX1000 ?? 0,
        invoicedQuantityX1000: line.invoicedQtyX1000 ?? line.invoicedQuantityX1000 ?? 0,
        unitCostCents: line.invoicedUnitCostCents ?? line.unitCostCents ?? 0,
        taxCents: line.taxCents ?? 0,
        totalCents: lineTotal,
        poUnitCostCents: line.poUnitCostCents ?? 0,
        rrDeliveredQuantityX1000: line.rrAcceptedQtyX1000 ?? line.rrDeliveredQuantityX1000 ?? 0,
        quantityVarianceX1000: line.qtyVarianceX1000 ?? line.quantityVarianceX1000 ?? 0,
        priceVarianceCents: line.unitCostVarianceCents ?? line.priceVarianceCents ?? 0,
        matchStatus: line.lineMatchStatus ?? line.matchStatus ?? "Matched",
      };
    }),
  };
}

function normalizeSupplierPayableEntry(raw: JsonRecord): SupplierPayableEntry {
  return {
    id: raw.id ?? "",
    supplierId: raw.supplierId ?? "",
    supplierName: raw.supplierName ?? "Unknown Supplier",
    supplierInvoiceId: raw.supplierInvoiceId ?? "",
    apVoucherNumber: raw.apVoucherNumber ?? "",
    entryType: raw.entryType ?? "",
    referenceNumber: raw.referenceNumber ?? "",
    debitCents: raw.debitCents ?? 0,
    creditCents: raw.creditCents ?? 0,
    balanceAfterCents: raw.balanceAfterCents ?? 0,
    note: raw.note ?? null,
    actorUserId: raw.actorUserId ?? "",
    createdAt: raw.createdAt ?? "",
  };
}

function aggregateApAgingBySupplier(openInvoices: JsonRecord[]): ApAgingSummary[] {
  const supplierMap = new Map<string, ApAgingSummary>();
  for (const inv of openInvoices) {
    const sId = inv.supplierId || "unknown";
    const sName = inv.supplierName || "Supplier";
    const cur = supplierMap.get(sId) ?? {
      supplierId: sId,
      supplierName: sName,
      currentCents: 0,
      days1To30Cents: 0,
      days31To60Cents: 0,
      days61To90Cents: 0,
      over90DaysCents: 0,
      totalOutstandingCents: 0,
    };
    const rem = inv.remainingBalanceCents ?? inv.balanceDueCents ?? 0;
    const pastDue = inv.daysPastDue ?? 0;
    cur.totalOutstandingCents += rem;
    if (pastDue <= 0) cur.currentCents += rem;
    else if (pastDue <= 30) cur.days1To30Cents += rem;
    else if (pastDue <= 60) cur.days31To60Cents += rem;
    else if (pastDue <= 90) cur.days61To90Cents += rem;
    else cur.over90DaysCents += rem;
    supplierMap.set(sId, cur);
  }
  return Array.from(supplierMap.values());
}

function normalizeSupplierPayment(raw: JsonRecord): SupplierPayment {
  return {
    id: raw.id,
    paymentNumber: raw.paymentNumber ?? "",
    supplierId: raw.supplierId ?? "",
    supplierName: raw.supplierName ?? "Supplier",
    invoiceId: raw.supplierInvoiceId ?? raw.invoiceId ?? "",
    invoiceNumber: raw.supplierInvoiceNo ?? raw.invoiceNumber ?? raw.apVoucherNumber ?? "",
    paymentDate: raw.paymentDate ?? "",
    actualDisbursementDate: raw.actualDisbursementDate ?? null,
    amountCents: raw.amountCents ?? 0,
    paymentMethod: raw.paymentMethod ?? "BankTransfer",
    disbursementSource: raw.disbursementSource ?? "OperatingAccount",
    status: raw.status ?? "PendingApproval",
    referenceNumber: raw.referenceNo ?? raw.referenceNumber ?? null,
    bankName: raw.bankName ?? raw.bankAccount ?? null,
    bankAccount: raw.bankName ?? raw.bankAccount ?? null,
    checkNumber: raw.checkNo ?? raw.checkNumber ?? null,
    notes: raw.remarks ?? raw.notes ?? null,
    recordedByUserId: raw.recordedByUserId ?? "",
    approvedByUserId: raw.approvedByUserId ?? null,
    approvedAt: raw.approvedAt ?? null,
    createdAt: raw.createdAt ?? "",
  } as unknown as SupplierPayment;
}

export interface SupplierReturnSummary {
  id: string;
  returnNumber: string;
  supplierId: string;
  supplierName: string;
  purchaseOrderId: string | null;
  receivingReportId: string | null;
  returnDate: string;
  totalCostCents: number;
  reason: string;
  status: string;
  items: Array<{
    productId: string;
    productName: string;
    quantityX1000: number;
    unitCostCents: number;
    totalCostCents: number;
    sourceLocationId: string;
    reason: string;
  }>;
}

// API functions
export async function fetchLpiFoundation(includeInactiveSuppliers = false): Promise<LpiFoundationCatalog> {
  const response = await fetch(`/api/lpi/foundation?includeInactiveSuppliers=${includeInactiveSuppliers}`);
  return readJson<LpiFoundationCatalog>(response);
}

export async function fetchLpiRequesterCatalog(): Promise<LpiRequesterCatalog> {
  const response = await fetch("/api/lpi/requisition-catalog");
  return readJson<LpiRequesterCatalog>(response);
}

export async function fetchLpiOverview(): Promise<LpiDashboardOverview> {
  const response = await fetch("/api/lpi/overview");
  const raw = await readJson<JsonRecord>(response);
  const aging = raw.apAging ?? {};
  const openInvoices: JsonRecord[] = aging.openInvoices ?? [];

  const computedAging = aggregateApAgingBySupplier(openInvoices);
  const fallbackAging = (aging.totalOpenPayablesCents ?? raw.totalOpenPayablesCents ?? 0) > 0 ? [{
    supplierId: "all-open-payables",
    supplierName: "All Open Accounts Payable",
    currentCents: aging.currentBucketCents ?? 0,
    days1To30Cents: aging.days1To30Cents ?? 0,
    days31To60Cents: aging.days31To60Cents ?? 0,
    days61To90Cents: aging.days61To90Cents ?? 0,
    over90DaysCents: aging.daysOver90Cents ?? 0,
    totalOutstandingCents: aging.totalOpenPayablesCents ?? raw.totalOpenPayablesCents ?? 0,
  }] : [];

  return {
    totalActiveSuppliers: raw.activeSupplierCount ?? raw.totalActiveSuppliers ?? 0,
    pendingPurchaseRequisitions: raw.pendingRequisitionCount ?? raw.pendingPurchaseRequisitions ?? 0,
    openPurchaseOrders: raw.openPurchaseOrderCount ?? raw.openPurchaseOrders ?? 0,
    unmatchedInvoices: raw.varianceFlaggedInvoiceCount ?? raw.unmatchedInvoices ?? 0,
    totalAccountsPayableCents: raw.totalOpenPayablesCents ?? raw.totalAccountsPayableCents ?? 0,
    overduePayablesCents: raw.overduePayablesCents ?? 0,
    totalInventoryValuationCents: raw.totalInventoryValuationCents ?? 0,
    costCoverageIncomplete: raw.costCoverageIncomplete ?? false,
    missingCostProductCount: raw.missingCostProductCount ?? 0,
    quarantineStockQuantityX1000: raw.quarantineStockQuantityX1000 ?? 0,
    recentOrders: (raw.recentOrders ?? []).map(normalizePurchaseOrder),
    agingSummary: computedAging.length > 0 ? computedAging : fallbackAging,
  };
}

export async function fetchSuppliers(search?: string, includeInactive = false): Promise<LpiSupplier[]> {
  const params = new URLSearchParams();
  if (search) params.set("search", search);
  params.set("includeInactive", includeInactive ? "true" : "false");
  const response = await fetch(`/api/lpi/suppliers?${params.toString()}`);
  return readJson<LpiSupplier[]>(response);
}

export async function createSupplier(data: {
  code: string;
  name: string;
  contactPerson?: string;
  contactNumber?: string;
  email?: string;
  address?: string;
  tin?: string;
  paymentTermsDays?: number;
  leadTimeDays?: number;
  notes?: string;
}): Promise<LpiSupplier> {
  const response = await fetch("/api/lpi/suppliers", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(data),
  });
  return readJson<LpiSupplier>(response);
}

export async function updateSupplier(id: string, data: {
  code: string;
  name: string;
  contactPerson?: string;
  contactNumber?: string;
  email?: string;
  address?: string;
  tin?: string;
  paymentTermsDays?: number;
  leadTimeDays?: number;
  notes?: string;
  isActive?: boolean;
}): Promise<LpiSupplier> {
  const response = await fetch(`/api/lpi/suppliers/${id}`, {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(data),
  });
  return readJson<LpiSupplier>(response);
}

export async function fetchRequisitions(status?: string): Promise<PurchaseRequisition[]> {
  const url = status ? `/api/lpi/requisitions?status=${encodeURIComponent(status)}` : "/api/lpi/requisitions";
  const response = await fetch(url);
  return (await readJson<JsonRecord[]>(response)).map(normalizeRequisition);
}

export async function fetchRequisitionById(id: string): Promise<PurchaseRequisition> {
  const response = await fetch(`/api/lpi/requisitions/${id}`);
  return normalizeRequisition(await readJson<JsonRecord>(response));
}

export async function createRequisition(data: {
  department?: string;
  purpose?: string;
  requiredDate?: string;
  notes?: string;
  items: Array<{
    productId: string;
    uomId: string;
    requestedQuantityX1000: number;
    estimatedCostCents: number;
    preferredSupplierId?: string;
    notes?: string;
  }>;
}): Promise<PurchaseRequisition> {
  const response = await fetch("/api/lpi/requisitions", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(data),
  });
  return normalizeRequisition(await readJson<JsonRecord>(response));
}

export async function submitRequisition(id: string): Promise<PurchaseRequisition> {
  const response = await fetch(`/api/lpi/requisitions/${id}/submit`, { method: "POST" });
  return normalizeRequisition(await readJson<JsonRecord>(response));
}

export async function approveRequisition(id: string, notes?: string): Promise<PurchaseRequisition> {
  const response = await fetch(`/api/lpi/requisitions/${id}/approve`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ notes }),
  });
  return normalizeRequisition(await readJson<JsonRecord>(response));
}

export async function rejectRequisition(id: string, reason: string): Promise<PurchaseRequisition> {
  const response = await fetch(`/api/lpi/requisitions/${id}/reject`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ reason }),
  });
  return normalizeRequisition(await readJson<JsonRecord>(response));
}

export async function reviseRequisition(id: string, revisionNotes: string): Promise<PurchaseRequisition> {
  const response = await fetch(`/api/lpi/requisitions/${id}/revise`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ revisionNotes }),
  });
  return readJson<PurchaseRequisition>(response);
}

export async function convertRequisitionToPo(id: string, allocations: Array<{
  supplierId: string;
  prItemIds: string[];
  expectedDeliveryDate?: string;
  notes?: string;
}>): Promise<PurchaseOrder[]> {
  const created: PurchaseOrder[] = [];
  for (const allocation of allocations) {
    const response = await fetch(`/api/lpi/requisitions/${id}/convert-to-po`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        supplierId: allocation.supplierId,
        requisitionLineIds: allocation.prItemIds,
        expectedDeliveryDate: allocation.expectedDeliveryDate,
      }),
    });
    created.push(await readJson<PurchaseOrder>(response));
  }
  return created;
}

export async function fetchQuotations(requisitionId: string): Promise<SupplierQuotation[]> {
  const response = await fetch(`/api/lpi/quotations?requisitionId=${requisitionId}`);
  return (await readJson<JsonRecord[]>(response)).map(normalizeQuotation);
}

export interface RecordQuotationPayload {
  requisitionId: string;
  requisitionLineId?: string;
  supplierId: string;
  productId?: string;
  itemDescription?: string;
  quotedUnitCostCents?: number;
  minimumOrderQtyX1000?: number;
  leadTimeDays?: number;
  paymentTermsDays?: number;
  validityDate?: string;
  isSelected?: boolean;
  selectionReason?: string;
  items?: Array<{
    prItemId?: string;
    productId?: string;
    quantityX1000?: number;
    quotedUnitCostCents?: number;
    leadTimeDays?: number;
    notes?: string;
  }>;
}

export async function createQuotation(data: RecordQuotationPayload): Promise<SupplierQuotation> {
  const payload = {
    requisitionId: data.requisitionId,
    requisitionLineId: data.requisitionLineId ?? (data.items && data.items.length > 0 ? data.items[0].prItemId : undefined),
    supplierId: data.supplierId,
    productId: data.productId ?? (data.items && data.items.length > 0 ? data.items[0].productId : undefined),
    itemDescription: data.itemDescription ?? (data.items && data.items.length > 0 ? data.items[0].notes : "Quoted Item"),
    quotedUnitCostCents: data.quotedUnitCostCents ?? (data.items && data.items.length > 0 ? data.items[0].quotedUnitCostCents : 0),
    minimumOrderQtyX1000: data.minimumOrderQtyX1000 ?? (data.items && data.items.length > 0 ? data.items[0].quantityX1000 : 1000),
    leadTimeDays: data.leadTimeDays,
    paymentTermsDays: data.paymentTermsDays,
    validityDate: data.validityDate,
    isSelected: data.isSelected,
    selectionReason: data.selectionReason,
  };
  const response = await fetch("/api/lpi/quotations", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });
  return readJson<SupplierQuotation>(response);
}

export async function selectWinningQuotation(id: string, justification: string): Promise<SupplierQuotation> {
  const response = await fetch(`/api/lpi/quotations/${id}/select-winner`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ justification }),
  });
  return readJson<SupplierQuotation>(response);
}

export async function fetchPurchaseOrders(status?: string, supplierId?: string): Promise<PurchaseOrder[]> {
  const params = new URLSearchParams();
  if (status) params.set("status", status);
  if (supplierId) params.set("supplierId", supplierId);
  const response = await fetch(`/api/lpi/purchase-orders?${params.toString()}`);
  return (await readJson<JsonRecord[]>(response)).map(normalizePurchaseOrder);
}

export async function fetchPurchaseOrderById(id: string): Promise<PurchaseOrder> {
  const response = await fetch(`/api/lpi/purchase-orders/${id}`);
  return normalizePurchaseOrder(await readJson<JsonRecord>(response));
}

export async function createPurchaseOrder(data: {
  supplierId: string;
  requisitionId?: string;
  expectedDeliveryDate?: string;
  deliveryAddress?: string;
  paymentTermsDays?: number;
  notes?: string;
  submitAndApproveImmediately?: boolean;
  items: Array<{
    prItemId?: string;
    productId: string;
    productName: string;
    unitName: string;
    orderedQuantityX1000: number;
    unitCostCents: number;
    notes?: string;
  }>;
}): Promise<PurchaseOrder> {
  const response = await fetch("/api/lpi/purchase-orders", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      supplierId: data.supplierId,
      sourceRequisitionId: data.requisitionId,
      expectedDeliveryDate: data.expectedDeliveryDate,
      paymentTermsDays: data.paymentTermsDays,
      notes: data.notes,
      submitAndApproveImmediately: data.submitAndApproveImmediately ?? false,
      lines: data.items.map((item) => ({
        requisitionLineId: item.prItemId,
        productId: item.productId,
        itemDescription: item.productName,
        unitName: item.unitName,
        orderedQtyX1000: item.orderedQuantityX1000,
        unitCostCents: item.unitCostCents,
        notes: item.notes,
      })),
    }),
  });
  return normalizePurchaseOrder(await readJson<JsonRecord>(response));
}

export async function approvePurchaseOrder(id: string, notes?: string): Promise<PurchaseOrder> {
  const response = await fetch(`/api/lpi/purchase-orders/${id}/approve`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ notes }),
  });
  return normalizePurchaseOrder(await readJson<JsonRecord>(response));
}

export async function markPurchaseOrderSent(id: string): Promise<PurchaseOrder> {
  const response = await fetch(`/api/lpi/purchase-orders/${id}/mark-sent`, { method: "POST" });
  return normalizePurchaseOrder(await readJson<JsonRecord>(response));
}

export async function cancelPurchaseOrder(id: string, reason: string): Promise<PurchaseOrder> {
  const response = await fetch(`/api/lpi/purchase-orders/${id}/cancel`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ reason }),
  });
  return normalizePurchaseOrder(await readJson<JsonRecord>(response));
}

export async function fetchReceivingReports(poId?: string): Promise<ReceivingReport[]> {
  const url = poId ? `/api/lpi/receiving-reports?purchaseOrderId=${poId}` : "/api/lpi/receiving-reports";
  const response = await fetch(url);
  return (await readJson<JsonRecord[]>(response)).map(normalizeReceivingReport);
}

export async function fetchReceivingReportById(id: string): Promise<ReceivingReport> {
  const response = await fetch(`/api/lpi/receiving-reports/${id}`);
  return normalizeReceivingReport(await readJson<JsonRecord>(response));
}

export async function createReceivingReport(data: {
  purchaseOrderId: string;
  locationId?: string;
  deliveryReceiptNumber?: string;
  deliveryReceiptNo?: string;
  carrier?: string;
  driverName?: string;
  notes?: string;
  items: Array<{
    poItemId: string;
    productId: string;
    uomId: string;
    deliveredQuantityX1000: number;
    acceptedQuantityX1000: number;
    rejectedQuantityX1000: number;
    targetLocationId?: string;
    quarantineLocationId?: string;
    rejectionReason?: string;
    dispositionNotes?: string;
  }>;
}): Promise<ReceivingReport> {
  const payload = {
    purchaseOrderId: data.purchaseOrderId,
    locationId: data.locationId || data.items?.[0]?.targetLocationId || null,
    deliveryReceiptNo: data.deliveryReceiptNumber || data.deliveryReceiptNo,
    carrierOrDriver: [data.carrier, data.driverName].filter(Boolean).join(" - ") || (data.carrier || "Dock Carrier"),
    postImmediately: true,
    notes: data.notes,
    lines: data.items.map((it) => ({
      purchaseOrderLineId: it.poItemId,
      actualDeliveredQtyX1000: it.deliveredQuantityX1000,
      acceptedQtyX1000: it.acceptedQuantityX1000,
      rejectedQtyX1000: it.rejectedQuantityX1000,
      rejectionReason: it.rejectionReason || null,
      rejectionDisposition: it.dispositionNotes || null,
    })),
  };
  const response = await fetch("/api/lpi/receiving-reports", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });
  return normalizeReceivingReport(await readJson<JsonRecord>(response));
}

export async function fetchStockCards(): Promise<ProductStockCard[]> {
  const response = await fetch("/api/lpi/stock-cards");
  return (await readJson<JsonRecord[]>(response)).map(normalizeStockCard);
}

export async function fetchStockCardByProduct(productId: string): Promise<ProductStockCard> {
  const response = await fetch(`/api/lpi/stock-cards/${productId}`);
  return normalizeStockCard(await readJson<JsonRecord>(response));
}

export async function createStockAdjustment(data: {
  productId: string;
  locationId: string;
  systemQuantityX1000: number;
  actualQuantityX1000: number;
  unitCostCents: number;
  reason: string;
}): Promise<{ adjustmentId: string; movementId: string; varianceQuantityX1000: number; varianceValuationCents: number }> {
  const response = await fetch("/api/lpi/inventory-adjustments", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      productId: data.productId,
      locationId: data.locationId,
      actualCountedQtyX1000: data.actualQuantityX1000,
      reasonCode: "CycleCount",
      notes: data.reason,
    }),
  });
  return readJson(response);
}

export async function fetchSupplierReturns(supplierId?: string): Promise<SupplierReturnSummary[]> {
  const url = supplierId ? `/api/lpi/supplier-returns?supplierId=${supplierId}` : "/api/lpi/supplier-returns";
  const response = await fetch(url);
  return (await readJson<JsonRecord[]>(response)).map(normalizeSupplierReturn);
}

export async function createSupplierReturn(data: {
  receivingReportId: string;
  supplierInvoiceId?: string;
  reason: string;
  lines: Array<{
    receivingReportLineId: string;
    returnQtyX1000: number;
    reason: string;
  }>;
}): Promise<unknown> {
  const response = await fetch("/api/lpi/supplier-returns", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(data),
  });
  return readJson(response);
}

export async function fetchSupplierInvoices(status?: string, supplierId?: string): Promise<SupplierInvoice[]> {
  const params = new URLSearchParams();
  if (status) params.set("status", status);
  if (supplierId) params.set("supplierId", supplierId);
  const response = await fetch(`/api/lpi/invoices?${params.toString()}`);
  return (await readJson<JsonRecord[]>(response)).map(normalizeSupplierInvoice);
}

export async function fetchSupplierInvoiceById(id: string): Promise<SupplierInvoice> {
  const response = await fetch(`/api/lpi/invoices/${id}`);
  return normalizeSupplierInvoice(await readJson<JsonRecord>(response));
}

export async function createSupplierInvoice(data: {
  supplierId: string;
  invoiceNumber: string;
  invoiceDate: string;
  dueDate: string;
  purchaseOrderId?: string;
  receivingReportId?: string;
  subtotalCents: number;
  taxCents?: number;
  totalCents: number;
  notes?: string;
  items: Array<{
    productId: string;
    invoicedQuantityX1000: number;
    unitCostCents: number;
    poItemId?: string;
    rrItemId?: string;
  }>;
}): Promise<SupplierInvoice> {
  const payload = {
    supplierId: data.supplierId,
    invoiceNumber: data.invoiceNumber,
    supplierInvoiceNo: data.invoiceNumber,
    invoiceDate: data.invoiceDate,
    dueDate: data.dueDate,
    purchaseOrderId: data.purchaseOrderId || null,
    receivingReportId: data.receivingReportId || null,
    notes: data.notes,
    postToApIfMatched: true,
    lines: data.items.map((it) => ({
      purchaseOrderLineId: it.poItemId || null,
      receivingReportLineId: it.rrItemId || null,
      productId: it.productId,
      itemDescription: "Invoice Line",
      invoicedQtyX1000: it.invoicedQuantityX1000,
      invoicedUnitCostCents: it.unitCostCents,
      taxCents: 0,
    })),
  };
  const response = await fetch("/api/lpi/invoices", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });
  return normalizeSupplierInvoice(await readJson<JsonRecord>(response));
}

export async function approveInvoiceVariance(id: string, notes: string): Promise<SupplierInvoice> {
  const response = await fetch(`/api/lpi/invoices/${id}/approve-variance`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      approvalReason: notes,
      postToApImmediately: true,
    }),
  });
  return normalizeSupplierInvoice(await readJson<JsonRecord>(response));
}

export async function fetchSupplierPayments(supplierId?: string, invoiceId?: string): Promise<SupplierPayment[]> {
  const params = new URLSearchParams();
  if (supplierId) params.set("supplierId", supplierId);
  if (invoiceId) {
    params.set("supplierInvoiceId", invoiceId);
    params.set("invoiceId", invoiceId);
  }
  const response = await fetch(`/api/lpi/payments?${params.toString()}`);
  return (await readJson<JsonRecord[]>(response)).map(normalizeSupplierPayment);
}

export async function recordSupplierPayment(data: {
  supplierId: string;
  invoiceId: string;
  amountCents: number;
  paymentMethod: string;
  disbursementSource?: string;
  referenceNumber?: string;
  bankName?: string;
  bankAccount?: string; // Legacy caller compatibility only; value denotes bank name, not an account identifier
  checkNumber?: string;
  notes?: string;
}): Promise<SupplierPayment> {
  const disbursementSource = data.disbursementSource || (data.paymentMethod === "Cash" ? "TreasuryFund" : "OperatingAccount");
  const payload = {
    supplierInvoiceId: data.invoiceId,
    invoiceId: data.invoiceId,
    amountCents: data.amountCents,
    paymentMethod: data.paymentMethod,
    disbursementSource: disbursementSource,
    referenceNo: data.referenceNumber,
    referenceNumber: data.referenceNumber,
    bankName: data.bankName ?? data.bankAccount,
    checkNo: data.checkNumber,
    remarks: data.notes,
  };
  const response = await fetch("/api/lpi/payments", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });
  return normalizeSupplierPayment(await readJson<JsonRecord>(response));
}

export async function fetchSupplierPayableLedger(supplierId?: string): Promise<SupplierPayableEntry[]> {
  const params = new URLSearchParams();
  if (supplierId) params.set("supplierId", supplierId);
  const query = params.toString();
  const response = await fetch(`/api/lpi/payables/ledger${query ? `?${query}` : ""}`);
  return (await readJson<JsonRecord[]>(response)).map(normalizeSupplierPayableEntry);
}

export async function fetchApAgingReport(asOfDate?: string): Promise<ApAgingReport> {
  const params = new URLSearchParams();
  if (asOfDate) params.set("asOfDate", asOfDate);
  const query = params.toString();
  const response = await fetch(`/api/lpi/ap-aging${query ? `?${query}` : ""}`);
  const raw = await readJson<JsonRecord>(response);
  const rawOpenInvoices: JsonRecord[] = raw.openInvoices ?? [];
  return {
    totalOpenPayablesCents: raw.totalOpenPayablesCents ?? 0,
    currentBucketCents: raw.currentBucketCents ?? 0,
    days1To30Cents: raw.days1To30Cents ?? 0,
    days31To60Cents: raw.days31To60Cents ?? 0,
    days61To90Cents: raw.days61To90Cents ?? 0,
    daysOver90Cents: raw.daysOver90Cents ?? 0,
    openInvoiceCount: raw.openInvoiceCount ?? rawOpenInvoices.length,
    overdueInvoiceCount: raw.overdueInvoiceCount ?? 0,
    varianceFlaggedInvoiceCount: raw.varianceFlaggedInvoiceCount ?? 0,
    openInvoices: rawOpenInvoices.map(normalizeSupplierInvoice),
    supplierSummaries: aggregateApAgingBySupplier(rawOpenInvoices),
  };
}

export async function fetchApAging(asOfDate?: string): Promise<ApAgingSummary[]> {
  const report = await fetchApAgingReport(asOfDate);
  return report.supplierSummaries;
}

export async function approveSupplierPayment(paymentId: string, settlementConfirmation: string, actualDisbursementDate: string, remarks?: string): Promise<SupplierPayment> {
  const response = await fetch(`/api/lpi/payments/${paymentId}/approve`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ settlementConfirmation, actualDisbursementDate, remarks }),
  });
  return normalizeSupplierPayment(await readJson<JsonRecord>(response));
}

export async function approveOverdelivery(receivingReportId: string, approvalReason: string): Promise<ReceivingReport> {
  const response = await fetch(`/api/lpi/receiving-reports/${receivingReportId}/approve-overdelivery`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ approvalReason }),
  });
  return readJson<ReceivingReport>(response);
}
