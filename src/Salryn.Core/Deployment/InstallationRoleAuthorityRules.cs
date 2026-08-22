namespace Salryn.Core.Deployment;

public static class InstallationRoleAuthorityRules
{
    public static MachineAuthority ResolveAuthority(InstallationRole role)
    {
        return role switch
        {
            InstallationRole.HostServer => new MachineAuthority(
                IsDatabaseAuthority: true,
                IsOfficialSequenceAuthority: true,
                IsJournalAuthority: true,
                IsBackupAuthority: true,
                IsClientOnly: false,
                LanTransportEnabled: false),
            InstallationRole.AllInOne => new MachineAuthority(
                IsDatabaseAuthority: true,
                IsOfficialSequenceAuthority: true,
                IsJournalAuthority: true,
                IsBackupAuthority: true,
                IsClientOnly: false,
                LanTransportEnabled: false),
            InstallationRole.ClientTerminal => new MachineAuthority(
                IsDatabaseAuthority: false,
                IsOfficialSequenceAuthority: false,
                IsJournalAuthority: false,
                IsBackupAuthority: false,
                IsClientOnly: true,
                LanTransportEnabled: false),
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unsupported installation role.")
        };
    }

    public static ClientHostConnectionStatus ResolveConnectionStatus(
        InstallationRole role,
        string? hostAddress)
    {
        if (role != InstallationRole.ClientTerminal)
        {
            return ClientHostConnectionStatus.NotConfigured;
        }

        if (string.IsNullOrWhiteSpace(hostAddress))
        {
            return ClientHostConnectionStatus.NotConfigured;
        }

        return ClientHostConnectionStatus.TransportNotImplemented;
    }

    public static bool RequiresHostAddress(InstallationRole role)
    {
        return role == InstallationRole.ClientTerminal;
    }
}
