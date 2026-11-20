using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Salryn.Sync.Contracts;

public sealed record CafeInventoryBootstrapSupplierV1(
    Guid SupplierId,
    string Name,
    string? ContactNumber,
    string? Notes,
    bool IsActive,
    int DisplayOrder);

public sealed record CafeInventorySupplierPublicationV1(
    Guid RevisionId,
    Guid StoreId,
    int RevisionNumber,
    string ContentHash,
    string PublicationHash,
    DateTimeOffset PublishedAtUtc,
    IReadOnlyList<CafeInventoryBootstrapSupplierV1> Suppliers);

public sealed record CafeInventoryBootstrapV1(
    Guid CentralInstanceId,
    Guid StoreId,
    long BaselineServerSequence,
    Guid SnapshotId,
    string SnapshotHash,
    CafeInventorySupplierPublicationV1? SupplierDirectory);

public static class CafeInventoryBootstrapProtocolV1
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static CafeInventoryBootstrapV1 Create(
        Guid centralInstanceId,
        Guid storeId,
        long baselineServerSequence,
        CafeInventorySupplierPublicationV1? supplierDirectory)
    {
        if (centralInstanceId == Guid.Empty ||
            storeId == Guid.Empty ||
            baselineServerSequence < 0)
        {
            throw new ArgumentException(
                "Cafe inventory bootstrap requires central/store identity and non-negative baseline.");
        }

        CafeInventorySupplierPublicationV1? normalized = Normalize(supplierDirectory);
        string canonical = JsonSerializer.Serialize(new
        {
            centralInstanceId = centralInstanceId.ToString("D"),
            storeId = storeId.ToString("D"),
            baselineServerSequence,
            supplierDirectory = normalized
        }, Json);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        string snapshotHash = Convert.ToHexString(hash).ToLowerInvariant();

        Span<byte> idBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(idBytes);
        idBytes[6] = (byte)((idBytes[6] & 0x0f) | 0x50);
        idBytes[8] = (byte)((idBytes[8] & 0x3f) | 0x80);

        return new CafeInventoryBootstrapV1(
            centralInstanceId,
            storeId,
            baselineServerSequence,
            new Guid(idBytes),
            snapshotHash,
            normalized);
    }

    public static bool Validate(CafeInventoryBootstrapV1 snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        CafeInventoryBootstrapV1 expected = Create(
            snapshot.CentralInstanceId,
            snapshot.StoreId,
            snapshot.BaselineServerSequence,
            snapshot.SupplierDirectory);

        return expected.SnapshotId == snapshot.SnapshotId &&
            string.Equals(expected.SnapshotHash, snapshot.SnapshotHash, StringComparison.Ordinal);
    }

    private static CafeInventorySupplierPublicationV1? Normalize(
        CafeInventorySupplierPublicationV1? publication)
    {
        if (publication is null)
        {
            return null;
        }

        return publication with
        {
            Suppliers = publication.Suppliers
                .OrderBy(x => x.DisplayOrder)
                .ThenBy(x => x.SupplierId)
                .ToArray()
        };
    }
}
