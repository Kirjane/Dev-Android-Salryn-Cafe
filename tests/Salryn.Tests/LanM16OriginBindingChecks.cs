namespace Salryn.Tests;

// LAN-M16-QA-A guard: remote origin / session binding. This is a source-level guard (the functional
// proof lives in the remote-pos-origin-binding infrastructure smoke). It proves the trusted origin
// is the SERVER-OBSERVED connection origin (never the spoofable client X-Salryn-Remote-Origin
// header), that the remote operator session is bound to that observed origin at login, and that
// every command re-validates it and fails closed with RemotePos.OriginMismatch on a mismatch.
public static class LanM16OriginBindingChecks
{
    public static void Run()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        string apiRoot = Path.Combine(projectRoot, "src", "Salryn.Api.Local");
        string remotePosRoot = Path.Combine(apiRoot, "RemotePos");
        string handoffRoot = Path.Combine(projectRoot, "00-HANDOFF");

        string bridge = ReadRequiredFile(apiRoot, "Bridge", "RemoteReadOnlyBridgeRequestContext.cs");
        string authorization = ReadRequiredFile(remotePosRoot, "RemotePosApiAuthorization.cs");
        string sessionService = ReadRequiredFile(remotePosRoot, "RemotePosOperatorSessionService.cs");
        string doc = ReadRequiredFile(handoffRoot, "LAN_M16_HOST_OWNED_MULTI_TERMINAL_ROLE_RUNTIME.md");
        string coreSmoke = ReadRequiredFile(projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");
        string infraSmoke = ReadRequiredFile(projectRoot, "tools", "Salryn.Tools.InfrastructureSmoke", "Program.cs");

        AssertObservedOriginIsServerObserved(bridge);
        AssertAuthorizationBindsObservedOrigin(authorization);
        AssertSessionBindsAndEnforcesOrigin(sessionService);
        AssertWiring(doc, coreSmoke, infraSmoke);
    }

    private static void AssertObservedOriginIsServerObserved(string bridge)
    {
        AssertContains(bridge, "public string ObservedOrigin", "Bridge context must expose a server-observed origin.");
        AssertContains(bridge, "ResolveObservedOrigin(httpContext)", "Bridge context must resolve the observed origin from the request.");
        AssertContains(bridge, "httpContext.Connection.RemoteIpAddress", "Observed origin must be derived from the server-observed connection remote IP.");
        AssertContains(bridge, "IPAddress.IsLoopback", "Observed origin must canonicalize loopback so the local operator is never falsely rejected.");
        AssertContains(bridge, "LoopbackObservedOrigin", "Observed origin must use a stable loopback token.");

        // The observed-origin resolver must NOT consult the client-supplied origin header. Its
        // signature takes only the HttpContext, unlike ResolveOriginAddress(httpContext, originHeader).
        AssertContains(bridge, "ResolveObservedOrigin(HttpContext httpContext)",
            "Observed origin resolution must take only the HttpContext (no client-supplied header).");
        int observedResolver = bridge.IndexOf("private static string ResolveObservedOrigin(HttpContext httpContext)", StringComparison.Ordinal);
        AssertTrue(observedResolver >= 0, "ResolveObservedOrigin must be defined.");
        int resolverEnd = bridge.IndexOf("public const string LoopbackObservedOrigin", StringComparison.Ordinal);
        AssertTrue(resolverEnd > observedResolver, "ResolveObservedOrigin body must precede the loopback token constant.");
        string resolverBody = bridge.Substring(observedResolver, resolverEnd - observedResolver);
        AssertNotContains(resolverBody, "originHeader", "Observed origin must never be derived from the client-supplied origin header.");
        AssertNotContains(resolverBody, "RemoteOrigin", "Observed origin must never read the X-Salryn-Remote-Origin header.");
    }

    private static void AssertAuthorizationBindsObservedOrigin(string authorization)
    {
        // BuildTrustContext must feed the server-observed origin (not the spoofable client header).
        AssertContains(authorization, "bridge.ObservedOrigin", "Authorization must bind the trust context to the server-observed origin.");
        AssertNotContains(authorization, "new RemotePosTrustContext(bridge.TerminalId, bridge.HandshakeSessionId, bridge.OriginAddress)",
            "Authorization must not bind the trust context to the client-supplied origin header.");
    }

    private static void AssertSessionBindsAndEnforcesOrigin(string sessionService)
    {
        // The trust context and session both carry the server-observed origin.
        AssertContains(sessionService, "string ObservedOrigin", "Trust context / session must carry the observed origin.");
        AssertContains(sessionService, "ObservedOrigin = trust.ObservedOrigin", "Session must bind the observed origin at issuance (login).");

        // Every command re-validates the observed origin and fails closed with a sanitized code.
        AssertContains(sessionService, "session.ObservedOrigin", "Validation must compare the session's bound observed origin.");
        AssertContains(sessionService, "trust.ObservedOrigin", "Validation must compare the current request's observed origin.");
        AssertContains(sessionService, "RemotePos.OriginMismatch", "Validation must fail closed with RemotePos.OriginMismatch.");
        AssertContains(sessionService, "Remote POS session origin does not match the current request.",
            "Origin mismatch must return the sanitized operator message.");
        AssertContains(sessionService, "RemotePosSessionState.OriginMismatch", "Origin mismatch must map to the OriginMismatch session state.");

        // The origin check must be inside ValidateAsync (the per-command re-validation path).
        int validateStart = sessionService.IndexOf("public async Task<RemotePosSessionValidation> ValidateAsync(", StringComparison.Ordinal);
        AssertTrue(validateStart >= 0, "ValidateAsync must exist.");
        int originCheck = sessionService.IndexOf("RemotePos.OriginMismatch", validateStart, StringComparison.Ordinal);
        AssertTrue(originCheck > validateStart, "Origin mismatch must be enforced inside ValidateAsync (every command).");
    }

    private static void AssertWiring(string doc, string coreSmoke, string infraSmoke)
    {
        AssertContains(doc, "Remote origin / session binding", "LAN-M16 doc must document the origin/session binding hardening.");
        AssertContains(doc, "RemotePos.OriginMismatch", "LAN-M16 doc must name the origin-mismatch fail-closed code.");
        AssertContains(coreSmoke, "LanM16OriginBindingChecks.Run();", "CoreSmoke must wire the LAN-M16 origin binding guard.");
        AssertContains(infraSmoke, "remote-pos-origin-binding", "InfrastructureSmoke must wire the remote-pos-origin-binding command.");
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

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
