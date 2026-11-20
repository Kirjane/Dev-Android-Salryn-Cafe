using Salryn.Core.Common;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Data.Transactions;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Sync;

public sealed partial class SqliteSyncProjectionService
{
    private Result<SyncProjectionResult> ProjectCafeCash(
        SqliteSharedTransactionContext context,
        Guid storeId,
        Guid localDeviceId,
        SyncRemoteState state,
        long expectedSequence,
        AppliedBusinessChangeV1 change)
    {
        CafeCashMovementCanonicalParseResult parsed =
            CafeCashMovementPostedV1CanonicalJson.Parse(
                CafeCashMovementSyncContract.OperationType,
                change.PayloadJson);

        if (!parsed.IsValid || parsed.Payload is null)
        {
            return ReconcileCurrent(
                context,
                storeId,
                expectedSequence,
                parsed.ErrorCode ?? "CafeCashSync.ProjectionPayloadInvalid",
                parsed.ErrorMessage ?? "Canonical C6 cash payload could not be parsed.");
        }

        CafeCashMovementPostedV1 payload = parsed.Payload;
        if (payload.MovementId != change.AggregateId ||
            payload.StoreId != storeId)
        {
            return ReconcileCurrent(
                context,
                storeId,
                expectedSequence,
                "CafeCashSync.ProjectionAuthorityMismatch",
                "Canonical C6 cash aggregate/store differs from applied change.");
        }

        SyncOperationEnvelopeV1 envelope =
            SyncProtocolV1.CreateEnvelope(
                change.OriginDeviceId,
                change.StoreId,
                payload.ActorUserId,
                change.OriginLocalSequence,
                CafeCashMovementSyncContract.OperationType,
                CafeCashMovementSyncContract.SchemaVersion,
                payload.MovementId,
                null,
                payload.OccurredAtUtc,
                change.PayloadJson,
                change.SourceOperationId);

        if (!string.Equals(
                envelope.PayloadHash,
                change.PayloadHash,
                StringComparison.Ordinal))
        {
            return ReconcileCurrent(
                context,
                storeId,
                expectedSequence,
                "CafeCashSync.ProjectionPayloadHashMismatch",
                "Applied C6 cash payload hash differs from reconstructed source operation.");
        }

        SharedSqliteCafeCashParticipant participant = new();
        bool originEcho = change.OriginDeviceId == localDeviceId;

        if (originEcho)
        {
            Result<bool> verification =
                participant.VerifyCanonical(
                    context,
                    envelope,
                    payload);

            if (verification.IsFailure)
            {
                return ReconcileCurrent(
                    context,
                    storeId,
                    expectedSequence,
                    verification.ErrorDetail.Code,
                    verification.ErrorDetail.Message);
            }

            _failureInjector.ThrowIfRequested(
                SyncProjectionFailurePoint.AfterDomainWriteBeforeResolution,
                expectedSequence);

            ResolveApplied(
                context.Connection,
                context.Transaction,
                storeId,
                state,
                expectedSequence,
                DateTimeOffset.UtcNow);

            SyncRemoteState resolved =
                RequireRemoteState(context, storeId);

            return Result<SyncProjectionResult>.Success(
                SyncProjectionResult.Applied(
                    expectedSequence,
                    isOriginEcho: true,
                    resolved,
                    SyncProjectionContract.OriginEchoResolvedCode));
        }

        _failureInjector.ThrowIfRequested(
            SyncProjectionFailurePoint.BeforeDomainWrite,
            expectedSequence);

        Result<bool> applied =
            participant.ApplyCanonical(
                context,
                envelope,
                payload);

        if (applied.IsFailure)
            return Result<SyncProjectionResult>.Fail(applied.ErrorDetail);

        _failureInjector.ThrowIfRequested(
            SyncProjectionFailurePoint.AfterDomainWriteBeforeResolution,
            expectedSequence);

        ResolveApplied(
            context.Connection,
            context.Transaction,
            storeId,
            state,
            expectedSequence,
            DateTimeOffset.UtcNow);

        SyncRemoteState finalState =
            RequireRemoteState(context, storeId);

        return Result<SyncProjectionResult>.Success(
            SyncProjectionResult.Applied(
                expectedSequence,
                isOriginEcho: false,
                finalState,
                SyncProjectionContract.ProjectionAppliedCode));
    }

    private static bool IsCafeCashAppliedChange(
        AppliedBusinessChangeV1 change) =>
        string.Equals(
            change.ChangeType,
            AppliedBusinessChangeContract.CashMovementPostedV1AppliedV1,
            StringComparison.Ordinal) &&
        change.SchemaVersion ==
            AppliedBusinessChangeContract.CafeCashMovementAppliedSchemaVersion;
}
