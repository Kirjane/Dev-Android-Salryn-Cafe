namespace Salryn.Core.Host;

public interface IRemoteHostCommandTransport
{
    Task<SalrynHostCommandResult<TResponse>> SendAsync<TPayload, TResponse>(
        SalrynRemoteHostCommandEnvelope<TPayload>? remoteEnvelope,
        CancellationToken cancellationToken = default);
}
