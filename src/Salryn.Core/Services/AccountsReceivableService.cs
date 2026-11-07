using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Core.Services;

public sealed record CreateReceivableCustomerRequest(
    string FullName,
    string? Phone,
    string? Address,
    string? Note,
    long? CreditLimitCents,
    Guid CreatedByUserId);

public sealed record UpdateReceivableCustomerRequest(
    string FullName,
    string? Phone,
    string? Address,
    string? Note,
    long? CreditLimitCents,
    bool IsActive);

public sealed record RecordReceivableCollectionRequest(
    Guid CustomerId,
    long AmountCents,
    string Method,
    string? Note,
    Guid CollectedByUserId,
    string ClientRequestId);

public sealed record RecordReceivableAdjustmentRequest(
    Guid CustomerId,
    long AmountCents,
    bool IsDebit,
    string Note,
    Guid CreatedByUserId);

public sealed record ReceivableCustomerBalance(
    ReceivableCustomer Customer,
    long BalanceCents,
    DateOnly? OldestDueDate,
    DateTimeOffset? LastTransactionAt);

public sealed record AccountsReceivableSummary(
    long TotalReceivablesCents,
    long DueTodayCents,
    long OverdueCents,
    long CollectedTodayCents);

public sealed record ReceivablePage<T>(IReadOnlyList<T> Items,int Offset,int Limit,int Total,bool HasMore,DateTimeOffset AsOfUtc,string TimeZoneId);

public sealed record CollectionPaymentAcknowledgment(
    Guid CollectionId,
    string CustomerName,
    long AmountPaidCents,
    long PreviousBalanceCents,
    long NewBalanceCents,
    string CollectedBy,
    DateTimeOffset CollectedAt,
    string? Note,
    string Method);

public interface IAccountsReceivableService
{
    Task<Result<IReadOnlyList<ReceivableCustomerBalance>>> SearchCustomersAsync(
        string? search,
        CancellationToken cancellationToken = default);
    Task<Result<ReceivablePage<ReceivableCustomerBalance>>> SearchCustomersPageAsync(
        string? search,int offset,int limit,CancellationToken cancellationToken = default);
    Task<Result<ReceivableCustomer>> CreateCustomerAsync(
        CreateReceivableCustomerRequest request,
        CancellationToken cancellationToken = default);
    Task<Result<ReceivableCustomer>> UpdateCustomerAsync(
        Guid customerId,
        UpdateReceivableCustomerRequest request,
        CancellationToken cancellationToken = default);
    Task<Result<ReceivableCustomer?>> FindCustomerAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<AccountsReceivableEntry>>> GetLedgerAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);
    Task<Result<ReceivablePage<AccountsReceivableEntry>>> GetLedgerPageAsync(
        Guid customerId,int offset,int limit,CancellationToken cancellationToken = default);
    Task<Result<AccountsReceivableSummary>> GetSummaryAsync(
        CancellationToken cancellationToken = default);
    Task<Result<AccountsReceivableCollection>> CollectAsync(
        RecordReceivableCollectionRequest request,
        CancellationToken cancellationToken = default);
    Task<Result<CollectionPaymentAcknowledgment>> GetCollectionAcknowledgmentAsync(
        Guid collectionId,
        CancellationToken cancellationToken = default);
    Task<Result<AccountsReceivableEntry>> AdjustAsync(
        RecordReceivableAdjustmentRequest request,
        CancellationToken cancellationToken = default);
    Task<Result> VoidCollectionAsync(
        Guid collectionId,
        Guid voidedByUserId,
        string reason,
        CancellationToken cancellationToken = default);
}
