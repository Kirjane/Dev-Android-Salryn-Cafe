using Salryn.Composition.Local;
using Salryn.Core.Repositories;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Bootstrap;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class PosB1BackupRestoreSafetyChecks
{
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "salryn-pos-b1-" + Guid.NewGuid().ToString("N"));
        try
        {
            var c = SalrynLocalComposition.Create(DbPaths.CreateDevelopment(root));
            c.InitializeDatabaseAsync().GetAwaiter().GetResult();
            Require(c.DatabaseInitialization.CreateFirstAdmin(new("Backup Store", null, null, null, null, 80, "admin", "Admin", "StrongAdmin123!")).IsSuccess, "bootstrap");
            var svc = new ControlledBackupPackageService(c.DbPaths);
            long sales = Count(c, "sales"), payments = Count(c, "payments"), moves = Count(c, "stock_movements");
            var created = svc.Create();
            Require(created.IsSuccess, "create");
            string id = created.Value.BackupId;
            string dir = Path.Combine(c.DbPaths.BackupPath, id);
            string manifest = Path.Combine(dir, "manifest.json");
            Require(File.Exists(manifest), "manifest");
            var json = File.ReadAllText(manifest);
            Require(json.Contains("Sha256") && !json.Contains(c.DbPaths.DatabasePath), "manifest hash and relative path");
            Require(svc.Verify(id).IsSuccess, "verify");
            Require(!svc.Verify("../escape").IsSuccess, "traversal");
            Require(!svc.Verify(Path.GetFullPath(manifest)).IsSuccess, "absolute");
            Require(!svc.Verify("backup-missing").IsSuccess, "unknown");
            Require(!svc.Restore(id, "wrong").IsSuccess, "confirmation");
            var factory = new SqliteRepositoryFactory(c.ConnectionFactory);
            var read = factory.CreateStoreSettingsReadRepository();
            var write = factory.CreateStoreSettingsWriteRepository();
            var before = read.GetCurrentAsync().Result.Value!;
            Require(write.UpdateAsync(new StoreSettingsUpdateRequest(before.Id, "Changed", before.BusinessName, before.Address, before.ContactNumber, before.Tin, before.CurrencyLabel, before.ReceiptWidthMm, before.TaxModeEnabled, before.ShiftModeEnabled, before.ReceiptHeader, before.ReceiptFooter, before.PrinterMode)).Result.IsSuccess, "change");
            Require(svc.Restore(id, SqliteBackupRecoveryService.RestoreConfirmationText).IsSuccess, "restore");
            var restored = read.GetCurrentAsync().Result.Value!;
            Require(restored.StoreName == "Backup Store", "settings restored");
            Require(Count(c, "sales") == sales && Count(c, "payments") == payments && Count(c, "stock_movements") == moves, "ledger counts");
            var tamper = svc.Create().Value.BackupId;
            SqliteConnectionFactory.ClearAllPools();
            File.AppendAllText(Path.Combine(c.DbPaths.BackupPath, tamper, "salryn.db"), "tamper");
            Require(!svc.Verify(tamper).IsSuccess, "tamper rejected");
            var missing = svc.Create().Value.BackupId;
            File.Delete(Path.Combine(c.DbPaths.BackupPath, missing, "manifest.json"));
            Require(!svc.Verify(missing).IsSuccess, "missing manifest");
            var missingFile = svc.Create().Value.BackupId;
            SqliteConnectionFactory.ClearAllPools();
            File.Delete(Path.Combine(c.DbPaths.BackupPath, missingFile, "salryn.db"));
            Require(!svc.Verify(missingFile).IsSuccess, "missing file");
            string endpoints = File.ReadAllText(Path.Combine(FindRoot().FullName, "src", "Salryn.Api.Local", "LocalBackupPackageEndpoints.cs"));
            Require(endpoints.Contains("BackupManage"), "permission guard");
            RunFrontendSourceGuards();
        }
        finally
        {
            SqliteConnectionFactory.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static void RunFrontendSourceGuards()
    {
        DirectoryInfo root = FindRoot();
        string webRoot = Path.Combine(root.FullName, "src", "Salryn.App.Web", "src");
        string navigation = Read(webRoot, "navigation.ts");
        string permissions = Read(webRoot, "security", "permissions.ts");
        string app = Read(webRoot, "App.tsx");
        string shell = Read(webRoot, "components", "AppShell.tsx");
        string backupApi = Read(webRoot, "api", "backups.ts");
        string backupPage = Read(webRoot, "pages", "BackupRestorePage.tsx");

        Require(navigation.Contains("id: \"backup-restore\", label: \"Backup & Restore\""), "Backup & Restore must be visible as an admin nav item.");
        Require(navigation.Contains("group: \"Admin\""), "Backup & Restore must live in admin tools, not cashier navigation.");
        Require(permissions.Contains("BackupManage: \"Backup.Manage\""), "Frontend permission catalog must include Backup.Manage.");
        Require(permissions.Contains("\"backup-restore\": [Permissions.BackupManage]"), "Backup page must require Backup.Manage.");
        Require(permissions.Contains("const cashierPages: readonly AppPage[] = [\"checkout\", \"sales\"]"), "Cashier page allowlist must not include Backup & Restore.");
        Require(!permissions.Contains("managerPages") || !permissions.Contains("\"backup-restore\", \"audit\""), "Manager allowlist must not include Backup & Restore.");
        Require(app.Contains("BackupRestorePage"), "App must render the Backup & Restore page.");
        Require(shell.Contains("\"Backup & Restore\""), "Shell icon mapping must include Backup & Restore.");
        Require(!shell.Contains("item.id === \"backup-restore\""), "Shell must not show an Admin badge on Backup & Restore.");

        Require(backupApi.Contains("/api/backups"), "Backup UI API client must use existing backend backup endpoints.");
        Require(backupApi.Contains("encodeURIComponent(backupId)"), "Backup UI API client must call verify/restore by encoded backup ID.");
        Require(backupApi.Contains("RESTORE SALRYN BACKUP"), "Backup UI API client must send backend confirmation text only after UI confirmation.");
        Require(backupPage.Contains("Create Backup"), "Backup UI must expose create action.");
        Require(backupPage.Contains("Known backup packages"), "Backup UI must list known backup IDs.");
        Require(backupPage.Contains("Verify"), "Backup UI must expose verify action.");
        Require(backupPage.Contains("Type RESTORE"), "Backup UI restore must require typed confirmation.");
        Require(backupPage.Contains("Restore replaces the current local database"), "Backup UI must warn destructive restore replacement.");
        Require(!backupPage.Contains("not cloud backup"), "Backup UI must not show a cloud disclaimer wall.");
        Require(!backupPage.Contains("installer migration proof"), "Backup UI must not show an installer disclaimer wall.");
        Require(!backupPage.Contains("type=\"file\""), "Backup UI must not expose raw file upload.");
        Require(!backupPage.Contains("FilePath"), "Backup UI must not display raw backup file paths.");
        Require(!backupPage.Contains("RestoredBackupPath"), "Backup UI must not display restored backup file paths.");
        Require(!backupPage.Contains("SafetyBackupPath"), "Backup UI must not display safety backup file paths.");
    }

    private static long Count(SalrynLocalComposition c, string t)
    {
        using var x = c.ConnectionFactory.CreateConnection();
        x.Open();
        using var q = x.CreateCommand();
        q.CommandText = $"SELECT COUNT(*) FROM {t}";
        return Convert.ToInt64(q.ExecuteScalar());
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));

    private static DirectoryInfo FindRoot()
    {
        DirectoryInfo? d = new(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "Salryn.sln"))) d = d.Parent;
        return d!;
    }

    private static void Require(bool b, string m)
    {
        if (!b) throw new InvalidOperationException(m);
    }
}
