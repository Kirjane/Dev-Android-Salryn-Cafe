namespace Salryn.Api.Local.Contracts;

public sealed record PagedApiResponse<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int PageNumber,
    int PageSize,
    int TotalPages);

public sealed record ProductListItemResponse(
    Guid Id,
    string? Sku,
    string? Barcode,
    string Name,
    string UnitName,
    string? CategoryName,
    string? BrandName,
    decimal SellingPrice,
    decimal? CostPrice,
    decimal StockOnHand,
    decimal ReorderLevel,
    bool TrackStock,
    bool IsActive);

public sealed record ProductDetailResponse(
    Guid Id,
    string? Sku,
    string? Barcode,
    string Name,
    string? Description,
    string? CategoryName,
    string? BrandName,
    string UnitName,
    string? SupplierName,
    decimal? CostPrice,
    decimal SellingPrice,
    decimal? WholesalePrice,
    decimal StockOnHand,
    decimal ReorderLevel,
    bool TrackStock,
    bool AllowDiscount,
    bool IsActive,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record CatalogOptionResponse(
    Guid Id,
    string Name,
    bool IsActive);

public sealed record LowStockProductResponse(
    Guid ProductId,
    string? Barcode,
    string ProductName,
    string UnitName,
    decimal StockOnHand,
    decimal ReorderLevel);

public sealed record StockMovementResponse(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string MovementType,
    decimal QuantityChange,
    string? ReferenceType,
    string? ReferenceId,
    string? Reason,
    string UserDisplayName,
    DateTimeOffset CreatedAt);

public sealed record SaleHistoryItemResponse(
    Guid Id,
    string ReceiptNo,
    DateTimeOffset SaleDate,
    string CashierDisplayName,
    decimal Total,
    decimal AmountPaid,
    decimal Change,
    string Status,
    string? VoidReason);

public sealed record SaleItemDetailResponse(
    Guid Id,
    string ProductNameSnapshot,
    string? BarcodeSnapshot,
    string UnitNameSnapshot,
    decimal Quantity,
    decimal UnitPrice,
    decimal Discount,
    decimal LineTotal);

public sealed record SalePaymentResponse(
    Guid Id,
    string Method,
    decimal Amount,
    DateTimeOffset PaidAt);

public sealed record SaleDetailResponse(
    Guid Id,
    string ReceiptNo,
    DateTimeOffset SaleDate,
    string CashierDisplayName,
    decimal Subtotal,
    decimal Discount,
    decimal Tax,
    decimal Total,
    decimal AmountPaid,
    decimal Change,
    string Status,
    string? VoidReason,
    DateTimeOffset? VoidedAt,
    IReadOnlyList<SaleItemDetailResponse> Items,
    IReadOnlyList<SalePaymentResponse> Payments);

public sealed record UserListItemResponse(
    Guid Id,
    string Username,
    string DisplayName,
    string RoleName,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record UserDetailResponse(
    Guid Id,
    string Username,
    string DisplayName,
    string RoleName,
    string Status,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> Permissions);

public sealed record UserRoleResponse(
    Guid RoleId,
    string RoleName,
    bool IsActive);

public sealed record AuditLogResponse(
    Guid Id,
    string? UserDisplayName,
    string Action,
    string EntityType,
    string? EntityId,
    string? Reason,
    DateTimeOffset CreatedAt);

public sealed record ErrorLogResponse(
    Guid Id,
    string? UserDisplayName,
    string Area,
    string Message,
    DateTimeOffset CreatedAt);
