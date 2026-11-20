using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Salryn.Sync.Contracts;

public sealed record CafeOrderBootstrapModifierV1(
    string GroupId,
    string GroupNameSnapshot,
    string OptionId,
    string OptionNameSnapshot,
    long PriceDeltaCents,
    int DisplayOrder);

public sealed record CafeOrderBootstrapLineV1(
    Guid OrderLineId,
    Guid ProductId,
    string ProductNameSnapshot,
    string UnitNameSnapshot,
    long QuantityX1000,
    long BaseUnitPriceCents,
    string? VariantId,
    string? VariantNameSnapshot,
    long? VariantUnitPriceCents,
    IReadOnlyList<CafeOrderBootstrapModifierV1> ModifierSelections,
    string? Note,
    long CalculatedUnitPriceCents,
    long LineTotalCents,
    int DisplayOrder);

public sealed record CafeOrderBootstrapOrderV1(
    Guid OrderId,
    int CurrentVersion,
    Guid CurrentRevisionId,
    int RevisionNumber,
    int RevisionOrderVersion,
    Guid? PreviousRevisionId,
    string MenuPublicationRevisionId,
    string State,
    string ServiceMode,
    string? ServiceReference,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    Guid RevisionActorUserId,
    DateTimeOffset RevisionCreatedAtUtc,
    Guid? SubmittedRevisionId,
    string? SubmittedRecipeBookRevisionId,
    string RevisionContentHash,
    IReadOnlyList<CafeOrderBootstrapLineV1> Lines);

public sealed record CafeOrderBootstrapV1(
    Guid CentralInstanceId,
    Guid StoreId,
    long BaselineServerSequence,
    Guid SnapshotId,
    string SnapshotHash,
    IReadOnlyList<CafeOrderBootstrapOrderV1> Orders);

public static class CafeOrderBootstrapProtocolV1
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static CafeOrderBootstrapV1 Create(
        Guid centralInstanceId,
        Guid storeId,
        long baselineServerSequence,
        IReadOnlyList<CafeOrderBootstrapOrderV1> orders)
    {
        if (centralInstanceId == Guid.Empty ||
            storeId == Guid.Empty ||
            baselineServerSequence < 0)
        {
            throw new ArgumentException(
                "Cafe order bootstrap requires central/store identity and a non-negative baseline.");
        }

        CafeOrderBootstrapOrderV1[] normalized =
            (orders ?? Array.Empty<CafeOrderBootstrapOrderV1>())
            .Select(Normalize)
            .OrderBy(x => x.OrderId)
            .ToArray();

        foreach (CafeOrderBootstrapOrderV1 order in normalized)
        {
            if (order.OrderId == Guid.Empty ||
                order.CurrentVersion <= 0 ||
                order.CurrentRevisionId == Guid.Empty ||
                order.RevisionNumber <= 0 ||
                order.RevisionOrderVersion <= 0 ||
                order.RevisionOrderVersion > order.CurrentVersion ||
                order.RevisionActorUserId == Guid.Empty ||
                string.IsNullOrWhiteSpace(order.MenuPublicationRevisionId) ||
                order.State is "Completed" or "Cancelled" ||
                order.State is not ("Open" or "Parked" or "Submitted" or "Preparing" or "Ready") ||
                order.ServiceMode is not ("DineIn" or "Takeout") ||
                order.Lines.Count == 0)
            {
                throw new ArgumentException(
                    "Cafe order bootstrap may contain only valid non-terminal operational orders.");
            }
        }

        string canonical = JsonSerializer.Serialize(new
        {
            centralInstanceId = centralInstanceId.ToString("D"),
            storeId = storeId.ToString("D"),
            baselineServerSequence,
            orders = normalized
        }, Json);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        string snapshotHash = Convert.ToHexString(hash).ToLowerInvariant();

        byte[] idBytes = hash[..16];
        idBytes[6] = (byte)((idBytes[6] & 0x0f) | 0x50);
        idBytes[8] = (byte)((idBytes[8] & 0x3f) | 0x80);

        return new CafeOrderBootstrapV1(
            centralInstanceId,
            storeId,
            baselineServerSequence,
            new Guid(idBytes),
            snapshotHash,
            normalized);
    }

    public static bool Validate(CafeOrderBootstrapV1 snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        try
        {
            CafeOrderBootstrapV1 expected = Create(
                snapshot.CentralInstanceId,
                snapshot.StoreId,
                snapshot.BaselineServerSequence,
                snapshot.Orders);

            return expected.SnapshotId == snapshot.SnapshotId &&
                string.Equals(
                    expected.SnapshotHash,
                    snapshot.SnapshotHash,
                    StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static CafeOrderBootstrapOrderV1 Normalize(
        CafeOrderBootstrapOrderV1 order) =>
        order with
        {
            CreatedAtUtc = order.CreatedAtUtc.ToUniversalTime(),
            UpdatedAtUtc = order.UpdatedAtUtc.ToUniversalTime(),
            RevisionCreatedAtUtc = order.RevisionCreatedAtUtc.ToUniversalTime(),
            ServiceReference = Clean(order.ServiceReference),
            Lines = (order.Lines ?? Array.Empty<CafeOrderBootstrapLineV1>())
                .Select(line => line with
                {
                    Note = Clean(line.Note),
                    ModifierSelections =
                        (line.ModifierSelections ?? Array.Empty<CafeOrderBootstrapModifierV1>())
                        .OrderBy(x => x.DisplayOrder)
                        .ThenBy(x => x.GroupId, StringComparer.Ordinal)
                        .ThenBy(x => x.OptionId, StringComparer.Ordinal)
                        .ToArray()
                })
                .OrderBy(x => x.DisplayOrder)
                .ThenBy(x => x.OrderLineId)
                .ToArray()
        };

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
