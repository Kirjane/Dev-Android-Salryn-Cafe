using Salryn.Composition.Local;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

/// <summary>
/// Fully code-proven backup restore phrase + backend accept/reject against temporary fixture only.
/// </summary>
public static class PreICodeProvenBackupRestoreChecks
{
    public static void Run()
    {
        string root = PreICodeProvenCheckSupport.FindRoot();
        string backupPage = PreICodeProvenCheckSupport.Read(root, "src", "Salryn.App.Web", "src", "pages", "BackupRestorePage.tsx");
        string backupApi = PreICodeProvenCheckSupport.Read(root, "src", "Salryn.App.Web", "src", "api", "backups.ts");
        string recovery = PreICodeProvenCheckSupport.Read(root, "src", "Salryn.Infrastructure", "Data", "Services", "SqliteBackupRecoveryService.cs");

        AssertPhraseAndLabelContracts(backupPage, backupApi, recovery);
        RunBackendPhraseBehavior();
    }

    private static void AssertPhraseAndLabelContracts(string backupPage, string backupApi, string recovery)
    {
        const string phrase = "RESTORE SALRYN BACKUP";

        PreICodeProvenCheckSupport.RequireContains(
            recovery,
            "RestoreConfirmationText = \"" + phrase + "\"",
            "Backend restore phrase constant must be exact.");
        PreICodeProvenCheckSupport.RequireContains(
            backupApi,
            "RESTORE_CONFIRMATION_INPUT = \"" + phrase + "\"",
            "Web API client constant must match backend phrase.");
        PreICodeProvenCheckSupport.RequireContains(
            backupPage,
            "Type RESTORE SALRYN BACKUP to enable restore",
            "UI must require the full restore phrase.");
        PreICodeProvenCheckSupport.RequireContains(
            backupPage,
            "restoreBackup(restoreTarget, restoreText)",
            "UI must send user-entered phrase unchanged (not a silent constant substitution).");
        PreICodeProvenCheckSupport.RequireContains(
            backupApi,
            "confirmation",
            "API client payload must include confirmation field.");
        PreICodeProvenCheckSupport.RequireContains(
            backupPage,
            "Backup format version",
            "User-facing backup format label must be friendly.");
        PreICodeProvenCheckSupport.RequireNotContains(
            backupPage,
            "Schema / app migration",
            "Normal user-facing backup UI must not show Schema / app migration label.");
        PreICodeProvenCheckSupport.RequireTrue(
            SqliteBackupRecoveryService.RestoreConfirmationText == phrase,
            "Runtime RestoreConfirmationText must equal RESTORE SALRYN BACKUP.");
    }

    private static void RunBackendPhraseBehavior()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "salryn-code-proven-restore-" + Guid.NewGuid().ToString("N"));
        try
        {
            SalrynLocalComposition composition = SalrynLocalComposition.Create(DbPaths.CreateDevelopment(tempRoot));
            composition.InitializeDatabaseAsync().GetAwaiter().GetResult();
            PreICodeProvenCheckSupport.RequireTrue(
                composition.DatabaseInitialization.CreateFirstAdmin(
                    new("Code Proven Backup Store", null, null, null, null, 80, "admin", "Admin", "StrongAdmin123!"))
                    .IsSuccess,
                "Temporary backup fixture bootstrap must succeed.");

            ControlledBackupPackageService service = new(composition.DbPaths);
            var created = service.Create();
            PreICodeProvenCheckSupport.RequireTrue(
                created.IsSuccess,
                "Temporary backup create must succeed: " + (created.IsFailure ? created.ErrorDetail.Message : ""));
            string backupId = created.Value.BackupId;

            PreICodeProvenCheckSupport.RequireTrue(
                !service.Restore(backupId, "RESTORE").IsSuccess,
                "Backend must reject short phrase RESTORE.");
            PreICodeProvenCheckSupport.RequireTrue(
                !service.Restore(backupId, "RESTORE BACKUP").IsSuccess,
                "Backend must reject incomplete phrase RESTORE BACKUP.");
            PreICodeProvenCheckSupport.RequireTrue(
                !service.Restore(backupId, string.Empty).IsSuccess,
                "Backend must reject empty restore confirmation.");
            PreICodeProvenCheckSupport.RequireTrue(
                !service.Restore(backupId, "   ").IsSuccess,
                "Backend must reject whitespace-only restore confirmation.");
            PreICodeProvenCheckSupport.RequireTrue(
                service.Restore(backupId, SqliteBackupRecoveryService.RestoreConfirmationText).IsSuccess,
                "Backend must accept exact RESTORE SALRYN BACKUP against temporary fixture.");
        }
        finally
        {
            SqliteConnectionFactory.ClearAllPools();
            try { Directory.Delete(tempRoot, recursive: true); } catch { /* best effort */ }
        }
    }
}
