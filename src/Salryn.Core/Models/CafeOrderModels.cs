namespace Salryn.Core.Models;

public static class CafeOrderServiceModes
{
    public const string DineIn = "DineIn";
    public const string Takeout = "Takeout";

    public static bool IsSupported(string? value) =>
        string.Equals(value, DineIn, StringComparison.Ordinal) ||
        string.Equals(value, Takeout, StringComparison.Ordinal);
}

public static class CafeOrderStates
{
    public const string Open = "Open";
    public const string Parked = "Parked";
    public const string Submitted = "Submitted";
    public const string Preparing = "Preparing";
    public const string Ready = "Ready";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";

    public static bool IsTerminal(string? value) =>
        string.Equals(value, Completed, StringComparison.Ordinal) ||
        string.Equals(value, Cancelled, StringComparison.Ordinal);

    public static bool IsKnown(string? value) =>
        string.Equals(value, Open, StringComparison.Ordinal) ||
        string.Equals(value, Parked, StringComparison.Ordinal) ||
        string.Equals(value, Submitted, StringComparison.Ordinal) ||
        string.Equals(value, Preparing, StringComparison.Ordinal) ||
        string.Equals(value, Ready, StringComparison.Ordinal) ||
        IsTerminal(value);
}

public static class CafeOrderLifecycleActions
{
    public const string Created = "Created";
    public const string Revised = "Revised";
    public const string Parked = "Parked";
    public const string Resumed = "Resumed";
    public const string PreparationSubmitted = "PreparationSubmitted";
    public const string PreparationPreparing = "PreparationPreparing";
    public const string PreparationReady = "PreparationReady";
    public const string Cancelled = "Cancelled";
    public const string Completed = "Completed";
}

public sealed record CafeOrderModifierSelectionSnapshot(
    string GroupId,
    string GroupNameSnapshot,
    string OptionId,
    string OptionNameSnapshot,
    long PriceDeltaCents,
    int DisplayOrder);

public sealed record CafeOrderLineSnapshot(
    Guid OrderLineId,
    Guid ProductId,
    string ProductNameSnapshot,
    string UnitNameSnapshot,
    long QuantityX1000,
    long BaseUnitPriceCents,
    string? VariantId,
    string? VariantNameSnapshot,
    long? VariantUnitPriceCents,
    IReadOnlyList<CafeOrderModifierSelectionSnapshot> ModifierSelections,
    string? Note,
    long CalculatedUnitPriceCents,
    long LineTotalCents,
    int DisplayOrder);

public sealed record CafeOrderRevisionSnapshot(
    Guid RevisionId,
    Guid OrderId,
    int RevisionNumber,
    int OrderVersion,
    Guid? PreviousRevisionId,
    string MenuPublicationRevisionId,
    string ServiceMode,
    string? ServiceReference,
    string ContentHash,
    Guid ActorUserId,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<CafeOrderLineSnapshot> Lines);

public sealed record CafeOrderLifecycleEvent(
    Guid EventId,
    Guid OperationId,
    Guid OrderId,
    int ExpectedVersion,
    int ResultingVersion,
    string Action,
    string? FromState,
    string ToState,
    Guid ActorUserId,
    DateTimeOffset OccurredAtUtc,
    string? Reason,
    Guid? OrderRevisionId,
    string? RecipeBookRevisionId,
    Guid? SaleId,
    string ContentHash);

public sealed record CafeOrderSummary(
    Guid OrderId,
    Guid StoreId,
    int OrderVersion,
    Guid CurrentRevisionId,
    string MenuPublicationRevisionId,
    string State,
    string ServiceMode,
    string? ServiceReference,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    Guid? SubmittedRevisionId,
    string? SubmittedRecipeBookRevisionId,
    Guid? CompletedSaleId,
    string? CancellationReason);

public sealed record CafeOrderDetail(
    CafeOrderSummary Order,
    CafeOrderRevisionSnapshot CurrentRevision,
    IReadOnlyList<CafeOrderLifecycleEvent> LifecycleEvents);

public sealed record CafeOrderLineMutation(
    Guid OrderLineId,
    Guid ProductId,
    string? VariantId,
    IReadOnlyList<string> ModifierOptionIds,
    long QuantityX1000,
    string? Note,
    int DisplayOrder);

public sealed record CafeOrderRevisionMutation(
    Guid OperationId,
    Guid DeviceId,
    Guid OrderId,
    Guid StoreId,
    int ExpectedVersion,
    Guid ActorUserId,
    DateTimeOffset OccurredAtUtc,
    string ServiceMode,
    string? ServiceReference,
    IReadOnlyList<CafeOrderLineMutation> Lines);

public sealed record CafeOrderLifecycleMutation(
    Guid OperationId,
    Guid DeviceId,
    Guid OrderId,
    Guid StoreId,
    int ExpectedVersion,
    Guid ActorUserId,
    DateTimeOffset OccurredAtUtc);

public sealed record CafeOrderPreparationSubmitMutation(
    Guid OperationId,
    Guid DeviceId,
    Guid OrderId,
    Guid StoreId,
    int ExpectedVersion,
    Guid ActorUserId,
    DateTimeOffset OccurredAtUtc);

public sealed record CafeOrderPreparationStatusMutation(
    Guid OperationId,
    Guid DeviceId,
    Guid OrderId,
    Guid StoreId,
    int ExpectedVersion,
    Guid ActorUserId,
    DateTimeOffset OccurredAtUtc,
    string TargetState);

public sealed record CafeOrderCancellationMutation(
    Guid OperationId,
    Guid DeviceId,
    Guid OrderId,
    Guid StoreId,
    int ExpectedVersion,
    Guid ActorUserId,
    DateTimeOffset OccurredAtUtc,
    string Reason);

public sealed record CafeOrderMutationResult(
    Guid OrderId,
    int OrderVersion,
    string State,
    Guid CurrentRevisionId,
    bool Idempotent);

public sealed record CafeOrderPreparationSubmissionResult(
    Guid OrderId,
    int OrderVersion,
    Guid SubmittedRevisionId,
    string? RecipeBookRevisionId,
    bool Idempotent);

public sealed record CafeOrderCompletionLink(
    Guid OrderId,
    Guid OrderRevisionId,
    Guid SaleId,
    Guid CompletionOperationId,
    Guid SaleCommittedOperationId,
    DateTimeOffset CompletedAtUtc);

public sealed record CafeOrderSaleItemLink(
    Guid SaleItemId,
    Guid OrderId,
    Guid OrderRevisionId,
    Guid OrderLineId);

public sealed record CafeOrderPaymentMutation(
    PaymentMethod Method,
    long AmountCents);

public sealed record CafeOrderLineDiscountMutation(
    Guid OrderLineId,
    long AmountCents);

public sealed record CafeOrderCompletionMutation(
    Guid CompletionOperationId,
    Guid SaleCommittedOperationId,
    Guid DeviceId,
    Guid OrderId,
    Guid StoreId,
    int ExpectedVersion,
    Guid ActorUserId,
    DateTimeOffset OccurredAtUtc,
    IReadOnlyList<CafeOrderPaymentMutation> Payments)
{
    public Guid FinancialDecisionOperationId { get; init; }

    public Guid FinancialDecisionId { get; init; }

    public Guid? CustomerId { get; init; }

    public DateOnly? ReceivableDueDate { get; init; }

    public string? ReceivableNote { get; init; }

    public IReadOnlyList<CafeOrderLineDiscountMutation> LineDiscounts { get; init; } =
        Array.Empty<CafeOrderLineDiscountMutation>();

    public long OrderDiscountCents { get; init; }

    public string? DiscountReason { get; init; }

    public IReadOnlyList<CafeSaleFeeMutation> Fees { get; init; } =
        Array.Empty<CafeSaleFeeMutation>();

    public long TaxCents { get; init; }

    public CafeBookingFinalizationBinding? BookingFinalization { get; init; }
}

public sealed record CafeOrderCompletionResult(
    Guid OrderId,
    int OrderVersion,
    Guid SaleId,
    string ReceiptNumber,
    bool Idempotent);
