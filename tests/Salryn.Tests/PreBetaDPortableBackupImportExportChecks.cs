using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Salryn.Composition.Local;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

/// <summary>
/// PRE-BETA-D: portable .salryn-backup.zip download/import, verify, corrupt reject, future reject, restore archive.
/// </summary>
public static class PreBetaDPortableBackupImportExportChecks
{
    public static void Run()
    {
        string repoRoot = FindRoot();
        string endpoints = File.ReadAllText(Path.Combine(repoRoot, "src", "Salryn.Api.Local", "LocalBackupPackageEndpoints.cs"));
        string service = File.ReadAllText(Path.Combine(repoRoot, "src", "Salryn.Infrastructure", "Data", "Services", "ControlledBackupPackageService.cs"));
        string page = File.ReadAllText(Path.Combine(repoRoot, "src", "Salryn.App.Web", "src", "pages", "BackupRestorePage.tsx"));
        string api = File.ReadAllText(Path.Combine(repoRoot, "src", "Salryn.App.Web", "src", "api", "backups.ts"));

        RequireContains(endpoints, "/api/backups/{id}/download", "Download endpoint required.");
        RequireContains(endpoints, "/api/backups/import", "Import endpoint required.");
        RequireContains(service, "PortablePackageExtension", "Portable package extension required.");
        RequireContains(service, ".salryn-backup.zip", "Portable package must use .salryn-backup.zip.");
        RequireContains(service, "ExportPortable", "ExportPortable required.");
        RequireContains(service, "ImportPortable", "ImportPortable required.");
        RequireContains(service, "MaxPortableImportBytes", "Import size limit required.");
        RequireContains(page, "Download Backup File", "UI Download Backup File label.");
        RequireContains(page, "Import Backup File", "UI Import Backup File label.");
        RequireContains(page, "Verify Backup", "UI Verify Backup label.");
        RequireContains(page, "Restore Backup", "UI Restore Backup label.");
        RequireContains(page, "Type RESTORE", "Typed RESTORE confirmation required.");
        RequireContains(api, "downloadBackupFileUrl", "Client download helper required.");
        RequireContains(api, "importBackupFile", "Client import helper required.");

        string root = Path.Combine(Path.GetTempPath(), "salryn-pre-d-" + Guid.NewGuid().ToString("N"));
        try
        {
            var composition = SalrynLocalComposition.Create(DbPaths.CreateDevelopment(root));
            composition.InitializeDatabaseAsync().GetAwaiter().GetResult();
            RequireTrue(
                composition.DatabaseInitialization.CreateFirstAdmin(
                    new("Portable Backup Store", null, null, null, null, 80, "admin", "Admin", "StrongAdmin123!")).IsSuccess,
                "bootstrap admin");

            var svc = new ControlledBackupPackageService(composition.DbPaths);
            var created = svc.Create();
            RequireTrue(created.IsSuccess, "create backup: " + (created.IsFailure ? created.ErrorDetail.Message : ""));
            string id = created.Value.BackupId;

            var verified = svc.Verify(id);
            RequireTrue(verified.IsSuccess, "verify created package");
            RequireTrue(verified.Value.Database.IsRestorable, "created package restorable");
            RequireTrue(
                string.Equals(verified.Value.StoreName, "Portable Backup Store", StringComparison.Ordinal),
                "store name in verification summary");

            var export = svc.ExportPortable(id);
            RequireTrue(export.IsSuccess, "export portable: " + (export.IsFailure ? export.ErrorDetail.Message : ""));
            RequireTrue(File.Exists(export.Value.ZipPath), "export zip exists");
            RequireTrue(export.Value.FileName.EndsWith(".salryn-backup.zip", StringComparison.OrdinalIgnoreCase), "zip extension");

            // Import round-trip
            using (FileStream zipStream = File.OpenRead(export.Value.ZipPath))
            {
                var imported = svc.ImportPortable(zipStream, zipStream.Length);
                RequireTrue(imported.IsSuccess, "import portable: " + (imported.IsFailure ? imported.ErrorDetail.Message : ""));
                RequireTrue(imported.Value.Database.IsRestorable, "imported package restorable");
                RequireTrue(imported.Value.BackupId != id, "imported package gets new local id");
            }

            // Corrupt package rejected
            string corruptZip = Path.Combine(root, "corrupt.salryn-backup.zip");
            File.WriteAllBytes(corruptZip, Encoding.UTF8.GetBytes("not-a-zip"));
            using (FileStream bad = File.OpenRead(corruptZip))
            {
                var corrupt = svc.ImportPortable(bad, bad.Length);
                RequireTrue(corrupt.IsFailure, "corrupt package must fail");
            }

            // Checksum mismatch rejected
            string tamperRoot = Path.Combine(root, "tamper-export");
            Directory.CreateDirectory(tamperRoot);
            string goodDir = Path.Combine(composition.DbPaths.BackupPath, id);
            string tamperZip = Path.Combine(tamperRoot, "tamper.salryn-backup.zip");
            File.Copy(export.Value.ZipPath, tamperZip, overwrite: true);
            // Rewrite zip with mutated db while keeping original manifest hash
            string extract = Path.Combine(tamperRoot, "x");
            Directory.CreateDirectory(extract);
            ZipFile.ExtractToDirectory(tamperZip, extract, overwriteFiles: true);
            File.AppendAllText(Path.Combine(extract, "salryn.db"), "TAMPER");
            if (File.Exists(tamperZip)) File.Delete(tamperZip);
            ZipFile.CreateFromDirectory(extract, tamperZip);
            using (FileStream tampered = File.OpenRead(tamperZip))
            {
                var mismatch = svc.ImportPortable(tampered, tampered.Length);
                RequireTrue(mismatch.IsFailure, "checksum mismatch must fail");
                RequireTrue(
                    mismatch.ErrorDetail.Code.Contains("Hash", StringComparison.OrdinalIgnoreCase) ||
                    mismatch.ErrorDetail.Message.Contains("hash", StringComparison.OrdinalIgnoreCase),
                    "hash failure code/message");
            }

            // Future migration package rejected
            string futureZip = Path.Combine(tamperRoot, "future.salryn-backup.zip");
            string futureExtract = Path.Combine(tamperRoot, "future-x");
            Directory.CreateDirectory(futureExtract);
            ZipFile.ExtractToDirectory(export.Value.ZipPath, futureExtract, overwriteFiles: true);
            string futureDb = Path.Combine(futureExtract, "salryn.db");
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + futureDb))
            {
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO schema_migrations (version, name, applied_at)
                    VALUES ($v, 'future-probe', $ts);
                    """;
                // Probe one version beyond the runtime-supported migration catalog.
                cmd.Parameters.AddWithValue("$v", 30);
                cmd.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToString("O"));
                cmd.ExecuteNonQuery();
            }

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            // Rebuild manifest hash for the future DB so hash check passes and future version check runs
            string futureHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(futureDb)));
            long futureSize = new FileInfo(futureDb).Length;
            var futureManifest = new
            {
                BackupId = "backup-future-probe",
                CreatedAt = DateTimeOffset.UtcNow,
                SchemaVersion = 30,
                Files = new[]
                {
                    new { RelativePath = "salryn.db", SizeBytes = futureSize, Sha256 = futureHash }
                }
            };
            File.WriteAllText(
                Path.Combine(futureExtract, "manifest.json"),
                JsonSerializer.Serialize(futureManifest, new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(futureZip)) File.Delete(futureZip);
            ZipFile.CreateFromDirectory(futureExtract, futureZip);
            using (FileStream futureStream = File.OpenRead(futureZip))
            {
                var future = svc.ImportPortable(futureStream, futureStream.Length);
                RequireTrue(future.IsFailure, "future package must fail");
            }

            // Restore imported package and confirm live DB readable
            var reExport = svc.ExportPortable(id);
            RequireTrue(reExport.IsSuccess, "re-export");
            ControlledBackupVerification importedOk;
            using (FileStream zipStream = File.OpenRead(reExport.Value.ZipPath))
            {
                var imported = svc.ImportPortable(zipStream, zipStream.Length);
                RequireTrue(imported.IsSuccess, "import for restore");
                importedOk = imported.Value;
            }

            // Mutate live store name, then restore
            using (var live = composition.ConnectionFactory.CreateConnection())
            {
                live.Open();
                using var cmd = live.CreateCommand();
                cmd.CommandText = "UPDATE store_settings SET store_name = 'Mutated Live';";
                cmd.ExecuteNonQuery();
            }

            var restore = svc.Restore(importedOk.BackupId, SqliteBackupRecoveryService.RestoreConfirmationText);
            RequireTrue(restore.IsSuccess, "restore imported: " + (restore.IsFailure ? restore.ErrorDetail.Message : ""));
            RequireTrue(File.Exists(restore.Value.SafetyBackupPath), "safety archive of current DB before restore");

            using (var live = composition.ConnectionFactory.CreateConnection())
            {
                live.Open();
                using var cmd = live.CreateCommand();
                cmd.CommandText = "SELECT store_name FROM store_settings LIMIT 1;";
                string? name = cmd.ExecuteScalar()?.ToString();
                RequireTrue(name == "Portable Backup Store", "restored store name from portable package");
            }

            try { File.Delete(export.Value.ZipPath); } catch { /* best effort */ }
            try { File.Delete(reExport.Value.ZipPath); } catch { /* best effort */ }
        }
        finally
        {
            SqliteConnectionFactory.ClearAllPools();
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { /* best effort */ }
        }
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
            throw new InvalidOperationException(message);
    }

    private static void RequireTrue(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
