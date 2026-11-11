using Salryn.Api.Local.Auth;
using Salryn.Core.Common;
using Salryn.Core.Models.ProcureToPay;
using Salryn.Core.Services;

namespace Salryn.Api.Local;

internal static class LocalProcureToPayEndpoints
{
    private static readonly string[] AnyLpiViewPermissions =
    {
        LocalApiPermissions.ProcurementView,
        LocalApiPermissions.ReceivingView,
        LocalApiPermissions.PayablesView,
        LocalApiPermissions.InventoryView,
        LocalApiPermissions.ReportsView
    };

    // Requesters may read only the catalogs and PR records needed to prepare requisitions.
    // Do not add ProcurementRequest to AnyLpiViewPermissions: PO and finance reads use it.
    private static readonly string[] RequisitionReadPermissions =
        AnyLpiViewPermissions.Append(LocalApiPermissions.ProcurementRequest).ToArray();

    private static readonly string[] PrivilegedSupplierReadPermissions =
    {
        LocalApiPermissions.ProcurementManagePurchaseOrder,
        LocalApiPermissions.PayablesView
    };

    // The default cashier role has Procurement.View; never treat it as supplier-directory access.
    private static bool IsCashier(LocalApiSession session) =>
        session.RoleId == Guid.Parse("00000000-0000-0000-0000-000000000003");

    public static WebApplication MapLocalProcureToPayEndpoints(this WebApplication app)
    {
        // S0: Foundation & Supplier CRUD
        app.MapGet("/api/lpi/foundation", async (
            bool? includeInactiveSuppliers,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequireAnyPermission(context, sessions, PrivilegedSupplierReadPermissions) is IResult denied)
                return denied;
            if (IsCashier(RequiredSession(context, sessions))) return Results.StatusCode(403);
            return ToResult(await service.GetFoundationCatalogAsync(includeInactiveSuppliers ?? true, ct));
        });

        // PR requesters receive product identity and UOM only. Never serialize supplier,
        // cost, stock, sales, financial or location data into this response.
        app.MapGet("/api/lpi/requisition-catalog", async (
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(context, sessions, LocalApiPermissions.ProcurementRequest) is IResult denied)
                return denied;
            var catalog = await service.GetFoundationCatalogAsync(false, ct);
            if (!catalog.IsSuccess)
                return ToResult(catalog);
            return Results.Ok(new
            {
                unitsOfMeasure = catalog.Value.UnitsOfMeasure.Select(unit => new
                {
                    unit.Id, unit.Code, unit.Name, unit.AllowsDecimalQuantity
                }),
                products = catalog.Value.Products.Select(product => new
                {
                    product.Id, product.Name, product.Barcode, product.UnitId, product.UnitName
                })
            });
        });

        app.MapGet("/api/lpi/overview", async (
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequireAnyPermission(context, sessions, AnyLpiViewPermissions) is IResult denied)
                return denied;
            var result = await service.GetDashboardOverviewAsync(ct);
            if (!result.IsSuccess)
                return ToResult(result);

            LocalApiSession session = RequiredSession(context, sessions);
            bool canViewPayables = session.Permissions.Contains(LocalApiPermissions.PayablesView, StringComparer.Ordinal);
            if (!canViewPayables)
            {
                var redacted = result.Value with
                {
                    TotalOpenPayablesCents = 0L,
                    OverduePayablesCents = 0L,
                    VarianceFlaggedInvoiceCount = 0,
                    TotalPaymentsDisbursedCents = 0L,
                    ApAging = new ApAgingSummaryModel(0L, 0L, 0L, 0L, 0L, 0L, 0, 0, 0, Array.Empty<SupplierInvoiceModel>())
                };
                return Results.Ok(redacted);
            }
            return Results.Ok(result.Value);
        });

        app.MapGet("/api/lpi/suppliers", async (
            string? search,
            bool? includeInactive,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequireAnyPermission(context, sessions, PrivilegedSupplierReadPermissions) is IResult denied)
                return denied;
            if (IsCashier(RequiredSession(context, sessions))) return Results.StatusCode(403);
            return ToResult(await service.GetSuppliersAsync(search, includeInactive ?? true, ct));
        });

        app.MapPost("/api/lpi/suppliers", async (
            SupplierWriteApiRequest request,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(context, sessions, LocalApiPermissions.ProcurementManagePurchaseOrder) is IResult denied)
                return denied;
            if (IsCashier(RequiredSession(context, sessions))) return Results.StatusCode(403);
            LocalApiSession session = RequiredSession(context, sessions);
            return ToResult(
                await service.CreateSupplierAsync(new CreateLpiSupplierRequest(
                    request.Code,
                    request.Name,
                    request.ContactPerson,
                    request.ContactNumber,
                    request.Email,
                    request.Address,
                    request.Tin,
                    request.PaymentTermsDays ?? 30,
                    request.LeadTimeDays ?? 3,
                    request.Notes,
                    session.UserId), ct),
                StatusCodes.Status201Created);
        });

        app.MapPut("/api/lpi/suppliers/{id:guid}", async (
            Guid id,
            SupplierUpdateApiRequest request,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(context, sessions, LocalApiPermissions.ProcurementManagePurchaseOrder) is IResult denied)
                return denied;
            if (IsCashier(RequiredSession(context, sessions))) return Results.StatusCode(403);
            LocalApiSession session = RequiredSession(context, sessions);
            return ToResult(
                await service.UpdateSupplierAsync(id, new UpdateLpiSupplierRequest(
                    request.Code,
                    request.Name,
                    request.ContactPerson,
                    request.ContactNumber,
                    request.Email,
                    request.Address,
                    request.Tin,
                    request.PaymentTermsDays ?? 30,
                    request.LeadTimeDays ?? 3,
                    request.Notes,
                    request.IsActive ?? true,
                    session.UserId), ct));
        });

        // S1: Purchase Requisitions (PR), Quotations & Purchase Orders (PO)
        app.MapGet("/api/lpi/requisitions", async (
            string? status,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequireAnyPermission(context, sessions, RequisitionReadPermissions) is IResult denied)
                return denied;
            return ToResult(await service.GetPurchaseRequisitionsAsync(status, ct));
        });

        app.MapGet("/api/lpi/requisitions/{id:guid}", async (
            Guid id,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequireAnyPermission(context, sessions, RequisitionReadPermissions) is IResult denied)
                return denied;
            return ToResult(await service.GetPurchaseRequisitionByIdAsync(id, ct));
        });

        app.MapPost("/api/lpi/requisitions", async (
            CreateRequisitionApiRequest request,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(context, sessions, LocalApiPermissions.ProcurementRequest) is IResult denied)
                return denied;
            LocalApiSession session = RequiredSession(context, sessions);
            DateOnly needed = request.ResolvedDateNeeded ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
            return ToResult(
                await service.CreatePurchaseRequisitionAsync(new CreatePurchaseRequisitionRequest(
                    request.ResolvedDepartment,
                    needed,
                    request.Priority ?? "Normal",
                    request.Purpose ?? "Requisition",
                    request.Notes,
                    request.SubmitImmediately ?? false,
                    request.ResolvedLines,
                    session.UserId), ct),
                StatusCodes.Status201Created);
        });

        app.MapPost("/api/lpi/requisitions/{id:guid}/submit", async (
            Guid id,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(context, sessions, LocalApiPermissions.ProcurementRequest) is IResult denied)
                return denied;
            LocalApiSession session = RequiredSession(context, sessions);
            return ToResult(await service.SubmitPurchaseRequisitionAsync(id, session.UserId, ct));
        });

        app.MapPost("/api/lpi/requisitions/{id:guid}/review", async (
            Guid id,
            ReviewRequisitionApiRequest request,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(context, sessions, LocalApiPermissions.ProcurementApproveRequisition) is IResult denied)
                return denied;
            LocalApiSession session = RequiredSession(context, sessions);
            return ToResult(await service.ReviewPurchaseRequisitionAsync(new ReviewPurchaseRequisitionRequest(
                id,
                request.Decision,
                request.Reason,
                request.LineDecisions,
                session.UserId), ct));
        });

        app.MapPost("/api/lpi/requisitions/{id:guid}/approve", async (
            Guid id,
            NotesOnlyApiRequest? request,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(context, sessions, LocalApiPermissions.ProcurementApproveRequisition) is IResult denied)
                return denied;
            LocalApiSession session = RequiredSession(context, sessions);
            return ToResult(await service.ReviewPurchaseRequisitionAsync(new ReviewPurchaseRequisitionRequest(
                id,
                "Approve",
                request?.Notes ?? "Approved via web",
                null,
                session.UserId), ct));
        });

        app.MapPost("/api/lpi/requisitions/{id:guid}/reject", async (
            Guid id,
            ReasonOnlyApiRequest? request,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(context, sessions, LocalApiPermissions.ProcurementApproveRequisition) is IResult denied)
                return denied;
            LocalApiSession session = RequiredSession(context, sessions);
            return ToResult(await service.ReviewPurchaseRequisitionAsync(new ReviewPurchaseRequisitionRequest(
                id,
                "Reject",
                request?.Reason ?? "Rejected via web",
                null,
                session.UserId), ct));
        });

        app.MapPost("/api/lpi/requisitions/{id:guid}/revise", async (
            Guid id,
            RevisionOnlyApiRequest? request,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(context, sessions, LocalApiPermissions.ProcurementApproveRequisition) is IResult denied)
                return denied;
            LocalApiSession session = RequiredSession(context, sessions);
            return ToResult(await service.ReviewPurchaseRequisitionAsync(new ReviewPurchaseRequisitionRequest(
                id,
                "RequestRevision",
                request?.RevisionNotes ?? request?.Reason ?? "Needs revision via web",
                null,
                session.UserId), ct));
        });

        app.MapPost("/api/lpi/requisitions/{id:guid}/convert-to-po", async (
            Guid id,
            ConvertToPoApiRequest request,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(context, sessions, LocalApiPermissions.ProcurementManagePurchaseOrder) is IResult denied)
                return denied;
            if (request is null || request.SupplierId == Guid.Empty)
            {
                return Results.Json(new { error = "A valid supplier must be selected to generate a Purchase Order.", code = "Lpi.PurchaseOrder.SupplierRequired" }, statusCode: StatusCodes.Status400BadRequest);
            }
            LocalApiSession session = RequiredSession(context, sessions);
            return ToResult(await service.ConvertRequisitionToPurchaseOrderAsync(
                id,
                request.SupplierId,
                request.RequisitionLineIds,
                request.DeliveryLocationId,
                request.ExpectedDeliveryDate,
                session.UserId,
                ct), StatusCodes.Status201Created);
        });

        app.MapGet("/api/lpi/quotations", async (
            Guid? requisitionId,
            Guid? productId,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequireAnyPermission(context, sessions, PrivilegedSupplierReadPermissions) is IResult denied)
                return denied;
            if (IsCashier(RequiredSession(context, sessions))) return Results.StatusCode(403);
            return ToResult(await service.GetSupplierQuotationsAsync(requisitionId, productId, ct));
        });

        app.MapPost("/api/lpi/quotations", async (
            RecordQuotationApiRequest request,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(context, sessions, LocalApiPermissions.ProcurementManagePurchaseOrder) is IResult denied)
                return denied;
            if (IsCashier(RequiredSession(context, sessions))) return Results.StatusCode(403);
            LocalApiSession session = RequiredSession(context, sessions);
            return ToResult(
                await service.RecordSupplierQuotationAsync(new RecordSupplierQuotationRequest(
                    request.RequisitionId,
                    request.RequisitionLineId,
                    request.SupplierId,
                    request.ProductId,
                    request.ItemDescription,
                    request.QuotedUnitCostCents,
                    request.MinimumOrderQtyX1000 ?? 1000L,
                    request.LeadTimeDays ?? 3,
                    request.PaymentTermsDays ?? 30,
                    request.ValidityDate,
                    request.IsSelected ?? false,
                    request.SelectionReason,
                    session.UserId), ct),
                StatusCodes.Status201Created);
        });

        app.MapPost("/api/lpi/quotations/{id:guid}/select-winner", async (
            Guid id,
            SelectWinnerApiRequest? request,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(context, sessions, LocalApiPermissions.ProcurementManagePurchaseOrder) is IResult denied)
                return denied;
            if (IsCashier(RequiredSession(context, sessions))) return Results.StatusCode(403);
            LocalApiSession session = RequiredSession(context, sessions);
            return ToResult(await service.SelectWinningQuotationAsync(
                id,
                request?.Justification ?? request?.Reason,
                session.UserId,
                ct));
        });

        app.MapGet("/api/lpi/purchase-orders", async (
            string? status,
            Guid? supplierId,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequireAnyPermission(context, sessions, new[] { LocalApiPermissions.ProcurementManagePurchaseOrder, LocalApiPermissions.ProcurementApprovePurchaseOrder }) is IResult denied)
                return denied;
            return ToResult(await service.GetPurchaseOrdersAsync(status, supplierId, ct));
        });

        app.MapGet("/api/lpi/purchase-orders/{id:guid}", async (
            Guid id,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequireAnyPermission(context, sessions, new[] { LocalApiPermissions.ProcurementManagePurchaseOrder, LocalApiPermissions.ProcurementApprovePurchaseOrder }) is IResult denied)
                return denied;
            return ToResult(await service.GetPurchaseOrderByIdAsync(id, ct));
        });

        app.MapPost("/api/lpi/purchase-orders", async (
            CreatePurchaseOrderApiRequest request,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(context, sessions, LocalApiPermissions.ProcurementManagePurchaseOrder) is IResult denied)
                return denied;
            LocalApiSession session = RequiredSession(context, sessions);
            DateOnly expDate = request.ExpectedDeliveryDate ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
            return ToResult(
                await service.CreatePurchaseOrderAsync(new CreatePurchaseOrderRequest(
                    request.SupplierId,
                    request.ResolvedRequisitionId,
                    expDate,
                    request.DeliveryLocationId,
                    request.PaymentTermsDays,
                    request.ShippingCents ?? 0L,
                    request.TermsAndConditions,
                    request.Notes,
                    request.SubmitAndApproveImmediately ?? false,
                    request.ResolvedLines,
                    session.UserId), ct),
                StatusCodes.Status201Created);
        });

        app.MapPost("/api/lpi/purchase-orders/{id:guid}/status", async (
            Guid id,
            PurchaseOrderStatusApiRequest request,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            // Normalize the public action alias at the API boundary; the service uses MarkSent.
            string action = string.Equals(request.Action, "Send", StringComparison.OrdinalIgnoreCase)
                ? "MarkSent"
                : request.Action;
            string requiredPerm = string.Equals(action, "Approve", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(action, "Cancel", StringComparison.OrdinalIgnoreCase)
                ? LocalApiPermissions.ProcurementApprovePurchaseOrder
                : LocalApiPermissions.ProcurementManagePurchaseOrder;
            if (LocalApiPermissionGuards.RequirePermission(context, sessions, requiredPerm) is IResult denied)
                return denied;
            LocalApiSession session = RequiredSession(context, sessions);
            return ToResult(await service.TransitionPurchaseOrderStatusAsync(new PurchaseOrderStatusActionRequest(
                id,
                action,
                request.Reason,
                session.UserId), ct));
        });

        app.MapPost("/api/lpi/purchase-orders/{id:guid}/approve", async (
            Guid id,
            ReasonOnlyApiRequest? request,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(context, sessions, LocalApiPermissions.ProcurementApprovePurchaseOrder) is IResult denied)
                return denied;
            LocalApiSession session = RequiredSession(context, sessions);
            return ToResult(await service.TransitionPurchaseOrderStatusAsync(new PurchaseOrderStatusActionRequest(
                id,
                "Approve",
                request?.Reason ?? request?.Notes,
                session.UserId), ct));
        });

        app.MapPost("/api/lpi/purchase-orders/{id:guid}/mark-sent", async (
            Guid id,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(context, sessions, LocalApiPermissions.ProcurementManagePurchaseOrder) is IResult denied)
                return denied;
            LocalApiSession session = RequiredSession(context, sessions);
            return ToResult(await service.TransitionPurchaseOrderStatusAsync(new PurchaseOrderStatusActionRequest(
                id,
                "MarkSent",
                null,
                session.UserId), ct));
        });

        app.MapPost("/api/lpi/purchase-orders/{id:guid}/cancel", async (
            Guid id,
            ReasonOnlyApiRequest? request,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(context, sessions, LocalApiPermissions.ProcurementApprovePurchaseOrder) is IResult denied)
                return denied;
            LocalApiSession session = RequiredSession(context, sessions);
            return ToResult(await service.TransitionPurchaseOrderStatusAsync(new PurchaseOrderStatusActionRequest(
                id,
                "Cancel",
                request?.Reason,
                session.UserId), ct));
        });

        // S2: Receiving Reports (RR), Stock Posting, Count Adjustments & Supplier Returns
        app.MapGet("/api/lpi/receiving-reports", async (
            Guid? purchaseOrderId,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequireAnyPermission(context, sessions, AnyLpiViewPermissions) is IResult denied)
                return denied;
            return ToResult(await service.GetReceivingReportsAsync(purchaseOrderId, ct));
        });

        app.MapGet("/api/lpi/receiving-reports/{id:guid}", async (
            Guid id,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequireAnyPermission(context, sessions, AnyLpiViewPermissions) is IResult denied)
                return denied;
            return ToResult(await service.GetReceivingReportByIdAsync(id, ct));
        });

        app.MapPost("/api/lpi/receiving-reports", async (
            CreateReceivingReportApiRequest request,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(context, sessions, LocalApiPermissions.ReceivingManage) is IResult denied)
                return denied;
            LocalApiSession session = RequiredSession(context, sessions);
            if (string.IsNullOrWhiteSpace(request.ResolvedDeliveryReceiptNo))
                return Results.BadRequest(new { error = "Delivery receipt number is mandatory." });
            return ToResult(
                await service.CreateReceivingReportAsync(new CreateReceivingReportRequest(
                    request.PurchaseOrderId,
                    request.LocationId,
                    request.ResolvedDeliveryReceiptNo,
                    request.SupplierInvoiceRef,
                    request.ResolvedCarrierOrDriver,
                    request.ReceivedDate,
                    request.PostImmediately ?? true,
                    null,
                    null,
                    request.ClientRequestId,
                    request.Notes,
                    request.ResolvedLines,
                    session.UserId), ct),
                StatusCodes.Status201Created);
        });

        app.MapPost("/api/lpi/receiving-reports/{id:guid}/approve-overdelivery", async (
            Guid id,
            ApproveOverdeliveryApiRequest request,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(context, sessions, LocalApiPermissions.ReceivingApproveException) is IResult denied)
                return denied;
            LocalApiSession session = RequiredSession(context, sessions);
            return ToResult(await service.ApproveOverdeliveryAsync(new ApproveOverdeliveryRequest(
                id,
                request.ApprovalReason,
                session.UserId), ct));
        });

        app.MapPost("/api/lpi/receiving-reports/{id:guid}/post", async (
            Guid id,
            PostReceivingReportApiRequest request,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(context, sessions, LocalApiPermissions.ReceivingManage) is IResult denied)
                return denied;
            LocalApiSession session = RequiredSession(context, sessions);
            return ToResult(await service.PostReceivingReportAsync(new PostReceivingReportRequest(
                id,
                null,
                request.OverdeliveryApprovalReason,
                request.ClientRequestId,
                session.UserId), ct));
        });

        app.MapGet("/api/lpi/stock-cards", async (
            string? search,
            Guid? locationId,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequireAnyPermission(context, sessions, AnyLpiViewPermissions) is IResult denied)
                return denied;
            return ToResult(await service.GetProductStockCardsAsync(search, locationId, ct));
        });

        app.MapGet("/api/lpi/inventory/stock-cards", async (
            string? search,
            Guid? locationId,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequireAnyPermission(context, sessions, AnyLpiViewPermissions) is IResult denied)
                return denied;
            return ToResult(await service.GetProductStockCardsAsync(search, locationId, ct));
        });

        app.MapGet("/api/lpi/stock-cards/{productId:guid}", async (
            Guid productId,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequireAnyPermission(context, sessions, AnyLpiViewPermissions) is IResult denied)
                return denied;
            return ToResult(await service.GetProductStockCardAsync(productId, ct));
        });

        app.MapGet("/api/lpi/inventory/stock-card/{productId:guid}", async (
            Guid productId,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequireAnyPermission(context, sessions, AnyLpiViewPermissions) is IResult denied)
                return denied;
            return ToResult(await service.GetProductStockCardAsync(productId, ct));
        });

        app.MapGet("/api/lpi/inventory/buckets", async (
            Guid? productId,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequireAnyPermission(context, sessions, AnyLpiViewPermissions) is IResult denied)
                return denied;
            return ToResult(await service.GetProductStockBucketsAsync(productId, ct));
        });

        app.MapGet("/api/lpi/inventory/adjustments", async (
            Guid? productId,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequireAnyPermission(context, sessions, AnyLpiViewPermissions) is IResult denied)
                return denied;
            return ToResult(await service.GetStockCountAdjustmentsAsync(productId, ct));
        });

        app.MapPost("/api/lpi/inventory/adjustments", async (
            RecordStockAdjustmentApiRequest request,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(context, sessions, LocalApiPermissions.InventoryAdjust) is IResult denied)
                return denied;
            LocalApiSession session = RequiredSession(context, sessions);
            return ToResult(
                await service.RecordStockCountAdjustmentAsync(new RecordStockCountAdjustmentRequest(
                    request.ProductId,
                    request.LocationId,
                    request.ResolvedActualQty,
                    request.ReasonCode ?? "CycleCount",
                    request.Notes,
                    session.UserId,
                    session.UserId), ct),
                StatusCodes.Status201Created);
        });

        app.MapPost("/api/lpi/inventory-adjustments", async (
            RecordStockAdjustmentApiRequest request,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(context, sessions, LocalApiPermissions.InventoryAdjust) is IResult denied)
                return denied;
            LocalApiSession session = RequiredSession(context, sessions);
            return ToResult(
                await service.RecordStockCountAdjustmentAsync(new RecordStockCountAdjustmentRequest(
                    request.ProductId,
                    request.LocationId,
                    request.ResolvedActualQty,
                    request.ReasonCode ?? "CycleCount",
                    request.Notes,
                    session.UserId,
                    session.UserId), ct),
                StatusCodes.Status201Created);
        });

        app.MapGet("/api/lpi/supplier-returns", async (
            Guid? supplierId,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequireAnyPermission(context, sessions, AnyLpiViewPermissions) is IResult denied)
                return denied;
            return ToResult(await service.GetSupplierReturnsAsync(supplierId, ct));
        });

        app.MapPost("/api/lpi/supplier-returns", async (
            CreateSupplierReturnApiRequest request,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(context, sessions, LocalApiPermissions.InventoryManageReturns) is IResult denied)
                return denied;
            LocalApiSession session = RequiredSession(context, sessions);
            return ToResult(
                await service.CreateSupplierReturnAsync(new CreateSupplierReturnRequest(
                    request.ReceivingReportId,
                    request.SupplierInvoiceId,
                    request.Reason,
                    request.Lines,
                    session.UserId,
                    session.UserId), ct),
                StatusCodes.Status201Created);
        });

        // S3: Supplier Accounts Payable, 3-Way Match & Payments
        app.MapGet("/api/lpi/invoices", async (
            Guid? supplierId,
            string? status,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(context, sessions, LocalApiPermissions.PayablesView) is IResult denied)
                return denied;
            return ToResult(await service.GetSupplierInvoicesAsync(supplierId, status, ct));
        });

        app.MapGet("/api/lpi/invoices/{id:guid}", async (
            Guid id,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(context, sessions, LocalApiPermissions.PayablesView) is IResult denied)
                return denied;
            return ToResult(await service.GetSupplierInvoiceByIdAsync(id, ct));
        });

        app.MapPost("/api/lpi/invoices", async (
            CreateSupplierInvoiceApiRequest request,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(context, sessions, LocalApiPermissions.PayablesManageInvoices) is IResult denied)
                return denied;
            LocalApiSession session = RequiredSession(context, sessions);
            if (string.IsNullOrWhiteSpace(request.ResolvedInvoiceNo))
                return Results.BadRequest(new { error = "Supplier invoice number is mandatory." });
            DateOnly invDate = request.InvoiceDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
            return ToResult(
                await service.CreateSupplierInvoiceAsync(new CreateSupplierInvoiceRequest(
                    request.SupplierId,
                    request.ResolvedInvoiceNo,
                    request.PurchaseOrderId,
                    request.ReceivingReportId,
                    invDate,
                    request.DueDate,
                    request.DiscountCents ?? 0L,
                    request.WithholdingTaxCents ?? 0L,
                    request.PostToApIfMatched ?? true,
                    request.ResolvedLines,
                    session.UserId), ct),
                StatusCodes.Status201Created);
        });

        app.MapPost("/api/lpi/invoices/{id:guid}/approve-variance", async (
            Guid id,
            ApproveVarianceApiRequest request,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(context, sessions, LocalApiPermissions.PayablesApproveVariance) is IResult denied)
                return denied;
            LocalApiSession session = RequiredSession(context, sessions);
            if (string.IsNullOrWhiteSpace(request.ResolvedApprovalReason))
                return Results.BadRequest(new { error = "Variance approval reason is mandatory." });
            return ToResult(await service.ApproveInvoiceVarianceAsync(new ApproveInvoiceVarianceRequest(
                id,
                request.ResolvedApprovalReason,
                request.PostToApImmediately ?? true,
                session.UserId), ct));
        });

        app.MapPost("/api/lpi/invoices/{id:guid}/post", async (
            Guid id,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(context, sessions, LocalApiPermissions.PayablesManageInvoices) is IResult denied)
                return denied;
            LocalApiSession session = RequiredSession(context, sessions);
            return ToResult(await service.PostSupplierInvoiceToApAsync(id, session.UserId, ct));
        });

        app.MapGet("/api/lpi/payments", async (
            Guid? supplierId,
            Guid? supplierInvoiceId,
            Guid? invoiceId,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(context, sessions, LocalApiPermissions.PayablesView) is IResult denied)
                return denied;
            Guid? targetInvoiceId = supplierInvoiceId ?? invoiceId;
            return ToResult(await service.GetSupplierPaymentsAsync(supplierId, targetInvoiceId, ct));
        });

        app.MapPost("/api/lpi/payments", async (
            RecordSupplierPaymentApiRequest request,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(context, sessions, LocalApiPermissions.PayablesRecordPayment) is IResult denied)
                return denied;
            LocalApiSession session = RequiredSession(context, sessions);
            if (request.ResolvedInvoiceId == Guid.Empty)
                return Results.BadRequest(new { error = "Supplier invoice ID is mandatory." });
            if (string.IsNullOrWhiteSpace(request.ResolvedReferenceNo))
                return Results.BadRequest(new { error = "Payment reference number is mandatory." });

            string disbursementSource = !string.IsNullOrWhiteSpace(request.DisbursementSource)
                ? request.DisbursementSource.Trim()
                : "OperatingAccount";

            if (disbursementSource.Contains("Drawer", StringComparison.OrdinalIgnoreCase) ||
                disbursementSource.Contains("Cashier", StringComparison.OrdinalIgnoreCase) ||
                disbursementSource.Contains("ShiftDrawer", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { error = "Cashier shift drawer cannot be used as a procurement disbursement source." });
            }

            return ToResult(
                await service.RecordSupplierPaymentAsync(new RecordSupplierPaymentRequest(
                    request.ResolvedInvoiceId,
                    request.PaymentDate,
                    request.AmountCents,
                    request.PaymentMethod ?? "BankTransfer",
                    disbursementSource,
                    request.ResolvedReferenceNo,
                    request.ResolvedCheckNo,
                    request.ResolvedBankName,
                    request.ResolvedRemarks,
                    request.ClientRequestId,
                    session.UserId,
                    null), ct),
                StatusCodes.Status201Created);
        });

        app.MapPost("/api/lpi/payments/{id:guid}/approve", async (
            Guid id,
            ApproveSupplierPaymentApiRequest request,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(context, sessions, LocalApiPermissions.PayablesApprovePayment) is IResult denied)
                return denied;
            LocalApiSession session = RequiredSession(context, sessions);
            return ToResult(await service.ApproveSupplierPaymentAsync(new ApproveSupplierPaymentRequest(
                id,
                session.UserId,
                request.Remarks), ct));
        });

        app.MapGet("/api/lpi/payables/ledger", async (
            Guid? supplierId,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(context, sessions, LocalApiPermissions.PayablesView) is IResult denied)
                return denied;
            return ToResult(await service.GetSupplierPayableLedgerAsync(supplierId, ct));
        });

        app.MapGet("/api/lpi/payables/aging", async (
            DateOnly? asOfDate,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(context, sessions, LocalApiPermissions.PayablesView) is IResult denied)
                return denied;
            return ToResult(await service.GetApAgingSummaryAsync(asOfDate, ct));
        });

        app.MapGet("/api/lpi/ap-aging", async (
            DateOnly? asOfDate,
            HttpContext context,
            LocalApiSessionService sessions,
            IProcureToPayService service,
            CancellationToken ct) =>
        {
            if (LocalApiPermissionGuards.RequirePermission(context, sessions, LocalApiPermissions.PayablesView) is IResult denied)
                return denied;
            return ToResult(await service.GetApAgingSummaryAsync(asOfDate, ct));
        });

        return app;
    }

    private static LocalApiSession RequiredSession(HttpContext context, LocalApiSessionService sessions) =>
        sessions.GetRequiredSession(context.Request.Cookies[LocalApiSessionService.SessionCookieName])!;

    private static IResult ToResult<T>(Result<T> result, int successStatus = StatusCodes.Status200OK) =>
        result.IsSuccess ? Results.Json(result.Value, statusCode: successStatus) : ToError(result.ErrorDetail);

    private static IResult ToError(Error error)
    {
        int status = error.Type switch
        {
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Json(new { error = error.Message, code = error.Code }, statusCode: status);
    }

    internal sealed record SupplierWriteApiRequest(
        string? Code,
        string Name,
        string? ContactPerson,
        string? ContactNumber,
        string? Email,
        string? Address,
        string? Tin,
        int? PaymentTermsDays,
        int? LeadTimeDays,
        string? Notes);

    internal sealed record SupplierUpdateApiRequest(
        string? Code,
        string Name,
        string? ContactPerson,
        string? ContactNumber,
        string? Email,
        string? Address,
        string? Tin,
        int? PaymentTermsDays,
        int? LeadTimeDays,
        string? Notes,
        bool? IsActive);

    internal sealed record NotesOnlyApiRequest(string? Notes);
    internal sealed record ReasonOnlyApiRequest(string? Reason, string? Notes);
    internal sealed record RevisionOnlyApiRequest(string? RevisionNotes, string? Reason);
    internal sealed record SelectWinnerApiRequest(string? Justification, string? Reason);
    internal sealed record ConvertToPoApiRequest(
        Guid SupplierId,
        DateOnly? ExpectedDeliveryDate,
        Guid? DeliveryLocationId,
        IReadOnlyList<Guid>? RequisitionLineIds);

    internal sealed record CreateRequisitionItemInput(
        Guid? ProductId,
        string? ItemCode,
        string? ItemDescription,
        string? CategoryName,
        string? PurchaseClassification,
        string? UnitName,
        string? UomId,
        long? RequestedQtyX1000,
        long? RequestedQuantityX1000,
        long? EstimatedUnitCostCents,
        long? EstimatedCostCents,
        Guid? PreferredSupplierId,
        string? Specifications,
        string? Notes)
    {
        public CreatePurchaseRequisitionLineInput ToLineInput() => new(
            ProductId: ProductId,
            ItemCode: ItemCode,
            ItemDescription: ItemDescription ?? "Requested Item",
            CategoryName: CategoryName,
            PurchaseClassification: PurchaseClassification ?? "Merchandise",
            UnitName: UnitName ?? "pc",
            RequestedQtyX1000: RequestedQtyX1000 ?? RequestedQuantityX1000 ?? 1000L,
            EstimatedUnitCostCents: EstimatedUnitCostCents ?? EstimatedCostCents ?? 0L,
            PreferredSupplierId: PreferredSupplierId,
            Specifications: Specifications,
            Notes: Notes);
    }

    internal sealed record CreateRequisitionApiRequest(
        string? DepartmentName,
        string? Department,
        DateOnly? DateNeeded,
        DateOnly? RequiredDate,
        string? Priority,
        string? Purpose,
        string? Notes,
        bool? SubmitImmediately,
        IReadOnlyList<CreatePurchaseRequisitionLineInput>? Lines,
        IReadOnlyList<CreateRequisitionItemInput>? Items)
    {
        public string ResolvedDepartment => DepartmentName ?? Department ?? "General";
        public DateOnly? ResolvedDateNeeded => DateNeeded ?? RequiredDate;
        public IReadOnlyList<CreatePurchaseRequisitionLineInput> ResolvedLines =>
            Lines ?? Items?.Select(i => i.ToLineInput()).ToList() ?? (IReadOnlyList<CreatePurchaseRequisitionLineInput>)Array.Empty<CreatePurchaseRequisitionLineInput>();
    }

    internal sealed record ReviewRequisitionApiRequest(
        string Decision,
        string? Reason,
        IReadOnlyList<ReviewPurchaseRequisitionLineDecision>? LineDecisions);

    internal sealed record RecordQuotationApiRequest(
        Guid? RequisitionId,
        Guid? RequisitionLineId,
        Guid SupplierId,
        Guid? ProductId,
        string ItemDescription,
        long QuotedUnitCostCents,
        long? MinimumOrderQtyX1000,
        int? LeadTimeDays,
        int? PaymentTermsDays,
        DateOnly? ValidityDate,
        bool? IsSelected,
        string? SelectionReason);

    internal sealed record CreatePurchaseOrderLineApiInput(
        Guid? RequisitionLineId,
        Guid? ProductId,
        string? ItemCode,
        string? ItemDescription,
        string? ProductName,
        string? PurchaseClassification,
        string? UnitName,
        string? UomName,
        long? OrderedQtyX1000,
        long? OrderedQuantityX1000,
        long? UnitCostCents,
        long? DiscountCents,
        int? TaxRateBp,
        string? Notes)
    {
        public CreatePurchaseOrderLineInput ToCoreInput() => new(
            RequisitionLineId,
            ProductId,
            ItemCode,
            !string.IsNullOrWhiteSpace(ItemDescription) ? ItemDescription : (!string.IsNullOrWhiteSpace(ProductName) ? ProductName : "Purchased Item"),
            !string.IsNullOrWhiteSpace(PurchaseClassification) ? PurchaseClassification : "DirectMaterials",
            !string.IsNullOrWhiteSpace(UnitName) ? UnitName : (!string.IsNullOrWhiteSpace(UomName) ? UomName : "Unit"),
            OrderedQtyX1000 ?? OrderedQuantityX1000 ?? 0L,
            UnitCostCents ?? 0L,
            DiscountCents ?? 0L,
            TaxRateBp ?? 0,
            Notes);
    }

    internal sealed record CreatePurchaseOrderApiRequest(
        Guid SupplierId,
        Guid? SourceRequisitionId,
        Guid? RequisitionId,
        DateOnly? ExpectedDeliveryDate,
        Guid? DeliveryLocationId,
        int? PaymentTermsDays,
        long? ShippingCents,
        string? TermsAndConditions,
        string? Notes,
        bool? SubmitAndApproveImmediately,
        IReadOnlyList<CreatePurchaseOrderLineApiInput>? Lines,
        IReadOnlyList<CreatePurchaseOrderLineApiInput>? Items)
    {
        public Guid? ResolvedRequisitionId => SourceRequisitionId ?? RequisitionId;
        public IReadOnlyList<CreatePurchaseOrderLineInput> ResolvedLines =>
            (Lines ?? Items ?? Array.Empty<CreatePurchaseOrderLineApiInput>())
                .Select(l => l.ToCoreInput())
                .ToList();
    }

    internal sealed record PurchaseOrderStatusApiRequest(
        string Action,
        string? Reason);

    internal sealed record CreateReceivingReportLineApiInput(
        Guid? PurchaseOrderLineId,
        Guid? PoItemId,
        long? ActualDeliveredQtyX1000,
        long? DeliveredQuantityX1000,
        long? AcceptedQtyX1000,
        long? AcceptedQuantityX1000,
        long? RejectedQtyX1000,
        long? RejectedQuantityX1000,
        string? RejectionReason,
        string? RejectionDisposition,
        string? LotOrBatchNo,
        DateOnly? ExpiryDate)
    {
        public CreateReceivingReportLineInput ToCoreInput() => new(
            PurchaseOrderLineId ?? PoItemId ?? Guid.Empty,
            ActualDeliveredQtyX1000 ?? DeliveredQuantityX1000 ?? 0L,
            AcceptedQtyX1000 ?? AcceptedQuantityX1000 ?? 0L,
            RejectedQtyX1000 ?? RejectedQuantityX1000 ?? 0L,
            RejectionReason,
            RejectionDisposition,
            LotOrBatchNo,
            ExpiryDate);
    }

    internal sealed record CreateReceivingReportApiRequest(
        Guid PurchaseOrderId,
        Guid? LocationId,
        string? DeliveryReceiptNo,
        string? DeliveryReceiptNumber,
        string? SupplierInvoiceRef,
        string? CarrierOrDriver,
        string? Carrier,
        string? DriverName,
        DateOnly? ReceivedDate,
        bool? PostImmediately,
        bool? ApproveOverdelivery,
        string? OverdeliveryApprovalReason,
        string? ClientRequestId,
        string? Notes,
        IReadOnlyList<CreateReceivingReportLineApiInput>? Lines,
        IReadOnlyList<CreateReceivingReportLineApiInput>? Items)
    {
        public string? ResolvedDeliveryReceiptNo =>
            !string.IsNullOrWhiteSpace(DeliveryReceiptNo) ? DeliveryReceiptNo.Trim() :
            !string.IsNullOrWhiteSpace(DeliveryReceiptNumber) ? DeliveryReceiptNumber.Trim() :
            null;

        public string? ResolvedCarrierOrDriver =>
            !string.IsNullOrWhiteSpace(CarrierOrDriver) ? CarrierOrDriver :
            !string.IsNullOrWhiteSpace(Carrier) && !string.IsNullOrWhiteSpace(DriverName) ? $"{Carrier} - {DriverName}" :
            !string.IsNullOrWhiteSpace(Carrier) ? Carrier :
            DriverName;

        public IReadOnlyList<CreateReceivingReportLineInput> ResolvedLines =>
            (Lines ?? Items ?? Array.Empty<CreateReceivingReportLineApiInput>())
                .Select(l => l.ToCoreInput())
                .ToList();
    }

    internal sealed record PostReceivingReportApiRequest(
        bool? ApproveOverdelivery,
        string? OverdeliveryApprovalReason,
        string? ClientRequestId);

    internal sealed record RecordStockAdjustmentApiRequest(
        Guid ProductId,
        Guid? LocationId,
        long? ActualCountedQtyX1000,
        long? ActualQuantityX1000,
        string? ReasonCode,
        string? Notes)
    {
        public long ResolvedActualQty => ActualCountedQtyX1000 ?? ActualQuantityX1000 ?? 0L;
    }

    internal sealed record CreateSupplierReturnApiRequest(
        Guid ReceivingReportId,
        Guid? SupplierInvoiceId,
        string Reason,
        IReadOnlyList<CreateSupplierReturnLineInput> Lines);

    internal sealed record CreateSupplierInvoiceLineApiInput(
        Guid? PurchaseOrderLineId,
        Guid? PoItemId,
        Guid? ReceivingReportLineId,
        Guid? RrItemId,
        Guid? ProductId,
        string? ItemDescription,
        string? ProductName,
        long? InvoicedQtyX1000,
        long? InvoicedQuantityX1000,
        long? InvoicedUnitCostCents,
        long? UnitCostCents,
        long? TaxCents)
    {
        public CreateSupplierInvoiceLineInput ToCoreInput() => new(
            PurchaseOrderLineId ?? PoItemId,
            ReceivingReportLineId ?? RrItemId,
            ProductId,
            ItemDescription ?? ProductName ?? "Invoice Line",
            InvoicedQtyX1000 ?? InvoicedQuantityX1000 ?? 0L,
            InvoicedUnitCostCents ?? UnitCostCents ?? 0L,
            TaxCents ?? 0L);
    }

    internal sealed record CreateSupplierInvoiceApiRequest(
        Guid SupplierId,
        string? SupplierInvoiceNo,
        string? InvoiceNumber,
        Guid? PurchaseOrderId,
        Guid? ReceivingReportId,
        DateOnly? InvoiceDate,
        DateOnly? DueDate,
        long? DiscountCents,
        long? WithholdingTaxCents,
        bool? PostToApIfMatched,
        IReadOnlyList<CreateSupplierInvoiceLineApiInput>? Lines,
        IReadOnlyList<CreateSupplierInvoiceLineApiInput>? Items)
    {
        public string? ResolvedInvoiceNo =>
            !string.IsNullOrWhiteSpace(SupplierInvoiceNo) ? SupplierInvoiceNo.Trim() :
            !string.IsNullOrWhiteSpace(InvoiceNumber) ? InvoiceNumber.Trim() :
            null;
        public IReadOnlyList<CreateSupplierInvoiceLineInput> ResolvedLines =>
            (Lines ?? Items ?? Array.Empty<CreateSupplierInvoiceLineApiInput>())
                .Select(l => l.ToCoreInput())
                .ToList();
    }

    internal sealed record ApproveVarianceApiRequest(
        string? ApprovalReason,
        string? Notes,
        string? Reason,
        bool? PostToApImmediately)
    {
        public string? ResolvedApprovalReason =>
            !string.IsNullOrWhiteSpace(ApprovalReason) ? ApprovalReason.Trim() :
            !string.IsNullOrWhiteSpace(Notes) ? Notes.Trim() :
            !string.IsNullOrWhiteSpace(Reason) ? Reason.Trim() :
            null;
    }

    internal sealed record RecordSupplierPaymentApiRequest(
        Guid? SupplierInvoiceId,
        Guid? InvoiceId,
        DateOnly? PaymentDate,
        long AmountCents,
        string? PaymentMethod,
        string? DisbursementSource,
        string? ReferenceNo,
        string? ReferenceNumber,
        string? CheckNo,
        string? CheckNumber,
        string? BankName,
        string? BankAccount,
        string? Remarks,
        string? Notes,
        string? ClientRequestId)
    {
        public Guid ResolvedInvoiceId => SupplierInvoiceId ?? InvoiceId ?? Guid.Empty;
        public string? ResolvedReferenceNo =>
            !string.IsNullOrWhiteSpace(ReferenceNo) ? ReferenceNo.Trim() :
            !string.IsNullOrWhiteSpace(ReferenceNumber) ? ReferenceNumber.Trim() :
            null;
        public string? ResolvedCheckNo => CheckNo ?? CheckNumber;
        public string? ResolvedBankName => BankName ?? BankAccount;
        public string? ResolvedRemarks => Remarks ?? Notes;
    }

    internal sealed record ApproveSupplierPaymentApiRequest(
        string? Remarks);

    internal sealed record ApproveOverdeliveryApiRequest(
        string ApprovalReason);
}
