export type DeploymentSurface = "CENTRAL_OWNER" | "WINDOWS_HOST";

export type OwnerReportsSnapshot = {
  generatedAt: string;
  officialBoundary: string;
  daily: { salesCount: number; salesAmountCents: number; averageSaleCents: number };
  weekly: { salesCount: number; salesAmountCents: number; averageSaleCents: number };
  monthly: { salesCount: number; salesAmountCents: number; averageSaleCents: number };
  receivables: { outstandingCents: number; collectionsCents: number; overdueCents: number } | null;
  lowStock: Array<{ productId: string; productName: string; unitName: string; stockOnHand: number; reorderLevel: number }>;
  cafeManagement?: {
    costCoverage: { state: string; revenueSaleCount: number; costCompleteSaleCount: number; costIncompleteSaleCount: number };
    profitability: {
      recognizedNetRevenueCents: number;
      historicalCogsCents: number;
      grossProfitCents: number;
      operationalExpenseCents: number;
      recognizedWasteCostCents: number;
      operationalContributionCents: number;
    };
    cash?: { saleCashReceiptCents: number; receivableCashCollectionCents: number; cashRefundCents: number; netCashDeltaCents: number } | null;
    accountsReceivable?: { outstandingBalanceCents: number; cashCollectionCentsInRange: number } | null;
    recipeCosts: Array<{
      recipeId: string;
      recipeName: string;
      yieldX1000: number;
      knownCostCents: number;
      costCoverage: string;
    }>;
  } | null;
};

export type MenuCatalogDraft = {
  categories: Array<{ id: string; name: string; isActive: boolean }>;
  items: Array<{ productId: string; categoryId: string | null; name: string; unitId: string; sellingPriceCents: number; isActive: boolean }>;
  variants: Array<{ id: string; productId: string; name: string; sellingPriceCents: number; isActive: boolean }>;
  modifierOptions?: Array<{ id: string; groupId: string; name: string; priceDeltaCents?: number; isActive?: boolean }>;
  currentPublication?: { revisionNumber: number } | null;
};

export type MenuUnitOption = { id: string; name: string; isActive: boolean };

export type IngredientRecipeDraft = {
  ingredients: Array<{
    id: string;
    name: string;
    stockUnit: string;
    stockOnHandX1000: number;
    reorderLevelX1000: number;
    isActive: boolean;
    notes: string | null;
  }>;
  recipes: Array<{
    id: string;
    name: string;
    yieldX1000: number;
    isActive: boolean;
  }>;
  lines: Array<{
    recipeId: string;
    ingredientId: string;
    inputUnit: string;
    declaredQuantityX1000: number;
    normalizedQuantityX1000: number;
    displayOrder: number;
  }>;
  itemBindings: Array<{ bindingType: string; bindingId: string; recipeId: string }>;
};

export type LowStockItem = {
  stockTargetType: string;
  stockTargetId: string;
  name: string;
  stockUnit: string;
  stockOnHandX1000: number;
  reorderLevelX1000: number;
};

export type SupplierSummary = {
  id: string;
  name: string;
  contactNumber?: string | null;
  isActive: boolean;
};

export type ArCustomerRow = {
  customer: {
    id: string;
    fullName: string;
    phone: string | null;
    creditLimitCents: number | null;
    isActive: boolean;
  };
  balanceCents: number;
  oldestDueDate?: string | null;
  lastTransactionAt?: string | null;
};

export type CashMovementRow = {
  movementId: string;
  movementType: string | number;
  deltaCents: number;
  reason: string | null;
  referenceType: string;
  createdAtUtc: string;
};

export type CafeRefundSummaryRow = {
  refundId: string;
  saleId: string;
  receiptNumber: string | null;
  version: number;
  state: "Requested" | "Approved" | "Rejected" | "Committed" | string;
  reason: string;
  requestedAtUtc: string;
  resolutionReason: string | null;
  totalRefundCents: number | null;
};

export type RefundableSaleRow = {
  saleId: string;
  receiptNumber: string;
  createdAtUtc: string;
  totalCents: number;
  cashNetCents: number;
  arCents: number;
  items: Array<{
    saleItemId: string;
    productId: string;
    productName: string;
    quantityX1000: number;
    remainingQuantityX1000: number;
    unitPriceCents: number;
    lineTotalCents: number;
    trackStock: boolean;
  }>;
};

export type CafeBookingRow = {
  bookingId: string;
  bookingVersion: number;
  status: string;
  fulfillmentState: string;
  bookingMode: string;
  customerNameSnapshot: string;
  eventStartUtc: string;
  location: string | null;
  packageLabel: string | null;
  quotedTotalCents: number;
  depositReceivedTotalCents: number;
};

export type CafeDeliveryRow = {
  deliveryId: string;
  orderId: string;
  version: number;
  status: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  terminalReason: string | null;
};

export type AttendanceRow = {
  attendanceEventId: string;
  subjectUserId: string;
  eventType: string;
  occurredAtUtc: string;
};

export type IncidentRow = {
  incidentId: string;
  version: number;
  status: string;
  severity: string;
  classification: string;
  title: string;
  occurredAtUtc: string;
  createdAtUtc: string;
};

export type AuditLogRow = {
  id: string;
  action: string;
  entityType: string;
  entityId: string | null;
  userDisplayName: string | null;
  reason: string | null;
  createdAt: string;
};

export type UserRow = {
  id: string;
  username: string;
  displayName: string;
  roleName: string;
  status: string;
};

export type UserRoleOption = {
  roleId: string;
  roleName: string;
  isActive: boolean;
};

export type StoreSettingsSnapshot = {
  id: string;
  storeName: string;
  businessName: string | null;
  address: string | null;
  contactNumber: string | null;
  tin: string | null;
  currencyLabel: string;
  receiptWidthMm: number;
  receiptHeader: string | null;
  receiptFooter: string | null;
  printerMode: string;
  windowsPrinterName: string | null;
};

export type BackupPackageRow = {
  backupId: string;
  createdAt: string;
  sizeBytes: number;
};

export type LicenseStatusSnapshot = {
  status: number | string;
  edition?: number | string | null;
  machineMatched: boolean;
  message: string;
  enabledFeatures: string[];
  expiresAt?: string | null;
};

export type LanControlStatusSnapshot = {
  remoteTransportEnabled: boolean;
  remotePosRuntimeEnabled: boolean;
  bindMode: string;
  configuredBindAddress: string;
  hostLocalUrl: string;
  hostLanUrl: string;
};

export type LanPairingSnapshot = {
  joinCodeIssued: boolean;
  joinCodeExpiresAtUtc: string | null;
  pendingRequestCount: number;
  pendingRequests: Array<{
    requestId: string;
    deviceName: string;
    requestedAtUtc: string;
  }>;
  message: string;
  activeJoinCode?: string | null;
};

export type CentralHealthSnapshot = {
  status: string;
  productId: string;
  centralInstanceId: string;
  serverEpoch: number;
  headSequence: number;
  schemaVersion: number;
  timestampUtc: string;
};

export type CentralAdminSession = {
  accessToken: string;
  session: {
    adminId: string;
    tenantId: string;
    storeId: string;
    productId: string;
    username: string;
    displayName: string;
    role: string;
    expiresAtUtc: string;
  };
};

export type CentralInviteTicket = {
  inviteId: string;
  joinCode: string;
  deviceRole: string;
  expiresAtUtc: string;
};

export type CentralDeviceRow = {
  deviceId: string;
  tenantId: string;
  storeId: string;
  productId: string;
  deviceName: string;
  deviceRole: string;
  publicKeyThumbprint: string;
  status: "Pending" | "Active" | "Revoked" | string;
  enrolledAtUtc: string;
  approvedAtUtc: string | null;
  revokedAtUtc: string | null;
  revokeReason: string | null;
  lastSeenAtUtc: string | null;
};

export type CentralQuarantineRow = {
  quarantineId: string;
  deviceId: string;
  operationId: string;
  operationType: string;
  receivedAtUtc: string;
  reviewStatus: string;
  reviewNote: string | null;
};

export type WasteReason = "Waste" | "Spoilage" | "Damage" | "Expired";
export type IncidentClassification = "Operational" | "Safety" | "Conduct" | "Equipment" | "Other";
export type RefundStockDisposition = "Restock" | "DamagedNonRestockable" | "ServiceNoStock";

export interface BackofficeApi {
  getOwnerReports(): Promise<OwnerReportsSnapshot>;
  getIngredientsAndRecipes(): Promise<IngredientRecipeDraft>;
  getMenuCatalog(): Promise<MenuCatalogDraft>;
  getMenuUnits(): Promise<MenuUnitOption[]>;
  saveMenuCategory(req: { operationId: string; name: string }): Promise<void>;
  saveMenuItem(req: { operationId: string; name: string; unitId: string; categoryId: string | null; sellingPriceCents: number }): Promise<void>;
  saveMenuVariant(req: { operationId: string; productId: string; name: string; sellingPriceCents: number }): Promise<void>;
  saveIngredient(req: { operationId: string; name: string; stockUnit: "Gram" | "Milliliter" | "Piece"; reorderLevelX1000: number }): Promise<void>;
  saveRecipe(req: { operationId: string; name: string; yieldX1000: number }): Promise<void>;
  saveRecipeLine(req: { operationId: string; recipeId: string; ingredientId: string; inputUnit: string; declaredQuantityX1000: number }): Promise<void>;
  saveSupplier(req: { operationId: string; name: string; contactNumber?: string }): Promise<void>;
  bindMenuItemRecipe(req: { operationId: string; productId: string; recipeId: string }): Promise<void>;
  createArCustomer(req: { fullName: string; phone?: string | null; creditLimitCents?: number | null }): Promise<void>;
  getPrintStations(): Promise<Array<{ stationId: string; name: string; stationKind: number | string; routeKey: string | null; isActive: boolean }>>;
  createPrintStation(req: { operationId: string; name: string; stationKind: "CustomerReceipt" | "Kitchen" | "BarDrinks"; routeKey: string }): Promise<void>;
  publishMenuAndRecipes(operationId: string): Promise<void>;
  getLowStock(): Promise<LowStockItem[]>;
  getSuppliers(): Promise<SupplierSummary[]>;
  receivePurchase(req: {
    operationId: string;
    supplierId: string;
    stockTargetId: string;
    quantityX1000: number;
    lineAcquisitionCostCents: number;
    notes?: string;
  }): Promise<void>;
  recordWaste(req: {
    operationId: string;
    stockTargetId: string;
    quantityWastedX1000: number;
    reason: WasteReason;
    notes?: string;
  }): Promise<void>;
  getReceivableCustomers(): Promise<ArCustomerRow[]>;
  collectArCash(req: {
    clientRequestId: string;
    customerId: string;
    amountCents: number;
    note?: string;
  }): Promise<void>;
  getCashMovements(): Promise<CashMovementRow[]>;
  postCashMovement(req: {
    operationId: string;
    movementType: "CashIn" | "CashOut" | "OperationalExpense";
    amountCents: number;
    reason: string;
  }): Promise<void>;
  getRefunds(): Promise<CafeRefundSummaryRow[]>;
  getRefundableSales(search?: string): Promise<RefundableSaleRow[]>;
  requestRefund(req: {
    operationId: string;
    saleId: string;
    reason: string;
    lines: Array<{ saleItemId: string; quantityX1000: number; stockDisposition: RefundStockDisposition }>;
  }): Promise<{ refundId: string; version: number; state: string }>;
  resolveRefund(req: {
    operationId: string;
    refundId: string;
    saleId: string;
    expectedVersion: number;
    approve: boolean;
    reason?: string;
  }): Promise<{ refundId: string; version: number; state: string }>;
  commitRefund(req: {
    operationId: string;
    refundId: string;
    saleId: string;
    expectedVersion: number;
    settlements: Array<{ method: "Cash" | "AccountsReceivable"; amountCents: number }>;
  }): Promise<{ refundId: string; version: number; state: string; totalRefundCents: number }>;
  getBookings(): Promise<CafeBookingRow[]>;
  advanceBookingStatus(req: {
    operationId: string;
    bookingId: string;
    expectedVersion: number;
    action: "confirm" | "ready-for-fulfillment" | "cancel";
    reason?: string;
  }): Promise<void>;
  getDeliveries(): Promise<CafeDeliveryRow[]>;
  advanceDeliveryStatus(req: {
    operationId: string;
    deliveryId: string;
    expectedVersion: number;
    action: "dispatch" | "deliver" | "fail";
  }): Promise<void>;
  getAttendance(): Promise<AttendanceRow[]>;
  recordAttendance(req: { operationId: string; eventType: "ClockIn" | "ClockOut" }): Promise<void>;
  getIncidents(): Promise<IncidentRow[]>;
  createIncident(req: {
    operationId: string;
    classification: IncidentClassification;
    severity: string;
    title: string;
    description: string;
    incidentOccurredAtUtc?: string;
  }): Promise<void>;
  getAuditLogs(): Promise<AuditLogRow[]>;
  getUsers(): Promise<UserRow[]>;
  getUserRoles(): Promise<UserRoleOption[]>;
  createUser(req: { username: string; displayName: string; password: string; roleId: string }): Promise<UserRow>;
  getStoreSettings(): Promise<StoreSettingsSnapshot>;
  updateStoreSettings(req: Partial<StoreSettingsSnapshot> & { storeName: string; businessName?: string | null; currencyLabel: string; receiptWidthMm: number; printerMode: string }): Promise<void>;
  getBackups(): Promise<BackupPackageRow[]>;
  createBackup(): Promise<BackupPackageRow>;
  verifyBackup(backupId: string): Promise<{ valid: boolean; summary?: string }>;
  restoreBackup(backupId: string, confirmation: string): Promise<void>;
  getLicenseStatus(): Promise<LicenseStatusSnapshot>;
  getLanStatus(): Promise<LanControlStatusSnapshot>;
  getLanPairing(): Promise<LanPairingSnapshot>;
  rotateLanJoinCode(): Promise<LanPairingSnapshot>;
}

export interface CentralOwnerApi {
  getHealth(baseUrl: string): Promise<CentralHealthSnapshot>;
  login(baseUrl: string, username: string, password: string): Promise<CentralAdminSession>;
  logout(baseUrl: string, accessToken: string): Promise<void>;
  listDevices(baseUrl: string, accessToken: string): Promise<CentralDeviceRow[]>;
  createInvite(baseUrl: string, accessToken: string, deviceRole: string): Promise<CentralInviteTicket>;
  approveDevice(baseUrl: string, accessToken: string, deviceId: string): Promise<CentralDeviceRow>;
  revokeDevice(baseUrl: string, accessToken: string, deviceId: string, reason?: string): Promise<CentralDeviceRow>;
  listQuarantine(baseUrl: string, accessToken: string): Promise<CentralQuarantineRow[]>;
  reviewQuarantine(
    baseUrl: string,
    accessToken: string,
    quarantineId: string,
    reviewStatus: "ApprovedForManualAudit" | "Discarded",
    reviewNote?: string,
  ): Promise<CentralQuarantineRow>;
}
