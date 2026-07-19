namespace Salryn.Tests;

/// <summary>
/// PRE-I: operator-facing LAN setup clarity and confirmation guards.
/// Source-proves the React wiring while preserving the existing backend mutations.
/// </summary>
public static class PreILanSetupUxClarityChecks
{
    public static void Run()
    {
        string root = FindRoot();
        string page = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "pages", "HostLanControlPage.tsx"));

        // A. Operator copy and terminology.
        foreach (string required in new[]
        {
            "Connect terminals over LAN",
            "Host PC LAN address",
            "Terminal link",
            "Copy terminal link",
            "Start LAN access",
            "Stop LAN access",
            "Terminal POS access",
            "terminal device"
        })
        {
            RequireContains(page, required, "LAN setup copy missing: " + required);
        }

        RequireNotContains(page, "Copy client URL", "Normal button copy must use terminal link.");
        RequireNotContains(page, "Enable Remote POS runtime", "Normal button copy must use Terminal POS access.");
        RequireNotContains(page, "Disable Remote POS runtime", "Normal button copy must use Terminal POS access.");
        RequireNotContains(page, "LAN transport disabled", "Normal status must not expose transport terminology.");

        int advancedIndex = RequireIndex(page, "Advanced diagnostics");
        RequireAfter(page, "Configured bind (display)", advancedIndex, "Configured bind must stay under Advanced diagnostics.");
        RequireAfter(page, "Host local URL", advancedIndex, "Host local URL must stay under Advanced diagnostics.");
        RequireAfter(page, "Remote transport", advancedIndex, "Transport state must stay under Advanced diagnostics.");
        RequireAfter(page, "Remote POS runtime", advancedIndex, "Runtime state must stay under Advanced diagnostics.");

        // B. Exact terminal link behavior.
        RequireContains(page, "runtime?.clientUrlReady && runtime.clientUrl ? runtime.clientUrl : \"\"", "Terminal link must come only from runtime-confirmed reachability.");
        RequireContains(page, "readiness === \"Ready\" && terminalLink", "Ready state must visibly render the terminal link.");
        RequireNotContains(page, "readiness !== \"Ready\" && terminalLink", "Pre-ready terminal-link previews must not imply reachability.");
        RequireContains(page, "Terminal devices can open this link", "Ready instruction must be explicit.");
        RequireContains(page, "Restart Salryn before connecting terminals", "Restart-required instruction must be explicit.");
        RequireContains(page, "disabled={!canCopyTerminalLink || busy}", "Copy may remain readiness-gated.");

        // C. LAN-level confirmation guards.
        foreach (string required in new[]
        {
            "pendingStopLan",
            "title=\"Stop LAN access?\"",
            "pendingStopTerminalPos",
            "title=\"Stop terminal POS access?\"",
            "pendingRejectRequestId",
            "title=\"Reject pairing request?\"",
            "pendingCreateJoinCode",
            "title=\"Create a new join code?\"",
            "pendingRepairLanAddress",
            "title=\"Repair LAN address?\""
        })
        {
            RequireContains(page, required, "Confirmation guard missing: " + required);
        }

        RequireContains(page, "onDisableLan={() => setPendingStopLan(true)}", "Stop LAN button must open confirmation.");
        RequireContains(page, "onDisableRemotePos={() => setPendingStopTerminalPos(true)}", "Stop Terminal POS button must open confirmation.");
        RequireContains(page, "onClick={() => setPendingRejectRequestId(request.requestId)}", "Reject button must open confirmation.");
        RequireContains(page, "onClick={() => setPendingCreateJoinCode(true)}", "Join-code rotation must open confirmation.");
        RequireContains(page, "onRepair={requestRepairLanAddress}", "Repair must use the guarded request handler.");
        RequireNotContains(page, "onDisableLan={() => remoteTransportMutation.mutate", "Stop LAN button must not mutate directly.");
        RequireNotContains(page, "onDisableRemotePos={() => remotePosRuntimeMutation.mutate", "Stop Terminal POS button must not mutate directly.");
        RequireNotContains(page, "onClick={() => rejectJoinRequestMutation.mutate(request.requestId)}", "Reject button must not mutate directly.");

        foreach (string existingDialog in new[]
        {
            "title=\"Disable terminal?\"",
            "title=\"Enable terminal?\"",
            "title=\"Remove terminal?\"",
            "title=\"Clear operator sessions?\""
        })
        {
            RequireContains(page, existingDialog, "Existing terminal confirmation regressed: " + existingDialog);
        }

        // D. Support/admin details stay collapsed and out of the normal task flow.
        int licenseDeskIndex = RequireIndex(page, "License desk details");
        RequireContains(page, "Each terminal device needs its own Terminal license", "Normal license reminder required.");
        RequireContains(page, "only needs to send its Terminal", "Normal copy must limit the terminal-device task.");
        RequireContains(page, "Machine Code and a name/location", "Normal copy must state the two operator-provided values.");
        RequireAfter(page, ">Store ID</dt>", licenseDeskIndex, "Store ID must stay under License desk details.");
        RequireAfter(page, ">Host License ID</dt>", licenseDeskIndex, "Host License ID must stay under License desk details.");
        RequireNotContains(page, "LicensingOps", "Normal product source must use license desk/support wording.");

        // E. Same underlying API mutations remain after confirmation.
        RequireContains(page, "remoteTransportMutation.mutate(", "Stop LAN must retain the existing API mutation.");
        RequireContains(page, "{ enable: false, bindAddress: null }", "Stop LAN must retain the existing disable payload.");
        RequireContains(page, "remotePosRuntimeMutation.mutate(", "Stop Terminal POS must retain the existing API mutation.");
        RequireContains(page, "{ enable: false }", "Stop Terminal POS must retain the existing disable payload.");
        RequireContains(page, "rejectJoinRequestMutation.mutate(requestId,", "Reject must retain the existing API mutation.");
        RequireContains(page, "rotateJoinCodeMutation.mutate(undefined,", "Create join code must retain the existing rotation mutation.");
        RequireContains(page, "repairBindMutation.mutate(target || null,", "Repair must retain the existing API mutation.");
    }

    private static int RequireIndex(string source, string value)
    {
        int index = source.IndexOf(value, StringComparison.Ordinal);
        if (index < 0) throw new InvalidOperationException("Missing: " + value);
        return index;
    }

    private static void RequireAfter(string source, string value, int boundary, string message)
    {
        int index = source.IndexOf(value, StringComparison.Ordinal);
        if (index <= boundary) throw new InvalidOperationException(message + " Value: " + value);
    }

    private static void RequireContains(string source, string value, string message)
    {
        if (!source.Contains(value, StringComparison.Ordinal))
            throw new InvalidOperationException(message + " Missing: " + value);
    }

    private static void RequireNotContains(string source, string value, string message)
    {
        if (source.Contains(value, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(message + " Forbidden: " + value);
    }

    private static string FindRoot()
    {
        DirectoryInfo? current = new(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln"))) return current.FullName;
            current = current.Parent;
        }
        throw new InvalidOperationException("Could not locate Salryn.sln.");
    }
}
