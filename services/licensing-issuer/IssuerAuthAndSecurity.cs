using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;

namespace Salryn.LicensingIssuer.Server;

public sealed record IssuerActiveSession(
    string SessionToken,
    string CsrfToken,
    IssuerAdminUser User,
    DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Server-side opaque session store with per-session CSRF token and sliding/absolute expiration.
/// </summary>
public sealed class IssuerSessionManager
{
    private readonly ConcurrentDictionary<string, IssuerActiveSession> _sessions = new(StringComparer.Ordinal);
    private readonly TimeSpan _ttl;

    public IssuerSessionManager(TimeSpan? ttl = null)
    {
        _ttl = ttl ?? TimeSpan.FromHours(8);
    }

    public IssuerActiveSession Create(IssuerAdminUser user)
    {
        PruneExpired();
        string sessionToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        string csrfToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var session = new IssuerActiveSession(sessionToken, csrfToken, user, DateTimeOffset.UtcNow.Add(_ttl));
        _sessions[sessionToken] = session;
        return session;
    }

    public IssuerActiveSession? TryGet(string? sessionToken)
    {
        if (string.IsNullOrWhiteSpace(sessionToken))
        {
            return null;
        }
        if (!_sessions.TryGetValue(sessionToken, out IssuerActiveSession? session))
        {
            return null;
        }
        if (session.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            _sessions.TryRemove(sessionToken, out _);
            return null;
        }
        return session;
    }

    public void Revoke(string? sessionToken)
    {
        if (!string.IsNullOrWhiteSpace(sessionToken))
        {
            _sessions.TryRemove(sessionToken, out _);
        }
    }

    public static bool ValidateCsrf(IssuerActiveSession session, HttpRequest request)
    {
        if (!request.Headers.TryGetValue(IssuerConstants.CsrfHeaderName, out var values))
        {
            return false;
        }
        string provided = values.ToString().Trim();
        if (provided.Length != session.CsrfToken.Length)
        {
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(provided),
            System.Text.Encoding.UTF8.GetBytes(session.CsrfToken));
    }

    private void PruneExpired()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (var kvp in _sessions)
        {
            if (kvp.Value.ExpiresAtUtc <= now)
            {
                _sessions.TryRemove(kvp.Key, out _);
            }
        }
    }
}

/// <summary>
/// Simple bounded sliding-window login rate limiter keyed by client IP + normalized username.
/// </summary>
public sealed class LoginRateLimiter
{
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _attempts = new(StringComparer.Ordinal);
    private readonly int _maxAttempts;
    private readonly TimeSpan _window;

    public LoginRateLimiter(int maxAttempts = 8, TimeSpan? window = null)
    {
        _maxAttempts = Math.Max(1, maxAttempts);
        _window = window ?? TimeSpan.FromMinutes(5);
    }

    public bool IsRateLimited(string key)
    {
        DateTimeOffset cutoff = DateTimeOffset.UtcNow - _window;
        var q = _attempts.GetOrAdd(key, _ => new Queue<DateTimeOffset>());
        lock (q)
        {
            while (q.Count > 0 && q.Peek() < cutoff)
            {
                q.Dequeue();
            }
            return q.Count >= _maxAttempts;
        }
    }

    public void RecordFailure(string key)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset cutoff = now - _window;
        var q = _attempts.GetOrAdd(key, _ => new Queue<DateTimeOffset>());
        lock (q)
        {
            while (q.Count > 0 && q.Peek() < cutoff)
            {
                q.Dequeue();
            }
            q.Enqueue(now);
        }
    }

    public void Reset(string key) => _attempts.TryRemove(key, out _);
}
