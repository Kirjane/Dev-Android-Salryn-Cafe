// LAN-M16 remote-pos-final-acceptance smoke: runs the full host-owned multi-terminal role runtime
// acceptance suite end to end (operator session fail-closed, role authorization matrix, checkout
// idempotency + host write boundary, manager/admin boundary, host/client identity) and confirms
// the read-only bridge remains untouched and GET-only.
internal static class RemotePosFinalAcceptanceSmokeChecks
{
    public static void Run(string projectRoot)
    {
        Console.WriteLine("===== LAN-M16 REMOTE POS FINAL ACCEPTANCE SMOKE CHECKS =====");

        RemotePosOperatorSessionSmokeChecks.Run(projectRoot);
        RemotePosCommandAuthorizationSmokeChecks.Run(projectRoot);
        RemotePosCheckoutIdempotencySmokeChecks.Run(projectRoot);
        RemotePosHostWriteBoundarySmokeChecks.Run(projectRoot);
        RemotePosManagerAdminBoundarySmokeChecks.Run(projectRoot);
        RemotePosHostClientIdentitySmokeChecks.Run(projectRoot);

        RequireReadOnlyBridgeUntouched(projectRoot);

        Console.WriteLine("LAN-M16 remote POS final acceptance smoke checks succeeded.");
        Console.WriteLine();
    }

    private static void RequireReadOnlyBridgeUntouched(string projectRoot)
    {
        string apiRoot = Path.Combine(projectRoot, "src", "Salryn.Api.Local");

        string readOnlyEndpoints = ReadRequired(apiRoot, "RemoteReadOnlyApiEndpoints.cs");
        if (readOnlyEndpoints.Contains(".MapPost(", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Read-only bridge must remain GET-only (no MapPost).");
        }

        string remotePosEndpoints = ReadRequired(apiRoot, "RemotePos", "RemotePosApiEndpoints.cs");
        if (!remotePosEndpoints.Contains("MapGroup(\"/api/remote-pos\")", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Remote POS surface must be a separate /api/remote-pos route group.");
        }

        // Remote POS command surface must not be mounted under the read-only route group.
        if (remotePosEndpoints.Contains("MapGroup(\"/api/remote-readonly\")", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Remote POS surface must not reuse the read-only route group.");
        }
    }

    private static string ReadRequired(params string[] parts)
    {
        string path = Path.Combine(parts);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("Required source file missing: " + path);
        }

        return File.ReadAllText(path);
    }
}
