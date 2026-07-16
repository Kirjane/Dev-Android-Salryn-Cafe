using Salryn.Api.Local.Composition;
using Salryn.Core.Common;
using Salryn.Core.Security;
using Salryn.Core.TerminalRegistry;

namespace Salryn.Api.Local.RemotePos;

public sealed class RemotePosTerminalTrustService
{
    private readonly RemotePosOperatorSessionService _sessionService;
    private readonly RemotePosPairingGateService _pairingGate;
    private readonly LanRemoteTransportApiServices _lanServices;

    public RemotePosTerminalTrustService(
        RemotePosOperatorSessionService sessionService,
        RemotePosPairingGateService pairingGate,
        LanRemoteTransportApiServices lanServices)
    {
        _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
        _pairingGate = pairingGate ?? throw new ArgumentNullException(nameof(pairingGate));
        _lanServices = lanServices ?? throw new ArgumentNullException(nameof(lanServices));
    }

    public async Task<RemotePosTerminalTrustResponse> ResolveAsync(
        RemotePosTrustContext trust,
        string? requestId,
        string? operatorSessionToken,
        CancellationToken cancellationToken = default)
    {
        Guid? terminalId = trust.TerminalId == Guid.Empty ? null : trust.TerminalId;
        Guid? handshakeSessionId = trust.HandshakeSessionId == Guid.Empty ? null : trust.HandshakeSessionId;

        if (terminalId.HasValue)
        {
            RemotePosTerminalTrustResponse? terminalStatus = await ResolveTerminalRegistryStatusAsync(
                terminalId.Value,
                handshakeSessionId,
                trust,
                operatorSessionToken,
                cancellationToken)
                .ConfigureAwait(false);

            if (terminalStatus is not null)
            {
                return terminalStatus;
            }
        }

        if (!string.IsNullOrWhiteSpace(requestId))
        {
            Result<RemotePosPairingStatusResponse> pairingStatus = _pairingGate.GetRemoteStatus(requestId);
            if (pairingStatus.IsSuccess)
            {
                return MapPairingRequestStatus(pairingStatus.Value, terminalId?.ToString("D"));
            }
        }

        return new RemotePosTerminalTrustResponse(
            TerminalId: terminalId?.ToString("D"),
            Status: RemotePosTerminalTrustStatuses.Unknown,
            Mode: RemotePosTerminalTrustModes.RemotePos,
            DisplayName: null,
            RequiresPairingCode: false,
            OperatorSessionValid: false,
            Message: "No trusted terminal or active pairing request was found. Request access to this host.",
            HandshakeSessionId: null,
            HandshakeExpiresAtUtc: null,
            RequestId: requestId,
            HostId: null,
            StoreId: null);
    }

    private async Task<RemotePosTerminalTrustResponse?> ResolveTerminalRegistryStatusAsync(
        Guid terminalId,
        Guid? handshakeSessionId,
        RemotePosTrustContext trust,
        string? operatorSessionToken,
        CancellationToken cancellationToken)
    {
        TerminalRegistryActorContext actor = new(
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            new[] { SalrynPermissions.StoreTerminalView });

        Result<SalrynTerminalRegistration?> terminalResult = await _lanServices.TerminalRegistry
            .GetTerminalAsync(terminalId, actor, cancellationToken)
            .ConfigureAwait(false);

        if (terminalResult.IsFailure || terminalResult.Value is null)
        {
            return null;
        }

        SalrynTerminalRegistration terminal = terminalResult.Value;
        string displayName = terminal.TerminalName;

        if (terminal.Status == TerminalRegistrationStatus.Revoked)
        {
            return TrustResponse(
                terminalId,
                RemotePosTerminalTrustStatuses.Revoked,
                displayName,
                false,
                false,
                "Pairing revoked or expired. Re-pair this device.",
                null,
                null,
                null,
                terminal.StoreId,
                terminal.HostId);
        }

        if (terminal.Status == TerminalRegistrationStatus.Disabled)
        {
            return TrustResponse(
                terminalId,
                RemotePosTerminalTrustStatuses.Disabled,
                displayName,
                false,
                false,
                "This terminal is disabled on the host. Ask the host operator to re-enable it or re-pair.",
                null,
                null,
                null,
                terminal.StoreId,
                terminal.HostId);
        }

        if (terminal.Status != TerminalRegistrationStatus.Active)
        {
            return null;
        }

        if (!TerminalLicenseTrustMetadata.TryRead(terminal.Notes, out _, out _, out _, out DateTimeOffset? terminalLicenseExpiresAt))
        {
            return TrustResponse(
                terminalId,
                RemotePosTerminalTrustStatuses.Revoked,
                displayName,
                false,
                false,
                "This terminal is not backed by a verified Terminal license. Re-pair it from Terminal Management.",
                null,
                null,
                null,
                terminal.StoreId,
                terminal.HostId);
        }

        if (terminalLicenseExpiresAt.HasValue && terminalLicenseExpiresAt.Value <= DateTimeOffset.UtcNow)
        {
            return TrustResponse(
                terminalId,
                RemotePosTerminalTrustStatuses.Revoked,
                displayName,
                false,
                false,
                "This Terminal license has expired. Install a renewed Terminal license and re-pair this device.",
                null,
                null,
                null,
                terminal.StoreId,
                terminal.HostId);
        }

        if (!handshakeSessionId.HasValue)
        {
            return TrustResponse(
                terminalId,
                RemotePosTerminalTrustStatuses.HandshakeExpired,
                displayName,
                false,
                false,
                "This terminal is paired on the host. Refresh the terminal handshake to continue.",
                null,
                null,
                null,
                terminal.StoreId,
                terminal.HostId);
        }

        RemotePosTrustContext trustForValidation = trust with
        {
            TerminalId = terminalId,
            HandshakeSessionId = handshakeSessionId.Value,
        };

        RemotePosTrustResult trustResult = await _sessionService
            .ValidateTrustAsync(trustForValidation, cancellationToken)
            .ConfigureAwait(false);

        if (!trustResult.Ok)
        {
            if (string.Equals(trustResult.Code, "RemotePos.HandshakeInvalid", StringComparison.Ordinal))
            {
                return TrustResponse(
                    terminalId,
                    RemotePosTerminalTrustStatuses.HandshakeExpired,
                    displayName,
                    false,
                    false,
                    "Terminal handshake expired. Refresh handshake to continue without re-entering a pairing code.",
                    handshakeSessionId.Value.ToString("D"),
                    null,
                    null,
                    terminal.StoreId,
                    terminal.HostId);
            }

            return TrustResponse(
                terminalId,
                RemotePosTerminalTrustStatuses.Revoked,
                displayName,
                false,
                false,
                trustResult.Message,
                handshakeSessionId.Value.ToString("D"),
                null,
                null,
                terminal.StoreId,
                terminal.HostId);
        }

        bool operatorSessionValid = false;
        if (!string.IsNullOrWhiteSpace(operatorSessionToken))
        {
            RemotePosSessionValidation sessionValidation = await _sessionService
                .ValidateAsync(trustForValidation, operatorSessionToken, cancellationToken)
                .ConfigureAwait(false);
            operatorSessionValid = sessionValidation.Ok;
        }

        return TrustResponse(
            terminalId,
            RemotePosTerminalTrustStatuses.Trusted,
            displayName,
            false,
            operatorSessionValid,
            operatorSessionValid
                ? "Trusted terminal with a valid operator session."
                : "This terminal is paired. Sign in as Cashier or Manager to continue.",
            handshakeSessionId.Value.ToString("D"),
            null,
            null,
            terminal.StoreId,
            terminal.HostId);
    }

    private static RemotePosTerminalTrustResponse MapPairingRequestStatus(
        RemotePosPairingStatusResponse status,
        string? terminalId)
    {
        string pairingState = status.PairingState ?? string.Empty;

        if (status.Paired || string.Equals(pairingState, "Paired", StringComparison.OrdinalIgnoreCase))
        {
            return TrustResponse(
                ParseGuidOrNull(terminalId),
                RemotePosTerminalTrustStatuses.Trusted,
                status.DeviceName,
                false,
                false,
                status.Message,
                null,
                null,
                status.RequestId,
                null,
                null);
        }

        if (status.PairingCodeRequired || string.Equals(pairingState, "Accepted", StringComparison.OrdinalIgnoreCase))
        {
            return TrustResponse(
                ParseGuidOrNull(terminalId),
                RemotePosTerminalTrustStatuses.Approved,
                status.DeviceName,
                true,
                false,
                status.Message,
                null,
                null,
                status.RequestId,
                null,
                null);
        }

        if (string.Equals(pairingState, "Pending", StringComparison.OrdinalIgnoreCase))
        {
            return TrustResponse(
                ParseGuidOrNull(terminalId),
                RemotePosTerminalTrustStatuses.Pending,
                status.DeviceName,
                false,
                false,
                status.Message,
                null,
                null,
                status.RequestId,
                null,
                null);
        }

        if (string.Equals(pairingState, "Rejected", StringComparison.OrdinalIgnoreCase)
            || string.Equals(pairingState, "Expired", StringComparison.OrdinalIgnoreCase))
        {
            return TrustResponse(
                ParseGuidOrNull(terminalId),
                RemotePosTerminalTrustStatuses.Revoked,
                status.DeviceName,
                false,
                false,
                status.Message,
                null,
                null,
                status.RequestId,
                null,
                null);
        }

        return TrustResponse(
            ParseGuidOrNull(terminalId),
            RemotePosTerminalTrustStatuses.Unknown,
            status.DeviceName,
            false,
            false,
            status.Message,
            null,
            null,
            status.RequestId,
            null,
            null);
    }

    private static RemotePosTerminalTrustResponse TrustResponse(
        Guid? terminalId,
        string status,
        string? displayName,
        bool requiresPairingCode,
        bool operatorSessionValid,
        string message,
        string? handshakeSessionId,
        string? handshakeExpiresAtUtc,
        string? requestId,
        Guid? storeId,
        Guid? hostId) =>
        new(
            TerminalId: terminalId?.ToString("D"),
            Status: status,
            Mode: RemotePosTerminalTrustModes.RemotePos,
            DisplayName: displayName,
            RequiresPairingCode: requiresPairingCode,
            OperatorSessionValid: operatorSessionValid,
            Message: message,
            HandshakeSessionId: handshakeSessionId,
            HandshakeExpiresAtUtc: handshakeExpiresAtUtc,
            RequestId: requestId,
            HostId: hostId?.ToString("D"),
            StoreId: storeId?.ToString("D"));

    private static Guid? ParseGuidOrNull(string? value) =>
        Guid.TryParse(value, out Guid parsed) ? parsed : null;
}

public static class RemotePosTerminalTrustStatuses
{
    public const string Unknown = "Unknown";
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Trusted = "Trusted";
    public const string Revoked = "Revoked";
    public const string Disabled = "Disabled";
    public const string HandshakeExpired = "HandshakeExpired";
}

public static class RemotePosTerminalTrustModes
{
    public const string RemotePos = "RemotePos";
}
