namespace Salryn.Api.Local.RemotePos;

public sealed record RemotePosJoinRequest(string JoinCode, string DeviceName);

public sealed record RemotePosJoinResponse(
    string RequestId,
    string DeviceName,
    string PairingState,
    string RequestedAtUtc,
    string ExpiresAtUtc,
    string Message);

public sealed record RemotePosPairingStatusResponse(
    string PairingState,
    string? RequestId,
    string? DeviceName,
    string Message,
    bool PairingCodeRequired,
    bool Paired);

public sealed record RemotePosPendingJoinRequestResponse(
    string RequestId,
    string DeviceName,
    string ObservedOrigin,
    string PairingState,
    string RequestedAtUtc,
    string ExpiresAtUtc,
    string? PairingCodeExpiresAtUtc);

public sealed record RemotePosPairingHostStatusResponse(
    bool JoinCodeIssued,
    string? JoinCodeExpiresAtUtc,
    int PendingRequestCount,
    IReadOnlyList<RemotePosPendingJoinRequestResponse> PendingRequests,
    string Message);

public sealed record RemotePosJoinCodeRotateResponse(
    string JoinCode,
    string IssuedAtUtc,
    string ExpiresAtUtc,
    string Message);

public sealed record RemotePosApproveJoinRequest(string? Note = null);

public sealed record RemotePosApproveJoinResponse(
    string RequestId,
    string DeviceName,
    string PairingCode,
    string PairingCodeExpiresAtUtc,
    string Message);

public sealed record RemotePosRejectJoinRequest(string? Reason = null);

public sealed record RemotePosRejectJoinResponse(
    string RequestId,
    string PairingState,
    string Message);

public sealed record RemotePosPairingActivationRequest(string RequestId, string PairingCode);

public sealed record RemotePosPairingActivationResponse(
    string PairingState,
    string? TerminalId,
    string? HandshakeSessionId,
    string? HostId,
    string? StoreId,
    string? TerminalName,
    string? SessionExpiresAtUtc,
    string Message);

public sealed record RemotePosTerminalTrustResponse(
    bool Ok,
    string State,
    string Code,
    string Message,
    string? HostId,
    string? StoreId);
