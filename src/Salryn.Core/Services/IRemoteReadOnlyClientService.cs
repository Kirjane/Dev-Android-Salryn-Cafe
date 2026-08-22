using Salryn.Core.Common;
using Salryn.Core.Host;
using Salryn.Core.RemoteClients;

namespace Salryn.Core.Services;

public interface IRemoteReadOnlyClientService
{
    Task<Result<RemoteReadOnlyClientResult<RemoteClientStatusHostCommandResponse>>> GetRemoteClientStatusAsync(
        RemoteReadOnlyClientContext context,
        CancellationToken cancellationToken = default);

    Task<Result<RemoteReadOnlyClientResult<DeploymentStatusHostCommandResponse>>> GetDeploymentStatusAsync(
        RemoteReadOnlyClientContext context,
        CancellationToken cancellationToken = default);

    Task<Result<RemoteReadOnlyClientResult<ProductLookupHostCommandResponse>>> LookupProductAsync(
        RemoteReadOnlyClientRequest<ProductLookupHostCommandRequest> request,
        CancellationToken cancellationToken = default);

    Task<Result<RemoteReadOnlyClientResult<RemoteDashboardSummaryHostCommandResponse>>> GetDashboardSummaryAsync(
        RemoteReadOnlyClientContext context,
        CancellationToken cancellationToken = default);

    Task<Result<RemoteReadOnlyClientResult<RemoteTerminalStatusHostCommandResponse>>> GetTerminalStatusAsync(
        RemoteReadOnlyClientContext context,
        CancellationToken cancellationToken = default);
}
