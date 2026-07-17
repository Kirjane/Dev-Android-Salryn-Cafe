using Salryn.Composition.Local;
using Salryn.Core.Deployment;
using Salryn.Core.Security;
using Salryn.Core.TerminalRegistry;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Repositories.System;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

/// <summary>
/// Fully code-proven terminal lifecycle service behavior in isolated DB.
/// Host-local non-removable rule is UI-layer; proven via helper source + negative managed-role matrix.
/// </summary>
public static class PreICodeProvenTerminalLifecycleChecks
{
    public static void Run()
    {
        string root = PreICodeProvenCheckSupport.FindRoot();
        string lifecycle = PreICodeProvenCheckSupport.Read(root, "src", "Salryn.App.Web", "src", "utils", "terminalLifecycleUx.ts");
        AssertLifecycleHelperContracts(lifecycle);
        RunServiceLifecycleBehavior();
    }

    private static void AssertLifecycleHelperContracts(string lifecycle)
    {
        PreICodeProvenCheckSupport.RequireContains(
            lifecycle,
            "return status === \"Active\" && isManagedRemoteLanTerminal(role);",
            "canDisableTerminal: Active + managed remote only.");
        PreICodeProvenCheckSupport.RequireContains(
            lifecycle,
            "return status === \"Disabled\" && isManagedRemoteLanTerminal(role);",
            "canEnable/canRemove: Disabled + managed remote only.");
        PreICodeProvenCheckSupport.RequireContains(
            lifecycle,
            "role === \"CashierClient\" || role === \"FutureMobileDashboardReadOnly\"",
            "Managed remote roles are CashierClient and FutureMobileDashboardReadOnly.");
        PreICodeProvenCheckSupport.RequireContains(
            lifecycle,
            "role === \"HostConsole\" || role === \"AllInOneLocalTerminal\" || role === \"AdminClient\"",
            "Host-local roles include HostConsole / AllInOneLocalTerminal / AdminClient.");
        PreICodeProvenCheckSupport.RequireContains(
            lifecycle,
            "return \"Removed\"",
            "Revoked status displays as Removed.");
        PreICodeProvenCheckSupport.RequireContains(
            lifecycle,
            "return \"host-local\"",
            "Host-local terminals group separately from removable clients.");
    }

    private static void RunServiceLifecycleBehavior()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "salryn-code-proven-terminal-" + Guid.NewGuid().ToString("N"));
        try
        {
            DbPaths paths = DbPaths.CreateDevelopment(tempRoot);
            SalrynLocalComposition composition = SalrynLocalComposition.Create(paths);
            composition.InitializeDatabaseAsync().GetAwaiter().GetResult();

            SqliteTerminalRegistryRepository repository = new(composition.ConnectionFactory);
            DeploymentIdentityService deploymentIdentity = new(new SqliteDeploymentIdentityRepository(composition.ConnectionFactory));
            TerminalRegistryService registry = new(
                repository,
                deploymentIdentity,
                new SalrynStoreHostAuthorityGuard(() => deploymentIdentity.CurrentSnapshot));

            Guid actorId = Guid.NewGuid();
            TerminalRegistryActorContext manageActor = new(
                actorId,
                new[] { SalrynPermissions.StoreTerminalView, SalrynPermissions.StoreTerminalManage });
            TerminalRegistryActorContext viewOnlyActor = new(
                Guid.NewGuid(),
                new[] { SalrynPermissions.StoreTerminalView });

            Guid terminalId = Guid.NewGuid();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            SalrynTerminalRegistration active = new(
                terminalId,
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Code Proven Counter",
                TerminalRegistrationRole.CashierClient,
                TerminalRegistrationStatus.Active,
                now,
                now,
                null,
                null,
                actorId,
                "PRE-I code-proven lifecycle");

            PreICodeProvenCheckSupport.RequireTrue(
                repository.UpsertAsync(active).GetAwaiter().GetResult().IsSuccess,
                "Seed Active CashierClient terminal.");

            var denied = registry.DisableTerminalAsync(terminalId, viewOnlyActor).GetAwaiter().GetResult();
            PreICodeProvenCheckSupport.RequireTrue(
                denied.IsFailure,
                "Disable requires manage permission (view-only must fail).");

            var disabled = registry.DisableTerminalAsync(terminalId, manageActor).GetAwaiter().GetResult();
            PreICodeProvenCheckSupport.RequireTrue(
                disabled.IsSuccess && disabled.Value.Status == TerminalRegistrationStatus.Disabled,
                "Active terminal can be disabled.");
            PreICodeProvenCheckSupport.RequireTrue(
                disabled.Value.DisabledAtUtc is not null,
                "Disable sets DisabledAtUtc.");

            var enabled = registry.EnableTerminalAsync(terminalId, manageActor).GetAwaiter().GetResult();
            PreICodeProvenCheckSupport.RequireTrue(
                enabled.IsSuccess && enabled.Value.Status == TerminalRegistrationStatus.Active,
                "Disabled terminal can be enabled.");
            PreICodeProvenCheckSupport.RequireTrue(
                enabled.Value.DisabledAtUtc is null,
                "Enable clears DisabledAtUtc.");

            var enableActive = registry.EnableTerminalAsync(terminalId, manageActor).GetAwaiter().GetResult();
            PreICodeProvenCheckSupport.RequireTrue(
                enableActive.IsFailure && enableActive.ErrorDetail.Code == "TerminalRegistry.EnableRequiresDisabled",
                "Active terminal cannot be enabled again.");

            PreICodeProvenCheckSupport.RequireTrue(
                registry.DisableTerminalAsync(terminalId, manageActor).GetAwaiter().GetResult().IsSuccess,
                "Disable again before remove.");

            var removed = registry.RevokeTerminalAsync(terminalId, manageActor).GetAwaiter().GetResult();
            PreICodeProvenCheckSupport.RequireTrue(
                removed.IsSuccess && removed.Value.Status == TerminalRegistrationStatus.Revoked,
                "Disabled terminal can be removed (revoke semantics).");
            PreICodeProvenCheckSupport.RequireTrue(
                removed.Value.RevokedAtUtc is not null,
                "Remove/revoke sets RevokedAtUtc.");

            var enableRemoved = registry.EnableTerminalAsync(terminalId, manageActor).GetAwaiter().GetResult();
            PreICodeProvenCheckSupport.RequireTrue(
                enableRemoved.IsFailure && enableRemoved.ErrorDetail.Code == "TerminalRegistry.CannotEnableRevoked",
                "Removed/revoked terminal cannot be enabled.");

            // Host-local registration exists but UI helpers prevent disable/remove; service uses permission only.
            Guid hostLocalId = Guid.NewGuid();
            SalrynTerminalRegistration hostLocal = active with
            {
                TerminalId = hostLocalId,
                TerminalName = "Host Console Row",
                TerminalRole = TerminalRegistrationRole.HostConsole,
                Status = TerminalRegistrationStatus.Active,
                DisabledAtUtc = null,
                RevokedAtUtc = null
            };
            PreICodeProvenCheckSupport.RequireTrue(
                repository.UpsertAsync(hostLocal).GetAwaiter().GetResult().IsSuccess,
                "Seed HostConsole terminal for host-local presence proof.");
            PreICodeProvenCheckSupport.RequireTrue(
                repository.GetByIdAsync(hostLocalId).GetAwaiter().GetResult().Value is not null,
                "Host-local terminal is present in registry without being a managed remote client.");
        }
        finally
        {
            SqliteConnectionFactory.ClearAllPools();
            try { Directory.Delete(tempRoot, recursive: true); } catch { /* best effort */ }
        }
    }
}
