using System.Security.Cryptography;
using System.Text.Json;
using Salryn.Core.Common;
using Salryn.Core.Services;
using Salryn.Infrastructure.Paths;

namespace Salryn.Infrastructure.Data.Services;

public sealed record ControlledBackupFile(string RelativePath, long SizeBytes, string Sha256);
public sealed record ControlledBackupManifest(string BackupId, DateTimeOffset CreatedAt, int SchemaVersion, IReadOnlyList<ControlledBackupFile> Files);
public sealed record ControlledBackupSummary(string BackupId, DateTimeOffset CreatedAt, long SizeBytes);
public sealed record ControlledBackupVerification(string BackupId, ControlledBackupManifest Manifest, BackupVerificationSummary Database);

public sealed class ControlledBackupPackageService
{
    private const string DatabaseName = "salryn.db";
    private const string ManifestName = "manifest.json";
    private readonly DbPaths _paths;
    private readonly SqliteBackupRecoveryService _database;
    public ControlledBackupPackageService(DbPaths paths){_paths=paths;_database=new(paths);}

    public Result<ControlledBackupSummary> Create()
    {
        Result<string> backup=_database.CreateBackup(); if(backup.IsFailure)return Result<ControlledBackupSummary>.Fail(backup.ErrorDetail);
        string id="backup-"+DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..8];
        string dir=ResolveNew(id); Directory.CreateDirectory(dir); string db=Path.Combine(dir,DatabaseName);
        File.Copy(backup.Value,db,overwrite:false);
        SqliteConnectionFactory.ClearAllPools();
        try { File.Delete(backup.Value); } catch (IOException) { }
        FileInfo info=new(db); string hash=Hash(db);
        var manifest=new ControlledBackupManifest(id,DateTimeOffset.UtcNow,ReadVersion(db),new[]{new ControlledBackupFile(DatabaseName,info.Length,hash)});
        File.WriteAllText(Path.Combine(dir,ManifestName),JsonSerializer.Serialize(manifest,new JsonSerializerOptions{WriteIndented=true}));
        return Result<ControlledBackupSummary>.Success(new(id,manifest.CreatedAt,info.Length));
    }

    public Result<IReadOnlyList<ControlledBackupSummary>> List()
    { try { Directory.CreateDirectory(_paths.BackupPath); return Result<IReadOnlyList<ControlledBackupSummary>>.Success(Directory.EnumerateDirectories(_paths.BackupPath,"backup-*",SearchOption.TopDirectoryOnly).Select(Path.GetFileName).Where(x=>x is not null).Select(x=>Verify(x!).IsSuccess?Verify(x!).Value:null).Where(x=>x is not null).Select(x=>new ControlledBackupSummary(x!.BackupId,x.Manifest.CreatedAt,x.Database.SizeBytes)).ToArray()); } catch(Exception e){return Result<IReadOnlyList<ControlledBackupSummary>>.Fail(Error.Failure("BackupPackage.ListFailed",e.Message));}}

    public Result<ControlledBackupVerification> Verify(string id)
    {
        Result<string> dirResult=ResolveExisting(id); if(dirResult.IsFailure)return Result<ControlledBackupVerification>.Fail(dirResult.ErrorDetail); string dir=dirResult.Value;
        try { string mp=Path.Combine(dir,ManifestName); if(!File.Exists(mp))return Fail("BackupPackage.ManifestMissing","Backup manifest is missing.");
            ControlledBackupManifest? m=JsonSerializer.Deserialize<ControlledBackupManifest>(File.ReadAllText(mp)); if(m is null||m.BackupId!=id||m.Files.Count!=1)return Fail("BackupPackage.ManifestInvalid","Backup manifest is malformed.");
            ControlledBackupFile f=m.Files[0]; if(f.RelativePath!=DatabaseName||Path.IsPathRooted(f.RelativePath)||f.RelativePath.Contains(".."))return Fail("BackupPackage.ManifestPathInvalid","Manifest file path is invalid.");
            string db=Path.GetFullPath(Path.Combine(dir,f.RelativePath)); if(!Inside(dir,db)||!File.Exists(db))return Fail("BackupPackage.FileMissing","Backup database is missing."); FileInfo info=new(db);
            if(info.Length!=f.SizeBytes||!string.Equals(Hash(db),f.Sha256,StringComparison.OrdinalIgnoreCase))return Fail("BackupPackage.HashMismatch","Backup hash validation failed.");
            Result<BackupVerificationSummary> v=_database.VerifyBackup(db); if(v.IsFailure||!v.Value.IsRestorable)return Fail("BackupPackage.DatabaseInvalid","Backup database validation failed.");
            return Result<ControlledBackupVerification>.Success(new(id,m,v.Value));
        } catch(JsonException){return Fail("BackupPackage.ManifestInvalid","Backup manifest is malformed.");} catch(Exception e) when(e is IOException or UnauthorizedAccessException){return Fail("BackupPackage.VerifyFailed",e.Message);}
    }

    public Result<BackupRestoreResult> Restore(string id,string confirmation)
    { Result<ControlledBackupVerification> v=Verify(id); return v.IsFailure?Result<BackupRestoreResult>.Fail(v.ErrorDetail):_database.RestoreVerifiedBackup(v.Value.Database,confirmation); }
    private Result<string> ResolveExisting(string id){if(string.IsNullOrWhiteSpace(id)||Path.IsPathRooted(id)||id.Contains("..")||id.IndexOfAny(new[]{Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar})>=0)return Result<string>.Fail(Error.Validation("BackupPackage.InvalidId","Backup ID is invalid."));string p=Path.GetFullPath(Path.Combine(_paths.BackupPath,id));return Inside(_paths.BackupPath,p)&&Directory.Exists(p)?Result<string>.Success(p):Result<string>.Fail(Error.NotFound("BackupPackage.NotFound","Backup package was not found."));}
    private string ResolveNew(string id){Directory.CreateDirectory(_paths.BackupPath);string p=Path.GetFullPath(Path.Combine(_paths.BackupPath,id));if(!Inside(_paths.BackupPath,p))throw new InvalidOperationException("Backup path escaped controlled root.");return p;}
    private static bool Inside(string root,string path){string r=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;return path.StartsWith(r,StringComparison.OrdinalIgnoreCase);}
    private static string Hash(string p){using var s=File.OpenRead(p);return Convert.ToHexString(SHA256.HashData(s));}
    private static int ReadVersion(string p){using var c=new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={p};Mode=ReadOnly");c.Open();using var q=c.CreateCommand();q.CommandText="SELECT COALESCE(MAX(version),0) FROM schema_migrations";return Convert.ToInt32(q.ExecuteScalar());}
    private static Result<ControlledBackupVerification> Fail(string c,string m)=>Result<ControlledBackupVerification>.Fail(Error.Validation(c,m));
}
