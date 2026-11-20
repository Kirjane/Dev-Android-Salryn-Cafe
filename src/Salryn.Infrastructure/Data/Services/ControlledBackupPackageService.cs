using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Services;
using Salryn.Infrastructure.Paths;
using Salryn.Infrastructure.Data.Migrations;

namespace Salryn.Infrastructure.Data.Services;

public sealed record ControlledBackupFile(string RelativePath, long SizeBytes, string Sha256);

public sealed record ControlledBackupManifest(
    string BackupId,
    DateTimeOffset CreatedAt,
    int SchemaVersion,
    IReadOnlyList<ControlledBackupFile> Files,
    [property: JsonPropertyName("product_id")] string? ProductId = null);

public sealed record ControlledBackupSummary(string BackupId, DateTimeOffset CreatedAt, long SizeBytes);

public sealed record ControlledBackupVerification(
    string BackupId,
    ControlledBackupManifest Manifest,
    BackupVerificationSummary Database,
    string? StoreName = null,
    bool? HasLogo = null);

public sealed record ControlledBackupPortableExport(
    string BackupId,
    string FileName,
    string ContentType,
    string ZipPath);

public sealed class ControlledBackupPackageService
{
    public const string PortablePackageExtension = ".salryn-backup.zip";
    public const string PortableContentType = "application/zip";
    public const long MaxPortableImportBytes = 100L * 1024 * 1024;

    private const string DatabaseName = "salryn.db";
    private const string ManifestName = "manifest.json";

    private static readonly JsonSerializerOptions ManifestJsonOptions = new() { WriteIndented = true };

    private readonly DbPaths _paths;
    private readonly SqliteBackupRecoveryService _database;

    public ControlledBackupPackageService(DbPaths paths)
    {
        _paths = paths;
        _database = new SqliteBackupRecoveryService(paths);
    }

    public Result<ControlledBackupSummary> Create()
    {
        Result<string> backup = _database.CreateBackup();
        if (backup.IsFailure)
        {
            return Result<ControlledBackupSummary>.Fail(backup.ErrorDetail);
        }

        string id = NewBackupId();
        string dir = ResolveNew(id);
        Directory.CreateDirectory(dir);
        string db = Path.Combine(dir, DatabaseName);
        File.Copy(backup.Value, db, overwrite: false);
        SqliteConnectionFactory.ClearAllPools();
        try { File.Delete(backup.Value); } catch (IOException) { }

        FileInfo info = new(db);
        string hash = Hash(db);
        ControlledBackupManifest manifest = new(
            id,
            DateTimeOffset.UtcNow,
            ReadVersion(db),
            new[] { new ControlledBackupFile(DatabaseName, info.Length, hash) },
            SalrynProductIdentity.ProductId);
        WriteManifest(dir, manifest);
        return Result<ControlledBackupSummary>.Success(new ControlledBackupSummary(id, manifest.CreatedAt, info.Length));
    }

    public Result<IReadOnlyList<ControlledBackupSummary>> List()
    {
        try
        {
            Directory.CreateDirectory(_paths.BackupPath);
            ControlledBackupSummary[] items = Directory
                .EnumerateDirectories(_paths.BackupPath, "backup-*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(x => x is not null)
                .Select(x =>
                {
                    Result<ControlledBackupVerification> verified = Verify(x!);
                    return verified.IsSuccess
                        ? new ControlledBackupSummary(
                            verified.Value.BackupId,
                            verified.Value.Manifest.CreatedAt,
                            verified.Value.Database.SizeBytes)
                        : null;
                })
                .Where(x => x is not null)
                .Select(x => x!)
                .OrderByDescending(x => x.CreatedAt)
                .ToArray();
            return Result<IReadOnlyList<ControlledBackupSummary>>.Success(items);
        }
        catch (Exception e)
        {
            return Result<IReadOnlyList<ControlledBackupSummary>>.Fail(
                Error.Failure("BackupPackage.ListFailed", e.Message));
        }
    }

    public Result<ControlledBackupVerification> Verify(string id)
    {
        Result<string> dirResult = ResolveExisting(id);
        if (dirResult.IsFailure)
        {
            return Result<ControlledBackupVerification>.Fail(dirResult.ErrorDetail);
        }

        string dir = dirResult.Value;
        try
        {
            string mp = Path.Combine(dir, ManifestName);
            if (!File.Exists(mp))
            {
                return Fail("BackupPackage.ManifestMissing", "Backup manifest is missing.");
            }

            ControlledBackupManifest? m = JsonSerializer.Deserialize<ControlledBackupManifest>(File.ReadAllText(mp));
            if (m is null || m.BackupId != id || m.Files.Count != 1)
            {
                return Fail("BackupPackage.ManifestInvalid", "Backup manifest is malformed.");
            }

            ControlledBackupFile f = m.Files[0];
            if (f.RelativePath != DatabaseName || Path.IsPathRooted(f.RelativePath) || f.RelativePath.Contains("..", StringComparison.Ordinal))
            {
                return Fail("BackupPackage.ManifestPathInvalid", "Manifest file path is invalid.");
            }

            string db = Path.GetFullPath(Path.Combine(dir, f.RelativePath));
            if (!Inside(dir, db) || !File.Exists(db))
            {
                return Fail("BackupPackage.FileMissing", "Backup database is missing.");
            }

            FileInfo info = new(db);
            if (info.Length != f.SizeBytes ||
                !string.Equals(Hash(db), f.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return Fail("BackupPackage.HashMismatch", "Backup hash validation failed.");
            }

            Result<BackupVerificationSummary> v = _database.VerifyBackup(db);
            if (v.IsFailure)
            {
                return Fail("BackupPackage.DatabaseInvalid", "Backup database validation failed.");
            }

            if (m.ProductId is not null && !string.Equals(m.ProductId, SalrynProductIdentity.ProductId, StringComparison.Ordinal))
            {
                return Fail("BackupPackage.ProductMismatch", "Backup belongs to a different product.");
            }
            if (m.ProductId is null)
            {
                using var lineageConnection = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = db, Mode = SqliteOpenMode.ReadOnly, Pooling = false
                }.ToString());
                lineageConnection.Open();
                if (SchemaLineageClassifier.Classify(lineageConnection).Kind != SchemaLineageKind.YzaV39Legacy)
                {
                    return Fail("BackupPackage.ProductMissing", "Only verified legacy Cafe Yza packages may omit product_id.");
                }
            }

            if (v.Value.HasFutureMigrationVersion)
            {
                return Fail(
                    "BackupPackage.FutureVersion",
                    "Backup was created by a newer app version and cannot be restored safely.");
            }

            if (!v.Value.IsRestorable)
            {
                return Fail("BackupPackage.DatabaseInvalid", "Backup database validation failed.");
            }

            (string? storeName, bool? hasLogo) = ReadBrandingSummary(db);
            return Result<ControlledBackupVerification>.Success(
                new ControlledBackupVerification(id, m, v.Value, storeName, hasLogo));
        }
        catch (JsonException)
        {
            return Fail("BackupPackage.ManifestInvalid", "Backup manifest is malformed.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Fail("BackupPackage.VerifyFailed", e.Message);
        }
    }

    public Result<BackupRestoreResult> Restore(string id, string confirmation)
    {
        Result<ControlledBackupVerification> v = Verify(id);
        return v.IsFailure
            ? Result<BackupRestoreResult>.Fail(v.ErrorDetail)
            : _database.RestoreVerifiedBackup(v.Value.Database, confirmation);
    }

    /// <summary>
    /// Builds a portable .salryn-backup.zip containing only manifest.json and salryn.db.
    /// Caller should delete ZipPath after streaming when done.
    /// </summary>
    public Result<ControlledBackupPortableExport> ExportPortable(string id)
    {
        Result<ControlledBackupVerification> verified = Verify(id);
        if (verified.IsFailure)
        {
            return Result<ControlledBackupPortableExport>.Fail(verified.ErrorDetail);
        }

        Result<string> dirResult = ResolveExisting(id);
        if (dirResult.IsFailure)
        {
            return Result<ControlledBackupPortableExport>.Fail(dirResult.ErrorDetail);
        }

        try
        {
            string exportRoot = Path.Combine(_paths.BackupPath, "_portable-export");
            Directory.CreateDirectory(exportRoot);
            string zipPath = Path.Combine(exportRoot, id + PortablePackageExtension);
            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            string dir = dirResult.Value;
            using (ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(Path.Combine(dir, ManifestName), ManifestName, CompressionLevel.Optimal);
                archive.CreateEntryFromFile(Path.Combine(dir, DatabaseName), DatabaseName, CompressionLevel.Optimal);
            }

            string fileName = id + PortablePackageExtension;
            return Result<ControlledBackupPortableExport>.Success(
                new ControlledBackupPortableExport(id, fileName, PortableContentType, zipPath));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return Result<ControlledBackupPortableExport>.Fail(
                Error.Failure("BackupPackage.ExportFailed", e.Message));
        }
    }

    /// <summary>
    /// Imports a portable .salryn-backup.zip into the controlled backup package store.
    /// Rejects corrupt packages, path traversal, unexpected entries, hash mismatch, and future schema.
    /// </summary>
    public Result<ControlledBackupVerification> ImportPortable(Stream zipStream, long? declaredLength = null)
    {
        if (zipStream is null)
        {
            return Fail("BackupPackage.ImportRequired", "Backup package file is required.");
        }

        if (declaredLength is > MaxPortableImportBytes)
        {
            return Fail(
                "BackupPackage.ImportTooLarge",
                "Backup package must be 100 MB or smaller.");
        }

        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "salryn-backup-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string tempZip = Path.Combine(tempRoot, "import.zip");
        string extractDir = Path.Combine(tempRoot, "extract");
        Directory.CreateDirectory(extractDir);

        try
        {
            using (FileStream fs = File.Create(tempZip))
            {
                zipStream.CopyTo(fs);
            }

            FileInfo zipInfo = new(tempZip);
            if (zipInfo.Length <= 0)
            {
                return Fail("BackupPackage.ImportRequired", "Backup package file is empty.");
            }

            if (zipInfo.Length > MaxPortableImportBytes)
            {
                return Fail(
                    "BackupPackage.ImportTooLarge",
                    "Backup package must be 100 MB or smaller.");
            }

            using (ZipArchive archive = ZipFile.OpenRead(tempZip))
            {
                if (archive.Entries.Count == 0)
                {
                    return Fail("BackupPackage.ImportCorrupt", "Backup package is empty or corrupt.");
                }

                HashSet<string> allowed = new(StringComparer.OrdinalIgnoreCase) { ManifestName, DatabaseName };
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name) && entry.FullName.EndsWith("/", StringComparison.Ordinal))
                    {
                        // Directory marker — ignore empty dirs, reject nested content.
                        if (!string.Equals(entry.FullName, "/", StringComparison.Ordinal))
                        {
                            string dirName = entry.FullName.TrimEnd('/');
                            if (dirName.Contains('/') || dirName.Contains('\\') || dirName.Contains("..", StringComparison.Ordinal))
                            {
                                return Fail(
                                    "BackupPackage.ImportPathInvalid",
                                    "Backup package contains unsupported nested paths.");
                            }
                        }

                        continue;
                    }

                    string name = Path.GetFileName(entry.FullName.Replace('\\', '/'));
                    if (!allowed.Contains(name) ||
                        entry.FullName.Contains("..", StringComparison.Ordinal) ||
                        entry.FullName.Contains('/') && !string.Equals(entry.FullName, name, StringComparison.OrdinalIgnoreCase) ||
                        entry.FullName.Contains('\\'))
                    {
                        // Allow only flat entries named manifest.json or salryn.db
                        string normalized = entry.FullName.Replace('\\', '/').TrimStart('/');
                        if (!allowed.Contains(normalized) || normalized.Contains('/'))
                        {
                            return Fail(
                                "BackupPackage.ImportUnexpectedEntry",
                                "Backup package may only contain manifest.json and salryn.db.");
                        }
                    }

                    if (entry.Length > MaxPortableImportBytes)
                    {
                        return Fail(
                            "BackupPackage.ImportTooLarge",
                            "Backup package must be 100 MB or smaller.");
                    }

                    string dest = Path.GetFullPath(Path.Combine(extractDir, name));
                    if (!Inside(extractDir, dest))
                    {
                        return Fail(
                            "BackupPackage.ImportPathInvalid",
                            "Backup package path escaped extract root.");
                    }

                    entry.ExtractToFile(dest, overwrite: true);
                }
            }

            string extractedManifest = Path.Combine(extractDir, ManifestName);
            string extractedDb = Path.Combine(extractDir, DatabaseName);
            if (!File.Exists(extractedManifest) || !File.Exists(extractedDb))
            {
                return Fail(
                    "BackupPackage.ImportCorrupt",
                    "Backup package must include manifest.json and salryn.db.");
            }

            ControlledBackupManifest? importedManifest;
            try
            {
                importedManifest = JsonSerializer.Deserialize<ControlledBackupManifest>(
                    File.ReadAllText(extractedManifest));
            }
            catch (JsonException)
            {
                return Fail("BackupPackage.ManifestInvalid", "Backup manifest is malformed.");
            }

            if (importedManifest is null || importedManifest.Files.Count != 1)
            {
                return Fail("BackupPackage.ManifestInvalid", "Backup manifest is malformed.");
            }

            ControlledBackupFile declared = importedManifest.Files[0];
            if (declared.RelativePath != DatabaseName)
            {
                return Fail("BackupPackage.ManifestPathInvalid", "Manifest file path is invalid.");
            }

            FileInfo dbInfo = new(extractedDb);
            string actualHash = Hash(extractedDb);
            if (dbInfo.Length != declared.SizeBytes ||
                !string.Equals(actualHash, declared.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return Fail("BackupPackage.HashMismatch", "Backup hash validation failed.");
            }

            // Assign a local package id (avoid collisions / path overwrite).
            string localId = NewBackupId();
            string packageDir = ResolveNew(localId);
            Directory.CreateDirectory(packageDir);
            string finalDb = Path.Combine(packageDir, DatabaseName);
            File.Copy(extractedDb, finalDb, overwrite: false);

            ControlledBackupManifest localManifest = new(
                localId,
                importedManifest.CreatedAt == default ? DateTimeOffset.UtcNow : importedManifest.CreatedAt,
                importedManifest.SchemaVersion > 0 ? importedManifest.SchemaVersion : ReadVersion(finalDb),
                new[] { new ControlledBackupFile(DatabaseName, dbInfo.Length, actualHash) },
                importedManifest.ProductId);
            WriteManifest(packageDir, localManifest);

            Result<ControlledBackupVerification> verified = Verify(localId);
            if (verified.IsFailure)
            {
                try
                {
                    Directory.Delete(packageDir, recursive: true);
                }
                catch (IOException)
                {
                    // best effort cleanup
                }

                return verified;
            }

            return verified;
        }
        catch (InvalidDataException)
        {
            return Fail("BackupPackage.ImportCorrupt", "Backup package is corrupt or not a valid zip.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Fail("BackupPackage.ImportFailed", e.Message);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
            catch (IOException)
            {
                // best effort
            }
        }
    }

    private static string NewBackupId() =>
        "backup-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];

    private static void WriteManifest(string dir, ControlledBackupManifest manifest) =>
        File.WriteAllText(
            Path.Combine(dir, ManifestName),
            JsonSerializer.Serialize(manifest, ManifestJsonOptions));

    private static (string? StoreName, bool? HasLogo) ReadBrandingSummary(string databasePath)
    {
        try
        {
            using SqliteConnection connection = new($"Data Source={databasePath};Mode=ReadOnly");
            connection.Open();
            using SqliteCommand tables = connection.CreateCommand();
            tables.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='store_settings';";
            if (tables.ExecuteScalar() is null)
            {
                return (null, null);
            }

            HashSet<string> cols = new(StringComparer.OrdinalIgnoreCase);
            using (SqliteCommand pragma = connection.CreateCommand())
            {
                pragma.CommandText = "PRAGMA table_info(store_settings);";
                using SqliteDataReader reader = pragma.ExecuteReader();
                while (reader.Read())
                {
                    cols.Add(reader.GetString(1));
                }
            }

            bool hasLogoColumn = cols.Contains("logo_bytes");
            string sql = hasLogoColumn
                ? "SELECT store_name, CASE WHEN logo_bytes IS NOT NULL AND length(logo_bytes) > 0 THEN 1 ELSE 0 END FROM store_settings LIMIT 1;"
                : "SELECT store_name, 0 FROM store_settings LIMIT 1;";
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            using SqliteDataReader row = command.ExecuteReader();
            if (!row.Read())
            {
                return (null, null);
            }

            string? storeName = row.IsDBNull(0) ? null : row.GetString(0);
            bool hasLogo = !row.IsDBNull(1) && Convert.ToInt32(row.GetValue(1)) == 1;
            return (storeName, hasLogo);
        }
        catch
        {
            return (null, null);
        }
    }

    private Result<string> ResolveExisting(string id)
    {
        if (string.IsNullOrWhiteSpace(id) ||
            Path.IsPathRooted(id) ||
            id.Contains("..", StringComparison.Ordinal) ||
            id.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) >= 0)
        {
            return Result<string>.Fail(Error.Validation("BackupPackage.InvalidId", "Backup ID is invalid."));
        }

        string p = Path.GetFullPath(Path.Combine(_paths.BackupPath, id));
        return Inside(_paths.BackupPath, p) && Directory.Exists(p)
            ? Result<string>.Success(p)
            : Result<string>.Fail(Error.NotFound("BackupPackage.NotFound", "Backup package was not found."));
    }

    private string ResolveNew(string id)
    {
        Directory.CreateDirectory(_paths.BackupPath);
        string p = Path.GetFullPath(Path.Combine(_paths.BackupPath, id));
        if (!Inside(_paths.BackupPath, p))
        {
            throw new InvalidOperationException("Backup path escaped controlled root.");
        }

        return p;
    }

    private static bool Inside(string root, string path)
    {
        string r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(r, StringComparison.OrdinalIgnoreCase);
    }

    private static string Hash(string p)
    {
        using FileStream s = File.OpenRead(p);
        return Convert.ToHexString(SHA256.HashData(s));
    }

    private static int ReadVersion(string p)
    {
        using SqliteConnection c = new($"Data Source={p};Mode=ReadOnly");
        c.Open();
        using SqliteCommand q = c.CreateCommand();
        q.CommandText = "SELECT COALESCE(MAX(version),0) FROM schema_migrations";
        return Convert.ToInt32(q.ExecuteScalar());
    }

    private static Result<ControlledBackupVerification> Fail(string c, string m) =>
        Result<ControlledBackupVerification>.Fail(Error.Validation(c, m));
}
