using Salryn.Core.Common;
using Salryn.Core.Deployment;
using Salryn.Core.Host;

namespace Salryn.Host.Local.HostCommands.Handlers;

public sealed class DeploymentStatusHostCommandHandler : IHostCommandHandler
{
    private readonly IDeploymentStatusHostCommandExecutor _executor;
    private readonly Func<DeploymentIdentitySnapshot?> _identityProvider;

    public DeploymentStatusHostCommandHandler(
        IDeploymentStatusHostCommandExecutor executor,
        Func<DeploymentIdentitySnapshot?> identityProvider)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _identityProvider = identityProvider ?? throw new ArgumentNullException(nameof(identityProvider));
    }

    public string CommandName => SalrynHostCommandNames.DeploymentStatus;

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
