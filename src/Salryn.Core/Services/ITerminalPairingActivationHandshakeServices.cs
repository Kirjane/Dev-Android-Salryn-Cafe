using Salryn.Core.Common;
using Salryn.Core.TerminalRegistry;

namespace Salryn.Core.Services;

public interface ITerminalPairingActivationService
{
    Task<Result<TerminalPairingActivationResult>> ActivatePairingAsync(
        TerminalPairingActivationRequest request,
        CancellationToken cancellationToken = default);
}

public interface IRemoteClientHandshakeService
{
    Task<Result<RemoteClientHandshakeResult>> HandshakeAsync(
        RemoteClientHandshakeRequest request,
        CancellationToken cancellationToken = default);

    Task<Result> ValidateHandshakeSessionAsync(
        Guid terminalId,
        Guid handshakeSessionId,
        CancellationToken cancellationToken = default);
}
