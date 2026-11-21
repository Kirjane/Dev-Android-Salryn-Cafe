using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Sync;

/// <summary>Verifies already committed same-database origin facts. It never creates financial effects.
/// The caller must independently authorize current device/operator/operation scope before admission.</summary>
public static class CanonicalOriginSaleVerifier
{
    public static Result<bool> VerifyIfRetained(SqliteConnection connection, SqliteTransaction transaction,
        SyncOperationEnvelopeV1 envelope)
    {
        var retained=new SqliteSyncOutboxStore(new SqliteConnectionFactory(connection.DataSource)).Find(connection,transaction,envelope.OperationId);
        if(retained is null) return Result<bool>.Success(false);
        if(retained.Envelope.OperationHash!=envelope.OperationHash || retained.Envelope.DeviceId!=envelope.DeviceId ||
            retained.Envelope.ActorUserId!=envelope.ActorUserId || !SyncProtocolV1.Validate(envelope).IsValid)
            return Result<bool>.Fail(Error.Conflict("Sync.OriginEnvelopeConflict","Retained origin envelope differs from submitted evidence."));
        var change=new AppliedBusinessChangeV1(Guid.Empty,0,Guid.Empty,envelope.OperationId,envelope.DeviceId,envelope.LocalSequence,
            envelope.StoreId,"OriginVerification",1,envelope.AggregateId,envelope.PayloadJson,envelope.PayloadHash,"",envelope.OccurredAtLocal);
        LocalSaleFactVerification facts;
        if(envelope.OperationType==SaleCommittedV1Contract.OperationType)
        {
            var parsed=SaleCommittedV1CanonicalJson.Parse(envelope.PayloadJson);
            if(!parsed.IsValid || parsed.Payload is null) return Invalid();
            facts=LocalSaleCommittedV1FactVerifier.VerifyOriginEcho(connection,transaction,change,parsed.Payload);
        }
        else if(envelope.OperationType==SaleCommittedV2Contract.OperationType)
        {
            var parsed=SaleCommittedV2CanonicalJson.Parse(envelope.PayloadJson);
            if(!parsed.IsValid || parsed.Payload is null) return Invalid();
            facts=LocalSaleCommittedV2FactVerifier.VerifyOriginEcho(connection,transaction,change,parsed.Payload);
        }
        else if(envelope.OperationType==SaleCommittedV3Contract.OperationType)
        {
            var parsed=SaleCommittedV3CanonicalJson.Parse(envelope.PayloadJson);
            if(!parsed.IsValid || parsed.Payload is null) return Invalid();
            facts=SharedSqliteCafeBookingV3Participant.VerifyOriginEcho(connection,transaction,change,parsed.Payload);
        }
        else return Result<bool>.Success(false);
        return facts.Matches?Result<bool>.Success(true):Result<bool>.Fail(Error.Conflict(facts.Code??"Sync.OriginFactConflict",facts.Message??"Origin financial evidence conflicts."));
    }
    private static Result<bool> Invalid()=>Result<bool>.Fail(Error.Validation("Sync.OriginPayloadInvalid","Canonical origin sale payload is invalid."));
}
