using Salryn.Core.Common;
using Salryn.Core.Deployment;
using Salryn.Core.Host;
using Salryn.Core.RemoteClients;
using Salryn.Core.Services;

namespace Salryn.Api.Local.HostCommands;

public sealed class RemoteReadOnlyClientService : IRemoteReadOnlyClientService
{
    private static readonly Guid RemoteReadOnlyUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private readonly IRemoteHostCommandTransport _transport;

    public RemoteReadOnlyClientService(IRemoteHostCommandTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public Task<Result<RemoteReadOnlyClientResult<RemoteClientStatusHostCommandResponse>>> GetRemoteClientStatusAsync(
        RemoteReadOnlyClientContext context,
        CancellationToken cancellationToken = default) =>
        SendAsync<RemoteReadOnlyClientContext, RemoteClientStatusHostCommandResponse>(
            context,
            SalrynHostCommandNames.RemoteClientStatus,
            context,
            Guid.NewGuid(),
            cancellationToken);

    public Task<Result<RemoteReadOnlyClientResult<DeploymentStatusHostCommandResponse>>> GetDeploymentStatusAsync(
        RemoteReadOnlyClientContext context,
        CancellationToken cancellationToken = default) =>
        SendAsync<object?, DeploymentStatusHostCommandResponse>(
            context,
            SalrynHostCommandNames.RemoteDeploymentStatus,
            null,
            Guid.NewGuid(),
            cancellationToken);

    public Task<Result<RemoteReadOnlyClientResult<ProductLookupHostCommandResponse>>> LookupProductAsync(
        RemoteReadOnlyClientRequest<ProductLookupHostCommandRequest> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return SendAsync<ProductLookupHostCommandRequest, ProductLookupHostCommandResponse>(
            request.Context,
            SalrynHostCommandNames.RemoteProductLookup,
            request.Payload,
            request.RequestId,
            cancellationToken);
    }

    public Task<Result<RemoteReadOnlyClientResult<RemoteDashboardSummaryHostCommandResponse>>> GetDashboardSummaryAsync(
        RemoteReadOnlyClientContext context,
        CancellationToken cancellationToken = default) =>
        SendAsync<RemoteReadOnlyClientContext, RemoteDashboardSummaryHostCommandResponse>(
            context,
            SalrynHostCommandNames.RemoteDashboardSummary,
            context,
            Guid.NewGuid(),
            cancellationToken);

    public Task<Result<RemoteReadOnlyClientResult<RemoteTerminalStatusHostCommandResponse>>> GetTerminalStatusAsync(
        RemoteReadOnlyClientContext context,
        CancellationToken cancellationToken = default) =>
        SendAsync<RemoteReadOnlyClientContext, RemoteTerminalStatusHostCommandResponse>(
            context,
            SalrynHostCommandNames.RemoteTerminalStatus,
            context,
            Guid.NewGuid(),
            cancellationToken);

    private async Task<Result<RemoteReadOnlyClientResult<TResponse>>> SendAsync<TPayload, TResponse>(
        RemoteReadOnlyClientContext context,
        string commandName,
        TPayload payload,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        Result contextValidation = ValidateContext(context);
        if (contextValidation.IsFailure)
        {
            return Result<RemoteReadOnlyClientResult<TResponse>>.Success(
                Failure<TResponse>(context, requestId, contextValidation.ErrorDetail));
        }

        SalrynHostCommandEnvelope<TPayload> envelope = new(
            CommandName: commandName,
            RequestId: requestId,
            StoreId: context.StoreId,
            HostId: context.HostId,
            TerminalId: context.TerminalId,
            UserId: RemoteReadOnlyUserId,
            SessionId: context.HandshakeSessionId,
            TimestampUtc: context.RequestedAtUtc,
            SourceMachineRole: InstallationRole.ClientTerminal,
            InstallationRole: InstallationRole.ClientTerminal,
            Payload: payload);

        SalrynHostCommandResult<TResponse> result = await _transport
            .SendAsync<TPayload, TResponse>(
                new SalrynRemoteHostCommandEnvelope<TPayload>(
                    context.OriginAddress,
                    context.TerminalId,
                    envelope,
                    context.HandshakeSessionId),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<RemoteReadOnlyClientResult<TResponse>>.Success(
            RemoteReadOnlyClientResult<TResponse>.FromHostCommandResult(context, result));
    }

    private static Result ValidateContext(RemoteReadOnlyClientContext context)
    {
        if (context is null)
        {
            return Result.Fail(Error.Validation(
                "RemoteReadOnlyClient.ContextRequired",
                "Remote read-only client context is required."));
        }

        if (context.TerminalId == Guid.Empty || context.StoreId == Guid.Empty || context.HostId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "RemoteReadOnlyClient.ScopeRequired",
                "Terminal, store, and host IDs are required."));
        }

        if (context.HandshakeSessionId == Guid.Empty)
        {
            return Result.Fail(Error.Forbidden(
                "RemoteReadOnlyClient.HandshakeSessionRequired",
                "Remote read-only client operations require a successful handshake session."));
        }

        if (string.IsNullOrWhiteSpace(context.OriginAddress))
        {
            return Result.Fail(Error.Validation(
                "RemoteReadOnlyClient.OriginRequired",
                "Remote read-only client origin address is required."));
        }

        if (context.RequestedAtUtc == default)
        {
            return Result.Fail(Error.Validation(
                "RemoteReadOnlyClient.TimestampRequired",
                "Remote read-only client request timestamp is required."));
        }

        return Result.Success();
    }

    private static RemoteReadOnlyClientResult<TResponse> Failure<TResponse>(
        RemoteReadOnlyClientContext? context,
        Guid requestId,
        Error error)
    {
        return new RemoteReadOnlyClientResult<TResponse>(
            false,
            requestId,
            context?.TerminalId ?? Guid.Empty,
            context?.StoreId ?? Guid.Empty,
            default,
            error.Code,
            error.Message,
            RemoteReadOnlyClientCapabilityCatalog.SafeCapabilities,
            null);
    }
}
