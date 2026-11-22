export type ServiceMode = "DineIn" | "Takeout";
export type TenderMode = "Cash" | "AccountsReceivable" | "Partial";
export type HostOrderState = "Open" | "Parked" | "Submitted" | "Preparing" | "Ready" | "Completed" | "Cancelled" | string;

export type SessionUser = {
  userId: string;
  username: string;
  displayName: string;
  roleName?: string;
  permissions: string[];
};

export type HostSession = {
  isAuthenticated: boolean;
  user?: SessionUser;
};

export type MenuCategory = {
  categoryId: string;
  name: string;
  displayOrder: number;
};

export type MenuItem = {
  productId: string;
  categoryId: string | null;
  name: string;
  description: string | null;
  unitId: string;
  unitName: string | null;
  sku: string | null;
  barcode: string | null;
  sellingPriceCents: number;
  costPriceCents: number | null;
  isTemporarilyUnavailable: boolean;
  displayOrder: number;
};

export type MenuVariant = {
  variantId: string;
  productId: string;
  name: string;
  sku: string | null;
  barcode: string | null;
  sellingPriceCents: number;
  costPriceCents: number | null;
  isTemporarilyUnavailable: boolean;
  displayOrder: number;
};

export type MenuModifierGroup = {
  groupId: string;
  name: string;
  isRequired: boolean;
  minSelections: number;
  maxSelections: number;
  displayOrder: number;
};

export type MenuModifierOption = {
  optionId: string;
  groupId: string;
  name: string;
  priceDeltaCents: number;
  displayOrder: number;
};

export type MenuModifierLink = {
  ownerId: string;
  groupId: string;
  displayOrder: number;
};

export type MenuPublication = {
  revisionId: string;
  storeId: string;
  revisionNumber: number;
  publishedAtUtc: string;
  categories: MenuCategory[];
  items: MenuItem[];
  variants: MenuVariant[];
  modifierGroups: MenuModifierGroup[];
  modifierOptions: MenuModifierOption[];
  itemModifierGroups: MenuModifierLink[];
  variantModifierGroups: MenuModifierLink[];
};

export type DraftLine = {
  orderLineId: string;
  productId: string;
  productName: string;
  variantId: string | null;
  variantName: string | null;
  modifierOptionIds: string[];
  modifierLabels: string[];
  quantity: number;
  unitPriceCents: number;
  note: string | null;
};

export type CafeOrderLineSnapshot = {
  orderLineId: string;
  productId: string;
  productNameSnapshot: string;
  unitNameSnapshot: string;
  quantityX1000: number;
  variantId: string | null;
  variantNameSnapshot: string | null;
  modifierSelections: Array<{ optionId: string; optionNameSnapshot: string; priceDeltaCents: number }>;
  note: string | null;
  calculatedUnitPriceCents: number;
  lineTotalCents: number;
  displayOrder: number;
};

export type CafeOrderSummary = {
  orderId: string;
  storeId: string;
  orderVersion: number;
  currentRevisionId: string;
  menuPublicationRevisionId: string;
  state: HostOrderState;
  serviceMode: ServiceMode;
  serviceReference: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
  completedSaleId: string | null;
  cancellationReason: string | null;
};

export type CafeOrderDetail = {
  order: CafeOrderSummary;
  currentRevision: {
    revisionId: string;
    revisionNumber: number;
    orderVersion: number;
    serviceMode: ServiceMode;
    serviceReference: string | null;
    lines: CafeOrderLineSnapshot[];
  };
};

export type CafeOrderMutationResult = {
  orderId: string;
  revisionId?: string;
  orderVersion: number;
  state: HostOrderState;
  idempotent?: boolean;
};

export type CafeOrderCompletionResult = {
  orderId: string;
  saleId: string;
  receiptNumber: string;
  idempotent?: boolean;
};

export type CustomerAccount = {
  id: string;
  fullName: string;
  isActive: boolean;
  outstandingBalanceCents: number;
};

export type HostShiftInfo = {
  shiftState: "Open" | "Closed" | "None" | string;
  shiftId?: string | null;
  openedAtUtc?: string | null;
  openingCashCents?: number | null;
  message?: string | null;
  expectedCashCents?: number | null;
  countedCashCents?: number | null;
  varianceCents?: number | null;
  coverageStatus?: string | null;
  reviewStatus?: string | null;
};

export type HostShiftOperationResult = {
  code: string;
  message: string;
  shift: HostShiftInfo | null;
};

export type CafePrintStation = {
  stationId: string;
  name: string;
  stationKind: number | string;
  routeKey: string | null;
  isActive: boolean;
};

export function isReceiptStation(station: CafePrintStation): boolean {
  return (
    station.isActive &&
    (station.stationKind === 1 || station.stationKind === "CustomerReceipt" || station.stationKind === "Receipt")
  );
}

export function isKitchenStation(station: CafePrintStation): boolean {
  return (
    station.isActive &&
    (station.stationKind === 2 ||
      station.stationKind === 3 ||
      station.stationKind === "Kitchen" ||
      station.stationKind === "BarDrinks")
  );
}

export type CafePrintAttempt = {
  attemptId?: string;
  status: number | string;
  resultCode?: string | null;
  resultMessage?: string | null;
  errorMessage?: string | null;
};

export type CafePrintJobResult = {
  job: {
    jobId: string;
    stationId: string;
    status?: string;
    copyKind?: number | string;
  };
  attempt?: CafePrintAttempt | null;
};

export function classifyPrintAttemptOutcome(
  result: CafePrintJobResult | null | undefined,
): { outcome: "succeeded" | "disabled" | "failed"; detail: string | null } {
  const attempt = result?.attempt;
  if (!attempt) {
    return { outcome: "succeeded", detail: null };
  }
  if (attempt.status === 1 || attempt.status === "Succeeded") {
    return { outcome: "succeeded", detail: attempt.resultMessage ?? null };
  }
  if (attempt.status === 3 || attempt.status === "DisabledUnavailable") {
    return {
      outcome: "disabled",
      detail: attempt.resultMessage ?? attempt.errorMessage ?? "Printer is disabled in store settings; print job recorded.",
    };
  }
  return {
    outcome: "failed",
    detail: attempt.errorMessage ?? attempt.resultMessage ?? "Printer reported a failure; print job recorded.",
  };
}

export type CentralEnrollRequest = {
  configuration: {
    httpsOrigin: string;
    tenantId: string;
    storeId: string;
    pinnedServerKey: string;
  };
  joinCode: string;
  deviceName: string;
};

export type CentralSyncResult = {
  applied: number;
  pending: number;
};

export type OrderRevisionPayload = {
  operationId: string;
  orderId: string;
  expectedVersion: number;
  serviceMode: ServiceMode;
  serviceReference: string | null;
  lines: Array<{
    orderLineId: string;
    productId: string;
    variantId: string | null;
    modifierOptionIds: string[];
    quantityX1000: number;
    note: string | null;
    displayOrder: number;
  }>;
};

export type CompleteOrderPayload = {
  completionOperationId: string;
  saleCommittedOperationId: string;
  financialDecisionOperationId: string;
  financialDecisionId: string;
  expectedVersion: number;
  customerId: string | null;
  orderDiscountCents?: number;
  discountReason?: string | null;
  payments: Array<{ method: "Cash" | "AccountsReceivable"; amountCents: number }>;
};

export type PendingHostIntent = {
  storeId: string;
  actor: string;
  path: string;
  body: Record<string, unknown>;
  rejection?: string;
};

export type RejectedIntentRecord = {
  request: PendingHostIntent;
  reason: string;
  archivedAtUtc: string;
};

export interface WorkstationApi {
  getSession(): Promise<HostSession>;
  login(username: string, password: string): Promise<HostSession>;
  logout(): Promise<void>;
  getPublishedMenu(): Promise<MenuPublication | null>;
  getOrders(): Promise<CafeOrderSummary[]>;
  getOrderDetail(orderId: string): Promise<CafeOrderDetail>;
  getCustomers(): Promise<CustomerAccount[]>;
  getShift(): Promise<HostShiftOperationResult>;
  openShift(openingCashCents: number): Promise<HostShiftOperationResult>;
  getPrintStations(): Promise<CafePrintStation[]>;
  printReceipt(req: {
    operationId: string;
    stationId: string;
    saleId: string;
    copyKind: "Original" | "Reprint";
  }): Promise<CafePrintJobResult>;
  printKitchenTicket(req: {
    operationId: string;
    stationId: string;
    orderId: string;
    orderRevisionId: string;
    copyKind: "Original" | "Reprint";
  }): Promise<CafePrintJobResult>;
  enrollCentral(req: CentralEnrollRequest): Promise<{ deviceId: string; status: string }>;
  synchronizeCentral(password: string): Promise<CentralSyncResult>;
  saveOrderRevision(payload: OrderRevisionPayload): Promise<CafeOrderMutationResult>;
  submitOrder(orderId: string, payload: { operationId: string; expectedVersion: number }): Promise<CafeOrderMutationResult>;
  setPreparationStatus(
    orderId: string,
    payload: { operationId: string; expectedVersion: number; targetState: "Preparing" | "Ready" },
  ): Promise<CafeOrderMutationResult>;
  parkOrder(orderId: string, payload: { operationId: string; expectedVersion: number }): Promise<CafeOrderMutationResult>;
  resumeOrder(orderId: string, payload: { operationId: string; expectedVersion: number }): Promise<CafeOrderMutationResult>;
  cancelOrder(
    orderId: string,
    payload: { operationId: string; expectedVersion: number; reason: string },
  ): Promise<CafeOrderMutationResult>;
  completeOrder(orderId: string, payload: CompleteOrderPayload): Promise<CafeOrderCompletionResult>;
  closeShift(payload: {
    countedCashCents: number;
    physicalCountConfirmed: true;
    clientRequestId: string;
    note: string;
  }): Promise<HostShiftOperationResult>;
  sendRawIntent(path: string, body: Record<string, unknown>): Promise<Record<string, unknown>>;
}
