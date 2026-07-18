namespace Salryn.Tests;

/// <summary>
/// Strong structural source proofs: forbidden claims, Terminal Management dialogs,
/// Replace License re-ack, checkout completion hierarchy.
/// Explicitly not browser proof.
/// </summary>
public static class PreICodeProvenClientSurfaceGuards
{
    public static void Run()
    {
        string root = PreICodeProvenCheckSupport.FindRoot();
        string license = PreICodeProvenCheckSupport.Read(root, "src", "Salryn.App.Web", "src", "pages", "LicensePage.tsx");
        string terminalActivation = PreICodeProvenCheckSupport.Read(root, "src", "Salryn.App.Web", "src", "pages", "TerminalActivationPage.tsx");
        string hostLan = PreICodeProvenCheckSupport.Read(root, "src", "Salryn.App.Web", "src", "pages", "HostLanControlPage.tsx");
        string storeSettings = TryRead(root, "src", "Salryn.App.Web", "src", "pages", "StoreSettingsPage.tsx");
        string backupPage = PreICodeProvenCheckSupport.Read(root, "src", "Salryn.App.Web", "src", "pages", "BackupRestorePage.tsx");
        string checkout = PreICodeProvenCheckSupport.Read(root, "src", "Salryn.App.Web", "src", "pages", "CheckoutPage.tsx");
        string remotePos = PreICodeProvenCheckSupport.Read(root, "src", "Salryn.App.Web", "src", "pages", "RemotePosClientPage.tsx");
        string credits = PreICodeProvenCheckSupport.Read(root, "src", "Salryn.App.Web", "src", "components", "ProductCreditsCard.tsx");
        string appShell = TryRead(root, "src", "Salryn.App.Web", "src", "components", "AppShell.tsx");
        string lifecycle = PreICodeProvenCheckSupport.Read(root, "src", "Salryn.App.Web", "src", "utils", "terminalLifecycleUx.ts");

        AssertForbiddenClaims(
            license,
            terminalActivation,
            hostLan,
            storeSettings,
            backupPage,
            checkout,
            remotePos,
            credits,
            appShell);
        AssertTerminalActivationJargonAbsent(terminalActivation);
        AssertTerminalManagementDialogWiring(hostLan, lifecycle);
        AssertReplaceLicenseReAck(license);
        AssertCheckoutCompletionHierarchy(checkout);
    }

    private static void AssertForbiddenClaims(params string?[] sources)
    {
        string joined = string.Join("\n", sources.Where(s => !string.IsNullOrEmpty(s)));

        string[] forbidden =
        {
            "cloud activation",
            "BIR-ready",
            "official receipt accreditation",
            "ESC/POS supported",
            "cash drawer supported",
            "drawer kick",
            "cutter supported",
            "hardware bundle",
            "Products of Kirjane Labs",
            "mock sample"
        };

        foreach (string claim in forbidden)
        {
            PreICodeProvenCheckSupport.RequireNotContains(joined, claim, "Forbidden client-facing claim regression.");
        }

        PreICodeProvenCheckSupport.RequireNotContains(joined, "Build Type", "No big Build Type metadata block on client surfaces.");
        PreICodeProvenCheckSupport.RequireNotContains(joined, ">Publisher<", "No big Publisher metadata block on client surfaces.");
        PreICodeProvenCheckSupport.RequireNotContains(joined, "find Host License ID", "Host License ID must not be a terminal-user task.");
        PreICodeProvenCheckSupport.RequireNotContains(joined, "find Store ID", "Store ID must not be a terminal-user task.");
    }

    private static void AssertTerminalActivationJargonAbsent(string terminalActivation)
    {
        PreICodeProvenCheckSupport.RequireNotContains(terminalActivation, "payload", "No payload jargon in normal Terminal Activation copy.");
        PreICodeProvenCheckSupport.RequireNotContains(terminalActivation, "schema", "No schema jargon in normal Terminal Activation copy.");
        PreICodeProvenCheckSupport.RequireNotContains(terminalActivation, "wrapper", "No wrapper jargon in normal Terminal Activation copy.");
        PreICodeProvenCheckSupport.RequireNotContains(terminalActivation, " raw ", "No raw-data jargon in normal Terminal Activation copy.");
        PreICodeProvenCheckSupport.RequireNotContains(terminalActivation, "debug", "No debug wording in normal Terminal Activation copy.");
    }

    private static void AssertTerminalManagementDialogWiring(string hostLan, string lifecycle)
    {
        PreICodeProvenCheckSupport.RequireContains(hostLan, "pendingDisableTerminal", "Disable pending state exists.");
        PreICodeProvenCheckSupport.RequireContains(hostLan, "pendingEnableTerminal", "Enable pending state exists.");
        PreICodeProvenCheckSupport.RequireContains(hostLan, "pendingRemoveTerminal", "Remove pending state exists.");
        PreICodeProvenCheckSupport.RequireContains(hostLan, "pendingClearSessionsTerminal", "Clear sessions pending state exists.");

        PreICodeProvenCheckSupport.RequireContains(hostLan, "title=\"Disable terminal?\"", "ConfirmDialog: Disable terminal.");
        PreICodeProvenCheckSupport.RequireContains(hostLan, "title=\"Enable terminal?\"", "ConfirmDialog: Enable terminal.");
        PreICodeProvenCheckSupport.RequireContains(hostLan, "title=\"Remove terminal?\"", "ConfirmDialog: Remove terminal.");
        PreICodeProvenCheckSupport.RequireContains(hostLan, "title=\"Clear operator sessions?\"", "ConfirmDialog: Clear operator sessions.");

        PreICodeProvenCheckSupport.RequireContains(
            hostLan,
            "onRequestDisable={() => setPendingDisableTerminal(terminal)}",
            "Row Disable sets pending state first.");
        PreICodeProvenCheckSupport.RequireContains(
            hostLan,
            "onRequestEnable={() => setPendingEnableTerminal(terminal)}",
            "Row Enable sets pending state first.");
        PreICodeProvenCheckSupport.RequireContains(
            hostLan,
            "onRequestRemove={() => setPendingRemoveTerminal(terminal)}",
            "Row Remove sets pending state first.");

        PreICodeProvenCheckSupport.RequireNotContains(
            hostLan,
            "onRequestDisable={() => disableTerminalMutation.mutate",
            "Disable must not call mutation directly from row.");
        PreICodeProvenCheckSupport.RequireNotContains(
            hostLan,
            "onRequestEnable={() => enableDisabledTerminalMutation.mutate",
            "Enable must not call mutation directly from row.");
        PreICodeProvenCheckSupport.RequireNotContains(
            hostLan,
            "onRequestRemove={() => revokeTerminalMutation.mutate",
            "Remove must not call mutation directly from row.");
        PreICodeProvenCheckSupport.RequireNotContains(
            hostLan,
            "onClearSessions={() => clearTerminalSessionsMutation.mutate",
            "Clear sessions must not fire immediately from row.");

        PreICodeProvenCheckSupport.RequireContains(hostLan, "disableTerminalMutation.mutate(id)", "Confirm Disable calls disable mutation.");
        PreICodeProvenCheckSupport.RequireContains(hostLan, "enableDisabledTerminalMutation.mutate(id)", "Confirm Enable calls enable mutation.");
        PreICodeProvenCheckSupport.RequireContains(hostLan, "revokeTerminalMutation.mutate(id)", "Confirm Remove uses revoke mutation.");
        PreICodeProvenCheckSupport.RequireContains(hostLan, "clearTerminalSessionsMutation.mutate(id)", "Confirm Clear sessions calls clear mutation.");

        PreICodeProvenCheckSupport.RequireContains(hostLan, "const showDisable = canDisableTerminal", "Active row Disable visibility gate.");
        PreICodeProvenCheckSupport.RequireContains(hostLan, "const showEnable = canEnableDisabledTerminal", "Disabled row Enable visibility gate.");
        PreICodeProvenCheckSupport.RequireContains(hostLan, "const showRemove = canRemoveTerminal", "Disabled row Remove visibility gate.");
        PreICodeProvenCheckSupport.RequireContains(hostLan, "Disable terminal", "Disable terminal label.");
        PreICodeProvenCheckSupport.RequireContains(hostLan, "Enable terminal", "Enable terminal label.");
        PreICodeProvenCheckSupport.RequireContains(hostLan, "Remove terminal", "Remove terminal label.");
        PreICodeProvenCheckSupport.RequireNotContains(hostLan, "Restore (not supported)", "No visible Restore (not supported).");
        PreICodeProvenCheckSupport.RequireNotContains(hostLan, ">Revoke<", "No primary Revoke label.");
        PreICodeProvenCheckSupport.RequireContains(
            hostLan,
            "Remove terminal is available only after Disable",
            "Help copy uses Remove terminal language.");

        // Pure helper contracts as source-level unit proofs for Active/Disabled/Revoked/host-local.
        PreICodeProvenCheckSupport.RequireContains(
            lifecycle,
            "export function canDisableTerminal",
            "Disable helper exists.");
        PreICodeProvenCheckSupport.RequireContains(
            lifecycle,
            "export function canEnableDisabledTerminal",
            "Enable helper exists.");
        PreICodeProvenCheckSupport.RequireContains(
            lifecycle,
            "export function canRemoveTerminal",
            "Remove helper exists.");
        PreICodeProvenCheckSupport.RequireContains(
            lifecycle,
            "export function isHostLocalTerminalRole",
            "Host-local role helper exists (not treated as removable remote client).");
    }

    private static void AssertReplaceLicenseReAck(string license)
    {
        PreICodeProvenCheckSupport.RequireContains(
            license,
            "canInstallWizard = licenseText.trim().length > 0 && termsAccepted && !busy",
            "First install requires license text + terms accepted.");
        PreICodeProvenCheckSupport.RequireContains(
            license,
            "canInstallReplace = licenseText.trim().length > 0 && replaceConfirmed && termsAccepted && !busy",
            "Replace requires text + terms re-ack + replace consequence.");
        PreICodeProvenCheckSupport.RequireContains(
            license,
            "step === \"import\" ? \"terms\" : step",
            "Direct import routes through terms/re-ack step first.");
        PreICodeProvenCheckSupport.RequireContains(
            license,
            "disabled={!canInstallReplace}",
            "Replace install cannot be enabled by license text alone.");
        PreICodeProvenCheckSupport.RequireContains(
            license,
            "I have re-read and accept the Salryn Activation Terms for this replacement.",
            "Replace terms re-ack copy.");
        PreICodeProvenCheckSupport.RequireContains(
            license,
            "I understand this replaces the active local license for this device.",
            "Replace consequence confirmation copy.");
        PreICodeProvenCheckSupport.RequireNotContains(
            license,
            "find Host License ID",
            "Replace flow must not introduce Host License ID task burden.");
        PreICodeProvenCheckSupport.RequireNotContains(
            license,
            "find Store ID",
            "Replace flow must not introduce Store ID task burden.");
    }

    private static void AssertCheckoutCompletionHierarchy(string checkout)
    {
        PreICodeProvenCheckSupport.RequireOrder(
            checkout,
            "Cash sale committed",
            "Support details",
            "Receipt/success copy is primary before Support details.");
        PreICodeProvenCheckSupport.RequireOrder(
            checkout,
            "Support details",
            "Sale ID:",
            "Sale ID appears only inside/after Support details.");
        PreICodeProvenCheckSupport.RequireOrder(
            checkout,
            "Keep this receipt number for your store records.",
            "Sale ID:",
            "Receipt number instruction precedes internal Sale ID.");
        PreICodeProvenCheckSupport.RequireContains(
            checkout,
            "Keep this receipt number for your store records.",
            "Receipt reference is primary cashier copy.");
        PreICodeProvenCheckSupport.RequireContains(
            checkout,
            "Support details",
            "Support details collapse exists for internal IDs.");
        PreICodeProvenCheckSupport.RequireNotContains(
            checkout,
            "debug",
            "Normal cashier checkout path must not use debug wording.");
    }

    private static string? TryRead(string root, params string[] parts)
    {
        string path = Path.Combine(new[] { root }.Concat(parts).ToArray());
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }
}
