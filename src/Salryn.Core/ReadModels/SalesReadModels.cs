using Salryn.Core.Models;
using Salryn.Core.ValueObjects;

namespace Salryn.Core.ReadModels;

public sealed record SaleHistoryItemReadModel(
    Guid Id,
    string ReceiptNo,
    DateTimeOffset SaleDate,
    string CashierDisplayName,
    Money Total,
    Money AmountPaid,
    Money Change,
    SaleStatus Status,
    string? VoidReason);

public sealed record SaleDetailReadModel(
    Guid Id,
    string ReceiptNo,
    DateTimeOffset SaleDate,
    string CashierDisplayName,
    Money Subtotal,
    Money Discount,
    Money Tax,
    Money Total,
    Money AmountPaid,
    Money Change,
    SaleStatus Status,
    string? VoidReason,
    DateTimeOffset? VoidedAt,
    string? ReceiptSnapshot,
    IReadOnlyList<SaleItemDetailReadModel> Items,
    IReadOnlyList<PaymentReadModel> Payments);

public sealed record SaleItemDetailReadModel(
    Guid Id,
    Guid ProductId,
    string ProductNameSnapshot,
    string? BarcodeSnapshot,
    string UnitNameSnapshot,
    Quantity Quantity,
    Money UnitPrice,
    Money? CostPrice,
    Money Discount,
    Money LineTotal);

public sealed record PaymentReadModel(
    Guid Id,
    PaymentMethod Method,
    Money Amount,
    DateTimeOffset PaidAt);

public sealed record SaleHistoryFilter(
    DateTimeOffset? From,
    DateTimeOffset? To,
    Guid? CashierUserId,
    SaleStatus? Status,
    string? ReceiptNo,
    PageRequest Page);
