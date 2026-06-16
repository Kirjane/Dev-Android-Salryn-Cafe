using Salryn.Core.Contracts;
using Salryn.Core.Deployment;

namespace Salryn.App.WinForms.Composition;

// Kirch: Passive Host request context provider; it can only build context from real supplied identity and must not invent runtime, terminal, user, or session state.
public sealed class SalrynLocalHostRequestContextProvider
{
    private readonly StoreDeploymentProfile localHostProfile;

    public SalrynLocalHostRequestContextProvider(StoreDeploymentProfile localHostProfile)
    {
        ArgumentNullException.ThrowIfNull(localHostProfile);

        this.localHostProfile = localHostProfile;
    }

    public StoreDeploymentProfile Profile => localHostProfile;

    public bool HasStoreIdentity =>
        localHostProfile.Store.TenantId != Guid.Empty &&
        localHostProfile.Store.StoreId != Guid.Empty;

    public bool HasTerminalIdentity =>
        localHostProfile.Terminal is not null &&
        localHostProfile.Terminal.TerminalId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(localHostProfile.Terminal.DeviceKey);

    public bool CanCreateAuthenticatedContext(Guid? userId, Guid? roleId)
    {
        return HasStoreIdentity &&
            HasTerminalIdentity &&
            userId.HasValue &&
            userId.Value != Guid.Empty &&
            roleId.HasValue &&
            roleId.Value != Guid.Empty;
    }

    public bool TryCreateAuthenticatedContext(
        Guid? userId,
        Guid? roleId,
        Guid? cashierSessionId,
        DateTimeOffset requestedAt,
        out HostRequestContext? context)
    {
        context = null;

        TerminalIdentity? terminal = localHostProfile.Terminal;

        if (terminal is null || !CanCreateAuthenticatedContext(userId, roleId))
        {
            return false;
        }

        context = new HostRequestContext(
            TenantId: localHostProfile.Store.TenantId,
            StoreId: localHostProfile.Store.StoreId,
            UserId: userId.GetValueOrDefault(),
            RoleId: roleId.GetValueOrDefault(),
            CashierSessionId: NormalizeOptionalIdentity(cashierSessionId),
            TerminalId: terminal.TerminalId,
            DeviceKey: terminal.DeviceKey,
            RequestedAt: requestedAt);

        return true;
    }

    private static Guid? NormalizeOptionalIdentity(Guid? value)
    {
        return value.HasValue && value.Value != Guid.Empty
            ? value.Value
            : null;
    }
}