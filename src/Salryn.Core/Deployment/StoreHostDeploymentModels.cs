namespace Salryn.Core.Deployment;

// Kirch: Defines the physical role a machine can play in a SALRYN store setup.
public enum SalrynMachineRole
{
    StandaloneHost = 1,
    CashierHost = 2,
    AdminHost = 3,
    DedicatedHost = 4,
    CashierClient = 5,
    AdminClient = 6,
    MobileDashboardClient = 7
}

// Kirch: Defines the supported deployment shape without forcing one fixed hardware setup.
public enum SalrynDeploymentMode
{
    Standalone = 1,
    CashierPcAsHost = 2,
    AdminPcAsHost = 3,
    DedicatedStoreHost = 4
}

// Kirch: Captures the store and tenant identity needed before future multi-client support.
public sealed record StoreIdentity(
    Guid TenantId,
    Guid StoreId);

// Kirch: Captures the Store Host machine identity that owns the local SQLite database.
public sealed record StoreHostIdentity(
    Guid HostId,
    string MachineName,
    SalrynMachineRole Role);

// Kirch: Captures a registered terminal or device allowed to talk to the Store Host.
public sealed record TerminalIdentity(
    Guid TerminalId,
    string DeviceKey,
    string MachineName,
    SalrynMachineRole Role);

// Kirch: Describes how a client should reach the Store Host later without exposing SQLite directly.
public sealed record StoreHostEndpoint(
    string HostAddress,
    int Port,
    bool UsesLoopback);

// Kirch: Combines the selected deployment mode with store, host, and optional terminal identity.
public sealed record StoreDeploymentProfile(
    StoreIdentity Store,
    StoreHostIdentity Host,
    SalrynDeploymentMode Mode,
    StoreHostEndpoint? Endpoint,
    TerminalIdentity? Terminal);

// Kirch: Centralizes deployment-mode rules so future UI and Host code do not guess machine behavior.
public static class StoreDeploymentRules
{
    // Kirch: Host roles are the only roles allowed to own and open the local SQLite database.
    public static bool CanOwnDatabase(SalrynMachineRole role)
    {
        return role is SalrynMachineRole.StandaloneHost
            or SalrynMachineRole.CashierHost
            or SalrynMachineRole.AdminHost
            or SalrynMachineRole.DedicatedHost;
    }

    // Kirch: Client roles must use Host contracts instead of direct database access.
    public static bool MustUseHostConnection(SalrynMachineRole role)
    {
        return role is SalrynMachineRole.CashierClient
            or SalrynMachineRole.AdminClient
            or SalrynMachineRole.MobileDashboardClient;
    }

    // Kirch: Standalone mode keeps Host and UI on the same machine.
    public static bool IsStandalone(SalrynDeploymentMode mode)
    {
        return mode == SalrynDeploymentMode.Standalone;
    }

    // Kirch: LAN mode means at least one client can connect to a Store Host over the local network.
    public static bool IsLanMode(SalrynDeploymentMode mode)
    {
        return mode is SalrynDeploymentMode.CashierPcAsHost
            or SalrynDeploymentMode.AdminPcAsHost
            or SalrynDeploymentMode.DedicatedStoreHost;
    }
}
