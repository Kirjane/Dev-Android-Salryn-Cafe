using System.Text.Json;
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
 public static void Run(){string root=Path.Combine(Path.GetTempPath(),"salryn-pos-b1-"+Guid.NewGuid().ToString("N"));try{
  var c=SalrynLocalComposition.Create(DbPaths.CreateDevelopment(root));c.InitializeDatabaseAsync().GetAwaiter().GetResult();Require(c.DatabaseInitialization.CreateFirstAdmin(new("Backup Store",null,null,null,null,80,"admin","Admin","StrongAdmin123!")).IsSuccess,"bootstrap");
  var svc=new ControlledBackupPackageService(c.DbPaths);long sales=Count(c,"sales"),payments=Count(c,"payments"),moves=Count(c,"stock_movements");var created=svc.Create();Require(created.IsSuccess,"create");string id=created.Value.BackupId;string dir=Path.Combine(c.DbPaths.BackupPath,id);string manifest=Path.Combine(dir,"manifest.json");Require(File.Exists(manifest),"manifest");var json=File.ReadAllText(manifest);Require(json.Contains("Sha256")&&!json.Contains(c.DbPaths.DatabasePath),"manifest hash and relative path");Require(svc.Verify(id).IsSuccess,"verify");Require(!svc.Verify("../escape").IsSuccess,"traversal");Require(!svc.Verify(Path.GetFullPath(manifest)).IsSuccess,"absolute");Require(!svc.Verify("backup-missing").IsSuccess,"unknown");Require(!svc.Restore(id,"wrong").IsSuccess,"confirmation");
  var factory=new SqliteRepositoryFactory(c.ConnectionFactory);var read=factory.CreateStoreSettingsReadRepository();var write=factory.CreateStoreSettingsWriteRepository();var before=read.GetCurrentAsync().Result.Value!;Require(write.UpdateAsync(new StoreSettingsUpdateRequest(before.Id,"Changed",before.BusinessName,before.Address,before.ContactNumber,before.Tin,before.CurrencyLabel,before.ReceiptWidthMm,before.TaxModeEnabled,before.ShiftModeEnabled,before.ReceiptHeader,before.ReceiptFooter,before.PrinterMode)).Result.IsSuccess,"change");Require(svc.Restore(id,SqliteBackupRecoveryService.RestoreConfirmationText).IsSuccess,"restore");var restored=read.GetCurrentAsync().Result.Value!;Require(restored.StoreName=="Backup Store","settings restored");Require(Count(c,"sales")==sales&&Count(c,"payments")==payments&&Count(c,"stock_movements")==moves,"ledger counts");
  var tamper=svc.Create().Value.BackupId;SqliteConnectionFactory.ClearAllPools();File.AppendAllText(Path.Combine(c.DbPaths.BackupPath,tamper,"salryn.db"),"tamper");Require(!svc.Verify(tamper).IsSuccess,"tamper rejected");var missing=svc.Create().Value.BackupId;File.Delete(Path.Combine(c.DbPaths.BackupPath,missing,"manifest.json"));Require(!svc.Verify(missing).IsSuccess,"missing manifest");var missingFile=svc.Create().Value.BackupId;SqliteConnectionFactory.ClearAllPools();File.Delete(Path.Combine(c.DbPaths.BackupPath,missingFile,"salryn.db"));Require(!svc.Verify(missingFile).IsSuccess,"missing file");
  string endpoints=File.ReadAllText(Path.Combine(FindRoot().FullName,"src","Salryn.Api.Local","LocalBackupPackageEndpoints.cs"));Require(endpoints.Contains("BackupManage"),"permission guard");
 }finally{SqliteConnectionFactory.ClearAllPools();if(Directory.Exists(root))Directory.Delete(root,true);}}
 private static long Count(SalrynLocalComposition c,string t){using var x=c.ConnectionFactory.CreateConnection();x.Open();using var q=x.CreateCommand();q.CommandText=$"SELECT COUNT(*) FROM {t}";return Convert.ToInt64(q.ExecuteScalar());}
 private static DirectoryInfo FindRoot(){DirectoryInfo? d=new(AppContext.BaseDirectory);while(d!=null&&!File.Exists(Path.Combine(d.FullName,"Salryn.sln")))d=d.Parent;return d!;}
 private static void Require(bool b,string m){if(!b)throw new InvalidOperationException(m);}
}
