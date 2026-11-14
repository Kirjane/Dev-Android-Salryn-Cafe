using Microsoft.Data.Sqlite;
using Salryn.Composition.Local;
using Salryn.Core.Common;

namespace Salryn.Api.Local.Services;

public sealed record CashDrawerSnapshot(string SessionId,string State,long OpeningCashCents,long? ExpectedCashCents,long? CountedCashCents,long? VarianceCents,string CoverageStatus,string ReviewStatus,IReadOnlyList<CashDrawerEventRow> Events,IReadOnlyList<CashDrawerReviewRow> Reviews);
public sealed record CashDrawerEventRow(string EventType,long SignedAmountCents,string SourceType,string SourceId,string ActorUserId,string OccurredAt,string? Note);
public sealed record CashDrawerReviewRow(string Decision,string Reason,string ReviewerUserId,string CreatedAt);
public sealed record DrawerCloseCommand(long CountedCashCents,bool PhysicalCountConfirmed,string ClientRequestId,string? Note);
public sealed record DrawerMovementCommand(string EventType,long AmountCents,string ClientRequestId,string Reason);
public sealed record DrawerReviewCommand(string Decision,string Reason,string ClientRequestId);
public sealed record CashDrawerHistoryPage(IReadOnlyList<CashDrawerSnapshot> Items,int Limit,int Offset);

public sealed class CashDrawerReconciliationService
{
    private readonly SalrynLocalComposition _composition;
    public CashDrawerReconciliationService(SalrynLocalComposition composition)=>_composition=composition;

    public Result<CashDrawerSnapshot> Read(Guid userId)
    {
        try{using SqliteConnection c=Open();string? id=ScalarString(c,"SELECT id FROM cashier_sessions WHERE user_id=$user AND status=1 ORDER BY opened_at DESC LIMIT 1",("$user",userId.ToString("D")));return id is null?Result<CashDrawerSnapshot>.Fail(Error.NotFound("CashDrawer.NoOpenShift","No open shift.")):ReadById(c,id);}
        catch(Exception ex){return Result<CashDrawerSnapshot>.Fail(Error.Unexpected("CashDrawer.ReadFailed",ex.Message));}
    }

    public Result<CashDrawerSnapshot> RecordMovement(Guid userId,DrawerMovementCommand request)
    {
        if(request.AmountCents<=0||string.IsNullOrWhiteSpace(request.Reason)||string.IsNullOrWhiteSpace(request.ClientRequestId))return Result<CashDrawerSnapshot>.Fail(Error.Validation("CashDrawer.InvalidMovement","Positive amount, reason, and request id are required."));
        string type=request.EventType switch{"CashIn"=>"CashIn","CashOut"=>"CashOut","CashRefund"=>"CashRefund",_=>string.Empty};if(type.Length==0)return Result<CashDrawerSnapshot>.Fail(Error.Validation("CashDrawer.InvalidMovementType","Movement must be CashIn, CashOut, or CashRefund."));
        try{using SqliteConnection c=Open();using SqliteTransaction t=c.BeginTransaction();string? session=ScalarString(c,"SELECT id FROM cashier_sessions WHERE user_id=$user AND status=1",t,("$user",userId.ToString("D")));if(session is null)return Result<CashDrawerSnapshot>.Fail(Error.Conflict("CashDrawer.NoOpenShift","Open a shift before recording drawer cash."));
            using(var replay=c.CreateCommand()){replay.Transaction=t;replay.CommandText="SELECT cashier_session_id,event_type,amount_cents,note FROM cash_drawer_events WHERE request_id=$request";replay.Parameters.AddWithValue("$request",request.ClientRequestId);using var rr=replay.ExecuteReader();if(rr.Read()){bool same=rr.GetString(0)==session&&rr.GetString(1)==type&&rr.GetInt64(2)==request.AmountCents&&rr.GetString(3)==request.Reason.Trim();rr.Close();t.Rollback();return same?ReadById(c,session):Result<CashDrawerSnapshot>.Fail(Error.Conflict("CashDrawer.RequestConflict","The request id was already used with different movement details."));}}
            using SqliteCommand q=c.CreateCommand();q.Transaction=t;q.CommandText="INSERT OR IGNORE INTO cash_drawer_events(id,cashier_session_id,event_type,direction,amount_cents,source_type,source_id,actor_user_id,method,occurred_at,request_id,note) VALUES($id,$session,$type,$direction,$amount,'ManualMovement',$source,$actor,'Cash',$at,$request,$note)";q.Parameters.AddWithValue("$id",Guid.NewGuid().ToString("D"));q.Parameters.AddWithValue("$session",session);q.Parameters.AddWithValue("$type",type);q.Parameters.AddWithValue("$direction",type=="CashIn"?1:-1);q.Parameters.AddWithValue("$amount",request.AmountCents);q.Parameters.AddWithValue("$source",request.ClientRequestId);q.Parameters.AddWithValue("$actor",userId.ToString("D"));q.Parameters.AddWithValue("$at",DateTimeOffset.UtcNow.ToString("O"));q.Parameters.AddWithValue("$request",request.ClientRequestId);q.Parameters.AddWithValue("$note",request.Reason.Trim());q.ExecuteNonQuery();t.Commit();return ReadById(c,session);}
        catch(SqliteException ex){return Result<CashDrawerSnapshot>.Fail(Error.Conflict("CashDrawer.MovementConflict",ex.Message));}
    }

    public Result<CashDrawerSnapshot> Close(Guid userId,DrawerCloseCommand request)
    {
        if(request.CountedCashCents<0||!request.PhysicalCountConfirmed||string.IsNullOrWhiteSpace(request.ClientRequestId))return Result<CashDrawerSnapshot>.Fail(Error.Validation("CashDrawer.PhysicalCountRequired","Enter a nonnegative physical count, confirm it, and supply a request id."));
        string? canonicalNote=NormalizeNote(request.Note);
        try{using SqliteConnection c=Open();using SqliteTransaction t=c.BeginTransaction();using(var replay=c.CreateCommand()){replay.Transaction=t;replay.CommandText="SELECT cashier_session_id,counted_cash_cents,submitted_by_user_id,confirmation_text,close_note FROM cash_drawer_reconciliations WHERE close_request_id=$request";replay.Parameters.AddWithValue("$request",request.ClientRequestId);using var rr=replay.ExecuteReader();if(rr.Read()){string existing=rr.GetString(0);bool same=rr.GetInt64(1)==request.CountedCashCents&&rr.GetString(2)==userId.ToString("D")&&rr.GetString(3)=="Operator confirmed physical cash count"&&ReadNullable(rr,4)==canonicalNote;rr.Close();t.Rollback();return same?ReadById(c,existing):Result<CashDrawerSnapshot>.Fail(Error.Conflict("CashDrawer.RequestConflict","The close request id was already used with different submitted details."));}}
            using SqliteCommand read=c.CreateCommand();read.Transaction=t;read.CommandText="SELECT id,opening_cash_cents,cash_coverage_started_at FROM cashier_sessions WHERE user_id=$user AND status=1";read.Parameters.AddWithValue("$user",userId.ToString("D"));using SqliteDataReader r=read.ExecuteReader();if(!r.Read())return Result<CashDrawerSnapshot>.Fail(Error.Conflict("CashDrawer.NoOpenShift","No open shift to close."));string session=r.GetString(0);long opening=r.GetInt64(1);bool covered=!r.IsDBNull(2);r.Close();
            covered&=CountCoverageViolations(c,t,session,userId)==0;if(!covered)return Result<CashDrawerSnapshot>.Fail(Error.Conflict("CashDrawer.CoverageUnverified","Cash source coverage is unverified; safe close is blocked. Manager force-close policy approval is required."));
            long movement=ScalarLong(c,"SELECT COALESCE(SUM(direction*amount_cents),0) FROM cash_drawer_events WHERE cashier_session_id=$session",t,("$session",session));long expected=opening+movement;long variance=request.CountedCashCents-expected;string review=variance==0?"Reconciled":"PendingReview";string now=DateTimeOffset.UtcNow.ToString("O");
            Execute(c,t,"UPDATE cashier_sessions SET expected_cash_cents=$expected,counted_cash_cents=$counted,variance_cents=$variance,closed_at=$at,status=2,notes=$note WHERE id=$session AND status=1",("$expected",expected),("$counted",request.CountedCashCents),("$variance",variance),("$at",now),("$note",(object?)canonicalNote??DBNull.Value),("$session",session));
            Execute(c,t,"INSERT INTO cash_drawer_reconciliations(cashier_session_id,expected_cash_cents,counted_cash_cents,variance_cents,coverage_status,review_status,close_request_id,submitted_by_user_id,submitted_at,confirmation_text,close_note) VALUES($session,$expected,$counted,$variance,'Verified',$review,$request,$user,$at,'Operator confirmed physical cash count',$note)",( "$session",session),("$expected",expected),("$counted",request.CountedCashCents),("$variance",variance),("$review",review),("$request",request.ClientRequestId),("$user",userId.ToString("D")),("$at",now),("$note",(object?)canonicalNote??DBNull.Value));t.Commit();return ReadById(c,session);}
        catch(SqliteException ex){return Result<CashDrawerSnapshot>.Fail(Error.Conflict("CashDrawer.CloseConflict",ex.Message));}
    }

    public Result<CashDrawerSnapshot> Review(Guid reviewer,Guid sessionId,DrawerReviewCommand request)
    {
        if(sessionId==Guid.Empty||request.Decision!="Acknowledged"||string.IsNullOrWhiteSpace(request.Reason)||string.IsNullOrWhiteSpace(request.ClientRequestId))return Result<CashDrawerSnapshot>.Fail(Error.Validation("CashDrawer.InvalidReview","Acknowledged decision, reason, and request id are required."));
        try{using SqliteConnection c=Open();using SqliteTransaction t=c.BeginTransaction();string session=sessionId.ToString("D");string? submitter=ScalarString(c,"SELECT submitted_by_user_id FROM cash_drawer_reconciliations WHERE cashier_session_id=$session AND variance_cents<>0",t,("$session",session));if(submitter is null)return Result<CashDrawerSnapshot>.Fail(Error.Conflict("CashDrawer.NoReviewableVariance","The shift has no reviewable variance."));if(submitter==reviewer.ToString("D"))return Result<CashDrawerSnapshot>.Fail(Error.Conflict("CashDrawer.SelfReviewDenied","The operator who submitted the count cannot acknowledge the same discrepancy."));using(var replay=c.CreateCommand()){replay.Transaction=t;replay.CommandText="SELECT cashier_session_id,reviewer_user_id,decision,reason FROM cash_drawer_reviews WHERE request_id=$request";replay.Parameters.AddWithValue("$request",request.ClientRequestId);using var rr=replay.ExecuteReader();if(rr.Read()){bool same=rr.GetString(0)==session&&rr.GetString(1)==reviewer.ToString("D")&&rr.GetString(2)==request.Decision&&rr.GetString(3)==request.Reason.Trim();rr.Close();t.Rollback();return same?ReadById(c,session):Result<CashDrawerSnapshot>.Fail(Error.Conflict("CashDrawer.RequestConflict","The request id was already used with different review details."));}}Execute(c,t,"INSERT INTO cash_drawer_reviews(id,cashier_session_id,reviewer_user_id,decision,reason,request_id,created_at) VALUES($id,$session,$user,'Acknowledged',$reason,$request,$at)",( "$id",Guid.NewGuid().ToString("D")),("$session",session),("$user",reviewer.ToString("D")),("$reason",request.Reason.Trim()),("$request",request.ClientRequestId),("$at",DateTimeOffset.UtcNow.ToString("O")));Execute(c,t,"UPDATE cash_drawer_reconciliations SET review_status='Acknowledged' WHERE cashier_session_id=$session AND variance_cents<>0",("$session",session));t.Commit();return ReadById(c,session);}catch(Exception ex){return Result<CashDrawerSnapshot>.Fail(Error.Conflict("CashDrawer.ReviewConflict",ex.Message));}
    }

    public Result<CashDrawerHistoryPage> ReadClosedHistory(int limit,int offset)
    {
        if(limit<1||limit>100||offset<0)return Result<CashDrawerHistoryPage>.Fail(Error.Validation("CashDrawer.InvalidPage","Limit must be 1-100 and offset must be nonnegative."));
        try{using SqliteConnection c=Open();var items=new List<CashDrawerSnapshot>();using var q=c.CreateCommand();q.CommandText="SELECT cashier_session_id FROM cash_drawer_reconciliations ORDER BY submitted_at DESC,cashier_session_id LIMIT $limit OFFSET $offset";q.Parameters.AddWithValue("$limit",limit);q.Parameters.AddWithValue("$offset",offset);var ids=new List<string>();using(var r=q.ExecuteReader())while(r.Read())ids.Add(r.GetString(0));foreach(string id in ids){var item=ReadById(c,id);if(item.IsFailure)return Result<CashDrawerHistoryPage>.Fail(item.ErrorDetail);items.Add(item.Value);}return Result<CashDrawerHistoryPage>.Success(new(items,limit,offset));}catch(Exception ex){return Result<CashDrawerHistoryPage>.Fail(Error.Unexpected("CashDrawer.HistoryFailed",ex.Message));}
    }

    private static long CountCoverageViolations(SqliteConnection c,SqliteTransaction t,string session,Guid userId)
    {
        const string sql="""
            WITH sale_expected AS (
              SELECT s.id,s.cashier_session_id,s.cashier_user_id,MAX(0,COALESCE(SUM(CASE WHEN p.method=1 THEN p.amount_cents ELSE 0 END),0)-s.change_cents) amount
              FROM sales s LEFT JOIN payments p ON p.sale_id=s.id WHERE s.cashier_session_id=$session
              GROUP BY s.id,s.cashier_session_id,s.cashier_user_id,s.change_cents
            ), sale_bad AS (
              SELECT id FROM sale_expected x WHERE
                (x.amount>0 AND (SELECT COUNT(*) FROM cash_drawer_events e WHERE e.source_type='Sale' AND e.source_id=x.id AND e.cashier_session_id=x.cashier_session_id AND e.event_type='CashSaleNet' AND e.direction=1 AND e.amount_cents=x.amount AND e.actor_user_id=x.cashier_user_id AND e.method='Cash')<>1)
                OR (x.amount=0 AND EXISTS(SELECT 1 FROM cash_drawer_events e WHERE e.source_type='Sale' AND e.source_id=x.id))
            ), sale_orphan AS (
              SELECT e.id FROM cash_drawer_events e LEFT JOIN sale_expected x ON x.id=e.source_id
              WHERE e.cashier_session_id=$session AND e.source_type='Sale' AND (x.id IS NULL OR x.amount<=0 OR e.event_type<>'CashSaleNet' OR e.direction<>1 OR e.amount_cents<>x.amount OR e.actor_user_id<>x.cashier_user_id OR e.method<>'Cash')
            ), ar_expected AS (
              SELECT a.id,a.cashier_session_id,a.collected_by_user_id,a.amount_cents FROM accounts_receivable_collections a WHERE a.cashier_session_id=$session AND LOWER(TRIM(a.method))='cash'
            ), ar_bad AS (
              SELECT id FROM ar_expected x WHERE (SELECT COUNT(*) FROM cash_drawer_events e WHERE e.source_type='ArCollection' AND e.source_id=x.id AND e.cashier_session_id=x.cashier_session_id AND e.event_type='ArCashCollection' AND e.direction=1 AND e.amount_cents=x.amount_cents AND e.actor_user_id=x.collected_by_user_id AND e.method='Cash')<>1
            ), ar_orphan AS (
              SELECT e.id FROM cash_drawer_events e LEFT JOIN ar_expected x ON x.id=e.source_id
              WHERE e.cashier_session_id=$session AND e.source_type='ArCollection' AND (x.id IS NULL OR e.event_type<>'ArCashCollection' OR e.direction<>1 OR e.amount_cents<>x.amount_cents OR e.actor_user_id<>x.collected_by_user_id OR e.method<>'Cash')
            ), manual_bad AS (
              SELECT id FROM cash_drawer_events WHERE cashier_session_id=$session AND (source_type NOT IN('Sale','ArCollection','ManualMovement') OR (source_type='ManualMovement' AND (actor_user_id IS NULL OR actor_user_id<>$user OR actor_user_id<>(SELECT cs.user_id FROM cashier_sessions cs WHERE cs.id=cashier_session_id) OR amount_cents<=0 OR method<>'Cash' OR source_id<>request_id OR (event_type='CashIn' AND direction<>1) OR (event_type IN('CashOut','CashRefund') AND direction<>-1) OR event_type NOT IN('CashIn','CashOut','CashRefund'))))
            )
            SELECT (SELECT COUNT(*) FROM sale_bad)+(SELECT COUNT(*) FROM sale_orphan)+(SELECT COUNT(*) FROM ar_bad)+(SELECT COUNT(*) FROM ar_orphan)+(SELECT COUNT(*) FROM manual_bad)+
              (SELECT COUNT(*) FROM accounts_receivable_collections a WHERE a.collected_by_user_id=$user AND LOWER(TRIM(a.method))='cash' AND a.cashier_session_id IS NULL AND a.collected_at>=(SELECT opened_at FROM cashier_sessions WHERE id=$session));
            """;
        return ScalarLong(c,sql,t,("$session",session),("$user",userId.ToString("D")));
    }

    private Result<CashDrawerSnapshot> ReadById(SqliteConnection c,string id){using SqliteCommand q=c.CreateCommand();q.CommandText="SELECT cs.status,cs.opening_cash_cents,r.expected_cash_cents,r.counted_cash_cents,r.variance_cents,COALESCE(r.coverage_status,CASE WHEN cs.cash_coverage_started_at IS NULL THEN 'LegacyUnverified' ELSE 'OpenVerified' END),COALESCE(r.review_status,'Open') FROM cashier_sessions cs LEFT JOIN cash_drawer_reconciliations r ON r.cashier_session_id=cs.id WHERE cs.id=$id";q.Parameters.AddWithValue("$id",id);using SqliteDataReader r=q.ExecuteReader();if(!r.Read())return Result<CashDrawerSnapshot>.Fail(Error.NotFound("CashDrawer.NotFound","Shift not found."));string state=r.GetInt32(0)==1?"Open":"Closed";long opening=r.GetInt64(1);long? expected=r.IsDBNull(2)?opening+ScalarLong(c,"SELECT COALESCE(SUM(direction*amount_cents),0) FROM cash_drawer_events WHERE cashier_session_id=$id",("$id",id)):r.GetInt64(2);long? counted=r.IsDBNull(3)?null:r.GetInt64(3);long? variance=r.IsDBNull(4)?null:r.GetInt64(4);string coverage=r.GetString(5),review=r.GetString(6);r.Close();var events=new List<CashDrawerEventRow>();using(var e=c.CreateCommand()){e.CommandText="SELECT event_type,direction*amount_cents,source_type,source_id,actor_user_id,occurred_at,note FROM cash_drawer_events WHERE cashier_session_id=$id ORDER BY occurred_at,id";e.Parameters.AddWithValue("$id",id);using var er=e.ExecuteReader();while(er.Read())events.Add(new(er.GetString(0),er.GetInt64(1),er.GetString(2),er.GetString(3),er.GetString(4),er.GetString(5),er.IsDBNull(6)?null:er.GetString(6)));}var reviews=new List<CashDrawerReviewRow>();using(var v=c.CreateCommand()){v.CommandText="SELECT decision,reason,reviewer_user_id,created_at FROM cash_drawer_reviews WHERE cashier_session_id=$id ORDER BY created_at,id";v.Parameters.AddWithValue("$id",id);using var vr=v.ExecuteReader();while(vr.Read())reviews.Add(new(vr.GetString(0),vr.GetString(1),vr.GetString(2),vr.GetString(3)));}return Result<CashDrawerSnapshot>.Success(new(id,state,opening,expected,counted,variance,coverage,review,events,reviews));}
    private SqliteConnection Open(){var c=_composition.ConnectionFactory.CreateConnection();c.Open();return c;}
    private static void Execute(SqliteConnection c,SqliteTransaction t,string sql,params(string,object)[] p){using var q=c.CreateCommand();q.Transaction=t;q.CommandText=sql;foreach(var x in p)q.Parameters.AddWithValue(x.Item1,x.Item2);if(q.ExecuteNonQuery()!=1)throw new InvalidOperationException("Expected one affected row.");}
    private static string? ScalarString(SqliteConnection c,string sql,params(string,object)[] p)=>ScalarString(c,sql,null,p);
    private static string? ScalarString(SqliteConnection c,string sql,SqliteTransaction? t,params(string,object)[] p){using var q=c.CreateCommand();q.Transaction=t;q.CommandText=sql;foreach(var x in p)q.Parameters.AddWithValue(x.Item1,x.Item2);return q.ExecuteScalar() as string;}
    private static long ScalarLong(SqliteConnection c,string sql,params(string,object)[] p)=>ScalarLong(c,sql,null,p);
    private static long ScalarLong(SqliteConnection c,string sql,SqliteTransaction? t,params(string,object)[] p){using var q=c.CreateCommand();q.Transaction=t;q.CommandText=sql;foreach(var x in p)q.Parameters.AddWithValue(x.Item1,x.Item2);return Convert.ToInt64(q.ExecuteScalar());}
    private static string? NormalizeNote(string? note)=>string.IsNullOrWhiteSpace(note)?null:note.Trim();
    private static string? ReadNullable(SqliteDataReader reader,int ordinal)=>reader.IsDBNull(ordinal)?null:reader.GetString(ordinal);
}
