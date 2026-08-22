namespace Salryn.Tests;

// LAN-M16 guard: the host-owned remote operator session model. This is a source-level guard so it
// runs under CoreSmoke without a database. It proves the session service is fail-closed, the token
// is opaque (only a hash is retained + fixed-time compared), the mandatory trust headers are used,
// and the preferred /api/remote-pos session endpoints exist and are separate from the read-only
// bridge. Cookie-only LAN trust must be rejected.
public static class LanM16HostOwnedRemoteOperatorSessionChecks
{
    private const string BaselineSha = "e477699dca27a21954a5ad9d388307fdb9ed08fd";

    public static void Run()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        string apiRoot = Path.Combine(projectRoot, "src", "Salryn.Api.Local", "RemotePos");
        string handoffRoot = Path.Combine(projectRoot, "00-HANDOFF");

        string sessionService = ReadRequiredFile(apiRoot, "RemotePosOperatorSessionService.cs");
        string authorization = ReadRequiredFile(apiRoot, "RemotePosApiAuthorization.cs");
        string contracts = ReadRequiredFile(apiRoot, "RemotePosContracts.cs");
        string runtimeGate = ReadRequiredFile(apiRoot, "RemotePosRuntimeGate.cs");
        string endpoints = ReadRequiredFile(apiRoot, "RemotePosApiEndpoints.cs");
        string doc = ReadRequiredFile(handoffRoot, "LAN_M16_HOST_OWNED_MULTI_TERMINAL_ROLE_RUNTIME.md");
        string currentState = ReadRequiredFile(handoffRoot, "CURRENT_STABLE_STATE_AND_NEXT_WORK.md");
        string coreSmoke = ReadRequiredFile(projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");
        string infraSmoke = ReadRequiredFile(projectRoot, "tools", "Salryn.Tools.InfrastructureSmoke", "Program.cs");

        AssertOpaqueToken(sessionService);
        AssertSessionBinding(sessionService);
        AssertFailClosedTrust(sessionService);
        AssertFailClosedSessionStates(sessionService);
        AssertTrustHeadersAndCookieRejection(contracts, authorization);
        AssertRuntimeGateFailSafe(runtimeGate);
        AssertPreferredEndpoints(endpoints);
        AssertWiring(doc, currentState, coreSmoke, infraSmoke);
    }

    private static void AssertOpaqueToken(string sessionService)
    {
        // The session must retain only a hash of the secret, compared in fixed time. The raw token
        // is derived from a cryptographically random secret and never stored on the session object.
        AssertContains(sessionService, "byte[] TokenHash", "Operator session must retain only a token hash, never the raw token.");
        AssertContains(sessionService, "SHA256.HashData", "Operator session token must be hashed with SHA-256.");
        AssertContains(sessionService, "CryptographicOperations.FixedTimeEquals", "Operator session token must be compared in fixed time.");
        AssertContains(sessionService, "RandomNumberGenerator.GetBytes", "Operator session secret must be cryptographically random.");
        AssertNotContains(sessionService, "public required string Token ", "Operator session must not persist the raw token as a property.");
    }

    private static void AssertSessionBinding(string sessionService)
    {
        // The session binds terminal, handshake, host, store, user, role, permissions, issued/expiry.
        foreach (string field in new[]
        {
            "TerminalId", "HandshakeSessionId", "StoreId", "HostId", "UserId",
            "RoleName", "Permissions", "IssuedAtUtc", "ExpiresAtUtc", "RevokedAtUtc",
        })
        {
            AssertContains(sessionService, field, $"Operator session must bind {field}.");
        }

        AssertContains(sessionService, "SessionLifetime = TimeSpan.FromMinutes(30)", "Operator session must have an explicit 30-minute expiry.");
    }

    private static void AssertFailClosedTrust(string sessionService)
    {
        // ValidateTrustAsync (shared by login and every command) must fail closed for each condition.
        AssertContains(sessionService, "RemotePos.TerminalHandshakeRequired", "Trust must fail closed when terminal/handshake are missing.");
        AssertContains(sessionService, "RemotePos.RuntimeDisabled", "Trust must fail closed when the remote POS runtime is disabled.");
        AssertContains(sessionService, "RemotePos.RemoteTransportDisabled", "Trust must fail closed when LAN transport is disabled.");
        AssertContains(sessionService, "RemotePos.DeploymentIdentityRequired", "Trust must fail closed when deployment identity is not configured.");
        AssertContains(sessionService, "RemotePos.HandshakeInvalid", "Trust must fail closed when the handshake is invalid/expired.");
        AssertContains(sessionService, "RemotePos.TerminalNotAccepted", "Trust must fail closed when the terminal is not accepted.");
        AssertContains(sessionService, "ValidateTrustAsync", "Every remote POS command must re-validate trust.");
    }

    private static void AssertFailClosedSessionStates(string sessionService)
    {
        foreach (string state in new[]
        {
            "RemotePosDisabled", "TrustRejected", "AuthenticationFailed", "SessionInvalid",
            "SessionExpired", "SessionRevoked", "UserInactive", "IdentityMismatch",
        })
        {
            AssertContains(sessionService, state, $"Session lifecycle must model the {state} state.");
        }

        // Validation must check expiry, revocation, terminal/host identity, and active user.
        AssertContains(sessionService, "session.RevokedAtUtc is not null", "Validation must fail a revoked session.");
        AssertContains(sessionService, "DateTimeOffset.UtcNow >= session.ExpiresAtUtc", "Validation must fail an expired session.");
        AssertContains(sessionService, "UserStatus.Active", "Validation must require the user to still be active.");
        AssertContains(sessionService, "SessionTerminalMismatch", "Validation must reject a terminal/handshake mismatch.");
        AssertContains(sessionService, "SessionHostMismatch", "Validation must reject a host/store identity mismatch.");
    }

    private static void AssertTrustHeadersAndCookieRejection(string contracts, string authorization)
    {
        AssertContains(contracts, "\"X-Salryn-Remote-Pos-Session\"", "Operator session token must use the dedicated opaque header.");
        AssertContains(contracts, "RemoteReadOnlyBridgeHeaders.TerminalId", "Remote POS must reuse the terminal identity header.");
        AssertContains(contracts, "RemoteReadOnlyBridgeHeaders.HandshakeSessionId", "Remote POS must reuse the handshake proof header.");

        // Cookie-only LAN trust must be explicitly rejected in the command authorization.
        AssertContains(authorization, "Cookie-only trust is not accepted", "Authorization must reject cookie-only LAN trust.");
        AssertContains(authorization, "RequireRemoteBridge", "Authorization must require a header-bound remote bridge request.");
        AssertContains(authorization, "AuthorizeCommandAsync", "Authorization must validate the session on every command.");
    }

    private static void AssertRuntimeGateFailSafe(string runtimeGate)
    {
        AssertContains(runtimeGate, "private bool _enabled;", "Runtime gate must default to disabled (fail-safe).");
        AssertContains(runtimeGate, "public void Enable(", "Runtime gate must be explicitly enabled by the host operator.");
        AssertContains(runtimeGate, "public void Disable(", "Runtime gate must be disable-able by the host operator.");
        // The gate is an in-memory instance field with no persistence, so it resets on restart.
        AssertNotContains(runtimeGate, "connectionString", "Runtime gate must not persist enablement.");
    }

    private static void AssertPreferredEndpoints(string endpoints)
    {
        AssertContains(endpoints, "MapGroup(\"/api/remote-pos\")", "Remote POS endpoints must be under /api/remote-pos.");
        AssertContains(endpoints, "MapPost(\"/session/login\"", "Remote POS must expose POST /session/login.");
        AssertContains(endpoints, "MapPost(\"/session/logout\"", "Remote POS must expose POST /session/logout.");
        AssertContains(endpoints, "MapGet(\"/session\"", "Remote POS must expose GET /session.");
        AssertContains(endpoints, "MapGet(\"/capabilities\"", "Remote POS must expose GET /capabilities.");
        AssertNotContains(endpoints, "MapGroup(\"/api/remote-readonly\")", "Remote POS endpoints must not reuse the read-only route group.");
    }

    private static void AssertWiring(string doc, string currentState, string coreSmoke, string infraSmoke)
    {
        AssertContains(doc, BaselineSha, "LAN-M16 doc must carry the stable Sandbox source stamp.");
        AssertContains(doc, "Remote Operator Session Model", "LAN-M16 doc must document the operator session model.");
        if (IsCurrentLanM16QaBState(currentState))
        {
            AssertContains(currentState, "Stable as of `e477699dca27a21954a5ad9d388307fdb9ed08fd`", "Current-state doc must preserve the current stable Sandbox SHA.");
            AssertContains(currentState, "LAN-M16-QA-B", "Current-state doc must mark LAN-M16-QA-B as the current candidate work.");
            AssertContains(currentState, "LAN-M16-QA-B adds a host-approved Remote POS pairing gate and remote role login boundary.", "Current-state doc must describe the LAN-M16-QA-B pairing gate boundary.");
            AssertContains(currentState, "Current live coordination authority:", "Current-state doc must keep live coordination authority visible.");
            AssertContains(currentState, "Historical M-series and ROLE docs remain context only and must not override current source truth.", "Current-state doc must keep older M-series docs demoted to historical context.");
        }
        else
        {
            AssertContains(currentState, "LAN-M16 is current host-owned multi-terminal role runtime candidate.", "Current-state doc must name LAN-M16 as current candidate.");
        }
        AssertContains(coreSmoke, "LanM16HostOwnedRemoteOperatorSessionChecks.Run();", "CoreSmoke must wire the LAN-M16 operator session guard.");
        AssertContains(infraSmoke, "remote-pos-operator-session", "InfrastructureSmoke must wire the remote-pos-operator-session command.");
    }

    private static bool IsCurrentLanM16QaBState(string currentState)
    {
        return currentState.Contains("LAN-M16-QA-B", StringComparison.Ordinal)
            && currentState.Contains("Historical M-series and ROLE docs remain context only and must not override current source truth.", StringComparison.Ordinal)
            && currentState.Contains("Current live coordination authority:", StringComparison.Ordinal);
    }

    private static string ReadRequiredFile(params string[] pathParts)
    {
        string path = Path.Combine(pathParts);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("Required source file missing: " + path);
        }

        return File.ReadAllText(path);
    }

    private static string FindProjectRoot(string startPath)
    {
        DirectoryInfo? directory = new(startPath);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Salryn.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find Salryn.sln from current directory.");
    }

    private static void AssertContains(string source, string expected, string message)
    {
        if (!source.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertNotContains(string source, string forbidden, string message)
    {
        if (source.Contains(forbidden, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }
}
