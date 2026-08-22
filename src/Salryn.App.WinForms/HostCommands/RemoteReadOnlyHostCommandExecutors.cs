using Salryn.Core.Common;
using Salryn.Core.Host;
using Salryn.Core.RemoteClients;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Core.TerminalRegistry;

namespace Salryn.App.WinForms.HostCommands;

internal sealed class RemoteClientStatusHostCommandExecutor : IRemoteClientStatusHostCommandExecutor
{
    public Task<Result<RemoteClientStatusHostCommandResponse>> ExecuteAsync(
        RemoteReadOnlyClientContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        RemoteClientStatusHostCommandResponse response = new(
            RemoteReadOnlyClientStatus.Ready,
            context.TerminalId,
            context.StoreId,
            context.HostId,
            RemoteReadOnlyClientCapabilityCatalog.SafeCapabilities,
            "LAN-M7 remote read-only client can use status, product, dashboard, and terminal status reads only.");

        return Task.FromResult(Result<RemoteClientStatusHostCommandResponse>.Success(response));
    }
}

internal sealed class RemoteDashboardSummaryHostCommandExecutor : IRemoteDashboardSummaryHostCommandExecutor
{
    public Task<Result<RemoteDashboardSummaryHostCommandResponse>> ExecuteAsync(
        RemoteReadOnlyClientContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        RemoteDashboardSummaryHostCommandResponse response = new(
            DateTimeOffset.UtcNow,
            context.TerminalId,
            "LAN-M7 safe read-only dashboard foundation",
            RemoteReadOnlyClientCapabilityCatalog.SafeCapabilities,
            "Dashboard read is limited to safe remote-client capability/status information in LAN-M7.");

        return Task.FromResult(Result<RemoteDashboardSummaryHostCommandResponse>.Success(response));
    }
}

internal sealed class RemoteTerminalStatusHostCommandExecutor : IRemoteTerminalStatusHostCommandExecutor
{
    private readonly ITerminalRegistryService _terminalRegistry;

    public RemoteTerminalStatusHostCommandExecutor(ITerminalRegistryService terminalRegistry)
    {
        _terminalRegistry = terminalRegistry ?? throw new ArgumentNullException(nameof(terminalRegistry));
    }

    public async Task<Result<RemoteTerminalStatusHostCommandResponse>> ExecuteAsync(
        Guid terminalId,
        RemoteReadOnlyClientContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        TerminalRegistryActorContext actor = new(
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            new[] { SalrynPermissions.StoreTerminalView });

        Result<SalrynTerminalRegistration?> terminalResult = await _terminalRegistry
            .GetTerminalAsync(terminalId, actor, cancellationToken)
            .ConfigureAwait(false);

        if (terminalResult.IsFailure)
        {
            return Result<RemoteTerminalStatusHostCommandResponse>.Fail(terminalResult.ErrorDetail);
        }

        SalrynTerminalRegistration? terminal = terminalResult.Value;
        if (terminal is null)
        {
            return Result<RemoteTerminalStatusHostCommandResponse>.Fail(Error.NotFound(
                "RemoteTerminalStatus.TerminalUnknown",
                "Remote terminal is not registered."));
        }

        if (terminal.StoreId != context.StoreId || terminal.HostId != context.HostId)
        {
            return Result<RemoteTerminalStatusHostCommandResponse>.Fail(Error.Forbidden(
                "RemoteTerminalStatus.ScopeMismatch",
                "Remote terminal does not match the requested store or host."));
        }

        return Result<RemoteTerminalStatusHostCommandResponse>.Success(
            new RemoteTerminalStatusHostCommandResponse(
                terminal.TerminalId,
                terminal.StoreId,
                terminal.HostId,
                terminal.TerminalName,
                terminal.TerminalRole,
                terminal.Status,
                terminal.RegisteredAtUtc,
                terminal.LastSeenAtUtc));
    }
}
