using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Salryn.Mobile.Foundation.P0;

namespace Salryn.Mobile.Foundation.Security;

public sealed record MobileSignedOfflineLease(
    Guid DeviceId,
    Guid TenantId,
    Guid StoreId,
    string ProductId,
    long ServerEpoch,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string SignatureBase64,
    string ServerPublicKeySpkiBase64);

public sealed record AntiRollbackWatermark(
    DateTimeOffset LastSeenUtc,
    long LastElapsedRealtimeMs,
    int LastBootCount,
    bool IsRevokedFenced);

public sealed record LeaseValidationResult(
    bool IsAuthorized,
    string Code,
    string Message)
{
    public static LeaseValidationResult Allowed() => new(true, "LEASE_VALID", "Offline lease is valid.");
    public static LeaseValidationResult Denied(string code, string message) => new(false, code, message);
}

/// <summary>
/// Verifies Central ECDSA P-256 signed offline leases and persists a tamper-resistant
/// anti-rollback watermark in hardware-backed IDeviceSecretStore (AndroidKeyStore on Android).
/// </summary>
public sealed class MobileOfflineLeaseGuard
{
    public const string CanonicalProductId = "Salryn.Cafe";
    private const string WatermarkSecretKey = "salryn.cafe.offline_lease_watermark.v1";
    private static readonly TimeSpan ClockRollbackTolerance = TimeSpan.FromMinutes(2);

    private readonly IDeviceSecretStore _secretStore;

    public MobileOfflineLeaseGuard(IDeviceSecretStore secretStore)
    {
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
    }

    public static bool VerifyLeaseSignature(MobileSignedOfflineLease lease)
    {
        if (lease is null ||
            !string.Equals(lease.ProductId, CanonicalProductId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(lease.ServerPublicKeySpkiBase64) ||
            string.IsNullOrWhiteSpace(lease.SignatureBase64))
        {
            return false;
        }

        string payload = $"{lease.DeviceId:D}|{lease.TenantId:D}|{lease.StoreId:D}|{lease.ProductId}|{lease.ServerEpoch}|{lease.IssuedAtUtc:O}|{lease.ExpiresAtUtc:O}";
        try
        {
            byte[] spki = Convert.FromBase64String(lease.ServerPublicKeySpkiBase64);
            byte[] sig = Convert.FromBase64String(lease.SignatureBase64);
            using ECDsa ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(spki, out _);
            return ecdsa.VerifyData(Encoding.UTF8.GetBytes(payload), sig, HashAlgorithmName.SHA256);
        }
        catch
        {
            return false;
        }
    }

    public async Task MarkRevokedFencedAsync(DateTimeOffset nowUtc, long elapsedMs, int bootCount, CancellationToken ct = default)
    {
        AntiRollbackWatermark wm = new(nowUtc, elapsedMs, bootCount, IsRevokedFenced: true);
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(wm));
        await _secretStore.WriteAsync(WatermarkSecretKey, bytes, ct).ConfigureAwait(false);
    }

    public async Task<LeaseValidationResult> EvaluateAndAdvanceAsync(
        MobileSignedOfflineLease? lease,
        Guid expectedDeviceId,
        Guid expectedTenantId,
        Guid expectedStoreId,
        DateTimeOffset nowUtc,
        long elapsedRealtimeMs,
        int bootCount,
        CancellationToken ct = default)
    {
        AntiRollbackWatermark? prior = await ReadWatermarkAsync(ct).ConfigureAwait(false);

        if (prior is { IsRevokedFenced: true })
        {
            return LeaseValidationResult.Denied("DEVICE_REVOKED_FENCED", "Device is revoked and fenced; offline mutations are locked.");
        }

        if (lease is null)
        {
            return LeaseValidationResult.Denied("LEASE_MISSING", "No signed offline lease present. Connect to Central to authenticate.");
        }

        if (!string.Equals(lease.ProductId, CanonicalProductId, StringComparison.Ordinal))
        {
            return LeaseValidationResult.Denied("ERR_WRONG_PRODUCT", "Offline lease belongs to another product.");
        }

        if (lease.DeviceId != expectedDeviceId || lease.TenantId != expectedTenantId || lease.StoreId != expectedStoreId)
        {
            return LeaseValidationResult.Denied("ERR_TENANT_SCOPE_VIOLATION", "Offline lease does not match enrolled device scope.");
        }

        if (!VerifyLeaseSignature(lease))
        {
            return LeaseValidationResult.Denied("LEASE_SIGNATURE_INVALID", "Offline lease cryptographic signature failed verification.");
        }

        // Initial clock-behind-IssuedAtUtc check (prevents stretching lease window)
        if (nowUtc < lease.IssuedAtUtc - ClockRollbackTolerance)
        {
            return LeaseValidationResult.Denied("LEASE_CLOCK_TAMPER_LOCKED", "Device clock is earlier than lease issuance time.");
        }

        // Anti-rollback watermark check across reboots and wall-clock tampering
        if (prior is not null)
        {
            if (nowUtc < prior.LastSeenUtc - ClockRollbackTolerance)
            {
                return LeaseValidationResult.Denied("LEASE_CLOCK_TAMPER_LOCKED", "Device wall clock rolled back below persisted high-water mark.");
            }

            if (bootCount == prior.LastBootCount && elapsedRealtimeMs + 5_000 < prior.LastElapsedRealtimeMs)
            {
                return LeaseValidationResult.Denied("LEASE_CLOCK_TAMPER_LOCKED", "Monotonic elapsed clock regressed within the same boot.");
            }

            if (bootCount < prior.LastBootCount)
            {
                return LeaseValidationResult.Denied("LEASE_CLOCK_TAMPER_LOCKED", "Device boot counter regressed.");
            }
        }

        if (nowUtc >= lease.ExpiresAtUtc)
        {
            return LeaseValidationResult.Denied("LEASE_EXPIRED", "Offline authorization lease has expired. Reconnect to Central to renew.");
        }

        DateTimeOffset nextHighWaterUtc = prior is not null && prior.LastSeenUtc > nowUtc ? prior.LastSeenUtc : nowUtc;
        AntiRollbackWatermark updated = new(nextHighWaterUtc, elapsedRealtimeMs, bootCount, IsRevokedFenced: false);
        await _secretStore.WriteAsync(
            WatermarkSecretKey,
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(updated)),
            ct).ConfigureAwait(false);

        return LeaseValidationResult.Allowed();
    }

    private async Task<AntiRollbackWatermark?> ReadWatermarkAsync(CancellationToken ct)
    {
        byte[]? raw = await _secretStore.ReadAsync(WatermarkSecretKey, ct).ConfigureAwait(false);
        if (raw is null || raw.Length == 0)
        {
            return null;
        }

        return JsonSerializer.Deserialize<AntiRollbackWatermark>(Encoding.UTF8.GetString(raw));
    }
}
