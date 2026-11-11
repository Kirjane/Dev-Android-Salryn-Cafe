namespace Salryn.Core.Models;

public sealed record CustomerQuotation(
    Guid Id,
    string QuoteNumber,
    string CustomerName,
    string? CustomerContact,
    string? Notes,
    long SubtotalCents,
    long DiscountCents,
    long TotalCents,
    string Status,
    Guid CreatedByUserId,
    string CreatedByUserName,
    DateTimeOffset CreatedAt,
    DateOnly? ValidUntil,
    IReadOnlyList<CustomerQuotationLine> Lines);

public sealed record CustomerQuotationLine(
    Guid Id,
    Guid QuotationId,
    Guid ProductId,
    string ProductNameSnapshot,
    string? BarcodeSnapshot,
    string UnitNameSnapshot,
    long QuantityX1000,
    long UnitPriceCents,
    long DiscountCents,
    long LineTotalCents);

public sealed record CustomerOrder(
    Guid Id,
    string OrderNumber,
    Guid? CustomerId,
    string CustomerName,
    string? CustomerPhone,
    DateOnly? PromisedDate,
    string? Notes,
    long TotalCents,
    string Status,
    Guid? SaleId,
    Guid CreatedByUserId,
    string CreatedByUserName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? FulfilledAt,
    IReadOnlyList<CustomerOrderLine> Lines);

public sealed record CustomerOrderLine(
    Guid Id,
    Guid OrderId,
    Guid ProductId,
    string ProductNameSnapshot,
    string? BarcodeSnapshot,
    string UnitNameSnapshot,
    long QuantityX1000,
    long UnitPriceCents,
    long LineTotalCents);

public sealed record CreateCustomerQuotationLineRequest(
    Guid ProductId,
    string ProductNameSnapshot,
    string? BarcodeSnapshot,
    string UnitNameSnapshot,
    long QuantityX1000,
    long UnitPriceCents,
    long DiscountCents);

public sealed record CreateCustomerQuotationRequest(
    string CustomerName,
    string? CustomerContact,
    string? Notes,
    long DiscountCents,
    DateOnly? ValidUntil,
    IReadOnlyList<CreateCustomerQuotationLineRequest> Lines);

public sealed record CreateCustomerOrderLineRequest(
    Guid ProductId,
    string ProductNameSnapshot,
    string? BarcodeSnapshot,
    string UnitNameSnapshot,
    long QuantityX1000,
    long UnitPriceCents);

public sealed record CreateCustomerOrderRequest(
    Guid? CustomerId,
    string CustomerName,
    string? CustomerPhone,
    DateOnly? PromisedDate,
    string? Notes,
    IReadOnlyList<CreateCustomerOrderLineRequest> Lines);
