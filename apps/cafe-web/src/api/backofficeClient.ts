import { deriveScopedOperationId, hostRequest } from "./hostClient";
import type {
  ArCustomerRow,
  AttendanceRow,
  AuditLogRow,
  BackofficeApi,
  BackupPackageRow,
  CafeBookingRow,
  CafeDeliveryRow,
  CafeRefundSummaryRow,
  CashMovementRow,
  IncidentRow,
  IngredientRecipeDraft,
  LanControlStatusSnapshot,
  LanPairingSnapshot,
  LicenseStatusSnapshot,
  LowStockItem,
  OwnerReportsSnapshot,
  RefundableSaleRow,
  StoreSettingsSnapshot,
  SupplierSummary,
  UserRoleOption,
  UserRow,
} from "../backoffice/types";

export function createLiveBackofficeApi(): BackofficeApi {
  return {
    getOwnerReports: () => hostRequest<OwnerReportsSnapshot>("/api/owner-reports/summary"),
    getIngredientsAndRecipes: () => hostRequest<IngredientRecipeDraft>("/api/cafe/ingredients-recipes"),
    publishMenuAndRecipes: async (operationId) => {
      await hostRequest<unknown>("/api/menu/publish", {
        operationId: deriveScopedOperationId(operationId, "menu-publish"),
      });
      await hostRequest<unknown>("/api/cafe/recipes/publish", {
        operationId: deriveScopedOperationId(operationId, "recipes-publish"),
      });
    },
    getLowStock: () => hostRequest<LowStockItem[]>("/api/cafe/inventory/low-stock"),
    getSuppliers: () => hostRequest<SupplierSummary[]>("/api/cafe/suppliers"),
    receivePurchase: (req) =>
      hostRequest<void>("/api/cafe/purchases/receive", {
        operationId: req.operationId,
        supplierId: req.supplierId,
        notes: req.notes ?? "Back-office purchase receipt",
        lines: [
          {
            lineId: crypto.randomUUID(),
            stockTargetType: "Ingredient",
            stockTargetId: req.stockTargetId,
            quantityReceivedX1000: req.quantityX1000,
            lineAcquisitionCostCents: req.lineAcquisitionCostCents,
          },
        ],
      }),
    recordWaste: (req) =>
      hostRequest<void>("/api/cafe/inventory/waste", {
        operationId: req.operationId,
        reason: req.reason,
        notes: req.notes,
        lines: [
          {
            lineId: crypto.randomUUID(),
            stockTargetType: "Ingredient",
            stockTargetId: req.stockTargetId,
            quantityWastedX1000: req.quantityWastedX1000,
          },
        ],
      }),
    getReceivableCustomers: () => hostRequest<ArCustomerRow[]>("/api/receivables/customers"),
    collectArCash: (req) =>
      hostRequest<void>("/api/receivables/collections", {
        customerId: req.customerId,
        amountCents: req.amountCents,
        method: "Cash",
        note: req.note ?? "Back-office Cash AR collection",
        clientRequestId: req.clientRequestId,
      }),
    getCashMovements: () => hostRequest<CashMovementRow[]>("/api/cafe/cash/movements"),
    postCashMovement: (req) =>
      hostRequest<void>("/api/cafe/cash/movements", {
        operationId: req.operationId,
        movementType: req.movementType,
        amountCents: req.amountCents,
        reason: req.reason,
      }),
    getRefunds: () => hostRequest<CafeRefundSummaryRow[]>("/api/cafe/refunds"),
    getRefundableSales: (search) =>
      hostRequest<RefundableSaleRow[]>(
        `/api/cafe/refunds/sales${search ? `?search=${encodeURIComponent(search)}` : ""}`,
      ),
    requestRefund: (req) =>
      hostRequest<{ refundId: string; version: number; state: string }>("/api/cafe/refunds", {
        operationId: req.operationId,
        saleId: req.saleId,
        expectedVersion: 0,
        reason: req.reason,
        lines: req.lines,
      }),
    resolveRefund: (req) =>
      hostRequest<{ refundId: string; version: number; state: string }>(
        `/api/cafe/refunds/${req.refundId}/resolve`,
        {
          operationId: req.operationId,
          saleId: req.saleId,
          expectedVersion: req.expectedVersion,
          approve: req.approve,
          reason: req.reason ?? (req.approve ? "Approved by back-office manager" : "Rejected by back-office manager"),
        },
      ),
    commitRefund: (req) =>
      hostRequest<{ refundId: string; version: number; state: string; totalRefundCents: number }>(
        `/api/cafe/refunds/${req.refundId}/commit`,
        {
          operationId: req.operationId,
          saleId: req.saleId,
          expectedVersion: req.expectedVersion,
          settlements: req.settlements,
        },
      ),
    getBookings: () => hostRequest<CafeBookingRow[]>("/api/cafe/bookings"),
    advanceBookingStatus: (req) =>
      hostRequest<void>(`/api/cafe/bookings/${req.bookingId}/${req.action}`, {
        operationId: req.operationId,
        expectedVersion: req.expectedVersion,
        reason: req.reason,
      }),
    getDeliveries: () => hostRequest<CafeDeliveryRow[]>("/api/cafe/deliveries"),
    advanceDeliveryStatus: (req) =>
      req.action === "dispatch"
        ? hostRequest<void>(`/api/cafe/deliveries/${req.deliveryId}/dispatch`, {
            operationId: req.operationId,
            expectedVersion: req.expectedVersion,
          })
        : hostRequest<void>(`/api/cafe/deliveries/${req.deliveryId}/resolve`, {
            operationId: req.operationId,
            expectedVersion: req.expectedVersion,
            targetStatus: req.action === "deliver" ? "Delivered" : "Failed",
          }),
    getAttendance: () => hostRequest<AttendanceRow[]>("/api/cafe/attendance"),
    recordAttendance: (req) =>
      hostRequest<void>("/api/cafe/attendance", {
        operationId: req.operationId,
        eventType: req.eventType,
      }),
    getIncidents: () => hostRequest<IncidentRow[]>("/api/cafe/incidents"),
    createIncident: (req) =>
      hostRequest<void>("/api/cafe/incidents", {
        operationId: req.operationId,
        incidentOccurredAtUtc: new Date().toISOString(),
        title: req.title,
        description: req.description,
        classification: req.classification,
        severity: req.severity,
      }),
    getAuditLogs: async () => {
      const page = await hostRequest<{ items?: AuditLogRow[] } | AuditLogRow[]>("/api/audit/logs?page=1&pageSize=30");
      return Array.isArray(page) ? page : page.items ?? [];
    },
    getUsers: () => hostRequest<UserRow[]>("/api/users"),
    getUserRoles: () => hostRequest<UserRoleOption[]>("/api/users/roles"),
    createUser: (req) => hostRequest<UserRow>("/api/users", req),
    getStoreSettings: () => hostRequest<StoreSettingsSnapshot>("/api/settings/store"),
    updateStoreSettings: (req) => hostRequest<void>("/api/settings/store", req, "PUT"),
    getBackups: () => hostRequest<BackupPackageRow[]>("/api/backups"),
    createBackup: () => hostRequest<BackupPackageRow>("/api/backups", {}),
    verifyBackup: async (backupId) => {
      const verified = await hostRequest<{
        backupId: string;
        manifest?: { schemaVersion?: number; files?: unknown[] };
        database?: { pageCount?: number };
        storeName?: string | null;
      }>(`/api/backups/${backupId}/verify`, {});
      return {
        valid: Boolean(verified?.backupId),
        summary: `Manifest v${verified?.manifest?.schemaVersion ?? "?"} · ${verified?.storeName ?? "Salryn Cafe"} (${verified?.manifest?.files?.length ?? 0} files)`,
      };
    },
    restoreBackup: (backupId, confirmation) =>
      hostRequest<void>(`/api/backups/${backupId}/restore`, { confirmation }),
    getLicenseStatus: () => hostRequest<LicenseStatusSnapshot>("/api/license/status"),
    getLanStatus: () => hostRequest<LanControlStatusSnapshot>("/api/lan-control/status"),
    getLanPairing: () => hostRequest<LanPairingSnapshot>("/api/lan-control/remote-pos/pairing"),
    rotateLanJoinCode: async () => {
      const res = await hostRequest<{ joinCode: string; issuedAtUtc: string; expiresAtUtc: string; message: string }>(
        "/api/lan-control/remote-pos/pairing/join-code/rotate",
        {},
      );
      return {
        joinCodeIssued: true,
        joinCodeExpiresAtUtc: res.expiresAtUtc,
        pendingRequestCount: 0,
        pendingRequests: [],
        message: res.message,
        activeJoinCode: res.joinCode,
      };
    },
  };
}
