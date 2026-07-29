namespace Salryn.Core.Models;

public enum AccountsReceivableEntryType
{
    CreditSale = 1,
    Collection = 2,
    AdjustmentDebit = 3,
    AdjustmentCredit = 4,
    SaleReversal = 5,
    CollectionReversal = 6
}

public enum SalePaymentStatus
{
    Paid = 1,
    Credit = 2,
    Partial = 3
}

public sealed record ReceivableCustomer(
    Guid Id,
    string FullName,
    string? Phone,
    string? Address,
    string? Note,
    long? CreditLimitCents,
    bool IsActive,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record AccountsReceivableEntry(
    Guid Id,
    Guid CustomerId,
    Guid? SaleId,
    Guid? CollectionId,
    AccountsReceivableEntryType EntryType,
    long DebitCents,
    long CreditCents,
    long BalanceAfterCents,
    DateOnly? DueDate,
    string? Note,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt,
    SalePaymentStatus? PaymentStatus = null);

public sealed record AccountsReceivableCollection(
    Guid Id,
    Guid CustomerId,
    long AmountCents,
    string Method,
    string? Note,
    Guid CollectedByUserId,
    DateTimeOffset CollectedAt,
    DateTimeOffset? VoidedAt,
    Guid? VoidedByUserId,
    string? VoidReason);
