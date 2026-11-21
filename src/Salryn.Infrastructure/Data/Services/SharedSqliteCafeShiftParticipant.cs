using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Infrastructure.Data.Transactions;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Services;

/// <summary>Single canonical shift write participant. Caller owns the inbox/outbox/feed transaction.</summary>
public sealed class SharedSqliteCafeShiftParticipant
{
    public Result<bool> ApplyCanonical(SqliteSharedTransactionContext context, SyncOperationEnvelopeV1 envelope,
        bool checkCentralPrerequisites = false, Guid? tenantId = null, bool requireCurrentAuthority = true)
    {
        var c=context.Connection; var t=context.Transaction;
        var payload=CafeShiftSyncContract.Parse(envelope.OperationType,envelope.PayloadJson);
        if(!SyncProtocolV1.Validate(envelope).IsValid || envelope.SchemaVersion!=1 || payload is null ||
            payload.SessionId!=envelope.AggregateId || payload.DeviceId!=envelope.DeviceId ||
            payload.StoreId!=envelope.StoreId || payload.ActorUserId!=envelope.ActorUserId ||
            payload.OccurredAtUtc!=envelope.OccurredAtLocal || envelope.BaseVersion is not null ||
            payload.PrerequisiteOperationIds.Contains(envelope.OperationId) ||
            (envelope.OperationType==CafeShiftSyncContract.Opened && payload.SessionId!=CafeShiftSyncContract.SessionIdFor(envelope.OperationId)))
            return Denied("CafeShift.EnvelopeInvalid","Shift evidence must match its immutable envelope.");
        if(Scalar(c,t,"SELECT COUNT(*) FROM deployment_identity WHERE store_id=$store;",("$store",payload.StoreId.ToString("D")))!=1)
            return Denied("CafeShift.StoreMismatch","Shift store does not match canonical authority.");
        if(requireCurrentAuthority && Scalar(c,t,"SELECT COUNT(*) FROM users u JOIN role_permissions p ON p.role_id=u.role_id WHERE u.id=$actor AND u.status=1 AND p.permission='Shift.OpenClose';",("$actor",payload.ActorUserId.ToString("D")))!=1)
            return Denied("ERR_CANONICAL_OPERATOR_AUTHORITY","Current Shift.OpenClose authority is required.");
        if(envelope.OperationType==CafeShiftSyncContract.Opened) return Open(c,t,payload);
        if(checkCentralPrerequisites)
        {
            if(tenantId is null) return Denied("CafeShift.PrerequisiteScopeMissing","Central prerequisite scope is required.");
            foreach(Guid operationId in payload.PrerequisiteOperationIds)
                if(Scalar(c,t,"SELECT COUNT(*) FROM sync_inbox_operations WHERE tenant_id=$tenant AND store_id=$store AND operation_id=$operation AND status='Applied';",
                    ("$tenant",tenantId.Value.ToString("D")),("$store",payload.StoreId.ToString("D")),("$operation",operationId.ToString("D")))!=1)
                    return Denied("CafeShift.PrerequisiteMissing","Earlier financial operations must apply before closing this drawer.");
        }
        return Close(c,t,envelope.OperationId,payload);
    }

    public Result<bool> VerifyOriginEcho(SqliteSharedTransactionContext context, SyncOperationEnvelopeV1 envelope)
    {
        var original = new Sync.SqliteSyncOutboxStore(new SqliteConnectionFactory(context.Connection.DataSource))
            .Find(context.Connection, context.Transaction, envelope.OperationId);
        var payload = CafeShiftSyncContract.Parse(envelope.OperationType, envelope.PayloadJson);
        if(original is null || original.Envelope.OperationHash != envelope.OperationHash || payload is null ||
            Scalar(context.Connection,context.Transaction,"SELECT COUNT(*) FROM cashier_sessions WHERE id=$session AND user_id=$actor;",
                ("$session",envelope.AggregateId.ToString("D")),("$actor",envelope.ActorUserId.ToString("D")))!=1 ||
            (envelope.OperationType==CafeShiftSyncContract.Closed &&
                Scalar(context.Connection,context.Transaction,"SELECT COUNT(*) FROM cashier_sessions WHERE id=$session AND status=2;",("$session",envelope.AggregateId.ToString("D")))!=1))
            return Denied("CafeShift.OriginEvidenceMissing","Origin echo requires the original retained envelope and committed shift evidence.");
        // Canonical historical echo is verified without granting current operator authority.
        return ApplyCanonical(context,envelope,requireCurrentAuthority:false);
    }

    private static Result<bool> Open(SqliteConnection c,SqliteTransaction t,CafeShiftOperationV1 p)
    {
        using(var q=Command(c,t,"SELECT user_id,opening_cash_cents,opened_at FROM cashier_sessions WHERE id=$session;",("$session",p.SessionId.ToString("D"))))
        using(var r=q.ExecuteReader()) if(r.Read())
            return r.GetString(0)==p.ActorUserId.ToString("D") && r.GetInt64(1)==p.CashAmountCents && DateTimeOffset.Parse(r.GetString(2))==p.OccurredAtUtc
                ? Result<bool>.Success(false) : Denied("CafeShift.SessionConflict","Session identity already has different opening evidence.");
        if(Scalar(c,t,"SELECT COUNT(*) FROM cashier_sessions WHERE user_id=$actor AND status=1;",("$actor",p.ActorUserId.ToString("D")))!=0)
            return Denied("CafeShift.OpenSessionExists","This operator already owns an open drawer across devices; reconcile rather than merge it.");
        // A previously contested offline opening cannot become accepted merely because
        // another device subsequently closed its drawer. Preserve historical custody too.
        using(var history=Command(c,t,"SELECT closed_at FROM cashier_sessions WHERE user_id=$actor AND closed_at IS NOT NULL;",("$actor",p.ActorUserId.ToString("D"))))
        using(var r=history.ExecuteReader()) while(r.Read())
            if(DateTimeOffset.Parse(r.GetString(0))>p.OccurredAtUtc)
                return Denied("CafeShift.OpenHistoryConflict","Opening time overlaps retained drawer custody; preserve this evidence for reconciliation.");
        using var insert=Command(c,t,"""
            INSERT INTO cashier_sessions(id,user_id,opened_at,opening_cash_cents,expected_cash_cents,variance_cents,status,notes,cash_coverage_started_at)
            VALUES($session,$actor,$at,$cash,$cash,0,1,'Canonical Cafe shift',$at);
            """,("$session",p.SessionId.ToString("D")),("$actor",p.ActorUserId.ToString("D")),("$at",p.OccurredAtUtc.ToString("O")),("$cash",p.CashAmountCents));
        insert.ExecuteNonQuery();
        return Result<bool>.Success(true);
    }

    private static Result<bool> Close(SqliteConnection c,SqliteTransaction t,Guid operationId,CafeShiftOperationV1 p)
    {
        int status;
        using(var q=Command(c,t,"SELECT status,opened_at FROM cashier_sessions WHERE id=$session AND user_id=$actor;",("$session",p.SessionId.ToString("D")),("$actor",p.ActorUserId.ToString("D"))))
        using(var r=q.ExecuteReader())
        {
            if(!r.Read()) return Denied("CafeShift.SessionMissing","Opening session evidence must arrive before close.");
            status=r.GetInt32(0);
            if(p.OccurredAtUtc<DateTimeOffset.Parse(r.GetString(1))) return Denied("CafeShift.CloseTimeInvalid","Close cannot precede opening.");
        }
        var evidence=CafeShiftCashEvidenceReader.Read(c,t,p.SessionId,p.ActorUserId);
        if(evidence.IsFailure) return Result<bool>.Fail(evidence.ErrorDetail);
        if(evidence.Value.Digest!=p.CashSourceDigest)
            return Denied("CafeShift.CashSourceMismatch","Central cash source evidence differs; pending transactions must reconcile before close.");
        if(status!=1)
        {
            bool same=Scalar(c,t,"""
                SELECT COUNT(*) FROM cash_drawer_reconciliations r JOIN cashier_sessions s ON s.id=r.cashier_session_id
                WHERE r.cashier_session_id=$session AND r.close_request_id=$op AND r.submitted_by_user_id=$actor
                  AND r.counted_cash_cents=$cash AND r.expected_cash_cents=$expected AND s.closed_at=$at AND s.status=2;
                """,("$session",p.SessionId.ToString("D")),("$op",operationId.ToString("D")),("$actor",p.ActorUserId.ToString("D")),
                ("$cash",p.CashAmountCents),("$expected",evidence.Value.ExpectedCashCents),("$at",p.OccurredAtUtc.ToString("O")))==1;
            return same?Result<bool>.Success(false):Denied("CafeShift.CloseConflict","Drawer was already closed with different retained evidence.");
        }
        long variance=checked(p.CashAmountCents-evidence.Value.ExpectedCashCents);
        using(var update=Command(c,t,"""
            UPDATE cashier_sessions SET expected_cash_cents=$expected,counted_cash_cents=$cash,variance_cents=$variance,
                closed_at=$at,status=2,notes='Canonical Cafe physical count' WHERE id=$session AND status=1;
            """,("$expected",evidence.Value.ExpectedCashCents),("$cash",p.CashAmountCents),("$variance",variance),
            ("$at",p.OccurredAtUtc.ToString("O")),("$session",p.SessionId.ToString("D"))))
            if(update.ExecuteNonQuery()!=1) return Denied("CafeShift.CloseConflict","Drawer state changed during close.");
        using var insert=Command(c,t,"""
            INSERT INTO cash_drawer_reconciliations(cashier_session_id,expected_cash_cents,counted_cash_cents,variance_cents,
                coverage_status,review_status,close_request_id,submitted_by_user_id,submitted_at,confirmation_text,close_note)
            VALUES($session,$expected,$cash,$variance,'Verified',$review,$op,$actor,$at,'Operator confirmed physical cash count','Canonical Cafe physical count');
            """,("$session",p.SessionId.ToString("D")),("$expected",evidence.Value.ExpectedCashCents),("$cash",p.CashAmountCents),("$variance",variance),
            ("$review",variance==0?"Reconciled":"PendingReview"),("$op",operationId.ToString("D")),("$actor",p.ActorUserId.ToString("D")),("$at",p.OccurredAtUtc.ToString("O")));
        insert.ExecuteNonQuery();
        return Result<bool>.Success(true);
    }

    internal static SqliteCommand Command(SqliteConnection c,SqliteTransaction t,string sql,params (string,object)[] values)
    { var q=c.CreateCommand();q.Transaction=t;q.CommandText=sql;foreach(var value in values)q.Parameters.AddWithValue(value.Item1,value.Item2);return q; }
    private static long Scalar(SqliteConnection c,SqliteTransaction t,string sql,params (string,object)[] values)
    { using var q=Command(c,t,sql,values);return Convert.ToInt64(q.ExecuteScalar()); }
    private static Result<bool> Denied(string code,string message)=>Result<bool>.Fail(code=="ERR_CANONICAL_OPERATOR_AUTHORITY"?Error.Forbidden(code,message):Error.Conflict(code,message));
}
