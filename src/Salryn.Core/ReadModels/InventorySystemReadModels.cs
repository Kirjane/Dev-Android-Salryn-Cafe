using Salryn.Core.Models;
using Salryn.Core.ValueObjects;

namespace Salryn.Core.ReadModels;

public sealed record StockMovementReadModel(
    Guid Id,
    Guid ProductId,
    string ProductName,
    StockMovementType MovementType,
    long QuantityChangeX1000,
    decimal QuantityChangeUnits,
    Money? CostPrice,
    string? ReferenceType,
    string? ReferenceId,
    string? Reason,
    string UserDisplayName,
    DateTimeOffset CreatedAt);

public sealed record LowStockProductReadModel(
    Guid ProductId,
    string? Barcode,
    string ProductName,
    string UnitName,
    Quantity StockOnHand,
    Quantity ReorderLevel);

public sealed record StockMovementFilter(
    Guid? ProductId,
    StockMovementType? MovementType,
    DateTimeOffset? From,
    DateTimeOffset? To,
    PageRequest Page,
    string? ReferenceType = null,
    string? ReferenceId = null);

public sealed record UserReadModel(
    Guid Id,
    string Username,
    string DisplayName,
    string RoleName,
    UserStatus Status,
    DateTimeOffset CreatedAt);

public sealed record StoreSettingsReadModel(
    Guid Id,
    string StoreName,
    string? BusinessName,
    string? Address,
    string? ContactNumber,
    string? Tin,
    string CurrencyLabel,
    int ReceiptWidthMm,
    bool TaxModeEnabled,
    bool ShiftModeEnabled,
    bool AllowNegativeStock,
    string? ReceiptHeader = null,
    string? ReceiptFooter = null,
    string PrinterMode = "Disabled",
    string? WindowsPrinterName = null);

public sealed record CashierSessionReadModel(
    Guid Id,
    Guid UserId,
    string UserDisplayName,
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    Money OpeningCash,
    Money ExpectedCash,
    Money? CountedCash,
    Money Variance,
    CashierSessionStatus Status,
    string? Notes);

public sealed record AuditLogReadModel(
    Guid Id,
    Guid? UserId,
    string? UserDisplayName,
    string Action,
    string EntityType,
    string? EntityId,
    string? Reason,
    DateTimeOffset CreatedAt);

public sealed record ErrorLogReadModel(
    Guid Id,
    Guid? UserId,
    string? UserDisplayName,
    string Area,
    string Message,
    string? TechnicalDetail,
    DateTimeOffset CreatedAt);

public sealed record AuditLogSearchFilter(
    PageRequest Page,
    DateTimeOffset? From,
    DateTimeOffset? To,
    SearchText Search);

public sealed record ErrorLogSearchFilter(
    PageRequest Page,
    DateTimeOffset? From,
    DateTimeOffset? To,
    SearchText Search);
