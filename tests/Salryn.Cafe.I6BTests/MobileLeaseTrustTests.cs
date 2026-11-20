using System.Security.Cryptography;
using System.Text;
using Salryn.Mobile.Foundation.Security;
using Salryn.Mobile.Foundation.Printing;
using Salryn.Mobile.Runtime;
using Xunit;

namespace Salryn.Cafe.I6BTests;

public sealed class MobileLeaseTrustTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 21, 0, 0, 0, TimeSpan.Zero);

    private static MobileSignedOfflineLease Sign(ECDsa key, Guid device, Guid tenant, Guid store,
        DateTimeOffset? expires = null)
    {
        MobileSignedOfflineLease lease = new(device, tenant, store, "Salryn.Cafe", 1,
            Now.AddMinutes(-1), expires ?? Now.AddHours(1), "", Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));
        string payload = $"{lease.DeviceId:D}|{lease.TenantId:D}|{lease.StoreId:D}|{lease.ProductId}|{lease.ServerEpoch}|{lease.IssuedAtUtc:O}|{lease.ExpiresAtUtc:O}";
        return lease with { SignatureBase64 = Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256)) };
    }

    [Fact]
    public async Task LeaseRequiresIndependentTrust_AndRejectsAttackerSelfSignedKey()
    {
        using ECDsa server = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ECDsa attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        MobileSignedOfflineLease good = Sign(server, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        MobileSignedOfflineLease forged = Sign(attacker, good.DeviceId, good.TenantId, good.StoreId);
        MobileOfflineLeaseGuard missingTrust = new(new InMemorySecretStore());
        Assert.Equal("LEASE_TRUST_NOT_CONFIGURED", (await Check(missingTrust, good)).Code);

        MobileOfflineLeaseGuard guard = new(new InMemorySecretStore(), good.ServerPublicKeySpkiBase64);
        Assert.Equal("LEASE_SIGNATURE_INVALID", (await Check(guard, forged)).Code);
        Assert.True((await Check(guard, good)).IsAuthorized);
        Assert.False(MobileOfflineLeaseGuard.VerifyLeaseSignature(good with { ExpiresAtUtc = Now.AddDays(3) }, good.ServerPublicKeySpkiBase64));
    }

    [Fact]
    public async Task FrozenWallClock_StillExpiresUsingElapsedTime_AcrossGuardRestart()
    {
        using ECDsa server = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        MobileSignedOfflineLease lease = Sign(server, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        InMemorySecretStore store = new();
        MobileOfflineLeaseGuard guard = new(store, lease.ServerPublicKeySpkiBase64);
        Assert.True((await Check(guard, lease, elapsed: 1000)).IsAuthorized);
        guard = new(store, lease.ServerPublicKeySpkiBase64);
        Assert.Equal("LEASE_EXPIRED", (await Check(guard, lease, elapsed: 3_601_000)).Code);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    public async Task MissingPlatformClock_DeniesOfflineAuthorization(long elapsed, int boot)
    {
        using ECDsa server = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        MobileSignedOfflineLease lease = Sign(server, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        MobileOfflineLeaseGuard guard = new(new InMemorySecretStore(), lease.ServerPublicKeySpkiBase64);
        Assert.Equal("LEASE_CLOCK_UNAVAILABLE", (await Check(guard, lease, elapsed, boot)).Code);
    }

    [Fact]
    public async Task CrossProductScopeTamperingAndRevocation_AreDenied()
    {
        using ECDsa server = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        MobileSignedOfflineLease lease = Sign(server, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        InMemorySecretStore store = new();
        MobileOfflineLeaseGuard guard = new(store, lease.ServerPublicKeySpkiBase64);
        Assert.Equal("ERR_WRONG_PRODUCT", (await Check(guard, lease with { ProductId = "Salryn.Retail" })).Code);
        Assert.Equal("ERR_TENANT_SCOPE_VIOLATION", (await guard.EvaluateAndAdvanceAsync(lease,
            lease.DeviceId, Guid.NewGuid(), lease.StoreId, Now, 1000, 1)).Code);
        await guard.MarkRevokedFencedAsync(Now, 1000, 1);
        guard = new(store, lease.ServerPublicKeySpkiBase64);
        Assert.Equal("DEVICE_REVOKED_FENCED", (await Check(guard, lease)).Code);
    }

    [Fact]
    public async Task EngineWithoutPlatformClock_CannotOpenShift()
    {
        using ECDsa server = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        MobileSignedOfflineLease lease = Sign(server, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        CafeMobileDatabase db = new(Path.Combine(Path.GetTempPath(), "salryn-clock-test-" + Guid.NewGuid().ToString("N"), "mobile.db"));
        db.Initialize();
        MobileCashierEngine engine = new(db, new MobileOfflineLeaseGuard(new InMemorySecretStore(), lease.ServerPublicKeySpkiBase64),
            new SimulatedEscPosPrinterTransport(), utcNow: () => Now);
        engine.ConfigureDeviceBinding(lease.DeviceId, lease.TenantId, lease.StoreId, 1, lease);
        var result = await engine.OpenShiftAsync(new MobileOperatorPrincipal(Guid.NewGuid(), "synthetic", "Synthetic", "Cashier"), 0);
        Assert.False(result.IsSuccess);
        Assert.Equal("LEASE_CLOCK_UNAVAILABLE", result.ErrorCode);
    }

    private static Task<LeaseValidationResult> Check(MobileOfflineLeaseGuard guard, MobileSignedOfflineLease lease,
        long elapsed = 1000, int boot = 1) => guard.EvaluateAndAdvanceAsync(lease, lease.DeviceId, lease.TenantId,
            lease.StoreId, Now, elapsed, boot);
}
