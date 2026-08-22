using Salryn.Core.Common;
using Salryn.Core.Host;
using Salryn.Core.TerminalRegistry;

namespace Salryn.Host.Local.HostCommands.Handlers;

public sealed class TerminalRegistryListHostCommandHandler : IHostCommandHandler
{
    private readonly ITerminalRegistryListHostCommandExecutor _executor;

    public TerminalRegistryListHostCommandHandler(ITerminalRegistryListHostCommandExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public string CommandName => SalrynHostCommandNames.TerminalRegistryList;

    public HostCommandAuthorityRequirements AuthorityRequirements => HostCommandAuthorityRequirements.Database;

    public bool RequiresPayload => true;

    public async Task<SalrynHostCommandResult<object?>> HandleAsync(
        SalrynHostCommandEnvelope<object?> envelope,
        CancellationToken cancellationToken = default)
    {
        if (envelope.Payload is not TerminalRegistryActorContext actor)
        {
            return SalrynHostCommandResult<object?>.Failure(
                envelope,
                Error.Validation(
                    "HostCommand.TerminalRegistryActorInvalid",
                    "Terminal registry actor context is invalid."));
        }

        Result<TerminalRegistryListHostCommandResponse> result = await _executor
            .ExecuteAsync(actor, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return SalrynHostCommandResult<object?>.Failure(envelope, result.ErrorDetail);
        }

        return SalrynHostCommandResult<object?>.Success(result.Value, envelope);
    }
}

public sealed class TerminalRegistryDisableHostCommandHandler : IHostCommandHandler
{
    private readonly ITerminalRegistryDisableHostCommandExecutor _executor;

    public TerminalRegistryDisableHostCommandHandler(ITerminalRegistryDisableHostCommandExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public string CommandName => SalrynHostCommandNames.TerminalRegistryDisable;

    public HostCommandAuthorityRequirements AuthorityRequirements => HostCommandAuthorityRequirements.Database;

    public bool RequiresPayload => true;

    public async Task<SalrynHostCommandResult<object?>> HandleAsync(
        SalrynHostCommandEnvelope<object?> envelope,
        CancellationToken cancellationToken = default)
    {
        if (envelope.Payload is not TerminalRegistryDisableHostCommandPayload payload)
        {
            return SalrynHostCommandResult<object?>.Failure(
                envelope,
                Error.Validation(
                    "HostCommand.TerminalRegistryDisablePayloadInvalid",
                    "Terminal registry disable payload is invalid."));
        }

        Result<SalrynTerminalRegistration> result = await _executor
            .ExecuteAsync(payload, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return SalrynHostCommandResult<object?>.Failure(envelope, result.ErrorDetail);
        }

        return SalrynHostCommandResult<object?>.Success(result.Value, envelope);
    }
}

public sealed class TerminalRegistryRevokeHostCommandHandler : IHostCommandHandler
{
    private readonly ITerminalRegistryRevokeHostCommandExecutor _executor;

    public TerminalRegistryRevokeHostCommandHandler(ITerminalRegistryRevokeHostCommandExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public string CommandName => SalrynHostCommandNames.TerminalRegistryRevoke;

    public HostCommandAuthorityRequirements AuthorityRequirements => HostCommandAuthorityRequirements.Database;

    public bool RequiresPayload => true;

    public async Task<SalrynHostCommandResult<object?>> HandleAsync(
        SalrynHostCommandEnvelope<object?> envelope,
        CancellationToken cancellationToken = default)
    {
        if (envelope.Payload is not TerminalRegistryRevokeHostCommandPayload payload)
        {
            return SalrynHostCommandResult<object?>.Failure(
                envelope,
                Error.Validation(
                    "HostCommand.TerminalRegistryRevokePayloadInvalid",
                    "Terminal registry revoke payload is invalid."));
        }

        Result<SalrynTerminalRegistration> result = await _executor
            .ExecuteAsync(payload, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return SalrynHostCommandResult<object?>.Failure(envelope, result.ErrorDetail);
        }

        return SalrynHostCommandResult<object?>.Success(result.Value, envelope);
    }
}

public sealed class TerminalPairingIssueHostCommandHandler : IHostCommandHandler
{
    private readonly ITerminalPairingIssueHostCommandExecutor _executor;

    public TerminalPairingIssueHostCommandHandler(ITerminalPairingIssueHostCommandExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public string CommandName => SalrynHostCommandNames.TerminalPairingIssue;

    public HostCommandAuthorityRequirements AuthorityRequirements => HostCommandAuthorityRequirements.Database;

    public bool RequiresPayload => true;

    public async Task<SalrynHostCommandResult<object?>> HandleAsync(
        SalrynHostCommandEnvelope<object?> envelope,
        CancellationToken cancellationToken = default)
    {
        if (envelope.Payload is not TerminalPairingIssueHostCommandPayload payload)
        {
            return SalrynHostCommandResult<object?>.Failure(
                envelope,
                Error.Validation(
                    "HostCommand.TerminalPairingIssuePayloadInvalid",
                    "Terminal pairing issue payload is invalid."));
        }

        Result<TerminalPairingIssueHostCommandResponse> result = await _executor
            .ExecuteAsync(payload, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return SalrynHostCommandResult<object?>.Failure(envelope, result.ErrorDetail);
        }

        return SalrynHostCommandResult<object?>.Success(result.Value, envelope);
    }
}

public sealed class TerminalPairingRevokeHostCommandHandler : IHostCommandHandler
{
    private readonly ITerminalPairingRevokeHostCommandExecutor _executor;

    public TerminalPairingRevokeHostCommandHandler(ITerminalPairingRevokeHostCommandExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public string CommandName => SalrynHostCommandNames.TerminalPairingRevoke;

    public HostCommandAuthorityRequirements AuthorityRequirements => HostCommandAuthorityRequirements.Database;

    public bool RequiresPayload => true;

    public async Task<SalrynHostCommandResult<object?>> HandleAsync(
        SalrynHostCommandEnvelope<object?> envelope,
        CancellationToken cancellationToken = default)
    {
        if (envelope.Payload is not TerminalPairingRevokeHostCommandPayload payload)
        {
            return SalrynHostCommandResult<object?>.Failure(
                envelope,
                Error.Validation(
                    "HostCommand.TerminalPairingRevokePayloadInvalid",
                    "Terminal pairing revoke payload is invalid."));
        }

        Result<SalrynTerminalPairingInvite> result = await _executor
            .ExecuteAsync(payload, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return SalrynHostCommandResult<object?>.Failure(envelope, result.ErrorDetail);
        }

        return SalrynHostCommandResult<object?>.Success(result.Value, envelope);
    }
}

public sealed class TerminalPairingListHostCommandHandler : IHostCommandHandler
{
    private readonly ITerminalPairingListHostCommandExecutor _executor;

    public TerminalPairingListHostCommandHandler(ITerminalPairingListHostCommandExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public string CommandName => SalrynHostCommandNames.TerminalPairingList;

    public HostCommandAuthorityRequirements AuthorityRequirements => HostCommandAuthorityRequirements.Database;

    public bool RequiresPayload => true;

    public async Task<SalrynHostCommandResult<object?>> HandleAsync(
        SalrynHostCommandEnvelope<object?> envelope,
        CancellationToken cancellationToken = default)
    {
        if (envelope.Payload is not TerminalRegistryActorContext actor)
        {
            return SalrynHostCommandResult<object?>.Failure(
                envelope,
                Error.Validation(
                    "HostCommand.TerminalPairingActorInvalid",
                    "Terminal pairing actor context is invalid."));
        }

        Result<TerminalPairingListHostCommandResponse> result = await _executor
            .ExecuteAsync(actor, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return SalrynHostCommandResult<object?>.Failure(envelope, result.ErrorDetail);
        }

        return SalrynHostCommandResult<object?>.Success(result.Value, envelope);
    }
}
