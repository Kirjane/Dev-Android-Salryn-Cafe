using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Salryn.Security.Pairing;

public sealed record DeviceChallengeIssued(
    Guid ChallengeId,
    string NonceHex,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    byte[] CanonicalPayloadToSign);

public sealed record DeviceSessionIssued(
    Guid SessionId,
    string RawSessionToken,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DeviceTrustPrincipal Principal);

/// <summary>
/// ECDSA P-256 asymmetric challenge-response handshake, constant-time hashed device session store,
/// and strict Device-Trust + Operator-RBAC combined authorization gate.
/// </summary>
public sealed class DeviceChallengeHandshakeService
{
    private readonly DeviceEnrollmentService _enrollmentService;
    private readonly TimeProvider _timeProvider;
    private readonly SlidingWindowRateLimiter _rateLimiter;
    private readonly ConcurrentDictionary<Guid, ChallengeState> _challenges = new();
    private readonly ConcurrentDictionary<Guid, StoredDeviceSession> _sessions = new();

    public DeviceChallengeHandshakeService(
        DeviceEnrollmentService enrollmentService,
        TimeProvider? timeProvider = null,
        SlidingWindowRateLimiter? rateLimiter = null)
    {
        _enrollmentService = enrollmentService ?? throw new ArgumentNullException(nameof(enrollmentService));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _rateLimiter = rateLimiter ?? new SlidingWindowRateLimiter(timeProvider: _timeProvider);
        _enrollmentService.DeviceRevoked += RevokeAllSessionsForDevice;
    }

    public static byte[] BuildCanonicalChallengePayload(
        Guid challengeId,
        string nonceHex,
        Guid deviceId,
        TenantScope scope,
        DateTimeOffset issuedAtUtc)
    {
        string canonical = $"salryn.cafe.challenge.v1|{challengeId:D}|{nonceHex}|{deviceId:D}|{scope.ProductId}|{scope.TenantId:D}|{scope.StoreId:D}|{issuedAtUtc.ToUnixTimeMilliseconds()}";
        return Encoding.UTF8.GetBytes(canonical);
    }

    public PairingResult<DeviceChallengeIssued> IssueChallenge(
        Guid deviceId,
        TenantScope scope,
        string observedOrigin)
    {
        PairingResult scopeCheck = ProductScopeGuard.ValidateProductAndTenant(scope);
        if (scopeCheck.IsFailure)
        {
            return PairingResult<DeviceChallengeIssued>.Fail(scopeCheck.ErrorCode!, scopeCheck.ErrorMessage!, scopeCheck.HttpStatusCode);
        }

        if (string.IsNullOrWhiteSpace(observedOrigin))
        {
            return PairingResult<DeviceChallengeIssued>.Fail(
                PairingErrorCodes.OriginRequired,
                "Server-observed request origin is required.",
                httpStatusCode: 400);
        }

        string rateKey = $"challenge:{deviceId:D}:{observedOrigin.Trim()}";
        PairingResult rateCheck = _rateLimiter.CheckAllowed(rateKey);
        if (rateCheck.IsFailure)
        {
            return PairingResult<DeviceChallengeIssued>.Fail(rateCheck.ErrorCode!, rateCheck.ErrorMessage!, rateCheck.HttpStatusCode);
        }

        EnrolledDeviceRecord? device = _enrollmentService.GetDevice(deviceId);
        if (device is null)
        {
            _rateLimiter.RecordFailure(rateKey);
            return PairingResult<DeviceChallengeIssued>.Fail(
                PairingErrorCodes.DeviceNotFound,
                "Device is not enrolled.",
                httpStatusCode: 404);
        }

        PairingResult deviceScopeMatch = ProductScopeGuard.ValidateExactMatch(scope, device.Scope);
        if (deviceScopeMatch.IsFailure)
        {
            _rateLimiter.RecordFailure(rateKey);
            return PairingResult<DeviceChallengeIssued>.Fail(deviceScopeMatch.ErrorCode!, deviceScopeMatch.ErrorMessage!, deviceScopeMatch.HttpStatusCode);
        }

        if (device.Status == DeviceStatus.Revoked)
        {
            return PairingResult<DeviceChallengeIssued>.Fail(
                PairingErrorCodes.DeviceRevoked,
                "Device has been revoked.",
                httpStatusCode: 403);
        }

        if (device.Status != DeviceStatus.Active)
        {
            return PairingResult<DeviceChallengeIssued>.Fail(
                PairingErrorCodes.DeviceDisabled,
                $"Device is in state '{device.Status}' and cannot authenticate.",
                httpStatusCode: 403);
        }

        Guid challengeId = Guid.NewGuid();
        string nonceHex = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateTimeOffset expires = now.Add(PairingConstants.ChallengeLifetime);
        byte[] canonicalPayload = BuildCanonicalChallengePayload(challengeId, nonceHex, deviceId, scope, now);

        ChallengeState state = new()
        {
            ChallengeId = challengeId,
            DeviceId = deviceId,
            Scope = scope,
            ObservedOrigin = observedOrigin.Trim(),
            CanonicalPayload = canonicalPayload,
            IssuedAtUtc = now,
            ExpiresAtUtc = expires,
            Consumed = false
        };

        _challenges[challengeId] = state;

        return PairingResult<DeviceChallengeIssued>.Success(
            new DeviceChallengeIssued(challengeId, nonceHex, now, expires, canonicalPayload));
    }

    public PairingResult<DeviceSessionIssued> VerifyChallengeAndIssueSession(
        Guid challengeId,
        Guid deviceId,
        TenantScope scope,
        byte[] signature,
        string observedOrigin)
    {
        PairingResult scopeCheck = ProductScopeGuard.ValidateProductAndTenant(scope);
        if (scopeCheck.IsFailure)
        {
            return PairingResult<DeviceSessionIssued>.Fail(scopeCheck.ErrorCode!, scopeCheck.ErrorMessage!, scopeCheck.HttpStatusCode);
        }

        string rateKey = $"challenge:{deviceId:D}:{observedOrigin?.Trim()}";
        PairingResult rateCheck = _rateLimiter.CheckAllowed(rateKey);
        if (rateCheck.IsFailure)
        {
            return PairingResult<DeviceSessionIssued>.Fail(rateCheck.ErrorCode!, rateCheck.ErrorMessage!, rateCheck.HttpStatusCode);
        }

        if (!_challenges.TryGetValue(challengeId, out ChallengeState? challenge))
        {
            _rateLimiter.RecordFailure(rateKey);
            return PairingResult<DeviceSessionIssued>.Fail(
                PairingErrorCodes.ChallengeNotFound,
                "Challenge was not found.",
                httpStatusCode: 404);
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        lock (challenge)
        {
            if (challenge.Consumed)
            {
                _rateLimiter.RecordFailure(rateKey);
                return PairingResult<DeviceSessionIssued>.Fail(
                    PairingErrorCodes.ChallengeAlreadyConsumed,
                    "Challenge nonce has already been consumed (replay rejected).",
                    httpStatusCode: 403);
            }

            if (challenge.ExpiresAtUtc <= now)
            {
                challenge.Consumed = true;
                _rateLimiter.RecordFailure(rateKey);
                return PairingResult<DeviceSessionIssued>.Fail(
                    PairingErrorCodes.ChallengeExpired,
                    "Challenge nonce has expired.",
                    httpStatusCode: 403);
            }

            if (challenge.DeviceId != deviceId)
            {
                _rateLimiter.RecordFailure(rateKey);
                return PairingResult<DeviceSessionIssued>.Fail(
                    PairingErrorCodes.DeviceNotFound,
                    "Challenge does not belong to this device.",
                    httpStatusCode: 403);
            }

            PairingResult match = ProductScopeGuard.ValidateExactMatch(scope, challenge.Scope);
            if (match.IsFailure)
            {
                _rateLimiter.RecordFailure(rateKey);
                return PairingResult<DeviceSessionIssued>.Fail(match.ErrorCode!, match.ErrorMessage!, match.HttpStatusCode);
            }

            if (!string.Equals(challenge.ObservedOrigin, observedOrigin?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                _rateLimiter.RecordFailure(rateKey);
                return PairingResult<DeviceSessionIssued>.Fail(
                    PairingErrorCodes.OriginMismatch,
                    "Challenge verification origin does not match issued origin.",
                    httpStatusCode: 403);
            }

            EnrolledDeviceRecord? device = _enrollmentService.GetDevice(deviceId);
            if (device is null)
            {
                return PairingResult<DeviceSessionIssued>.Fail(
                    PairingErrorCodes.DeviceNotFound,
                    "Device is not enrolled.",
                    httpStatusCode: 404);
            }

            if (device.Status == DeviceStatus.Revoked)
            {
                challenge.Consumed = true;
                return PairingResult<DeviceSessionIssued>.Fail(
                    PairingErrorCodes.DeviceRevoked,
                    "Device has been revoked.",
                    httpStatusCode: 403);
            }

            if (device.Status != DeviceStatus.Active)
            {
                return PairingResult<DeviceSessionIssued>.Fail(
                    PairingErrorCodes.DeviceDisabled,
                    "Device is not active.",
                    httpStatusCode: 403);
            }

            if (!SecretCrypto.VerifyEcdsaP256(device.SpkiPublicKey, challenge.CanonicalPayload, signature))
            {
                _rateLimiter.RecordFailure(rateKey);
                return PairingResult<DeviceSessionIssued>.Fail(
                    PairingErrorCodes.InvalidSignature,
                    "Asymmetric ECDSA P-256 challenge signature verification failed.",
                    httpStatusCode: 403);
            }

            // Single-use nonce consumed atomically after valid signature verification.
            challenge.Consumed = true;
            _rateLimiter.RecordSuccess(rateKey);

            Guid sessionId = Guid.NewGuid();
            string rawToken = SecretCrypto.GenerateHighEntropyToken(32);
            byte[] tokenHash = SecretCrypto.ComputeSha256(rawToken);
            DateTimeOffset sessionExpires = now.Add(PairingConstants.DeviceSessionLifetime);

            DeviceTrustPrincipal principal = new(
                DeviceId: device.DeviceId,
                Scope: device.Scope,
                DeviceRole: device.DeviceRole,
                PublicKeyThumbprint: device.PublicKeyThumbprint,
                ObservedOrigin: challenge.ObservedOrigin,
                SessionId: sessionId,
                SessionExpiresAtUtc: sessionExpires);

            _sessions[sessionId] = new StoredDeviceSession
            {
                SessionId = sessionId,
                DeviceId = device.DeviceId,
                TokenHash = tokenHash,
                Principal = principal,
                ExpiresAtUtc = sessionExpires
            };

            return PairingResult<DeviceSessionIssued>.Success(
                new DeviceSessionIssued(sessionId, rawToken, now, sessionExpires, principal));
        }
    }

    public PairingResult<DeviceTrustPrincipal> ValidateSession(
        Guid sessionId,
        string? rawSessionToken,
        Guid deviceId,
        TenantScope scope,
        string observedOrigin)
    {
        PairingResult scopeCheck = ProductScopeGuard.ValidateProductAndTenant(scope);
        if (scopeCheck.IsFailure)
        {
            return PairingResult<DeviceTrustPrincipal>.Fail(scopeCheck.ErrorCode!, scopeCheck.ErrorMessage!, scopeCheck.HttpStatusCode);
        }

        if (string.IsNullOrWhiteSpace(rawSessionToken) || !_sessions.TryGetValue(sessionId, out StoredDeviceSession? stored))
        {
            return PairingResult<DeviceTrustPrincipal>.Fail(
                PairingErrorCodes.SessionInvalid,
                "Device session is missing or invalid.",
                httpStatusCode: 401);
        }

        if (stored.RevokedAtUtc.HasValue)
        {
            return PairingResult<DeviceTrustPrincipal>.Fail(
                PairingErrorCodes.SessionRevoked,
                "Device session has been revoked.",
                httpStatusCode: 401);
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        if (stored.ExpiresAtUtc <= now)
        {
            _sessions.TryRemove(sessionId, out _);
            return PairingResult<DeviceTrustPrincipal>.Fail(
                PairingErrorCodes.SessionExpired,
                "Device session has expired.",
                httpStatusCode: 401);
        }

        if (!SecretCrypto.FixedTimeEqualsSecretHash(rawSessionToken, stored.TokenHash))
        {
            return PairingResult<DeviceTrustPrincipal>.Fail(
                PairingErrorCodes.SessionInvalid,
                "Device session token is invalid.",
                httpStatusCode: 401);
        }

        if (stored.DeviceId != deviceId)
        {
            return PairingResult<DeviceTrustPrincipal>.Fail(
                PairingErrorCodes.SessionInvalid,
                "Device session does not match device identity.",
                httpStatusCode: 403);
        }

        PairingResult match = ProductScopeGuard.ValidateExactMatch(scope, stored.Principal.Scope);
        if (match.IsFailure)
        {
            return PairingResult<DeviceTrustPrincipal>.Fail(match.ErrorCode!, match.ErrorMessage!, match.HttpStatusCode);
        }

        if (!string.Equals(stored.Principal.ObservedOrigin, observedOrigin?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return PairingResult<DeviceTrustPrincipal>.Fail(
                PairingErrorCodes.OriginMismatch,
                "Request origin does not match device session origin.",
                httpStatusCode: 403);
        }

        // Re-check live device status on every request (fail-closed on revocation).
        EnrolledDeviceRecord? device = _enrollmentService.GetDevice(deviceId);
        if (device is null || device.Status == DeviceStatus.Revoked)
        {
            RevokeAllSessionsForDevice(deviceId);
            return PairingResult<DeviceTrustPrincipal>.Fail(
                PairingErrorCodes.DeviceRevoked,
                "Device has been revoked.",
                httpStatusCode: 403);
        }

        if (device.Status != DeviceStatus.Active)
        {
            return PairingResult<DeviceTrustPrincipal>.Fail(
                PairingErrorCodes.DeviceDisabled,
                "Device is not active.",
                httpStatusCode: 403);
        }

        return PairingResult<DeviceTrustPrincipal>.Success(stored.Principal);
    }

    public void RevokeAllSessionsForDevice(Guid deviceId)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        foreach (StoredDeviceSession session in _sessions.Values)
        {
            if (session.DeviceId == deviceId)
            {
                session.RevokedAtUtc = now;
            }
        }
    }

    /// <summary>
    /// Enforces strict separation between Device Trust and Operator Identity/RBAC.
    /// Cookie-only or device-only remote requests are always rejected.
    /// </summary>
    public PairingResult<AuthorizedRequestContext> AuthorizeCombinedRequest(
        Guid sessionId,
        string? rawSessionToken,
        Guid deviceId,
        TenantScope scope,
        string observedOrigin,
        OperatorIdentityPrincipal? operatorPrincipal,
        string requiredPermission,
        bool isCookieOnlyAttempt = false)
    {
        if (isCookieOnlyAttempt)
        {
            return PairingResult<AuthorizedRequestContext>.Fail(
                PairingErrorCodes.CookieOnlyRejected,
                "Cookie-only trust is rejected for remote requests; enrolled device trust and operator auth are both required.",
                httpStatusCode: 403);
        }

        PairingResult<DeviceTrustPrincipal> deviceValidation = ValidateSession(
            sessionId,
            rawSessionToken,
            deviceId,
            scope,
            observedOrigin);

        if (deviceValidation.IsFailure)
        {
            return PairingResult<AuthorizedRequestContext>.Fail(
                deviceValidation.ErrorCode!,
                deviceValidation.ErrorMessage!,
                deviceValidation.HttpStatusCode);
        }

        if (operatorPrincipal is null || operatorPrincipal.UserId == Guid.Empty || !operatorPrincipal.IsActive)
        {
            return PairingResult<AuthorizedRequestContext>.Fail(
                PairingErrorCodes.OperatorAuthRequired,
                "An active authenticated operator is required; device trust alone never grants operator authority.",
                httpStatusCode: 401);
        }

        if (!IsOperatorRoleAllowedForDevice(deviceValidation.Value!.DeviceRole, operatorPrincipal.RoleName))
        {
            return PairingResult<AuthorizedRequestContext>.Fail(
                PairingErrorCodes.OperatorRoleNotAllowed,
                $"Operator role '{operatorPrincipal.RoleName}' is not permitted on device role '{deviceValidation.Value.DeviceRole}'.",
                httpStatusCode: 403);
        }

        if (!string.IsNullOrWhiteSpace(requiredPermission) &&
            !operatorPrincipal.Permissions.Contains(requiredPermission, StringComparer.Ordinal))
        {
            return PairingResult<AuthorizedRequestContext>.Fail(
                PairingErrorCodes.PermissionDenied,
                $"Operator lacks required permission '{requiredPermission}'.",
                httpStatusCode: 403);
        }

        return PairingResult<AuthorizedRequestContext>.Success(
            new AuthorizedRequestContext(deviceValidation.Value, operatorPrincipal));
    }

    public static bool IsOperatorRoleAllowedForDevice(DeviceRole deviceRole, string roleName) =>
        deviceRole switch
        {
            DeviceRole.LanThinTerminal => roleName is "Cashier" or "Manager",
            DeviceRole.OfflineAndroidCashier => roleName is "Cashier" or "Manager",
            DeviceRole.OwnerAdminClient => roleName is "Owner" or "Admin",
            DeviceRole.StandbyBackupNode => roleName is "Admin",
            _ => false
        };

    private sealed class ChallengeState
    {
        public required Guid ChallengeId { get; init; }
        public required Guid DeviceId { get; init; }
        public required TenantScope Scope { get; init; }
        public required string ObservedOrigin { get; init; }
        public required byte[] CanonicalPayload { get; init; }
        public required DateTimeOffset IssuedAtUtc { get; init; }
        public required DateTimeOffset ExpiresAtUtc { get; init; }
        public bool Consumed { get; set; }
    }

    private sealed class StoredDeviceSession
    {
        public required Guid SessionId { get; init; }
        public required Guid DeviceId { get; init; }
        public required byte[] TokenHash { get; init; }
        public required DeviceTrustPrincipal Principal { get; init; }
        public required DateTimeOffset ExpiresAtUtc { get; init; }
        public DateTimeOffset? RevokedAtUtc { get; set; }
    }
}
