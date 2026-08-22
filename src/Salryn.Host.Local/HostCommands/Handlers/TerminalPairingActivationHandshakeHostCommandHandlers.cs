using Salryn.Core.Common;
using Salryn.Core.Host;
using Salryn.Core.TerminalRegistry;

namespace Salryn.Host.Local.HostCommands.Handlers;

public sealed class TerminalPairingActivationHostCommandHandler : IHostCommandHandler
{
    private readonly ITerminalPairingActivationHostCommandExecutor _executor;

    public TerminalPairingActivationHostCommandHandler(ITerminalPairingActivationHostCommandExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public string CommandName => SalrynHostCommandNames.TerminalPairingActivate;

    public HostCommandAuthorityRequirements AuthorityRequirements => HostCommandAuthorityRequirements.Database;

    public bool RequiresPayload => true;

    public async Task<SalrynHostCommandResult<object?>> HandleAsync(
        SalrynHostCommandEnvelope<object?> envelope,
        CancellationToken cancellationToken = default)
    {
        if (envelope.Payload is not TerminalPairingActivationHostCommandPayload payload)
        {
            return SalrynHostCommandResult<object?>.Failure(
                envelope,
                Error.Validation(
                    "HostCommand.TerminalPairingActivationPayloadInvalid",
                    "Terminal pairing activation payload is invalid."));
        }

        Result<TerminalPairingActivationResult> result = await _executor
            .ExecuteAsync(payload, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return SalrynHostCommandResult<object?>.Failure(envelope, result.ErrorDetail);
        }

        return SalrynHostCommandResult<object?>.Success(result.Value, envelope);
    }
}

public sealed class RemoteClientHandshakeHostCommandHandler : IHostCommandHandler
{
    private readonly IRemoteClientHandshakeHostCommandExecutor _executor;

    public RemoteClientHandshakeHostCommandHandler(IRemoteClientHandshakeHostCommandExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public string CommandName => SalrynHostCommandNames.TerminalRemoteHandshake;

    public HostCommandAuthorityRequirements AuthorityRequirements => HostCommandAuthorityRequirements.Database;

    public bool RequiresPayload => true;

    public async Task<SalrynHostCommandResult<object?>> HandleAsync(
        SalrynHostCommandEnvelope<object?> envelope,
        CancellationToken cancellationToken = default)
    {
        if (envelope.Payload is not RemoteClientHandshakeHostCommandPayload payload)
        {
            return SalrynHostCommandResult<object?>.Failure(
                envelope,
                Error.Validation(
                    "HostCommand.RemoteHandshakePayloadInvalid",
                    "Remote handshake payload is invalid."));
        }

        Result<RemoteClientHandshakeResult> result = await _executor
            .ExecuteAsync(payload, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return SalrynHostCommandResult<object?>.Failure(envelope, result.ErrorDetail);
        }

        return SalrynHostCommandResult<object?>.Success(result.Value, envelope);
    }
}
