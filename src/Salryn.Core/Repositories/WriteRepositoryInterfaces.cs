using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ValueObjects;

namespace Salryn.Core.Repositories;

public interface IProductWriteRepository
{
    Task<Result> AddAsync(
        Product product,
        CancellationToken cancellationToken = default);

    Task<Result> UpdateAsync(
        Product product,
        CancellationToken cancellationToken = default);

    Task<Result> DeactivateAsync(
        Guid productId,
        string reason,
        Guid userId,
        CancellationToken cancellationToken = default);
}

public interface ISaleWriteRepository
{
    Task<Result> AddCompletedSaleAsync(
        Sale sale,
        IReadOnlyList<SaleItem> items,
        IReadOnlyList<Payment> payments,
        IReadOnlyList<StockMovement> stockMovements,
        CancellationToken cancellationToken = default);

    Task<Result> MarkVoidedAsync(
        Guid saleId,
        Guid voidedByUserId,
        string reason,
        IReadOnlyList<StockMovement> reversalMovements,
        CancellationToken cancellationToken = default);
}

public interface IInventoryWriteRepository
{
    Task<Result> AddStockMovementAsync(
        StockMovement movement,
        CancellationToken cancellationToken = default);

    Task<Result> AddStockMovementsAsync(
        IReadOnlyList<StockMovement> movements,
        CancellationToken cancellationToken = default);
}

public sealed record UserAdminCreateRequest(
    string Username,
    string DisplayName,
    Guid RoleId,
    string PasswordHash,
    Guid CreatedByUserId,
    UserStatus Status = UserStatus.Active);

public sealed record UserAdminUpdateRequest(
    Guid UserId,
    string Username,
    string DisplayName,
    Guid RoleId,
    Guid UpdatedByUserId);

public sealed record UserAdminPasswordResetRequest(
    Guid UserId,
    string PasswordHash,
    Guid ResetByUserId);

public sealed record UserAdminStatusChangeRequest(
    Guid UserId,
    UserStatus Status,
    string? Reason,
    Guid ChangedByUserId);

public interface IUserWriteRepository
{
    Task<Result> CreateAsync(
        UserAdminCreateRequest request,
        CancellationToken cancellationToken = default);

    Task<Result> UpdateAsync(
        UserAdminUpdateRequest request,
        CancellationToken cancellationToken = default);

    Task<Result> ResetPasswordAsync(
        UserAdminPasswordResetRequest request,
        CancellationToken cancellationToken = default);

    Task<Result> SetStatusAsync(
        UserAdminStatusChangeRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record StoreSettingsUpdateRequest(
    Guid StoreSettingsId,
    string StoreName,
    string? BusinessName,
    string? Address,
    string? ContactNumber,
    string? Tin,
    string CurrencyLabel,
    int ReceiptWidthMm,
    bool TaxModeEnabled,
    bool ShiftModeEnabled,
    string? ReceiptHeader = null,
    string? ReceiptFooter = null,
    string PrinterMode = "Disabled");

public interface IStoreSettingsWriteRepository
{
    Task<Result> UpdateAsync(
        StoreSettingsUpdateRequest request,
        CancellationToken cancellationToken = default);
}

public interface ICashierSessionWriteRepository
{
    Task<Result> OpenAsync(
        CashierSession session,
        CancellationToken cancellationToken = default);

    Task<Result> CloseAsync(
        Guid sessionId,
        Guid userId,
        Money countedCash,
        string? notes,
        CancellationToken cancellationToken = default);

    Task<Result> ForceCloseAsync(
        Guid sessionId,
        Guid userId,
        Money? countedCash,
        string reason,
        CancellationToken cancellationToken = default);
}
public interface IAuditWriteRepository
{
    Task<Result> AddAsync(
        AuditLog auditLog,
        CancellationToken cancellationToken = default);
}

public interface IErrorLogWriteRepository
{
    Task<Result> AddAsync(
        ErrorLog errorLog,
        CancellationToken cancellationToken = default);
}
