using Salryn.Core.Host;
using Salryn.Core.TerminalRegistry;

namespace Salryn.Core.RemoteClients;

public enum RemoteReadOnlyClientCapability
{
    StatusRead = 1,
    ProductRead = 2,
    DashboardRead = 3,
    TerminalStatusRead = 4,
    CheckoutMutation = 100,
    SalesMutation = 101,
    InventoryMutation = 102,
    OfficialJournalMutation = 103,
    OfficialSequenceMutation = 104,
    AdminMutation = 105,
    BackupMutation = 106
}

public enum RemoteReadOnlyClientStatus
{
    NotReady = 1,
    Ready = 2,
    SessionRequired = 3,
    Rejected = 4
}

public sealed record RemoteReadOnlyClientContext(
    Guid TerminalId,
    Guid StoreId,
    Guid HostId,
    Guid HandshakeSessionId,
    string OriginAddress,
    DateTimeOffset RequestedAtUtc,
    string? ClientMachineName,
    string? ClientFingerprint);

public sealed record RemoteReadOnlyClientRequest<TPayload>(
    Guid RequestId,
    RemoteReadOnlyClientContext Context,
    TPayload Payload);

public sealed record RemoteReadOnlyClientResult<TResponse>(
    bool Succeeded,
    Guid RequestId,
    Guid TerminalId,
    Guid StoreId,
    TResponse? Response,
    string? ErrorCode,
    string? ErrorMessage,
    IReadOnlyList<RemoteReadOnlyClientCapability> AllowedCapabilities,
    HostCommandTraceMetadata? Trace)
{
    public static RemoteReadOnlyClientResult<TResponse> FromHostCommandResult(
        RemoteReadOnlyClientContext context,
        SalrynHostCommandResult<TResponse> result) =>
        new(
            result.Succeeded,
            result.RequestId,
            context.TerminalId,
            context.StoreId,
            result.Response,
            result.ErrorCode,
            result.ErrorMessage,
            RemoteReadOnlyClientCapabilityCatalog.SafeCapabilities,
            result.Trace);
}

public static class RemoteReadOnlyClientCapabilityCatalog
{
    public static readonly RemoteReadOnlyClientCapability[] SafeCapabilities =
    {
        RemoteReadOnlyClientCapability.StatusRead,
        RemoteReadOnlyClientCapability.ProductRead,
        RemoteReadOnlyClientCapability.DashboardRead,
        RemoteReadOnlyClientCapability.TerminalStatusRead
    };
}

public sealed record RemoteClientStatusHostCommandResponse(
    RemoteReadOnlyClientStatus Status,
    Guid TerminalId,
    Guid StoreId,
    Guid HostId,
    IReadOnlyList<RemoteReadOnlyClientCapability> AllowedCapabilities,
    string Message);

public sealed record RemoteDashboardSummaryHostCommandResponse(
    DateTimeOffset GeneratedAtUtc,
    Guid TerminalId,
    string SummaryScope,
    IReadOnlyList<RemoteReadOnlyClientCapability> AllowedCapabilities,
    string Message);

public sealed record RemoteTerminalStatusHostCommandResponse(
    Guid TerminalId,
    Guid StoreId,
    Guid HostId,
    string TerminalName,
    TerminalRegistrationRole TerminalRole,
    TerminalRegistrationStatus TerminalStatus,
    DateTimeOffset RegisteredAtUtc,
    DateTimeOffset? LastSeenAtUtc);
