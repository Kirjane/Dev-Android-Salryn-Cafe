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
  paymentTermsDays: number;
  deliveryAddress: string | null;
  status: string;
  notes: string | null;
  subtotalCents: number;
  taxCents: number;
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
  invoicedQuantityX1000: number;
  unitCostCents: number;
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
  taxCents: number;
  totalCents: number;
  amountPaidCents: number;
  balanceDueCents: number;
  matchStatus: string;
  status: string;
  notes: string | null;
  varianceApprovedByUserId: string | null;
  varianceApprovedAt: string | null;
  varianceNotes: string | null;
  createdAt: string;
  items: SupplierInvoiceItem[];
}

export interface SupplierPayment {
  id: string;
  paymentNumber: string;
  supplierId: string;
  supplierName: string;
  invoiceId: string;
  invoiceNumber: string;
  paymentDate: string;
  amountCents: number;
  paymentMethod: string;
  status: string;
  referenceNumber: string | null;
  bankAccount: string | null;
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

export async function fetchLpiOverview(): Promise<LpiDashboardOverview> {
  const response = await fetch("/api/lpi/overview");
  const raw = await readJson<JsonRecord>(response);
  const aging = raw.apAging ?? {};
  return {
    totalActiveSuppliers: raw.activeSupplierCount ?? 0,
    pendingPurchaseRequisitions: raw.pendingRequisitionCount ?? 0,
    openPurchaseOrders: raw.openPurchaseOrderCount ?? 0,
    unmatchedInvoices: raw.varianceFlaggedInvoiceCount ?? 0,
    totalAccountsPayableCents: raw.totalOpenPayablesCents ?? 0,
    overduePayablesCents: raw.overduePayablesCents ?? 0,
    totalInventoryValuationCents: raw.totalInventoryValuationCents ?? 0,
    costCoverageIncomplete: raw.costCoverageIncomplete ?? false,
    missingCostProductCount: raw.missingCostProductCount ?? 0,
    quarantineStockQuantityX1000: raw.quarantineStockQuantityX1000 ?? 0,
    recentOrders: raw.recentOrders ?? [],
    agingSummary: (aging.openInvoiceCount ?? 0) === 0 ? [] : [{
      supplierId: "all-open-payables",
      supplierName: "All suppliers",
      currentCents: aging.currentBucketCents ?? 0,
      days1To30Cents: aging.days1To30Cents ?? 0,
      days31To60Cents: aging.days31To60Cents ?? 0,
      days61To90Cents: aging.days61To90Cents ?? 0,
      over90DaysCents: aging.daysOver90Cents ?? 0,
      totalOutstandingCents: aging.totalOpenPayablesCents ?? raw.totalOpenPayablesCents ?? 0,
    }],
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
  return readJson<SupplierQuotation[]>(response);
}

export async function createQuotation(data: {
  requisitionId: string;
  supplierId: string;
  quoteReference?: string;
  validUntil?: string;
  leadTimeDays?: number;
  paymentTermsDays?: number;
  notes?: string;
  items: Array<{
    prItemId: string;
    productId: string;
    quantityX1000: number;
    quotedUnitCostCents: number;
    leadTimeDays?: number;
    notes?: string;
  }>;
}): Promise<SupplierQuotation> {
  const response = await fetch("/api/lpi/quotations", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(data),
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
  return readJson<PurchaseOrder[]>(response);
}

export async function fetchPurchaseOrderById(id: string): Promise<PurchaseOrder> {
  const response = await fetch(`/api/lpi/purchase-orders/${id}`);
  return readJson<PurchaseOrder>(response);
}

export async function createPurchaseOrder(data: {
  supplierId: string;
  requisitionId?: string;
  expectedDeliveryDate?: string;
  deliveryAddress?: string;
  paymentTermsDays?: number;
  notes?: string;
  items: Array<{
    prItemId?: string;
    productId: string;
    uomId: string;
    orderedQuantityX1000: number;
    unitCostCents: number;
    notes?: string;
  }>;
}): Promise<PurchaseOrder> {
  const response = await fetch("/api/lpi/purchase-orders", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(data),
  });
  return readJson<PurchaseOrder>(response);
}

export async function approvePurchaseOrder(id: string, notes?: string): Promise<PurchaseOrder> {
  const response = await fetch(`/api/lpi/purchase-orders/${id}/approve`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ notes }),
  });
  return readJson<PurchaseOrder>(response);
}

export async function markPurchaseOrderSent(id: string): Promise<PurchaseOrder> {
  const response = await fetch(`/api/lpi/purchase-orders/${id}/mark-sent`, { method: "POST" });
  return readJson<PurchaseOrder>(response);
}

export async function cancelPurchaseOrder(id: string, reason: string): Promise<PurchaseOrder> {
  const response = await fetch(`/api/lpi/purchase-orders/${id}/cancel`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ reason }),
  });
  return readJson<PurchaseOrder>(response);
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
  deliveryReceiptNumber?: string;
  carrier?: string;
  driverName?: string;
  isOverdeliveryApproved?: boolean;
  notes?: string;
  items: Array<{
    poItemId: string;
    productId: string;
    uomId: string;
    deliveredQuantityX1000: number;
    acceptedQuantityX1000: number;
    rejectedQuantityX1000: number;
    targetLocationId: string;
    quarantineLocationId?: string;
    rejectionReason?: string;
    dispositionNotes?: string;
  }>;
}): Promise<ReceivingReport> {
  const response = await fetch("/api/lpi/receiving-reports", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(data),
  });
  return readJson<ReceivingReport>(response);
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
  return readJson<SupplierInvoice[]>(response);
}

export async function fetchSupplierInvoiceById(id: string): Promise<SupplierInvoice> {
  const response = await fetch(`/api/lpi/invoices/${id}`);
  return readJson<SupplierInvoice>(response);
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
  const response = await fetch("/api/lpi/invoices", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(data),
  });
  return readJson<SupplierInvoice>(response);
}

export async function approveInvoiceVariance(id: string, notes?: string): Promise<SupplierInvoice> {
  const response = await fetch(`/api/lpi/invoices/${id}/approve-variance`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ notes }),
  });
  return readJson<SupplierInvoice>(response);
}

export async function fetchSupplierPayments(supplierId?: string, invoiceId?: string): Promise<SupplierPayment[]> {
  const params = new URLSearchParams();
  if (supplierId) params.set("supplierId", supplierId);
  if (invoiceId) params.set("invoiceId", invoiceId);
  const response = await fetch(`/api/lpi/payments?${params.toString()}`);
  return readJson<SupplierPayment[]>(response);
}

export async function recordSupplierPayment(data: {
  supplierId: string;
  invoiceId: string;
  amountCents: number;
  paymentMethod: string;
  referenceNumber?: string;
  bankAccount?: string;
  checkNumber?: string;
  notes?: string;
}): Promise<SupplierPayment> {
  const response = await fetch("/api/lpi/payments", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(data),
  });
  return readJson<SupplierPayment>(response);
}

export async function fetchApAging(): Promise<ApAgingSummary[]> {
  const response = await fetch("/api/lpi/ap-aging");
  return readJson<ApAgingSummary[]>(response);
}

export async function approveSupplierPayment(paymentId: string, remarks?: string): Promise<SupplierPayment> {
  const response = await fetch(`/api/lpi/payments/${paymentId}/approve`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ remarks }),
  });
  return readJson<SupplierPayment>(response);
}

export async function approveOverdelivery(receivingReportId: string, approvalReason: string): Promise<ReceivingReport> {
  const response = await fetch(`/api/lpi/receiving-reports/${receivingReportId}/approve-overdelivery`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ approvalReason }),
  });
  return readJson<ReceivingReport>(response);
}
