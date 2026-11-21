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
