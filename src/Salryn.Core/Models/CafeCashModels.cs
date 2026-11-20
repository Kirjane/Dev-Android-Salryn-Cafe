namespace Salryn.Core.Models;

public static class CafeCashReferenceTypes
{
    public const string CashIn = "CASH_IN";
    public const string CashOut = "CASH_OUT";
    public const string OperationalExpense = "OPERATIONAL_EXPENSE";
    public const string InventoryPurchase = "INVENTORY_PURCHASE";
    public const string Reversal = "CASH_REVERSAL";

    public static bool IsKnownManualReference(string? value) =>
        string.Equals(value, CashIn, StringComparison.Ordinal) ||
        string.Equals(value, CashOut, StringComparison.Ordinal) ||
        string.Equals(value, OperationalExpense, StringComparison.Ordinal) ||
        string.Equals(value, InventoryPurchase, StringComparison.Ordinal) ||
        string.Equals(value, Reversal, StringComparison.Ordinal);
}

public sealed record CafeCashPostMutation(
    Guid OperationId,
    Guid MovementId,
    Guid DeviceId,
    Guid StoreId,
    Guid ActorUserId,
    Guid CashierSessionId,
    CashMovementType MovementType,
    long AmountCents,
    string Reason,
    string ReferenceType,
    Guid ReferenceId,
    DateTimeOffset OccurredAtUtc);

public sealed record CafeCashReversalMutation(
    Guid OperationId,
    Guid MovementId,
    Guid DeviceId,
    Guid StoreId,
    Guid ActorUserId,
    Guid CashierSessionId,
    Guid OriginalMovementId,
    string Reason,
    DateTimeOffset OccurredAtUtc);

public sealed record CafeCashMutationResult(
    Guid OperationId,
    Guid MovementId,
    Guid CashierSessionId,
    CashMovementType MovementType,
    long DeltaCents,
    bool IsReplay);

public sealed record CafeCashMovementSnapshot(
    Guid MovementId,
    Guid CashierSessionId,
    Guid ActorUserId,
    CashMovementType MovementType,
    long DeltaCents,
    string ReferenceType,
    Guid ReferenceId,
    Guid? ReversalOfMovementId,
    string? Reason,
    DateTimeOffset CreatedAtUtc);
