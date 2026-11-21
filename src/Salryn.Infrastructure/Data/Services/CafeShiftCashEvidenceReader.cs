using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;

namespace Salryn.Infrastructure.Data.Services;

internal sealed record CafeShiftCashEvidence(string Digest, long ExpectedCashCents);

// Same source coverage checks as the Host blind-close boundary. No inferred refunds or AP cash.
internal static class CafeShiftCashEvidenceReader
{
    internal static Result<CafeShiftCashEvidence> Read(SqliteConnection c, SqliteTransaction t, Guid sessionId, Guid actor)
    {
        string session = sessionId.ToString("D");
        using var command = c.CreateCommand(); command.Transaction = t;
        command.CommandText = "SELECT opening_cash_cents,cash_coverage_started_at FROM cashier_sessions WHERE id=$id AND user_id=$user;";
        command.Parameters.AddWithValue("$id",session); command.Parameters.AddWithValue("$user",actor.ToString("D"));
        long opening;
        using(var reader=command.ExecuteReader())
        {
            if(!reader.Read() || reader.IsDBNull(1)) return Denied();
            opening=reader.GetInt64(0);
        }
        if(CountCoverageViolations(c,t,session,actor)!=0) return Denied();
        // Explicit Cafe movements do not yet have a reconciled Host drawer projection.
        // Fail closed rather than silently omit or manufacture those physical cash effects.
        if(ScalarLong(c,"SELECT COUNT(*) FROM cash_movements WHERE cashier_session_id=$session AND movement_type NOT IN(1,2);",t,("$session",session))!=0) return Denied();
        long movement=ScalarLong(c,"SELECT COALESCE(SUM(direction*amount_cents),0) FROM cash_drawer_events WHERE cashier_session_id=$session;",t,("$session",session));
        long ledger=ScalarLong(c,"SELECT COALESCE(SUM(delta_cents),0) FROM cash_movements WHERE cashier_session_id=$session;",t,("$session",session));
        if(ledger!=movement) return Denied();
        var rows=new List<string>();
        command.Parameters.Clear();command.Parameters.AddWithValue("$session",session);
        command.CommandText="""
            SELECT source_type,source_id,event_type,direction,amount_cents,actor_user_id,method
            FROM cash_drawer_events WHERE cashier_session_id=$session
            ORDER BY source_type,source_id,event_type;
            """;
        using(var reader=command.ExecuteReader()) while(reader.Read())
            rows.Add(JsonSerializer.Serialize(new object[]{reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.GetInt64(3),reader.GetInt64(4),reader.GetString(5),reader.GetString(6)}));
        string evidence=JsonSerializer.Serialize(new {session,actor=actor.ToString("D"),opening,rows});
        return Result<CafeShiftCashEvidence>.Success(new(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(evidence))).ToLowerInvariant(),checked(opening+movement)));
    }
    private static Result<CafeShiftCashEvidence> Denied()=>Result<CafeShiftCashEvidence>.Fail(Error.Conflict("CafeShift.CoverageUnverified","Cash sources must be fully reconciled before blind close; existing evidence is preserved."));
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

    private static long ScalarLong(SqliteConnection c,string sql,SqliteTransaction t,params (string,object)[] parameters)
    {
        using var command=c.CreateCommand(); command.Transaction=t; command.CommandText=sql;
        foreach(var parameter in parameters) command.Parameters.AddWithValue(parameter.Item1,parameter.Item2);
        return Convert.ToInt64(command.ExecuteScalar());
    }
}
