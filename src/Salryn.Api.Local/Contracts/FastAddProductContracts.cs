namespace Salryn.Api.Local.Contracts;

public sealed record FastAddProductRequest(
    string ProductName,
    string Barcode,
    Guid UnitId,
    decimal SellingPrice,
    decimal InitialStockQuantity);

public sealed record FastAddProductResponse(
    string Code,
    string Message,
    ProductListItemResponse Product,
    bool InitialStockMovementCreated);
