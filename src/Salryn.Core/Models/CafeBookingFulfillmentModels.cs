namespace Salryn.Core.Models;

public sealed record CafeBookingOrderBindMutation(
    Guid OperationId,
    Guid BookingId,
    Guid OrderId,
    Guid DeviceId,
    Guid StoreId,
    Guid ActorUserId,
    int ExpectedBookingVersion,
    DateTimeOffset OccurredAtUtc);

public sealed record CafeBookingOrderBindResult(
    Guid BookingId,
    Guid BookingRevisionId,
    Guid BookingOrderLinkId,
    Guid OrderId,
    Guid OrderRevisionId,
    int BookingVersion,
    string BookingStatus,
    string FulfillmentState,
    bool IsReplay);

public sealed record CafeBookingFinalizationBinding(
    Guid BookingId,
    Guid BookingRevisionId,
    Guid BookingOrderLinkId,
    int ExpectedBookingVersion);
