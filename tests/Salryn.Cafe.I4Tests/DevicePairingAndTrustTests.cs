using System.Security.Cryptography;
using System.Text;
using Salryn.Security.Pairing;
using Xunit;

namespace Salryn.Cafe.I4Tests;

public sealed class DevicePairingAndTrustTests
{
    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid StoreA = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid StoreB = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid AdminUserId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid CashierUserId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private static readonly TenantScope CafeScopeA = new(PairingConstants.CanonicalProductId, TenantA, StoreA);
    private static readonly TenantScope RetailScopeA = new("Salryn.GeneralMerchandise", TenantA, StoreA);
    private static readonly TenantScope CafeScopeB = new(PairingConstants.CanonicalProductId, TenantB, StoreA);
    private static readonly TenantScope CafeScopeOtherStore = new(PairingConstants.CanonicalProductId, TenantA, StoreB);

    private static (ECDsa PrivateKey, byte[] Spki) GenerateP256KeyPair()
    {
        ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (key, key.ExportSubjectPublicKeyInfo());
    }

    private static (DeviceEnrollmentService Enrollment, DeviceChallengeHandshakeService Handshake, EnrolledDeviceRecord Device, ECDsa DeviceKey) EnrollActiveDevice(
        FakeTimeProvider clock,
        DeviceRole role = DeviceRole.OfflineAndroidCashier,
        int maxTerminals = 2,
        string origin = "192.168.10.25")
    {
        DeviceEnrollmentService enrollment = new(clock);
        DeviceChallengeHandshakeService handshake = new(enrollment, clock);
        (ECDsa key, byte[] spki) = GenerateP256KeyPair();

        PairingResult<JoinCodeIssuedResponse> joinIssued = enrollment.RotateJoinCode(CafeScopeA, maxTerminals, AdminUserId);
        Assert.True(joinIssued.IsSuccess);

        PairingResult<PendingEnrollmentResponse> pending = enrollment.RequestEnrollment(
            joinIssued.Value!.JoinCode, CafeScopeA, "Barista Tablet 1", role, spki, origin);
        Assert.True(pending.IsSuccess);

        PairingResult<ApprovedEnrollmentResponse> approved = enrollment.ApproveEnrollment(
            pending.Value!.RequestId, CafeScopeA, AdminUserId, maxTerminals);
        Assert.True(approved.IsSuccess);

        PairingResult<EnrolledDeviceRecord> activated = enrollment.ActivateDevice(
            pending.Value.RequestId, approved.Value!.OneTimeActivationToken, CafeScopeA, origin);
        Assert.True(activated.IsSuccess);

        return (enrollment, handshake, activated.Value!, key);
    }

    [Fact]
    public void HappyPath_Enroll_Activate_EcdsaChallenge_Session_And_CombinedRbac_Succeeds()
    {
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 21, 8, 0, 0, TimeSpan.Zero));
        (_, DeviceChallengeHandshakeService handshake, EnrolledDeviceRecord device, ECDsa deviceKey) =
            EnrollActiveDevice(clock, DeviceRole.LanThinTerminal, origin: "192.168.10.42");
        using (deviceKey)
        {
            PairingResult<DeviceChallengeIssued> challenge = handshake.IssueChallenge(device.DeviceId, CafeScopeA, "192.168.10.42");
            Assert.True(challenge.IsSuccess);

            byte[] signature = SecretCrypto.SignEcdsaP256(deviceKey, challenge.Value!.CanonicalPayloadToSign);
            PairingResult<DeviceSessionIssued> session = handshake.VerifyChallengeAndIssueSession(
                challenge.Value.ChallengeId, device.DeviceId, CafeScopeA, signature, "192.168.10.42");
            Assert.True(session.IsSuccess);

            OperatorIdentityPrincipal cashier = new(CashierUserId, "cashier1", "Cashier", true, new[] { "Sales.Create", "Orders.Create" });
            PairingResult<AuthorizedRequestContext> auth = handshake.AuthorizeCombinedRequest(
                session.Value!.SessionId,
                session.Value.RawSessionToken,
                device.DeviceId,
                CafeScopeA,
                "192.168.10.42",
                cashier,
                "Sales.Create");

            Assert.True(auth.IsSuccess);
            Assert.Equal(device.DeviceId, auth.Value!.Device.DeviceId);
            Assert.Equal(CashierUserId, auth.Value.Operator.UserId);
        }
    }

    [Fact]
    public void JoinCode_Expired_IsRejected()
    {
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 21, 8, 0, 0, TimeSpan.Zero));
        DeviceEnrollmentService enrollment = new(clock);
        (ECDsa key, byte[] spki) = GenerateP256KeyPair();
        using (key)
        {
            PairingResult<JoinCodeIssuedResponse> join = enrollment.RotateJoinCode(CafeScopeA, 2, AdminUserId);
            clock.Advance(PairingConstants.JoinCodeLifetime + TimeSpan.FromSeconds(1));

            PairingResult<PendingEnrollmentResponse> res = enrollment.RequestEnrollment(
                join.Value!.JoinCode, CafeScopeA, "Tablet", DeviceRole.LanThinTerminal, spki, "192.168.1.50");

            Assert.True(res.IsFailure);
            Assert.Equal(PairingErrorCodes.JoinCodeExpired, res.ErrorCode);
            Assert.Equal(403, res.HttpStatusCode);
        }
    }

    [Fact]
    public void JoinCode_BruteForce_TripsRateLimiterLockout()
    {
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 21, 8, 0, 0, TimeSpan.Zero));
        SlidingWindowRateLimiter limiter = new(maxFailures: 3, window: TimeSpan.FromMinutes(5), lockoutDuration: TimeSpan.FromMinutes(15), timeProvider: clock);
        DeviceEnrollmentService enrollment = new(clock, limiter);
        (ECDsa key, byte[] spki) = GenerateP256KeyPair();
        using (key)
        {
            enrollment.RotateJoinCode(CafeScopeA, 2, AdminUserId);

            for (int i = 0; i < 3; i++)
            {
                PairingResult<PendingEnrollmentResponse> bad = enrollment.RequestEnrollment(
                    "WRONGCOD", CafeScopeA, "Attacker", DeviceRole.LanThinTerminal, spki, "192.168.1.99");
                Assert.True(bad.IsFailure);
                Assert.Equal(PairingErrorCodes.JoinCodeInvalid, bad.ErrorCode);
            }

            // 4th attempt is locked out with 429 even if join code were valid.
            PairingResult<PendingEnrollmentResponse> locked = enrollment.RequestEnrollment(
                "WRONGCOD", CafeScopeA, "Attacker", DeviceRole.LanThinTerminal, spki, "192.168.1.99");
            Assert.True(locked.IsFailure);
            Assert.Equal(PairingErrorCodes.RateLimitExceeded, locked.ErrorCode);
            Assert.Equal(429, locked.HttpStatusCode);
        }
    }

    [Fact]
    public void ActivationToken_Replay_And_Expiry_AreRejected()
    {
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 21, 8, 0, 0, TimeSpan.Zero));
        DeviceEnrollmentService enrollment = new(clock);
        (ECDsa k1, byte[] spki1) = GenerateP256KeyPair();
        (ECDsa k2, byte[] spki2) = GenerateP256KeyPair();
        using (k1)
        using (k2)
        {
            string joinCode = enrollment.RotateJoinCode(CafeScopeA, 5, AdminUserId).Value!.JoinCode;

            // 1. Replay of already-consumed activation token fails with 409.
            Guid req1 = enrollment.RequestEnrollment(joinCode, CafeScopeA, "T1", DeviceRole.LanThinTerminal, spki1, "10.0.0.1").Value!.RequestId;
            string token1 = enrollment.ApproveEnrollment(req1, CafeScopeA, AdminUserId, 5).Value!.OneTimeActivationToken;

            Assert.True(enrollment.ActivateDevice(req1, token1, CafeScopeA, "10.0.0.1").IsSuccess);
            PairingResult<EnrolledDeviceRecord> replay = enrollment.ActivateDevice(req1, token1, CafeScopeA, "10.0.0.1");
            Assert.True(replay.IsFailure);
            Assert.Equal(PairingErrorCodes.ActivationTokenAlreadyConsumed, replay.ErrorCode);
            Assert.Equal(409, replay.HttpStatusCode);

            // 2. Expired activation token fails with 403.
            Guid req2 = enrollment.RequestEnrollment(joinCode, CafeScopeA, "T2", DeviceRole.LanThinTerminal, spki2, "10.0.0.2").Value!.RequestId;
            string token2 = enrollment.ApproveEnrollment(req2, CafeScopeA, AdminUserId, 5).Value!.OneTimeActivationToken;
            clock.Advance(PairingConstants.ActivationTokenLifetime + TimeSpan.FromSeconds(1));

            PairingResult<EnrolledDeviceRecord> expired = enrollment.ActivateDevice(req2, token2, CafeScopeA, "10.0.0.2");
            Assert.True(expired.IsFailure);
            Assert.Equal(PairingErrorCodes.ActivationTokenExpired, expired.ErrorCode);
        }
    }

    [Fact]
    public void EntitlementCapacityOverflow_IsRejected()
    {
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 21, 8, 0, 0, TimeSpan.Zero));
        (DeviceEnrollmentService enrollment, _, _, ECDsa k1) = EnrollActiveDevice(clock, maxTerminals: 1);
        (ECDsa k2, byte[] spki2) = GenerateP256KeyPair();
        using (k1)
        using (k2)
        {
            string join = enrollment.RotateJoinCode(CafeScopeA, maxPairedTerminals: 1, AdminUserId).Value!.JoinCode;
            PairingResult<PendingEnrollmentResponse> overflow = enrollment.RequestEnrollment(
                join, CafeScopeA, "Second Device", DeviceRole.LanThinTerminal, spki2, "192.168.10.26");

            Assert.True(overflow.IsFailure);
            Assert.Equal(PairingErrorCodes.CapacityExceeded, overflow.ErrorCode);
        }
    }

    [Fact]
    public void Challenge_InvalidSignature_ReplayedNonce_And_ExpiredNonce_AreRejected()
    {
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 21, 8, 0, 0, TimeSpan.Zero));
        (_, DeviceChallengeHandshakeService handshake, EnrolledDeviceRecord device, ECDsa deviceKey) = EnrollActiveDevice(clock);
        (ECDsa wrongKey, _) = GenerateP256KeyPair();
        using (deviceKey)
        using (wrongKey)
        {
            // Invalid signature with wrong key
            DeviceChallengeIssued ch1 = handshake.IssueChallenge(device.DeviceId, CafeScopeA, "192.168.10.25").Value!;
            byte[] badSig = SecretCrypto.SignEcdsaP256(wrongKey, ch1.CanonicalPayloadToSign);
            PairingResult<DeviceSessionIssued> badRes = handshake.VerifyChallengeAndIssueSession(
                ch1.ChallengeId, device.DeviceId, CafeScopeA, badSig, "192.168.10.25");
            Assert.True(badRes.IsFailure);
            Assert.Equal(PairingErrorCodes.InvalidSignature, badRes.ErrorCode);

            // Valid signature succeeds, then replay fails
            byte[] goodSig = SecretCrypto.SignEcdsaP256(deviceKey, ch1.CanonicalPayloadToSign);
            Assert.True(handshake.VerifyChallengeAndIssueSession(
                ch1.ChallengeId, device.DeviceId, CafeScopeA, goodSig, "192.168.10.25").IsSuccess);

            PairingResult<DeviceSessionIssued> replay = handshake.VerifyChallengeAndIssueSession(
                ch1.ChallengeId, device.DeviceId, CafeScopeA, goodSig, "192.168.10.25");
            Assert.True(replay.IsFailure);
            Assert.Equal(PairingErrorCodes.ChallengeAlreadyConsumed, replay.ErrorCode);

            // Expired challenge fails
            DeviceChallengeIssued ch2 = handshake.IssueChallenge(device.DeviceId, CafeScopeA, "192.168.10.25").Value!;
            clock.Advance(PairingConstants.ChallengeLifetime + TimeSpan.FromSeconds(1));
            byte[] sig2 = SecretCrypto.SignEcdsaP256(deviceKey, ch2.CanonicalPayloadToSign);
            PairingResult<DeviceSessionIssued> expired = handshake.VerifyChallengeAndIssueSession(
                ch2.ChallengeId, device.DeviceId, CafeScopeA, sig2, "192.168.10.25");
            Assert.True(expired.IsFailure);
            Assert.Equal(PairingErrorCodes.ChallengeExpired, expired.ErrorCode);
        }
    }

    [Fact]
    public void CrossProduct_And_CrossTenant_And_OriginMismatch_AreRejected()
    {
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 21, 8, 0, 0, TimeSpan.Zero));
        (DeviceEnrollmentService enrollment, DeviceChallengeHandshakeService handshake, EnrolledDeviceRecord device, ECDsa deviceKey) =
            EnrollActiveDevice(clock, origin: "192.168.10.25");
        using (deviceKey)
        {
            // Cross-product join code rotation rejected
            PairingResult<JoinCodeIssuedResponse> retailJoin = enrollment.RotateJoinCode(RetailScopeA, 2, AdminUserId);
            Assert.True(retailJoin.IsFailure);
            Assert.Equal(PairingErrorCodes.ProductScopeMismatch, retailJoin.ErrorCode);

            // Cross-product & cross-tenant challenge rejected
            Assert.Equal(PairingErrorCodes.ProductScopeMismatch,
                handshake.IssueChallenge(device.DeviceId, RetailScopeA, "192.168.10.25").ErrorCode);
            Assert.Equal(PairingErrorCodes.TenantScopeMismatch,
                handshake.IssueChallenge(device.DeviceId, CafeScopeB, "192.168.10.25").ErrorCode);
            Assert.Equal(PairingErrorCodes.StoreScopeMismatch,
                handshake.IssueChallenge(device.DeviceId, CafeScopeOtherStore, "192.168.10.25").ErrorCode);

            // Session origin mismatch rejected
            DeviceChallengeIssued ch = handshake.IssueChallenge(device.DeviceId, CafeScopeA, "192.168.10.25").Value!;
            byte[] sig = SecretCrypto.SignEcdsaP256(deviceKey, ch.CanonicalPayloadToSign);
            DeviceSessionIssued session = handshake.VerifyChallengeAndIssueSession(
                ch.ChallengeId, device.DeviceId, CafeScopeA, sig, "192.168.10.25").Value!;

            PairingResult<DeviceTrustPrincipal> wrongOrigin = handshake.ValidateSession(
                session.SessionId, session.RawSessionToken, device.DeviceId, CafeScopeA, "192.168.10.99");
            Assert.True(wrongOrigin.IsFailure);
            Assert.Equal(PairingErrorCodes.OriginMismatch, wrongOrigin.ErrorCode);
        }
    }

    [Fact]
    public void MidSessionRevocation_KillsSession_BlocksPull_And_FencesSignedPushToQuarantine()
    {
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 21, 8, 0, 0, TimeSpan.Zero));
        (DeviceEnrollmentService enrollment, DeviceChallengeHandshakeService handshake, EnrolledDeviceRecord device, ECDsa deviceKey) =
            EnrollActiveDevice(clock);
        using (deviceKey)
        {
            DeviceChallengeIssued ch = handshake.IssueChallenge(device.DeviceId, CafeScopeA, "192.168.10.25").Value!;
            byte[] sig = SecretCrypto.SignEcdsaP256(deviceKey, ch.CanonicalPayloadToSign);
            DeviceSessionIssued session = handshake.VerifyChallengeAndIssueSession(
                ch.ChallengeId, device.DeviceId, CafeScopeA, sig, "192.168.10.25").Value!;

            Assert.True(handshake.ValidateSession(session.SessionId, session.RawSessionToken, device.DeviceId, CafeScopeA, "192.168.10.25").IsSuccess);

            // Revoke online
            PairingResult<EnrolledDeviceRecord> revoked = enrollment.RevokeDevice(device.DeviceId, CafeScopeA, AdminUserId, "Lost tablet");
            Assert.True(revoked.IsSuccess);
            Assert.Equal(DeviceStatus.Revoked, revoked.Value!.Status);

            // Active session immediately rejected
            PairingResult<DeviceTrustPrincipal> afterRevoke = handshake.ValidateSession(
                session.SessionId, session.RawSessionToken, device.DeviceId, CafeScopeA, "192.168.10.25");
            Assert.True(afterRevoke.IsFailure);
            Assert.Equal(PairingErrorCodes.SessionRevoked, afterRevoke.ErrorCode);

            // Pull / token renewal fails closed with 403 DEVICE_REVOKED
            PairingResult pull = DeviceRevocationFenceEvaluator.EvaluatePullOrToken(revoked.Value, CafeScopeA);
            Assert.True(pull.IsFailure);
            Assert.Equal(PairingErrorCodes.DeviceRevoked, pull.ErrorCode);
            Assert.Equal(403, pull.HttpStatusCode);

            // Signed offline push batch from revoked device is fenced to quarantine (403 DEVICE_REVOKED_FENCED)
            Guid op1 = Guid.NewGuid();
            Guid op2 = Guid.NewGuid();
            byte[] batchPayload = Encoding.UTF8.GetBytes($"batch|{op1:D}|{op2:D}");
            byte[] batchSig = SecretCrypto.SignEcdsaP256(deviceKey, batchPayload);

            PushFenceEvaluation fence = DeviceRevocationFenceEvaluator.EvaluatePush(
                revoked.Value, CafeScopeA, batchPayload, batchSig, new[] { op1, op2 });
            Assert.Equal(PushFenceAction.QuarantineRevokedOutbox, fence.Action);
            Assert.Equal(403, fence.HttpStatusCode);
            Assert.Equal(PairingErrorCodes.DeviceRevokedFenced, fence.Code);
            Assert.Equal(new[] { op1, op2 }, fence.QuarantinedOperationIds);
        }
    }

    [Fact]
    public void OfflineLease_Detects_Tamper_Expiry_WallClockRollback_And_MonotonicRollback()
    {
        DateTimeOffset t0 = new(2026, 11, 21, 8, 0, 0, TimeSpan.Zero);
        FakeTimeProvider clock = new(t0);
        (_, _, EnrolledDeviceRecord device, ECDsa deviceKey) = EnrollActiveDevice(clock);
        (ECDsa centralKey, byte[] centralSpki) = GenerateP256KeyPair();
        using (deviceKey)
        using (centralKey)
        {
            OfflineAuthorizationLease lease = OfflineLeaseService.IssueLease(
                centralKey, device, syncEpoch: 1, nowUtc: t0, lifetime: TimeSpan.FromHours(24)).Value!;

            AntiRollbackWatermark wm0 = new(t0, t0, LastBootCount: 10, LastElapsedRealtimeMs: 50_000);

            // 1. Valid progression advances watermark
            PairingResult<AntiRollbackWatermark> ok = OfflineLeaseService.VerifyLeaseAndAdvanceWatermark(
                lease, centralSpki, device.DeviceId, CafeScopeA, wm0, t0.AddHours(2), currentBootCount: 10, currentElapsedRealtimeMs: 7_250_000);
            Assert.True(ok.IsSuccess);
            AntiRollbackWatermark wm1 = ok.Value!;

            // 2. Same-boot monotonic rollback rejected
            PairingResult<AntiRollbackWatermark> monoRollback = OfflineLeaseService.VerifyLeaseAndAdvanceWatermark(
                lease, centralSpki, device.DeviceId, CafeScopeA, wm1, t0.AddHours(3), currentBootCount: 10, currentElapsedRealtimeMs: 1_000);
            Assert.True(monoRollback.IsFailure);
            Assert.Equal(PairingErrorCodes.MonotonicClockRollbackDetected, monoRollback.ErrorCode);

            // 3. Reboot + rolled-back wall clock (> 5 min tolerance) rejected
            PairingResult<AntiRollbackWatermark> wallRollback = OfflineLeaseService.VerifyLeaseAndAdvanceWatermark(
                lease, centralSpki, device.DeviceId, CafeScopeA, wm1, t0.AddHours(1), currentBootCount: 11, currentElapsedRealtimeMs: 10_000);
            Assert.True(wallRollback.IsFailure);
            Assert.Equal(PairingErrorCodes.ClockRollbackDetected, wallRollback.ErrorCode);

            // 4. Lease expiry rejected
            PairingResult<AntiRollbackWatermark> expired = OfflineLeaseService.VerifyLeaseAndAdvanceWatermark(
                lease, centralSpki, device.DeviceId, CafeScopeA, wm1, t0.AddHours(24).AddMinutes(1), currentBootCount: 11, currentElapsedRealtimeMs: 100_000);
            Assert.True(expired.IsFailure);
            Assert.Equal(PairingErrorCodes.OfflineLeaseExpired, expired.ErrorCode);

            // 5. Tampered lease signature rejected
            OfflineAuthorizationLease tampered = lease with { ExpiresAtUtc = t0.AddDays(30) };
            PairingResult<AntiRollbackWatermark> tamperedRes = OfflineLeaseService.VerifyLeaseAndAdvanceWatermark(
                tampered, centralSpki, device.DeviceId, CafeScopeA, wm1, t0.AddHours(3), currentBootCount: 10, currentElapsedRealtimeMs: 10_850_000);
            Assert.True(tamperedRes.IsFailure);
            Assert.Equal(PairingErrorCodes.OfflineLeaseInvalidSignature, tamperedRes.ErrorCode);
        }
    }

    [Fact]
    public void TrustPlaneSeparation_CookieOnly_DeviceOnly_RoleMismatch_And_PrinterIsolation()
    {
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 21, 8, 0, 0, TimeSpan.Zero));
        (_, DeviceChallengeHandshakeService handshake, EnrolledDeviceRecord device, ECDsa deviceKey) =
            EnrollActiveDevice(clock, DeviceRole.LanThinTerminal);
        using (deviceKey)
        {
            DeviceChallengeIssued ch = handshake.IssueChallenge(device.DeviceId, CafeScopeA, "192.168.10.25").Value!;
            byte[] sig = SecretCrypto.SignEcdsaP256(deviceKey, ch.CanonicalPayloadToSign);
            DeviceSessionIssued session = handshake.VerifyChallengeAndIssueSession(
                ch.ChallengeId, device.DeviceId, CafeScopeA, sig, "192.168.10.25").Value!;

            // Cookie-only rejected
            Assert.Equal(PairingErrorCodes.CookieOnlyRejected,
                handshake.AuthorizeCombinedRequest(session.SessionId, session.RawSessionToken, device.DeviceId, CafeScopeA, "192.168.10.25",
                    new OperatorIdentityPrincipal(CashierUserId, "c1", "Cashier", true, new[] { "Sales.Create" }), "Sales.Create", isCookieOnlyAttempt: true).ErrorCode);

            // Device-only (missing operator) rejected
            Assert.Equal(PairingErrorCodes.OperatorAuthRequired,
                handshake.AuthorizeCombinedRequest(session.SessionId, session.RawSessionToken, device.DeviceId, CafeScopeA, "192.168.10.25",
                    operatorPrincipal: null, "Sales.Create").ErrorCode);

            // Owner role not allowed on LanThinTerminal (only Cashier/Manager)
            Assert.Equal(PairingErrorCodes.OperatorRoleNotAllowed,
                handshake.AuthorizeCombinedRequest(session.SessionId, session.RawSessionToken, device.DeviceId, CafeScopeA, "192.168.10.25",
                    new OperatorIdentityPrincipal(AdminUserId, "owner", "Owner", true, new[] { "Sales.Create" }), "Sales.Create").ErrorCode);

            // Missing permission rejected
            Assert.Equal(PairingErrorCodes.PermissionDenied,
                handshake.AuthorizeCombinedRequest(session.SessionId, session.RawSessionToken, device.DeviceId, CafeScopeA, "192.168.10.25",
                    new OperatorIdentityPrincipal(CashierUserId, "c1", "Cashier", true, new[] { "Orders.View" }), "Sales.Void").ErrorCode);

            // Peripheral Bluetooth printer pairing is completely isolated from device trust
            PeripheralPrinterRegistry printers = new();
            PeripheralPrinterBinding receipt = printers.PairPrinter(PrinterStationRole.Receipt, "AA:BB:CC:DD:EE:01", "Front Counter 58mm");
            Assert.False(receipt.CanGrantDeviceTrust);
            Assert.False(receipt.CanGrantOperatorAuthority);
            Assert.True(printers.UnpairPrinter(PrinterStationRole.Receipt));
            // Unpairing printer does NOT affect enrolled device session
            Assert.True(handshake.ValidateSession(session.SessionId, session.RawSessionToken, device.DeviceId, CafeScopeA, "192.168.10.25").IsSuccess);
        }
    }

    [Fact]
    public void TransportSecurityPolicy_EnforcesHttpsForLanAndCentral_AndLoopback5399OnlyForDesktop()
    {
        Assert.True(TransportSecurityPolicy.ValidateEndpoint(new Uri("http://127.0.0.1:5399"), TransportContext.DesktopLoopbackWebView).IsSuccess);
        Assert.True(TransportSecurityPolicy.ValidateEndpoint(new Uri("http://127.0.0.1:5387"), TransportContext.DesktopLoopbackWebView).IsFailure);

        // Plaintext HTTP rejected on LAN & Central
        Assert.Equal(PairingErrorCodes.PlaintextTransportRejected,
            TransportSecurityPolicy.ValidateEndpoint(new Uri("http://192.168.1.10:5400"), TransportContext.LanRemoteTerminal).ErrorCode);
        Assert.Equal(PairingErrorCodes.PlaintextTransportRejected,
            TransportSecurityPolicy.ValidateEndpoint(new Uri("http://sync.salryncafe.ph"), TransportContext.CentralSync).ErrorCode);

        // HTTPS accepted on explicit LAN IP & Central
        Assert.True(TransportSecurityPolicy.ValidateEndpoint(new Uri("https://192.168.1.10:5400"), TransportContext.LanRemoteTerminal).IsSuccess);
        Assert.True(TransportSecurityPolicy.ValidateEndpoint(new Uri("https://sync.salryncafe.ph"), TransportContext.CentralSync).IsSuccess);
    }

    [Fact]
    public void DualApproval_CannotBypassEntitlementCapacity_AndExpiredApprovalReleasesSlot()
    {
        FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 21, 8, 0, 0, TimeSpan.Zero));
        DeviceEnrollmentService enrollment = new(clock);
        (ECDsa k1, byte[] spki1) = GenerateP256KeyPair();
        (ECDsa k2, byte[] spki2) = GenerateP256KeyPair();
        using (k1)
        using (k2)
        {
            string joinCode = enrollment.RotateJoinCode(CafeScopeA, maxPairedTerminals: 1, AdminUserId).Value!.JoinCode;

            Guid reqA = enrollment.RequestEnrollment(joinCode, CafeScopeA, "Tablet A", DeviceRole.LanThinTerminal, spki1, "192.168.1.11").Value!.RequestId;
            Guid reqB = enrollment.RequestEnrollment(joinCode, CafeScopeA, "Tablet B", DeviceRole.LanThinTerminal, spki2, "192.168.1.12").Value!.RequestId;

            // Approve Req A -> reserves the single slot
            PairingResult<ApprovedEnrollmentResponse> approveA = enrollment.ApproveEnrollment(reqA, CafeScopeA, AdminUserId, maxPairedTerminals: 1);
            Assert.True(approveA.IsSuccess);

            // Attempt to approve Req B while Req A's approval is still active -> MUST fail with CapacityExceeded
            PairingResult<ApprovedEnrollmentResponse> approveBBlocked = enrollment.ApproveEnrollment(reqB, CafeScopeA, AdminUserId, maxPairedTerminals: 1);
            Assert.True(approveBBlocked.IsFailure);
            Assert.Equal(PairingErrorCodes.CapacityExceeded, approveBBlocked.ErrorCode);

            // Once Req A's 5-min activation expires without activation, slot is released and Req B (10-min pending TTL) can be approved & activated
            clock.Advance(PairingConstants.ActivationTokenLifetime + TimeSpan.FromSeconds(1));

            PairingResult<EnrolledDeviceRecord> lateA = enrollment.ActivateDevice(reqA, approveA.Value!.OneTimeActivationToken, CafeScopeA, "192.168.1.11");
            Assert.True(lateA.IsFailure);
            Assert.Equal(PairingErrorCodes.ActivationTokenExpired, lateA.ErrorCode);

            PairingResult<ApprovedEnrollmentResponse> approveB = enrollment.ApproveEnrollment(reqB, CafeScopeA, AdminUserId, maxPairedTerminals: 1);
            Assert.True(approveB.IsSuccess);
            PairingResult<EnrolledDeviceRecord> activateB = enrollment.ActivateDevice(reqB, approveB.Value!.OneTimeActivationToken, CafeScopeA, "192.168.1.12");
            Assert.True(activateB.IsSuccess);
        }
    }

    [Fact]
    public void OfflineLease_RejectsInitialClockBehindIssuedAtUtc()
    {
        DateTimeOffset centralNow = new(2026, 11, 21, 8, 0, 0, TimeSpan.Zero);
        FakeTimeProvider clock = new(centralNow);
        (_, _, EnrolledDeviceRecord device, ECDsa deviceKey) = EnrollActiveDevice(clock);
        (ECDsa centralKey, byte[] centralSpki) = GenerateP256KeyPair();
        using (deviceKey)
        using (centralKey)
        {
            OfflineAuthorizationLease lease = OfflineLeaseService.IssueLease(
                centralKey, device, syncEpoch: 1, nowUtc: centralNow, lifetime: TimeSpan.FromHours(24)).Value!;

            // Device clock is 10 minutes behind Central's IssuedAtUtc on first check
            DateTimeOffset skewedClientClock = centralNow.AddMinutes(-10);
            AntiRollbackWatermark wm = new(skewedClientClock, skewedClientClock, LastBootCount: 1, LastElapsedRealtimeMs: 1_000);

            PairingResult<AntiRollbackWatermark> res = OfflineLeaseService.VerifyLeaseAndAdvanceWatermark(
                lease, centralSpki, device.DeviceId, CafeScopeA, wm, skewedClientClock, currentBootCount: 1, currentElapsedRealtimeMs: 2_000);

            Assert.True(res.IsFailure);
            Assert.Equal(PairingErrorCodes.ClockRollbackDetected, res.ErrorCode);
        }
    }
}
