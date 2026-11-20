using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Salryn.Sync.Contracts;

public sealed record CafeFinancialBootstrapV1(
    Guid CentralInstanceId,
    Guid StoreId,
    long BaselineServerSequence,
    Guid SnapshotId,
    string SnapshotHash,
    long FinancialDecisionCount,
    long RefundCount,
    long NonTerminalRefundCount,
    string AuthorityDigest);

public static class CafeFinancialBootstrapProtocolV1
{
    private static readonly Regex LowerHexSha256 =
        new("^[0-9a-f]{64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static CafeFinancialBootstrapV1 Create(
        Guid centralInstanceId,
        Guid storeId,
        long baselineServerSequence,
        long financialDecisionCount,
        long refundCount,
        long nonTerminalRefundCount,
        string authorityDigest)
    {
        if (centralInstanceId == Guid.Empty ||
            storeId == Guid.Empty ||
            baselineServerSequence < 0 ||
            financialDecisionCount < 0 ||
            refundCount < 0 ||
            nonTerminalRefundCount < 0 ||
            nonTerminalRefundCount > refundCount ||
            string.IsNullOrWhiteSpace(authorityDigest) ||
            !LowerHexSha256.IsMatch(authorityDigest))
        {
            throw new ArgumentException(
                "C5 financial bootstrap requires valid identity, counts and lowercase SHA-256 authority digest.");
        }

        string canonical = JsonSerializer.Serialize(new
        {
            centralInstanceId = centralInstanceId.ToString("D"),
            storeId = storeId.ToString("D"),
            baselineServerSequence,
            financialDecisionCount,
            refundCount,
            nonTerminalRefundCount,
            authorityDigest
        }, Json);

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        string snapshotHash = Convert.ToHexString(hash).ToLowerInvariant();
        byte[] idBytes = hash[..16];
        idBytes[6] = (byte)((idBytes[6] & 0x0f) | 0x50);
        idBytes[8] = (byte)((idBytes[8] & 0x3f) | 0x80);

        return new CafeFinancialBootstrapV1(
            centralInstanceId,
            storeId,
            baselineServerSequence,
            new Guid(idBytes),
            snapshotHash,
            financialDecisionCount,
            refundCount,
            nonTerminalRefundCount,
            authorityDigest);
    }

    public static bool Validate(CafeFinancialBootstrapV1 snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        try
        {
            CafeFinancialBootstrapV1 expected = Create(
                snapshot.CentralInstanceId,
                snapshot.StoreId,
                snapshot.BaselineServerSequence,
                snapshot.FinancialDecisionCount,
                snapshot.RefundCount,
                snapshot.NonTerminalRefundCount,
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
