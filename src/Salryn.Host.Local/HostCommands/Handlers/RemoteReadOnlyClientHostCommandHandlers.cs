using Salryn.Core.Common;
using Salryn.Core.Host;
using Salryn.Core.RemoteClients;

namespace Salryn.Host.Local.HostCommands.Handlers;

public sealed class RemoteClientStatusHostCommandHandler : IHostCommandHandler
{
    private readonly IRemoteClientStatusHostCommandExecutor _executor;

    public RemoteClientStatusHostCommandHandler(IRemoteClientStatusHostCommandExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public string CommandName => SalrynHostCommandNames.RemoteClientStatus;

    public HostCommandAuthorityRequirements AuthorityRequirements => HostCommandAuthorityRequirements.None;

    public bool RequiresPayload => true;

    public async Task<SalrynHostCommandResult<object?>> HandleAsync(
        SalrynHostCommandEnvelope<object?> envelope,
        CancellationToken cancellationToken = default)
    {
        if (envelope.Payload is not RemoteReadOnlyClientContext context)
        {
            return SalrynHostCommandResult<object?>.Failure(
                envelope,
                Error.Validation(
                    "HostCommand.RemoteClientStatusPayloadInvalid",
                    "Remote client status payload is invalid."));
        }

        Result<RemoteClientStatusHostCommandResponse> result = await _executor
            .ExecuteAsync(context, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return SalrynHostCommandResult<object?>.Failure(envelope, result.ErrorDetail);
        }

        return SalrynHostCommandResult<object?>.Success(result.Value, envelope);
    }
}

public sealed class RemoteDeploymentStatusHostCommandHandler : IHostCommandHandler
{
    private readonly IDeploymentStatusHostCommandExecutor _executor;
    private readonly Func<Salryn.Core.Deployment.DeploymentIdentitySnapshot?> _identityProvider;

    public RemoteDeploymentStatusHostCommandHandler(
        IDeploymentStatusHostCommandExecutor executor,
        Func<Salryn.Core.Deployment.DeploymentIdentitySnapshot?> identityProvider)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _identityProvider = identityProvider ?? throw new ArgumentNullException(nameof(identityProvider));
    }

    public string CommandName => SalrynHostCommandNames.RemoteDeploymentStatus;

    public HostCommandAuthorityRequirements AuthorityRequirements => HostCommandAuthorityRequirements.None;

    public bool RequiresPayload => false;

    public async Task<SalrynHostCommandResult<object?>> HandleAsync(
        SalrynHostCommandEnvelope<object?> envelope,
        CancellationToken cancellationToken = default)
    {
        Result<DeploymentStatusHostCommandResponse> result = await _executor
            .ExecuteAsync(_identityProvider(), cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return SalrynHostCommandResult<object?>.Failure(envelope, result.ErrorDetail);
        }

        return SalrynHostCommandResult<object?>.Success(result.Value, envelope);
    }
}

public sealed class RemoteProductLookupHostCommandHandler : IHostCommandHandler
{
    private readonly IProductLookupHostCommandExecutor _executor;

    public RemoteProductLookupHostCommandHandler(IProductLookupHostCommandExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public string CommandName => SalrynHostCommandNames.RemoteProductLookup;

    public HostCommandAuthorityRequirements AuthorityRequirements => HostCommandAuthorityRequirements.Database;

    public bool RequiresPayload => true;

    public async Task<SalrynHostCommandResult<object?>> HandleAsync(
        SalrynHostCommandEnvelope<object?> envelope,
        CancellationToken cancellationToken = default)
    {
        if (envelope.Payload is not ProductLookupHostCommandRequest request)
        {
            return SalrynHostCommandResult<object?>.Failure(
                envelope,
                Error.Validation(
                    "HostCommand.RemoteProductLookupPayloadInvalid",
                    "Remote product lookup payload is invalid."));
        }

        Result<ProductLookupHostCommandResponse> result = await _executor
            .ExecuteAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return SalrynHostCommandResult<object?>.Failure(envelope, result.ErrorDetail);
        }

        return SalrynHostCommandResult<object?>.Success(result.Value, envelope);
    }
}

public sealed class RemoteDashboardSummaryHostCommandHandler : IHostCommandHandler
{
    private readonly IRemoteDashboardSummaryHostCommandExecutor _executor;

    public RemoteDashboardSummaryHostCommandHandler(IRemoteDashboardSummaryHostCommandExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public string CommandName => SalrynHostCommandNames.RemoteDashboardSummary;

    public HostCommandAuthorityRequirements AuthorityRequirements => HostCommandAuthorityRequirements.None;

    public bool RequiresPayload => true;

    public async Task<SalrynHostCommandResult<object?>> HandleAsync(
        SalrynHostCommandEnvelope<object?> envelope,
        CancellationToken cancellationToken = default)
    {
        if (envelope.Payload is not RemoteReadOnlyClientContext context)
        {
            return SalrynHostCommandResult<object?>.Failure(
                envelope,
                Error.Validation(
                    "HostCommand.RemoteDashboardPayloadInvalid",
                    "Remote dashboard summary payload is invalid."));
        }

        Result<RemoteDashboardSummaryHostCommandResponse> result = await _executor
            .ExecuteAsync(context, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return SalrynHostCommandResult<object?>.Failure(envelope, result.ErrorDetail);
        }

        return SalrynHostCommandResult<object?>.Success(result.Value, envelope);
    }
}

public sealed class RemoteTerminalStatusHostCommandHandler : IHostCommandHandler
{
    private readonly IRemoteTerminalStatusHostCommandExecutor _executor;

    public RemoteTerminalStatusHostCommandHandler(IRemoteTerminalStatusHostCommandExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public string CommandName => SalrynHostCommandNames.RemoteTerminalStatus;

    public HostCommandAuthorityRequirements AuthorityRequirements => HostCommandAuthorityRequirements.Database;

    public bool RequiresPayload => true;

    public async Task<SalrynHostCommandResult<object?>> HandleAsync(
        SalrynHostCommandEnvelope<object?> envelope,
        CancellationToken cancellationToken = default)
    {
        if (envelope.Payload is not RemoteReadOnlyClientContext context)
        {
            return SalrynHostCommandResult<object?>.Failure(
                envelope,
                Error.Validation(
                    "HostCommand.RemoteTerminalStatusPayloadInvalid",
                    "Remote terminal status payload is invalid."));
        }

        Result<RemoteTerminalStatusHostCommandResponse> result = await _executor
            .ExecuteAsync(envelope.TerminalId, context, cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return SalrynHostCommandResult<object?>.Failure(envelope, result.ErrorDetail);
        }

        return SalrynHostCommandResult<object?>.Success(result.Value, envelope);
    }
}
