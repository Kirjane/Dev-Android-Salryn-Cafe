using System.Collections.Concurrent;
using System.Security.Cryptography;
using Salryn.Api.Local.Composition;
using Salryn.Core.Common;
using Salryn.Core.Deployment;
using Salryn.Core.Host;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Core.TerminalRegistry;

namespace Salryn.Api.Local.RemotePos;

// LAN-M16: lifecycle/state labels for the remote operator session. Never carries secrets.
public static class RemotePosSessionState
{
    public const string Active = "Active";
    public const string RemotePosDisabled = "RemotePosDisabled";
    public const string TrustRejected = "TrustRejected";
    public const string AuthenticationFailed = "AuthenticationFailed";
    public const string RoleNotAllowed = "RoleNotAllowed";
    public const string SessionInvalid = "SessionInvalid";
    public const string SessionExpired = "SessionExpired";
    public const string SessionRevoked = "SessionRevoked";
    public const string UserInactive = "UserInactive";
    public const string IdentityMismatch = "IdentityMismatch";
    public const string OriginMismatch = "OriginMismatch";
    public const string CapacityExceeded = "CapacityExceeded";
}

/// <summary>
/// A host-issued remote operator session. The token secret is never stored or logged in the
/// clear: only a SHA-256 hash is retained for fixed-time comparison. The session binds the
/// terminal, handshake, host, store, user, role, permission snapshot, and the server-observed
/// request origin with an explicit expiry.
/// </summary>
public sealed class RemotePosOperatorSession
{
    public required Guid SessionId { get; init; }

    public required byte[] TokenHash { get; init; }

    public required Guid TerminalId { get; init; }

    public required Guid HandshakeSessionId { get; init; }

    // LAN-M16-QA-A: the server-observed connection origin captured at login. Every subsequent
    // command re-checks the current server-observed origin against this value and fails closed on
    // a mismatch. This is never a client-supplied value, so it cannot be spoofed by the client.
    public required string ObservedOrigin { get; init; }

    public required Guid StoreId { get; init; }

    public required Guid HostId { get; init; }

    public required Guid UserId { get; init; }

    public required string Username { get; init; }

    public required string DisplayName { get; init; }

    public required string RoleName { get; init; }

    public required Guid RoleId { get; init; }

    public required IReadOnlyList<string> Permissions { get; init; }

    public required DateTimeOffset IssuedAtUtc { get; init; }

    public required DateTimeOffset ExpiresAtUtc { get; init; }

    public DateTimeOffset? RevokedAtUtc { get; set; }
}

// LAN-M16-QA-A: ObservedOrigin is the trusted, server-observed connection origin (never a
// client-supplied header). It is bound to the remote operator session at login and revalidated on
// every command. Callers must supply the server-observed value (see RemotePosApiAuthorization).
public sealed record RemotePosTrustContext(Guid TerminalId, Guid HandshakeSessionId, string ObservedOrigin);

public sealed record RemotePosTrustResult(bool Ok, string State, string Code, string Message, Guid StoreId, Guid HostId);

public sealed record RemotePosLoginOutcome(
    bool Ok,
    string State,
    string Code,
    string Message,
    RemotePosOperatorSession? Session,
    string? Token);

public sealed record RemotePosSessionValidation(
    bool Ok,
    string State,
    string Code,
    string Message,
    RemotePosOperatorSession? Session);

public sealed class RemotePosOperatorSessionService
{
    // Kirch: why - a short-lived remote operator session limits blast radius if a token leaks. The
    // host owns issuance; sessions live in memory only and are cleared on host restart (fail-safe).
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(30);

    private static readonly string[] AllowedRemoteLoginRoles = { "Cashier", "Manager" };

    private readonly ConcurrentDictionary<Guid, RemotePosOperatorSession> _sessions = new();
    private readonly LanRemoteTransportApiServices _lanServices;
    private readonly RemotePosRuntimeGate _runtimeGate;
    private readonly IAuthenticationService _authentication;
    private readonly IUserReadRepository _userReadRepository;
    private readonly RemotePosCapacityPolicy _capacityPolicy;

    public RemotePosOperatorSessionService(
        LanRemoteTransportApiServices lanServices,
        RemotePosRuntimeGate runtimeGate,
        IAuthenticationService authentication,
        IUserReadRepository userReadRepository,
        RemotePosCapacityPolicy capacityPolicy)
    {
        _lanServices = lanServices ?? throw new ArgumentNullException(nameof(lanServices));
        _runtimeGate = runtimeGate ?? throw new ArgumentNullException(nameof(runtimeGate));
        _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        _userReadRepository = userReadRepository ?? throw new ArgumentNullException(nameof(userReadRepository));
        _capacityPolicy = capacityPolicy ?? throw new ArgumentNullException(nameof(capacityPolicy));
    }

    public bool RemotePosRuntimeEnabled => _runtimeGate.IsEnabled;

    /// <summary>
    /// Fail-closed trust validation shared by login and every remote POS command. Requires the
    /// read-only transport to be explicitly enabled, the remote POS runtime to be explicitly
    /// enabled by the host operator, a configured deployment identity, a valid handshake session,
    /// and an accepted (Active) terminal. Cookie-only trust is never accepted here.
    /// </summary>
    public async Task<RemotePosTrustResult> ValidateTrustAsync(
        RemotePosTrustContext trust,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trust);

        if (trust.TerminalId == Guid.Empty || trust.HandshakeSessionId == Guid.Empty)
        {
            return Trust(false, RemotePosSessionState.TrustRejected, "RemotePos.TerminalHandshakeRequired",
                "Remote POS requires a valid terminal id and handshake session id.");
        }

        if (!_runtimeGate.IsEnabled)
        {
            return Trust(false, RemotePosSessionState.RemotePosDisabled, "RemotePos.RuntimeDisabled",
                "Remote POS runtime is disabled. Enable it locally on the POS host.");
        }

        Result<LanRemoteTransportConfig> configResult = await _lanServices.Config
            .GetCurrentAsync(cancellationToken)
            .ConfigureAwait(false);

        if (configResult.IsFailure || !configResult.Value.RemoteTransportEnabled)
        {
            return Trust(false, RemotePosSessionState.RemotePosDisabled, "RemotePos.RemoteTransportDisabled",
                "Remote transport is disabled. Enable explicit LAN transport on the POS host.");
        }

        Result<DeploymentIdentitySnapshot?> identityResult = await _lanServices.DeploymentIdentity
            .GetCurrentAsync(cancellationToken)
            .ConfigureAwait(false);

        if (identityResult.IsFailure || identityResult.Value is null || !identityResult.Value.IsConfigured)
        {
            return Trust(false, RemotePosSessionState.TrustRejected, "RemotePos.DeploymentIdentityRequired",
                "Deployment identity must be configured before remote POS commands.");
        }

        DeploymentIdentitySnapshot identity = identityResult.Value;

        TerminalRegistryActorContext terminalActor = new(
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            new[] { SalrynPermissions.StoreTerminalView });
        Result<SalrynTerminalRegistration?> terminalResult = await _lanServices.TerminalRegistry
            .GetTerminalAsync(trust.TerminalId, terminalActor, cancellationToken)
            .ConfigureAwait(false);
        if (terminalResult.IsFailure
            || terminalResult.Value is null
            || !TerminalLicenseTrustMetadata.TryRead(terminalResult.Value.Notes, out _, out _, out _, out DateTimeOffset? terminalLicenseExpiresAt))
        {
            return Trust(false, RemotePosSessionState.TrustRejected, "RemotePos.TerminalLicenseProofRequired",
                "This terminal is not backed by a verified Terminal license. Re-pair it from Terminal Management.");
        }

        if (terminalLicenseExpiresAt.HasValue && terminalLicenseExpiresAt.Value <= DateTimeOffset.UtcNow)
        {
            return Trust(false, RemotePosSessionState.TrustRejected, "RemotePos.TerminalLicenseExpired",
                "This Terminal license has expired. Install a renewed Terminal license and re-pair this device.");
        }

        Result handshakeValidation = await _lanServices.Handshake
            .ValidateHandshakeSessionAsync(trust.TerminalId, trust.HandshakeSessionId, cancellationToken)
            .ConfigureAwait(false);

        if (handshakeValidation.IsFailure)
        {
            return Trust(false, RemotePosSessionState.TrustRejected, "RemotePos.HandshakeInvalid",
                "Remote POS handshake session is invalid or expired. Re-pair this terminal on the host.");
        }

        Result terminalAccepted = await _lanServices.TerminalRegistry
            .IsTerminalAcceptedForHostCommandsAsync(trust.TerminalId, cancellationToken)
            .ConfigureAwait(false);

        if (terminalAccepted.IsFailure)
        {
            return Trust(false, RemotePosSessionState.TrustRejected, "RemotePos.TerminalNotAccepted",
                "Remote POS terminal is not active/accepted for host commands.");
        }

        return new RemotePosTrustResult(true, RemotePosSessionState.Active, "RemotePos.TrustOk", "Trusted.",
            identity.Store.StoreId, identity.Host.HostId);
    }

    public async Task<RemotePosLoginOutcome> LoginAsync(
        RemotePosTrustContext trust,
        RemotePosLoginRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        RemotePosTrustResult trustResult = await ValidateTrustAsync(trust, cancellationToken).ConfigureAwait(false);

        if (!trustResult.Ok)
        {
            return new RemotePosLoginOutcome(false, trustResult.State, trustResult.Code, trustResult.Message, null, null);
        }

        Result<AuthenticatedUserReadModel> loginResult = await _authentication
            .LoginAsync(new LoginRequest(request.Username, request.Password), cancellationToken)
            .ConfigureAwait(false);

        if (loginResult.IsFailure)
        {
            // Kirch: why - do not leak whether the username exists; a single generic state is returned.
            return new RemotePosLoginOutcome(false, RemotePosSessionState.AuthenticationFailed,
                "RemotePos.AuthenticationFailed", "Remote POS login was rejected.", null, null);
        }

        AuthenticatedUserReadModel user = loginResult.Value;
        if (!IsRemotePosLoginRoleAllowed(user.RoleName))
        {
            return new RemotePosLoginOutcome(false, RemotePosSessionState.RoleNotAllowed,
                "RemotePos.RoleNotAllowed",
                "Remote POS login is limited to Cashier or Manager accounts. Admin and Owner accounts are host-local only for this build.",
                null,
                null);
        }

        int activeSessions = GetActiveSessionCount();
        if (activeSessions >= _capacityPolicy.MaxActiveSessions)
        {
            return new RemotePosLoginOutcome(false, RemotePosSessionState.CapacityExceeded,
                "RemotePos.ActiveSessionLimitReached",
                $"Remote POS active session capacity is full ({activeSessions}/{_capacityPolicy.MaxActiveSessions}). Sign out another remote POS session or raise the host policy within the hard cap.",
                null,
                null);
        }

        (RemotePosOperatorSession session, string token) = IssueSession(trust, trustResult, user);

        _sessions[session.SessionId] = session;

        return new RemotePosLoginOutcome(true, RemotePosSessionState.Active, "RemotePos.LoginOk",
            "Remote operator session issued.", session, token);
    }

    public async Task<RemotePosSessionValidation> ValidateAsync(
        RemotePosTrustContext trust,
        string? sessionToken,
        CancellationToken cancellationToken = default)
    {
        RemotePosTrustResult trustResult = await ValidateTrustAsync(trust, cancellationToken).ConfigureAwait(false);

        if (!trustResult.Ok)
        {
            return new RemotePosSessionValidation(false, trustResult.State, trustResult.Code, trustResult.Message, null);
        }

        if (!TryParseToken(sessionToken, out Guid sessionId, out byte[] secretBytes))
        {
            return Invalid(RemotePosSessionState.SessionInvalid, "RemotePos.SessionTokenMalformed",
                "Remote operator session token is missing or malformed.");
        }

        if (!_sessions.TryGetValue(sessionId, out RemotePosOperatorSession? session))
        {
            return Invalid(RemotePosSessionState.SessionInvalid, "RemotePos.SessionNotFound",
                "Remote operator session was not found. Log in again on the host.");
        }

        byte[] presentedHash = SHA256.HashData(secretBytes);

        if (!CryptographicOperations.FixedTimeEquals(presentedHash, session.TokenHash))
        {
            return Invalid(RemotePosSessionState.SessionInvalid, "RemotePos.SessionTokenMismatch",
                "Remote operator session token is invalid.");
        }

        if (session.RevokedAtUtc is not null)
        {
            return Invalid(RemotePosSessionState.SessionRevoked, "RemotePos.SessionRevoked",
                "Remote operator session has been revoked.");
        }

        if (DateTimeOffset.UtcNow >= session.ExpiresAtUtc)
        {
            return Invalid(RemotePosSessionState.SessionExpired, "RemotePos.SessionExpired",
                "Remote operator session has expired. Log in again on the host.");
        }

        if (session.TerminalId != trust.TerminalId || session.HandshakeSessionId != trust.HandshakeSessionId)
        {
            return Invalid(RemotePosSessionState.IdentityMismatch, "RemotePos.SessionTerminalMismatch",
                "Remote operator session does not match the presented terminal/handshake.");
        }

        if (session.StoreId != trustResult.StoreId || session.HostId != trustResult.HostId)
        {
            return Invalid(RemotePosSessionState.IdentityMismatch, "RemotePos.SessionHostMismatch",
                "Remote operator session host/store identity mismatch.");
        }

        // Kirch: why - the session is bound to the server-observed origin captured at login. If the
        // current request arrives from a different observed origin, fail closed: a leaked/replayed
        // token from another machine cannot be used, and a client-supplied origin header can never
        // satisfy this check because both values are server-observed only.
        if (!string.Equals(session.ObservedOrigin, trust.ObservedOrigin ?? string.Empty, StringComparison.Ordinal))
        {
            return Invalid(RemotePosSessionState.OriginMismatch, "RemotePos.OriginMismatch",
                "Remote POS session origin does not match the current request.");
        }

        Result<UserReadModel?> userResult = await _userReadRepository
            .FindByIdAsync(session.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (userResult.IsFailure || userResult.Value is null || userResult.Value.Status != UserStatus.Active)
        {
            return Invalid(RemotePosSessionState.UserInactive, "RemotePos.UserInactive",
                "Remote operator user is no longer active.");
        }

        return new RemotePosSessionValidation(true, RemotePosSessionState.Active, "RemotePos.SessionOk",
            "Session valid.", session);
    }

    public bool Revoke(Guid sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out RemotePosOperatorSession? session))
        {
            session.RevokedAtUtc = DateTimeOffset.UtcNow;
            return true;
        }

        return false;
    }

    public void RevokeAll()
    {
        foreach (RemotePosOperatorSession session in _sessions.Values)
        {
            session.RevokedAtUtc ??= DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// HOST-LAN-CONTROL-M4: invalidate in-memory remote POS operator sessions for one terminal.
    /// Does not change terminal registry trust; pairing remains until disable/revoke.
    /// </summary>
    public int RevokeSessionsForTerminal(Guid terminalId)
    {
        if (terminalId == Guid.Empty)
        {
            return 0;
        }

        int count = 0;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (RemotePosOperatorSession session in _sessions.Values)
        {
            if (session.TerminalId != terminalId || session.RevokedAtUtc is not null)
            {
                continue;
            }

            session.RevokedAtUtc = now;
            count++;
        }

        return count;
    }

    public int GetActiveSessionCount()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return _sessions.Values.Count(session =>
            session.RevokedAtUtc is null
            && session.ExpiresAtUtc > now);
    }

    public static bool IsRemotePosLoginRoleAllowed(string? roleName) =>
        !string.IsNullOrWhiteSpace(roleName)
        && AllowedRemoteLoginRoles.Contains(roleName.Trim(), StringComparer.OrdinalIgnoreCase);

    private (RemotePosOperatorSession Session, string Token) IssueSession(
        RemotePosTrustContext trust,
        RemotePosTrustResult trustResult,
        AuthenticatedUserReadModel user)
    {
        Guid sessionId = Guid.NewGuid();
        byte[] secretBytes = RandomNumberGenerator.GetBytes(32);
        byte[] tokenHash = SHA256.HashData(secretBytes);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        RemotePosOperatorSession session = new()
        {
            SessionId = sessionId,
            TokenHash = tokenHash,
            TerminalId = trust.TerminalId,
            HandshakeSessionId = trust.HandshakeSessionId,
            ObservedOrigin = trust.ObservedOrigin ?? string.Empty,
            StoreId = trustResult.StoreId,
            HostId = trustResult.HostId,
            UserId = user.UserId,
            Username = user.Username,
            DisplayName = user.DisplayName,
            RoleName = user.RoleName,
            RoleId = user.RoleId,
            Permissions = user.Permissions.ToArray(),
            IssuedAtUtc = now,
            ExpiresAtUtc = now.Add(SessionLifetime),
            RevokedAtUtc = null,
        };

        string token = sessionId.ToString("N") + "." + Base64UrlEncode(secretBytes);
        return (session, token);
    }

    private static bool TryParseToken(string? token, out Guid sessionId, out byte[] secretBytes)
    {
        sessionId = Guid.Empty;
        secretBytes = Array.Empty<byte>();

        if (string.IsNullOrWhiteSpace(token) || token.Length > 512)
        {
            return false;
        }

        int separator = token.IndexOf('.');
        if (separator <= 0 || separator >= token.Length - 1)
        {
            return false;
        }

        string sessionPart = token[..separator];
        string secretPart = token[(separator + 1)..];

        if (!Guid.TryParseExact(sessionPart, "N", out sessionId))
        {
            return false;
        }

        try
        {
            secretBytes = Base64UrlDecode(secretPart);
        }
        catch (FormatException)
        {
            return false;
        }

        return secretBytes.Length > 0;
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        string padded = value.Replace('-', '+').Replace('_', '/');
        int remainder = padded.Length % 4;
        if (remainder == 2)
        {
            padded += "==";
        }
        else if (remainder == 3)
        {
            padded += "=";
        }
        else if (remainder == 1)
        {
            throw new FormatException("Invalid base64url length.");
        }

        return Convert.FromBase64String(padded);
    }

    private static RemotePosTrustResult Trust(bool ok, string state, string code, string message) =>
        new(ok, state, code, message, Guid.Empty, Guid.Empty);

    private static RemotePosSessionValidation Invalid(string state, string code, string message) =>
        new(false, state, code, message, null);
}
