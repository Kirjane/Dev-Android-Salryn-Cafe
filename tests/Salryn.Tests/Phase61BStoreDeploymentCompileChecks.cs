using Salryn.Core.Deployment;

namespace Salryn.Tests;

// Kirch: Verifies the Phase 61B deployment identity model compiles and keeps Host/client rules explicit.
public static class Phase61BStoreDeploymentCompileChecks
{
    // Kirch: This compile check prevents future refactors from breaking the adaptive Store Host model.
    public static void Run()
    {
        StoreIdentity store = new(
            TenantId: Guid.NewGuid(),
            StoreId: Guid.NewGuid());

        StoreHostIdentity host = new(
            HostId: Guid.NewGuid(),
            MachineName: "SALRYN-HOST",
            Role: SalrynMachineRole.CashierHost);

        StoreHostEndpoint endpoint = new(
            HostAddress: "192.168.1.10",
            Port: 58161,
            UsesLoopback: false);

        TerminalIdentity terminal = new(
            TerminalId: Guid.NewGuid(),
            DeviceKey: "sample-device-key",
            MachineName: "SALRYN-CASHIER-02",
            Role: SalrynMachineRole.CashierClient);

        StoreDeploymentProfile profile = new(
            Store: store,
            Host: host,
            Mode: SalrynDeploymentMode.CashierPcAsHost,
            Endpoint: endpoint,
            Terminal: terminal);

        if (!StoreDeploymentRules.CanOwnDatabase(profile.Host.Role))
        {
            throw new InvalidOperationException("Host role should be allowed to own the database.");
        }

        if (profile.Terminal is null || !StoreDeploymentRules.MustUseHostConnection(profile.Terminal.Role))
        {
            throw new InvalidOperationException("Client role should be required to use Host connection.");
        }

        if (!StoreDeploymentRules.IsLanMode(profile.Mode))
        {
            throw new InvalidOperationException("Cashier-PC-as-Host should be treated as LAN mode.");
        }
    }
}
