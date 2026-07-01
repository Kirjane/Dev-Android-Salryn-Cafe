using Salryn.Core.Common;

namespace Salryn.Core.Host;

public enum LanRemoteTransportBindMode
{
    LoopbackOnly = 1,
    ExplicitLanAddress = 2
}

public enum HostCommandCategory
{
    StatusRead = 1,
    ProductRead = 2,
    TerminalPairingRead = 3,
    AdminRead = 4,
    AdminMutation = 5,
    OfficialMutation = 6,
    BackupMutation = 7,
    AuthorityProbe = 8,
    PairingActivation = 9,
    RemoteHandshake = 10
}

public sealed record LanRemoteTransportConfig(
    bool RemoteTransportEnabled,
    LanRemoteTransportBindMode BindMode,
    string BindAddress,
    int Port,
    bool RequirePairedTerminal,
    bool RequireActiveTerminal,
    bool RejectUnknownTerminals,
    bool AllowOfficialCommandsRemotely,
    bool AllowAdminCommandsRemotely,
    DateTimeOffset UpdatedAtUtc,
    Guid? UpdatedByUserId)
{
    public const string DefaultBindAddress = SalrynHostCommandLoopbackRules.LoopbackHost;
    public const int DefaultPort = 58161;

    public static LanRemoteTransportConfig CreateDefault(DateTimeOffset? updatedAtUtc = null) =>
        new(
            RemoteTransportEnabled: false,
            BindMode: LanRemoteTransportBindMode.LoopbackOnly,
            BindAddress: DefaultBindAddress,
            Port: DefaultPort,
            RequirePairedTerminal: true,
            RequireActiveTerminal: true,
            RejectUnknownTerminals: true,
            AllowOfficialCommandsRemotely: false,
            AllowAdminCommandsRemotely: false,
            UpdatedAtUtc: updatedAtUtc ?? DateTimeOffset.UtcNow,
            UpdatedByUserId: null);
}

public sealed record LanRemoteTransportConfigUpdateRequest(
    bool RemoteTransportEnabled,
    LanRemoteTransportBindMode BindMode,
    string BindAddress,
    int Port,
    bool RequirePairedTerminal,
    bool RequireActiveTerminal,
    bool RejectUnknownTerminals,
    bool AllowOfficialCommandsRemotely,
    bool AllowAdminCommandsRemotely,
    Guid? UpdatedByUserId);

public interface ILanRemoteTransportConfigService
{
    Task<Result<LanRemoteTransportConfig>> GetCurrentAsync(
        CancellationToken cancellationToken = default);

    Task<Result<LanRemoteTransportConfig>> SaveAsync(
        LanRemoteTransportConfigUpdateRequest request,
        CancellationToken cancellationToken = default);

    Task<Result<LanRemoteTransportConfig>> DisableAsync(
        Guid? updatedByUserId,
        CancellationToken cancellationToken = default);
}

public sealed record LanRemoteTransportGateRequest(
    string OriginAddress,
    Guid TerminalId,
    string CommandName,
    HostCommandCategory CommandCategory);

public sealed record SalrynRemoteHostCommandEnvelope<TPayload>(
    string OriginAddress,
    Guid TerminalId,
    SalrynHostCommandEnvelope<TPayload>? HostEnvelope,
    Guid? HandshakeSessionId = null);

public static class SalrynHostCommandCategoryCatalog
{
    public static bool TryResolve(string? commandName, out HostCommandCategory category)
    {
        category = HostCommandCategory.AdminMutation;

        if (string.IsNullOrWhiteSpace(commandName))
        {
            return false;
        }

        string normalized = commandName.Trim();

        category = normalized switch
        {
            SalrynHostCommandNames.DeploymentStatus => HostCommandCategory.StatusRead,
            SalrynHostCommandNames.ProductLookup => HostCommandCategory.ProductRead,
            SalrynHostCommandNames.TerminalRegistryList => HostCommandCategory.TerminalPairingRead,
            SalrynHostCommandNames.TerminalPairingList => HostCommandCategory.TerminalPairingRead,
            SalrynHostCommandNames.TerminalPairingActivate => HostCommandCategory.PairingActivation,
            SalrynHostCommandNames.TerminalRemoteHandshake => HostCommandCategory.RemoteHandshake,
            SalrynHostCommandNames.TerminalRegistryDisable => HostCommandCategory.AdminMutation,
            SalrynHostCommandNames.TerminalRegistryRevoke => HostCommandCategory.AdminMutation,
            SalrynHostCommandNames.TerminalPairingIssue => HostCommandCategory.AdminMutation,
            SalrynHostCommandNames.TerminalPairingRevoke => HostCommandCategory.AdminMutation,
            SalrynHostCommandNames.OfficialCheckoutComplete => HostCommandCategory.OfficialMutation,
            SalrynHostCommandNames.OfficialLifecycleAppend => HostCommandCategory.OfficialMutation,
            SalrynHostCommandNames.OfficialReadingPreviewX => HostCommandCategory.OfficialMutation,
            SalrynHostCommandNames.OfficialReadingFinalizeZ => HostCommandCategory.OfficialMutation,
            SalrynHostCommandNames.BackupAuthorityProbe => HostCommandCategory.AuthorityProbe,
            _ => HostCommandCategory.AdminMutation
        };

        return normalized == SalrynHostCommandNames.DeploymentStatus ||
            normalized == SalrynHostCommandNames.ProductLookup ||
            normalized == SalrynHostCommandNames.TerminalRegistryList ||
            normalized == SalrynHostCommandNames.TerminalPairingList ||
            normalized == SalrynHostCommandNames.TerminalPairingActivate ||
            normalized == SalrynHostCommandNames.TerminalRemoteHandshake ||
            normalized == SalrynHostCommandNames.TerminalRegistryDisable ||
            normalized == SalrynHostCommandNames.TerminalRegistryRevoke ||
            normalized == SalrynHostCommandNames.TerminalPairingIssue ||
            normalized == SalrynHostCommandNames.TerminalPairingRevoke ||
            normalized == SalrynHostCommandNames.OfficialCheckoutComplete ||
            normalized == SalrynHostCommandNames.OfficialLifecycleAppend ||
            normalized == SalrynHostCommandNames.OfficialReadingPreviewX ||
            normalized == SalrynHostCommandNames.OfficialReadingFinalizeZ ||
            normalized == SalrynHostCommandNames.BackupAuthorityProbe;
    }

    public static Result RequireRemoteSafeCategory(HostCommandCategory category)
    {
        return category switch
        {
            HostCommandCategory.StatusRead => Result.Success(),
            HostCommandCategory.ProductRead => Result.Success(),
            HostCommandCategory.PairingActivation => Result.Success(),
            HostCommandCategory.RemoteHandshake => Result.Success(),
            HostCommandCategory.TerminalPairingRead => Result.Fail(Error.Forbidden(
                "LanRemoteTransport.TerminalPairingReadRemoteBlocked",
                "Terminal pairing reads are blocked remotely in LAN-M6.")),
            HostCommandCategory.OfficialMutation => Result.Fail(Error.Forbidden(
                "LanRemoteTransport.OfficialCommandRemoteBlocked",
                "Official command execution is blocked remotely in LAN-M6.")),
            HostCommandCategory.AdminMutation => Result.Fail(Error.Forbidden(
                "LanRemoteTransport.AdminCommandRemoteBlocked",
                "Administrative command execution is blocked remotely in LAN-M6.")),
            HostCommandCategory.AdminRead => Result.Fail(Error.Forbidden(
                "LanRemoteTransport.AdminReadRemoteBlocked",
                "Administrative reads are blocked remotely in LAN-M6.")),
            HostCommandCategory.BackupMutation => Result.Fail(Error.Forbidden(
                "LanRemoteTransport.BackupCommandRemoteBlocked",
                "Backup command execution is blocked remotely in LAN-M6.")),
            HostCommandCategory.AuthorityProbe => Result.Fail(Error.Forbidden(
                "LanRemoteTransport.AuthorityProbeRemoteBlocked",
                "Authority probes are blocked remotely in LAN-M6.")),
            _ => Result.Fail(Error.Forbidden(
                "LanRemoteTransport.CommandCategoryBlocked",
                "Remote command category is blocked in LAN-M6."))
        };
    }
}
