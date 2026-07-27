using System.Collections.Concurrent;
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
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);

    private readonly ConcurrentDictionary<string, StoredSession> _sessions = new(StringComparer.Ordinal);

    public LocalApiSession? Get(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        if (!_sessions.TryGetValue(sessionId, out StoredSession? stored))
        {
            return null;
        }

        if (stored.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _sessions.TryRemove(sessionId, out _);
            return null;
        }

        return stored.Session;
    }

    public LocalApiSession Create(AuthenticatedUserReadModel user)
    {
        ArgumentNullException.ThrowIfNull(user);

        string sessionId = Guid.NewGuid().ToString("N");
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        LocalApiSession session = new(
            sessionId,
            user.UserId,
            user.Username,
            user.DisplayName,
            user.RoleName,
            user.RoleId,
            user.Status,
            user.Permissions,
            startedAt);

        _sessions[sessionId] = new StoredSession(session, startedAt.Add(SessionLifetime));
        return session;
    }

    public bool TryRemove(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        return _sessions.TryRemove(sessionId, out _);
    }

    private sealed record StoredSession(LocalApiSession Session, DateTimeOffset ExpiresAt);
}
