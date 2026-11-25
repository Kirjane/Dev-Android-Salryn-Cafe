/**
 * Canonical Host RBAC permission constants matching DefaultSeedData.Permissions
 * and LocalApiPermissions (singular domain prefixes — never ISO plural drift).
 */
export const R3_HOST_PERMISSIONS = {
  ReportsView: "Reports.View",
  ReceivablesView: "Receivables.View",
  ReceivablesCreateCreditSale: "Receivables.CreateCreditSale",
  InventoryView: "Inventory.View",
  InventoryAdjust: "Inventory.Adjust",
  InventoryCount: "Inventory.Count",
  InventoryWaste: "Inventory.Waste",
  PurchasingReceive: "Purchasing.Receive",
  SuppliersView: "Suppliers.View",
  SuppliersManage: "Suppliers.Manage",
  IngredientsView: "Ingredients.View",
  RecipesView: "Recipes.View",
  ProductsView: "Products.View",
  ProductsManage: "Products.Manage",
  OrdersView: "Orders.View",
  OrdersCreate: "Orders.Create",
  OrdersManage: "Orders.Manage",
  OrdersComplete: "Orders.Complete",
  OrdersCancel: "Orders.Cancel",
  PreparationManage: "Preparation.Manage",
  SalesCreate: "Sales.Create",
  SalesRefundRequest: "Sales.RefundRequest",
  SalesRefundApprove: "Sales.RefundApprove",
  ShiftOpenClose: "Shift.OpenClose",
  CashIn: "Cash.In",
  CashOut: "Cash.Out",
  BookingView: "Booking.View",
  BookingManage: "Booking.Manage",
  BookingCancel: "Booking.Cancel",
  BookingDepositReceive: "Booking.DepositReceive",
  BookingDepositRefund: "Booking.DepositRefund",
  BookingFulfill: "Booking.Fulfill",
  DeliveryView: "Delivery.View",
  DeliveryManage: "Delivery.Manage",
  DeliveryAdvance: "Delivery.Advance",
  AttendanceRecord: "Attendance.Record",
  IncidentView: "Incident.View",
  IncidentRecord: "Incident.Record",
  AuditView: "Audit.View",
  UsersManage: "Users.Manage",
  SettingsManage: "Settings.Manage",
} as const;

export type R3HostPermission = (typeof R3_HOST_PERMISSIONS)[keyof typeof R3_HOST_PERMISSIONS];

export function hasHostPermission(
  permissions: readonly string[] | null | undefined,
  required: R3HostPermission,
): boolean {
  return Array.isArray(permissions) && permissions.includes(required);
}
