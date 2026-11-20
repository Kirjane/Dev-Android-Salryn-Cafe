namespace Salryn.Api.Local.Auth;

internal static class LocalApiPermissions
{
    public const string ProductsView = "Products.View";
    public const string ProductsManage = "Products.Manage";
    public const string ProductsFastAdd = "Products.FastAdd";
    public const string InventoryView = "Inventory.View";
    public const string InventoryAdjust = "Inventory.Adjust";
    public const string InventoryWaste = "Inventory.Waste";
    public const string InventoryCount = "Inventory.Count";
    public const string SuppliersView = "Suppliers.View";
    public const string SuppliersManage = "Suppliers.Manage";
    public const string PurchasingView = "Purchasing.View";
    public const string PurchasingReceive = "Purchasing.Receive";
    public const string OrdersView = "Orders.View";
    public const string OrdersCreate = "Orders.Create";
    public const string OrdersManage = "Orders.Manage";
    public const string OrdersCancel = "Orders.Cancel";
    public const string OrdersComplete = "Orders.Complete";
    public const string PreparationView = "Preparation.View";
    public const string PreparationManage = "Preparation.Manage";
    public const string IngredientsView = "Ingredients.View";
    public const string IngredientsManage = "Ingredients.Manage";
    public const string RecipesView = "Recipes.View";
    public const string RecipesManage = "Recipes.Manage";
    public const string RecipesPublish = "Recipes.Publish";
    public const string SalesViewHistory = "Sales.ViewHistory";
    public const string ReportsView = "Reports.View";
    public const string UsersManage = "Users.Manage";
    public const string AuditView = "Audit.View";
    public const string StoreTerminalView = "StoreTerminal.View";
    public const string StoreTerminalManage = "StoreTerminal.Manage";
    public const string SettingsManage = "Settings.Manage";
    public const string ShiftOpenClose = "Shift.OpenClose";
    public const string SalesCreate = "Sales.Create";
    public const string SalesDiscountApply = "Sales.DiscountApply";
    public const string SalesFeeApply = "Sales.FeeApply";
    public const string SalesRefundRequest = "Sales.RefundRequest";
    public const string SalesRefundApprove = "Sales.RefundApprove";
    public const string SalesVoid = "Sales.Void";
    public const string BackupManage = "Backup.Manage";
    public const string CashView = "Cash.View";
    public const string CashIn = "Cash.In";
    public const string CashOut = "Cash.Out";
    public const string CashReverse = "Cash.Reverse";
    public const string ExpensesRecord = "Expenses.Record";
    public const string PrintingView = "Printing.View";
    public const string PrintingPrint = "Printing.Print";
    public const string PrintingReprint = "Printing.Reprint";
    public const string PrintingStationManage = "Printing.StationManage";
    public const string DeliveryView = "Delivery.View";
    public const string DeliveryManage = "Delivery.Manage";
    public const string DeliveryAdvance = "Delivery.Advance";
    public const string BookingView = "Booking.View";
    public const string BookingManage = "Booking.Manage";
    public const string BookingCancel = "Booking.Cancel";
    public const string BookingFulfill = "Booking.Fulfill";
    public const string BookingDepositReceive = "Booking.DepositReceive";
    public const string BookingDepositRefund = "Booking.DepositRefund";
    public const string AttendanceView = "Attendance.View";
    public const string AttendanceRecord = "Attendance.Record";
    public const string AttendanceManage = "Attendance.Manage";
    public const string IncidentView = "Incident.View";
    public const string IncidentRecord = "Incident.Record";
    public const string IncidentManage = "Incident.Manage";
    public const string ReceivablesView = "Receivables.View";
    public const string ReceivablesCreateCreditSale = "Receivables.CreateCreditSale";
    public const string ReceivablesCollect = "Receivables.Collect";
    public const string ReceivablesAdjust = "Receivables.Adjust";
    public const string ReceivablesExport = "Receivables.Export";
    public const string ReceivablesManageCustomers = "Receivables.ManageCustomers";
    public const string ProcurementView = "Procurement.View";
    public const string ProcurementRequest = "Procurement.Request";
    public const string ProcurementApproveRequisition = "Procurement.ApproveRequisition";
    public const string ProcurementManagePurchaseOrder = "Procurement.ManagePurchaseOrder";
    public const string ProcurementApprovePurchaseOrder = "Procurement.ApprovePurchaseOrder";
    public const string ReceivingView = "Receiving.View";
    public const string ReceivingManage = "Receiving.Manage";
    public const string ReceivingApproveException = "Receiving.ApproveException";
    public const string InventoryManageReturns = "Inventory.ManageReturns";
    public const string PayablesView = "Payables.View";
    public const string PayablesManageInvoices = "Payables.ManageInvoices";
    public const string PayablesApproveVariance = "Payables.ApproveVariance";
    public const string PayablesRecordPayment = "Payables.RecordPayment";
    public const string PayablesApprovePayment = "Payables.ApprovePayment";
}

internal static class LocalApiPermissionGuards
{
    public static IResult? RequireAuthenticatedSession(
        HttpContext httpContext,
        LocalApiSessionService sessions)
    {
        if (LocalApiLocalAccessGuard.RequireLoopbackOperatorRequest(httpContext) is IResult localOnly)
        {
            return localOnly;
        }

        return GetSession(httpContext, sessions) is null
            ? AuthenticationRequired()
            : null;
    }

    public static IResult? RequirePermission(
        HttpContext httpContext,
        LocalApiSessionService sessions,
        string permission)
    {
        if (LocalApiLocalAccessGuard.RequireLoopbackOperatorRequest(httpContext) is IResult localOnly)
        {
            return localOnly;
        }

        LocalApiSession? session = GetSession(httpContext, sessions);

        if (session is null)
        {
            return AuthenticationRequired();
        }

        return session.Permissions.Contains(permission, StringComparer.Ordinal)
            ? null
            : PermissionDenied(permission);
    }

    public static IResult? RequireAnyPermission(
        HttpContext httpContext,
        LocalApiSessionService sessions,
        IReadOnlyCollection<string> permissions)
    {
        if (LocalApiLocalAccessGuard.RequireLoopbackOperatorRequest(httpContext) is IResult localOnly)
        {
            return localOnly;
        }

        LocalApiSession? session = GetSession(httpContext, sessions);

        if (session is null)
        {
            return AuthenticationRequired();
        }

        return permissions.Any(permission => session.Permissions.Contains(permission, StringComparer.Ordinal))
            ? null
            : PermissionDenied(string.Join(", ", permissions));
    }

    public static IResult? RequireSettingsManage(
        HttpContext httpContext,
        LocalApiSessionService sessions) =>
        RequirePermission(httpContext, sessions, LocalApiPermissions.SettingsManage);

    private static LocalApiSession? GetSession(
        HttpContext httpContext,
        LocalApiSessionService sessions)
    {
        string? sessionId = httpContext.Request.Cookies[LocalApiSessionService.SessionCookieName];
        return sessions.GetRequiredSession(sessionId);
    }

    private static IResult AuthenticationRequired()
    {
        return Results.Json(
            new
            {
                error = "Authentication is required.",
                code = "Auth.AuthenticationRequired"
            },
            statusCode: StatusCodes.Status401Unauthorized);
    }

    private static IResult PermissionDenied(string requiredPermission)
    {
        return Results.Json(
            new
            {
                error = "Permission denied.",
                code = "Auth.PermissionDenied",
                requiredPermission
            },
            statusCode: StatusCodes.Status403Forbidden);
    }
}
