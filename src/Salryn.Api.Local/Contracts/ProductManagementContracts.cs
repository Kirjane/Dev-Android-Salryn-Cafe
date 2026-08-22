namespace Salryn.Api.Local.Contracts;

public sealed record ProductManagementWriteRequest(
    string ProductName,
    string? Barcode,
    Guid UnitId,
    Guid? CategoryId,
    Guid? BrandId,
    decimal SellingPrice,
    decimal? CostPrice,
    decimal ReorderLevel);

public sealed record ProductDeactivateRequest(
    string Reason);

public sealed record ProductManagementWriteResponse(
    string Code,
    string Message,
    ProductListItemResponse Product);

public sealed record ProductDeactivateResponse(
    string Code,
    string Message,
    Guid ProductId);
