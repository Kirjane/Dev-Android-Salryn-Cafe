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
/// Verifies Central ECDSA P-256 leases against an independently provisioned trust key.
/// Persists an anti-rollback watermark through IDeviceSecretStore; protection against
/// deletion/restore of its ciphertext remains a platform integration responsibility.
/// </summary>
public sealed class MobileOfflineLeaseGuard
{
    public const string CanonicalProductId = "Salryn.Cafe";
    private const string WatermarkSecretKey = "salryn.cafe.offline_lease_watermark.v1";
    private static readonly TimeSpan ClockRollbackTolerance = TimeSpan.FromMinutes(2);

    private readonly IDeviceSecretStore _secretStore;
    private readonly string? _trustedServerPublicKeySpkiBase64;
    private readonly SemaphoreSlim _watermarkGate = new(1, 1);

    public MobileOfflineLeaseGuard(IDeviceSecretStore secretStore, string? trustedServerPublicKeySpkiBase64 = null)
    {
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
        // Never obtain this key from the lease being verified. Missing enrollment trust
        // deliberately keeps offline authorization locked.
        _trustedServerPublicKeySpkiBase64 = trustedServerPublicKeySpkiBase64;
    }

    public static bool VerifyLeaseSignature(MobileSignedOfflineLease lease, string? trustedServerPublicKeySpkiBase64)
    {
        if (lease is null ||
            !string.Equals(lease.ProductId, CanonicalProductId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(trustedServerPublicKeySpkiBase64) ||
            string.IsNullOrWhiteSpace(lease.ServerPublicKeySpkiBase64) ||
            string.IsNullOrWhiteSpace(lease.SignatureBase64))
        {
            return false;
        }

        string payload = $"{lease.DeviceId:D}|{lease.TenantId:D}|{lease.StoreId:D}|{lease.ProductId}|{lease.ServerEpoch}|{lease.IssuedAtUtc:O}|{lease.ExpiresAtUtc:O}";
        try
        {
            byte[] spki = Convert.FromBase64String(lease.ServerPublicKeySpkiBase64);
            byte[] trustedSpki = Convert.FromBase64String(trustedServerPublicKeySpkiBase64);
            if (!CryptographicOperations.FixedTimeEquals(spki, trustedSpki)) return false;
            byte[] sig = Convert.FromBase64String(lease.SignatureBase64);
            using ECDsa ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(trustedSpki, out int bytesRead);
            if (bytesRead != trustedSpki.Length || ecdsa.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7") return false;
            return ecdsa.VerifyData(Encoding.UTF8.GetBytes(payload), sig, HashAlgorithmName.SHA256);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            return false;
        }
    }

    public async Task MarkRevokedFencedAsync(DateTimeOffset nowUtc, long elapsedMs, int bootCount, CancellationToken ct = default)
    {
        await _watermarkGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            AntiRollbackWatermark wm = new(nowUtc, elapsedMs, bootCount, IsRevokedFenced: true);
            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(wm));
            await _secretStore.WriteAsync(WatermarkSecretKey, bytes, ct).ConfigureAwait(false);
        }
        finally { _watermarkGate.Release(); }
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
        await _watermarkGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await EvaluateAndAdvanceCoreAsync(lease, expectedDeviceId, expectedTenantId,
                expectedStoreId, nowUtc, elapsedRealtimeMs, bootCount, ct).ConfigureAwait(false);
        }
        finally { _watermarkGate.Release(); }
    }

    private async Task<LeaseValidationResult> EvaluateAndAdvanceCoreAsync(
        MobileSignedOfflineLease? lease, Guid expectedDeviceId, Guid expectedTenantId,
        Guid expectedStoreId, DateTimeOffset nowUtc, long elapsedRealtimeMs, int bootCount, CancellationToken ct)
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

        if (string.IsNullOrWhiteSpace(_trustedServerPublicKeySpkiBase64))
        {
            return LeaseValidationResult.Denied("LEASE_TRUST_NOT_CONFIGURED", "No independently provisioned server trust key is present.");
        }

        if (!VerifyLeaseSignature(lease, _trustedServerPublicKeySpkiBase64))
        {
            return LeaseValidationResult.Denied("LEASE_SIGNATURE_INVALID", "Offline lease cryptographic signature failed verification.");
        }

        if (elapsedRealtimeMs < 0 || bootCount < 0)
        {
            return LeaseValidationResult.Denied("LEASE_CLOCK_UNAVAILABLE", "A trusted platform elapsed clock and boot counter are required.");
        }

        if (lease.DeviceId == Guid.Empty || lease.TenantId == Guid.Empty || lease.StoreId == Guid.Empty ||
            lease.ServerEpoch < 1 || lease.ExpiresAtUtc <= lease.IssuedAtUtc)
        {
            return LeaseValidationResult.Denied("LEASE_INVALID", "Offline lease scope or validity interval is invalid.");
        }

        // Initial clock-behind-IssuedAtUtc check (prevents stretching lease window)
        if (nowUtc < lease.IssuedAtUtc - ClockRollbackTolerance)
        {
            return LeaseValidationResult.Denied("LEASE_CLOCK_TAMPER_LOCKED", "Device clock is earlier than lease issuance time.");
        }

        // Anti-rollback watermark check across reboots and wall-clock tampering
        DateTimeOffset effectiveNowUtc = nowUtc;
        if (prior is not null)
        {
            if (nowUtc < prior.LastSeenUtc - ClockRollbackTolerance)
            {
                return LeaseValidationResult.Denied("LEASE_CLOCK_TAMPER_LOCKED", "Device wall clock rolled back below persisted high-water mark.");
            }

            if (bootCount == prior.LastBootCount && elapsedRealtimeMs < prior.LastElapsedRealtimeMs - 5_000)
            {
                return LeaseValidationResult.Denied("LEASE_CLOCK_TAMPER_LOCKED", "Monotonic elapsed clock regressed within the same boot.");
            }

            if (bootCount < prior.LastBootCount)
            {
                return LeaseValidationResult.Denied("LEASE_CLOCK_TAMPER_LOCKED", "Device boot counter regressed.");
            }

            if (bootCount == prior.LastBootCount && elapsedRealtimeMs > prior.LastElapsedRealtimeMs)
            {
                // A frozen/slowed wall clock cannot extend the lease within a boot.
                long deltaMs = elapsedRealtimeMs - prior.LastElapsedRealtimeMs;
                long remainingMs = (long)(DateTimeOffset.MaxValue - prior.LastSeenUtc).TotalMilliseconds;
                if (deltaMs >= remainingMs)
                    return LeaseValidationResult.Denied("LEASE_EXPIRED", "Elapsed time exceeds lease validity.");
                DateTimeOffset elapsedNow = prior.LastSeenUtc.AddMilliseconds(deltaMs);
                if (elapsedNow > effectiveNowUtc) effectiveNowUtc = elapsedNow;
            }
        }

        if (effectiveNowUtc >= lease.ExpiresAtUtc)
        {
            return LeaseValidationResult.Denied("LEASE_EXPIRED", "Offline authorization lease has expired. Reconnect to Central to renew.");
        }

        DateTimeOffset nextHighWaterUtc = prior is not null && prior.LastSeenUtc > effectiveNowUtc ? prior.LastSeenUtc : effectiveNowUtc;
        long nextElapsedMs = prior is not null && prior.LastBootCount == bootCount
            ? Math.Max(elapsedRealtimeMs, prior.LastElapsedRealtimeMs) : elapsedRealtimeMs;
        AntiRollbackWatermark updated = new(nextHighWaterUtc, nextElapsedMs, bootCount, IsRevokedFenced: false);
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
