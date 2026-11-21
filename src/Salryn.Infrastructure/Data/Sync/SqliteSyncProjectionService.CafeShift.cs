using Salryn.Core.Common;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Data.Transactions;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Sync;

public sealed partial class SqliteSyncProjectionService
{
    private static bool IsCafeShiftAppliedChange(AppliedBusinessChangeV1 change) => change.SchemaVersion == 1 &&
        change.ChangeType is AppliedBusinessChangeContract.CafeShiftOpenedAppliedV1 or AppliedBusinessChangeContract.CafeShiftClosedAppliedV1;

    private Result<SyncProjectionResult> ProjectCafeShift(SqliteSharedTransactionContext context, Guid storeId,
        Guid localDeviceId, SyncRemoteState state, long expectedSequence, AppliedBusinessChangeV1 change)
    {
        string type = change.ChangeType == AppliedBusinessChangeContract.CafeShiftOpenedAppliedV1 ? CafeShiftSyncContract.Opened : CafeShiftSyncContract.Closed;
        var payload = CafeShiftSyncContract.Parse(type,change.PayloadJson);
        if(payload is null || payload.StoreId != storeId || payload.SessionId != change.AggregateId || payload.DeviceId != change.OriginDeviceId)
            return ReconcileCurrent(context,storeId,expectedSequence,"CafeShift.ProjectionScopeMismatch","Canonical shift payload does not match feed scope.");
        var envelope = SyncProtocolV1.CreateEnvelope(change.OriginDeviceId,storeId,payload.ActorUserId,change.OriginLocalSequence,
            type,1,payload.SessionId,null,payload.OccurredAtUtc,change.PayloadJson,change.SourceOperationId);
        if(envelope.PayloadHash != change.PayloadHash)
            return ReconcileCurrent(context,storeId,expectedSequence,"CafeShift.ProjectionHashMismatch","Canonical shift hash differs from retained feed evidence.");
        bool echo=change.OriginDeviceId==localDeviceId;
        var participant=new SharedSqliteCafeShiftParticipant();
        _failureInjector.ThrowIfRequested(SyncProjectionFailurePoint.BeforeDomainWrite,expectedSequence);
        var result=echo ? participant.VerifyOriginEcho(context,envelope)
            : participant.ApplyCanonical(context,envelope,requireCurrentAuthority:false);
        if(result.IsFailure) return Result<SyncProjectionResult>.Fail(result.ErrorDetail);
        _failureInjector.ThrowIfRequested(SyncProjectionFailurePoint.AfterDomainWriteBeforeResolution,expectedSequence);
        ResolveApplied(context.Connection,context.Transaction,storeId,state,expectedSequence,DateTimeOffset.UtcNow);
        return Result<SyncProjectionResult>.Success(SyncProjectionResult.Applied(expectedSequence,echo,
            RequireRemoteState(context,storeId),echo?SyncProjectionContract.OriginEchoResolvedCode:SyncProjectionContract.ProjectionAppliedCode));
    }
}
