using System.Net;
using Microsoft.AspNetCore.Http;
using Salryn.Api.Local.Bridge;
using Salryn.Api.Local.Composition;
using Salryn.Api.Local.RemotePos;
using Salryn.Composition.Local;
using Salryn.Core.Deployment;
using Salryn.Core.Host;
using Salryn.Core.Security;
using Salryn.Core.TerminalRegistry;

// LAN-M16-QA-A remote-pos-origin-binding smoke. Proves the remote POS runtime binds a session to
// the SERVER-OBSERVED connection origin at login and fails closed on every command whose observed
// origin differs. Two layers are exercised end-to-end against REAL host services:
//   1. Trusted-origin resolution (bridge + authorization): the observed origin is derived from the
//      connection remote IP, NEVER the spoofable X-Salryn-Remote-Origin header, and cookie-only
//      remote requests are rejected before any session is consulted.
//   2. Per-command enforcement (real login + ValidateAsync): a login binds the observed origin; a
//      command from the same origin validates, a command from a different origin fails closed with
//      RemotePos.OriginMismatch. No shared SQLite; the host owns the DB.
internal static class RemotePosOriginBindingSmokeChecks
{
    private const string SpoofedOriginHeader = "203.0.113.9";

    private static readonly string[] PairingActorPermissions =
    {
        SalrynPermissions.StoreTerminalView,
        SalrynPermissions.StoreTerminalManage,
        SalrynPermissions.StoreTerminalPairingIssue,
        SalrynPermissions.StoreTerminalPairingRevoke,
    };

    public static void Run(string projectRoot)
    {
        _ = projectRoot;

        Console.WriteLine("===== LAN-M16-QA-A REMOTE POS ORIGIN BINDING SMOKE CHECKS =====");

        RequireTrustedOriginIsServerObserved();
        RequireCookieOnlyRemoteRequestRejected();
        RequireEndToEndOriginEnforcement();

        Console.WriteLine("LAN-M16-QA-A remote POS origin binding smoke checks succeeded.");
        Console.WriteLine();
    }

    // The trusted origin is the server-observed connection remote IP. A spoofed client origin header
    // never becomes the observed origin, loopback canonicalizes to a stable token, IPv4-mapped IPv6
    // normalizes to IPv4, and distinct LAN peers produce distinct observed origins.
    private static void RequireTrustedOriginIsServerObserved()
    {
        // Loopback connection + a spoofed remote-origin header -> observed origin is "loopback".
        (RemoteReadOnlyBridgeRequestContext loopbackCtx, RemotePosTrustContext loopbackTrust) =
            ResolveWithSpoofedHeader(IPAddress.Loopback);
        RemotePosSmokeSupport.Require(
            loopbackCtx.ObservedOrigin == RemoteReadOnlyBridgeRequestContext.LoopbackObservedOrigin,
            "Loopback connection must canonicalize to the loopback observed-origin token.");
        RemotePosSmokeSupport.Require(
            loopbackTrust.ObservedOrigin == RemoteReadOnlyBridgeRequestContext.LoopbackObservedOrigin,
            "Trust context must bind the server-observed loopback origin.");
        RemotePosSmokeSupport.Require(
            loopbackTrust.ObservedOrigin != SpoofedOriginHeader,
            "Spoofed origin header must never become the trusted observed origin (loopback case).");
        // The client-supplied header is diagnostic only; it may still be captured in OriginAddress
        // but it is NOT the origin the session is bound to.
        RemotePosSmokeSupport.Require(
            loopbackCtx.ObservedOrigin != loopbackCtx.OriginAddress,
            "Observed origin (trusted) must be independent of the client-supplied origin address.");

        // LAN connection + a spoofed remote-origin header -> observed origin is the LAN IP.
        (_, RemotePosTrustContext lanTrust) = ResolveWithSpoofedHeader(IPAddress.Parse("192.168.1.50"));
        RemotePosSmokeSupport.Require(
            lanTrust.ObservedOrigin == "192.168.1.50",
            "LAN connection must bind the server-observed LAN IP as the trusted origin.");
        RemotePosSmokeSupport.Require(
            lanTrust.ObservedOrigin != SpoofedOriginHeader,
            "Spoofed origin header must never become the trusted observed origin (LAN case).");

        // IPv4-mapped IPv6 connection normalizes to the IPv4 form for a stable comparison key.
        (RemoteReadOnlyBridgeRequestContext mappedCtx, _) =
            ResolveWithSpoofedHeader(IPAddress.Parse("::ffff:192.168.1.50"));
        RemotePosSmokeSupport.Require(
            mappedCtx.ObservedOrigin == "192.168.1.50",
            "IPv4-mapped IPv6 connection must normalize to its IPv4 observed origin.");

        // A null (in-process) remote IP canonicalizes to loopback (local operator never falsely rejected).
        DefaultHttpContext nullPeer = BuildBridgeRequest(remoteIpAddress: null);
        RemoteReadOnlyBridgeRequestContext nullCtx = RemoteReadOnlyBridgeRequestContext.FromHttpContext(nullPeer);
        RemotePosSmokeSupport.Require(
            nullCtx.ObservedOrigin == RemoteReadOnlyBridgeRequestContext.LoopbackObservedOrigin,
            "A null remote IP must canonicalize to the loopback observed-origin token.");

        // Distinct LAN peers produce distinct observed origins, so a session bound to one peer can
        // never be replayed from another.
        (_, RemotePosTrustContext peerA) = ResolveWithSpoofedHeader(IPAddress.Parse("192.168.1.50"));
        (_, RemotePosTrustContext peerB) = ResolveWithSpoofedHeader(IPAddress.Parse("192.168.1.60"));
        RemotePosSmokeSupport.Require(
            peerA.ObservedOrigin != peerB.ObservedOrigin,
            "Distinct LAN peers must resolve to distinct observed origins.");
    }

    // Cookie-only remote requests (no terminal/handshake headers) are rejected fail-closed by the
    // remote POS bridge guard before any session or origin is consulted.
    private static void RequireCookieOnlyRemoteRequestRejected()
    {
        DefaultHttpContext cookieOnly = new();
        cookieOnly.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.51");
        cookieOnly.Request.Headers.Cookie = "salryn.sid=operator-session-only";

        IResult? rejected = RemotePosApiAuthorization.RequireRemoteBridge(cookieOnly);
        RemotePosSmokeSupport.Require(
            rejected is not null,
            "Cookie-only remote POS request (no terminal/handshake headers) must be rejected fail-closed.");
    }

    // End-to-end: bootstrap an isolated host DB, pair + activate a terminal, handshake, then prove a
    // real login binds the observed origin and every command re-validates it.
    private static void RequireEndToEndOriginEnforcement()
    {
        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "salryn-remote-pos-origin-" + Guid.NewGuid().ToString("N"));

        try
        {
            SalrynLocalComposition composition = SalrynLocalComposition.CreateDevelopment(tempRoot);
            RemotePosSmokeSupport.RequireSuccess(
                composition.DatabaseInitialization.InitializeDatabase(), "OriginBinding.InitializeDatabase");

            const string adminUser = "origin-smoke-admin";
            const string adminPass = "OriginSmokePass123!";
            AuthenticatedUserReadModel admin = RemotePosSmokeSupport.RequireSuccess(
                composition.Authentication
                    .CreateFirstAdminAsync(new FirstRunAdminSetupRequest(adminUser, "Origin Smoke Admin", adminPass, adminPass))
                    .GetAwaiter().GetResult(),
                "OriginBinding.CreateFirstAdmin");

            LanRemoteTransportApiServices lan = LanRemoteTransportApiComposition.Create(composition);

            DeploymentIdentitySnapshot identity = lan.DeploymentIdentity.GetCurrentAsync().GetAwaiter().GetResult().Value
                ?? throw new InvalidOperationException("Deployment identity must exist after database initialization.");
            RemotePosSmokeSupport.Require(identity.IsConfigured, "Deployment identity must be configured for remote POS login.");

            TerminalRegistryActorContext actor = new(admin.UserId, PairingActorPermissions);

            RemotePosSmokeSupport.RequireSuccess(
                lan.Config.SaveAsync(new LanRemoteTransportConfigUpdateRequest(
                    RemoteTransportEnabled: true,
                    BindMode: LanRemoteTransportBindMode.ExplicitLanAddress,
                    BindAddress: "192.168.1.10",
                    Port: LanRemoteTransportConfig.DefaultPort,
                    RequirePairedTerminal: true,
                    RequireActiveTerminal: true,
                    RejectUnknownTerminals: true,
                    AllowOfficialCommandsRemotely: false,
                    AllowAdminCommandsRemotely: false,
                    UpdatedByUserId: admin.UserId)).GetAwaiter().GetResult(),
                "OriginBinding.EnableTransport");

            (Guid terminalId, Guid handshakeSessionId) = PairAndHandshake(lan, identity, actor);

            RemotePosRuntimeGate runtimeGate = new();
            RemotePosApiServices pos = RemotePosApiServices.Create(composition, lan, runtimeGate);
            runtimeGate.Enable(admin.UserId);

            RemotePosOperatorSessionService sessions = pos.SessionService;

            // 1. Login observed at loopback binds the loopback origin to the session.
            RemotePosOperatorSession loopbackSession = RequireLogin(
                sessions, terminalId, handshakeSessionId, RemoteReadOnlyBridgeRequestContext.LoopbackObservedOrigin,
                adminUser, adminPass, out string loopbackToken);
            RemotePosSmokeSupport.Require(
                loopbackSession.ObservedOrigin == RemoteReadOnlyBridgeRequestContext.LoopbackObservedOrigin,
                "Login must bind the session to the observed loopback origin.");

            // 2. A command from the SAME observed origin validates.
            RemotePosSessionValidation sameOrigin = sessions
                .ValidateAsync(
                    new RemotePosTrustContext(terminalId, handshakeSessionId, RemoteReadOnlyBridgeRequestContext.LoopbackObservedOrigin),
                    loopbackToken)
                .GetAwaiter().GetResult();
            RemotePosSmokeSupport.Require(sameOrigin.Ok, "A command from the matching observed origin must validate.");

            // 3. A command from a DIFFERENT observed origin fails closed with RemotePos.OriginMismatch.
            RemotePosSessionValidation differentOrigin = sessions
                .ValidateAsync(
                    new RemotePosTrustContext(terminalId, handshakeSessionId, "192.168.1.77"),
                    loopbackToken)
                .GetAwaiter().GetResult();
            RemotePosSmokeSupport.Require(!differentOrigin.Ok, "A command from a different observed origin must fail closed.");
            RemotePosSmokeSupport.Require(
                differentOrigin.State == RemotePosSessionState.OriginMismatch,
                $"Origin mismatch must report the OriginMismatch state, got {differentOrigin.State}.");
            RemotePosSmokeSupport.Require(
                differentOrigin.Code == "RemotePos.OriginMismatch",
                $"Origin mismatch must return the RemotePos.OriginMismatch code, got {differentOrigin.Code}.");
            RemotePosSmokeSupport.Require(
                differentOrigin.Session is null,
                "A rejected origin must never return a usable session.");

            // 4. A login observed at a LAN origin binds THAT origin; a loopback command is then rejected.
            RemotePosOperatorSession lanSession = RequireLogin(
                sessions, terminalId, handshakeSessionId, "192.168.1.50", adminUser, adminPass, out string lanToken);
            RemotePosSmokeSupport.Require(
                lanSession.ObservedOrigin == "192.168.1.50",
                "Login must bind the session to the observed LAN origin.");

            RemotePosSessionValidation lanMatch = sessions
                .ValidateAsync(new RemotePosTrustContext(terminalId, handshakeSessionId, "192.168.1.50"), lanToken)
                .GetAwaiter().GetResult();
            RemotePosSmokeSupport.Require(lanMatch.Ok, "A command from the matching LAN observed origin must validate.");

            RemotePosSessionValidation lanMismatch = sessions
                .ValidateAsync(
                    new RemotePosTrustContext(terminalId, handshakeSessionId, RemoteReadOnlyBridgeRequestContext.LoopbackObservedOrigin),
                    lanToken)
                .GetAwaiter().GetResult();
            RemotePosSmokeSupport.Require(
                !lanMismatch.Ok && lanMismatch.State == RemotePosSessionState.OriginMismatch,
                "A LAN-bound session must reject a loopback-origin command.");

            runtimeGate.Disable();
        }
        finally
        {
            CleanupTempRoot(tempRoot);
        }
    }

    private static (Guid TerminalId, Guid HandshakeSessionId) PairAndHandshake(
        LanRemoteTransportApiServices lan,
        DeploymentIdentitySnapshot identity,
        TerminalRegistryActorContext actor)
    {
        PairingInviteCreationResult invite = RemotePosSmokeSupport.RequireSuccess(
            lan.TerminalPairingPreparation
                .CreatePairingInviteAsync(
                    "LAN-M16 origin smoke client",
                    TerminalRegistrationRole.CashierClient,
                    actor,
                    TimeSpan.FromMinutes(30))
                .GetAwaiter().GetResult(),
            "OriginBinding.CreatePairingInvite");

        TerminalPairingActivationResult activation = RemotePosSmokeSupport.RequireSuccess(
            lan.TerminalPairingActivation
                .ActivatePairingAsync(new TerminalPairingActivationRequest(
                    invite.Invite.PairingId,
                    identity.Store.StoreId,
                    identity.Host.HostId,
                    "LAN-M16 origin smoke terminal",
                    TerminalRegistrationRole.CashierClient,
                    invite.PairingCode,
                    "origin-smoke-client",
                    "origin-smoke-fingerprint",
                    DateTimeOffset.UtcNow,
                    "192.168.1.50"))
                .GetAwaiter().GetResult(),
            "OriginBinding.ActivatePairing");

        RemotePosSmokeSupport.Require(activation.Succeeded, "Pairing activation must succeed.");
        RemotePosSmokeSupport.Require(activation.TerminalId.HasValue, "Activation must return a terminal id.");
        RemotePosSmokeSupport.Require(
            !string.IsNullOrWhiteSpace(activation.OneTimeTerminalCredential),
            "Activation must return a one-time terminal credential.");

        Guid terminalId = activation.TerminalId.GetValueOrDefault();
        string rawCredential = activation.OneTimeTerminalCredential!;

        RemoteClientHandshakeResult handshake = RemotePosSmokeSupport.RequireSuccess(
            lan.Handshake
                .HandshakeAsync(new RemoteClientHandshakeRequest(
                    terminalId,
                    identity.Store.StoreId,
                    identity.Host.HostId,
                    rawCredential,
                    "192.168.1.50",
                    DateTimeOffset.UtcNow,
                    "origin-smoke-client",
                    "origin-smoke-fingerprint"))
                .GetAwaiter().GetResult(),
            "OriginBinding.Handshake");

        RemotePosSmokeSupport.Require(
            handshake.Succeeded && handshake.HandshakeSessionId.HasValue,
            "Handshake with the active terminal credential must succeed.");

        return (terminalId, handshake.HandshakeSessionId.GetValueOrDefault());
    }

    private static RemotePosOperatorSession RequireLogin(
        RemotePosOperatorSessionService sessions,
        Guid terminalId,
        Guid handshakeSessionId,
        string observedOrigin,
        string username,
        string password,
        out string token)
    {
        RemotePosLoginOutcome login = sessions
            .LoginAsync(
                new RemotePosTrustContext(terminalId, handshakeSessionId, observedOrigin),
                new RemotePosLoginRequest(username, password))
            .GetAwaiter().GetResult();

        RemotePosSmokeSupport.Require(login.Ok, $"Remote POS login must succeed, got {login.State}: {login.Message}");
        RemotePosSmokeSupport.Require(login.Session is not null, "A successful login must return a session.");
        RemotePosSmokeSupport.Require(!string.IsNullOrWhiteSpace(login.Token), "A successful login must return a token.");

        token = login.Token!;
        return login.Session!;
    }

    private static (RemoteReadOnlyBridgeRequestContext Context, RemotePosTrustContext Trust) ResolveWithSpoofedHeader(
        IPAddress remoteIpAddress)
    {
        DefaultHttpContext httpContext = BuildBridgeRequest(remoteIpAddress);
        httpContext.Request.Headers[RemoteReadOnlyBridgeHeaders.RemoteOrigin] = SpoofedOriginHeader;

        RemoteReadOnlyBridgeRequestContext context = RemoteReadOnlyBridgeRequestContext.FromHttpContext(httpContext);
        RemotePosTrustContext trust = RemotePosApiAuthorization.BuildTrustContext(httpContext);
        return (context, trust);
    }

    private static DefaultHttpContext BuildBridgeRequest(IPAddress? remoteIpAddress)
    {
        DefaultHttpContext httpContext = new();
        httpContext.Connection.RemoteIpAddress = remoteIpAddress;
        httpContext.Request.Headers[RemoteReadOnlyBridgeHeaders.TerminalId] = Guid.NewGuid().ToString();
        httpContext.Request.Headers[RemoteReadOnlyBridgeHeaders.HandshakeSessionId] = Guid.NewGuid().ToString();
        return httpContext;
    }

    private static void CleanupTempRoot(string tempRoot)
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            if (!Directory.Exists(tempRoot))
            {
                return;
            }

            const int maxAttempts = 8;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                    Directory.Delete(tempRoot, recursive: true);
                    return;
                }
                catch (IOException) when (attempt < maxAttempts)
                {
                    Thread.Sleep(150);
                }
                catch (UnauthorizedAccessException) when (attempt < maxAttempts)
                {
                    Thread.Sleep(150);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: temp cleanup encountered issue (non-fatal): {ex.Message}");
        }
    }
}
