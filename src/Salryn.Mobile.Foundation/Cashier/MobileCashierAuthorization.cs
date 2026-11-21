using System.Text.Json;
using Salryn.Core.Common;
using Salryn.Mobile.Foundation.P0;
using Salryn.Mobile.Foundation.Security;

namespace Salryn.Mobile.Foundation.Cashier;

public sealed record MobileEnrollmentBinding(Guid DeviceId, Guid TenantId, Guid StoreId,
    string ProductId, long ServerEpoch, string TrustedServerPublicKeySpkiBase64,
    MobileSignedOfflineLease Lease);

public interface IMobileEnrollmentReader
{
    Task<MobileEnrollmentBinding?> ReadAsync(CancellationToken cancellationToken = default);
}

public interface IMobileEnrollmentWriter
{
    Task<Result<MobileEnrollmentBinding>> WriteVerifiedAsync(
        MobileEnrollmentBinding binding,
        string pinnedServerPublicKeySpkiBase64,
        MobileClockSnapshot now,
        CancellationToken cancellationToken = default);
}

/// <summary>Read only. Only the approved pairing/bootstrap flow may write this protected bundle.
/// The pinned server key must come from independently approved endpoint configuration,
/// never the key embedded in an unauthenticated lease response.</summary>
public sealed class ProtectedMobileEnrollmentReader(IDeviceSecretStore secrets) : IMobileEnrollmentReader
{
    public const string SecretKey = "salryn.cafe.approved_enrollment.v1";
    public async Task<MobileEnrollmentBinding?> ReadAsync(CancellationToken cancellationToken = default)
    {
        byte[]? bytes = await secrets.ReadAsync(SecretKey, cancellationToken).ConfigureAwait(false);
        if (bytes is null || bytes.Length == 0) return null;
        try { return JsonSerializer.Deserialize<MobileEnrollmentBinding>(bytes); }
        catch (JsonException) { return null; }
    }
}

/// <summary>Fail-closed writer for approved device pairing/bootstrap. Refuses to persist
/// unless the lease is cryptographically verified against an independently pinned server
/// public key and passes anti-rollback/expiry checks.</summary>
public sealed class ProtectedMobileEnrollmentWriter(IDeviceSecretStore secrets) : IMobileEnrollmentWriter
{
    public async Task<Result<MobileEnrollmentBinding>> WriteVerifiedAsync(
        MobileEnrollmentBinding binding,
        string pinnedServerPublicKeySpkiBase64,
        MobileClockSnapshot now,
        CancellationToken cancellationToken = default)
    {
        if (binding is null || binding.Lease is null)
            return Denied("Mobile.EnrollmentInvalid", "Enrollment binding and signed offline lease are required.");
        if (string.IsNullOrWhiteSpace(pinnedServerPublicKeySpkiBase64))
            return Denied("Mobile.PinnedKeyRequired", "An independently pinned server public key is required before persisting enrollment.");
        if (binding.DeviceId == Guid.Empty || binding.TenantId == Guid.Empty || binding.StoreId == Guid.Empty || binding.ServerEpoch < 1)
            return Denied("Mobile.EnrollmentScopeInvalid", "Enrollment device, tenant, store, and server epoch must be valid.");
        if (!string.Equals(binding.ProductId, MobileOfflineLeaseGuard.CanonicalProductId, StringComparison.Ordinal) ||
            !string.Equals(binding.Lease.ProductId, MobileOfflineLeaseGuard.CanonicalProductId, StringComparison.Ordinal))
            return Denied("ERR_WRONG_PRODUCT", "Enrollment product identity must match Salryn.Cafe.");
        if (binding.ServerEpoch != binding.Lease.ServerEpoch ||
            binding.DeviceId != binding.Lease.DeviceId ||
            binding.TenantId != binding.Lease.TenantId ||
            binding.StoreId != binding.Lease.StoreId)
            return Denied("Mobile.EnrollmentLeaseMismatch", "Enrollment scope does not match the signed offline lease.");
        if (!string.Equals(binding.TrustedServerPublicKeySpkiBase64, pinnedServerPublicKeySpkiBase64, StringComparison.Ordinal) ||
            !string.Equals(binding.Lease.ServerPublicKeySpkiBase64, pinnedServerPublicKeySpkiBase64, StringComparison.Ordinal))
            return Denied("Mobile.UntrustedServerKey", "Lease server key does not match the independently pinned server public key.");

        MobileOfflineLeaseGuard guard = new(secrets, pinnedServerPublicKeySpkiBase64);
        LeaseValidationResult validation = await guard.EvaluateAndAdvanceAsync(
            binding.Lease, binding.DeviceId, binding.TenantId, binding.StoreId,
            now.UtcNow, now.ElapsedRealtimeMs, now.BootCount, cancellationToken).ConfigureAwait(false);
        if (!validation.IsAuthorized)
            return Denied(validation.Code, validation.Message);

        MobileEnrollmentBinding canonical = binding with { TrustedServerPublicKeySpkiBase64 = pinnedServerPublicKeySpkiBase64 };
        await secrets.WriteAsync(
            ProtectedMobileEnrollmentReader.SecretKey,
            JsonSerializer.SerializeToUtf8Bytes(canonical),
            cancellationToken).ConfigureAwait(false);
        return Result<MobileEnrollmentBinding>.Success(canonical);
    }

    private static Result<MobileEnrollmentBinding> Denied(string code, string message) =>
        Result<MobileEnrollmentBinding>.Fail(Error.Forbidden(code, message));
}

public sealed record MobileClockSnapshot(DateTimeOffset UtcNow, long ElapsedRealtimeMs, int BootCount);

public sealed class MobileCashierDeviceGuard(
    IMobileEnrollmentReader enrollment, IDeviceSecretStore secrets, Func<MobileClockSnapshot> clock)
{
    public async Task<Result<MobileEnrollmentBinding>> AuthorizeAsync(CancellationToken ct = default)
    {
        MobileEnrollmentBinding? binding = await enrollment.ReadAsync(ct).ConfigureAwait(false);
        if (binding is null || binding.Lease is null)
            return Denied("Mobile.DeviceApprovalRequired", "Complete approved device pairing and bootstrap before taking orders.");
        if (binding.ProductId != MobileOfflineLeaseGuard.CanonicalProductId || binding.ServerEpoch < 1 ||
            binding.ServerEpoch != binding.Lease.ServerEpoch)
            return Denied("Mobile.DeviceScopeInvalid", "Device product or synchronization epoch does not match the approved lease.");
        MobileClockSnapshot now = clock();
        MobileOfflineLeaseGuard guard = new(secrets, binding.TrustedServerPublicKeySpkiBase64);
        LeaseValidationResult result = await guard.EvaluateAndAdvanceAsync(binding.Lease,
            binding.DeviceId, binding.TenantId, binding.StoreId, now.UtcNow,
            now.ElapsedRealtimeMs, now.BootCount, ct).ConfigureAwait(false);
        return result.IsAuthorized ? Result<MobileEnrollmentBinding>.Success(binding)
            : Denied(result.Code, result.Message);
    }

    private static Result<MobileEnrollmentBinding> Denied(string code, string message) =>
        Result<MobileEnrollmentBinding>.Fail(Error.Forbidden(code, message));
}
