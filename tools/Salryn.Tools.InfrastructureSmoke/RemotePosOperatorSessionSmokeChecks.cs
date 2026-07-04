using Salryn.Api.Local.Composition;
using Salryn.Api.Local.RemotePos;
using Salryn.Composition.Local;

// LAN-M16 remote-pos-operator-session smoke: proves the host-issued operator session trust chain
// fails CLOSED. Terminal identity + handshake are required, the remote POS runtime must be
// explicitly enabled on the host, and explicit LAN transport must be enabled. None of these are
// on by default, so a remote operator can never obtain a session on a fresh/default host.
internal static class RemotePosOperatorSessionSmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== LAN-M16 REMOTE POS OPERATOR SESSION SMOKE CHECKS =====");

        RemotePosSmokeSupport.EnsureDatabaseReady(projectRoot);

        RemotePosApiServices services = RemotePosSmokeSupport.CreateServices(
            projectRoot, out SalrynLocalComposition _, out RemotePosRuntimeGate runtimeGate);

        RemotePosOperatorSessionService sessions = services.SessionService;

        RemotePosTrustContext trust = new(Guid.NewGuid(), Guid.NewGuid(), "192.168.1.55");

        // 1. Runtime gate disabled by default -> trust fails closed as RemotePosDisabled.
        RemotePosSmokeSupport.Require(!runtimeGate.IsEnabled, "Remote POS runtime gate must be disabled by default.");

        RemotePosTrustResult defaultTrust = sessions.ValidateTrustAsync(trust).GetAwaiter().GetResult();
        RemotePosSmokeSupport.Require(!defaultTrust.Ok, "Default trust must be rejected.");
        RemotePosSmokeSupport.Require(defaultTrust.State == RemotePosSessionState.RemotePosDisabled,
            "Default trust rejection must be RemotePosDisabled.");

        // 2. Login is blocked before authentication when the runtime is disabled (no credential probing).
        RemotePosLoginOutcome disabledLogin = sessions
            .LoginAsync(trust, new RemotePosLoginRequest("whoever", "whatever"))
            .GetAwaiter()
            .GetResult();
        RemotePosSmokeSupport.Require(!disabledLogin.Ok, "Login must fail when runtime disabled.");
        RemotePosSmokeSupport.Require(disabledLogin.State == RemotePosSessionState.RemotePosDisabled,
            "Login rejection must be RemotePosDisabled before authentication.");
        RemotePosSmokeSupport.Require(disabledLogin.Session is null && disabledLogin.Token is null,
            "No session/token may be issued when runtime disabled.");

        // 3. Missing terminal/handshake -> TrustRejected even if runtime were enabled.
        runtimeGate.Enable(Guid.NewGuid());
        RemotePosTrustResult missingTerminal = sessions
            .ValidateTrustAsync(new RemotePosTrustContext(Guid.Empty, Guid.Empty, "192.168.1.55"))
            .GetAwaiter()
            .GetResult();
        RemotePosSmokeSupport.Require(!missingTerminal.Ok, "Missing terminal/handshake must be rejected.");
        RemotePosSmokeSupport.Require(missingTerminal.State == RemotePosSessionState.TrustRejected,
            "Missing terminal/handshake rejection must be TrustRejected.");

        // 4. Runtime enabled but explicit LAN transport still disabled by default -> RemotePosDisabled.
        RemotePosTrustResult transportDisabled = sessions.ValidateTrustAsync(trust).GetAwaiter().GetResult();
        RemotePosSmokeSupport.Require(!transportDisabled.Ok, "Trust must fail while LAN transport disabled.");
        RemotePosSmokeSupport.Require(transportDisabled.State == RemotePosSessionState.RemotePosDisabled,
            "Runtime-on but transport-off must still report RemotePosDisabled.");

        // 5. A malformed/empty session token can never validate.
        RemotePosSessionValidation malformed = sessions.ValidateAsync(trust, null).GetAwaiter().GetResult();
        RemotePosSmokeSupport.Require(!malformed.Ok, "Null session token must never validate.");

        runtimeGate.Disable();

        Console.WriteLine("LAN-M16 remote POS operator session smoke checks succeeded.");
        Console.WriteLine();
    }
}
