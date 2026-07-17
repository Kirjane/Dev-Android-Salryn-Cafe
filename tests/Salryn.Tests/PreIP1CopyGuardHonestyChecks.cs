using Microsoft.Data.Sqlite;
using Salryn.Composition.Local;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

/// <summary>
/// PRE-I P1 copy/guard honesty: remove theater, align restore phrase, replace re-ack, thin behavioral proofs, fresh-state routing.
/// </summary>
public static class PreIP1CopyGuardHonestyChecks
{
    public static void Run()
    {
        string root = FindRoot();
        string terminalActivation = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "pages", "TerminalActivationPage.tsx"));
        string hostLan = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "pages", "HostLanControlPage.tsx"));
        string licensePage = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "pages", "LicensePage.tsx"));
        string backupPage = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "pages", "BackupRestorePage.tsx"));
        string backupApi = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "api", "backups.ts"));
        string checkout = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "pages", "CheckoutPage.tsx"));
        string remotePos = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "pages", "RemotePosClientPage.tsx"));
        string lifecycle = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "utils", "terminalLifecycleUx.ts"));
        string app = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "App.tsx"));
        string recovery = File.ReadAllText(Path.Combine(root, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteBackupRecoveryService.cs"));
        string registry = File.ReadAllText(Path.Combine(root, "src", "Salryn.Infrastructure", "Data", "Services", "TerminalRegistryService.cs"));
        string lanService = File.ReadAllText(Path.Combine(root, "src", "Salryn.Api.Local", "Services", "LocalLanControlService.cs"));
        string lanEndpoints = File.ReadAllText(Path.Combine(root, "src", "Salryn.Api.Local", "LocalLanControlEndpoints.cs"));

        // P1-001: no sr-only theater
        RequireNotContains(terminalActivation, "sr-only", "Terminal activation must not ship sr-only guard theater.");
        RequireContains(terminalActivation, "This Terminal Machine Code", "Visible terminal machine code label.");
        RequireContains(terminalActivation, "Copy Terminal Machine Code", "Visible copy machine code.");
        RequireContains(terminalActivation, "do not need a Host License ID", "Visible no Host License ID task.");
        RequireContains(terminalActivation, "Import Signed Terminal License JSON", "Visible import title.");
        RequireContains(terminalActivation, "Host licenses cannot activate a terminal", "Visible host-license rejection.");

        // P1-003/004 operator Remove language
        RequireContains(hostLan, "Remove terminal", "Remove terminal primary label.");
        RequireContains(lifecycle, "Retired / removed terminals", "Retired group uses removed wording.");
        RequireContains(lifecycle, "return \"Removed\"", "Revoked status displays as Removed.");
        RequireNotContains(hostLan, "Remove (revoke)", "No Remove (revoke) operator phrase.");
        RequireNotContains(hostLan, ">Revoke<", "No Revoke button.");
        RequireContains(hostLan, "Revoked means re-pair", "Keep gate phrase for revoke semantics honesty.");

        // P1-005 clear sessions confirm
        RequireContains(hostLan, "pendingClearSessionsTerminal", "Clear sessions pending state.");
        RequireContains(hostLan, "Clear operator sessions?", "Clear sessions dialog title.");
        RequireContains(hostLan, "This signs out active operator sessions on this terminal. The terminal remains paired.", "Clear sessions body.");
        RequireContains(hostLan, "Clear sessions", "Clear sessions confirm label.");
        RequireContains(hostLan, "Keep sessions", "Clear sessions cancel.");
        RequireNotContains(hostLan, "onClearSessions={() => clearTerminalSessionsMutation.mutate", "Clear sessions must not fire immediately from row.");

        // P1-006 replace re-ack
        RequireContains(licensePage, "canInstallReplace = licenseText.trim().length > 0 && replaceConfirmed && termsAccepted", "Replace requires terms + replace confirm.");
        RequireContains(licensePage, "I have re-read and accept the Salryn Activation Terms for this replacement.", "Replace terms re-ack copy.");
        RequireContains(licensePage, "I understand this replaces the active local license for this device.", "Replace device-local re-ack.");

        // P1-007 LicensingOps softened in primary operator surfaces (allowed in request block)
        RequireContains(hostLan, "Kirjane Labs / Salryn Infodesk", "Primary Host LAN copy prefers Kirjane Labs / Salryn Infodesk.");
        RequireContains(licensePage, "Kirjane Labs / Salryn Infodesk", "License import prefers Infodesk wording.");

        // P1-008
        RequireContains(remotePos, "Advanced support only", "Terminal product ID path uses Advanced support only.");
        RequireNotContains(remotePos, "Support/debug path only", "No debug wording on terminal product ID path.");
        RequireNotContains(remotePos, "debug path", "No debug path wording on Remote POS.");

        // P1-009 restore phrase alignment
        RequireContains(backupApi, "RESTORE_CONFIRMATION_INPUT = \"RESTORE SALRYN BACKUP\"", "UI constant matches backend phrase.");
        RequireContains(backupApi, "confirmation", "Restore API sends typed confirmation.");
        RequireContains(backupPage, "Type RESTORE SALRYN BACKUP to enable restore", "UI asks for full phrase.");
        RequireContains(backupPage, "restoreBackup(restoreTarget, restoreText)", "UI sends typed text to API.");
        RequireContains(recovery, "RestoreConfirmationText = \"RESTORE SALRYN BACKUP\"", "Backend phrase constant.");
        RequireNotContains(backupApi, "BACKEND_RESTORE_CONFIRMATION", "No silent dual-phrase constant.");

        // P1-010 backup format version
        RequireContains(backupPage, "Backup format version", "Softer backup version label.");
        RequireNotContains(backupPage, "Schema / app migration", "No schema/migration user-facing label.");

        // P1-011 checkout support details
        RequireContains(checkout, "Support details", "Sale support details collapsed.");
        RequireContains(checkout, "Keep this receipt number for your store records.", "Cashier-facing receipt wording.");
        RequireContains(checkout, "checkout receipt number", "Non-official checkout receipt reference retained.");
        RequireContains(checkout, "Sale ID:", "Sale ID retained under support details.");

        // Lifecycle enable/disable/remove endpoints still present
        RequireContains(registry, "EnableTerminalAsync", "Enable backend.");
        RequireContains(registry, "DisableTerminalAsync", "Disable backend.");
        RequireContains(registry, "RevokeTerminalAsync", "Remove uses revoke backend.");
        RequireContains(lanEndpoints, "/terminals/{terminalId}/enable", "Enable route.");
        RequireContains(lanService, "EnableTerminalAsync", "LAN service enable.");

        // Fresh-state routing: license before bootstrap before login
        RequireContains(app, "isHostLicenseReady", "Host license readiness gate.");
        RequireOrder(app, "if (!isHostLicenseReady", "if (bootstrapStatus?.firstRunRequired)", "License landing before bootstrap.");
        RequireOrder(app, "if (bootstrapStatus?.firstRunRequired)", "if (!sessionQuery.data?.isAuthenticated)", "Bootstrap before login.");
        RequireContains(app, "<LicensePage />", "Renders LicensePage when unlicensed.");

        RunBackupRestoreConfirmationBehavior();
        RunFreshDbStateProof();
    }

    private static void RunBackupRestoreConfirmationBehavior()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "salryn-p1-restore-" + Guid.NewGuid().ToString("N"));
        try
        {
            var composition = SalrynLocalComposition.Create(DbPaths.CreateDevelopment(tempRoot));
            composition.InitializeDatabaseAsync().GetAwaiter().GetResult();
            RequireTrue(
                composition.DatabaseInitialization.CreateFirstAdmin(
                    new("P1 Backup Store", null, null, null, null, 80, "admin", "Admin", "StrongAdmin123!")).IsSuccess,
                "bootstrap for backup proof");

            var svc = new ControlledBackupPackageService(composition.DbPaths);
            var created = svc.Create();
            RequireTrue(created.IsSuccess, "create backup: " + (created.IsFailure ? created.ErrorDetail.Message : ""));
            string id = created.Value.BackupId;

            RequireTrue(!svc.Restore(id, "RESTORE").IsSuccess, "Short RESTORE phrase must be rejected by backend.");
            RequireTrue(!svc.Restore(id, "wrong").IsSuccess, "Wrong confirmation must fail.");
            RequireTrue(
                svc.Restore(id, SqliteBackupRecoveryService.RestoreConfirmationText).IsSuccess,
                "Full RESTORE SALRYN BACKUP must restore.");
        }
        finally
        {
            SqliteConnectionFactory.ClearAllPools();
            try { Directory.Delete(tempRoot, true); } catch { /* best effort */ }
        }
    }

    private static void RunFreshDbStateProof()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "salryn-p1-fresh-" + Guid.NewGuid().ToString("N"));
        try
        {
            DbPaths paths = DbPaths.CreateDevelopment(tempRoot);
            paths.EnsureDirectories();
            var composition = SalrynLocalComposition.Create(paths);
            composition.InitializeDatabaseAsync().GetAwaiter().GetResult();

            using var connection = new SqliteConnection("Data Source=" + paths.DatabasePath);
            connection.Open();
            long userCount = ScalarLong(connection, "SELECT COUNT(*) FROM users");
            RequireTrue(userCount == 0, "Fresh DB must have zero users before bootstrap. Count=" + userCount);

            // No installed license file expected under fresh paths root for composition default.
            string? licenseDir = Directory.Exists(paths.RootPath) ? paths.RootPath : tempRoot;
            bool licenseJsonPresent = Directory.Exists(licenseDir)
                && Directory.EnumerateFiles(licenseDir, "*license*", SearchOption.AllDirectories)
                    .Any(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
            RequireTrue(!licenseJsonPresent, "Fresh data root must not contain a preinstalled license JSON.");
        }
        finally
        {
            SqliteConnectionFactory.ClearAllPools();
            try { Directory.Delete(tempRoot, true); } catch { /* best effort */ }
        }
    }

    private static long ScalarLong(SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        object? value = cmd.ExecuteScalar();
        return value is long l ? l : Convert.ToInt64(value);
    }

    private static void RequireOrder(string text, string first, string second, string message)
    {
        int a = text.IndexOf(first, StringComparison.Ordinal);
        int b = text.IndexOf(second, StringComparison.Ordinal);
        if (a < 0 || b < 0 || a >= b)
            throw new InvalidOperationException(message + " Order failed: " + first + " then " + second);
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

    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
            throw new InvalidOperationException(message + " Missing: " + value);
    }

    private static void RequireNotContains(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(message + " Forbidden: " + value);
    }

    private static void RequireTrue(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
