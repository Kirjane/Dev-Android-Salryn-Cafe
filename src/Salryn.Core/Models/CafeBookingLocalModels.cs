namespace Salryn.Core.Models;

public static class CafeBookingActions
{
    public const string Created = "Created";
    public const string Revised = "Revised";
    public const string Confirmed = "Confirmed";
    public const string ReadyForFulfillment = "ReadyForFulfillment";
    public const string Cancelled = "Cancelled";
    public const string OrderBound = "OrderBound";
    public const string Fulfilled = "Fulfilled";
}

public sealed record CafeBookingLineMutation(
    Guid BookingLineId,
    Guid ProductId,
    string? VariantId,
    IReadOnlyList<string> ModifierOptionIds,
    long QuantityX1000,
    string? LineNote,
    int DisplayOrder);

public sealed record CafeBookingConfigurationMutation(
    Guid CustomerId,
    string BookingMode,
    string EventName,
    string? EventReference,
    DateTimeOffset EventStartUtc,
    DateTimeOffset? EventEndUtc,
    string? Location,
    DateTimeOffset? SetupAtUtc,
    DateTimeOffset? ServiceWindowStartUtc,
    DateTimeOffset? ServiceWindowEndUtc,
    int? Headcount,
    string? PackageLabel,
    string MenuPublicationRevisionId,
    long DepositRequirementCents,
    string? SpecialInstructions,
    string? Notes,
    string IntendedServiceMode,
    IReadOnlyList<CafeBookingLineMutation> Lines);

public sealed record CafeBookingCreateMutation(
    Guid OperationId,
    Guid BookingId,
    Guid RevisionId,
    Guid DeviceId,
    Guid StoreId,
    Guid ActorUserId,
    DateTimeOffset OccurredAtUtc,
    CafeBookingConfigurationMutation Configuration);

public sealed record CafeBookingReviseMutation(
    Guid OperationId,
    Guid BookingId,
    Guid RevisionId,
    Guid DeviceId,
    Guid StoreId,
    int ExpectedVersion,
    Guid ActorUserId,
    DateTimeOffset OccurredAtUtc,
    CafeBookingConfigurationMutation Configuration);

public sealed record CafeBookingStatusMutation(
    Guid OperationId,
    Guid BookingId,
    Guid DeviceId,
    Guid StoreId,
    int ExpectedVersion,
    Guid ActorUserId,
    DateTimeOffset OccurredAtUtc,
    string TargetStatus);

public sealed record CafeBookingCancelMutation(
    Guid OperationId,
    Guid BookingId,
    Guid DeviceId,
    Guid StoreId,
    int ExpectedVersion,
    Guid ActorUserId,
    DateTimeOffset OccurredAtUtc,
    string Reason);

public sealed record CafeBookingDepositReceiveMutation(
    Guid OperationId,
    Guid DepositFactId,
    Guid CashMovementId,
    Guid DeviceId,
    Guid StoreId,
    Guid BookingId,
    Guid BookingRevisionId,
    Guid ActorUserId,
    Guid CashierSessionId,
    long AmountCents,
    string Reference,
    DateTimeOffset OccurredAtUtc);

public sealed record CafeBookingDepositAllocationMutation(
    Guid ReceivedDepositFactId,
    long AmountCents);

public sealed record CafeBookingDepositRefundMutation(
    Guid OperationId,
    Guid DepositFactId,
    Guid CashMovementId,
    Guid DeviceId,
    Guid StoreId,
    Guid BookingId,
    Guid BookingRevisionId,
    Guid ActorUserId,
    Guid CashierSessionId,
    long AmountCents,
    string Reason,
    string Reference,
    DateTimeOffset OccurredAtUtc,
    IReadOnlyList<CafeBookingDepositAllocationMutation> Allocations);

public sealed record CafeBookingModifierSelectionSnapshot(
    string GroupId,
    string GroupNameSnapshot,
    string OptionId,
    string OptionNameSnapshot,
    long PriceDeltaCents,
    int DisplayOrder);

public sealed record CafeBookingLineSnapshot(
    Guid BookingLineId,
    Guid ProductId,
    string ProductNameSnapshot,
    string UnitNameSnapshot,
    long QuantityX1000,
    long BaseUnitPriceCents,
    string? VariantId,
    string? VariantNameSnapshot,
    long? VariantUnitPriceCents,
    long QuotedUnitPriceCents,
    long LineTotalCents,
    int DisplayOrder,
    string? LineNote,
    IReadOnlyList<CafeBookingModifierSelectionSnapshot> ModifierSelections);

public sealed record CafeBookingRevisionSnapshot(
    Guid RevisionId,
    Guid BookingId,
    int RevisionNumber,
    int BookingVersion,
    Guid? PreviousRevisionId,
    Guid CustomerId,
    string CustomerNameSnapshot,
    string? CustomerPhoneSnapshot,
    string? CustomerAddressSnapshot,
    string BookingMode,
    string EventName,
    string? EventReference,
    DateTimeOffset EventStartUtc,
    DateTimeOffset? EventEndUtc,
    string? Location,
    DateTimeOffset? SetupAtUtc,
    DateTimeOffset? ServiceWindowStartUtc,
    DateTimeOffset? ServiceWindowEndUtc,
    int? Headcount,
    string? PackageLabel,
    string MenuPublicationRevisionId,
    long QuotedTotalCents,
    long DepositRequirementCents,
    string? SpecialInstructions,
    string? Notes,
    string IntendedServiceMode,
    Guid ActorUserId,
    DateTimeOffset CreatedAtUtc,
    string ContentHash,
    IReadOnlyList<CafeBookingLineSnapshot> Lines);

public sealed record CafeBookingSummary(
    Guid BookingId,
    Guid StoreId,
    int BookingVersion,
    Guid CurrentRevisionId,
    string Status,
    string FulfillmentState,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAtUtc,
    Guid UpdatedByUserId,
    DateTimeOffset UpdatedAtUtc,
    Guid? CancelledByUserId,
    DateTimeOffset? CancelledAtUtc,
    string? CancellationReason,
    Guid CustomerId,
    string CustomerNameSnapshot,
    string BookingMode,
    DateTimeOffset EventStartUtc,
    DateTimeOffset? EventEndUtc,
    string? Location,
    string? PackageLabel,
    long QuotedTotalCents,
    long DepositRequirementCents,
    long DepositReceivedTotalCents,
    long DepositRefundedTotalCents,
    long DepositAppliedTotalCents,
    long PreFulfillmentDepositCreditCents,
    long PreFulfillmentOutstandingBalanceCents,
    Guid? FinalOrderId,
    Guid? FinalSaleId);

public sealed record CafeBookingDetail(
    CafeBookingSummary Booking,
    CafeBookingRevisionSnapshot CurrentRevision);

public sealed record CafeBookingMutationResult(
    Guid BookingId,
    int BookingVersion,
    Guid CurrentRevisionId,
    string Status,
    string FulfillmentState,
    bool IsReplay);

public sealed record CafeBookingDepositMutationResult(
    Guid BookingId,
    Guid BookingRevisionId,
    Guid DepositFactId,
    Guid CashMovementId,
    long AmountCents,
    bool IsReplay);
