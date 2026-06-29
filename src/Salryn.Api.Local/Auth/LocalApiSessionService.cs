using Salryn.Api.Local.Contracts;
using Salryn.Core.Common;
using Salryn.Core.Security;
using Salryn.Core.Services;

namespace Salryn.Api.Local.Auth;

public sealed class LocalApiSessionService
{
    public const string SessionCookieName = "salryn.sid";

    private readonly IAuthenticationService _authentication;
    private readonly ILocalApiSessionStore _sessionStore;

    public LocalApiSessionService(
        IAuthenticationService authentication,
        ILocalApiSessionStore sessionStore)
    {
        _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
    }

    public async Task<Result<LocalApiSession>> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        Result<AuthenticatedUserReadModel> loginResult =
            await _authentication.LoginAsync(request, cancellationToken).ConfigureAwait(false);

        if (loginResult.IsFailure)
        {
            return Result<LocalApiSession>.Fail(loginResult.ErrorDetail);
        }

        return Result<LocalApiSession>.Success(
            _sessionStore.Create(loginResult.Value));
    }

    public bool Logout(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        return _sessionStore.TryRemove(sessionId);
    }

    public SessionResponse GetSession(string? sessionId)
    {
        LocalApiSession? session = _sessionStore.Get(sessionId ?? string.Empty);

        if (session is null)
        {
            return new SessionResponse(
                IsAuthenticated: false,
                User: null,
                SessionStartedAt: null);
        }

        return ToSessionResponse(session);
    }

    public LocalApiSession? GetRequiredSession(string? sessionId)
    {
        return _sessionStore.Get(sessionId ?? string.Empty);
    }

    public static SessionResponse ToSessionResponse(LocalApiSession session)
    {
        return new SessionResponse(
            IsAuthenticated: true,
            User: new SessionUserResponse(
                session.UserId,
                session.Username,
                session.DisplayName,
                session.RoleName,
                session.RoleId,
                session.Status.ToString(),
                session.Permissions),
            SessionStartedAt: session.StartedAt);
    }
}
