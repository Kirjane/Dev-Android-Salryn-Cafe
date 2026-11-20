using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Salryn.Sync.Contracts;

public sealed record CafeCashBootstrapV1(
    Guid CentralInstanceId,
    Guid StoreId,
    long BaselineServerSequence,
    Guid SnapshotId,
    string SnapshotHash,
    long ManualMovementCount,
    long ReversalCount,
    long NetDeltaCents,
    string AuthorityDigest);

public static class CafeCashBootstrapProtocolV1
{
    private static readonly Regex LowerHexSha256 =
        new("^[0-9a-f]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static CafeCashBootstrapV1 Create(
        Guid centralInstanceId,
        Guid storeId,
        long baselineServerSequence,
        long manualMovementCount,
        long reversalCount,
        long netDeltaCents,
        string authorityDigest)
    {
        if (centralInstanceId == Guid.Empty ||
            storeId == Guid.Empty ||
            baselineServerSequence < 0 ||
            manualMovementCount < 0 ||
            reversalCount < 0 ||
            reversalCount > manualMovementCount ||
            string.IsNullOrWhiteSpace(authorityDigest) ||
            !LowerHexSha256.IsMatch(authorityDigest))
        {
            throw new ArgumentException(
                "C6 cash bootstrap requires valid identity, counts and lowercase SHA-256 authority digest.");
        }

        string canonical = JsonSerializer.Serialize(new
        {
            centralInstanceId = centralInstanceId.ToString("D"),
            storeId = storeId.ToString("D"),
            baselineServerSequence,
            manualMovementCount,
            reversalCount,
            netDeltaCents,
            authorityDigest
        }, Json);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        string snapshotHash = Convert.ToHexString(hash).ToLowerInvariant();
        byte[] idBytes = hash[..16];
        idBytes[6] = (byte)((idBytes[6] & 0x0f) | 0x50);
        idBytes[8] = (byte)((idBytes[8] & 0x3f) | 0x80);

        return new CafeCashBootstrapV1(
            centralInstanceId,
            storeId,
            baselineServerSequence,
            new Guid(idBytes),
            snapshotHash,
            manualMovementCount,
            reversalCount,
            netDeltaCents,
            authorityDigest);
    }

    public static bool Validate(CafeCashBootstrapV1 snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        try
        {
            CafeCashBootstrapV1 expected = Create(
                snapshot.CentralInstanceId,
                snapshot.StoreId,
                snapshot.BaselineServerSequence,
                snapshot.ManualMovementCount,
                snapshot.ReversalCount,
                snapshot.NetDeltaCents,
                snapshot.AuthorityDigest);

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
}
