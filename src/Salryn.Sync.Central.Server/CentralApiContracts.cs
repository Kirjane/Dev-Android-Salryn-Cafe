using Salryn.Sync.Contracts;

namespace Salryn.Sync.Central.Server;

public sealed record ErrorResponseDto(
    string ErrorCode,
    string ErrorMessage,
    long? ServerEpoch = null);

public sealed record HealthResponseDto(
    string Status,
    string ProductId,
    Guid CentralInstanceId,
    long ServerEpoch,
    long HeadSequence,
    int SchemaVersion,
    DateTimeOffset TimestampUtc);

// --- Admin Auth ---
public sealed record AdminLoginRequest(string Username, string Password);

public sealed record AdminSessionDto(
    Guid AdminId,
    Guid TenantId,
    Guid StoreId,
    string ProductId,
    string Username,
    string DisplayName,
    string Role,
    DateTimeOffset ExpiresAtUtc);

public sealed record AdminLoginResponse(
    string AccessToken,
    AdminSessionDto Session);

// --- Invites & Device Enrollment ---
public sealed record CreateInviteRequest(
    string DeviceRole);

public sealed record CreateInviteResponse(
    Guid InviteId,
    string JoinCode,
    string DeviceRole,
    DateTimeOffset ExpiresAtUtc);

public sealed record EnrollDeviceRequest(
    string JoinCode,
    Guid TenantId,
    Guid StoreId,
    string ProductId,
    string DeviceName,
    string PublicKeySpkiBase64);

public sealed record EnrollDeviceResponse(
    Guid DeviceId,
    string Status,
    string DeviceRole,
    string PublicKeyThumbprint);

public sealed record DeviceChallengeRequest(
    Guid DeviceId,
    Guid TenantId,
    Guid StoreId,
    string ProductId);

public sealed record DeviceChallengeResponse(
    string ChallengeId,
    string NonceBase64,
    DateTimeOffset ExpiresAtUtc);

public sealed record DeviceTokenRequest(
    string ChallengeId,
    Guid DeviceId,
    Guid TenantId,
    Guid StoreId,
    string ProductId,
    string SignatureBase64);

public sealed record SignedOfflineLeaseV1(
    Guid DeviceId,
    Guid TenantId,
    Guid StoreId,
    string ProductId,
    long ServerEpoch,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string SignatureBase64,
    string ServerPublicKeySpkiBase64);

public sealed record DeviceTokenResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    long ServerEpoch,
    SignedOfflineLeaseV1 OfflineLease);

// --- Sync Push / Pull / Bootstrap ---
public sealed record PushOperationsRequest(
    long KnownEpoch,
    IReadOnlyList<SyncOperationEnvelopeV1> Operations);

public sealed record PushOperationResultDto(
    Guid OperationId,
    bool IsDuplicateReplay,
    SyncOperationFinalResolutionV1 Resolution,
    long? ChangeFeedSequence);

public sealed record PushOperationsResponse(
    Guid CentralInstanceId,
    long ServerEpoch,
    long HeadSequence,
    IReadOnlyList<PushOperationResultDto> Results);

public sealed record RevokedFencedResponseDto(
    string ErrorCode,
    string ErrorMessage,
    long ServerEpoch,
    IReadOnlyList<Guid> QuarantinedOperationIds);

public sealed record BootstrapResponseDto(
    string ProductId,
    long ServerEpoch,
    long HeadSequence,
    SyncBootstrapSnapshotV1 Snapshot);

public sealed record PullChangesResponseDto(
    string ProductId,
    long ServerEpoch,
    SyncChangeFeedPageV1 Page);

// --- Admin Management ---
public sealed record DeviceSummaryDto(
    Guid DeviceId,
    Guid TenantId,
    Guid StoreId,
    string ProductId,
    string DeviceName,
    string DeviceRole,
    string PublicKeyThumbprint,
    string Status,
    DateTimeOffset EnrolledAtUtc,
    DateTimeOffset? ApprovedAtUtc,
    DateTimeOffset? RevokedAtUtc,
    string? RevokeReason,
    DateTimeOffset? LastSeenAtUtc);

public sealed record RevokeDeviceRequest(string? Reason);

public sealed record QuarantineItemDto(
    Guid QuarantineId,
    Guid TenantId,
    Guid StoreId,
    Guid DeviceId,
    Guid OperationId,
    string OperationType,
    string OperationHash,
    string PayloadHash,
    string EnvelopeJson,
    DateTimeOffset ReceivedAtUtc,
    string ReviewStatus,
    string? ReviewNote,
    Guid? ReviewedByAdminId,
    DateTimeOffset? ReviewedAtUtc);

public sealed record ReviewQuarantineRequest(
    string ReviewStatus,
    string? ReviewNote);

public sealed record EpochBumpRequest(string Reason);

public sealed record EpochBumpResponse(
    long PreviousEpoch,
    long NewEpoch,
    DateTimeOffset BumpedAtUtc);
