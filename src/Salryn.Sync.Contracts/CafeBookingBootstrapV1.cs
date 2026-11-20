using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Salryn.Sync.Contracts;

public sealed record CafeBookingBootstrapBookingV1(
    Guid BookingId, Guid StoreId, int CurrentVersion, Guid CurrentRevisionId,
    string Status, string FulfillmentState, Guid CreatedByUserId, string CreatedAtUtc,
    Guid UpdatedByUserId, string UpdatedAtUtc, Guid? CancelledByUserId,
    string? CancelledAtUtc, string? CancellationReason);

public sealed record CafeBookingBootstrapRevisionV1(
    Guid RevisionId, Guid BookingId, int RevisionNumber, int BookingVersion,
    Guid? PreviousRevisionId, Guid CustomerId, string CustomerNameSnapshot,
    string? CustomerPhoneSnapshot, string? CustomerAddressSnapshot, string BookingMode,
    string EventName, string? EventReference, string EventStartUtc, string? EventEndUtc,
    string? Location, string? SetupAtUtc, string? ServiceWindowStartUtc,
    string? ServiceWindowEndUtc, int? Headcount, string? PackageLabel,
    string MenuPublicationRevisionId, long QuotedTotalCents, long DepositRequirementCents,
    string? SpecialInstructions, string? Notes, string FulfillmentServiceMode,
    Guid ActorUserId, string CreatedAtUtc, string ContentHash);

public sealed record CafeBookingBootstrapLineV1(
    Guid RevisionId, Guid BookingLineId, Guid ProductId, string ProductNameSnapshot,
    string UnitNameSnapshot, long QuantityX1000, long BaseUnitPriceCents,
    string? VariantId, string? VariantNameSnapshot, long? VariantUnitPriceCents,
    long QuotedUnitPriceCents, long LineTotalCents, int DisplayOrder, string? LineNote);

public sealed record CafeBookingBootstrapModifierV1(
    Guid RevisionId, Guid BookingLineId, string GroupId, string GroupNameSnapshot,
    string OptionId, string OptionNameSnapshot, long PriceDeltaCents, int DisplayOrder);

public sealed record CafeBookingBootstrapOperationV1(
    Guid OperationId, Guid BookingId, Guid DeviceId, Guid StoreId, string OperationType,
    string PayloadHash, int ExpectedVersion, int ResultingVersion, Guid? ResultRevisionId,
    string ResultStatus, string ResultFulfillmentState, Guid ActorUserId, string OccurredAtUtc);

public sealed record CafeBookingBootstrapEventV1(
    Guid EventId, Guid OperationId, Guid BookingId, int ExpectedVersion, int ResultingVersion,
    string Action, string? FromStatus, string ToStatus, string? FromFulfillmentState,
    string ToFulfillmentState, Guid? BookingRevisionId, Guid? OrderId, Guid? SaleId,
    Guid ActorUserId, string OccurredAtUtc, string? Reason, string ContentHash);

public sealed record CafeBookingBootstrapDepositFactV1(
    Guid DepositFactId, Guid OperationId, Guid BookingId, Guid StoreId,
    Guid BookingRevisionId, string FactType, Guid ActorUserId, long AmountCents,
    string OccurredAtUtc, string Reference, Guid? CashierSessionId,
    Guid? CashMovementId, Guid? SaleId, string PayloadHash);

public sealed record CafeBookingBootstrapDepositAllocationV1(
    Guid ActionFactId, Guid ReceivedFactId, long AmountCents);

public sealed record CafeBookingBootstrapOrderLinkV1(
    Guid BookingOrderLinkId, Guid BookingId, Guid BookingRevisionId, Guid OrderId,
    Guid OperationId, Guid ActorUserId, string OccurredAtUtc, string ContentHash);

public sealed record CafeBookingBootstrapSaleLinkV1(
    Guid BookingSaleLinkId, Guid BookingId, Guid BookingRevisionId, Guid BookingOrderLinkId,
    Guid OrderId, Guid SaleId, Guid? DepositApplicationFactId, Guid OperationId,
    Guid ActorUserId, string OccurredAtUtc, string ContentHash);

public sealed record CafeBookingBootstrapAuthorityV1(
    IReadOnlyList<CafeBookingBootstrapBookingV1> Bookings,
    IReadOnlyList<CafeBookingBootstrapRevisionV1> Revisions,
    IReadOnlyList<CafeBookingBootstrapLineV1> Lines,
    IReadOnlyList<CafeBookingBootstrapModifierV1> ModifierSelections,
    IReadOnlyList<CafeBookingBootstrapOperationV1> Operations,
    IReadOnlyList<CafeBookingBootstrapEventV1> Events,
    IReadOnlyList<CafeBookingBootstrapDepositFactV1> DepositFacts,
    IReadOnlyList<CafeBookingBootstrapDepositAllocationV1> DepositAllocations,
    IReadOnlyList<CafeBookingBootstrapOrderLinkV1> OrderLinks,
    IReadOnlyList<CafeBookingBootstrapSaleLinkV1> SaleLinks);

public sealed record CafeBookingBootstrapV1(
    Guid CentralInstanceId, Guid StoreId, long BaselineServerSequence,
    Guid SnapshotId, string SnapshotHash,
    long BookingCount, long RevisionCount, long LineCount, long ModifierSelectionCount,
    long OperationCount, long EventCount, long DepositFactCount, long DepositAllocationCount,
    long OrderLinkCount, long SaleLinkCount, string AuthorityDigest,
    CafeBookingBootstrapAuthorityV1 Authority);

public static class CafeBookingBootstrapProtocolV1
{
    private static readonly Regex LowerHexSha256 =
        new("^[0-9a-f]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static CafeBookingBootstrapV1 Create(
        Guid centralInstanceId,
        Guid storeId,
        long baselineServerSequence,
        CafeBookingBootstrapAuthorityV1 authority)
    {
        if (centralInstanceId == Guid.Empty || storeId == Guid.Empty || baselineServerSequence < 0)
            throw new ArgumentException("C9 Booking bootstrap requires central/store identity and a non-negative baseline.");

        CafeBookingBootstrapAuthorityV1 normalized =
            Normalize(authority ?? throw new ArgumentNullException(nameof(authority)));
        ValidateAuthority(normalized, storeId);

        string authorityDigest =
            Sha256(JsonSerializer.Serialize(normalized, Json));

        string snapshotHash = Sha256(JsonSerializer.Serialize(new
        {
            centralInstanceId = centralInstanceId.ToString("D"),
            storeId = storeId.ToString("D"),
            baselineServerSequence,
            bookingCount = normalized.Bookings.Count,
            revisionCount = normalized.Revisions.Count,
            lineCount = normalized.Lines.Count,
            modifierSelectionCount = normalized.ModifierSelections.Count,
            operationCount = normalized.Operations.Count,
            eventCount = normalized.Events.Count,
            depositFactCount = normalized.DepositFacts.Count,
            depositAllocationCount = normalized.DepositAllocations.Count,
            orderLinkCount = normalized.OrderLinks.Count,
            saleLinkCount = normalized.SaleLinks.Count,
            authorityDigest
        }, Json));

        byte[] hash = Convert.FromHexString(snapshotHash);
        byte[] idBytes = hash[..16];
        idBytes[6] = (byte)((idBytes[6] & 0x0f) | 0x50);
        idBytes[8] = (byte)((idBytes[8] & 0x3f) | 0x80);

        return new CafeBookingBootstrapV1(
            centralInstanceId, storeId, baselineServerSequence, new Guid(idBytes), snapshotHash,
            normalized.Bookings.Count, normalized.Revisions.Count, normalized.Lines.Count,
            normalized.ModifierSelections.Count, normalized.Operations.Count, normalized.Events.Count,
            normalized.DepositFacts.Count, normalized.DepositAllocations.Count,
            normalized.OrderLinks.Count, normalized.SaleLinks.Count, authorityDigest, normalized);
    }

    public static bool Validate(CafeBookingBootstrapV1 snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        try
        {
            if (!LowerHexSha256.IsMatch(snapshot.SnapshotHash) ||
                !LowerHexSha256.IsMatch(snapshot.AuthorityDigest))
                return false;

            CafeBookingBootstrapV1 expected =
                Create(snapshot.CentralInstanceId, snapshot.StoreId,
                    snapshot.BaselineServerSequence, snapshot.Authority);

            return expected.SnapshotId == snapshot.SnapshotId &&
                expected.SnapshotHash == snapshot.SnapshotHash &&
                expected.AuthorityDigest == snapshot.AuthorityDigest &&
                expected.BookingCount == snapshot.BookingCount &&
                expected.RevisionCount == snapshot.RevisionCount &&
                expected.LineCount == snapshot.LineCount &&
                expected.ModifierSelectionCount == snapshot.ModifierSelectionCount &&
                expected.OperationCount == snapshot.OperationCount &&
                expected.EventCount == snapshot.EventCount &&
                expected.DepositFactCount == snapshot.DepositFactCount &&
                expected.DepositAllocationCount == snapshot.DepositAllocationCount &&
                expected.OrderLinkCount == snapshot.OrderLinkCount &&
                expected.SaleLinkCount == snapshot.SaleLinkCount;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static CafeBookingBootstrapAuthorityV1 Normalize(
        CafeBookingBootstrapAuthorityV1 authority) =>
        new(
            (authority.Bookings ?? Array.Empty<CafeBookingBootstrapBookingV1>())
                .OrderBy(x => x.BookingId).ToArray(),
            (authority.Revisions ?? Array.Empty<CafeBookingBootstrapRevisionV1>())
                .OrderBy(x => x.BookingId).ThenBy(x => x.BookingVersion).ThenBy(x => x.RevisionId).ToArray(),
            (authority.Lines ?? Array.Empty<CafeBookingBootstrapLineV1>())
                .OrderBy(x => x.RevisionId).ThenBy(x => x.DisplayOrder).ThenBy(x => x.BookingLineId).ToArray(),
            (authority.ModifierSelections ?? Array.Empty<CafeBookingBootstrapModifierV1>())
                .OrderBy(x => x.RevisionId).ThenBy(x => x.BookingLineId).ThenBy(x => x.DisplayOrder)
                .ThenBy(x => x.GroupId, StringComparer.Ordinal).ThenBy(x => x.OptionId, StringComparer.Ordinal).ToArray(),
            (authority.Operations ?? Array.Empty<CafeBookingBootstrapOperationV1>())
                .OrderBy(x => x.BookingId).ThenBy(x => x.ResultingVersion).ThenBy(x => x.OperationId).ToArray(),
            (authority.Events ?? Array.Empty<CafeBookingBootstrapEventV1>())
                .OrderBy(x => x.BookingId).ThenBy(x => x.ResultingVersion)
                .ThenBy(x => x.OccurredAtUtc, StringComparer.Ordinal).ThenBy(x => x.EventId).ToArray(),
            (authority.DepositFacts ?? Array.Empty<CafeBookingBootstrapDepositFactV1>())
                .OrderBy(x => x.BookingId).ThenBy(x => x.OccurredAtUtc, StringComparer.Ordinal)
                .ThenBy(x => x.DepositFactId).ToArray(),
            (authority.DepositAllocations ?? Array.Empty<CafeBookingBootstrapDepositAllocationV1>())
                .OrderBy(x => x.ActionFactId).ThenBy(x => x.ReceivedFactId).ToArray(),
            (authority.OrderLinks ?? Array.Empty<CafeBookingBootstrapOrderLinkV1>())
                .OrderBy(x => x.BookingId).ThenBy(x => x.BookingOrderLinkId).ToArray(),
            (authority.SaleLinks ?? Array.Empty<CafeBookingBootstrapSaleLinkV1>())
                .OrderBy(x => x.BookingId).ThenBy(x => x.BookingSaleLinkId).ToArray());

    private static void ValidateAuthority(CafeBookingBootstrapAuthorityV1 authority, Guid storeId)
    {
        if (authority.Bookings.Any(x => x.BookingId == Guid.Empty || x.StoreId != storeId ||
                x.CurrentVersion <= 0 || x.CurrentRevisionId == Guid.Empty) ||
            authority.Revisions.Any(x => x.RevisionId == Guid.Empty || x.BookingId == Guid.Empty ||
                x.CustomerId == Guid.Empty || x.RevisionNumber <= 0 || x.BookingVersion <= 0 ||
                string.IsNullOrWhiteSpace(x.MenuPublicationRevisionId) ||
                !LowerHexSha256.IsMatch(x.ContentHash)) ||
            authority.Lines.Any(x => x.RevisionId == Guid.Empty || x.BookingLineId == Guid.Empty ||
                x.ProductId == Guid.Empty || x.QuantityX1000 <= 0) ||
            authority.Operations.Any(x => x.OperationId == Guid.Empty || x.BookingId == Guid.Empty ||
                x.StoreId != storeId || !LowerHexSha256.IsMatch(x.PayloadHash)) ||
            authority.Events.Any(x => x.EventId == Guid.Empty || x.OperationId == Guid.Empty ||
                x.BookingId == Guid.Empty || !LowerHexSha256.IsMatch(x.ContentHash)) ||
            authority.DepositFacts.Any(x => x.DepositFactId == Guid.Empty || x.OperationId == Guid.Empty ||
                x.BookingId == Guid.Empty || x.StoreId != storeId || x.AmountCents <= 0 ||
                !LowerHexSha256.IsMatch(x.PayloadHash)) ||
            authority.DepositAllocations.Any(x => x.ActionFactId == Guid.Empty ||
                x.ReceivedFactId == Guid.Empty || x.AmountCents <= 0) ||
            authority.OrderLinks.Any(x => x.BookingOrderLinkId == Guid.Empty || x.BookingId == Guid.Empty ||
                x.OrderId == Guid.Empty || !LowerHexSha256.IsMatch(x.ContentHash)) ||
            authority.SaleLinks.Any(x => x.BookingSaleLinkId == Guid.Empty || x.BookingId == Guid.Empty ||
                x.OrderId == Guid.Empty || x.SaleId == Guid.Empty || !LowerHexSha256.IsMatch(x.ContentHash)))
            throw new ArgumentException("C9 Booking bootstrap contains invalid retained authority.");

        if (authority.Bookings.Select(x => x.BookingId).Distinct().Count() != authority.Bookings.Count ||
            authority.Revisions.Select(x => x.RevisionId).Distinct().Count() != authority.Revisions.Count ||
            authority.Operations.Select(x => x.OperationId).Distinct().Count() != authority.Operations.Count ||
            authority.Events.Select(x => x.EventId).Distinct().Count() != authority.Events.Count ||
            authority.DepositFacts.Select(x => x.DepositFactId).Distinct().Count() != authority.DepositFacts.Count ||
            authority.OrderLinks.Select(x => x.BookingOrderLinkId).Distinct().Count() != authority.OrderLinks.Count ||
            authority.SaleLinks.Select(x => x.BookingSaleLinkId).Distinct().Count() != authority.SaleLinks.Count)
            throw new ArgumentException("C9 Booking bootstrap contains duplicate retained identities.");
    }

    private static string Sha256(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
