using Microsoft.Data.Sqlite;
using Salryn.Api.Local.Services;
using Salryn.Composition.Local;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Paths;

namespace Salryn.Tests;

public static class W4CashDrawerReconciliationChecks
{
    public static void Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"salryn-w4-"+Guid.NewGuid().ToString("N"));
        try{SalrynLocalComposition c=SalrynLocalComposition.Create(DbPaths.CreateDevelopment(root));Require(c.DatabaseInitialization.InitializeDatabase().IsSuccess,"v25 initialization must pass");Guid user=SeedUser(c);HostOperatorApiServices host=HostOperatorApiServices.Create(c);CashDrawerReconciliationService drawer=new(c);
            var opened=host.ShiftService.OpenShiftAsync(user,10_000).GetAwaiter().GetResult();Require(opened.Ok,"nonzero opening float must open");string session=opened.Shift!.ShiftId!;
            var cashIn=drawer.RecordMovement(user,new("CashIn",2_000,"in-1","Documented float add"));Require(cashIn.IsSuccess&&cashIn.Value.ExpectedCashCents==12_000,"cash-in must increase expected");
            var replay=drawer.RecordMovement(user,new("CashIn",2_000,"in-1","Documented float add"));Require(replay.IsSuccess&&Count(c,"cash_drawer_events")==1,"movement replay must not duplicate");
            Require(drawer.RecordMovement(user,new("CashIn",2_001,"in-1","Documented float add")).IsFailure,"movement request replay with changed data must fail");
            Require(drawer.RecordMovement(user,new("CashOut",500,"out-1","Documented payout")).IsSuccess,"cash-out must record");Require(drawer.RecordMovement(user,new("CashRefund",300,"refund-1","Actual cash refund")).IsSuccess,"actual refund must record");
            Require(drawer.RecordMovement(user,new("CashIn",-1,"bad","bad")).IsFailure,"negative movement must fail");
            var close=drawer.Close(user,new(11_100,true,"close-1","Physical count"));Require(close.IsSuccess&&close.Value.ExpectedCashCents==11_200&&close.Value.VarianceCents==-100&&close.Value.ReviewStatus=="PendingReview","shortage must close pending review with true variance");
            Require(drawer.Close(user,new(11_100,true,"close-1","Physical count")).IsSuccess&&Count(c,"cash_drawer_reconciliations")==1,"close replay must reconcile without duplicate");Require(drawer.Close(user,new(11_100,true,"close-2",null)).IsFailure,"second close must fail");
            var review=drawer.Review(user,Guid.Parse(session),new("Acknowledged","Manager investigated shortage","review-1"));Require(review.IsSuccess&&review.Value.ReviewStatus=="Acknowledged"&&review.Value.CountedCashCents==11_100,"review must append without rewriting count");Require(Count(c,"cash_drawer_reviews")==1,"one immutable review expected");
            Require(drawer.Review(user,Guid.Parse(session),new("Acknowledged","Manager investigated shortage","review-1")).IsSuccess&&Count(c,"cash_drawer_reviews")==1,"review replay must not duplicate");
            Require(drawer.Review(user,Guid.Parse(session),new("Acknowledged","Changed reason","review-1")).IsFailure,"review request replay with changed data must fail");
            var second=host.ShiftService.OpenShiftAsync(user,0).GetAwaiter().GetResult();Require(second.Ok,"zero opening float must open");Require(drawer.Close(user,new(0,true,"close-zero",null)).Value.ReviewStatus=="Reconciled","verified zero variance must reconcile");
            Guid legacy=Guid.NewGuid();Exec(c,"INSERT INTO cashier_sessions(id,user_id,opened_at,opening_cash_cents,expected_cash_cents,variance_cents,status,notes,cash_coverage_started_at) VALUES($id,$user,$at,0,0,0,1,'legacy',NULL)",( "$id",legacy.ToString("D")),("$user",user.ToString("D")),("$at",DateTimeOffset.UtcNow.ToString("O")));Require(drawer.Close(user,new(0,true,"legacy-close",null)).IsFailure,"legacy unverified coverage must fail closed");
            string remote=File.ReadAllText(Path.Combine(Root(),"src","Salryn.Api.Local","RemotePos","RemotePosCheckoutService.cs"));Require(remote.Contains("PhysicalCountRequired",StringComparison.Ordinal)&&!remote.Contains("open.ExpectedCash, \"Remote POS shift close\"",StringComparison.Ordinal),"remote auto-balance bypass must be removed");
            Require(Scalar(c,"SELECT counted_cash_cents FROM cashier_sessions WHERE id=$id",("$id",session))==11_100,"physical count must persist exactly");Console.WriteLine("W4 cash drawer PASS: physical count, verified expected, cash in/out/refund, replay, shortage review, zero variance, immutable count, legacy fail-closed, remote bypass denial.");
        }finally{SqliteConnectionFactory.ClearAllPools();if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    private static Guid SeedUser(SalrynLocalComposition c){Guid id=Guid.NewGuid();using var x=c.ConnectionFactory.CreateConnection();x.Open();using var q=x.CreateCommand();q.CommandText="INSERT INTO users(id,username,display_name,password_hash,role_id,status,created_at) VALUES($id,'w4','W4','x',(SELECT id FROM roles LIMIT 1),1,$at)";q.Parameters.AddWithValue("$id",id.ToString("D"));q.Parameters.AddWithValue("$at",DateTimeOffset.UtcNow.ToString("O"));q.ExecuteNonQuery();return id;}
    private static void Exec(SalrynLocalComposition c,string sql,params(string,object)[] p){using var x=c.ConnectionFactory.CreateConnection();x.Open();using var q=x.CreateCommand();q.CommandText=sql;foreach(var a in p)q.Parameters.AddWithValue(a.Item1,a.Item2);q.ExecuteNonQuery();}
    private static long Scalar(SalrynLocalComposition c,string sql,params(string,object)[] p){using var x=c.ConnectionFactory.CreateConnection();x.Open();using var q=x.CreateCommand();q.CommandText=sql;foreach(var a in p)q.Parameters.AddWithValue(a.Item1,a.Item2);return Convert.ToInt64(q.ExecuteScalar());}
    private static long Count(SalrynLocalComposition c,string table)=>Scalar(c,$"SELECT COUNT(*) FROM {table}");
    private static string Root(){string? p=Directory.GetCurrentDirectory();while(p is not null&&!File.Exists(Path.Combine(p,"Salryn.sln")))p=Directory.GetParent(p)?.FullName;return p!;}
    private static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
}
