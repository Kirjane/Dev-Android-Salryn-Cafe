using Salryn.Core.Common;
using Salryn.Core.Deployment;

namespace Salryn.Core.Services;

public interface IDeploymentIdentityService
{
    Task<Result<DeploymentIdentitySnapshot?>> GetCurrentAsync(CancellationToken cancellationToken = default);

    Task<Result<bool>> IsConfiguredAsync(CancellationToken cancellationToken = default);

    Task<Result<DeploymentIdentitySnapshot>> ConfigureHostServerAsync(
        HostServerConfigurationRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<DeploymentIdentitySnapshot>> ConfigureAllInOneAsync(
        AllInOneConfigurationRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<DeploymentIdentitySnapshot>> ConfigureClientTerminalAsync(
        ClientTerminalConfigurationRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<DeploymentIdentitySnapshot>> ResetToAllInOneDefaultAsync(
        CancellationToken cancellationToken = default);
}
