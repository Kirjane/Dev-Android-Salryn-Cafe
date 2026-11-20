using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Salryn.Sync.Contracts;

public sealed record CafeDeliveryBootstrapV1(
    Guid CentralInstanceId,
    Guid StoreId,
    long BaselineServerSequence,
    Guid SnapshotId,
    string SnapshotHash,
    long DeliveryCount,
    long NonTerminalDeliveryCount,
    long RevisionCount,
    long OperationCount,
    long EventCount,
    string AuthorityDigest);

public static class CafeDeliveryBootstrapProtocolV1
{
    private static readonly Regex LowerHexSha256 =
        new(
            "^[0-9a-f]{64}$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static CafeDeliveryBootstrapV1 Create(
        Guid centralInstanceId,
        Guid storeId,
        long baselineServerSequence,
        long deliveryCount,
        long nonTerminalDeliveryCount,
        long revisionCount,
        long operationCount,
        long eventCount,
        string authorityDigest)
    {
        if (centralInstanceId == Guid.Empty ||
            storeId == Guid.Empty ||
            baselineServerSequence < 0 ||
            deliveryCount < 0 ||
            nonTerminalDeliveryCount < 0 ||
            nonTerminalDeliveryCount > deliveryCount ||
            revisionCount < 0 ||
            operationCount < 0 ||
            eventCount < 0 ||
            string.IsNullOrWhiteSpace(authorityDigest) ||
            !LowerHexSha256.IsMatch(authorityDigest))
        {
            throw new ArgumentException(
                "C8 delivery bootstrap requires valid identity, counts and lowercase SHA-256 authority digest.");
        }

        string canonical = JsonSerializer.Serialize(
            new
            {
                centralInstanceId = centralInstanceId.ToString("D"),
                storeId = storeId.ToString("D"),
                baselineServerSequence,
                deliveryCount,
                nonTerminalDeliveryCount,
                revisionCount,
                operationCount,
                eventCount,
                authorityDigest
            },
            Json);

        byte[] hash =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(canonical));
        string snapshotHash =
            Convert.ToHexString(hash).ToLowerInvariant();

        byte[] idBytes = hash[..16];
        idBytes[6] =
            (byte)((idBytes[6] & 0x0f) | 0x50);
        idBytes[8] =
            (byte)((idBytes[8] & 0x3f) | 0x80);

        return new CafeDeliveryBootstrapV1(
            centralInstanceId,
            storeId,
            baselineServerSequence,
            new Guid(idBytes),
            snapshotHash,
            deliveryCount,
            nonTerminalDeliveryCount,
            revisionCount,
            operationCount,
            eventCount,
            authorityDigest);
    }

    public static bool Validate(
        CafeDeliveryBootstrapV1 snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        try
        {
            CafeDeliveryBootstrapV1 expected =
                Create(
                    snapshot.CentralInstanceId,
                    snapshot.StoreId,
                    snapshot.BaselineServerSequence,
                    snapshot.DeliveryCount,
                    snapshot.NonTerminalDeliveryCount,
                    snapshot.RevisionCount,
                    snapshot.OperationCount,
                    snapshot.EventCount,
                    snapshot.AuthorityDigest);

            return
                expected.SnapshotId ==
                    snapshot.SnapshotId &&
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
}
