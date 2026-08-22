namespace Salryn.Tests;

// LAN-M16 final acceptance guard. Runs the four focused LAN-M16 guards, then asserts the
// cross-cutting boundaries for the full host-owned multi-terminal candidate: the read-only bridge
// stays GET-only and untouched, the remote POS surface is a SEPARATE route group, /api/lan-control
// stays local-operator only, the docs carry the required LAN-M16 statements, and every LAN-M16
// guard + infrastructure smoke is wired.
public static class LanM16FinalMultiTerminalAcceptanceChecks
{
    private const string BaselineSha = "e477699dca27a21954a5ad9d388307fdb9ed08fd";

    public static void Run()
    {
        LanM16HostOwnedRemoteOperatorSessionChecks.Run();
        LanM16RemotePosCommandAuthorizationChecks.Run();
        LanM16RemoteCheckoutIdempotencyAndHostWriteChecks.Run();
        LanM16HostClientIdentityUxChecks.Run();
        LanM16OriginBindingChecks.Run();
        LanM16PostCommitAuditSafetyChecks.Run();

        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        string apiRoot = Path.Combine(projectRoot, "src", "Salryn.Api.Local");

        string readOnlyEndpoints = ReadRequiredFile(apiRoot, "RemoteReadOnlyApiEndpoints.cs");
        string remotePosEndpoints = ReadRequiredFile(apiRoot, "RemotePos", "RemotePosApiEndpoints.cs");
        string lanControlEndpoints = ReadRequiredFile(apiRoot, "LocalLanControlEndpoints.cs");
        string remotePosServices = ReadRequiredFile(apiRoot, "RemotePos", "RemotePosApiServices.cs");
        string currentState = ReadRequiredFile(projectRoot, "00-HANDOFF", "CURRENT_STABLE_STATE_AND_NEXT_WORK.md");
        string doc = ReadRequiredFile(projectRoot, "00-HANDOFF", "LAN_M16_HOST_OWNED_MULTI_TERMINAL_ROLE_RUNTIME.md");
        string coreSmoke = ReadRequiredFile(projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");
        string infraSmoke = ReadRequiredFile(projectRoot, "tools", "Salryn.Tools.InfrastructureSmoke", "Program.cs");

        AssertReadOnlyBridgeUntouched(readOnlyEndpoints, remotePosEndpoints);
        AssertLanControlLocalOnly(lanControlEndpoints);
        AssertNoDefaultOrWildcardBind(remotePosServices, remotePosEndpoints);
        AssertCurrentStateDoc(currentState);
        AssertLanM16Doc(doc);
        AssertGuardsAndSmokesWired(coreSmoke, infraSmoke);
    }

    private static void AssertReadOnlyBridgeUntouched(string readOnlyEndpoints, string remotePosEndpoints)
    {
        AssertContains(readOnlyEndpoints, "/api/remote-readonly", "Read-only bridge must remain under /api/remote-readonly.");
        AssertNotContains(readOnlyEndpoints, ".MapPost(", "Read-only bridge must remain GET-only.");

        AssertContains(remotePosEndpoints, "MapGroup(\"/api/remote-pos\")", "Remote POS must live under its own /api/remote-pos route group.");
        AssertNotContains(remotePosEndpoints, "MapGroup(\"/api/remote-readonly\")", "Remote POS must not reuse the read-only route group.");
    }

    private static void AssertLanControlLocalOnly(string lanControlEndpoints)
    {
        AssertContains(lanControlEndpoints, "MapGroup(\"/api/lan-control\")", "LAN control must remain under /api/lan-control.");
        AssertContains(lanControlEndpoints, "LocalLanControlAuthorization.RequireLocalOperator", "LAN control must remain local-operator only.");
        // The remote POS runtime toggle is a host-local control, guarded like the rest.
        AssertContains(lanControlEndpoints, "MapPost(\"/remote-pos\"", "Host-local remote POS runtime toggle must live under /api/lan-control.");
    }

    private static void AssertNoDefaultOrWildcardBind(string remotePosServices, string remotePosEndpoints)
    {
        AssertNotContains(remotePosServices, "0.0.0.0", "Remote POS composition must not introduce a wildcard bind.");
        AssertNotContains(remotePosEndpoints, "0.0.0.0", "Remote POS endpoints must not introduce a wildcard bind.");
    }

    private static void AssertCurrentStateDoc(string currentState)
    {
        AssertContains(currentState, BaselineSha, "Current-state doc must carry the stable Sandbox SHA.");
        if (IsCurrentLanM16QaBState(currentState))
        {
            AssertContains(currentState, "LAN-M16-QA-B", "Current-state doc must name LAN-M16-QA-B as the current candidate.");
            AssertContains(currentState, "LAN-M16-QA-B adds a host-approved Remote POS pairing gate and remote role login boundary.", "Current-state doc must state LAN-M16-QA-B adds the remote POS pairing/login boundary.");
            AssertContains(currentState, "Cashier/Manager only for Remote POS login.", "Current-state doc must state Remote POS login is limited to Cashier/Manager.");
            AssertContains(currentState, "Admin/Owner remain host-local for this build.", "Current-state doc must keep Admin/Owner host-local.");
            AssertContains(currentState, "Remote-readonly remains GET-only under `/api/remote-readonly`.", "Current-state doc must keep remote-readonly GET-only.");
            AssertContains(currentState, "Local LAN control remains loopback/operator-only under `/api/lan-control`.", "Current-state doc must keep local LAN control loopback-only.");
            AssertContains(currentState, "Host owns SQLite, sales, inventory writes, receipt allocation, and audit.", "Current-state doc must keep host-owned storage/writes.");
            AssertContains(currentState, "Remote clients never share SQLite.", "Current-state doc must keep the no-shared-SQLite boundary.");
            AssertContains(currentState, "Independent hosts do not sync.", "Current-state doc must keep the no-sync boundary.");
            AssertContains(currentState, "Remote POS runtime is disabled by default and resets on host restart.", "Current-state doc must keep remote POS disabled by default.");
            AssertContains(currentState, "Remote POS commands remain bound to terminal identity, handshake proof, host-issued operator session, host/store identity, and server-observed origin.", "Current-state doc must keep the remote POS trust binding.");
            AssertContains(currentState, "Terminal disable/revoke still blocks remote POS through existing terminal trust checks.", "Current-state doc must keep terminal disable/revoke blocking remote POS.");
            AssertContains(currentState, "Historical M-series and ROLE docs remain context only and must not override current source truth.", "Current-state doc must keep older M-series docs demoted to historical context.");
        }
        else
        {
            AssertContains(currentState, "LAN-M16 is current host-owned multi-terminal role runtime candidate.", "Current-state doc must name LAN-M16 as current candidate.");
            AssertContains(currentState, "LAN-M16 adds actual remote cashier/manager/admin POS operation against host DB.", "Current-state doc must state LAN-M16 adds remote POS operation against the host DB.");
            AssertContains(currentState, "LAN-M16 does not use shared SQLite.", "Current-state doc must state LAN-M16 does not use shared SQLite.");
            AssertContains(currentState, "LAN-M16 does not imply sync between two independent hosts.", "Current-state doc must state LAN-M16 does not imply sync.");
            AssertContains(currentState, "LAN-M16 keeps LAN bind explicit and host-controlled.", "Current-state doc must state LAN bind stays explicit/host-controlled.");
            AssertContains(currentState, "LAN_M16_HOST_OWNED_MULTI_TERMINAL_ROLE_RUNTIME.md", "Current-state doc must list the LAN-M16 doc as live authority.");
        }

        // The read-only completion cap statement remains required only on the pre-QA-B branch shape.
        if (!IsCurrentLanM16QaBState(currentState))
        {
            AssertContains(currentState, "LAN-M15-F is the LAN completion cap", "Current-state doc must keep LAN-M15-F completion cap statement.");
        }
        AssertNotContains(currentState, "LAN-M14-J is current", "Current-state doc must not mark LAN-M14-J as current work.");
        AssertNotContains(currentState, "LAN-M15-G is current", "Current-state doc must not mark LAN-M15-G as current work.");

        if (!IsCurrentLanM16QaBState(currentState))
        {
            // LAN-M16-QA-A: the current-state doc must no longer globally deny mutation/checkout. The
            // "no mutation/no checkout/no remote cashier" boundary now applies only to /api/remote-readonly,
            // while controlled host-owned mutation/checkout is explicitly scoped to /api/remote-pos.
            AssertContains(currentState, "Remote-readonly (`/api/remote-readonly`) remains GET-only: no mutation, no checkout, and no remote cashier/admin execution on that surface.",
                "Current-state doc must scope the no-mutation/no-checkout boundary to remote-readonly.");
            AssertContains(currentState, "LAN-M16 adds controlled remote POS mutation/checkout through `/api/remote-pos` only.",
                "Current-state doc must state LAN-M16 adds controlled remote POS mutation/checkout through /api/remote-pos only.");
            AssertContains(currentState, "Next step is Codex review of LAN-M16-QA-A.",
                "Current-state doc must name Codex review of LAN-M16-QA-A as the next step.");
            AssertContains(currentState, "Remote official strict BIR/electronic-journal path remains host-local; do not claim remote RMO compliance.",
                "Current-state doc must keep the official/RMO host-local limitation.");
            // The old flat global denial bullets must not linger as standalone contradictions.
            AssertNotContains(currentState, "- No mutation.", "Current-state doc must not keep a flat global 'No mutation.' bullet.");
            AssertNotContains(currentState, "- No checkout.", "Current-state doc must not keep a flat global 'No checkout.' bullet.");
        }
    }

    private static bool IsCurrentLanM16QaBState(string currentState)
    {
        return currentState.Contains("LAN-M16-QA-B", StringComparison.Ordinal)
            && currentState.Contains("Historical M-series and ROLE docs remain context only and must not override current source truth.", StringComparison.Ordinal)
            && currentState.Contains("Current live coordination authority:", StringComparison.Ordinal);
    }

    private static void AssertLanM16Doc(string doc)
    {
        AssertContains(doc, BaselineSha, "LAN-M16 doc must carry the source stamp.");
        foreach (string section in new[]
        {
            "Remote Operator Session Model",
            "LAN Role Authorization Matrix",
            "Remote POS Command API",
            "Remote Checkout Runtime",
            "Host-Owned Official Write Boundary",
            "Remote Manager / Admin Runtime",
            "Conflict / Concurrency Model",
            "Host / Client Identity UX",
            "Integration Tests / Smokes Added",
            "Schema / Migration Changes",
            "Forbidden Scope",
            "Decision",
        })
        {
            AssertContains(doc, section, $"LAN-M16 doc must include the '{section}' section.");
        }

        AssertContains(doc, "None.", "LAN-M16 doc must state schema changes are None.");
        AssertContains(doc, "no wildcard/`0.0.0.0` bind", "LAN-M16 doc forbidden scope must reject wildcard/0.0.0.0 bind.");
        AssertNotContains(doc, "LAN-M15-G is current", "LAN-M16 doc must not mark LAN-M15-G as current.");

        // LAN-M16-QA-A: the doc must carry the explicit promotion-limitations/product boundary and
        // the hardening notes for origin binding + post-commit audit safety.
        foreach (string section in new[]
        {
            "Promotion limitations / product boundary",
            "LAN-M16-QA-A Hardening",
            "Remote origin / session binding",
            "Post-commit audit safety",
        })
        {
            AssertContains(doc, section, $"LAN-M16 doc must include the '{section}' section/heading.");
        }

        AssertContains(doc, "is **not** a strict remote official / RMO checkout",
            "LAN-M16 doc must state the remote sale is not strict remote official/RMO checkout.");
        AssertContains(doc, "Cash-only", "LAN-M16 doc must state remote tender is Cash-only.");
        AssertContains(doc, "in-memory and restart-scoped", "LAN-M16 doc must state sessions/carts/idempotency are in-memory and restart-scoped.");
        AssertContains(doc, "Independent hosts do **not** sync.", "LAN-M16 doc must state independent hosts do not sync.");
    }

    private static void AssertGuardsAndSmokesWired(string coreSmoke, string infraSmoke)
    {
        foreach (string guard in new[]
        {
            "LanM16HostOwnedRemoteOperatorSessionChecks.Run();",
            "LanM16RemotePosCommandAuthorizationChecks.Run();",
            "LanM16RemoteCheckoutIdempotencyAndHostWriteChecks.Run();",
            "LanM16HostClientIdentityUxChecks.Run();",
            "LanM16OriginBindingChecks.Run();",
            "LanM16PostCommitAuditSafetyChecks.Run();",
            "LanM16FinalMultiTerminalAcceptanceChecks.Run();",
        })
        {
            AssertContains(coreSmoke, guard, $"CoreSmoke must wire {guard}");
        }

        foreach (string command in new[]
        {
            "remote-pos-operator-session",
            "remote-pos-command-authorization",
            "remote-pos-checkout-idempotency",
            "remote-pos-host-write-boundary",
            "remote-pos-manager-admin-boundary",
            "remote-pos-host-client-identity",
            "remote-pos-final-acceptance",
            "remote-pos-origin-binding",
            "remote-pos-post-commit-audit-safety",
        })
        {
            AssertContains(infraSmoke, command, $"InfrastructureSmoke must wire the {command} command.");
        }
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
