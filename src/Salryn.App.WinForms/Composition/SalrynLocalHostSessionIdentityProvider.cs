using Salryn.App.WinForms.Features.Shell;

namespace Salryn.App.WinForms.Composition;

// Kirch: Passive app session identity bridge for Local Host requests; it observes the current shell session only and must not start runtime.
public sealed class SalrynLocalHostSessionIdentityProvider
{
    private CurrentAppSession? currentSession;

    public CurrentAppSession? CurrentSession => currentSession;

    public void SetSession(CurrentAppSession? session)
    {
        currentSession = session;
    }

    public bool HasAuthenticatedIdentity
    {
        get
        {
            CurrentAppSession? session = currentSession;

            return session is not null &&
                session.IsActive &&
                session.UserId != Guid.Empty &&
                session.RoleId != Guid.Empty;
        }
    }

    public bool TryGetAuthenticatedIdentity(out Guid userId, out Guid roleId)
    {
        userId = Guid.Empty;
        roleId = Guid.Empty;

        CurrentAppSession? session = currentSession;

        if (session is null ||
            !session.IsActive ||
            session.UserId == Guid.Empty ||
            session.RoleId == Guid.Empty)
        {
            return false;
        }

        userId = session.UserId;
        roleId = session.RoleId;

        return true;
    }
}