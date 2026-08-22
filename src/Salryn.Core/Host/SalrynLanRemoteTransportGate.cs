using Salryn.Core.Common;
using Salryn.Core.TerminalRegistry;

namespace Salryn.Core.Host;

public interface ILanRemoteTransportGate
{
    Task<Result> AuthorizeAsync(
        LanRemoteTransportGateRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class SalrynLanRemoteTransportGate : ILanRemoteTransportGate
{
    private readonly Func<CancellationToken, Task<Result<LanRemoteTransportConfig>>> _configProvider;
    private readonly Func<Guid, CancellationToken, Task<Result<SalrynTerminalRegistration?>>> _terminalResolver;

    public SalrynLanRemoteTransportGate(
        Func<CancellationToken, Task<Result<LanRemoteTransportConfig>>> configProvider,
        Func<Guid, CancellationToken, Task<Result<SalrynTerminalRegistration?>>> terminalResolver)
    {
        _configProvider = configProvider ?? throw new ArgumentNullException(nameof(configProvider));
        _terminalResolver = terminalResolver ?? throw new ArgumentNullException(nameof(terminalResolver));
    }

    public async Task<Result> AuthorizeAsync(
        LanRemoteTransportGateRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Result requestValidation = ValidateRequest(request);
        if (requestValidation.IsFailure)
        {
            return requestValidation;
        }

        Result<LanRemoteTransportConfig> configResult = await _configProvider(cancellationToken)
            .ConfigureAwait(false);

        if (configResult.IsFailure)
        {
            return Result.Fail(configResult.ErrorDetail);
        }

        LanRemoteTransportConfig config = configResult.Value;
        Result configValidation = ValidateConfig(config);
        if (configValidation.IsFailure)
        {
            return configValidation;
        }

        if (SalrynHostCommandLoopbackRules.IsLoopbackOrigin(request.OriginAddress))
        {
            return Result.Success();
        }

        if (!config.RemoteTransportEnabled)
        {
            return Result.Fail(Error.Forbidden(
                "LanRemoteTransport.RemoteDisabled",
                "Remote transport is disabled by configuration."));
        }

        if (config.BindMode != LanRemoteTransportBindMode.ExplicitLanAddress)
        {
            return Result.Fail(Error.Forbidden(
                "LanRemoteTransport.ExplicitLanBindRequired",
                "Remote transport requires an explicit LAN bind configuration."));
        }

        if (request.CommandCategory == HostCommandCategory.PairingActivation)
        {
            return Result.Success();
        }

        Result<SalrynTerminalRegistration?> terminalResult = await _terminalResolver(
                request.TerminalId,
                cancellationToken)
            .ConfigureAwait(false);

        if (terminalResult.IsFailure)
        {
            return Result.Fail(terminalResult.ErrorDetail);
        }

        SalrynTerminalRegistration? terminal = terminalResult.Value;

        if (terminal is null)
        {
            return config.RejectUnknownTerminals || config.RequirePairedTerminal
                ? Result.Fail(Error.Forbidden(
                    "LanRemoteTransport.UnknownTerminalRejected",
                    "Unknown remote terminals are rejected by the LAN transport gate."))
                : Result.Success();
        }

        if (config.RequireActiveTerminal && terminal.Status != TerminalRegistrationStatus.Active)
        {
            string code = terminal.Status switch
            {
                TerminalRegistrationStatus.PendingPairing => "LanRemoteTransport.PendingTerminalRejected",
                TerminalRegistrationStatus.Disabled => "LanRemoteTransport.DisabledTerminalRejected",
                TerminalRegistrationStatus.Revoked => "LanRemoteTransport.RevokedTerminalRejected",
                _ => "LanRemoteTransport.TerminalNotActive"
            };

            return Result.Fail(Error.Forbidden(
                code,
                "Remote terminal is not active."));
        }

        return SalrynHostCommandCategoryCatalog.RequireRemoteSafeCategory(request.CommandCategory);
    }

    public static Result ValidateConfig(LanRemoteTransportConfig config)
    {
        if (config is null)
        {
            return Result.Fail(Error.Validation(
                "LanRemoteTransport.ConfigRequired",
                "LAN remote transport configuration is required."));
        }

        if (string.IsNullOrWhiteSpace(config.BindAddress))
        {
            return Result.Fail(Error.Validation(
                "LanRemoteTransport.BindAddressRequired",
                "LAN remote transport bind address is required."));
        }

        if (SalrynHostCommandLoopbackRules.IsForbiddenBindAddress(config.BindAddress))
        {
            return Result.Fail(Error.Forbidden(
                "LanRemoteTransport.WildcardBindForbidden",
                "Wildcard bind addresses are forbidden in LAN-M6."));
        }

        if (config.Port is < 1 or > 65535)
        {
            return Result.Fail(Error.Validation(
                "LanRemoteTransport.PortInvalid",
                "LAN remote transport port must be between 1 and 65535."));
        }

        if (config.BindMode == LanRemoteTransportBindMode.LoopbackOnly &&
            !SalrynHostCommandLoopbackRules.IsLoopbackOrigin(config.BindAddress))
        {
            return Result.Fail(Error.Validation(
                "LanRemoteTransport.LoopbackBindRequired",
                "Loopback-only transport must bind to a loopback address."));
        }

        if (config.BindMode == LanRemoteTransportBindMode.ExplicitLanAddress &&
            !config.RemoteTransportEnabled)
        {
            return Result.Fail(Error.Forbidden(
                "LanRemoteTransport.LanBindRequiresEnable",
                "Explicit LAN bind requires remote transport to be enabled."));
        }

        if (config.BindMode == LanRemoteTransportBindMode.ExplicitLanAddress &&
            SalrynHostCommandLoopbackRules.IsLoopbackOrigin(config.BindAddress))
        {
            return Result.Fail(Error.Validation(
                "LanRemoteTransport.LanBindAddressRequired",
                "Explicit LAN bind must use a non-loopback LAN address."));
        }

        if (config.AllowOfficialCommandsRemotely)
        {
            return Result.Fail(Error.Forbidden(
                "LanRemoteTransport.OfficialRemoteForbidden",
                "Official remote commands remain disabled in LAN-M6."));
        }

        if (config.AllowAdminCommandsRemotely)
        {
            return Result.Fail(Error.Forbidden(
                "LanRemoteTransport.AdminRemoteForbidden",
                "Admin remote commands remain disabled in LAN-M6."));
        }

        return Result.Success();
    }

    private static Result ValidateRequest(LanRemoteTransportGateRequest request)
    {
        if (request is null)
        {
            return Result.Fail(Error.Validation(
                "LanRemoteTransport.RequestRequired",
                "LAN remote transport gate request is required."));
        }

        if (string.IsNullOrWhiteSpace(request.OriginAddress))
        {
            return Result.Fail(Error.Validation(
                "LanRemoteTransport.OriginRequired",
                "LAN remote transport origin address is required."));
        }

        if (request.TerminalId == Guid.Empty && request.CommandCategory != HostCommandCategory.PairingActivation)
        {
            return Result.Fail(Error.Validation(
                "LanRemoteTransport.TerminalRequired",
                "LAN remote transport terminal id is required."));
        }

        if (string.IsNullOrWhiteSpace(request.CommandName))
        {
            return Result.Fail(Error.Validation(
                "LanRemoteTransport.CommandNameRequired",
                "LAN remote transport command name is required."));
        }

        return Result.Success();
    }
}
