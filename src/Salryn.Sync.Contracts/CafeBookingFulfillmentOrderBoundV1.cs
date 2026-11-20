using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Salryn.Sync.Contracts;

public static class BookingFulfillmentOrderBoundV1Contract
{
    public const string OperationType = "BookingFulfillmentOrderBoundV1";
    public const int SchemaVersion = 1;
}

public sealed record BookingFulfillmentOrderBoundV1(
    string OperationType,
    Guid OperationId,
    Guid BookingId,
    Guid BookingRevisionId,
    Guid BookingOrderLinkId,
    int ExpectedBookingVersion,
    int ResultingBookingVersion,
    Guid StoreId,
    Guid ActorUserId,
    DateTimeOffset OccurredAtUtc,
    Guid OrderId,
    string MenuPublicationRevisionId,
    string IntendedServiceMode,
    CafeOrderRevisionSnapshotV1 OrderRevision,
    string ResultBookingStatus,
    string ResultFulfillmentState);

public sealed record BookingFulfillmentOrderBoundParseResult(
    bool IsValid,
    BookingFulfillmentOrderBoundV1? Payload,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static BookingFulfillmentOrderBoundParseResult Success(
        BookingFulfillmentOrderBoundV1 payload) =>
        new(true, payload, null, null);

    public static BookingFulfillmentOrderBoundParseResult Fail(
        string code,
        string message) =>
        new(false, null, code, message);
}

public static class BookingFulfillmentOrderBoundV1CanonicalJson
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static string Serialize(BookingFulfillmentOrderBoundV1 payload) =>
        JsonSerializer.Serialize(Normalize(payload), Json);

    public static string ComputeHash(string canonicalJson) =>
        Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson)))
        .ToLowerInvariant();

    public static BookingFulfillmentOrderBoundParseResult Parse(string canonicalJson)
    {
        try
        {
            BookingFulfillmentOrderBoundV1? payload =
                JsonSerializer.Deserialize<BookingFulfillmentOrderBoundV1>(
                    canonicalJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (payload is null)
                return BookingFulfillmentOrderBoundParseResult.Fail(
                    "CafeBooking.OrderBindPayloadMissing",
                    "Booking fulfillment order-bound payload is missing.");

            BookingFulfillmentOrderBoundV1 normalized = Normalize(payload);
            string? error = Validate(normalized);
            if (error is not null)
                return BookingFulfillmentOrderBoundParseResult.Fail(
                    "CafeBooking.OrderBindPayloadInvalid",
                    error);

            string serialized = Serialize(normalized);
            if (!string.Equals(serialized, canonicalJson, StringComparison.Ordinal))
                return BookingFulfillmentOrderBoundParseResult.Fail(
                    "CafeBooking.OrderBindPayloadNotCanonical",
                    "Booking fulfillment order-bound payload is not canonical JSON.");

            return BookingFulfillmentOrderBoundParseResult.Success(normalized);
        }
        catch (Exception ex) when (
            ex is JsonException or FormatException or OverflowException or ArgumentException)
        {
            return BookingFulfillmentOrderBoundParseResult.Fail(
                "CafeBooking.OrderBindPayloadInvalidJson",
                ex.Message);
        }
    }

    public static string? Validate(BookingFulfillmentOrderBoundV1 p)
    {
        if (!string.Equals(
                p.OperationType,
                BookingFulfillmentOrderBoundV1Contract.OperationType,
                StringComparison.Ordinal) ||
            p.OperationId == Guid.Empty ||
            p.BookingId == Guid.Empty ||
            p.BookingRevisionId == Guid.Empty ||
            p.BookingOrderLinkId == Guid.Empty ||
            p.StoreId == Guid.Empty ||
            p.ActorUserId == Guid.Empty ||
            p.OrderId == Guid.Empty ||
            p.ExpectedBookingVersion <= 0 ||
            p.ResultingBookingVersion != checked(p.ExpectedBookingVersion + 1))
            return "Booking fulfillment identities/version are invalid.";

        if (!string.Equals(p.ResultBookingStatus, "ReadyForFulfillment", StringComparison.Ordinal) ||
            !string.Equals(p.ResultFulfillmentState, "OrderBound", StringComparison.Ordinal))
            return "Order binding must retain ReadyForFulfillment and transition fulfillment to OrderBound.";

        if (string.IsNullOrWhiteSpace(p.MenuPublicationRevisionId) ||
            !string.Equals(p.IntendedServiceMode, p.OrderRevision.ServiceMode, StringComparison.Ordinal) ||
            !string.Equals(
                p.MenuPublicationRevisionId,
                p.OrderRevision.MenuPublicationRevisionId,
                StringComparison.Ordinal))
            return "Order revision must use the frozen Booking MenuPublication and service mode.";

        if (p.OrderRevision.RevisionId == Guid.Empty ||
            p.OrderRevision.RevisionNumber != 1 ||
            p.OrderRevision.OrderVersion != 1 ||
            p.OrderRevision.PreviousRevisionId.HasValue ||
            p.OrderRevision.ActorUserId != p.ActorUserId ||
            p.OrderRevision.Lines is null ||
            p.OrderRevision.Lines.Count == 0 ||
            string.IsNullOrWhiteSpace(p.OrderRevision.ContentHash))
            return "Seeded C4 initial revision evidence is incomplete.";

        string? revisionError = ValidateRevision(p.OrderRevision);
        return revisionError;
    }

    private static string? ValidateRevision(CafeOrderRevisionSnapshotV1 r)
    {
        HashSet<Guid> lineIds = new();
        foreach (CafeOrderLineSnapshotV1 line in r.Lines)
        {
            if (line.OrderLineId == Guid.Empty ||
                line.ProductId == Guid.Empty ||
                !lineIds.Add(line.OrderLineId) ||
                string.IsNullOrWhiteSpace(line.ProductNameSnapshot) ||
                string.IsNullOrWhiteSpace(line.UnitNameSnapshot) ||
                line.QuantityX1000 <= 0 ||
                line.BaseUnitPriceCents < 0 ||
                line.CalculatedUnitPriceCents < 0 ||
                line.LineTotalCents < 0)
                return "Seeded C4 line evidence is invalid.";

            bool noVariant =
                line.VariantId is null &&
                line.VariantNameSnapshot is null &&
                !line.VariantUnitPriceCents.HasValue;
            bool hasVariant =
                !string.IsNullOrWhiteSpace(line.VariantId) &&
                !string.IsNullOrWhiteSpace(line.VariantNameSnapshot) &&
                line.VariantUnitPriceCents.HasValue &&
                line.VariantUnitPriceCents.Value >= 0;
            if (!noVariant && !hasVariant)
                return "Seeded C4 Variant evidence must be fully absent or populated.";

            long modifierDelta = (line.ModifierSelections ??
                Array.Empty<CafeOrderModifierSnapshotV1>())
                .Sum(x => x.PriceDeltaCents);
            long expectedUnit = checked(
                (line.VariantUnitPriceCents ?? line.BaseUnitPriceCents) +
                modifierDelta);
            if (expectedUnit != line.CalculatedUnitPriceCents || expectedUnit < 0)
                return "Seeded C4 unit price does not match frozen Variant/Modifier evidence.";

            long expectedLine = checked((long)Math.Round(
                expectedUnit * (line.QuantityX1000 / 1000m),
                0,
                MidpointRounding.AwayFromZero));
            if (expectedLine != line.LineTotalCents)
                return "Seeded C4 line total does not match frozen quantity/price evidence.";
        }

        return null;
    }

    private static BookingFulfillmentOrderBoundV1 Normalize(
        BookingFulfillmentOrderBoundV1 p) =>
        p with
        {
            OperationType = (p.OperationType ?? string.Empty).Trim(),
            MenuPublicationRevisionId = (p.MenuPublicationRevisionId ?? string.Empty).Trim(),
            IntendedServiceMode = (p.IntendedServiceMode ?? string.Empty).Trim(),
            ResultBookingStatus = (p.ResultBookingStatus ?? string.Empty).Trim(),
            ResultFulfillmentState = (p.ResultFulfillmentState ?? string.Empty).Trim(),
            OccurredAtUtc = p.OccurredAtUtc.ToUniversalTime(),
            OrderRevision = Normalize(p.OrderRevision)
        };

    private static CafeOrderRevisionSnapshotV1 Normalize(CafeOrderRevisionSnapshotV1 r) =>
        r with
        {
            MenuPublicationRevisionId = (r.MenuPublicationRevisionId ?? string.Empty).Trim(),
            ServiceMode = (r.ServiceMode ?? string.Empty).Trim(),
            ServiceReference = Clean(r.ServiceReference),
            ContentHash = (r.ContentHash ?? string.Empty).Trim(),
            CreatedAtUtc = r.CreatedAtUtc.ToUniversalTime(),
            Lines = (r.Lines ?? Array.Empty<CafeOrderLineSnapshotV1>())
                .Select(line => line with
                {
                    ProductNameSnapshot = (line.ProductNameSnapshot ?? string.Empty).Trim(),
                    UnitNameSnapshot = (line.UnitNameSnapshot ?? string.Empty).Trim(),
                    VariantId = Clean(line.VariantId),
                    VariantNameSnapshot = Clean(line.VariantNameSnapshot),
                    Note = Clean(line.Note),
                    ModifierSelections = (line.ModifierSelections ??
                        Array.Empty<CafeOrderModifierSnapshotV1>())
                        .Select(m => m with
                        {
                            GroupId = (m.GroupId ?? string.Empty).Trim(),
                            GroupNameSnapshot = (m.GroupNameSnapshot ?? string.Empty).Trim(),
                            OptionId = (m.OptionId ?? string.Empty).Trim(),
                            OptionNameSnapshot = (m.OptionNameSnapshot ?? string.Empty).Trim()
                        })
                        .OrderBy(m => m.DisplayOrder)
                        .ThenBy(m => m.GroupId, StringComparer.Ordinal)
                        .ThenBy(m => m.OptionId, StringComparer.Ordinal)
                        .ToArray()
                })
                .OrderBy(line => line.DisplayOrder)
                .ThenBy(line => line.OrderLineId)
                .ToArray()
        };

    private static string? Clean(string? value)
    {
        string v = (value ?? string.Empty).Trim();
        return v.Length == 0 ? null : v;
    }
}
