using Salryn.Api.Local.Contracts;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.ValueObjects;

namespace Salryn.Api.Local.Services;

internal static class ReadModelMapper
{
    public static PagedApiResponse<TResponse> ToPaged<TModel, TResponse>(
        PagedResult<TModel> page,
        Func<TModel, TResponse> map)
    {
        return new PagedApiResponse<TResponse>(
            page.Items.Select(map).ToList(),
            page.TotalCount,
            page.PageNumber,
            page.PageSize,
            page.TotalPages);
    }

    public static ProductListItemResponse ToProductListItem(ProductListItemReadModel model) =>
        new(
            model.Id,
            model.Sku,
            model.Barcode,
            model.Name,
            model.UnitName,
            model.CategoryName,
            model.BrandName,
            model.SellingPrice.ToPesos(),
            model.CostPrice?.ToPesos(),
            model.StockOnHand.ToUnits(),
            model.ReorderLevel.ToUnits(),
            model.TrackStock,
            model.IsActive);

    public static ProductDetailResponse ToProductDetail(ProductDetailReadModel model) =>
        new(
            model.Id,
            model.Sku,
            model.Barcode,
            model.Name,
            model.Description,
            model.CategoryName,
            model.BrandName,
            model.UnitName,
            model.SupplierName,
            model.CostPrice?.ToPesos(),
            model.SellingPrice.ToPesos(),
            model.WholesalePrice?.ToPesos(),
            model.StockOnHand.ToUnits(),
            model.ReorderLevel.ToUnits(),
            model.TrackStock,
            model.AllowDiscount,
            model.IsActive,
            model.Notes,
            model.CreatedAt,
            model.UpdatedAt);

    public static CatalogOptionResponse ToCatalogOption(CategoryReadModel model) =>
        new(model.Id, model.Name, model.IsActive);

    public static CatalogOptionResponse ToCatalogOption(BrandReadModel model) =>
        new(model.Id, model.Name, model.IsActive);

    public static LowStockProductResponse ToLowStock(LowStockProductReadModel model) =>
        new(
            model.ProductId,
            model.Barcode,
            model.ProductName,
            model.UnitName,
            model.StockOnHand.ToUnits(),
            model.ReorderLevel.ToUnits());

    public static StockMovementResponse ToStockMovement(StockMovementReadModel model) =>
        new(
            model.Id,
            model.ProductId,
            model.ProductName,
            model.MovementType.ToString(),
            model.QuantityChangeUnits,
            model.ReferenceType,
            model.ReferenceId,
            model.Reason,
            model.UserDisplayName,
            model.CreatedAt);

    public static SaleHistoryItemResponse ToSaleHistoryItem(SaleHistoryItemReadModel model) =>
        new(
            model.Id,
            model.ReceiptNo,
            model.SaleDate,
            model.CashierDisplayName,
            model.Total.ToPesos(),
            model.AmountPaid.ToPesos(),
            model.Change.ToPesos(),
            model.Status.ToString(),
            model.VoidReason);

    public static SaleDetailResponse ToSaleDetail(SaleDetailReadModel model) =>
        new(
            model.Id,
            model.ReceiptNo,
            model.SaleDate,
            model.CashierDisplayName,
            model.Subtotal.ToPesos(),
            model.Discount.ToPesos(),
            model.Tax.ToPesos(),
            model.Total.ToPesos(),
            model.AmountPaid.ToPesos(),
            model.Change.ToPesos(),
            model.Status.ToString(),
            model.VoidReason,
            model.VoidedAt,
            model.Items.Select(item => new SaleItemDetailResponse(
                item.Id,
                item.ProductNameSnapshot,
                item.BarcodeSnapshot,
                item.UnitNameSnapshot,
                item.Quantity.ToUnits(),
                item.UnitPrice.ToPesos(),
                item.Discount.ToPesos(),
                item.LineTotal.ToPesos())).ToList(),
            model.Payments.Select(payment => new SalePaymentResponse(
                payment.Id,
                payment.Method.ToString(),
                payment.Amount.ToPesos(),
                payment.PaidAt)).ToList());

    public static UserListItemResponse ToUserListItem(UserReadModel model) =>
        new(
            model.Id,
            model.Username,
            model.DisplayName,
            model.RoleName,
            model.Status.ToString(),
            model.CreatedAt);

    public static UserDetailResponse ToUserDetail(UserPermissionReadModel model) =>
        new(
            model.UserId,
            model.Username,
            model.DisplayName,
            model.RoleName,
            model.Status.ToString(),
            DateTimeOffset.MinValue,
            model.Permissions);

    public static UserDetailResponse ToUserDetail(UserReadModel model, IReadOnlyList<string> permissions) =>
        new(
            model.Id,
            model.Username,
            model.DisplayName,
            model.RoleName,
            model.Status.ToString(),
            model.CreatedAt,
            permissions);

    public static UserRoleResponse ToUserRole(UserRoleReadModel model) =>
        new(model.RoleId, model.RoleName, model.IsActive);

    public static AuditLogResponse ToAuditLog(AuditLogReadModel model) =>
        new(
            model.Id,
            model.UserDisplayName,
            model.Action,
            model.EntityType,
            model.EntityId,
            model.Reason,
            model.CreatedAt);

    public static ErrorLogResponse ToErrorLog(ErrorLogReadModel model) =>
        new(
            model.Id,
            model.UserDisplayName,
            model.Area,
            model.Message,
            model.CreatedAt);

    public static PageRequest ToPageRequest(int page, int pageSize)
    {
        int safePage = page < 1 ? 1 : page;
        int safeSize = pageSize switch
        {
            < 1 => 25,
            > 200 => 200,
            _ => pageSize
        };

        return new PageRequest(safePage, safeSize);
    }

    public static SaleStatus? ParseSaleStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Enum.TryParse<SaleStatus>(value, ignoreCase: true, out SaleStatus status)
            ? status
            : null;
    }
}
