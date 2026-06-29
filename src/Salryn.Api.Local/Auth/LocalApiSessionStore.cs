using Salryn.Core.Models;
using Salryn.Core.Security;

namespace Salryn.Api.Local.Auth;

public sealed record LocalApiSession(
    string SessionId,
    Guid UserId,
    string Username,
    string DisplayName,
    string RoleName,
    Guid RoleId,
    UserStatus Status,
    IReadOnlyList<string> Permissions,
    DateTimeOffset StartedAt);

public interface ILocalApiSessionStore
{
    LocalApiSession? Get(string sessionId);

    LocalApiSession Create(AuthenticatedUserReadModel user);

    bool TryRemove(string sessionId);
}

public sealed class InMemoryLocalApiSessionStore : ILocalApiSessionStore
{
    private readonly Dictionary<string, LocalApiSession> _sessions = new(StringComparer.Ordinal);

    public LocalApiSession? Get(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        return _sessions.TryGetValue(sessionId, out LocalApiSession? session)
            ? session
            : null;
    }

    public LocalApiSession Create(AuthenticatedUserReadModel user)
    {
        ArgumentNullException.ThrowIfNull(user);

        string sessionId = Guid.NewGuid().ToString("N");
        LocalApiSession session = new(
            sessionId,
            user.UserId,
            user.Username,
            user.DisplayName,
            user.RoleName,
            user.RoleId,
            user.Status,
            user.Permissions,
            DateTimeOffset.UtcNow);

        _sessions[sessionId] = session;
        return session;
    }

    public bool TryRemove(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        return _sessions.Remove(sessionId);
    }
}
