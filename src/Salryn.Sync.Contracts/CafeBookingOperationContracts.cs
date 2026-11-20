using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Salryn.Sync.Contracts;

public static class CafeBookingOperationContract
{
    public const int SchemaVersion = 1;

    public const string BookingRevisionCommittedV1 = "BookingRevisionCommittedV1";
    public const string BookingStatusAdvancedV1 = "BookingStatusAdvancedV1";
    public const string BookingCancelledV1 = "BookingCancelledV1";
    public const string BookingDepositReceivedV1 = "BookingDepositReceivedV1";
    public const string BookingDepositRefundedV1 = "BookingDepositRefundedV1";

    public const string Created = "Created";
    public const string Revised = "Revised";
    public const string Confirmed = "Confirmed";
    public const string ReadyForFulfillment = "ReadyForFulfillment";

    public const string CashIn = "CashIn";
    public const string CashOut = "CashOut";
    public const string BookingDepositReceiptReference = "BOOKING_DEPOSIT_RECEIPT";
    public const string BookingDepositRefundReference = "BOOKING_DEPOSIT_REFUND";

    public static bool IsSupported(string? value) =>
        string.Equals(value, BookingRevisionCommittedV1, StringComparison.Ordinal) ||
        string.Equals(value, BookingStatusAdvancedV1, StringComparison.Ordinal) ||
        string.Equals(value, BookingCancelledV1, StringComparison.Ordinal) ||
        string.Equals(value, BookingDepositReceivedV1, StringComparison.Ordinal) ||
        string.Equals(value, BookingDepositRefundedV1, StringComparison.Ordinal);
}

public sealed record BookingModifierSelectionV1(
    string GroupId,
    string GroupNameSnapshot,
    string OptionId,
    string OptionNameSnapshot,
    long PriceDeltaCents,
    int DisplayOrder);

public sealed record BookingLineSnapshotV1(
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
    IReadOnlyList<BookingModifierSelectionV1> ModifierSelections);

public sealed record BookingRevisionSnapshotV1(
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
    IReadOnlyList<BookingLineSnapshotV1> Lines);

public sealed record BookingRevisionCommittedV1(
    string OperationType,
    string ConfigurationAction,
    Guid OperationId,
    Guid BookingId,
    Guid StoreId,
    int ExpectedVersion,
    int ResultingVersion,
    string? FromStatus,
    string ToStatus,
    string FromFulfillmentState,
    string ToFulfillmentState,
    Guid ActorUserId,
    DateTimeOffset OccurredAtUtc,
    BookingRevisionSnapshotV1 Revision);

public sealed record BookingStatusAdvancedV1(
    string OperationType,
    Guid OperationId,
    Guid BookingId,
    Guid BookingRevisionId,
    Guid StoreId,
    int ExpectedVersion,
    int ResultingVersion,
    string FromStatus,
    string ToStatus,
    string FromFulfillmentState,
    string ToFulfillmentState,
    Guid ActorUserId,
    DateTimeOffset OccurredAtUtc);

public sealed record BookingCancelledV1(
    string OperationType,
    Guid OperationId,
    Guid BookingId,
    Guid BookingRevisionId,
    Guid StoreId,
    int ExpectedVersion,
    int ResultingVersion,
    string FromStatus,
    string ToStatus,
    string FromFulfillmentState,
    string ToFulfillmentState,
    string Reason,
    Guid ActorUserId,
    DateTimeOffset OccurredAtUtc);

public sealed record BookingDepositReceivedV1(
    string OperationType,
    Guid OperationId,
    Guid DepositFactId,
    Guid BookingId,
    Guid BookingRevisionId,
    Guid StoreId,
    Guid ActorUserId,
    Guid CashierSessionId,
    Guid CashMovementId,
    long AmountCents,
    string Reference,
    DateTimeOffset OccurredAtUtc,
    string CashMovementClass,
    long CashDeltaCents,
    string CashReferenceType,
    Guid CashReferenceId);

public sealed record BookingDepositAllocationV1(
    Guid ReceivedDepositFactId,
    long AmountCents);

public sealed record BookingDepositRefundedV1(
    string OperationType,
    Guid OperationId,
    Guid DepositFactId,
    Guid BookingId,
    Guid BookingRevisionId,
    Guid StoreId,
    Guid ActorUserId,
    Guid CashierSessionId,
    Guid CashMovementId,
    long AmountCents,
    string Reason,
    string Reference,
    DateTimeOffset OccurredAtUtc,
    string CashMovementClass,
    long CashDeltaCents,
    string CashReferenceType,
    Guid CashReferenceId,
    IReadOnlyList<BookingDepositAllocationV1> Allocations);

public sealed record CafeBookingCanonicalParseResult<T>(
    bool IsValid,
    T? Payload,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static CafeBookingCanonicalParseResult<T> Success(T payload) =>
        new(true, payload, null, null);

    public static CafeBookingCanonicalParseResult<T> Fail(string code, string message) =>
        new(false, default, code, message);
}

public static class CafeBookingCanonicalJson
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static string Serialize(BookingRevisionCommittedV1 payload) =>
        JsonSerializer.Serialize(Normalize(payload), Json);

    public static string Serialize(BookingStatusAdvancedV1 payload) =>
        JsonSerializer.Serialize(Normalize(payload), Json);

    public static string Serialize(BookingCancelledV1 payload) =>
        JsonSerializer.Serialize(Normalize(payload), Json);

    public static string Serialize(BookingDepositReceivedV1 payload) =>
        JsonSerializer.Serialize(Normalize(payload), Json);

    public static string Serialize(BookingDepositRefundedV1 payload) =>
        JsonSerializer.Serialize(Normalize(payload), Json);

    public static string ComputeHash(string canonicalJson) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson)))
            .ToLowerInvariant();

    public static CafeBookingCanonicalParseResult<BookingRevisionCommittedV1>
        ParseRevision(string json) =>
        Parse<BookingRevisionCommittedV1>(
            json,
            Normalize,
            Validate,
            Serialize,
            CafeBookingOperationContract.BookingRevisionCommittedV1);

    public static CafeBookingCanonicalParseResult<BookingStatusAdvancedV1>
        ParseStatus(string json) =>
        Parse<BookingStatusAdvancedV1>(
            json,
            Normalize,
            Validate,
            Serialize,
            CafeBookingOperationContract.BookingStatusAdvancedV1);

    public static CafeBookingCanonicalParseResult<BookingCancelledV1>
        ParseCancellation(string json) =>
        Parse<BookingCancelledV1>(
            json,
            Normalize,
            Validate,
            Serialize,
            CafeBookingOperationContract.BookingCancelledV1);

    public static CafeBookingCanonicalParseResult<BookingDepositReceivedV1>
        ParseDepositReceive(string json) =>
        Parse<BookingDepositReceivedV1>(
            json,
            Normalize,
            Validate,
            Serialize,
            CafeBookingOperationContract.BookingDepositReceivedV1);

    public static CafeBookingCanonicalParseResult<BookingDepositRefundedV1>
        ParseDepositRefund(string json) =>
        Parse<BookingDepositRefundedV1>(
            json,
            Normalize,
            Validate,
            Serialize,
            CafeBookingOperationContract.BookingDepositRefundedV1);

    private static CafeBookingCanonicalParseResult<T> Parse<T>(
        string json,
        Func<T,T> normalize,
        Func<T,string?> validate,
        Func<T,string> serialize,
        string requiredType)
    {
        try
        {
            T? payload = JsonSerializer.Deserialize<T>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (payload is null)
                return CafeBookingCanonicalParseResult<T>.Fail(
                    "CafeBooking.PayloadMissing",
                    "C9 Booking payload is missing.");

            T normalized = normalize(payload);
            string? error = validate(normalized);
            if (error is not null)
                return CafeBookingCanonicalParseResult<T>.Fail(
                    "CafeBooking.PayloadInvalid",
                    error);

            string operationType = normalized switch
            {
                BookingRevisionCommittedV1 x => x.OperationType,
                BookingStatusAdvancedV1 x => x.OperationType,
                BookingCancelledV1 x => x.OperationType,
                BookingDepositReceivedV1 x => x.OperationType,
                BookingDepositRefundedV1 x => x.OperationType,
                _ => string.Empty
            };

            if (!string.Equals(operationType, requiredType, StringComparison.Ordinal))
                return CafeBookingCanonicalParseResult<T>.Fail(
                    "CafeBooking.OperationTypeMismatch",
                    "Canonical C9 payload operation type is incorrect.");

            if (!string.Equals(serialize(normalized), json, StringComparison.Ordinal))
                return CafeBookingCanonicalParseResult<T>.Fail(
                    "CafeBooking.PayloadNotCanonical",
                    "C9 Booking payload is not canonical JSON.");

            return CafeBookingCanonicalParseResult<T>.Success(normalized);
        }
        catch (JsonException ex)
        {
            return CafeBookingCanonicalParseResult<T>.Fail(
                "CafeBooking.PayloadInvalidJson",
                ex.Message);
        }
        catch (OverflowException ex)
        {
            return CafeBookingCanonicalParseResult<T>.Fail(
                "CafeBooking.PayloadOverflow",
                ex.Message);
        }
    }

    private static BookingRevisionCommittedV1 Normalize(BookingRevisionCommittedV1 p) =>
        p with
        {
            ConfigurationAction = CleanRequired(p.ConfigurationAction),
            FromStatus = CleanOptional(p.FromStatus),
            ToStatus = CleanRequired(p.ToStatus),
            FromFulfillmentState = CleanRequired(p.FromFulfillmentState),
            ToFulfillmentState = CleanRequired(p.ToFulfillmentState),
            OccurredAtUtc = p.OccurredAtUtc.ToUniversalTime(),
            Revision = Normalize(p.Revision)
        };

    private static BookingRevisionSnapshotV1 Normalize(BookingRevisionSnapshotV1 r) =>
        r with
        {
            CustomerNameSnapshot = CleanRequired(r.CustomerNameSnapshot),
            CustomerPhoneSnapshot = CleanOptional(r.CustomerPhoneSnapshot),
            CustomerAddressSnapshot = CleanOptional(r.CustomerAddressSnapshot),
            BookingMode = CleanRequired(r.BookingMode),
            EventName = CleanRequired(r.EventName),
            EventReference = CleanOptional(r.EventReference),
            EventStartUtc = r.EventStartUtc.ToUniversalTime(),
            EventEndUtc = r.EventEndUtc?.ToUniversalTime(),
            Location = CleanOptional(r.Location),
            SetupAtUtc = r.SetupAtUtc?.ToUniversalTime(),
            ServiceWindowStartUtc = r.ServiceWindowStartUtc?.ToUniversalTime(),
            ServiceWindowEndUtc = r.ServiceWindowEndUtc?.ToUniversalTime(),
            PackageLabel = CleanOptional(r.PackageLabel),
            MenuPublicationRevisionId = CleanRequired(r.MenuPublicationRevisionId),
            SpecialInstructions = CleanOptional(r.SpecialInstructions),
            Notes = CleanOptional(r.Notes),
            IntendedServiceMode = CleanRequired(r.IntendedServiceMode),
            CreatedAtUtc = r.CreatedAtUtc.ToUniversalTime(),
            ContentHash = CleanRequired(r.ContentHash),
            Lines = (r.Lines ?? Array.Empty<BookingLineSnapshotV1>())
                .Select(Normalize)
                .OrderBy(x => x.DisplayOrder)
                .ThenBy(x => x.BookingLineId)
                .ToArray()
        };

    private static BookingLineSnapshotV1 Normalize(BookingLineSnapshotV1 l) =>
        l with
        {
            ProductNameSnapshot = CleanRequired(l.ProductNameSnapshot),
            UnitNameSnapshot = CleanRequired(l.UnitNameSnapshot),
            VariantId = CleanOptional(l.VariantId),
            VariantNameSnapshot = CleanOptional(l.VariantNameSnapshot),
            LineNote = CleanOptional(l.LineNote),
            ModifierSelections = (l.ModifierSelections ?? Array.Empty<BookingModifierSelectionV1>())
                .Select(x => x with
                {
                    GroupId = CleanRequired(x.GroupId),
                    GroupNameSnapshot = CleanRequired(x.GroupNameSnapshot),
                    OptionId = CleanRequired(x.OptionId),
                    OptionNameSnapshot = CleanRequired(x.OptionNameSnapshot)
                })
                .OrderBy(x => x.DisplayOrder)
                .ThenBy(x => x.GroupId, StringComparer.Ordinal)
                .ThenBy(x => x.OptionId, StringComparer.Ordinal)
                .ToArray()
        };

    private static BookingStatusAdvancedV1 Normalize(BookingStatusAdvancedV1 p) =>
        p with
        {
            FromStatus = CleanRequired(p.FromStatus),
            ToStatus = CleanRequired(p.ToStatus),
            FromFulfillmentState = CleanRequired(p.FromFulfillmentState),
            ToFulfillmentState = CleanRequired(p.ToFulfillmentState),
            OccurredAtUtc = p.OccurredAtUtc.ToUniversalTime()
        };

    private static BookingCancelledV1 Normalize(BookingCancelledV1 p) =>
        p with
        {
            FromStatus = CleanRequired(p.FromStatus),
            ToStatus = CleanRequired(p.ToStatus),
            FromFulfillmentState = CleanRequired(p.FromFulfillmentState),
            ToFulfillmentState = CleanRequired(p.ToFulfillmentState),
            Reason = CleanRequired(p.Reason),
            OccurredAtUtc = p.OccurredAtUtc.ToUniversalTime()
        };

    private static BookingDepositReceivedV1 Normalize(BookingDepositReceivedV1 p) =>
        p with
        {
            Reference = CleanRequired(p.Reference),
            CashMovementClass = CleanRequired(p.CashMovementClass),
            CashReferenceType = CleanRequired(p.CashReferenceType),
            OccurredAtUtc = p.OccurredAtUtc.ToUniversalTime()
        };

    private static BookingDepositRefundedV1 Normalize(BookingDepositRefundedV1 p) =>
        p with
        {
            Reason = CleanRequired(p.Reason),
            Reference = CleanRequired(p.Reference),
            CashMovementClass = CleanRequired(p.CashMovementClass),
            CashReferenceType = CleanRequired(p.CashReferenceType),
            OccurredAtUtc = p.OccurredAtUtc.ToUniversalTime(),
            Allocations = (p.Allocations ?? Array.Empty<BookingDepositAllocationV1>())
                .OrderBy(x => x.ReceivedDepositFactId)
                .ToArray()
        };

    private static string? Validate(BookingRevisionCommittedV1 p)
    {
        if (p.OperationId == Guid.Empty || p.BookingId == Guid.Empty ||
            p.StoreId == Guid.Empty || p.ActorUserId == Guid.Empty ||
            p.ResultingVersion != checked(p.ExpectedVersion + 1) ||
            p.ExpectedVersion < 0)
            return "Revision contract identities/version are invalid.";

        if (p.ConfigurationAction is not (CafeBookingOperationContract.Created or CafeBookingOperationContract.Revised))
            return "ConfigurationAction must be Created or Revised.";

        if (p.ConfigurationAction == CafeBookingOperationContract.Created &&
            (p.ExpectedVersion != 0 || p.FromStatus is not null ||
             p.ToStatus != "Draft"))
            return "Created Booking revision must establish Draft from version zero.";

        if (p.ConfigurationAction == CafeBookingOperationContract.Revised &&
            (p.ExpectedVersion <= 0 ||
             p.FromStatus is not ("Draft" or "Confirmed") ||
             p.ToStatus != "Draft"))
            return "Revised Booking must originate from Draft/Confirmed and return to Draft.";

        if (p.FromFulfillmentState != "NotStarted" ||
            p.ToFulfillmentState != "NotStarted")
            return "C9B revision fulfillment state must remain NotStarted.";

        return Validate(p.Revision);
    }

    private static string? Validate(BookingRevisionSnapshotV1 r)
    {
        if (r.RevisionId == Guid.Empty || r.BookingId == Guid.Empty ||
            r.RevisionNumber <= 0 || r.BookingVersion <= 0 ||
            r.CustomerId == Guid.Empty || r.ActorUserId == Guid.Empty ||
            string.IsNullOrWhiteSpace(r.CustomerNameSnapshot) ||
            string.IsNullOrWhiteSpace(r.EventName) ||
            string.IsNullOrWhiteSpace(r.MenuPublicationRevisionId) ||
            string.IsNullOrWhiteSpace(r.ContentHash) ||
            r.ContentHash.Length != 64 ||
            r.QuotedTotalCents < 0 ||
            r.DepositRequirementCents < 0 ||
            r.DepositRequirementCents > r.QuotedTotalCents ||
            r.Lines is null || r.Lines.Count == 0)
            return "Booking revision snapshot is incomplete.";

        if (r.BookingMode is not ("Event" or "CoffeeCart") ||
            r.IntendedServiceMode is not ("DineIn" or "Takeout"))
            return "Booking mode/service mode is invalid.";

        if (r.BookingMode == "CoffeeCart" &&
            (!r.EventEndUtc.HasValue || string.IsNullOrWhiteSpace(r.Location) ||
             r.IntendedServiceMode != "Takeout"))
            return "CoffeeCart requires event end, location and Takeout.";

        HashSet<Guid> lineIds = new();
        long total = 0;
        foreach (BookingLineSnapshotV1 line in r.Lines)
        {
            if (line.BookingLineId == Guid.Empty || line.ProductId == Guid.Empty ||
                !lineIds.Add(line.BookingLineId) || line.QuantityX1000 <= 0 ||
                line.BaseUnitPriceCents < 0 || line.QuotedUnitPriceCents < 0 ||
                line.LineTotalCents < 0)
                return "Booking line snapshot is invalid.";

            bool noVariant =
                line.VariantId is null &&
                line.VariantNameSnapshot is null &&
                !line.VariantUnitPriceCents.HasValue;
            bool withVariant =
                !string.IsNullOrWhiteSpace(line.VariantId) &&
                !string.IsNullOrWhiteSpace(line.VariantNameSnapshot) &&
                line.VariantUnitPriceCents.HasValue &&
                line.VariantUnitPriceCents.Value >= 0;
            if (!noVariant && !withVariant)
                return "Booking variant snapshot must be fully absent or populated.";

            long expectedUnit = checked(
                (line.VariantUnitPriceCents ?? line.BaseUnitPriceCents) +
                (line.ModifierSelections ?? Array.Empty<BookingModifierSelectionV1>())
                    .Sum(x => x.PriceDeltaCents));
            if (expectedUnit != line.QuotedUnitPriceCents || expectedUnit < 0)
                return "Booking unit quote does not match immutable package evidence.";

            long expectedLine = checked((long)Math.Round(
                expectedUnit * (line.QuantityX1000 / 1000m),
                0, MidpointRounding.AwayFromZero));
            if (expectedLine != line.LineTotalCents)
                return "Booking line total does not match quantity and quoted unit price.";

            total = checked(total + line.LineTotalCents);
        }

        return total == r.QuotedTotalCents
            ? null
            : "Booking quoted total does not match immutable line totals.";
    }

    private static string? Validate(BookingStatusAdvancedV1 p)
    {
        if (p.OperationId == Guid.Empty || p.BookingId == Guid.Empty ||
            p.BookingRevisionId == Guid.Empty || p.StoreId == Guid.Empty ||
            p.ActorUserId == Guid.Empty || p.ExpectedVersion <= 0 ||
            p.ResultingVersion != checked(p.ExpectedVersion + 1) ||
            p.FromFulfillmentState != "NotStarted" ||
            p.ToFulfillmentState != "NotStarted")
            return "Booking status contract identity/version/state is invalid.";

        bool confirm = p.FromStatus == "Draft" && p.ToStatus == "Confirmed";
        bool ready = p.FromStatus == "Confirmed" && p.ToStatus == "ReadyForFulfillment";
        return confirm || ready ? null : "C9B status transition is not supported.";
    }

    private static string? Validate(BookingCancelledV1 p)
    {
        if (p.OperationId == Guid.Empty || p.BookingId == Guid.Empty ||
            p.BookingRevisionId == Guid.Empty || p.StoreId == Guid.Empty ||
            p.ActorUserId == Guid.Empty || p.ExpectedVersion <= 0 ||
            p.ResultingVersion != checked(p.ExpectedVersion + 1) ||
            p.ToStatus != "Cancelled" ||
            p.FromStatus is not ("Draft" or "Confirmed" or "ReadyForFulfillment") ||
            string.IsNullOrWhiteSpace(p.Reason))
            return "C9 cancellation contract identity/state is invalid.";

        bool preOrder =
            p.FromFulfillmentState == "NotStarted" &&
            p.ToFulfillmentState == "NotStarted";

        bool orderBound =
            p.FromStatus == "ReadyForFulfillment" &&
            p.FromFulfillmentState == "OrderBound" &&
            p.ToFulfillmentState == "OrderBound";

        return preOrder || orderBound
            ? null
            : "C9 cancellation may preserve NotStarted or an already-cancelled OrderBound authority.";
    }

    private static string? Validate(BookingDepositReceivedV1 p)
    {
        if (p.OperationId == Guid.Empty || p.DepositFactId == Guid.Empty ||
            p.BookingId == Guid.Empty || p.BookingRevisionId == Guid.Empty ||
            p.StoreId == Guid.Empty || p.ActorUserId == Guid.Empty ||
            p.CashierSessionId == Guid.Empty || p.CashMovementId == Guid.Empty ||
            p.AmountCents <= 0 || string.IsNullOrWhiteSpace(p.Reference) ||
            p.CashMovementClass != CafeBookingOperationContract.CashIn ||
            p.CashDeltaCents != p.AmountCents ||
            p.CashReferenceType != CafeBookingOperationContract.BookingDepositReceiptReference ||
            p.CashReferenceId != p.DepositFactId)
            return "Booking deposit received contract is invalid.";

        return null;
    }

    private static string? Validate(BookingDepositRefundedV1 p)
    {
        if (p.OperationId == Guid.Empty || p.DepositFactId == Guid.Empty ||
            p.BookingId == Guid.Empty || p.BookingRevisionId == Guid.Empty ||
            p.StoreId == Guid.Empty || p.ActorUserId == Guid.Empty ||
            p.CashierSessionId == Guid.Empty || p.CashMovementId == Guid.Empty ||
            p.AmountCents <= 0 || string.IsNullOrWhiteSpace(p.Reason) ||
            string.IsNullOrWhiteSpace(p.Reference) ||
            p.CashMovementClass != CafeBookingOperationContract.CashOut ||
            p.CashDeltaCents != -p.AmountCents ||
            p.CashReferenceType != CafeBookingOperationContract.BookingDepositRefundReference ||
            p.CashReferenceId != p.DepositFactId ||
            p.Allocations is null || p.Allocations.Count == 0 ||
            p.Allocations.Any(x => x.ReceivedDepositFactId == Guid.Empty || x.AmountCents <= 0) ||
            p.Allocations.Select(x => x.ReceivedDepositFactId).Distinct().Count() != p.Allocations.Count ||
            p.Allocations.Sum(x => x.AmountCents) != p.AmountCents)
            return "Booking deposit refunded contract/allocation authority is invalid.";

        return null;
    }

    private static string CleanRequired(string? value) =>
        (value ?? string.Empty).Trim();

    private static string? CleanOptional(string? value)
    {
        string cleaned = (value ?? string.Empty).Trim();
        return cleaned.Length == 0 ? null : cleaned;
    }
}
