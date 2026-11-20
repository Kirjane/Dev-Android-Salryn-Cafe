namespace Salryn.Core.Models;

public static class CafeDeliveryStatuses
{
    public const string Pending = "Pending";
    public const string Dispatched = "Dispatched";
    public const string Delivered = "Delivered";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";

    public static bool IsTerminal(string? value) =>
        string.Equals(value, Delivered, StringComparison.Ordinal) ||
        string.Equals(value, Failed, StringComparison.Ordinal) ||
        string.Equals(value, Cancelled, StringComparison.Ordinal);

    public static bool IsKnown(string? value) =>
        string.Equals(value, Pending, StringComparison.Ordinal) ||
        string.Equals(value, Dispatched, StringComparison.Ordinal) ||
        IsTerminal(value);
}

public static class CafeDeliveryActions
{
    public const string Created = "Created";
    public const string Revised = "Revised";
    public const string Dispatched = "Dispatched";
    public const string Delivered = "Delivered";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
}

public sealed record CafeDeliverySummary(
    Guid DeliveryId,
    Guid OrderId,
    Guid StoreId,
    int Version,
    Guid CurrentRevisionId,
    string Status,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? TerminalReason);

public sealed record CafeDeliveryRevision(
    Guid RevisionId,
    Guid DeliveryId,
    Guid OrderId,
    int RevisionNumber,
    int DeliveryVersion,
    string RecipientName,
    string? ContactPhone,
    string DeliveryAddress,
    string? Landmark,
    string? Instructions,
    long DeliveryFeeCents,
    string? CourierReference,
    string ContentHash,
    Guid ActorUserId,
    DateTimeOffset CreatedAtUtc);

public sealed record CafeDeliveryDetail(
    CafeDeliverySummary Delivery,
    CafeDeliveryRevision CurrentRevision);

public sealed record CafeDeliveryCreateMutation(
    Guid OperationId,
    Guid DeviceId,
    Guid DeliveryId,
    Guid OrderId,
    Guid StoreId,
    Guid ActorUserId,
    DateTimeOffset OccurredAtUtc,
    string RecipientName,
    string? ContactPhone,
    string DeliveryAddress,
    string? Landmark,
    string? Instructions,
    long DeliveryFeeCents,
    string? CourierReference);

public sealed record CafeDeliveryReviseMutation(
    Guid OperationId,
    Guid DeviceId,
    Guid DeliveryId,
    Guid StoreId,
    int ExpectedVersion,
    Guid ActorUserId,
    DateTimeOffset OccurredAtUtc,
    string RecipientName,
    string? ContactPhone,
    string DeliveryAddress,
    string? Landmark,
    string? Instructions,
    long DeliveryFeeCents,
    string? CourierReference);

public sealed record CafeDeliveryAdvanceMutation(
    Guid OperationId,
    Guid DeviceId,
    Guid DeliveryId,
    Guid StoreId,
    int ExpectedVersion,
    Guid ActorUserId,
    DateTimeOffset OccurredAtUtc,
    string TargetStatus,
    string? Reason);

public sealed record CafeDeliveryMutationResult(
    Guid DeliveryId,
    Guid OrderId,
    int Version,
    Guid CurrentRevisionId,
    string Status,
    bool Idempotent);
