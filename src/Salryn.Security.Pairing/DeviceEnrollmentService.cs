using System.Collections.Concurrent;

namespace Salryn.Security.Pairing;

public sealed record JoinCodeIssuedResponse(
    string JoinCode,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc);

public sealed record PendingEnrollmentResponse(
    Guid RequestId,
    string DeviceName,
    DeviceRole DeviceRole,
    EnrollmentRequestStatus Status,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset ExpiresAtUtc);

public sealed record ApprovedEnrollmentResponse(
    Guid RequestId,
    string OneTimeActivationToken,
    DateTimeOffset ApprovedAtUtc,
    DateTimeOffset ActivationExpiresAtUtc);

/// <summary>
/// Manages I4 device pairing join-code issuance, capacity-enforced pending requests,
/// host/admin approval, single-use 5-min activation tokens, and online device revocation.
/// </summary>
public sealed class DeviceEnrollmentService
{
    private readonly TimeProvider _timeProvider;
    private readonly SlidingWindowRateLimiter _rateLimiter;
    private readonly object _joinCodeLock = new();
    private readonly object _capacityLock = new();
    private readonly ConcurrentDictionary<Guid, EnrollmentState> _enrollments = new();
    private readonly ConcurrentDictionary<Guid, EnrolledDeviceRecord> _devices = new();

    private byte[]? _activeJoinCodeHash;
    private TenantScope? _activeJoinCodeScope;
    private DateTimeOffset? _activeJoinCodeExpiresAtUtc;
    private int _activeMaxPairedTerminals;

    public event Action<Guid>? DeviceRevoked;

    public DeviceEnrollmentService(
        TimeProvider? timeProvider = null,
        SlidingWindowRateLimiter? rateLimiter = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _rateLimiter = rateLimiter ?? new SlidingWindowRateLimiter(timeProvider: _timeProvider);
    }

    public PairingResult<JoinCodeIssuedResponse> RotateJoinCode(
        TenantScope scope,
        int maxPairedTerminals,
        Guid actorUserId)
    {
        PairingResult scopeCheck = ProductScopeGuard.ValidateProductAndTenant(scope);
        if (scopeCheck.IsFailure)
        {
            return PairingResult<JoinCodeIssuedResponse>.Fail(scopeCheck.ErrorCode!, scopeCheck.ErrorMessage!, scopeCheck.HttpStatusCode);
        }

        if (maxPairedTerminals <= 0)
        {
            return PairingResult<JoinCodeIssuedResponse>.Fail(
                PairingErrorCodes.CapacityExceeded,
                "Host/tenant entitlement must allow at least one paired terminal.",
                httpStatusCode: 403);
        }

        if (actorUserId == Guid.Empty)
        {
            return PairingResult<JoinCodeIssuedResponse>.Fail(
                PairingErrorCodes.OperatorAuthRequired,
                "An authenticated operator is required to issue a join code.",
                httpStatusCode: 401);
        }

        string joinCode = SecretCrypto.GenerateJoinCode();
        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateTimeOffset expires = now.Add(PairingConstants.JoinCodeLifetime);

        lock (_joinCodeLock)
        {
            _activeJoinCodeHash = SecretCrypto.ComputeSha256(joinCode);
            _activeJoinCodeScope = scope;
            _activeJoinCodeExpiresAtUtc = expires;
            _activeMaxPairedTerminals = maxPairedTerminals;
        }

        return PairingResult<JoinCodeIssuedResponse>.Success(
            new JoinCodeIssuedResponse(joinCode, now, expires));
    }

    public PairingResult<PendingEnrollmentResponse> RequestEnrollment(
        string joinCode,
        TenantScope scope,
        string deviceName,
        DeviceRole deviceRole,
        byte[] spkiPublicKey,
        string observedOrigin)
    {
        PairingResult scopeCheck = ProductScopeGuard.ValidateProductAndTenant(scope);
        if (scopeCheck.IsFailure)
        {
            return PairingResult<PendingEnrollmentResponse>.Fail(scopeCheck.ErrorCode!, scopeCheck.ErrorMessage!, scopeCheck.HttpStatusCode);
        }

        if (string.IsNullOrWhiteSpace(observedOrigin))
        {
            return PairingResult<PendingEnrollmentResponse>.Fail(
                PairingErrorCodes.OriginRequired,
                "Server-observed request origin is required.",
                httpStatusCode: 400);
        }

        string rateLimitKey = $"join:{observedOrigin.Trim()}";
        PairingResult rateCheck = _rateLimiter.CheckAllowed(rateLimitKey);
        if (rateCheck.IsFailure)
        {
            return PairingResult<PendingEnrollmentResponse>.Fail(rateCheck.ErrorCode!, rateCheck.ErrorMessage!, rateCheck.HttpStatusCode);
        }

        if (string.IsNullOrWhiteSpace(joinCode))
        {
            _rateLimiter.RecordFailure(rateLimitKey);
            return PairingResult<PendingEnrollmentResponse>.Fail(
                PairingErrorCodes.JoinCodeRequired,
                "Join code is required.",
                httpStatusCode: 400);
        }

        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return PairingResult<PendingEnrollmentResponse>.Fail(
                PairingErrorCodes.DeviceNameRequired,
                "Device display name is required.",
                httpStatusCode: 400);
        }

        PairingResult<string> keyValidation = SecretCrypto.ValidateAndThumbprintEcdsaP256Spki(spkiPublicKey);
        if (keyValidation.IsFailure)
        {
            return PairingResult<PendingEnrollmentResponse>.Fail(keyValidation.ErrorCode!, keyValidation.ErrorMessage!, keyValidation.HttpStatusCode);
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        int maxTerminals;
        lock (_joinCodeLock)
        {
            if (_activeJoinCodeHash is null || _activeJoinCodeExpiresAtUtc is null || _activeJoinCodeScope is null)
            {
                _rateLimiter.RecordFailure(rateLimitKey);
                return PairingResult<PendingEnrollmentResponse>.Fail(
                    PairingErrorCodes.JoinCodeInvalid,
                    "No active join code has been issued.",
                    httpStatusCode: 403);
            }

            if (_activeJoinCodeExpiresAtUtc.Value <= now)
            {
                _rateLimiter.RecordFailure(rateLimitKey);
                return PairingResult<PendingEnrollmentResponse>.Fail(
                    PairingErrorCodes.JoinCodeExpired,
                    "Join code has expired. Rotate a new join code on the host.",
                    httpStatusCode: 403);
            }

            PairingResult match = ProductScopeGuard.ValidateExactMatch(scope, _activeJoinCodeScope);
            if (match.IsFailure)
            {
                _rateLimiter.RecordFailure(rateLimitKey);
                return PairingResult<PendingEnrollmentResponse>.Fail(match.ErrorCode!, match.ErrorMessage!, match.HttpStatusCode);
            }

            if (!SecretCrypto.FixedTimeEqualsSecretHash(joinCode, _activeJoinCodeHash))
            {
                _rateLimiter.RecordFailure(rateLimitKey);
                return PairingResult<PendingEnrollmentResponse>.Fail(
                    PairingErrorCodes.JoinCodeInvalid,
                    "Join code is invalid.",
                    httpStatusCode: 403);
            }

            maxTerminals = _activeMaxPairedTerminals;
        }

        _rateLimiter.RecordSuccess(rateLimitKey);

        lock (_capacityLock)
        {
            ExpirePendingRequests(now);

            int activeCount = CountActiveDevices(scope);
            if (activeCount >= maxTerminals)
            {
                return PairingResult<PendingEnrollmentResponse>.Fail(
                    PairingErrorCodes.CapacityExceeded,
                    $"Paired terminal entitlement capacity ({maxTerminals}) has been reached.",
                    httpStatusCode: 403);
            }

            EnrollmentState state = new()
            {
                RequestId = Guid.NewGuid(),
                Scope = scope,
                DeviceName = deviceName.Trim(),
                DeviceRole = deviceRole,
                SpkiPublicKey = (byte[])spkiPublicKey.Clone(),
                PublicKeyThumbprint = keyValidation.Value!,
                ObservedOrigin = observedOrigin.Trim(),
                RequestedAtUtc = now,
                ExpiresAtUtc = now.Add(PairingConstants.PendingRequestLifetime),
                Status = EnrollmentRequestStatus.Pending
            };

            _enrollments[state.RequestId] = state;

            return PairingResult<PendingEnrollmentResponse>.Success(
                new PendingEnrollmentResponse(
                    state.RequestId,
                    state.DeviceName,
                    state.DeviceRole,
                    state.Status,
                    state.RequestedAtUtc,
                    state.ExpiresAtUtc),
                httpStatusCode: 201);
        }
    }

    public PairingResult<ApprovedEnrollmentResponse> ApproveEnrollment(
        Guid requestId,
        TenantScope expectedScope,
        Guid approverUserId,
        int maxPairedTerminals)
    {
        PairingResult scopeCheck = ProductScopeGuard.ValidateProductAndTenant(expectedScope);
        if (scopeCheck.IsFailure)
        {
            return PairingResult<ApprovedEnrollmentResponse>.Fail(scopeCheck.ErrorCode!, scopeCheck.ErrorMessage!, scopeCheck.HttpStatusCode);
        }

        if (approverUserId == Guid.Empty)
        {
            return PairingResult<ApprovedEnrollmentResponse>.Fail(
                PairingErrorCodes.OperatorAuthRequired,
                "An authenticated approver is required.",
                httpStatusCode: 401);
        }

        if (!_enrollments.TryGetValue(requestId, out EnrollmentState? state))
        {
            return PairingResult<ApprovedEnrollmentResponse>.Fail(
                PairingErrorCodes.EnrollmentNotFound,
                "Enrollment request was not found.",
                httpStatusCode: 404);
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        lock (_capacityLock)
        {
            lock (state)
            {
                PairingResult scopeMatch = ProductScopeGuard.ValidateExactMatch(state.Scope, expectedScope);
                if (scopeMatch.IsFailure)
                {
                    return PairingResult<ApprovedEnrollmentResponse>.Fail(scopeMatch.ErrorCode!, scopeMatch.ErrorMessage!, scopeMatch.HttpStatusCode);
                }

                if (state.Status == EnrollmentRequestStatus.Pending && state.ExpiresAtUtc <= now)
                {
                    state.Status = EnrollmentRequestStatus.Expired;
                }

                if (state.Status == EnrollmentRequestStatus.Expired)
                {
                    return PairingResult<ApprovedEnrollmentResponse>.Fail(
                        PairingErrorCodes.EnrollmentExpired,
                        "Enrollment request has expired.",
                        httpStatusCode: 403);
                }

                if (state.Status != EnrollmentRequestStatus.Pending)
                {
                    return PairingResult<ApprovedEnrollmentResponse>.Fail(
                        PairingErrorCodes.EnrollmentNotPending,
                        $"Enrollment request is in state '{state.Status}' and cannot be approved.",
                        httpStatusCode: 409);
                }

                int activeCount = CountActiveDevices(expectedScope);
                if (activeCount >= maxPairedTerminals)
                {
                    return PairingResult<ApprovedEnrollmentResponse>.Fail(
                        PairingErrorCodes.CapacityExceeded,
                        $"Paired terminal entitlement capacity ({maxPairedTerminals}) has been reached.",
                        httpStatusCode: 403);
                }

                string rawActivationToken = SecretCrypto.GenerateHighEntropyToken(32);
                DateTimeOffset activationExpires = now.Add(PairingConstants.ActivationTokenLifetime);

                state.Status = EnrollmentRequestStatus.Approved;
                state.ApprovedByUserId = approverUserId;
                state.ApprovedAtUtc = now;
                state.ActivationTokenHash = SecretCrypto.ComputeSha256(rawActivationToken);
                state.ActivationExpiresAtUtc = activationExpires;

                return PairingResult<ApprovedEnrollmentResponse>.Success(
                    new ApprovedEnrollmentResponse(
                        state.RequestId,
                        rawActivationToken,
                        now,
                        activationExpires));
            }
        }
    }

    public PairingResult<EnrolledDeviceRecord> ActivateDevice(
        Guid requestId,
        string oneTimeActivationToken,
        TenantScope scope,
        string observedOrigin)
    {
        PairingResult scopeCheck = ProductScopeGuard.ValidateProductAndTenant(scope);
        if (scopeCheck.IsFailure)
        {
            return PairingResult<EnrolledDeviceRecord>.Fail(scopeCheck.ErrorCode!, scopeCheck.ErrorMessage!, scopeCheck.HttpStatusCode);
        }

        if (string.IsNullOrWhiteSpace(observedOrigin))
        {
            return PairingResult<EnrolledDeviceRecord>.Fail(
                PairingErrorCodes.OriginRequired,
                "Server-observed request origin is required.",
                httpStatusCode: 400);
        }

        string rateKey = $"activate:{observedOrigin.Trim()}";
        PairingResult rateCheck = _rateLimiter.CheckAllowed(rateKey);
        if (rateCheck.IsFailure)
        {
            return PairingResult<EnrolledDeviceRecord>.Fail(rateCheck.ErrorCode!, rateCheck.ErrorMessage!, rateCheck.HttpStatusCode);
        }

        if (!_enrollments.TryGetValue(requestId, out EnrollmentState? state))
        {
            _rateLimiter.RecordFailure(rateKey);
            return PairingResult<EnrolledDeviceRecord>.Fail(
                PairingErrorCodes.EnrollmentNotFound,
                "Enrollment request was not found.",
                httpStatusCode: 404);
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        lock (state)
        {
            PairingResult match = ProductScopeGuard.ValidateExactMatch(scope, state.Scope);
            if (match.IsFailure)
            {
                _rateLimiter.RecordFailure(rateKey);
                return PairingResult<EnrolledDeviceRecord>.Fail(match.ErrorCode!, match.ErrorMessage!, match.HttpStatusCode);
            }

            if (state.Status == EnrollmentRequestStatus.Activated)
            {
                _rateLimiter.RecordFailure(rateKey);
                return PairingResult<EnrolledDeviceRecord>.Fail(
                    PairingErrorCodes.ActivationTokenAlreadyConsumed,
                    "Activation token has already been consumed.",
                    httpStatusCode: 409);
            }

            if (state.Status != EnrollmentRequestStatus.Approved || state.ActivationTokenHash is null || state.ActivationExpiresAtUtc is null)
            {
                _rateLimiter.RecordFailure(rateKey);
                return PairingResult<EnrolledDeviceRecord>.Fail(
                    PairingErrorCodes.EnrollmentNotApproved,
                    "Enrollment request is not approved for activation.",
                    httpStatusCode: 403);
            }

            if (state.ActivationExpiresAtUtc.Value <= now)
            {
                state.Status = EnrollmentRequestStatus.Expired;
                _rateLimiter.RecordFailure(rateKey);
                return PairingResult<EnrolledDeviceRecord>.Fail(
                    PairingErrorCodes.ActivationTokenExpired,
                    "One-time activation token has expired.",
                    httpStatusCode: 403);
            }

            if (!string.Equals(state.ObservedOrigin, observedOrigin.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                _rateLimiter.RecordFailure(rateKey);
                return PairingResult<EnrolledDeviceRecord>.Fail(
                    PairingErrorCodes.OriginMismatch,
                    "Activation origin does not match the enrollment origin.",
                    httpStatusCode: 403);
            }

            if (!SecretCrypto.FixedTimeEqualsSecretHash(oneTimeActivationToken, state.ActivationTokenHash))
            {
                _rateLimiter.RecordFailure(rateKey);
                return PairingResult<EnrolledDeviceRecord>.Fail(
                    PairingErrorCodes.ActivationTokenInvalid,
                    "Activation token is invalid.",
                    httpStatusCode: 403);
            }

            // Atomic single-consume: mark activated before releasing lock.
            state.Status = EnrollmentRequestStatus.Activated;
            state.ActivationTokenHash = null;

            EnrolledDeviceRecord record = new(
                DeviceId: Guid.NewGuid(),
                Scope: state.Scope,
                DeviceName: state.DeviceName,
                DeviceRole: state.DeviceRole,
                Status: DeviceStatus.Active,
                SpkiPublicKey: (byte[])state.SpkiPublicKey.Clone(),
                PublicKeyThumbprint: state.PublicKeyThumbprint,
                EnrolledAtUtc: now,
                ApprovedByUserId: state.ApprovedByUserId!.Value);

            _devices[record.DeviceId] = record;
            _rateLimiter.RecordSuccess(rateKey);

            return PairingResult<EnrolledDeviceRecord>.Success(record, httpStatusCode: 201);
        }
    }

    public EnrolledDeviceRecord? GetDevice(Guid deviceId) =>
        _devices.TryGetValue(deviceId, out EnrolledDeviceRecord? device) ? device : null;

    public IReadOnlyList<EnrolledDeviceRecord> ListDevices(TenantScope scope) =>
        _devices.Values
            .Where(d => d.Scope == scope)
            .OrderBy(d => d.EnrolledAtUtc)
            .ToArray();

    public PairingResult<EnrolledDeviceRecord> RevokeDevice(
        Guid deviceId,
        TenantScope expectedScope,
        Guid actorUserId,
        string reason)
    {
        if (actorUserId == Guid.Empty)
        {
            return PairingResult<EnrolledDeviceRecord>.Fail(
                PairingErrorCodes.OperatorAuthRequired,
                "An authenticated operator is required to revoke a device.",
                httpStatusCode: 401);
        }

        if (!_devices.TryGetValue(deviceId, out EnrolledDeviceRecord? existing))
        {
            return PairingResult<EnrolledDeviceRecord>.Fail(
                PairingErrorCodes.DeviceNotFound,
                "Device not found.",
                httpStatusCode: 404);
        }

        PairingResult scopeMatch = ProductScopeGuard.ValidateExactMatch(existing.Scope, expectedScope);
        if (scopeMatch.IsFailure)
        {
            return PairingResult<EnrolledDeviceRecord>.Fail(scopeMatch.ErrorCode!, scopeMatch.ErrorMessage!, scopeMatch.HttpStatusCode);
        }

        EnrolledDeviceRecord revoked = existing with
        {
            Status = DeviceStatus.Revoked,
            RevokedAtUtc = _timeProvider.GetUtcNow(),
            RevocationReason = string.IsNullOrWhiteSpace(reason) ? "Revoked by administrator." : reason.Trim()
        };

        _devices[deviceId] = revoked;
        DeviceRevoked?.Invoke(deviceId);

        return PairingResult<EnrolledDeviceRecord>.Success(revoked);
    }

    private int CountActiveDevices(TenantScope scope) =>
        _devices.Values.Count(d => d.Scope == scope && d.Status == DeviceStatus.Active);

    private void ExpirePendingRequests(DateTimeOffset now)
    {
        foreach (EnrollmentState state in _enrollments.Values)
        {
            lock (state)
            {
                if (state.Status == EnrollmentRequestStatus.Pending && state.ExpiresAtUtc <= now)
                {
                    state.Status = EnrollmentRequestStatus.Expired;
                }
            }
        }
    }

    private sealed class EnrollmentState
    {
        public required Guid RequestId { get; init; }
        public required TenantScope Scope { get; init; }
        public required string DeviceName { get; init; }
        public required DeviceRole DeviceRole { get; init; }
        public required byte[] SpkiPublicKey { get; init; }
        public required string PublicKeyThumbprint { get; init; }
        public required string ObservedOrigin { get; init; }
        public required DateTimeOffset RequestedAtUtc { get; init; }
        public required DateTimeOffset ExpiresAtUtc { get; init; }
        public EnrollmentRequestStatus Status { get; set; }
        public Guid? ApprovedByUserId { get; set; }
        public DateTimeOffset? ApprovedAtUtc { get; set; }
        public byte[]? ActivationTokenHash { get; set; }
        public DateTimeOffset? ActivationExpiresAtUtc { get; set; }
    }
}
