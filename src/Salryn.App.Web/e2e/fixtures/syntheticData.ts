import { Permissions } from "../../src/security/permissions";
import type { BootstrapStatus } from "../../src/api/bootstrap";
import type { LocalLicenseStatus } from "../../src/api/license";
import type { SessionState } from "../../src/api/session";
import type { StoreBranding, StoreSettings } from "../../src/api/storeSettings";
import type { DashboardSummary } from "../../src/api/dashboard";
import type { HostShiftEnvelope, CheckoutEnvelope } from "../../src/api/checkout";
import type { CatalogOption, ProductDetail, ProductListItem } from "../../src/api/products";
import type { SaleDetail, SaleHistoryItem } from "../../src/api/sales";
import type {
  CollectionApiResponse,
  ReceivableCustomer,
  ReceivableEntry,
  ReceivablePage,
  ReceivablesSummary,
} from "../../src/api/receivables";
import type { OwnerReportsResponse } from "../../src/api/ownerReports";
import type {
  AnalyticsOverview,
  ArAging,
  PaymentMix,
  SalesTrend,
  TopProducts,
} from "../../src/api/ownerAnalytics";
import type { LowStockProduct, StockMovement } from "../../src/api/inventory";
import type { UserDetail, UserListItem, UserRole } from "../../src/api/users";
import type { AuditLog, ErrorLog } from "../../src/api/audit";
import type { BackupSummary, BackupVerification } from "../../src/api/backups";
import type { PagedResponse } from "../../src/api/types";
import type {
  LocalLanControlRuntimeStatus,
  LocalLanControlStatus,
  RemotePosPairingHostStatus,
} from "../../src/types/remoteReadOnlyClient";

export const SYNTHETIC_IDS = {
  adminUser: "10000000-0000-0000-0000-000000000001",
  managerUser: "10000000-0000-0000-0000-000000000002",
  cashierUser: "10000000-0000-0000-0000-000000000003",
  roleAdmin: "00000000-0000-0000-0000-000000000001",
  roleManager: "00000000-0000-0000-0000-000000000002",
  roleCashier: "00000000-0000-0000-0000-000000000003",
  shiftOpen: "20000000-0000-0000-0000-000000000001",
  catPantry: "30000000-0000-0000-0000-000000000001",
  catBeverages: "30000000-0000-0000-0000-000000000002",
  catHousehold: "30000000-0000-0000-0000-000000000003",
  brandGolden: "31000000-0000-0000-0000-000000000001",
  brandBarako: "31000000-0000-0000-0000-000000000002",
  unitPack: "32000000-0000-0000-0000-000000000001",
  unitBottle: "32000000-0000-0000-0000-000000000002",
  unitCan: "32000000-0000-0000-0000-000000000003",
  prodRice5kg: "40000000-0000-0000-0000-000000000001",
  prodCoffeeBox: "40000000-0000-0000-0000-000000000002",
  prodCookingOil: "40000000-0000-0000-0000-000000000003",
  prodSardines: "40000000-0000-0000-0000-000000000004",
  prodDetergent: "40000000-0000-0000-0000-000000000005",
  custMaria: "50000000-0000-0000-0000-000000000001",
  custRamon: "50000000-0000-0000-0000-000000000002",
  custElena: "50000000-0000-0000-0000-000000000003",
  saleCash1: "60000000-0000-0000-0000-000000000001",
  saleUtang2: "60000000-0000-0000-0000-000000000002",
  saleMixed3: "60000000-0000-0000-0000-000000000003",
  saleVoided4: "60000000-0000-0000-0000-000000000004",
  collection1: "71000000-0000-0000-0000-000000000001",
  terminalCounter2: "80000000-0000-0000-0000-000000000001",
  terminalAisle1: "80000000-0000-0000-0000-000000000002",
};

export const ALL_PERMISSIONS: string[] = Object.values(Permissions);

export const MANAGER_PERMISSIONS: string[] = [
  Permissions.ProductsView,
  Permissions.ProductsManage,
  Permissions.InventoryView,
  Permissions.InventoryAdjust,
  Permissions.SalesCreate,
  Permissions.SalesVoid,
  Permissions.SalesViewHistory,
  Permissions.ReportsView,
  Permissions.AuditView,
  Permissions.ShiftOpenClose,
  Permissions.ReceivablesView,
  Permissions.ReceivablesCreateCreditSale,
  Permissions.ReceivablesCollect,
  Permissions.ReceivablesAdjust,
];

export const CASHIER_PERMISSIONS: string[] = [
  Permissions.SalesCreate,
  Permissions.SalesViewHistory,
  Permissions.ProductsView,
  Permissions.ShiftOpenClose,
  Permissions.ReceivablesCreateCreditSale,
];

export function buildBootstrapStatus(firstRunRequired = false): BootstrapStatus {
  return {
    databaseInitialized: true,
    databaseFileExists: true,
    firstRunRequired,
    hasUsers: !firstRunRequired,
    hasStoreSettings: !firstRunRequired,
    pendingMigrationCount: 0,
    configuredMigrationCount: 14,
  };
}

export function buildLicenseStatus(options?: {
  ready?: boolean;
  u1Entitled?: boolean;
  status?: LocalLicenseStatus["status"];
  licenseType?: LocalLicenseStatus["licenseType"];
  machineMatched?: boolean;
}): LocalLicenseStatus {
  const ready = options?.ready ?? true;
  const u1Entitled = options?.u1Entitled ?? true;
  const status = options?.status ?? (ready ? "Licensed" : "Unlicensed");
  const licenseType = options?.licenseType ?? "Host";
  const machineMatched = options?.machineMatched ?? ready;

  return {
    status,
    edition: ready ? "Professional" : null,
    enabledFeatures: ready && u1Entitled ? ["ReceivablesUtangLedger"] : [],
    expiresAt: ready ? "2027-04-19T00:00:00Z" : null,
    daysRemaining: ready ? 365 : null,
    machineMatched,
    message: ready
      ? "Host license verified for this machine."
      : "No active Host license found on this PC. Activate a signed license file to continue.",
    validationTimestamp: "2026-04-19T05:00:00Z",
    licenseId: ready ? "LIC-2026-SALRYN-RETAIL-001" : null,
    customerName: ready ? "Balite Sari-Sari & Mini Mart (Synthetic QA)" : null,
    applicationId: "Salryn.POS",
    storeId: "STORE-QC-001",
    allowedLanTerminals: 3,
    supportExpiresAt: "2027-04-19T00:00:00Z",
    updatesUntil: "2027-04-19T00:00:00Z",
    machineFingerprint: "SALRYN-HOST-QA-0001-9F4A",
    licenseType,
    hostLicenseId: ready ? "LIC-2026-SALRYN-RETAIL-001" : null,
    terminalId: null,
    machineCode: "SALRYN-HOST-QA-0001-9F4A",
  };
}

export function buildSessionState(
  persona: "admin" | "manager" | "cashier" | "unauthenticated",
  customPermissions?: string[],
): SessionState {
  if (persona === "unauthenticated") {
    return {
      isAuthenticated: false,
      user: null,
      sessionStartedAt: null,
    };
  }
  if (persona === "cashier") {
    return {
      isAuthenticated: true,
      sessionStartedAt: "2026-04-19T00:10:00Z",
      user: {
        userId: SYNTHETIC_IDS.cashierUser,
        username: "cashier.rosa",
        displayName: "Rosa Santos (Cashier)",
        roleName: "Cashier",
        roleId: SYNTHETIC_IDS.roleCashier,
        status: "Active",
        permissions: customPermissions ?? CASHIER_PERMISSIONS,
      },
    };
  }
  if (persona === "manager") {
    return {
      isAuthenticated: true,
      sessionStartedAt: "2026-04-19T00:05:00Z",
      user: {
        userId: SYNTHETIC_IDS.managerUser,
        username: "manager.marco",
        displayName: "Marco Reyes (Store Manager)",
        roleName: "Manager",
        roleId: SYNTHETIC_IDS.roleManager,
        status: "Active",
        permissions: customPermissions ?? MANAGER_PERMISSIONS,
      },
    };
  }
  return {
    isAuthenticated: true,
    sessionStartedAt: "2026-04-19T00:01:00Z",
    user: {
      userId: SYNTHETIC_IDS.adminUser,
      username: "owner.admin",
      displayName: "Kirch Balite (Store Owner)",
      roleName: "Admin",
      roleId: SYNTHETIC_IDS.roleAdmin,
      status: "Active",
      permissions: customPermissions ?? ALL_PERMISSIONS,
    },
  };
}

export const SYNTHETIC_STORE_BRANDING: StoreBranding = {
  storeName: "Balite Neighborhood Mart",
  hasLogo: false,
  logoContentType: null,
  logoUrl: null,
};

export const SYNTHETIC_STORE_SETTINGS: StoreSettings = {
  id: "01000000-0000-0000-0000-000000000001",
  storeName: "Balite Neighborhood Mart",
  businessName: "Balite Retail Enterprises Inc.",
  address: "148 Magsaysay Ave., Brgy. San Isidro, Quezon City",
  contactNumber: "+63 917 555 0148",
  tin: "123-456-789-000",
  currencyLabel: "PHP",
  receiptWidthMm: 80,
  receiptHeader: "Salamat sa pamimili sa Balite Mart!",
  receiptFooter: "Keep receipt for return/exchange within 7 days.",
  printerMode: "WindowsPrinter",
  windowsPrinterName: "EPSON TM-T82III Receipt",
  hasLogo: false,
  logoContentType: null,
};

export const SYNTHETIC_DASHBOARD_HEALTHY: DashboardSummary = {
  todaySalesTotal: 18450.0,
  todayTransactionCount: 42,
  productCount: 5,
  lowStockCount: 3,
  refreshedAt: "2026-04-19T05:15:00Z",
  status: "ok",
  warnings: [],
};

export const SYNTHETIC_DASHBOARD_WARNINGS: DashboardSummary = {
  todaySalesTotal: 18450.0,
  todayTransactionCount: 42,
  productCount: 5,
  lowStockCount: 3,
  refreshedAt: "2026-04-19T05:15:00Z",
  status: "Attention needed",
  warnings: [
    "No open shift session. Cashiers must open a shift with an opening float before ringing sales.",
    "Last database backup is more than 7 days old. Run a verified backup in Backup & Restore.",
    "3 active products are at or below reorder threshold.",
  ],
};

export function buildHostShiftEnvelope(open = true): HostShiftEnvelope {
  if (!open) {
    return {
      code: "SHIFT_CLOSED",
      message: "No active shift session.",
      shift: {
        shiftState: "Closed",
        shiftId: null,
        openedAtUtc: null,
        openingCashCents: null,
        message: "Shift is closed. Open a shift with starting cash to ring sales.",
      },
    };
  }
  return {
    code: "SHIFT_OPEN",
    message: "Shift is open.",
    shift: {
      shiftState: "Open",
      shiftId: SYNTHETIC_IDS.shiftOpen,
      openedAtUtc: "2026-04-19T00:15:00Z",
      openingCashCents: 300000,
      message: "Active shift opened at 08:15 AM with ₱3,000.00 opening float.",
    },
  };
}

export const SYNTHETIC_CATEGORIES: CatalogOption[] = [
  { id: SYNTHETIC_IDS.catPantry, name: "Rice & Pantry Staples", isActive: true },
  { id: SYNTHETIC_IDS.catBeverages, name: "Coffee & Beverages", isActive: true },
  { id: SYNTHETIC_IDS.catHousehold, name: "Laundry & Household", isActive: true },
];

export const SYNTHETIC_BRANDS: CatalogOption[] = [
  { id: SYNTHETIC_IDS.brandGolden, name: "Golden Harvest", isActive: true },
  { id: SYNTHETIC_IDS.brandBarako, name: "Kapeng Barako Co.", isActive: true },
];

export const SYNTHETIC_UNITS: CatalogOption[] = [
  { id: SYNTHETIC_IDS.unitPack, name: "pack", isActive: true },
  { id: SYNTHETIC_IDS.unitBottle, name: "bottle", isActive: true },
  { id: SYNTHETIC_IDS.unitCan, name: "can", isActive: true },
];

export const SYNTHETIC_PRODUCT_ITEMS: ProductListItem[] = [
  {
    id: SYNTHETIC_IDS.prodRice5kg,
    sku: "RICE-SIN-5KG",
    barcode: "4800011001015",
    name: "Sinandomeng Premium Rice 5kg",
    unitName: "pack",
    categoryName: "Rice & Pantry Staples",
    brandName: "Golden Harvest",
    sellingPrice: 285.0,
    costPrice: 245.0,
    stockOnHand: 34,
    reorderLevel: 10,
    trackStock: true,
    isActive: true,
  },
  {
    id: SYNTHETIC_IDS.prodCoffeeBox,
    sku: "COF-3IN1-10S",
    barcode: "4800022002026",
    name: "Kapeng Barako 3-in-1 Twin Pack (10 sachets)",
    unitName: "pack",
    categoryName: "Coffee & Beverages",
    brandName: "Kapeng Barako Co.",
    sellingPrice: 135.0,
    costPrice: 108.0,
    stockOnHand: 6,
    reorderLevel: 12,
    trackStock: true,
    isActive: true,
  },
  {
    id: SYNTHETIC_IDS.prodCookingOil,
    sku: "OIL-COCO-1L",
    barcode: "4800033003030",
    name: "Golden Pure Coconut Cooking Oil 1L",
    unitName: "bottle",
    categoryName: "Rice & Pantry Staples",
    brandName: "Golden Harvest",
    sellingPrice: 98.0,
    costPrice: 79.0,
    stockOnHand: 22,
    reorderLevel: 8,
    trackStock: true,
    isActive: true,
  },
  {
    id: SYNTHETIC_IDS.prodSardines,
    sku: "CAN-SARD-155G",
    barcode: "4800044004044",
    name: "Spicy Tomato Sardines 155g Can",
    unitName: "can",
    categoryName: "Rice & Pantry Staples",
    brandName: "Golden Harvest",
    sellingPrice: 26.5,
    costPrice: 21.0,
    stockOnHand: 4,
    reorderLevel: 15,
    trackStock: true,
    isActive: true,
  },
  {
    id: SYNTHETIC_IDS.prodDetergent,
    sku: "HLD-SURF-800G",
    barcode: "4800055005058",
    name: "Fresh Blossom Laundry Powder 800g",
    unitName: "pack",
    categoryName: "Laundry & Household",
    brandName: "Golden Harvest",
    sellingPrice: 112.0,
    costPrice: 88.0,
    stockOnHand: 0,
    reorderLevel: 6,
    trackStock: true,
    isActive: true,
  },
];

export function buildProductDetail(productId: string): ProductDetail {
  const item = SYNTHETIC_PRODUCT_ITEMS.find((p) => p.id === productId) ?? SYNTHETIC_PRODUCT_ITEMS[0];
  return {
    id: item.id,
    sku: item.sku,
    barcode: item.barcode,
    name: item.name,
    description: `Standard retail pack for ${item.name}`,
    categoryId: SYNTHETIC_IDS.catPantry,
    categoryName: item.categoryName,
    brandId: SYNTHETIC_IDS.brandGolden,
    brandName: item.brandName,
    unitId: SYNTHETIC_IDS.unitPack,
    unitName: item.unitName,
    supplierId: null,
    supplierName: "Metro Wholesale Distributors",
    costPrice: item.costPrice,
    sellingPrice: item.sellingPrice,
    wholesalePrice: null,
    stockOnHand: item.stockOnHand,
    reorderLevel: item.reorderLevel,
    trackStock: item.trackStock,
    allowDiscount: true,
    isActive: item.isActive,
    notes: "Verify seal upon delivery.",
    createdAt: "2026-01-15T00:00:00Z",
    updatedAt: "2026-04-18T09:30:00Z",
  };
}

export const SYNTHETIC_SALES_HISTORY_ITEMS: SaleHistoryItem[] = [
  {
    id: SYNTHETIC_IDS.saleCash1,
    receiptNo: "OR-20260419-0024",
    saleDate: "2026-04-19T05:12:00Z",
    cashierDisplayName: "Rosa Santos (Cashier)",
    total: 668.0,
    amountPaid: 1000.0,
    change: 332.0,
    status: "Completed",
    voidReason: null,
  },
  {
    id: SYNTHETIC_IDS.saleUtang2,
    receiptNo: "OR-20260418-0018",
    saleDate: "2026-04-18T06:30:00Z",
    cashierDisplayName: "Rosa Santos (Cashier)",
    total: 400.0,
    amountPaid: 400.0,
    change: 0,
    status: "Completed",
    voidReason: null,
  },
  {
    id: SYNTHETIC_IDS.saleMixed3,
    receiptNo: "OR-20260418-0019",
    saleDate: "2026-04-18T07:45:00Z",
    cashierDisplayName: "Marco Reyes (Store Manager)",
    total: 585.0,
    amountPaid: 585.0,
    change: 0,
    status: "Completed",
    voidReason: null,
  },
  {
    id: SYNTHETIC_IDS.saleVoided4,
    receiptNo: "OR-20260418-0012",
    saleDate: "2026-04-18T02:10:00Z",
    cashierDisplayName: "Rosa Santos (Cashier)",
    total: 135.0,
    amountPaid: 150.0,
    change: 15.0,
    status: "Voided",
    voidReason: "Customer requested cancellation",
  },
];

export const SYNTHETIC_SALE_DETAIL: SaleDetail = {
  id: SYNTHETIC_IDS.saleCash1,
  receiptNo: "OR-20260419-0024",
  saleDate: "2026-04-19T05:12:00Z",
  cashierDisplayName: "Rosa Santos (Cashier)",
  subtotal: 668.0,
  discount: 0,
  tax: 0,
  total: 668.0,
  amountPaid: 1000.0,
  change: 332.0,
  status: "Completed",
  voidReason: null,
  voidedAt: null,
  receiptSnapshot: [
    "      BALITE NEIGHBORHOOD MART      ",
    "148 Magsaysay Ave., Quezon City     ",
    "------------------------------------",
    "Receipt #: OR-20260419-0024         ",
    "Cashier: Rosa Santos                ",
    "------------------------------------",
    "2x Sinandomeng Rice 5kg      570.00 ",
    "1x Coconut Cooking Oil 1L     98.00 ",
    "------------------------------------",
    "TOTAL:                    PHP 668.00",
    "CASH:                    PHP 1000.00",
    "CHANGE:                   PHP 332.00",
  ].join("\n"),
  items: [
    {
      id: "90000000-0000-0000-0000-000000000001",
      productNameSnapshot: "Sinandomeng Premium Rice 5kg",
      barcodeSnapshot: "4800011001015",
      unitNameSnapshot: "pack",
      quantity: 2,
      unitPrice: 285.0,
      discount: 0,
      lineTotal: 570.0,
    },
    {
      id: "90000000-0000-0000-0000-000000000002",
      productNameSnapshot: "Golden Pure Coconut Cooking Oil 1L",
      barcodeSnapshot: "4800033003030",
      unitNameSnapshot: "bottle",
      quantity: 1,
      unitPrice: 98.0,
      discount: 0,
      lineTotal: 98.0,
    },
  ],
  payments: [
    {
      id: "90100000-0000-0000-0000-000000000001",
      method: "Cash",
      amount: 668.0,
      paidAt: "2026-04-19T05:12:00Z",
    },
  ],
};

export const SYNTHETIC_RECEIVABLES_SUMMARY: ReceivablesSummary = {
  totalReceivablesCents: 465000,
  dueTodayCents: 150000,
  overdueCents: 150000,
  collectedTodayCents: 85000,
};

export const SYNTHETIC_RECEIVABLE_CUSTOMERS: ReceivableCustomer[] = [
  {
    customer: {
      id: SYNTHETIC_IDS.custMaria,
      fullName: "Maria Clara Dela Cruz",
      phone: "0917-801-2234",
      note: "Sari-sari reseller across Brgy. Hall; settles every Friday.",
      isActive: true,
    },
    balanceCents: 315000,
    oldestDueDate: "2026-04-24",
    lastTransactionAt: "2026-04-19T04:00:00Z",
  },
  {
    customer: {
      id: SYNTHETIC_IDS.custRamon,
      fullName: "Ramon Magsaysay Jr.",
      phone: "0918-442-9910",
      note: "Canteen operator; follow up on overdue balance.",
      isActive: true,
    },
    balanceCents: 150000,
    oldestDueDate: "2026-04-12",
    lastTransactionAt: "2026-04-17T06:20:00Z",
  },
  {
    customer: {
      id: SYNTHETIC_IDS.custElena,
      fullName: "Elena Gilbert-Santos",
      phone: "0998-112-3301",
      note: "Always pays on time.",
      isActive: true,
    },
    balanceCents: 0,
    oldestDueDate: null,
    lastTransactionAt: "2026-04-15T05:15:00Z",
  },
];

export const SYNTHETIC_RECEIVABLE_LEDGER: ReceivableEntry[] = [
  {
    id: "70000000-0000-0000-0000-000000000002",
    collectionId: SYNTHETIC_IDS.collection1,
    entryType: 2, // Collection
    debitCents: 0,
    creditCents: 85000,
    balanceAfterCents: 315000,
    dueDate: null,
    note: "Cash partial collection at Counter 1",
    createdAt: "2026-04-19T04:00:00Z",
    paymentStatus: null,
  },
  {
    id: "70000000-0000-0000-0000-000000000001",
    collectionId: null,
    entryType: 1, // CreditSale
    debitCents: 400000,
    creditCents: 0,
    balanceAfterCents: 400000,
    dueDate: "2026-04-24",
    note: "OR-20260418-0018 Weekly rice & oil stock utang",
    createdAt: "2026-04-18T06:30:00Z",
    paymentStatus: 2, // Partial
  },
];

export const SYNTHETIC_COLLECTION_RESPONSE: CollectionApiResponse = {
  collection: {
    id: SYNTHETIC_IDS.collection1,
    customerId: SYNTHETIC_IDS.custMaria,
    amountCents: 85000,
    note: "Paid in cash at Counter 1",
    collectedAt: "2026-04-19T05:30:00Z",
  },
  acknowledgment: {
    collectionId: SYNTHETIC_IDS.collection1,
    customerName: "Maria Clara Dela Cruz",
    amountPaidCents: 85000,
    previousBalanceCents: 315000,
    newBalanceCents: 230000,
    collectedBy: "Kirch Balite (Store Owner)",
    collectedAt: "2026-04-19T05:30:00Z",
    note: "Paid in cash at Counter 1",
    method: "Cash",
  },
  plainTextAcknowledgment: [
    "   BALITE NEIGHBORHOOD MART   ",
    "   UTANG PAYMENT RECEIPT      ",
    "------------------------------",
    "Customer: Maria Clara Dela Cruz",
    "Prev Balance:     PHP 3,150.00",
    "Amount Paid:        PHP 850.00",
    "New Balance:      PHP 2,300.00",
    "Method:                   Cash",
  ].join("\n"),
};

const ANALYTICS_SCOPE = {
  asOfUtc: "2026-04-19T05:30:00Z",
  timeZoneId: "Asia/Manila",
  rangeStartUtc: "2026-04-12T16:00:00Z",
  rangeEndUtc: "2026-04-19T15:59:59Z",
  priorRangeStartUtc: "2026-04-05T16:00:00Z",
  priorRangeEndUtc: "2026-04-12T15:59:59Z",
  rangeSemantics: "Store-local calendar day boundaries (Asia/Manila)",
  moneySemantics: "Integer centavos; completed sales only",
  productAttributionSemantics: "Historical line snapshot with current catalog category join",
};

export const SYNTHETIC_ANALYTICS_OVERVIEW: AnalyticsOverview = {
  scope: ANALYTICS_SCOPE,
  current: {
    completedSaleCount: 214,
    salesTotalCents: 12830000,
    cashOnSalesCents: 11480000,
    creditOriginatedCents: 1350000,
    voidedSaleCount: 3,
  },
  prior: {
    completedSaleCount: 196,
    salesTotalCents: 11540000,
    cashOnSalesCents: 10420000,
    creditOriginatedCents: 1120000,
    voidedSaleCount: 2,
  },
  margin: {
    isAvailable: true,
    estimatedCogsCents: 10120000,
    estimatedGrossMarginCents: 2710000,
    costedItemCount: 214,
    missingCostItemCount: 0,
    basis: "Historical unit cost snapshot at time of sale",
  },
  lowStockCount: 3,
};

export const SYNTHETIC_SALES_TREND: SalesTrend = {
  scope: ANALYTICS_SCOPE,
  bucket: "day",
  points: [
    {
      storeDate: "2026-04-15",
      salesTotalCents: 1640000,
      cashOnSalesCents: 1490000,
      creditOriginatedCents: 150000,
      completedSaleCount: 29,
      voidedSaleCount: 0,
    },
    {
      storeDate: "2026-04-16",
      salesTotalCents: 1915000,
      cashOnSalesCents: 1715000,
      creditOriginatedCents: 200000,
      completedSaleCount: 34,
      voidedSaleCount: 1,
    },
    {
      storeDate: "2026-04-17",
      salesTotalCents: 2210000,
      cashOnSalesCents: 1960000,
      creditOriginatedCents: 250000,
      completedSaleCount: 38,
      voidedSaleCount: 0,
    },
    {
      storeDate: "2026-04-18",
      salesTotalCents: 2670000,
      cashOnSalesCents: 2270000,
      creditOriginatedCents: 400000,
      completedSaleCount: 45,
      voidedSaleCount: 1,
    },
    {
      storeDate: "2026-04-19",
      salesTotalCents: 1845000,
      cashOnSalesCents: 1695000,
      creditOriginatedCents: 150000,
      completedSaleCount: 42,
      voidedSaleCount: 1,
    },
  ],
};

export const SYNTHETIC_PAYMENT_MIX: PaymentMix = {
  scope: ANALYTICS_SCOPE,
  rows: [
    { method: "Cash on sales", amountCents: 11480000, scope: "Sale Tender" },
    { method: "Utang (Credit originated)", amountCents: 1350000, scope: "Receivable Charge" },
    { method: "AR Collections (Cash)", amountCents: 890000, scope: "Utang Collection" },
  ],
};

export const SYNTHETIC_TOP_PRODUCTS: TopProducts = {
  scope: ANALYTICS_SCOPE,
  products: [
    {
      productId: SYNTHETIC_IDS.prodRice5kg,
      historicalLabel: "Sinandomeng Premium Rice 5kg",
      historicalBarcode: "4800011001015",
      quantityX1000: 148000,
      lineRevenueCents: 4218000,
      estimatedCogsCents: 3626000,
      costCoverageComplete: true,
    },
    {
      productId: SYNTHETIC_IDS.prodCoffeeBox,
      historicalLabel: "Kapeng Barako 3-in-1 Twin Pack (10 sachets)",
      historicalBarcode: "4800022002026",
      quantityX1000: 210000,
      lineRevenueCents: 2830000,
      estimatedCogsCents: 2268000,
      costCoverageComplete: true,
    },
    {
      productId: SYNTHETIC_IDS.prodCookingOil,
      historicalLabel: "Golden Pure Coconut Cooking Oil 1L",
      historicalBarcode: "4800033003030",
      quantityX1000: 120000,
      lineRevenueCents: 1176000,
      estimatedCogsCents: 948000,
      costCoverageComplete: true,
    },
  ],
  categories: [
    { currentCategory: "Rice & Pantry Staples", quantityX1000: 380000, lineRevenueCents: 8420000 },
    { currentCategory: "Coffee & Beverages", quantityX1000: 262000, lineRevenueCents: 4410000 },
  ],
  classificationDisclosure: "Categories reflect current product catalog mapping.",
};

export const SYNTHETIC_AR_AGING: ArAging = {
  scope: ANALYTICS_SCOPE,
  outstandingCents: 465000,
  collectionsInPeriodCents: 890000,
  collectionsByMethod: [{ method: "Cash", amountCents: 890000, scope: "Collection" }],
  buckets: [
    { key: "current", label: "Current (Not Due)", outstandingCents: 315000 },
    { key: "1_30", label: "1–30 Days Overdue", outstandingCents: 150000 },
    { key: "31_60", label: "31–60 Days Overdue", outstandingCents: 0 },
    { key: "61_90", label: "61–90 Days Overdue", outstandingCents: 0 },
    { key: "90_plus", label: "90+ Days Overdue", outstandingCents: 0 },
  ],
  allocationDisclosure: "FIFO allocation across oldest open credit sale charges.",
};

export const SYNTHETIC_OWNER_REPORTS_SUMMARY: OwnerReportsResponse = {
  generatedAt: "2026-04-19T05:30:00Z",
  weekRule: "Monday-Sunday store-local week",
  officialBoundary: "Operational management summary only",
  daily: {
    periodKey: "daily",
    label: "Today",
    rangeStart: "2026-04-19",
    rangeEnd: "2026-04-19",
    salesCount: 42,
    salesAmountCents: 1845000,
    averageSaleCents: 43928,
    voidedCount: 1,
    voidedAmountCents: 15500,
  },
  weekly: {
    periodKey: "weekly",
    label: "This Week",
    rangeStart: "2026-04-13",
    rangeEnd: "2026-04-19",
    salesCount: 214,
    salesAmountCents: 12830000,
    averageSaleCents: 59953,
    voidedCount: 3,
    voidedAmountCents: 42500,
  },
  monthly: {
    periodKey: "monthly",
    label: "This Month",
    rangeStart: "2026-04-01",
    rangeEnd: "2026-04-19",
    salesCount: 540,
    salesAmountCents: 31940000,
    averageSaleCents: 59148,
    voidedCount: 6,
    voidedAmountCents: 89000,
  },
  topProducts: [
    {
      productId: SYNTHETIC_IDS.prodRice5kg,
      productName: "Sinandomeng Premium Rice 5kg",
      barcode: "4800011001015",
      quantitySold: 148,
      salesAmountCents: 4218000,
    },
  ],
  cashierTotals: [
    {
      cashierUserId: SYNTHETIC_IDS.cashierUser,
      cashierDisplayName: "Rosa Santos (Cashier)",
      salesCount: 168,
      salesAmountCents: 9840000,
      voidedCount: 2,
      voidedAmountCents: 27000,
    },
    {
      cashierUserId: SYNTHETIC_IDS.managerUser,
      cashierDisplayName: "Marco Reyes (Store Manager)",
      salesCount: 46,
      salesAmountCents: 2990000,
      voidedCount: 1,
      voidedAmountCents: 15500,
    },
  ],
  categoryTotals: [
    { categoryName: "Rice & Pantry Staples", quantitySold: 380, salesAmountCents: 8420000 },
    { categoryName: "Coffee & Beverages", quantitySold: 262, salesAmountCents: 4410000 },
  ],
  lowStock: [
    {
      productId: SYNTHETIC_IDS.prodDetergent,
      productName: "Fresh Blossom Laundry Powder 800g",
      barcode: "4800055005058",
      unitName: "pack",
      stockOnHand: 0,
      reorderLevel: 6,
    },
    {
      productId: SYNTHETIC_IDS.prodSardines,
      productName: "Spicy Tomato Sardines 155g Can",
      barcode: "4800044004044",
      unitName: "can",
      stockOnHand: 4,
      reorderLevel: 15,
    },
    {
      productId: SYNTHETIC_IDS.prodCoffeeBox,
      productName: "Kapeng Barako 3-in-1 Twin Pack (10 sachets)",
      barcode: "4800022002026",
      unitName: "pack",
      stockOnHand: 6,
      reorderLevel: 12,
    },
  ],
  inventoryValue: {
    isAvailable: true,
    estimatedValueCents: 1076100,
    valuedProductCount: 5,
    missingCostProductCount: 0,
    message: "All active products have cost prices recorded.",
  },
  receivables: {
    creditSalesCents: 1350000,
    cashCollectedOnSalesCents: 11480000,
    collectionsCents: 890000,
    outstandingCents: 465000,
    overdueCents: 150000,
  },
};

export const SYNTHETIC_LOW_STOCK_PRODUCTS: LowStockProduct[] = SYNTHETIC_OWNER_REPORTS_SUMMARY.lowStock;

export const SYNTHETIC_STOCK_MOVEMENTS: StockMovement[] = [
  {
    id: "91000000-0000-0000-0000-000000000001",
    productId: SYNTHETIC_IDS.prodRice5kg,
    productName: "Sinandomeng Premium Rice 5kg",
    movementType: "Sale",
    quantityChange: -2,
    referenceType: "Sale",
    referenceId: SYNTHETIC_IDS.saleCash1,
    reason: "OR-20260419-0024",
    userDisplayName: "Rosa Santos (Cashier)",
    createdAt: "2026-04-19T05:12:00Z",
  },
  {
    id: "91000000-0000-0000-0000-000000000002",
    productId: SYNTHETIC_IDS.prodRice5kg,
    productName: "Sinandomeng Premium Rice 5kg",
    movementType: "StockIn",
    quantityChange: 20,
    referenceType: "ManualStockIn",
    referenceId: null,
    reason: "Weekly supplier delivery DR#8841",
    userDisplayName: "Marco Reyes (Store Manager)",
    createdAt: "2026-04-18T01:30:00Z",
  },
];

export const SYNTHETIC_USERS: UserListItem[] = [
  {
    id: SYNTHETIC_IDS.adminUser,
    username: "owner.admin",
    displayName: "Kirch Balite (Store Owner)",
    roleName: "Admin",
    status: "Active",
    createdAt: "2026-01-15T00:00:00Z",
  },
  {
    id: SYNTHETIC_IDS.managerUser,
    username: "manager.marco",
    displayName: "Marco Reyes (Store Manager)",
    roleName: "Manager",
    status: "Active",
    createdAt: "2026-01-20T00:00:00Z",
  },
  {
    id: SYNTHETIC_IDS.cashierUser,
    username: "cashier.rosa",
    displayName: "Rosa Santos (Cashier)",
    roleName: "Cashier",
    status: "Active",
    createdAt: "2026-02-01T00:00:00Z",
  },
];

export const SYNTHETIC_USER_ROLES: UserRole[] = [
  { roleId: SYNTHETIC_IDS.roleAdmin, roleName: "Admin", isActive: true },
  { roleId: SYNTHETIC_IDS.roleManager, roleName: "Manager", isActive: true },
  { roleId: SYNTHETIC_IDS.roleCashier, roleName: "Cashier", isActive: true },
];

export function buildUserDetail(userId: string): UserDetail {
  const u = SYNTHETIC_USERS.find((item) => item.id === userId) ?? SYNTHETIC_USERS[0];
  const permissions =
    u.roleName === "Admin"
      ? ALL_PERMISSIONS
      : u.roleName === "Manager"
        ? MANAGER_PERMISSIONS
        : CASHIER_PERMISSIONS;
  return {
    ...u,
    permissions,
  };
}

export const SYNTHETIC_AUDIT_LOGS: AuditLog[] = [
  {
    id: "92000000-0000-0000-0000-000000000001",
    userDisplayName: "Rosa Santos (Cashier)",
    action: "Sale.Completed",
    entityType: "Sale",
    entityId: SYNTHETIC_IDS.saleCash1,
    reason: "Completed cash sale OR-20260419-0024 for ₱668.00",
    createdAt: "2026-04-19T05:12:00Z",
  },
  {
    id: "92000000-0000-0000-0000-000000000002",
    userDisplayName: "Kirch Balite (Store Owner)",
    action: "Receivable.PaymentCollected",
    entityType: "CustomerCreditAccount",
    entityId: SYNTHETIC_IDS.custMaria,
    reason: "Collected ₱850.00 Utang payment from Maria Clara Dela Cruz",
    createdAt: "2026-04-19T04:00:00Z",
  },
];

export const SYNTHETIC_ERROR_LOGS: ErrorLog[] = [
  {
    id: "93000000-0000-0000-0000-000000000001",
    userDisplayName: "Rosa Santos (Cashier)",
    area: "ReceiptPrinterSpooler",
    message: "Thermal printer paper-near-end sensor triggered on EPSON TM-T82III.",
    createdAt: "2026-04-18T14:22:10Z",
  },
];

export const SYNTHETIC_BACKUPS: BackupSummary[] = [
  {
    backupId: "salryn-backup-20260419-020000",
    createdAt: "2026-04-19T02:00:00Z",
    sizeBytes: 1482910,
  },
  {
    backupId: "salryn-backup-20260418-020000",
    createdAt: "2026-04-18T02:00:00Z",
    sizeBytes: 1421004,
  },
];

export function buildBackupVerification(backupId: string): BackupVerification {
  return {
    backupId,
    storeName: "Balite Neighborhood Mart",
    hasLogo: false,
    manifest: {
      backupId,
      createdAt: "2026-04-19T02:00:00Z",
      schemaVersion: 14,
      files: [
        {
          relativePath: "salryn.db",
          sizeBytes: 1480000,
          sha256: "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
        },
      ],
    },
    database: {
      sizeBytes: 1480000,
      lastModifiedAt: "2026-04-19T02:00:00Z",
      coreTablesExist: true,
      appMetadataExists: true,
      migrationMetadataExists: true,
      userCount: 3,
      productCount: 5,
      saleCount: 214,
      backupMigrationVersion: 14,
      supportedMigrationVersion: 14,
      criticalTablesExist: true,
      migrationVersionCompatible: true,
      missingCriticalTables: [],
      hasFutureMigrationVersion: false,
      isRestorable: true,
    },
  };
}

export const SYNTHETIC_LAN_STATUS: LocalLanControlStatus = {
  remoteTransportEnabled: true,
  remotePosRuntimeEnabled: true,
  configuredBindAddress: "192.168.1.161",
  effectiveBindUrl: "http://192.168.1.161:5299",
  terminalLanUrl: "http://192.168.1.161:5299",
  message: "LAN access is active.",
  warnings: [],
  terminals: [
    {
      terminalId: SYNTHETIC_IDS.terminalCounter2,
      terminalName: "Counter 2 Register",
      terminalRole: "RemotePos",
      status: "Active",
      lastSeenAtUtc: "2026-04-19T05:14:00Z",
      revokedAtUtc: null,
      disabledAtUtc: null,
    },
    {
      terminalId: SYNTHETIC_IDS.terminalAisle1,
      terminalName: "Aisle Price Checker",
      terminalRole: "RemoteReadOnly",
      status: "Disabled",
      lastSeenAtUtc: "2026-04-17T10:00:00Z",
      revokedAtUtc: null,
      disabledAtUtc: "2026-04-18T01:00:00Z",
    },
  ],
};

export const SYNTHETIC_LAN_RUNTIME_STATUS: LocalLanControlRuntimeStatus = {
  readinessState: "Ready",
  lanTransportEnabled: true,
  configuredBindAddress: "192.168.1.161",
  configuredBindMalformed: false,
  effectiveBindUrl: "http://192.168.1.161:5299",
  terminalLanUrl: "http://192.168.1.161:5299",
  restartRequired: false,
  problem: "None",
  detectedHostIps: ["192.168.1.161"],
  recommendedHostIp: "192.168.1.161",
  allowedLanTerminals: 3,
  activeTerminalCount: 1,
  remainingTerminalCapacity: 2,
  message: "Host PC is listening on 192.168.1.161:5299.",
};

export const SYNTHETIC_LAN_PAIRING_STATUS: RemotePosPairingHostStatus = {
  activeJoinCode: "482-915",
  joinCodeExpiresAtUtc: "2026-04-19T12:00:00Z",
  pendingRequests: [
    {
      requestId: "81000000-0000-0000-0000-000000000001",
      deviceName: "Counter 3 Express Tablet",
      requestedAtUtc: "2026-04-19T05:10:00Z",
      expiresAtUtc: "2026-04-19T05:40:00Z",
      status: "Pending",
      pairingCode: null,
    },
  ],
};

export function toPagedResponse<T>(items: T[], page = 1, pageSize = 25): PagedResponse<T> {
  return {
    items,
    page,
    pageSize,
    totalCount: items.length,
    totalPages: Math.max(1, Math.ceil(items.length / pageSize)),
  };
}

export function toReceivablePage<T>(items: T[], pageNumber = 1, pageSize = 50): ReceivablePage<T> {
  return {
    items,
    pageNumber,
    pageSize,
    totalCount: items.length,
    totalPages: Math.max(1, Math.ceil(items.length / pageSize)),
    hasMore: false,
    asOfUtc: "2026-04-19T05:30:00Z",
    timeZoneId: "Asia/Manila",
  };
}

export function buildCheckoutSuccessEnvelope(): CheckoutEnvelope {
  return {
    code: "CHECKOUT_COMPLETED",
    message: "Sale completed.",
    checkout: {
      status: "Completed",
      receiptNumber: "OR-20260419-0025",
      saleId: SYNTHETIC_IDS.saleCash1,
      totalCents: 66800,
      changeCents: 33200,
      message: "Sale OR-20260419-0025 recorded.",
      clientRequestId: "req-synthetic-001",
      duplicate: false,
    },
  };
}
