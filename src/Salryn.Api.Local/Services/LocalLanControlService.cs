using Salryn.Api.Local.Composition;
using Salryn.Api.Local.Contracts;
using Salryn.Api.Local.RemotePos;
using Salryn.Core.Common;
using Salryn.Core.Host;
using Salryn.Core.Services;
using Salryn.Core.TerminalRegistry;

namespace Salryn.Api.Local.Services;

/// <summary>
/// LAN-M15-C: local-only operator control surface for the remote read-only LAN stack.
/// Provides read status plus narrow enable/disable, explicit LAN bind configuration, and
/// terminal disable/revoke operations. Every operation is intended to run behind the
/// localhost operator guard; nothing here is exposed to remote LAN bridge trust, and no
/// mutation of catalog, checkout, official, or backup data is possible from this surface.
/// </summary>
public sealed class LocalLanControlService
{
    private readonly LanRemoteTransportApiServices _lanServices;
    private readonly RemotePosRuntimeGate _remotePosRuntimeGate;
    private readonly RemotePosApiServices _remotePosServices;
    private readonly LocalApiHostControlUrls _hostUrls;
    private readonly LocalApiLanRuntimeStatusService _runtimeStatus;

    public LocalLanControlService(
        LanRemoteTransportApiServices lanServices,
        RemotePosRuntimeGate remotePosRuntimeGate,
        RemotePosApiServices remotePosServices,
        LocalApiHostControlUrls hostUrls,
        LocalApiLanRuntimeStatusService runtimeStatus)
    {
        _lanServices = lanServices ?? throw new ArgumentNullException(nameof(lanServices));
        _remotePosRuntimeGate = remotePosRuntimeGate ?? throw new ArgumentNullException(nameof(remotePosRuntimeGate));
        _remotePosServices = remotePosServices ?? throw new ArgumentNullException(nameof(remotePosServices));
        _hostUrls = hostUrls ?? throw new ArgumentNullException(nameof(hostUrls));
        _runtimeStatus = runtimeStatus ?? throw new ArgumentNullException(nameof(runtimeStatus));
    }

    public async Task<LocalLanControlRuntimeStatusResponse> GetRuntimeStatusAsync(
        CancellationToken cancellationToken = default)
    {
        LanRemoteTransportConfig config = await GetConfigAsync(cancellationToken).ConfigureAwait(false);
        return _runtimeStatus.Build(config);
    }

    public async Task<LocalLanControlStatusResponse> GetStatusAsync(
        TerminalRegistryActorContext actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        LanRemoteTransportConfig config = await GetConfigAsync(cancellationToken).ConfigureAwait(false);
        RemoteReadOnlyBindPolicyResponse bindPolicy = RemoteReadOnlyLanBindPolicyMapper.Map(config);

        (bool visible, string visibilityMessage, IReadOnlyList<LocalLanControlTerminalResponse> terminals) =
            await ListTerminalsSafeAsync(actor, cancellationToken).ConfigureAwait(false);
        RemotePosCapacityStatusResponse limits = await _remotePosServices.PairingGate
            .GetCapacityStatusAsync(actor, _remotePosServices.SessionService.GetActiveSessionCount(), cancellationToken)
            .ConfigureAwait(false);

        LocalLanControlRuntimeStatusResponse runtimeStatus = _runtimeStatus.Build(config);
        List<string> warnings = BuildWarnings().ToList();
        if (runtimeStatus.RestartRequired)
        {
            warnings.Add(
                "LAN bind is configured but the API is not listening on that address yet. Restart Salryn with the explicit LAN bind command shown in runtime status.");
        }

        if (!runtimeStatus.ClientUrlReady && runtimeStatus.LanTransportEnabled)
        {
            warnings.Add("Do not copy a LAN client URL until runtime status reports Ready.");
        }

        return new LocalLanControlStatusResponse(
            LocalOperatorOnly: true,
            RemoteTransportEnabled: config.RemoteTransportEnabled,
            BindMode: config.BindMode.ToString(),
            ConfiguredBindAddress: SafeBindAddress(config),
            BindPolicy: bindPolicy,
            TerminalVisibilityAvailable: visible,
            TerminalVisibilityMessage: visibilityMessage,
            Terminals: terminals,
            Warnings: warnings,
            Message: "Local LAN control is operator-only on the POS host. Remote clients cannot change LAN configuration, approvals, or terminal trust.",
            RemotePosRuntimeEnabled: _remotePosRuntimeGate.IsEnabled,
            HostLocalUrl: _hostUrls.LocalUrl,
            HostLanUrl: BuildHostLanUrl(config),
            Safety: "Host control is local-only",
            RemotePosLimits: limits);
    }

    /// <summary>
    /// LAN-M16: local-only enablement of the host-owned remote POS command runtime. This is
    /// separate from remote read-only transport: enabling the read-only bridge never enables
    /// remote POS writes. The toggle is process-scoped and resets to disabled on host restart.
    /// </summary>
    public async Task<Result<LocalLanControlStatusResponse>> SetRemotePosRuntimeAsync(
        bool enable,
        TerminalRegistryActorContext actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (enable)
        {
            _remotePosRuntimeGate.Enable(actor.ActorUserId);
        }
        else
        {
            _remotePosRuntimeGate.Disable();
        }

        return Result<LocalLanControlStatusResponse>.Success(
            await GetStatusAsync(actor, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<LocalLanControlStatusResponse>> SetRemoteTransportAsync(
        bool enable,
        string? bindAddress,
        TerminalRegistryActorContext actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        LanRemoteTransportConfig current = await GetConfigAsync(cancellationToken).ConfigureAwait(false);

        if (!enable)
        {
            Result<LanRemoteTransportConfig> disabled = await _lanServices.Config
                .DisableAsync(actor.ActorUserId, cancellationToken)
                .ConfigureAwait(false);

            if (disabled.IsFailure)
            {
                return Result<LocalLanControlStatusResponse>.Fail(SanitizeError(disabled.ErrorDetail));
            }

            return Result<LocalLanControlStatusResponse>.Success(
                await GetStatusAsync(actor, cancellationToken).ConfigureAwait(false));
        }

        string trimmedBind = bindAddress?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmedBind))
        {
            return Result<LocalLanControlStatusResponse>.Fail(Error.Validation(
                "LocalLanControl.BindAddressRequired",
                "An explicit LAN IP address is required to enable remote read-only transport. Wildcard bind is never allowed."));
        }

        LanRemoteTransportConfigUpdateRequest request = new(
            RemoteTransportEnabled: true,
            BindMode: LanRemoteTransportBindMode.ExplicitLanAddress,
            BindAddress: trimmedBind,
            Port: current.Port,
            RequirePairedTerminal: true,
            RequireActiveTerminal: true,
            RejectUnknownTerminals: true,
            AllowOfficialCommandsRemotely: false,
            AllowAdminCommandsRemotely: false,
            UpdatedByUserId: actor.ActorUserId);

        Result<LanRemoteTransportConfig> saved = await _lanServices.Config
            .SaveAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (saved.IsFailure)
        {
            return Result<LocalLanControlStatusResponse>.Fail(SanitizeError(saved.ErrorDetail));
        }

        return Result<LocalLanControlStatusResponse>.Success(
            await GetStatusAsync(actor, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<LocalLanControlTerminalActionResponse>> DisableTerminalAsync(
        Guid terminalId,
        TerminalRegistryActorContext actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (terminalId == Guid.Empty)
        {
            return Result<LocalLanControlTerminalActionResponse>.Fail(Error.Validation(
                "LocalLanControl.TerminalIdRequired",
                "A valid terminal id is required."));
        }

        Result<SalrynTerminalRegistration> result = await _lanServices.TerminalRegistry
            .DisableTerminalAsync(terminalId, actor, cancellationToken)
            .ConfigureAwait(false);

        return MapTerminalActionResult(result, "Terminal disabled. It can no longer perform remote read-only reads until re-enabled locally.");
    }

    public async Task<Result<LocalLanControlTerminalActionResponse>> RevokeTerminalAsync(
        Guid terminalId,
        TerminalRegistryActorContext actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (terminalId == Guid.Empty)
        {
            return Result<LocalLanControlTerminalActionResponse>.Fail(Error.Validation(
                "LocalLanControl.TerminalIdRequired",
                "A valid terminal id is required."));
        }

        Result<SalrynTerminalRegistration> result = await _lanServices.TerminalRegistry
            .RevokeTerminalAsync(terminalId, actor, cancellationToken)
            .ConfigureAwait(false);

        return MapTerminalActionResult(result, "Terminal revoked. Its trust is permanently withdrawn for remote read-only LAN reads.");
    }

    private static Result<LocalLanControlTerminalActionResponse> MapTerminalActionResult(
        Result<SalrynTerminalRegistration> result,
        string successMessage)
    {
        if (result.IsFailure)
        {
            return Result<LocalLanControlTerminalActionResponse>.Fail(SanitizeError(result.ErrorDetail));
        }

        SalrynTerminalRegistration terminal = result.Value;

        return Result<LocalLanControlTerminalActionResponse>.Success(new LocalLanControlTerminalActionResponse(
            TerminalId: terminal.TerminalId.ToString("D"),
            Status: terminal.Status.ToString(),
            TrustState: ResolveTrustState(terminal.Status),
            Message: successMessage));
    }

    private async Task<LanRemoteTransportConfig> GetConfigAsync(CancellationToken cancellationToken)
    {
        Result<LanRemoteTransportConfig> configResult = await _lanServices.Config
            .GetCurrentAsync(cancellationToken)
            .ConfigureAwait(false);

        return configResult.IsSuccess ? configResult.Value : LanRemoteTransportConfig.CreateDefault();
    }

    private async Task<(bool Visible, string Message, IReadOnlyList<LocalLanControlTerminalResponse> Terminals)> ListTerminalsSafeAsync(
        TerminalRegistryActorContext actor,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<SalrynTerminalRegistration>> listResult = await _lanServices.TerminalRegistry
            .ListTerminalsAsync(actor, cancellationToken)
            .ConfigureAwait(false);

        if (listResult.IsFailure)
        {
            return (
                false,
                "Terminal visibility requires the StoreTerminal.View permission for the localhost operator.",
                Array.Empty<LocalLanControlTerminalResponse>());
        }

        List<LocalLanControlTerminalResponse> terminals = listResult.Value
            .Select(MapTerminal)
            .ToList();

        return (true, "Terminal trust state is visible to the localhost operator.", terminals);
    }

    private static LocalLanControlTerminalResponse MapTerminal(SalrynTerminalRegistration terminal)
    {
        return new LocalLanControlTerminalResponse(
            TerminalId: terminal.TerminalId.ToString("D"),
            TerminalName: RemoteReadOnlyBridgeDiagnostics.SanitizePublicText(terminal.TerminalName) ?? "terminal",
            TerminalRole: terminal.TerminalRole.ToString(),
            Status: terminal.Status.ToString(),
            Trusted: terminal.Status == TerminalRegistrationStatus.Active,
            TrustState: ResolveTrustState(terminal.Status),
            RegisteredAtUtc: terminal.RegisteredAtUtc.ToString("O"),
            LastSeenAtUtc: terminal.LastSeenAtUtc?.ToString("O"),
            DisabledAtUtc: terminal.DisabledAtUtc?.ToString("O"),
            RevokedAtUtc: terminal.RevokedAtUtc?.ToString("O"));
    }

    private static string ResolveTrustState(TerminalRegistrationStatus status) =>
        status switch
        {
            TerminalRegistrationStatus.Active => "Trusted for read-only reads",
            TerminalRegistrationStatus.PendingPairing => "Pending pairing (no read access)",
            TerminalRegistrationStatus.Disabled => "Disabled (no read access)",
            TerminalRegistrationStatus.Revoked => "Revoked (no read access)",
            _ => "Untrusted"
        };

    private static string SafeBindAddress(LanRemoteTransportConfig config)
    {
        if (!config.RemoteTransportEnabled
            || config.BindMode != LanRemoteTransportBindMode.ExplicitLanAddress)
        {
            return "127.0.0.1 (loopback only)";
        }

        string bind = config.BindAddress.Trim();
        return RemoteReadOnlyBridgeDiagnostics.SanitizePublicText(bind) ?? "127.0.0.1 (loopback only)";
    }

    private static IReadOnlyList<string> BuildWarnings()
    {
        return new[]
        {
            "Remote read-only transport is disabled by default and must be explicitly enabled by the localhost operator.",
            "Only a specific LAN IP may be configured. Wildcard bind is rejected and is never allowed.",
            "Remote clients cannot approve join requests, enable/disable LAN, configure the bind address, or disable/revoke terminals.",
            "Remote read-only clients cannot perform checkout, admin, mutation, official writes, or settings changes.",
            "Remote POS clients use /api/remote-pos only after host approval, pairing, terminal trust, and Cashier/Manager login.",
            "Localhost operator session cookies are never accepted as remote LAN trust."
        };
    }

    private string BuildHostLanUrl(LanRemoteTransportConfig config)
    {
        if (!config.RemoteTransportEnabled
            || config.BindMode != LanRemoteTransportBindMode.ExplicitLanAddress)
        {
            return "Not enabled";
        }

        string bind = SafeBindAddress(config);
        return bind.StartsWith("127.", StringComparison.Ordinal)
            ? "Not enabled"
            : _hostUrls.BuildLanUrl(bind);
    }

    private static Error SanitizeError(Error error)
    {
        string safeMessage = RemoteReadOnlyBridgeDiagnostics.SanitizePublicText(error.Message)
            ?? "Local LAN control operation failed.";
        string safeCode = RemoteReadOnlyBridgeDiagnostics.SanitizePublicText(error.Code)
            ?? "LocalLanControl.OperationFailed";

        return new Error(safeCode, safeMessage, error.Type);
    }
}
