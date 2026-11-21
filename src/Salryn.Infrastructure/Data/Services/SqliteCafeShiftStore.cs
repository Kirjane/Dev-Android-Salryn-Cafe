using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Infrastructure.Data.Sync;
using Salryn.Infrastructure.Data.Transactions;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Services;

public sealed class SqliteCafeShiftStore(SqliteConnectionFactory factory)
{
    public Result<Guid> Open(Guid operationId,Guid deviceId,Guid storeId,Guid actor,long openingCashCents,DateTimeOffset at)
        => Execute(operationId,CafeShiftSyncContract.Opened,new(CafeShiftSyncContract.SessionIdFor(operationId),deviceId,storeId,actor,openingCashCents,at.ToUniversalTime(),false,"",[]));

    public Result<Guid> Close(Guid operationId,Guid deviceId,Guid storeId,Guid actor,Guid sessionId,long countedCashCents,DateTimeOffset at)
        => Execute(operationId,CafeShiftSyncContract.Closed,new(sessionId,deviceId,storeId,actor,countedCashCents,at.ToUniversalTime(),true,"",[]));

    private Result<Guid> Execute(Guid operationId,string type,CafeShiftOperationV1 requested)
    {
        if(operationId==Guid.Empty) return Result<Guid>.Fail(Error.Validation("Mobile.OperationIdRequired","Retain a client operation ID before changing a shift."));
        using var c=factory.CreateConnection();c.Open();using var t=c.BeginTransaction(deferred:false);
        var outbox=new SqliteSyncOutboxStore(factory);
        var existing=outbox.Find(c,t,operationId);
        if(existing is not null)
        {
            var original=CafeShiftSyncContract.Parse(existing.Envelope.OperationType,existing.Envelope.PayloadJson);
            if(existing.Envelope.OperationType!=type || original is null ||
                CafeShiftSyncContract.Serialize(original with{CashSourceDigest="",PrerequisiteOperationIds=[]})!=CafeShiftSyncContract.Serialize(requested))
                return Result<Guid>.Fail(Error.Conflict("CafeShift.OperationConflict","Operation ID already has different immutable shift evidence."));
            // Recheck current authority and exact canonical evidence before returning a retry.
            var replay=new SharedSqliteCafeShiftParticipant().ApplyCanonical(new(c,t),existing.Envelope);
            if(replay.IsFailure) return Result<Guid>.Fail(replay.ErrorDetail);
            t.Commit();return Result<Guid>.Success(requested.SessionId);
        }
        CafeShiftOperationV1 payload=requested;
        if(type==CafeShiftSyncContract.Closed)
        {
            var evidence=CafeShiftCashEvidenceReader.Read(c,t,requested.SessionId,requested.ActorUserId);
            if(evidence.IsFailure) return Result<Guid>.Fail(evidence.ErrorDetail);
            using var q=SharedSqliteCafeShiftParticipant.Command(c,t,"""
                SELECT operation_id FROM sync_outbox WHERE device_id=$device AND actor_user_id=$actor AND delivery_status='PendingUpload'
                  AND (operation_type LIKE 'SaleCommitted%' OR operation_type LIKE 'SaleRefund%' OR operation_type IN('CashMovementPostedV1','CafeShiftOpenedV1'))
                ORDER BY local_sequence;
                """,("$device",requested.DeviceId.ToString("D")),("$actor",requested.ActorUserId.ToString("D")));
            var dependencies=new List<Guid>();using(var reader=q.ExecuteReader())while(reader.Read()) dependencies.Add(Guid.Parse(reader.GetString(0)));
            payload=requested with{CashSourceDigest=evidence.Value.Digest,PrerequisiteOperationIds=dependencies};
        }
        // Allocate and persist the immutable envelope in the same transaction as the actual drawer change.
        var envelope=outbox.Enqueue(c,t,new(requested.DeviceId,requested.StoreId,requested.ActorUserId,type,1,
            requested.SessionId,null,requested.OccurredAtUtc,CafeShiftSyncContract.Serialize(payload),operationId));
        var applied=new SharedSqliteCafeShiftParticipant().ApplyCanonical(new(c,t),envelope);
        if(applied.IsFailure) return Result<Guid>.Fail(applied.ErrorDetail);
        t.Commit();return Result<Guid>.Success(requested.SessionId);
    }
}
