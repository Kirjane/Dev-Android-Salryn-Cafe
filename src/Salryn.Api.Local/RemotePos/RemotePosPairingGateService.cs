using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Salryn.Api.Local.Bridge;
using Salryn.Api.Local.Composition;
using Salryn.Api.Local.Services;
using Salryn.Core.Common;
using Salryn.Core.Host;
using Salryn.Core.Services;
using Salryn.Core.Services.Licensing;
using Salryn.Core.TerminalRegistry;
using Salryn.Infrastructure.Licensing;

namespace Salryn.Api.Local.RemotePos;

public sealed class RemotePosPairingGateService
{
    public static readonly TimeSpan JoinCodeLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan PendingRequestLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan PairingCodeLifetime = TimeSpan.FromMinutes(5);
    private const int DeviceNameMaxLength = 40;

    private readonly LanRemoteTransportApiServices _lanServices;
    private readonly RemotePosCapacityPolicy _capacityPolicy;
    private readonly LocalLanTerminalEntitlementService _entitlements;
    private readonly ConcurrentDictionary<Guid, PairingRequestState> _requests = new();
    private readonly object _joinCodeLock = new();
    private byte[]? _currentJoinCodeHash;
    private DateTimeOffset? _joinCodeIssuedAtUtc;
    private DateTimeOffset? _joinCodeExpiresAtUtc;

    public RemotePosPairingGateService(
        LanRemoteTransportApiServices lanServices,
        RemotePosCapacityPolicy capacityPolicy,
        LocalLanTerminalEntitlementService? entitlements = null)
    {
        _lanServices = lanServices ?? throw new ArgumentNullException(nameof(lanServices));
        _capacityPolicy = capacityPolicy ?? throw new ArgumentNullException(nameof(capacityPolicy));
        _entitlements = entitlements ?? new LocalLanTerminalEntitlementService();
    }

    public RemotePosPairingHostStatusResponse GetHostStatus()
    {
        ExpireRequests();
        DateTimeOffset? joinExpiry = _joinCodeExpiresAtUtc;
        bool joinIssued = _currentJoinCodeHash is not null && joinExpiry is not null && joinExpiry > DateTimeOffset.UtcNow;

        return new RemotePosPairingHostStatusResponse(
            JoinCodeIssued: joinIssued,
            JoinCodeExpiresAtUtc: joinIssued ? joinExpiry?.ToString("O") : null,
            PendingRequestCount: _requests.Values.Count(request => request.Status == RemotePosPairingRequestStatus.Pending),
            PendingRequests: _requests.Values
                .OrderByDescending(request => request.RequestedAtUtc)
                .Select(ToPendingResponse)
                .ToArray(),
            Message: joinIssued
                ? "Remote POS join code is active. Share it only with the device being paired."
                : "No active Remote POS join code. Rotate a join code from the POS host before pairing a device.");
    }

    public async Task<Result<RemotePosTerminalLicenseValidationResponse>> ValidateTerminalLicenseAsync(
        RemotePosTerminalLicenseValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        LocalLanTerminalEntitlementStatus entitlement = await _entitlements
            .GetHostEntitlementAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!entitlement.HostOperationalLanAllowed)
        {
            return Result<RemotePosTerminalLicenseValidationResponse>.Fail(Error.Forbidden(
                LocalLanTerminalEntitlementCodes.HostLicenseRequired,
                entitlement.Message));
        }

        Result<ValidatedTerminalLicenseProof> proof = _entitlements.ValidateTerminalLicenseProof(
            request.TerminalLicenseJson,
            request.TerminalMachineCode,
            entitlement);
        if (proof.IsFailure)
        {
            return Result<RemotePosTerminalLicenseValidationResponse>.Fail(proof.ErrorDetail);
        }

        ValidatedTerminalLicenseProof value = proof.Value;
        return Result<RemotePosTerminalLicenseValidationResponse>.Success(new(
            value.LicenseId,
            value.CustomerName,
            value.StoreId,
            value.HostLicenseId,
            value.TerminalId,
            value.MachineCode,
            value.ExpiresAt?.ToString("O"),
            "Terminal license is valid for this device and POS Host."));
    }

    public Result<RemotePosJoinCodeRotateResponse> RotateJoinCode()
    {
        LocalLanTerminalEntitlementStatus entitlement = _entitlements
            .GetHostEntitlementAsync()
            .GetAwaiter()
            .GetResult();
        if (!entitlement.HostOperationalLanAllowed || entitlement.EffectiveMaxPairedTerminals <= 0)
        {
            return Result<RemotePosJoinCodeRotateResponse>.Fail(Error.Forbidden(
                LocalLanTerminalEntitlementCodes.NoTerminalsPurchased,
                entitlement.Message + " Pairing join codes stay disabled until Host LAN terminal entitlement is present."));
        }

        string joinCode = GenerateShortCode();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset expires = now.Add(JoinCodeLifetime);

        lock (_joinCodeLock)
        {
            _currentJoinCodeHash = HashSecret(joinCode);
            _joinCodeIssuedAtUtc = now;
            _joinCodeExpiresAtUtc = expires;
        }

        return Result<RemotePosJoinCodeRotateResponse>.Success(new RemotePosJoinCodeRotateResponse(
            JoinCode: joinCode,
            IssuedAtUtc: now.ToString("O"),
            ExpiresAtUtc: expires.ToString("O"),
            Message: "Show this short-lived join code on the POS host. The code only opens a pending request; it does not pair or log in the device."));
    }

    public Result<RemotePosJoinResponse> RequestJoin(
        RemotePosJoinRequest request,
        RemoteReadOnlyBridgeRequestContext bridgeContext)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bridgeContext);
        ExpireRequests();

        if (string.IsNullOrWhiteSpace(request.JoinCode))
        {
            return Result<RemotePosJoinResponse>.Fail(Error.Validation(
                "RemotePosPairing.JoinCodeRequired",
                "Host join code is required."));
        }

        if (string.IsNullOrWhiteSpace(request.DeviceName))
        {
            return Result<RemotePosJoinResponse>.Fail(Error.Validation(
                "RemotePosPairing.DeviceNameRequired",
                "Device display name is required."));
        }

        if (!IsCurrentJoinCodeAccepted(request.JoinCode, out string joinCodeFailureCode))
        {
            string message = joinCodeFailureCode == "RemotePosPairing.JoinCodeExpired"
                ? "Host join code has expired. Ask the POS host operator to rotate a new code."
                : "Host join code was rejected.";

            return Result<RemotePosJoinResponse>.Fail(Error.Forbidden(joinCodeFailureCode, message));
        }

        string deviceName = SanitizeDeviceName(request.DeviceName);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset expires = now.Add(PendingRequestLifetime);
        PairingRequestState state = new()
        {
            RequestId = Guid.NewGuid(),
            DeviceName = deviceName,
            ObservedOrigin = string.IsNullOrWhiteSpace(bridgeContext.ObservedOrigin)
                ? RemoteReadOnlyBridgeRequestContext.LoopbackObservedOrigin
                : bridgeContext.ObservedOrigin,
            RequestedAtUtc = now,
            ExpiresAtUtc = expires,
            Status = RemotePosPairingRequestStatus.Pending
        };

        _requests[state.RequestId] = state;

        return Result<RemotePosJoinResponse>.Success(new RemotePosJoinResponse(
            RequestId: state.RequestId.ToString("D"),
            DeviceName: state.DeviceName,
            PairingState: state.Status.ToString(),
            RequestedAtUtc: state.RequestedAtUtc.ToString("O"),
            ExpiresAtUtc: state.ExpiresAtUtc.ToString("O"),
            Message: "Join request submitted. Wait for the host to approve this device."));
    }

    public Result<RemotePosPairingStatusResponse> GetRemoteStatus(string? requestId)
    {
        ExpireRequests();

        if (string.IsNullOrWhiteSpace(requestId) || !Guid.TryParse(requestId, out Guid parsedRequestId))
        {
            return Result<RemotePosPairingStatusResponse>.Success(new RemotePosPairingStatusResponse(
                PairingState: "NotRequested",
                RequestId: null,
                DeviceName: null,
                Message: "Request pairing before remote POS login.",
                PairingCodeRequired: false,
                Paired: false));
        }

        if (!_requests.TryGetValue(parsedRequestId, out PairingRequestState? state))
        {
            return Result<RemotePosPairingStatusResponse>.Success(new RemotePosPairingStatusResponse(
                PairingState: "Unknown",
                RequestId: requestId,
                DeviceName: null,
                Message: "Pairing request was not found. Ask the host operator for a new join code.",
                PairingCodeRequired: false,
                Paired: false));
        }

        return Result<RemotePosPairingStatusResponse>.Success(new RemotePosPairingStatusResponse(
            PairingState: state.Status.ToString(),
            RequestId: state.RequestId.ToString("D"),
            DeviceName: state.DeviceName,
            Message: MessageFor(state.Status),
            PairingCodeRequired: state.Status == RemotePosPairingRequestStatus.Accepted,
            Paired: state.Status == RemotePosPairingRequestStatus.Paired));
    }

    public Result<IReadOnlyList<RemotePosPendingJoinRequestResponse>> ListRequests()
    {
        ExpireRequests();
        return Result<IReadOnlyList<RemotePosPendingJoinRequestResponse>>.Success(
            _requests.Values
                .OrderByDescending(request => request.RequestedAtUtc)
                .Select(ToPendingResponse)
                .ToArray());
    }

    public async Task<Result<RemotePosApproveJoinResponse>> AcceptAsync(
        Guid requestId,
        TerminalRegistryActorContext actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ExpireRequests();

        if (!_requests.TryGetValue(requestId, out PairingRequestState? state))
        {
            return Result<RemotePosApproveJoinResponse>.Fail(Error.NotFound(
                "RemotePosPairing.RequestNotFound",
                "Pending join request was not found."));
        }

        if (state.Status != RemotePosPairingRequestStatus.Pending)
        {
            return Result<RemotePosApproveJoinResponse>.Fail(Error.Conflict(
                "RemotePosPairing.RequestNotPending",
                "Only pending join requests can be approved."));
        }

        LocalLanTerminalEntitlementStatus entitlement = await _entitlements
            .GetHostEntitlementAsync(cancellationToken)
            .ConfigureAwait(false);
        int maxPaired = GetEffectiveMaxPairedTerminals(entitlement);
        if (maxPaired <= 0)
        {
            return Result<RemotePosApproveJoinResponse>.Fail(Error.Forbidden(
                LocalLanTerminalEntitlementCodes.NoTerminalsPurchased,
                entitlement.Message));
        }

        Result<int> reservedOrPairedCount = await CountReservedOrPairedTerminalsAsync(
            actor,
            excludingRequestId: state.RequestId,
            cancellationToken)
            .ConfigureAwait(false);

        if (reservedOrPairedCount.IsFailure)
        {
            return Result<RemotePosApproveJoinResponse>.Fail(reservedOrPairedCount.ErrorDetail);
        }

        if (reservedOrPairedCount.Value >= maxPaired)
        {
            return Result<RemotePosApproveJoinResponse>.Fail(Error.Conflict(
                "RemotePosPairing.PairedTerminalLimitReached",
                $"The LAN terminal safety limit is full ({reservedOrPairedCount.Value}/{maxPaired}). Revoke an unused terminal before pairing another device."));
        }

        Result<PairingInviteCreationResult> invite = await _lanServices.TerminalPairingPreparation
            .CreatePairingInviteAsync(
                state.DeviceName,
                TerminalRegistrationRole.CashierClient,
                actor,
                PairingCodeLifetime,
                cancellationToken)
            .ConfigureAwait(false);

        if (invite.IsFailure)
        {
            return Result<RemotePosApproveJoinResponse>.Fail(invite.ErrorDetail);
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        state.Status = RemotePosPairingRequestStatus.Accepted;
        state.AcceptedAtUtc = now;
        state.PairingId = invite.Value.Invite.PairingId;
        state.StoreId = invite.Value.Invite.StoreId;
        state.HostId = invite.Value.Invite.HostId;
        state.PairingCodeHash = HashSecret(invite.Value.PairingCode);
        state.PairingCodeExpiresAtUtc = invite.Value.Invite.ExpiresAtUtc;
        state.PairingCodeUsed = false;
        state.ApprovedByUserId = actor.ActorUserId;
        state.ApprovedActorPermissions = actor.Permissions.ToArray();

        return Result<RemotePosApproveJoinResponse>.Success(new RemotePosApproveJoinResponse(
            RequestId: state.RequestId.ToString("D"),
            DeviceName: state.DeviceName,
            PairingCode: invite.Value.PairingCode,
            PairingCodeExpiresAtUtc: invite.Value.Invite.ExpiresAtUtc.ToString("O"),
            Message: "Device approved. Give this one-time pairing code to the remote device. The code is shown once and is not stored in plaintext."));
    }

    public Result<RemotePosRejectJoinResponse> Reject(Guid requestId)
    {
        ExpireRequests();

        if (!_requests.TryGetValue(requestId, out PairingRequestState? state))
        {
            return Result<RemotePosRejectJoinResponse>.Fail(Error.NotFound(
                "RemotePosPairing.RequestNotFound",
                "Pending join request was not found."));
        }

        state.Status = RemotePosPairingRequestStatus.Rejected;
        state.RejectedAtUtc = DateTimeOffset.UtcNow;

        return Result<RemotePosRejectJoinResponse>.Success(new RemotePosRejectJoinResponse(
            RequestId: state.RequestId.ToString("D"),
            PairingState: state.Status.ToString(),
            Message: "Join request rejected by the POS host."));
    }

    public async Task<Result<RemotePosPairingActivationResponse>> ActivateAsync(
        RemotePosPairingActivationRequest request,
        RemoteReadOnlyBridgeRequestContext bridgeContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(bridgeContext);
        ExpireRequests();

        if (string.IsNullOrWhiteSpace(request.RequestId) || !Guid.TryParse(request.RequestId, out Guid requestId))
        {
            return PairingRejected("RemotePosPairing.RequestIdRequired");
        }

        if (string.IsNullOrWhiteSpace(request.PairingCode))
        {
            return PairingRejected("RemotePosPairing.PairingCodeRequired");
        }

        if (!_requests.TryGetValue(requestId, out PairingRequestState? state)
            || state.Status != RemotePosPairingRequestStatus.Accepted
            || state.PairingCodeHash is null
            || !state.PairingId.HasValue
            || !state.StoreId.HasValue
            || !state.HostId.HasValue)
        {
            return PairingRejected("RemotePosPairing.NotApproved");
        }

        if (state.PairingCodeUsed)
        {
            return PairingRejected("RemotePosPairing.PairingCodeUsed");
        }

        if (state.PairingCodeExpiresAtUtc is null || state.PairingCodeExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            state.Status = RemotePosPairingRequestStatus.Expired;
            return PairingRejected("RemotePosPairing.PairingCodeExpired");
        }

        if (!FixedTimeSecretEquals(state.PairingCodeHash, request.PairingCode))
        {
            return PairingRejected("RemotePosPairing.PairingCodeRejected");
        }

        LocalLanTerminalEntitlementStatus entitlement = await _entitlements
            .GetHostEntitlementAsync(cancellationToken)
            .ConfigureAwait(false);
        int maxPaired = GetEffectiveMaxPairedTerminals(entitlement);
        if (maxPaired <= 0)
        {
            return Result<RemotePosPairingActivationResponse>.Fail(Error.Forbidden(
                LocalLanTerminalEntitlementCodes.NoTerminalsPurchased,
                entitlement.Message));
        }

        // Operational terminal activation requires separate Terminal license proof (not optional).
        Result<ValidatedTerminalLicenseProof> proof = _entitlements.ValidateTerminalLicenseProof(
            request.TerminalLicenseJson,
            request.TerminalMachineCode,
            entitlement);
        if (proof.IsFailure)
        {
            return Result<RemotePosPairingActivationResponse>.Fail(proof.ErrorDetail);
        }

        Result<int> activePairedCount = await CountActivePairedTerminalsAsync(
            new TerminalRegistryActorContext(
                state.ApprovedByUserId ?? Guid.Empty,
                state.ApprovedActorPermissions ?? Array.Empty<string>()),
            cancellationToken)
            .ConfigureAwait(false);

        if (activePairedCount.IsFailure)
        {
            return Result<RemotePosPairingActivationResponse>.Fail(activePairedCount.ErrorDetail);
        }

        Result terminalLicenseAvailable = await EnsureTerminalLicenseAvailableAsync(
            proof.Value,
            new TerminalRegistryActorContext(
                state.ApprovedByUserId ?? Guid.Empty,
                state.ApprovedActorPermissions ?? Array.Empty<string>()),
            cancellationToken)
            .ConfigureAwait(false);
        if (terminalLicenseAvailable.IsFailure)
        {
            return Result<RemotePosPairingActivationResponse>.Fail(terminalLicenseAvailable.ErrorDetail);
        }

        if (activePairedCount.Value >= maxPaired)
        {
            return Result<RemotePosPairingActivationResponse>.Fail(Error.Conflict(
                "RemotePosPairing.PairedTerminalLimitReached",
                $"The LAN terminal safety limit is full ({activePairedCount.Value}/{maxPaired}). Revoke an unused terminal before pairing another device."));
        }

        TerminalPairingActivationRequest activationRequest = new(
            state.PairingId.Value,
            state.StoreId.Value,
            state.HostId.Value,
            state.DeviceName,
            TerminalRegistrationRole.CashierClient,
            request.PairingCode,
            state.DeviceName,
            "remote-pos-pairing-gate",
            DateTimeOffset.UtcNow,
            state.ObservedOrigin,
            proof.Value.LicenseId,
            proof.Value.TerminalId,
            proof.Value.MachineCode,
            proof.Value.ExpiresAt);

        Result<TerminalPairingActivationResult> activation = await _lanServices.TerminalPairingActivation
            .ActivatePairingAsync(activationRequest, cancellationToken)
            .ConfigureAwait(false);

        if (activation.IsFailure || !activation.Value.Succeeded || !activation.Value.TerminalId.HasValue
            || string.IsNullOrWhiteSpace(activation.Value.OneTimeTerminalCredential))
        {
            return PairingRejected("RemotePosPairing.ActivationRejected");
        }

        Guid terminalId = activation.Value.TerminalId.Value;
        Result<RemoteClientHandshakeResult> handshake = await _lanServices.Handshake
            .HandshakeAsync(new RemoteClientHandshakeRequest(
                terminalId,
                state.StoreId.Value,
                state.HostId.Value,
                activation.Value.OneTimeTerminalCredential,
                state.ObservedOrigin,
                DateTimeOffset.UtcNow,
                state.DeviceName,
                "remote-pos-pairing-gate"), cancellationToken)
            .ConfigureAwait(false);

        if (handshake.IsFailure || !handshake.Value.Succeeded || !handshake.Value.HandshakeSessionId.HasValue)
        {
            return PairingRejected("RemotePosPairing.HandshakeRejected");
        }

        state.PairingCodeUsed = true;
        state.Status = RemotePosPairingRequestStatus.Paired;
        state.PairedAtUtc = DateTimeOffset.UtcNow;
        state.TerminalId = terminalId;
        state.HandshakeSessionId = handshake.Value.HandshakeSessionId.Value;

        return Result<RemotePosPairingActivationResponse>.Success(new RemotePosPairingActivationResponse(
            PairingState: state.Status.ToString(),
            TerminalId: terminalId.ToString("D"),
            HandshakeSessionId: handshake.Value.HandshakeSessionId.Value.ToString("D"),
            HostId: state.HostId.Value.ToString("D"),
            StoreId: state.StoreId.Value.ToString("D"),
            TerminalName: state.DeviceName,
            SessionExpiresAtUtc: handshake.Value.SessionExpiresAtUtc?.ToString("O"),
            Message: "Device paired. After pairing, sign in as Cashier or Manager."));
    }

    private static Result<RemotePosPairingActivationResponse> PairingRejected(string code) =>
        Result<RemotePosPairingActivationResponse>.Fail(Error.Forbidden(
            code,
            "Pairing activation was rejected. Ask the host operator for a fresh pairing code."));

    private static string MessageFor(RemotePosPairingRequestStatus status) => status switch
    {
        RemotePosPairingRequestStatus.Pending => "Waiting for host approval.",
        RemotePosPairingRequestStatus.Accepted => "Host approved this device. Enter the one-time pairing code.",
        RemotePosPairingRequestStatus.Rejected => "Host rejected this join request.",
        RemotePosPairingRequestStatus.Expired => "Pairing request expired. Ask the host for a new join code.",
        RemotePosPairingRequestStatus.Paired => "Device paired. Sign in as Cashier or Manager.",
        _ => "Pairing state unknown."
    };

    private bool IsCurrentJoinCodeAccepted(string joinCode, out string failureCode)
    {
        failureCode = "RemotePosPairing.JoinCodeRejected";
        byte[]? currentHash;
        DateTimeOffset? expiresAt;

        lock (_joinCodeLock)
        {
            currentHash = _currentJoinCodeHash;
            expiresAt = _joinCodeExpiresAtUtc;
        }

        if (currentHash is null || expiresAt is null)
        {
            return false;
        }

        if (expiresAt <= DateTimeOffset.UtcNow)
        {
            failureCode = "RemotePosPairing.JoinCodeExpired";
            return false;
        }

        return FixedTimeSecretEquals(currentHash, joinCode);
    }

    private static RemotePosPendingJoinRequestResponse ToPendingResponse(PairingRequestState request) =>
        new(
            RequestId: request.RequestId.ToString("D"),
            DeviceName: request.DeviceName,
            ObservedOrigin: request.ObservedOrigin,
            PairingState: request.Status.ToString(),
            RequestedAtUtc: request.RequestedAtUtc.ToString("O"),
            ExpiresAtUtc: request.ExpiresAtUtc.ToString("O"),
            PairingCodeExpiresAtUtc: request.PairingCodeExpiresAtUtc?.ToString("O"));

    public async Task<RemotePosCapacityStatusResponse> GetCapacityStatusAsync(
        TerminalRegistryActorContext actor,
        int activeSessionCount,
        CancellationToken cancellationToken = default)
    {
        LocalLanTerminalEntitlementStatus entitlement = await _entitlements
            .GetHostEntitlementAsync(cancellationToken)
            .ConfigureAwait(false);
        int maxPaired = GetEffectiveMaxPairedTerminals(entitlement);
        int maxSessions = entitlement.HostOperationalLanAllowed
            ? Math.Min(_capacityPolicy.MaxActiveSessions, Math.Max(1, entitlement.EffectiveMaxActiveSessions))
            : 0;

        Result<int> activePaired = await CountActivePairedTerminalsAsync(actor, cancellationToken)
            .ConfigureAwait(false);

        int currentPairedCount = activePaired.IsSuccess ? activePaired.Value : 0;
        return new RemotePosCapacityStatusResponse(
            MaxPairedTerminals: maxPaired,
            MaxActiveSessions: maxSessions,
            HardCap: RemotePosCapacityPolicy.HardCap,
            CurrentPairedCount: currentPairedCount,
            CurrentActiveSessions: activeSessionCount,
            RemainingPairingCapacity: Math.Max(0, maxPaired - currentPairedCount),
            RemainingActiveSessionCapacity: Math.Max(0, maxSessions - activeSessionCount),
            HostLanEntitled: entitlement.HostOperationalLanAllowed,
            HostLicenseId: entitlement.HostLicenseId,
            StoreId: entitlement.StoreId,
            LicenseMessage: entitlement.Message,
            LicenseContactEmail: entitlement.ContactEmail);
    }

    private int GetEffectiveMaxPairedTerminals(LocalLanTerminalEntitlementStatus entitlement)
    {
        // Signed Terminal licenses are the commercial truth. This numeric limit is a
        // defensive safety/abuse ceiling and never expands beyond the hard cap.
        if (!entitlement.HostOperationalLanAllowed)
        {
            return 0;
        }

        return Math.Min(
            Math.Min(entitlement.EffectiveMaxPairedTerminals, _capacityPolicy.MaxPairedTerminals),
            RemotePosCapacityPolicy.HardCap);
    }

    private async Task<Result<int>> CountReservedOrPairedTerminalsAsync(
        TerminalRegistryActorContext actor,
        Guid excludingRequestId,
        CancellationToken cancellationToken)
    {
        Result<int> activePaired = await CountActivePairedTerminalsAsync(actor, cancellationToken)
            .ConfigureAwait(false);

        if (activePaired.IsFailure)
        {
            return activePaired;
        }

        int reserved = _requests.Values.Count(request =>
            request.RequestId != excludingRequestId
            && request.Status == RemotePosPairingRequestStatus.Accepted);

        return Result<int>.Success(activePaired.Value + reserved);
    }

    private async Task<Result<int>> CountActivePairedTerminalsAsync(
        TerminalRegistryActorContext actor,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<SalrynTerminalRegistration>> terminals = await _lanServices.TerminalRegistry
            .ListTerminalsAsync(actor, cancellationToken)
            .ConfigureAwait(false);

        if (terminals.IsFailure)
        {
            return Result<int>.Fail(Error.Forbidden(
                "RemotePosPairing.CapacityVisibilityRequired",
                "Remote POS capacity could not be verified. The host operator must have terminal visibility before approving remote POS devices."));
        }

        return Result<int>.Success(terminals.Value.Count(terminal =>
            terminal.TerminalRole == TerminalRegistrationRole.CashierClient
            && terminal.Status != TerminalRegistrationStatus.Revoked));
    }

    private async Task<Result> EnsureTerminalLicenseAvailableAsync(
        ValidatedTerminalLicenseProof proof,
        TerminalRegistryActorContext actor,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<SalrynTerminalRegistration>> terminals = await _lanServices.TerminalRegistry
            .ListTerminalsAsync(actor, cancellationToken)
            .ConfigureAwait(false);
        if (terminals.IsFailure)
        {
            return Result.Fail(terminals.ErrorDetail);
        }

        bool alreadyPaired = terminals.Value.Any(terminal =>
        {
            if (terminal.Status == TerminalRegistrationStatus.Revoked
                || !TerminalLicenseTrustMetadata.TryRead(terminal.Notes, out _, out string terminalId, out _, out _))
            {
                return false;
            }

            return string.Equals(terminalId, proof.TerminalId, StringComparison.OrdinalIgnoreCase);
        });

        return alreadyPaired
            ? Result.Fail(Error.Conflict(
                LocalLanTerminalEntitlementCodes.TerminalLicenseAlreadyPaired,
                "This Terminal license is already paired to another active or disabled terminal. Revoke that terminal before pairing it again."))
            : Result.Success();
    }

    private void ExpireRequests()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (PairingRequestState request in _requests.Values)
        {
            if (request.Status == RemotePosPairingRequestStatus.Pending && request.ExpiresAtUtc <= now)
            {
                request.Status = RemotePosPairingRequestStatus.Expired;
            }

            if (request.Status == RemotePosPairingRequestStatus.Accepted
                && request.PairingCodeExpiresAtUtc is not null
                && request.PairingCodeExpiresAtUtc <= now)
            {
                request.Status = RemotePosPairingRequestStatus.Expired;
            }
        }
    }

    private static string SanitizeDeviceName(string value)
    {
        string safe = RemoteReadOnlyBridgeDiagnostics.SanitizePublicText(value) ?? "Remote POS device";
        safe = new string(safe.Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(safe))
        {
            safe = "Remote POS device";
        }

        return safe.Length <= DeviceNameMaxLength ? safe : safe[..DeviceNameMaxLength];
    }

    private static string GenerateShortCode()
    {
        Span<byte> bytes = stackalloc byte[5];
        RandomNumberGenerator.Fill(bytes);
        string code = Convert.ToBase64String(bytes.ToArray())
            .Replace('+', '7')
            .Replace('/', '9')
            .TrimEnd('=')
            .ToUpperInvariant();
        return code.Length <= 8 ? code : code[..8];
    }

    private static byte[] HashSecret(string value) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeSecret(value)));

    private static bool FixedTimeSecretEquals(byte[] expectedHash, string suppliedSecret)
    {
        byte[] suppliedHash = HashSecret(suppliedSecret);
        return CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash);
    }

    private static string NormalizeSecret(string value) => value.Trim().ToUpperInvariant();

    private sealed class PairingRequestState
    {
        public required Guid RequestId { get; init; }
        public required string DeviceName { get; init; }
        public required string ObservedOrigin { get; init; }
        public required DateTimeOffset RequestedAtUtc { get; init; }
        public required DateTimeOffset ExpiresAtUtc { get; init; }
        public required RemotePosPairingRequestStatus Status { get; set; }
        public DateTimeOffset? AcceptedAtUtc { get; set; }
        public DateTimeOffset? RejectedAtUtc { get; set; }
        public DateTimeOffset? PairedAtUtc { get; set; }
        public Guid? ApprovedByUserId { get; set; }
        public IReadOnlyCollection<string>? ApprovedActorPermissions { get; set; }
        public Guid? PairingId { get; set; }
        public Guid? StoreId { get; set; }
        public Guid? HostId { get; set; }
        public Guid? TerminalId { get; set; }
        public Guid? HandshakeSessionId { get; set; }
        public byte[]? PairingCodeHash { get; set; }
        public DateTimeOffset? PairingCodeExpiresAtUtc { get; set; }
        public bool PairingCodeUsed { get; set; }
    }

    private enum RemotePosPairingRequestStatus
    {
        Pending = 1,
        Accepted = 2,
        Rejected = 3,
        Expired = 4,
        Paired = 5
    }
}
