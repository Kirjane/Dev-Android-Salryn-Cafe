namespace Salryn.Core.Deployment;

public enum InstallationRole
{
    HostServer = 1,
    AllInOne = 2,
    ClientTerminal = 3
}

public enum ClientHostConnectionStatus
{
    NotConfigured = 1,
    TransportNotImplemented = 2,
    ConfiguredButOffline = 3
}

public sealed record MachineAuthority(
    bool IsDatabaseAuthority,
    bool IsOfficialSequenceAuthority,
    bool IsJournalAuthority,
    bool IsBackupAuthority,
    bool IsClientOnly,
    bool LanTransportEnabled);

public sealed record DeploymentStoreIdentity(
    Guid StoreId,
    string StoreName,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record DeploymentHostIdentity(
    Guid HostId,
    InstallationRole InstallationRole,
    string HostDisplayName,
    bool IsDatabaseAuthority,
    bool IsOfficialSequenceAuthority,
    bool IsJournalAuthority,
    bool IsBackupAuthority);

public sealed record DeploymentTerminalIdentity(
    Guid TerminalId,
    string TerminalName,
    InstallationRole TerminalRole,
    bool IsLocalTerminal,
    DateTimeOffset RegisteredAtUtc);

public sealed record ClientHostConnectionConfig(
    string? HostAddress,
    int? HostPort,
    string? PairingCredentialLabel,
    string? HostFingerprintPlaceholder,
    ClientHostConnectionStatus ConnectionStatus);

public sealed record DeploymentIdentitySnapshot(
    DeploymentStoreIdentity Store,
    DeploymentHostIdentity Host,
    DeploymentTerminalIdentity Terminal,
    ClientHostConnectionConfig ClientConnection,
    MachineAuthority Authority,
    bool IsConfigured,
    DateTimeOffset UpdatedAtUtc);

public sealed record HostServerConfigurationRequest(
    string StoreName,
    string HostDisplayName,
    string TerminalName);

public sealed record AllInOneConfigurationRequest(
    string StoreName,
    string HostDisplayName,
    string TerminalName);

public sealed record ClientTerminalConfigurationRequest(
    string StoreName,
    string TerminalName,
    string HostAddress,
    int HostPort,
    string PairingCredentialLabel);
