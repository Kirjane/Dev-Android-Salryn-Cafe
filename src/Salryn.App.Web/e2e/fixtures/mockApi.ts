import type { Page } from "@playwright/test";
import {
  buildBackupVerification,
  buildBootstrapStatus,
  buildCheckoutSuccessEnvelope,
  buildHostShiftEnvelope,
  buildLicenseStatus,
  buildProductDetail,
  buildSessionState,
  buildUserDetail,
  SYNTHETIC_ANALYTICS_OVERVIEW,
  SYNTHETIC_AR_AGING,
  SYNTHETIC_AUDIT_LOGS,
  SYNTHETIC_BACKUPS,
  SYNTHETIC_BRANDS,
  SYNTHETIC_CATEGORIES,
  SYNTHETIC_COLLECTION_RESPONSE,
  SYNTHETIC_DASHBOARD_HEALTHY,
  SYNTHETIC_DASHBOARD_WARNINGS,
  SYNTHETIC_ERROR_LOGS,
  SYNTHETIC_IDS,
  SYNTHETIC_LAN_PAIRING_STATUS,
  SYNTHETIC_LAN_RUNTIME_STATUS,
  SYNTHETIC_LAN_STATUS,
  SYNTHETIC_LOW_STOCK_PRODUCTS,
  SYNTHETIC_OWNER_REPORTS_SUMMARY,
  SYNTHETIC_PAYMENT_MIX,
  SYNTHETIC_PRODUCT_ITEMS,
  SYNTHETIC_RECEIVABLE_CUSTOMERS,
  SYNTHETIC_RECEIVABLE_LEDGER,
  SYNTHETIC_RECEIVABLES_SUMMARY,
  SYNTHETIC_SALE_DETAIL,
  SYNTHETIC_SALES_HISTORY_ITEMS,
  SYNTHETIC_SALES_TREND,
  SYNTHETIC_STOCK_MOVEMENTS,
  SYNTHETIC_STORE_BRANDING,
  SYNTHETIC_STORE_SETTINGS,
  SYNTHETIC_TOP_PRODUCTS,
  SYNTHETIC_UNITS,
  SYNTHETIC_USER_ROLES,
  SYNTHETIC_USERS,
  toPagedResponse,
  toReceivablePage,
} from "./syntheticData";
import type { LocalLicenseStatus } from "../../src/api/license";

export interface MockSalrynScenarioOptions {
  /** Startup / license state */
  licenseReady?: boolean;
  licenseStatusOverride?: LocalLicenseStatus["status"];
  licenseTypeOverride?: LocalLicenseStatus["licenseType"];
  machineMatchedOverride?: boolean;
  u1Entitled?: boolean;
  firstRunRequired?: boolean;
  startupUnavailable?: boolean;
  /** Persona */
  persona?: "admin" | "manager" | "cashier" | "unauthenticated";
  customPermissions?: string[];
  /** Dashboard variant */
  dashboardVariant?: "healthy" | "warnings";
  /** Cash shift state */
  hasOpenShift?: boolean;
  /** Receivables AR aging 403 simulation */
  arAgingForbidden?: boolean;
}

export async function installMockSalrynApi(page: Page, options: MockSalrynScenarioOptions = {}) {
  const {
    licenseReady = true,
    licenseStatusOverride,
    licenseTypeOverride,
    machineMatchedOverride,
    u1Entitled = true,
    firstRunRequired = false,
    startupUnavailable = false,
    persona = "admin",
    customPermissions,
    dashboardVariant = "healthy",
    hasOpenShift = true,
    arAgingForbidden = false,
  } = options;

  let currentSession = buildSessionState(persona, customPermissions);
  let currentShiftOpen = hasOpenShift;
  let currentFirstRun = firstRunRequired;

  await page.route(
    (url) => url.pathname.startsWith("/api/"),
    async (route) => {
    const req = route.request();
    const url = new URL(req.url());
    const path = url.pathname;
    const method = req.method();

    if (startupUnavailable && (path === "/api/bootstrap/status" || path === "/api/session")) {
      return route.fulfill({
        status: 503,
        contentType: "application/json",
        body: JSON.stringify({ message: "Salryn local host service is not reachable." }),
      });
    }

    // 1. Bootstrap, License, Session
    if (path === "/api/bootstrap/status" && method === "GET") {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(buildBootstrapStatus(currentFirstRun)),
      });
    }

    if (path === "/api/bootstrap/first-admin" && method === "POST") {
      currentFirstRun = false;
      currentSession = buildSessionState("admin");
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          adminUserId: SYNTHETIC_IDS.adminUser,
          storeSettingsId: SYNTHETIC_STORE_SETTINGS.id,
          createdAt: "2026-04-19T05:30:00Z",
          status: buildBootstrapStatus(false),
        }),
      });
    }

    if (path === "/api/license/status" && method === "GET") {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(
          buildLicenseStatus({
            ready: licenseReady,
            u1Entitled,
            status: licenseStatusOverride,
            licenseType: licenseTypeOverride,
            machineMatched: machineMatchedOverride,
          }),
        ),
      });
    }

    if (path === "/api/license/install" && method === "POST") {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          installStatus: "Installed",
          validationStatus: "Licensed",
          errorCode: null,
          errorMessage: null,
          timestamp: "2026-04-19T05:30:00Z",
        }),
      });
    }

    if (path === "/api/session" && method === "GET") {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(currentSession),
      });
    }

    if (path === "/api/auth/login" && method === "POST") {
      const body = req.postDataJSON() as { username?: string; password?: string };
      if (body?.password === "wrong-password") {
        return route.fulfill({
          status: 401,
          contentType: "application/json",
          body: JSON.stringify({ message: "Invalid username or password." }),
        });
      }
      currentSession = buildSessionState(body?.username?.includes("cashier") ? "cashier" : "admin");
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(currentSession),
      });
    }

    if (path === "/api/auth/logout" && method === "POST") {
      currentSession = buildSessionState("unauthenticated");
      return route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({}) });
    }

    // 2. Store Branding & Settings
    if (path === "/api/branding/store" && method === "GET") {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(SYNTHETIC_STORE_BRANDING),
      });
    }

    if (path === "/api/settings/store" && method === "GET") {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(SYNTHETIC_STORE_SETTINGS),
      });
    }

    if (path === "/api/settings/store" && method === "PUT") {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({ ...SYNTHETIC_STORE_SETTINGS, ...(req.postDataJSON() ?? {}) }),
      });
    }

    // 3. Dashboard
    if (path === "/api/dashboard/summary" && method === "GET") {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(
          dashboardVariant === "warnings" ? SYNTHETIC_DASHBOARD_WARNINGS : SYNTHETIC_DASHBOARD_HEALTHY,
        ),
      });
    }

    // 4. Shift & Checkout
    if (path === "/api/shift" && method === "GET") {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(buildHostShiftEnvelope(currentShiftOpen)),
      });
    }

    if (path === "/api/shift/open" && method === "POST") {
      currentShiftOpen = true;
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(buildHostShiftEnvelope(true)),
      });
    }

    if (path === "/api/shift/close" && method === "POST") {
      currentShiftOpen = false;
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(buildHostShiftEnvelope(false)),
      });
    }

    if (path === "/api/checkout" && method === "POST") {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(buildCheckoutSuccessEnvelope()),
      });
    }

    // 5. Products & Catalog
    if (path === "/api/products/catalog/categories" && method === "GET") {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(SYNTHETIC_CATEGORIES),
      });
    }

    if (path === "/api/products/catalog/brands" && method === "GET") {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(SYNTHETIC_BRANDS),
      });
    }

    if (path === "/api/products/catalog/units" && method === "GET") {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(SYNTHETIC_UNITS),
      });
    }

    if (path === "/api/products" && method === "GET") {
      const search = (url.searchParams.get("search") ?? "").toLowerCase().trim();
      const filtered = search
        ? SYNTHETIC_PRODUCT_ITEMS.filter(
            (p) =>
              p.name.toLowerCase().includes(search) ||
              (p.sku ?? "").toLowerCase().includes(search) ||
              (p.barcode ?? "").toLowerCase().includes(search),
          )
        : SYNTHETIC_PRODUCT_ITEMS;
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(toPagedResponse(filtered)),
      });
    }

    if (path.startsWith("/api/products/") && method === "GET") {
      const prodId = path.split("/").pop() ?? SYNTHETIC_IDS.prodRice5kg;
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(buildProductDetail(prodId)),
      });
    }

    if (path === "/api/products/manage" && method === "POST") {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          code: "PRODUCT_CREATED",
          message: "Product created.",
          product: SYNTHETIC_PRODUCT_ITEMS[0],
        }),
      });
    }

    // 6. Sales History & Receipt Print
    if (path === "/api/sales/history" && method === "GET") {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(toPagedResponse(SYNTHETIC_SALES_HISTORY_ITEMS)),
      });
    }

    if (path.startsWith("/api/sales/") && path.endsWith("/void") && method === "POST") {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          code: "SALE_VOIDED",
          message: "Sale voided.",
          saleId: SYNTHETIC_IDS.saleCash1,
        }),
      });
    }

    if (path.startsWith("/api/sales/") && method === "GET") {
      const saleId = path.split("/").pop();
      const matched = SYNTHETIC_SALES_HISTORY_ITEMS.find((s) => s.id === saleId);
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          ...SYNTHETIC_SALE_DETAIL,
          id: matched?.id ?? SYNTHETIC_SALE_DETAIL.id,
          receiptNo: matched?.receiptNo ?? SYNTHETIC_SALE_DETAIL.receiptNo,
          status: matched?.status ?? SYNTHETIC_SALE_DETAIL.status,
          voidReason: matched?.voidReason ?? null,
        }),
      });
    }

    // 7. Receivables (Utang Ledger)
    if (path === "/api/receivables/summary" && method === "GET") {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(SYNTHETIC_RECEIVABLES_SUMMARY),
      });
    }

    if (path === "/api/receivables/customers/page" && method === "GET") {
      const search = (url.searchParams.get("search") ?? "").toLowerCase().trim();
      const filtered = search
        ? SYNTHETIC_RECEIVABLE_CUSTOMERS.filter((c) => c.customer.fullName.toLowerCase().includes(search))
        : SYNTHETIC_RECEIVABLE_CUSTOMERS;
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(toReceivablePage(filtered)),
      });
    }

    if (path === "/api/receivables/customers" && method === "GET") {
      const search = (url.searchParams.get("search") ?? "").toLowerCase().trim();
      const filtered = search
        ? SYNTHETIC_RECEIVABLE_CUSTOMERS.filter((c) => c.customer.fullName.toLowerCase().includes(search))
        : SYNTHETIC_RECEIVABLE_CUSTOMERS;
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(filtered),
      });
    }

    if (path.startsWith("/api/receivables/customers/") && path.endsWith("/ledger/page") && method === "GET") {
      const parts = path.split("/");
      const custId = parts[parts.length - 3];
      const entries = custId === SYNTHETIC_IDS.custElena ? [] : SYNTHETIC_RECEIVABLE_LEDGER;
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(toReceivablePage(entries)),
      });
    }

    if (path.startsWith("/api/receivables/customers/") && path.endsWith("/ledger") && method === "GET") {
      const parts = path.split("/");
      const custId = parts[parts.length - 2];
      const entries = custId === SYNTHETIC_IDS.custElena ? [] : SYNTHETIC_RECEIVABLE_LEDGER;
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(entries),
      });
    }

    if (path === "/api/receivables/collections" && method === "POST") {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(SYNTHETIC_COLLECTION_RESPONSE),
      });
    }

    if (path.startsWith("/api/receivables/collections/") && path.endsWith("/void") && method === "POST") {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({ status: "Reversed" }),
      });
    }

    // 8. Owner Analytics & Owner Reports
    if (path === "/api/owner-reports/summary" && method === "GET") {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(SYNTHETIC_OWNER_REPORTS_SUMMARY),
      });
    }

    if (path === "/api/owner-analytics/overview" && method === "GET") {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(SYNTHETIC_ANALYTICS_OVERVIEW),
      });
    }

    if (path === "/api/owner-analytics/sales-trend" && method === "GET") {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(SYNTHETIC_SALES_TREND),
      });
    }

    if (path === "/api/owner-analytics/payment-mix" && method === "GET") {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(SYNTHETIC_PAYMENT_MIX),
      });
    }

    if (path === "/api/owner-analytics/top-products" && method === "GET") {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(SYNTHETIC_TOP_PRODUCTS),
      });
    }

    if (path === "/api/owner-analytics/ar-aging" && method === "GET") {
      if (arAgingForbidden || !u1Entitled) {
        return route.fulfill({
          status: 403,
          contentType: "application/json",
          body: JSON.stringify({ message: "AR aging requires U1 ReceivablesUtangLedger entitlement." }),
        });
      }
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(SYNTHETIC_AR_AGING),
      });
    }

    // 9. Inventory & Stock Movements
    if (path === "/api/inventory/low-stock" && method === "GET") {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(SYNTHETIC_LOW_STOCK_PRODUCTS),
      });
    }

    if (path === "/api/inventory/movements" && method === "GET") {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(toPagedResponse(SYNTHETIC_STOCK_MOVEMENTS)),
      });
    }

    // 10. Users & Roles
    if (path === "/api/users" && method === "GET") {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(SYNTHETIC_USERS),
      });
    }

    if (path === "/api/users/roles" && method === "GET") {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(SYNTHETIC_USER_ROLES),
      });
    }

    if (path.startsWith("/api/users/") && method === "GET") {
      const uid = path.split("/").pop() ?? SYNTHETIC_IDS.adminUser;
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(buildUserDetail(uid)),
      });
    }

    // 11. Audit & Error Logs
    if (path === "/api/audit/logs" && method === "GET") {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(toPagedResponse(SYNTHETIC_AUDIT_LOGS)),
      });
    }

    if (path === "/api/audit/errors" && method === "GET") {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(toPagedResponse(SYNTHETIC_ERROR_LOGS)),
      });
    }

    // 12. Backups
    if (path === "/api/backups" && method === "GET") {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(SYNTHETIC_BACKUPS),
      });
    }

    if (path.startsWith("/api/backups/") && path.endsWith("/verify") && method === "POST") {
      const parts = path.split("/");
      const backupId = decodeURIComponent(parts[parts.length - 2]);
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(buildBackupVerification(backupId)),
      });
    }

    // 13. Host LAN Control
    if (path === "/api/lan-control/status" && method === "GET") {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(SYNTHETIC_LAN_STATUS),
      });
    }

    if (path === "/api/lan-control/runtime-status" && method === "GET") {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(SYNTHETIC_LAN_RUNTIME_STATUS),
      });
    }

    if (path === "/api/lan-control/remote-pos/pairing" && method === "GET") {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify(SYNTHETIC_LAN_PAIRING_STATUS),
      });
    }

    // 14. Internal Official Setup & Traditional Reporting
    if (path.startsWith("/api/official/")) {
      return route.fulfill({
        status: 200,
        contentType: "application/json",
        body: JSON.stringify({
          contractVersion: 1,
          status: "DRY_RUN_ONLY",
          dryRunOnly: true,
          nonOfficial: true,
          blocked: true,
          liveOfficialPostingAllowed: false,
          officialOutputEnabledInConfig: false,
          officialCheckoutAllocationEnabled: false,
          checkoutUsesAllocator: false,
          checkoutIntegrated: false,
          checkoutBound: false,
          remotePosOfficialAllowed: false,
          officialOutputImplemented: false,
          complianceClaimed: false,
          eisJsonSchemaResolved: false,
          complianceMode: "INTERNAL_DRY_RUN",
          defaultTrack: "TRADITIONAL_INTERNAL",
          summary: "Internal dry-run only; not live-bound.",
          note: "Production activation remains OFF.",
          warnings: [],
          blockers: [],
          notes: [],
          allowedActions: [],
          components: [],
          packages: [],
          documentType: "OFFICIAL_INVOICE",
          prefix: "INV",
          currentNumber: 0,
          nextNumber: 1,
          formattedNextNumber: "INV-2026-000001",
          rangeStart: 1,
          rangeEnd: null,
          previewMutatesSequence: false,
          allocationAllowed: false,
          mode: "PREVIEW_ONLY",
          blockedReason: "Live allocation disabled in commercial build.",
          business: null,
          machine: null,
          receipt: null,
          series: null,
          journal: {
            skeletonPresent: true,
            schemaPresent: true,
            liveBound: false,
            liveAppendEnabled: false,
            checkoutIntegrated: false,
            summary: "Internal EJ skeleton present.",
            blockers: [],
          },
          voidRefund: {
            skeletonPresent: true,
            liveIntegrated: false,
            checkoutIntegrated: false,
            summary: "Internal void skeleton present.",
            blockers: [],
            placeholderReasons: [],
          },
        }),
      });
    }

    // Default fallback
    return route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({}),
    });
  });
}
