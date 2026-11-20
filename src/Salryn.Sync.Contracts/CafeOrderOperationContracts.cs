namespace Salryn.Sync.Contracts;

public static class CafeOrderOperationContract
{
    public const int SchemaVersion = 1;

    public const string OrderRevisionCommittedV1 = "OrderRevisionCommittedV1";
    public const string OrderParkedV1 = "OrderParkedV1";
    public const string OrderResumedV1 = "OrderResumedV1";
    public const string OrderPreparationSubmittedV1 = "OrderPreparationSubmittedV1";
    public const string OrderPreparationStatusChangedV1 = "OrderPreparationStatusChangedV1";
    public const string OrderCancelledV1 = "OrderCancelledV1";
    public const string OrderCompletedV1 = "OrderCompletedV1";

    public static bool IsSupported(string? operationType) =>
        string.Equals(operationType, OrderRevisionCommittedV1, StringComparison.Ordinal) ||
        string.Equals(operationType, OrderParkedV1, StringComparison.Ordinal) ||
        string.Equals(operationType, OrderResumedV1, StringComparison.Ordinal) ||
        string.Equals(operationType, OrderPreparationSubmittedV1, StringComparison.Ordinal) ||
        string.Equals(operationType, OrderPreparationStatusChangedV1, StringComparison.Ordinal) ||
        string.Equals(operationType, OrderCancelledV1, StringComparison.Ordinal) ||
        string.Equals(operationType, OrderCompletedV1, StringComparison.Ordinal);
}

public sealed record CafeOrderModifierSnapshotV1(
    string GroupId,
    string GroupNameSnapshot,
    string OptionId,
    string OptionNameSnapshot,
    long PriceDeltaCents,
    int DisplayOrder);

public sealed record CafeOrderLineSnapshotV1(
    Guid OrderLineId,
    Guid ProductId,
    string ProductNameSnapshot,
    string UnitNameSnapshot,
    long QuantityX1000,
    long BaseUnitPriceCents,
    string? VariantId,
    string? VariantNameSnapshot,
    long? VariantUnitPriceCents,
    IReadOnlyList<CafeOrderModifierSnapshotV1> ModifierSelections,
    string? Note,
    long CalculatedUnitPriceCents,
    long LineTotalCents,
    int DisplayOrder);

public sealed record CafeOrderRevisionSnapshotV1(
    Guid RevisionId,
    int RevisionNumber,
    int OrderVersion,
    Guid? PreviousRevisionId,
    string MenuPublicationRevisionId,
    string ServiceMode,
    string? ServiceReference,
    string ContentHash,
    Guid ActorUserId,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<CafeOrderLineSnapshotV1> Lines);

public sealed record CafeOrderSaleItemLinkV1(
    Guid SaleItemId,
    Guid OrderLineId);

public sealed record CafeOrderOperationV1(
    string OperationType,
    Guid OrderId,
    Guid StoreId,
    int ExpectedVersion,
    int ResultingVersion,
    string Action,
    string? FromState,
    string ToState,
    Guid ActorUserId,
    DateTimeOffset OccurredAtUtc,
    Guid CurrentRevisionId,
    string MenuPublicationRevisionId,
    string ServiceMode,
    string? ServiceReference,
    CafeOrderRevisionSnapshotV1? Revision,
    Guid? SubmittedRevisionId,
    string? RecipeBookRevisionId,
    string? Reason,
    Guid? SaleId,
    Guid? SaleCommittedOperationId,
    string? ReceiptNumber,
    IReadOnlyList<CafeOrderSaleItemLinkV1> SaleItemLinks);
