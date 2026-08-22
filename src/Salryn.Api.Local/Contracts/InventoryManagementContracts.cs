namespace Salryn.Api.Local.Contracts;

public sealed record StockInRequest(
    Guid ProductId,
    decimal Quantity,
    string? Reason,
    string? Reference);

public sealed record StockAdjustmentRequest(
    Guid ProductId,
    string Direction,
    decimal Quantity,
    string? Reason,
    string? Reference);

public sealed record StockWriteResponse(
    string Code,
    string Message,
    ProductListItemResponse Product,
    string MovementType,
    decimal QuantityChange,
    string ReferenceType,
    string? Reference);
