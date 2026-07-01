using Salryn.Core.Host;

namespace Salryn.Host.Local.HostCommands;

public sealed class ControlledRemoteHostCommandTransport : IRemoteHostCommandTransport
{
    private readonly SalrynHostCommandRemoteTransport _transport;

    public ControlledRemoteHostCommandTransport(SalrynHostCommandRemoteTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public Task<SalrynHostCommandResult<TResponse>> SendAsync<TPayload, TResponse>(
        SalrynRemoteHostCommandEnvelope<TPayload>? remoteEnvelope,
        CancellationToken cancellationToken = default)
    {
        return _transport.SendAsync<TPayload, TResponse>(remoteEnvelope, cancellationToken);
    }
}
