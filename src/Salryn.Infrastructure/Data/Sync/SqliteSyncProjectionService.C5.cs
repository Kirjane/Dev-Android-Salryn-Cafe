using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Data.Transactions;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Sync;

public sealed partial class SqliteSyncProjectionService
{
    private async Task<Result<SyncProjectionResult>> ProjectSaleCommittedV2Async(
        SqliteSharedTransactionContext context,
        Guid storeId,
        Guid localDeviceId,
        SyncRemoteState state,
        long expectedSequence,
        AppliedBusinessChangeV1 change,
        CancellationToken cancellationToken)
    {
        SaleCommittedV2CanonicalParseResult parse =
            SaleCommittedV2CanonicalJson.Parse(change.PayloadJson);

        if (!parse.IsValid || parse.Payload is null)
        {
            return ReconcileCurrent(
                context,
                storeId,
                expectedSequence,
                parse.ErrorCode ?? "Sync.ProjectionV2PayloadInvalid",
                parse.ErrorMessage ?? "Canonical SaleCommittedV2 payload could not be parsed.");
        }

        SaleCommittedV2 payload = parse.Payload;

        if (payload.SaleEvidence.Sale.Id != change.AggregateId)
        {
            return ReconcileCurrent(
                context,
                storeId,
                expectedSequence,
                "Sync.ProjectionV2AggregateMismatch",
                "Canonical SaleCommittedV2 sale ID does not match change aggregate identity.");
        }

        Result<CafeSaleFinancialDecision> financial =
            SaleCommittedV2Mapper.ToFinancialDecision(
                payload,
                storeId);

        if (financial.IsFailure)
        {
            return ReconcileCurrent(
                context,
                storeId,
                expectedSequence,
                financial.ErrorDetail.Code,
                financial.ErrorDetail.Message);
        }

        bool originEcho =
            change.OriginDeviceId == localDeviceId;

        if (originEcho)
        {
            LocalSaleFactVerification facts =
                LocalSaleCommittedV2FactVerifier.VerifyOriginEcho(
                    context.Connection,
                    context.Transaction,
                    change,
                    payload);

            if (!facts.Matches)
            {
                return ReconcileCurrent(
                    context,
                    storeId,
                    expectedSequence,
                    facts.Code ?? "Sync.OriginEchoV2Conflict",
                    facts.Message ?? "Origin echo C5 durable facts conflict with canonical evidence.");
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

        if (SaleIdOrReceiptExists(
                context.Connection,
                context.Transaction,
                payload.SaleEvidence.Sale.Id,
                payload.SaleEvidence.Sale.ReceiptNumber))
        {
            return ReconcileCurrent(
                context,
                storeId,
                expectedSequence,
                "Sync.ProjectionV2LocalSaleConflict",
                "Canonical remote SaleCommittedV2 conflicts with an existing local sale ID or receipt.");
        }

        SaleCommittedV1ProjectionPrerequisiteResult prerequisites =
            SaleCommittedV1ProjectionPrerequisites.Validate(
                context.Connection,
                context.Transaction,
                payload.SaleEvidence);

        if (!prerequisites.CanApply)
        {
            return ReconcileCurrent(
                context,
                storeId,
                expectedSequence,
                prerequisites.Code ?? "Sync.ProjectionV2PrerequisiteMissing",
                prerequisites.Message ?? "SaleCommittedV2 projection prerequisite is missing.");
        }

        Result<CompleteCheckoutSaleRequest> mapping =
            SaleCommittedV2Mapper.ToCore(payload);
        if (mapping.IsFailure)
        {
            return ReconcileCurrent(
                context,
                storeId,
                expectedSequence,
                mapping.ErrorDetail.Code,
                mapping.ErrorDetail.Message);
        }

        _failureInjector.ThrowIfRequested(
            SyncProjectionFailurePoint.BeforeDomainWrite,
            expectedSequence);

        Result<SharedSqliteCompletedSaleWriteResult> saleWrite =
            await _saleWriter.ExecuteC5Async(
                context,
                mapping.Value,
                cancellationToken).ConfigureAwait(false);

        if (saleWrite.IsFailure)
            return Result<SyncProjectionResult>.Fail(saleWrite.ErrorDetail);

        Result<bool> financialWrite =
            await _financialWriter.ExecuteAsync(
                context,
                financial.Value,
                cancellationToken).ConfigureAwait(false);

        if (financialWrite.IsFailure)
            return Result<SyncProjectionResult>.Fail(financialWrite.ErrorDetail);

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
    private Result<SyncProjectionResult> ProjectCafeRefund(
        SqliteSharedTransactionContext context,
        Guid storeId,
        Guid localDeviceId,
        SyncRemoteState state,
        long expectedSequence,
        AppliedBusinessChangeV1 change)
    {
        string operationType = RefundOperationTypeForChange(change.ChangeType);
        CafeRefundCanonicalParseResult parse =
            CafeRefundOperationV1CanonicalJson.Parse(
                operationType,
                change.PayloadJson);

        if (!parse.IsValid || parse.Payload is null)
        {
            return ReconcileCurrent(
                context,
                storeId,
                expectedSequence,
                parse.ErrorCode ?? "CafeRefundSync.ProjectionPayloadInvalid",
                parse.ErrorMessage ?? "Canonical refund payload could not be parsed.");
        }

        CafeRefundOperationV1 payload = parse.Payload;
        if (payload.RefundId != change.AggregateId ||
            payload.StoreId != storeId)
        {
            return ReconcileCurrent(
                context,
                storeId,
                expectedSequence,
                "CafeRefundSync.ProjectionAuthorityMismatch",
                "Canonical refund aggregate/store authority differs from applied change.");
        }

        SyncOperationEnvelopeV1 envelope =
            SyncProtocolV1.CreateEnvelope(
                change.OriginDeviceId,
                change.StoreId,
                payload.ActorUserId,
                change.OriginLocalSequence,
                payload.OperationType,
                CafeRefundSyncContract.SchemaVersion,
                payload.RefundId,
                payload.ExpectedVersion,
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
                "CafeRefundSync.ProjectionPayloadHashMismatch",
                "Applied refund payload hash differs from reconstructed source operation.");
        }

        bool originEcho = change.OriginDeviceId == localDeviceId;
        SharedSqliteCafeRefundParticipant participant = new();

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

    private static bool IsCafeRefundAppliedChange(
        AppliedBusinessChangeV1 change) =>
        change.SchemaVersion ==
            AppliedBusinessChangeContract.CafeRefundAppliedSchemaVersion &&
        (
            string.Equals(
                change.ChangeType,
                AppliedBusinessChangeContract.SaleRefundRequestedV1AppliedV1,
                StringComparison.Ordinal) ||
            string.Equals(
                change.ChangeType,
                AppliedBusinessChangeContract.SaleRefundResolvedV1AppliedV1,
                StringComparison.Ordinal) ||
            string.Equals(
                change.ChangeType,
                AppliedBusinessChangeContract.SaleRefundCommittedV1AppliedV1,
                StringComparison.Ordinal)
        );

    private static string RefundOperationTypeForChange(string changeType) =>
        changeType switch
        {
            AppliedBusinessChangeContract.SaleRefundRequestedV1AppliedV1 =>
                CafeRefundSyncContract.Requested,
            AppliedBusinessChangeContract.SaleRefundResolvedV1AppliedV1 =>
                CafeRefundSyncContract.Resolved,
            AppliedBusinessChangeContract.SaleRefundCommittedV1AppliedV1 =>
                CafeRefundSyncContract.Committed,
            _ => throw new InvalidOperationException(
                "Unsupported C5 refund applied change.")
        };

}
