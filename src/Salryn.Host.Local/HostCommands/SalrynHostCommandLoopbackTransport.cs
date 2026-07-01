using Salryn.Core.Common;
using Salryn.Core.Deployment;
using Salryn.Core.Host;

namespace Salryn.Host.Local.HostCommands;

public sealed class SalrynHostCommandLoopbackTransport
{
    private readonly IHostCommandDispatcher _dispatcher;
    private readonly SalrynHostCommandAuthorityGuard _authorityGuard;
    private readonly Func<DeploymentIdentitySnapshot?> _identityProvider;

    public SalrynHostCommandLoopbackTransport(
        IHostCommandDispatcher dispatcher,
        SalrynHostCommandAuthorityGuard authorityGuard,
        Func<DeploymentIdentitySnapshot?> identityProvider)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _authorityGuard = authorityGuard ?? throw new ArgumentNullException(nameof(authorityGuard));
        _identityProvider = identityProvider ?? throw new ArgumentNullException(nameof(identityProvider));
    }

    public Task<SalrynHostCommandResult<TResponse>> SendAsync<TPayload, TResponse>(
        string originHost,
        SalrynHostCommandEnvelope<TPayload> envelope,
        CancellationToken cancellationToken = default)
    {
        SalrynHostCommandEnvelope<object?> normalized = new(
            envelope.CommandName,
            envelope.RequestId,
            envelope.StoreId,
            envelope.HostId,
            envelope.TerminalId,
            envelope.UserId,
            envelope.SessionId,
            envelope.TimestampUtc,
            envelope.SourceMachineRole,
            envelope.InstallationRole,
            envelope.Payload);

        Result loopback = _authorityGuard.RequireLocalLoopbackAllowed(originHost, _identityProvider());

        if (loopback.IsFailure)
        {
            return Task.FromResult(SalrynHostCommandResult<TResponse>.Failure(normalized, loopback.ErrorDetail));
        }

        return _dispatcher.DispatchAsync<TPayload, TResponse>(envelope, cancellationToken);
    }
}
