using Salryn.Core.Common;
using Salryn.Core.Deployment;
using Salryn.Core.Host;
using Salryn.Core.Services;
using Salryn.Core.TerminalRegistry;

namespace Salryn.Host.Local.HostCommands;

public sealed class SalrynHostCommandRemoteTransport
{
    private readonly IHostCommandDispatcher _dispatcher;
    private readonly ILanRemoteTransportGate _gate;
    private readonly IRemoteClientHandshakeService _handshakeService;

    public SalrynHostCommandRemoteTransport(
        IHostCommandDispatcher dispatcher,
        ILanRemoteTransportGate gate)
        : this(dispatcher, gate, new RemoteHandshakeRequiredService())
    {
    }

    public SalrynHostCommandRemoteTransport(
        IHostCommandDispatcher dispatcher,
        ILanRemoteTransportGate gate,
        IRemoteClientHandshakeService handshakeService)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _handshakeService = handshakeService ?? throw new ArgumentNullException(nameof(handshakeService));
    }

    public async Task<SalrynHostCommandResult<TResponse>> SendAsync<TPayload, TResponse>(
        SalrynRemoteHostCommandEnvelope<TPayload>? remoteEnvelope,
        CancellationToken cancellationToken = default)
    {
        SalrynHostCommandEnvelope<object?> failureEnvelope = CreateFailureEnvelope(
            remoteEnvelope?.HostEnvelope?.CommandName,
            remoteEnvelope?.TerminalId ?? Guid.Empty);

        if (remoteEnvelope?.HostEnvelope is null)
        {
            return SalrynHostCommandResult<TResponse>.Failure(
                failureEnvelope,
                Error.Validation(
                    "LanRemoteTransport.MalformedEnvelope",
                    "Remote host command envelope is malformed."));
        }

        SalrynHostCommandEnvelope<TPayload> hostEnvelope = remoteEnvelope.HostEnvelope;
        SalrynHostCommandEnvelope<object?> normalized = new(
            hostEnvelope.CommandName,
            hostEnvelope.RequestId,
            hostEnvelope.StoreId,
            hostEnvelope.HostId,
            hostEnvelope.TerminalId,
            hostEnvelope.UserId,
            hostEnvelope.SessionId,
            hostEnvelope.TimestampUtc,
            hostEnvelope.SourceMachineRole,
            hostEnvelope.InstallationRole,
            hostEnvelope.Payload);

        Result envelopeValidation = SalrynHostCommandEnvelopeValidator.Validate(
            normalized,
            payloadRequired: false);

        if (envelopeValidation.IsFailure)
        {
            return SalrynHostCommandResult<TResponse>.Failure(normalized, envelopeValidation.ErrorDetail);
        }

        if (!SalrynHostCommandCategoryCatalog.TryResolve(hostEnvelope.CommandName, out HostCommandCategory category))
        {
            return SalrynHostCommandResult<TResponse>.Failure(
                normalized,
                Error.NotFound(
                    "LanRemoteTransport.UnknownCommand",
                    "Remote host command is not registered for transport gate evaluation."));
        }

        Guid terminalId = remoteEnvelope.TerminalId == Guid.Empty
            ? hostEnvelope.TerminalId
            : remoteEnvelope.TerminalId;

        Result gateResult = await _gate.AuthorizeAsync(
                new LanRemoteTransportGateRequest(
                    remoteEnvelope.OriginAddress,
                    terminalId,
                    hostEnvelope.CommandName,
                    category),
                cancellationToken)
            .ConfigureAwait(false);

        if (gateResult.IsFailure)
        {
            return SalrynHostCommandResult<TResponse>.Failure(normalized, gateResult.ErrorDetail);
        }

        Result identityProof = await RequireHandshakeProofAsync(
                remoteEnvelope,
                terminalId,
                category,
                cancellationToken)
            .ConfigureAwait(false);

        if (identityProof.IsFailure)
        {
            return SalrynHostCommandResult<TResponse>.Failure(normalized, identityProof.ErrorDetail);
        }

        return await _dispatcher.DispatchAsync<TPayload, TResponse>(hostEnvelope, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result> RequireHandshakeProofAsync<TPayload>(
        SalrynRemoteHostCommandEnvelope<TPayload> remoteEnvelope,
        Guid terminalId,
        HostCommandCategory category,
        CancellationToken cancellationToken)
    {
        if (SalrynHostCommandLoopbackRules.IsLoopbackOrigin(remoteEnvelope.OriginAddress))
        {
            return Result.Success();
        }

        if (category is HostCommandCategory.PairingActivation or HostCommandCategory.RemoteHandshake)
        {
            return Result.Success();
        }

        if (category is not (HostCommandCategory.StatusRead or HostCommandCategory.ProductRead or HostCommandCategory.DashboardRead or HostCommandCategory.TerminalStatusRead))
        {
            return Result.Fail(Error.Forbidden(
                "RemoteHandshake.CategoryBlocked",
                "Only safe status, product, dashboard, and terminal status reads can be executed after remote handshake in LAN-M7."));
        }

        if (!remoteEnvelope.HandshakeSessionId.HasValue)
        {
            return Result.Fail(Error.Forbidden(
                "RemoteHandshake.SessionRequired",
                "Safe remote commands require a successful remote client handshake session."));
        }

        return await _handshakeService
            .ValidateHandshakeSessionAsync(terminalId, remoteEnvelope.HandshakeSessionId.Value, cancellationToken)
            .ConfigureAwait(false);
    }

    private static SalrynHostCommandEnvelope<object?> CreateFailureEnvelope(
        string? commandName,
        Guid terminalId)
    {
        return new SalrynHostCommandEnvelope<object?>(
            CommandName: string.IsNullOrWhiteSpace(commandName) ? "lan.remote.malformed" : commandName.Trim(),
            RequestId: Guid.NewGuid(),
            StoreId: Guid.Empty,
            HostId: Guid.Empty,
            TerminalId: terminalId,
            UserId: Guid.Empty,
            SessionId: null,
            TimestampUtc: DateTimeOffset.UtcNow,
            SourceMachineRole: InstallationRole.ClientTerminal,
            InstallationRole: InstallationRole.ClientTerminal,
            Payload: null);
    }

    private sealed class RemoteHandshakeRequiredService : IRemoteClientHandshakeService
    {
        public Task<Result<RemoteClientHandshakeResult>> HandshakeAsync(
            RemoteClientHandshakeRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<RemoteClientHandshakeResult>.Fail(Error.Forbidden(
                "RemoteHandshake.ServiceNotConfigured",
                "Remote handshake service is not configured for this transport instance.")));
        }

        public Task<Result> ValidateHandshakeSessionAsync(
            Guid terminalId,
            Guid handshakeSessionId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result.Fail(Error.Forbidden(
                "RemoteHandshake.ServiceNotConfigured",
                "Remote handshake service is required before safe remote commands can run.")));
        }

        public Task<Result<RemoteClientHandshakeResult>> RefreshTrustedTerminalHandshakeAsync(
            Guid terminalId,
            string originAddress,
            string? clientMachineName,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<RemoteClientHandshakeResult>.Fail(Error.Forbidden(
                "RemoteHandshake.ServiceNotConfigured",
                "Remote handshake service is not configured for this transport instance.")));
        }
    }
}
