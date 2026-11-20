using System.Security.Cryptography;
using System.Text;

namespace Salryn.Security.Pairing;

public sealed record OfflineAuthorizationLease(
    Guid LeaseId,
    Guid DeviceId,
    TenantScope Scope,
    long SyncEpoch,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    byte[] Signature);

public sealed record AntiRollbackWatermark(
    DateTimeOffset LastAttestedUtc,
    DateTimeOffset LastObservedUtc,
    long LastBootCount,
    long LastElapsedRealtimeMs);

/// <summary>
/// Issues and verifies Central-signed offline authorization leases and detects wall-clock
/// and monotonic clock rollbacks (including reboot + rolled-back wall clock) on Android cashiers.
/// </summary>
public static class OfflineLeaseService
{
    public static byte[] BuildCanonicalLeasePayload(
        Guid leaseId,
        Guid deviceId,
        TenantScope scope,
        long syncEpoch,
        DateTimeOffset issuedAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        string canonical = $"salryn.cafe.offline-lease.v1|{leaseId:D}|{deviceId:D}|{scope.ProductId}|{scope.TenantId:D}|{scope.StoreId:D}|{syncEpoch}|{issuedAtUtc.ToUnixTimeMilliseconds()}|{expiresAtUtc.ToUnixTimeMilliseconds()}";
        return Encoding.UTF8.GetBytes(canonical);
    }

    public static PairingResult<OfflineAuthorizationLease> IssueLease(
        ECDsa centralSigningKey,
        EnrolledDeviceRecord device,
        long syncEpoch,
        DateTimeOffset nowUtc,
        TimeSpan? lifetime = null)
    {
        ArgumentNullException.ThrowIfNull(centralSigningKey);
        ArgumentNullException.ThrowIfNull(device);

        PairingResult scopeCheck = ProductScopeGuard.ValidateProductAndTenant(device.Scope);
        if (scopeCheck.IsFailure)
        {
            return PairingResult<OfflineAuthorizationLease>.Fail(scopeCheck.ErrorCode!, scopeCheck.ErrorMessage!, scopeCheck.HttpStatusCode);
        }

        if (device.Status != DeviceStatus.Active)
        {
            return PairingResult<OfflineAuthorizationLease>.Fail(
                device.Status == DeviceStatus.Revoked ? PairingErrorCodes.DeviceRevoked : PairingErrorCodes.DeviceDisabled,
                "Cannot issue an offline lease for a non-active device.",
                httpStatusCode: 403);
        }

        TimeSpan effectiveLifetime = lifetime ?? PairingConstants.DefaultOfflineLeaseLifetime;
        if (effectiveLifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Lease lifetime must be positive.");
        }

        Guid leaseId = Guid.NewGuid();
        DateTimeOffset expiresAtUtc = nowUtc.Add(effectiveLifetime);
        byte[] payload = BuildCanonicalLeasePayload(leaseId, device.DeviceId, device.Scope, syncEpoch, nowUtc, expiresAtUtc);
        byte[] signature = SecretCrypto.SignEcdsaP256(centralSigningKey, payload);

        return PairingResult<OfflineAuthorizationLease>.Success(
            new OfflineAuthorizationLease(
                leaseId,
                device.DeviceId,
                device.Scope,
                syncEpoch,
                nowUtc,
                expiresAtUtc,
                signature));
    }

    public static PairingResult<AntiRollbackWatermark> VerifyLeaseAndAdvanceWatermark(
        OfflineAuthorizationLease lease,
        byte[] centralPublicKeySpki,
        Guid expectedDeviceId,
        TenantScope expectedScope,
        AntiRollbackWatermark currentWatermark,
        DateTimeOffset nowUtc,
        long currentBootCount,
        long currentElapsedRealtimeMs)
    {
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(currentWatermark);

        PairingResult scopeCheck = ProductScopeGuard.ValidateExactMatch(lease.Scope, expectedScope);
        if (scopeCheck.IsFailure)
        {
            return PairingResult<AntiRollbackWatermark>.Fail(scopeCheck.ErrorCode!, scopeCheck.ErrorMessage!, scopeCheck.HttpStatusCode);
        }

        if (lease.DeviceId != expectedDeviceId)
        {
            return PairingResult<AntiRollbackWatermark>.Fail(
                PairingErrorCodes.OfflineLeaseDeviceMismatch,
                "Offline lease is bound to a different device.",
                httpStatusCode: 403);
        }

        byte[] payload = BuildCanonicalLeasePayload(
            lease.LeaseId,
            lease.DeviceId,
            lease.Scope,
            lease.SyncEpoch,
            lease.IssuedAtUtc,
            lease.ExpiresAtUtc);

        if (!SecretCrypto.VerifyEcdsaP256(centralPublicKeySpki, payload, lease.Signature))
        {
            return PairingResult<AntiRollbackWatermark>.Fail(
                PairingErrorCodes.OfflineLeaseInvalidSignature,
                "Offline authorization lease signature is invalid.",
                httpStatusCode: 403);
        }

        // Anti-rollback check 1: Wall-clock rollback (catches reboot + rolled-back system clock).
        if (nowUtc < currentWatermark.LastObservedUtc - PairingConstants.ClockRollbackTolerance ||
            nowUtc < currentWatermark.LastAttestedUtc - PairingConstants.ClockRollbackTolerance)
        {
            return PairingResult<AntiRollbackWatermark>.Fail(
                PairingErrorCodes.ClockRollbackDetected,
                "Wall-clock rollback detected; offline mutations are locked until online server-time re-attestation.",
                httpStatusCode: 403);
        }

        // Anti-rollback check 2: Boot-count rollback or same-boot monotonic clock rollback.
        if (currentBootCount < currentWatermark.LastBootCount ||
            (currentBootCount == currentWatermark.LastBootCount && currentElapsedRealtimeMs < currentWatermark.LastElapsedRealtimeMs))
        {
            return PairingResult<AntiRollbackWatermark>.Fail(
                PairingErrorCodes.MonotonicClockRollbackDetected,
                "Monotonic/boot counter rollback detected; offline mutations are locked until online re-attestation.",
                httpStatusCode: 403);
        }

        // Lease expiry check.
        if (nowUtc >= lease.ExpiresAtUtc)
        {
            return PairingResult<AntiRollbackWatermark>.Fail(
                PairingErrorCodes.OfflineLeaseExpired,
                "Offline authorization lease has expired; online re-attestation is required before new mutations.",
                httpStatusCode: 403);
        }

        DateTimeOffset nextObservedUtc = nowUtc > currentWatermark.LastObservedUtc
            ? nowUtc
            : currentWatermark.LastObservedUtc;

        return PairingResult<AntiRollbackWatermark>.Success(
            new AntiRollbackWatermark(
                LastAttestedUtc: currentWatermark.LastAttestedUtc,
                LastObservedUtc: nextObservedUtc,
                LastBootCount: currentBootCount,
                LastElapsedRealtimeMs: currentElapsedRealtimeMs));
    }
}
