namespace Salryn.Security.Pairing;

public static class PairingConstants
{
    public const string CanonicalProductId = "Salryn.Cafe";
    public const int DefaultLoopbackPort = 5399;
    public const int DefaultLanHttpsPort = 5400;

    public static readonly TimeSpan JoinCodeLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan PendingRequestLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan ActivationTokenLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan DeviceSessionLifetime = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan DefaultOfflineLeaseLifetime = TimeSpan.FromHours(24);
    public static readonly TimeSpan ClockRollbackTolerance = TimeSpan.FromMinutes(5);
}

public static class PairingErrorCodes
{
    public const string ProductScopeMismatch = "Pairing.ProductScopeMismatch";
    public const string TenantScopeMismatch = "Pairing.TenantScopeMismatch";
    public const string StoreScopeMismatch = "Pairing.StoreScopeMismatch";
    public const string RateLimitExceeded = "Pairing.RateLimitExceeded";
    public const string JoinCodeRequired = "Pairing.JoinCodeRequired";
    public const string JoinCodeInvalid = "Pairing.JoinCodeInvalid";
    public const string JoinCodeExpired = "Pairing.JoinCodeExpired";
    public const string CapacityExceeded = "Pairing.CapacityExceeded";
    public const string DeviceNameRequired = "Pairing.DeviceNameRequired";
    public const string InvalidPublicKey = "Pairing.InvalidPublicKey";
    public const string OriginRequired = "Pairing.OriginRequired";
    public const string EnrollmentNotFound = "Pairing.EnrollmentNotFound";
    public const string EnrollmentExpired = "Pairing.EnrollmentExpired";
    public const string EnrollmentNotPending = "Pairing.EnrollmentNotPending";
    public const string EnrollmentNotApproved = "Pairing.EnrollmentNotApproved";
    public const string ActivationTokenInvalid = "Pairing.ActivationTokenInvalid";
    public const string ActivationTokenExpired = "Pairing.ActivationTokenExpired";
    public const string ActivationTokenAlreadyConsumed = "Pairing.ActivationTokenAlreadyConsumed";
    public const string DeviceNotFound = "Pairing.DeviceNotFound";
    public const string DeviceRevoked = "DEVICE_REVOKED";
    public const string DeviceRevokedFenced = "DEVICE_REVOKED_FENCED";
    public const string DeviceDisabled = "Pairing.DeviceDisabled";
    public const string ChallengeNotFound = "Pairing.ChallengeNotFound";
    public const string ChallengeExpired = "Pairing.ChallengeExpired";
    public const string ChallengeAlreadyConsumed = "Pairing.ChallengeAlreadyConsumed";
    public const string InvalidSignature = "Pairing.InvalidSignature";
    public const string OriginMismatch = "Pairing.OriginMismatch";
    public const string SessionInvalid = "Pairing.SessionInvalid";
    public const string SessionExpired = "Pairing.SessionExpired";
    public const string SessionRevoked = "Pairing.SessionRevoked";
    public const string CookieOnlyRejected = "Pairing.CookieOnlyRejected";
    public const string OperatorAuthRequired = "Pairing.OperatorAuthRequired";
    public const string OperatorRoleNotAllowed = "Pairing.OperatorRoleNotAllowed";
    public const string PermissionDenied = "Pairing.PermissionDenied";
    public const string OfflineLeaseInvalidSignature = "Pairing.OfflineLeaseInvalidSignature";
    public const string OfflineLeaseDeviceMismatch = "Pairing.OfflineLeaseDeviceMismatch";
    public const string OfflineLeaseExpired = "Pairing.OfflineLeaseExpired";
    public const string ClockRollbackDetected = "LEASE_CLOCK_TAMPER_LOCKED";
    public const string MonotonicClockRollbackDetected = "LEASE_MONOTONIC_ROLLBACK_LOCKED";
    public const string PlaintextTransportRejected = "Pairing.PlaintextTransportRejected";
    public const string InvalidEndpointOrigin = "Pairing.InvalidEndpointOrigin";
}

public enum DeviceRole
{
    LanThinTerminal = 1,
    OfflineAndroidCashier = 2,
    OwnerAdminClient = 3,
    StandbyBackupNode = 4
}

public enum DeviceStatus
{
    Pending = 1,
    Active = 2,
    Disabled = 3,
    Revoked = 4
}

public enum EnrollmentRequestStatus
{
    Pending = 1,
    Approved = 2,
    Activated = 3,
    Rejected = 4,
    Expired = 5
}

public enum PrinterStationRole
{
    Receipt = 1,
    Kitchen = 2
}

public sealed record TenantScope(string ProductId, Guid TenantId, Guid StoreId);

public sealed record PairingResult(
    bool IsSuccess,
    string? ErrorCode,
    string? ErrorMessage,
    int HttpStatusCode)
{
    public bool IsFailure => !IsSuccess;

    public static PairingResult Success() => new(true, null, null, 200);

    public static PairingResult Fail(string errorCode, string errorMessage, int httpStatusCode = 403) =>
        new(false, errorCode, errorMessage, httpStatusCode);
}

public sealed record PairingResult<T>(
    bool IsSuccess,
    T? Value,
    string? ErrorCode,
    string? ErrorMessage,
    int HttpStatusCode)
{
    public bool IsFailure => !IsSuccess;

    public static PairingResult<T> Success(T value, int httpStatusCode = 200) =>
        new(true, value, null, null, httpStatusCode);

    public static PairingResult<T> Fail(string errorCode, string errorMessage, int httpStatusCode = 403) =>
        new(false, default, errorCode, errorMessage, httpStatusCode);
}

public sealed record DeviceTrustPrincipal(
    Guid DeviceId,
    TenantScope Scope,
    DeviceRole DeviceRole,
    string PublicKeyThumbprint,
    string ObservedOrigin,
    Guid SessionId,
    DateTimeOffset SessionExpiresAtUtc);

public sealed record OperatorIdentityPrincipal(
    Guid UserId,
    string Username,
    string RoleName,
    bool IsActive,
    IReadOnlyCollection<string> Permissions);

public sealed record AuthorizedRequestContext(
    DeviceTrustPrincipal Device,
    OperatorIdentityPrincipal Operator);

public sealed record EnrolledDeviceRecord(
    Guid DeviceId,
    TenantScope Scope,
    string DeviceName,
    DeviceRole DeviceRole,
    DeviceStatus Status,
    byte[] SpkiPublicKey,
    string PublicKeyThumbprint,
    DateTimeOffset EnrolledAtUtc,
    Guid ApprovedByUserId,
    DateTimeOffset? RevokedAtUtc = null,
    string? RevocationReason = null,
    DateTimeOffset? DisabledAtUtc = null);
