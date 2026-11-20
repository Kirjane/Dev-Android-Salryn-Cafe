using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Data.Repositories.Sales;
using Salryn.Infrastructure.Data.Sync;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Services;

public sealed class SqliteCafeRefundService :
    SqliteRepositoryBase,
    ICafeRefundService
{
    private readonly SqliteSyncOutboxStore _outbox;

    public SqliteCafeRefundService(
        SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
        _outbox = new SqliteSyncOutboxStore(
            connectionFactory ??
            throw new ArgumentNullException(nameof(connectionFactory)));
    }

    public Result<CafeRefundMutationResult> Request(
        CafeRefundRequestMutation mutation)
    {
        string? validation = ValidateRequestMutation(mutation);
        if (validation is not null)
        {
            return Result<CafeRefundMutationResult>.Fail(
                Error.Validation("CafeRefund.RequestInvalid", validation));
        }

        string payloadHash = HashRequest(mutation);

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Result authority = ValidateAuthority(
                connection,
                transaction,
                mutation.StoreId,
                mutation.ActorUserId,
                SalrynPermissions.SalesRefundRequest);
            if (authority.IsFailure)
            {
                transaction.Rollback();
                return Result<CafeRefundMutationResult>.Fail(authority.ErrorDetail);
            }

            Result<CafeRefundMutationResult>? replay =
                TryReplayMutation(
                    connection,
                    transaction,
                    mutation.OperationId,
                    mutation.DeviceId,
                    mutation.StoreId,
                    mutation.RefundId,
                    mutation.SaleId,
                    CafeRefundOperationTypes.Requested,
                    payloadHash,
                    mutation.ExpectedVersion,
                    mutation.ActorUserId);
            if (replay is not null)
            {
                transaction.Commit();
                return replay;
            }

            if (mutation.ExpectedVersion != 0)
            {
                transaction.Rollback();
                return Result<CafeRefundMutationResult>.Fail(Error.Conflict(
                    "CafeRefund.RequestVersionConflict",
                    "A new refund request must start from expected version 0."));
            }

            EnsureCompletedSale(
                connection,
                transaction,
                mutation.SaleId);

            if (RefundExists(connection, transaction, mutation.RefundId))
            {
                transaction.Rollback();
                return Result<CafeRefundMutationResult>.Fail(Error.Conflict(
                    "CafeRefund.RefundIdConflict",
                    "Refund ID already exists without matching operation evidence."));
            }

            List<CafeRefundLineSnapshot> lines = BuildRequestedLines(
                connection,
                transaction,
                mutation);

            long total = lines.Aggregate(
                0L,
                (current, line) => checked(current + line.RefundAmountCents));

            if (total <= 0)
            {
                transaction.Rollback();
                return Result<CafeRefundMutationResult>.Fail(Error.Validation(
                    "CafeRefund.ZeroValueRequest",
                    "Refund request must have a positive server-calculated value."));
            }

            using (SqliteCommand insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO cafe_sale_refunds(
                        refund_id,sale_id,store_id,current_version,state,reason,
                        requested_by_user_id,requested_at_utc,request_hash,
                        resolved_by_user_id,resolved_at_utc,resolution_reason,
                        committed_by_user_id,committed_at_utc,total_refund_cents)
                    VALUES(
                        $refund,$sale,$store,1,'Requested',$reason,
                        $actor,$at,$hash,NULL,NULL,NULL,NULL,NULL,NULL);
                    """;
                insert.Parameters.AddWithValue("$refund", mutation.RefundId.ToString("D"));
                insert.Parameters.AddWithValue("$sale", mutation.SaleId.ToString("D"));
                insert.Parameters.AddWithValue("$store", mutation.StoreId.ToString("D"));
                insert.Parameters.AddWithValue("$reason", mutation.Reason.Trim());
                insert.Parameters.AddWithValue("$actor", mutation.ActorUserId.ToString("D"));
                insert.Parameters.AddWithValue("$at", mutation.OccurredAtUtc.ToUniversalTime().ToString("O"));
                insert.Parameters.AddWithValue("$hash", payloadHash);
                insert.ExecuteNonQuery();
            }

            foreach (CafeRefundLineSnapshot line in lines)
            {
                InsertRefundLine(connection, transaction, line);
            }

            InsertOperationAndEvent(
                connection,
                transaction,
                mutation.OperationId,
                mutation.DeviceId,
                mutation.StoreId,
                mutation.RefundId,
                mutation.SaleId,
                CafeRefundOperationTypes.Requested,
                payloadHash,
                expectedVersion: 0,
                resultingVersion: 1,
                resultState: CafeRefundStates.Requested,
                mutation.ActorUserId,
                mutation.OccurredAtUtc,
                action: CafeRefundStates.Requested,
                fromState: null,
                toState: CafeRefundStates.Requested,
                mutation.Reason);

            EnqueueRefundSync(
                connection,
                transaction,
                mutation.OperationId,
                mutation.DeviceId,
                mutation.StoreId,
                mutation.ActorUserId,
                mutation.RefundId,
                mutation.SaleId,
                CafeRefundSyncContract.Requested,
                expectedVersion: 0,
                resultingVersion: 1,
                fromState: null,
                toState: CafeRefundStates.Requested,
                requestReason: mutation.Reason,
                resolutionReason: null,
                requestHash: payloadHash,
                cashierSessionId: null,
                mutation.OccurredAtUtc,
                total,
                lines,
                Array.Empty<CafeRefundSettlementSnapshot>());

            transaction.Commit();

            return Result<CafeRefundMutationResult>.Success(
                new CafeRefundMutationResult(
                    mutation.RefundId,
                    1,
                    CafeRefundStates.Requested,
                    false));
        }
        catch (RefundValidationException ex)
        {
            return Result<CafeRefundMutationResult>.Fail(
                Error.Conflict(ex.Code, ex.Message));
        }
        catch (Exception ex) when (
            ex is SqliteException or InvalidOperationException or OverflowException or FormatException)
        {
            return Result<CafeRefundMutationResult>.Fail(
                Error.Unexpected("CafeRefund.RequestFailed", ex.Message));
        }
    }

    public Result<CafeRefundMutationResult> Resolve(
        CafeRefundResolutionMutation mutation)
    {
        string? validation = ValidateResolutionMutation(mutation);
        if (validation is not null)
        {
            return Result<CafeRefundMutationResult>.Fail(
                Error.Validation("CafeRefund.ResolveInvalid", validation));
        }

        string payloadHash = HashResolution(mutation);
        string targetState = mutation.Approve
            ? CafeRefundStates.Approved
            : CafeRefundStates.Rejected;

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Result authority = ValidateAuthority(
                connection,
                transaction,
                mutation.StoreId,
                mutation.ActorUserId,
                SalrynPermissions.SalesRefundApprove);
            if (authority.IsFailure)
            {
                transaction.Rollback();
                return Result<CafeRefundMutationResult>.Fail(authority.ErrorDetail);
            }

            Result<CafeRefundMutationResult>? replay =
                TryReplayMutation(
                    connection,
                    transaction,
                    mutation.OperationId,
                    mutation.DeviceId,
                    mutation.StoreId,
                    mutation.RefundId,
                    mutation.SaleId,
                    CafeRefundOperationTypes.Resolved,
                    payloadHash,
                    mutation.ExpectedVersion,
                    mutation.ActorUserId);
            if (replay is not null)
            {
                transaction.Commit();
                return replay;
            }

            CafeRefundSummary refund =
                ReadRefundSummary(
                    connection,
                    transaction,
                    mutation.RefundId)
                ?? throw new RefundValidationException(
                    "CafeRefund.NotFound",
                    "Refund request was not found.");

            if (refund.SaleId != mutation.SaleId ||
                refund.StoreId != mutation.StoreId)
            {
                throw new RefundValidationException(
                    "CafeRefund.AuthorityMismatch",
                    "Refund sale/store authority does not match the mutation.");
            }

            if (refund.Version != mutation.ExpectedVersion ||
                !string.Equals(
                    refund.State,
                    CafeRefundStates.Requested,
                    StringComparison.Ordinal))
            {
                throw new RefundValidationException(
                    "CafeRefund.ResolveVersionConflict",
                    "Only the current Requested refund version may be resolved.");
            }

            int resultingVersion = checked(mutation.ExpectedVersion + 1);

            using (SqliteCommand update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE cafe_sale_refunds
                    SET current_version=$version,
                        state=$state,
                        resolved_by_user_id=$actor,
                        resolved_at_utc=$at,
                        resolution_reason=$reason
                    WHERE refund_id=$refund
                      AND current_version=$expected
                      AND state='Requested';
                    """;
                update.Parameters.AddWithValue("$version", resultingVersion);
                update.Parameters.AddWithValue("$state", targetState);
                update.Parameters.AddWithValue("$actor", mutation.ActorUserId.ToString("D"));
                update.Parameters.AddWithValue("$at", mutation.OccurredAtUtc.ToUniversalTime().ToString("O"));
                update.Parameters.AddWithValue(
                    "$reason",
                    string.IsNullOrWhiteSpace(mutation.Reason)
                        ? DBNull.Value
                        : mutation.Reason.Trim());
                update.Parameters.AddWithValue("$refund", mutation.RefundId.ToString("D"));
                update.Parameters.AddWithValue("$expected", mutation.ExpectedVersion);

                if (update.ExecuteNonQuery() != 1)
                {
                    throw new RefundValidationException(
                        "CafeRefund.ResolveConcurrentConflict",
                        "Refund resolution lost version ownership.");
                }
            }

            InsertOperationAndEvent(
                connection,
                transaction,
                mutation.OperationId,
                mutation.DeviceId,
                mutation.StoreId,
                mutation.RefundId,
                mutation.SaleId,
                CafeRefundOperationTypes.Resolved,
                payloadHash,
                mutation.ExpectedVersion,
                resultingVersion,
                targetState,
                mutation.ActorUserId,
                mutation.OccurredAtUtc,
                action: mutation.Approve ? "Approved" : "Rejected",
                fromState: CafeRefundStates.Requested,
                toState: targetState,
                mutation.Reason);

            List<CafeRefundLineSnapshot> retainedLines =
                ReadRefundLines(
                    connection,
                    transaction,
                    mutation.RefundId);
            long retainedTotal = retainedLines.Aggregate(
                0L,
                (current, line) =>
                    checked(current + line.RefundAmountCents));

            EnqueueRefundSync(
                connection,
                transaction,
                mutation.OperationId,
                mutation.DeviceId,
                mutation.StoreId,
                mutation.ActorUserId,
                mutation.RefundId,
                mutation.SaleId,
                CafeRefundSyncContract.Resolved,
                mutation.ExpectedVersion,
                resultingVersion,
                CafeRefundStates.Requested,
                targetState,
                refund.Reason,
                Clean(mutation.Reason),
                refund.RequestHash,
                cashierSessionId: null,
                mutation.OccurredAtUtc,
                retainedTotal,
                retainedLines,
                Array.Empty<CafeRefundSettlementSnapshot>());

            transaction.Commit();

            return Result<CafeRefundMutationResult>.Success(
                new CafeRefundMutationResult(
                    mutation.RefundId,
                    resultingVersion,
                    targetState,
                    false));
        }
        catch (RefundValidationException ex)
        {
            return Result<CafeRefundMutationResult>.Fail(
                Error.Conflict(ex.Code, ex.Message));
        }
        catch (Exception ex) when (
            ex is SqliteException or InvalidOperationException or OverflowException or FormatException)
        {
            return Result<CafeRefundMutationResult>.Fail(
                Error.Unexpected("CafeRefund.ResolveFailed", ex.Message));
        }
    }

    public Result<CafeRefundCommitResult> Commit(
        CafeRefundCommitMutation mutation)
    {
        string? validation = ValidateCommitMutation(mutation);
        if (validation is not null)
        {
            return Result<CafeRefundCommitResult>.Fail(
                Error.Validation("CafeRefund.CommitInvalid", validation));
        }

        string payloadHash = HashCommit(mutation);

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Result authority = ValidateAuthority(
                connection,
                transaction,
                mutation.StoreId,
                mutation.ActorUserId,
                SalrynPermissions.SalesRefundApprove);
            if (authority.IsFailure)
            {
                transaction.Rollback();
                return Result<CafeRefundCommitResult>.Fail(authority.ErrorDetail);
            }

            Result<CafeRefundCommitResult>? replay =
                TryReplayCommit(
                    connection,
                    transaction,
                    mutation,
                    payloadHash);
            if (replay is not null)
            {
                transaction.Commit();
                return replay;
            }

            CafeRefundSummary refund =
                ReadRefundSummary(
                    connection,
                    transaction,
                    mutation.RefundId)
                ?? throw new RefundValidationException(
                    "CafeRefund.NotFound",
                    "Approved refund was not found.");

            if (refund.SaleId != mutation.SaleId ||
                refund.StoreId != mutation.StoreId)
            {
                throw new RefundValidationException(
                    "CafeRefund.AuthorityMismatch",
                    "Refund sale/store authority does not match the commit.");
            }

            if (refund.Version != mutation.ExpectedVersion ||
                !string.Equals(
                    refund.State,
                    CafeRefundStates.Approved,
                    StringComparison.Ordinal))
            {
                throw new RefundValidationException(
                    "CafeRefund.CommitVersionConflict",
                    "Only the current Approved refund version may be committed.");
            }

            SaleSettlementAuthority sale =
                ReadSaleSettlementAuthority(
                    connection,
                    transaction,
                    mutation.SaleId);

            if (sale.Status != SaleStatus.Completed)
            {
                throw new RefundValidationException(
                    "CafeRefund.SourceSaleNotRefundable",
                    "Only a completed, non-voided Sale may be refunded.");
            }

            List<CafeRefundLineSnapshot> lines =
                ReadRefundLines(
                    connection,
                    transaction,
                    mutation.RefundId);

            if (lines.Count == 0)
            {
                throw new RefundValidationException(
                    "CafeRefund.LinesMissing",
                    "Approved refund has no retained line evidence.");
            }

            RevalidateRemainingQuantities(
                connection,
                transaction,
                mutation.RefundId,
                lines);

            long refundTotal = lines.Aggregate(
                0L,
                (current, line) => checked(current + line.RefundAmountCents));

            if (refundTotal <= 0)
            {
                throw new RefundValidationException(
                    "CafeRefund.ZeroValueCommit",
                    "Refund commit requires a positive retained refund value.");
            }

            SettlementPlan settlement =
                ValidateSettlementPlan(
                    connection,
                    transaction,
                    mutation,
                    sale,
                    refundTotal);

            foreach (CafeRefundLineSnapshot line in lines)
            {
                if (string.Equals(
                        line.StockDisposition,
                        CafeRefundStockDispositions.Restock,
                        StringComparison.Ordinal))
                {
                    ApplyReturnRestock(
                        connection,
                        transaction,
                        mutation,
                        line);
                }
            }

            if (settlement.CashPart is not null)
            {
                PostCashRefund(
                    connection,
                    transaction,
                    mutation,
                    settlement.CashPart);
            }

            if (settlement.ArPart is not null)
            {
                PostArRefundCredit(
                    connection,
                    transaction,
                    mutation,
                    sale,
                    settlement.ArPart);
            }

            int resultingVersion = checked(mutation.ExpectedVersion + 1);

            using (SqliteCommand update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE cafe_sale_refunds
                    SET current_version=$version,
                        state='Committed',
                        committed_by_user_id=$actor,
                        committed_at_utc=$at,
                        total_refund_cents=$total
                    WHERE refund_id=$refund
                      AND current_version=$expected
                      AND state='Approved';
                    """;
                update.Parameters.AddWithValue("$version", resultingVersion);
                update.Parameters.AddWithValue("$actor", mutation.ActorUserId.ToString("D"));
                update.Parameters.AddWithValue("$at", mutation.OccurredAtUtc.ToUniversalTime().ToString("O"));
                update.Parameters.AddWithValue("$total", refundTotal);
                update.Parameters.AddWithValue("$refund", mutation.RefundId.ToString("D"));
                update.Parameters.AddWithValue("$expected", mutation.ExpectedVersion);

                if (update.ExecuteNonQuery() != 1)
                {
                    throw new RefundValidationException(
                        "CafeRefund.CommitConcurrentConflict",
                        "Refund commit lost version ownership.");
                }
            }

            InsertOperationAndEvent(
                connection,
                transaction,
                mutation.OperationId,
                mutation.DeviceId,
                mutation.StoreId,
                mutation.RefundId,
                mutation.SaleId,
                CafeRefundOperationTypes.Committed,
                payloadHash,
                mutation.ExpectedVersion,
                resultingVersion,
                CafeRefundStates.Committed,
                mutation.ActorUserId,
                mutation.OccurredAtUtc,
                action: "Committed",
                fromState: CafeRefundStates.Approved,
                toState: CafeRefundStates.Committed,
                reason: refund.Reason);

            List<CafeRefundSettlementSnapshot> retainedSettlements =
                ReadRefundSettlements(
                    connection,
                    transaction,
                    mutation.RefundId);

            EnqueueRefundSync(
                connection,
                transaction,
                mutation.OperationId,
                mutation.DeviceId,
                mutation.StoreId,
                mutation.ActorUserId,
                mutation.RefundId,
                mutation.SaleId,
                CafeRefundSyncContract.Committed,
                mutation.ExpectedVersion,
                resultingVersion,
                CafeRefundStates.Approved,
                CafeRefundStates.Committed,
                refund.Reason,
                refund.ResolutionReason,
                refund.RequestHash,
                mutation.CashierSessionId,
                mutation.OccurredAtUtc,
                refundTotal,
                lines,
                retainedSettlements);

            transaction.Commit();

            return Result<CafeRefundCommitResult>.Success(
                new CafeRefundCommitResult(
                    mutation.RefundId,
                    resultingVersion,
                    CafeRefundStates.Committed,
                    refundTotal,
                    false));
        }
        catch (RefundValidationException ex)
        {
            return Result<CafeRefundCommitResult>.Fail(
                Error.Conflict(ex.Code, ex.Message));
        }
        catch (Exception ex) when (
            ex is SqliteException or InvalidOperationException or OverflowException or FormatException)
        {
            return Result<CafeRefundCommitResult>.Fail(
                Error.Unexpected("CafeRefund.CommitFailed", ex.Message));
        }
    }

    public Result<CafeRefundDetail> Get(Guid refundId)
    {
        if (refundId == Guid.Empty)
        {
            return Result<CafeRefundDetail>.Fail(
                Error.Validation(
                    "CafeRefund.InvalidRefundId",
                    "Refund ID is required."));
        }

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            CafeRefundSummary? refund =
                ReadRefundSummary(connection, transaction, refundId);
            if (refund is null)
            {
                transaction.Rollback();
                return Result<CafeRefundDetail>.Fail(
                    Error.NotFound(
                        "CafeRefund.NotFound",
                        "Refund was not found."));
            }

            List<CafeRefundLineSnapshot> lines =
                ReadRefundLines(connection, transaction, refundId);
            List<CafeRefundSettlementSnapshot> settlements =
                ReadRefundSettlements(connection, transaction, refundId);

            transaction.Commit();

            return Result<CafeRefundDetail>.Success(
                new CafeRefundDetail(
                    refund,
                    lines,
                    settlements));
        }
        catch (Exception ex) when (
            ex is SqliteException or InvalidOperationException or FormatException)
        {
            return Result<CafeRefundDetail>.Fail(
                Error.Unexpected("CafeRefund.ReadFailed", ex.Message));
        }
    }

    private static List<CafeRefundLineSnapshot> BuildRequestedLines(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeRefundRequestMutation mutation)
    {
        HashSet<Guid> saleItems = new();
        HashSet<Guid> refundLines = new();
        List<CafeRefundLineSnapshot> result = new();

        foreach (CafeRefundLineMutation requested in mutation.Lines)
        {
            if (requested.RefundLineId == Guid.Empty ||
                !refundLines.Add(requested.RefundLineId))
            {
                throw new RefundValidationException(
                    "CafeRefund.InvalidRefundLineId",
                    "Refund line IDs must be non-empty and unique.");
            }

            if (requested.SaleItemId == Guid.Empty ||
                !saleItems.Add(requested.SaleItemId))
            {
                throw new RefundValidationException(
                    "CafeRefund.InvalidSaleItemId",
                    "Sale item IDs must be non-empty and unique within one refund.");
            }

            if (requested.QuantityX1000 <= 0)
            {
                throw new RefundValidationException(
                    "CafeRefund.InvalidQuantity",
                    "Refund quantity must be positive.");
            }

            if (!CafeRefundStockDispositions.IsKnown(
                    requested.StockDisposition))
            {
                throw new RefundValidationException(
                    "CafeRefund.InvalidStockDisposition",
                    "Refund stock disposition is invalid.");
            }

            SaleItemAuthority item =
                ReadSaleItemAuthority(
                    connection,
                    transaction,
                    mutation.SaleId,
                    requested.SaleItemId);

            (long priorQty, long priorAmount) =
                ReadPriorCommittedReturn(
                    connection,
                    transaction,
                    requested.SaleItemId);

            long remaining = checked(
                item.OriginalQuantityX1000 - priorQty);

            if (requested.QuantityX1000 > remaining)
            {
                throw new RefundValidationException(
                    "CafeRefund.OverRefundQuantity",
                    "Refund quantity exceeds remaining refundable quantity.");
            }

            if (string.Equals(
                    requested.StockDisposition,
                    CafeRefundStockDispositions.Restock,
                    StringComparison.Ordinal) &&
                !item.TrackStock)
            {
                throw new RefundValidationException(
                    "CafeRefund.RestockNotStockTracked",
                    "A non-stock-tracked SaleItem cannot be restocked.");
            }

            long cumulativeQty = checked(
                priorQty + requested.QuantityX1000);

            long cumulativeAmount =
                cumulativeQty == item.OriginalQuantityX1000
                    ? item.LineTotalCents
                    : checked((long)decimal.Round(
                        (decimal)item.LineTotalCents *
                        cumulativeQty /
                        item.OriginalQuantityX1000,
                        0,
                        MidpointRounding.AwayFromZero));

            long refundAmount = checked(
                cumulativeAmount - priorAmount);

            if (refundAmount < 0)
            {
                throw new RefundValidationException(
                    "CafeRefund.AmountEvidenceConflict",
                    "Prior refund evidence exceeds deterministic cumulative line value.");
            }

            result.Add(new CafeRefundLineSnapshot(
                requested.RefundLineId,
                mutation.RefundId,
                requested.SaleItemId,
                item.ProductId,
                item.ProductNameSnapshot,
                item.OriginalQuantityX1000,
                requested.QuantityX1000,
                item.UnitPriceCents,
                item.DiscountCents,
                refundAmount,
                requested.StockDisposition,
                Clean(requested.OperatorNote)));
        }

        return result;
    }

    private static SettlementPlan ValidateSettlementPlan(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeRefundCommitMutation mutation,
        SaleSettlementAuthority sale,
        long refundTotal)
    {
        Dictionary<string, CafeRefundSettlementMutation> parts =
            new(StringComparer.Ordinal);

        long sum = 0;

        foreach (CafeRefundSettlementMutation part in mutation.Settlements)
        {
            if (part.SettlementPartId == Guid.Empty)
            {
                throw new RefundValidationException(
                    "CafeRefund.InvalidSettlementId",
                    "Settlement part ID is required.");
            }

            if (!CafeRefundSettlementMethods.IsKnown(part.Method))
            {
                throw new RefundValidationException(
                    "CafeRefund.InvalidSettlementMethod",
                    "Refund settlement supports Cash and AccountsReceivable only.");
            }

            if (part.AmountCents <= 0)
            {
                throw new RefundValidationException(
                    "CafeRefund.InvalidSettlementAmount",
                    "Refund settlement amount must be positive.");
            }

            if (!parts.TryAdd(part.Method, part))
            {
                throw new RefundValidationException(
                    "CafeRefund.DuplicateSettlementMethod",
                    "Each refund settlement method may appear at most once.");
            }

            sum = checked(sum + part.AmountCents);
        }

        if (sum != refundTotal)
        {
            throw new RefundValidationException(
                "CafeRefund.SettlementTotalMismatch",
                "Refund settlement parts must exactly equal the server-calculated refund total.");
        }

        parts.TryGetValue(
            CafeRefundSettlementMethods.Cash,
            out CafeRefundSettlementMutation? cashPart);
        parts.TryGetValue(
            CafeRefundSettlementMethods.AccountsReceivable,
            out CafeRefundSettlementMutation? arPart);

        long priorCashRefund = ReadPriorSettlementTotal(
            connection,
            transaction,
            mutation.SaleId,
            CafeRefundSettlementMethods.Cash);

        long priorArRefund = ReadPriorSettlementTotal(
            connection,
            transaction,
            mutation.SaleId,
            CafeRefundSettlementMethods.AccountsReceivable);

        long retainedCashAuthority = checked(
            sale.CashTenderCents - sale.ChangeCents);

        if (cashPart is not null)
        {
            long remainingCash = checked(
                retainedCashAuthority - priorCashRefund);

            if (remainingCash < 0 ||
                cashPart.AmountCents > remainingCash)
            {
                throw new RefundValidationException(
                    "CafeRefund.CashAuthorityExceeded",
                    "Cash refund exceeds remaining retained Cash authority from the original Sale.");
            }

            ValidateCashierSession(
                connection,
                transaction,
                mutation.CashierSessionId,
                mutation.ActorUserId);
        }

        if (arPart is not null)
        {
            long remainingAr = checked(
                sale.AccountsReceivableTenderCents - priorArRefund);

            if (remainingAr < 0 ||
                arPart.AmountCents > remainingAr)
            {
                throw new RefundValidationException(
                    "CafeRefund.ArAuthorityExceeded",
                    "AR refund credit exceeds remaining original AccountsReceivable authority.");
            }

            if (!sale.CustomerId.HasValue)
            {
                throw new RefundValidationException(
                    "CafeRefund.ArCustomerMissing",
                    "AR refund credit requires the original Sale customer.");
            }

            EnsureDeterministicArAllocation(
                connection,
                transaction,
                sale.CustomerId.Value,
                mutation.SaleId,
                arPart.AmountCents);
        }

        return new SettlementPlan(cashPart, arPart);
    }

    private static void PostCashRefund(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeRefundCommitMutation mutation,
        CafeRefundSettlementMutation part)
    {
        Guid movementId =
            DeterministicGuid(
                "salryn-c5-refund-cash-v1",
                part.SettlementPartId.ToString("D"));

        Result cash = SqliteCashMovementPersistence.Insert(
            connection,
            transaction,
            new CashMovement
            {
                Id = movementId,
                CashierSessionId = mutation.CashierSessionId,
                ActorUserId = mutation.ActorUserId,
                MovementType = CashMovementType.CashRefund,
                Delta = Money.FromCents(-part.AmountCents),
                ReferenceType = "REFUND",
                ReferenceId = mutation.RefundId,
                ReversalOfMovementId = null,
                Reason = "Cafe sale refund",
                CreatedAt = mutation.OccurredAtUtc.ToUniversalTime()
            });

        if (cash.IsFailure)
        {
            throw new RefundValidationException(
                cash.ErrorDetail.Code,
                cash.ErrorDetail.Message);
        }

        InsertSettlement(
            connection,
            transaction,
            mutation.RefundId,
            part,
            movementId,
            receivableEntryId: null,
            mutation.OccurredAtUtc);
    }

    private static void PostArRefundCredit(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeRefundCommitMutation mutation,
        SaleSettlementAuthority sale,
        CafeRefundSettlementMutation part)
    {
        Guid customerId = sale.CustomerId
            ?? throw new RefundValidationException(
                "CafeRefund.ArCustomerMissing",
                "AR refund requires original customer evidence.");

        long currentBalance =
            ReadCustomerBalance(
                connection,
                transaction,
                customerId);

        if (currentBalance < part.AmountCents)
        {
            throw new RefundValidationException(
                "CafeRefund.ArBalanceConflict",
                "Current customer receivable balance cannot absorb the requested refund credit.");
        }

        Guid entryId =
            DeterministicGuid(
                "salryn-c5-refund-ar-v1",
                part.SettlementPartId.ToString("D"));

        using (SqliteCommand insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO accounts_receivable_entries(
                    id,customer_id,sale_id,collection_id,entry_type,
                    debit_cents,credit_cents,balance_after_cents,
                    due_date,note,created_by_user_id,created_at,refund_id)
                VALUES(
                    $id,$customer,$sale,NULL,'AdjustmentCredit',
                    0,$credit,$balance,NULL,$note,$actor,$at,$refund);
                """;
            insert.Parameters.AddWithValue("$id", entryId.ToString("D"));
            insert.Parameters.AddWithValue("$customer", customerId.ToString("D"));
            insert.Parameters.AddWithValue("$sale", mutation.SaleId.ToString("D"));
            insert.Parameters.AddWithValue("$credit", part.AmountCents);
            insert.Parameters.AddWithValue("$balance", currentBalance - part.AmountCents);
            insert.Parameters.AddWithValue("$note", "Cafe sale refund AR credit");
            insert.Parameters.AddWithValue("$actor", mutation.ActorUserId.ToString("D"));
            insert.Parameters.AddWithValue("$at", mutation.OccurredAtUtc.ToUniversalTime().ToString("O"));
            insert.Parameters.AddWithValue("$refund", mutation.RefundId.ToString("D"));
            insert.ExecuteNonQuery();
        }

        InsertSettlement(
            connection,
            transaction,
            mutation.RefundId,
            part,
            cashMovementId: null,
            entryId,
            mutation.OccurredAtUtc);
    }

    private static void ApplyReturnRestock(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeRefundCommitMutation mutation,
        CafeRefundLineSnapshot line)
    {
        using SqliteCommand read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = """
            SELECT stock_on_hand_x1000,track_stock,is_active
            FROM products
            WHERE id=$product
            LIMIT 1;
            """;
        read.Parameters.AddWithValue("$product", line.ProductId.ToString("D"));

        using SqliteDataReader reader = read.ExecuteReader();
        if (!reader.Read() ||
            reader.GetInt32(1) != 1 ||
            reader.GetInt32(2) != 1)
        {
            throw new RefundValidationException(
                "CafeRefund.RestockProductUnavailable",
                "Restock disposition requires an active stock-tracked Product.");
        }

        long current = reader.GetInt64(0);
        reader.Close();

        long next = checked(
            current + line.RefundQuantityX1000);

        using (SqliteCommand update = connection.CreateCommand())
        {
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE products
                SET stock_on_hand_x1000=$stock,
                    updated_at=$at
                WHERE id=$product
                  AND track_stock=1
                  AND is_active=1;
                """;
            update.Parameters.AddWithValue("$stock", next);
            update.Parameters.AddWithValue("$at", mutation.OccurredAtUtc.ToUniversalTime().ToString("O"));
            update.Parameters.AddWithValue("$product", line.ProductId.ToString("D"));
            if (update.ExecuteNonQuery() != 1)
            {
                throw new RefundValidationException(
                    "CafeRefund.RestockConcurrentConflict",
                    "Product stock was not available for refund restock.");
            }
        }

        Guid movementId =
            DeterministicGuid(
                "salryn-c5-refund-restock-v1",
                line.RefundLineId.ToString("D"));

        using SqliteCommand movement = connection.CreateCommand();
        movement.Transaction = transaction;
        movement.CommandText = """
            INSERT INTO stock_movements(
                id,product_id,movement_type,qty_change_x1000,
                cost_price_cents,reference_type,reference_id,
                reason,user_id,created_at)
            VALUES(
                $id,$product,$type,$qty,NULL,'REFUND',$refund,
                $reason,$actor,$at);
            """;
        movement.Parameters.AddWithValue("$id", movementId.ToString("D"));
        movement.Parameters.AddWithValue("$product", line.ProductId.ToString("D"));
        movement.Parameters.AddWithValue(
            "$type",
            Convert.ToInt32(StockMovementType.ReturnRestock));
        movement.Parameters.AddWithValue("$qty", line.RefundQuantityX1000);
        movement.Parameters.AddWithValue("$refund", mutation.RefundId.ToString("D"));
        movement.Parameters.AddWithValue("$reason", "Cafe refund return restock");
        movement.Parameters.AddWithValue("$actor", mutation.ActorUserId.ToString("D"));
        movement.Parameters.AddWithValue("$at", mutation.OccurredAtUtc.ToUniversalTime().ToString("O"));
        movement.ExecuteNonQuery();
    }

    private static void EnsureDeterministicArAllocation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid customerId,
        Guid saleId,
        long requestedCredit)
    {
        using SqliteCommand source = connection.CreateCommand();
        source.Transaction = transaction;
        source.CommandText = """
            SELECT created_at
            FROM accounts_receivable_entries
            WHERE customer_id=$customer
              AND sale_id=$sale
              AND entry_type='CreditSale'
            LIMIT 1;
            """;
        source.Parameters.AddWithValue("$customer", customerId.ToString("D"));
        source.Parameters.AddWithValue("$sale", saleId.ToString("D"));
        object? sourceAtValue = source.ExecuteScalar();

        if (sourceAtValue is not string sourceAt)
        {
            throw new RefundValidationException(
                "CafeRefund.ArDebitEvidenceMissing",
                "Original AR debit evidence is missing for the Sale.");
        }

        using SqliteCommand laterCredits = connection.CreateCommand();
        laterCredits.Transaction = transaction;
        laterCredits.CommandText = """
            SELECT COUNT(*)
            FROM accounts_receivable_entries
            WHERE customer_id=$customer
              AND created_at>$sourceAt
              AND entry_type IN ('Collection','AdjustmentCredit')
              AND refund_id IS NULL;
            """;
        laterCredits.Parameters.AddWithValue("$customer", customerId.ToString("D"));
        laterCredits.Parameters.AddWithValue("$sourceAt", sourceAt);

        if (Convert.ToInt64(laterCredits.ExecuteScalar() ?? 0L) != 0)
        {
            throw new RefundValidationException(
                "CafeRefund.ArAllocationNeedsReview",
                "Later AR credits/collections prevent deterministic allocation back to this Sale.");
        }

        long balance = ReadCustomerBalance(
            connection,
            transaction,
            customerId);

        if (balance < requestedCredit)
        {
            throw new RefundValidationException(
                "CafeRefund.ArBalanceConflict",
                "Customer balance is lower than the proposed AR refund credit.");
        }
    }

    private static void RevalidateRemainingQuantities(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid currentRefundId,
        IReadOnlyList<CafeRefundLineSnapshot> lines)
    {
        foreach (CafeRefundLineSnapshot line in lines)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT COALESCE(SUM(l.refund_quantity_x1000),0)
                FROM cafe_sale_refund_lines l
                INNER JOIN cafe_sale_refunds r
                  ON r.refund_id=l.refund_id
                WHERE l.sale_item_id=$item
                  AND r.state='Committed'
                  AND r.refund_id<>$refund;
                """;
            command.Parameters.AddWithValue("$item", line.SaleItemId.ToString("D"));
            command.Parameters.AddWithValue("$refund", currentRefundId.ToString("D"));
            long prior = Convert.ToInt64(command.ExecuteScalar() ?? 0L);

            if (checked(prior + line.RefundQuantityX1000) >
                line.OriginalQuantityX1000)
            {
                throw new RefundValidationException(
                    "CafeRefund.ConcurrentOverRefund",
                    "Another committed refund consumed the remaining refundable quantity.");
            }
        }
    }

    private static void EnsureCompletedSale(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid saleId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT status
            FROM sales
            WHERE id=$sale
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$sale", saleId.ToString("D"));
        object? value = command.ExecuteScalar();

        if (value is null or DBNull)
        {
            throw new RefundValidationException(
                "CafeRefund.SaleMissing",
                "Source Sale was not found.");
        }

        if ((SaleStatus)Convert.ToInt32(value) != SaleStatus.Completed)
        {
            throw new RefundValidationException(
                "CafeRefund.SourceSaleNotRefundable",
                "Voided or non-completed Sale cannot be refunded.");
        }
    }

    private static SaleSettlementAuthority ReadSaleSettlementAuthority(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid saleId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT s.status,s.customer_id,s.change_cents,
                   COALESCE(SUM(CASE WHEN p.method=$cash THEN p.amount_cents ELSE 0 END),0),
                   COALESCE(SUM(CASE WHEN p.method=$ar THEN p.amount_cents ELSE 0 END),0)
            FROM sales s
            LEFT JOIN payments p ON p.sale_id=s.id
            WHERE s.id=$sale
            GROUP BY s.id,s.status,s.customer_id,s.change_cents;
            """;
        command.Parameters.AddWithValue("$sale", saleId.ToString("D"));
        command.Parameters.AddWithValue("$cash", Convert.ToInt32(PaymentMethod.Cash));
        command.Parameters.AddWithValue("$ar", Convert.ToInt32(PaymentMethod.AccountsReceivable));

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new RefundValidationException(
                "CafeRefund.SaleMissing",
                "Source Sale was not found.");
        }

        return new SaleSettlementAuthority(
            (SaleStatus)reader.GetInt32(0),
            reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4));
    }

    private static SaleItemAuthority ReadSaleItemAuthority(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid saleId,
        Guid saleItemId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT i.product_id,i.product_name_snapshot,i.qty_x1000,
                   i.unit_price_cents,i.discount_cents,i.line_total_cents,
                   p.track_stock
            FROM sale_items i
            INNER JOIN products p ON p.id=i.product_id
            WHERE i.id=$item
              AND i.sale_id=$sale
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$item", saleItemId.ToString("D"));
        command.Parameters.AddWithValue("$sale", saleId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new RefundValidationException(
                "CafeRefund.SaleItemMissing",
                "Refund SaleItem is not part of the source Sale.");
        }

        return new SaleItemAuthority(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetInt64(5),
            reader.GetInt32(6) != 0);
    }

    private static (long Quantity, long Amount) ReadPriorCommittedReturn(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid saleItemId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                COALESCE(SUM(l.refund_quantity_x1000),0),
                COALESCE(SUM(l.refund_amount_cents),0)
            FROM cafe_sale_refund_lines l
            INNER JOIN cafe_sale_refunds r
              ON r.refund_id=l.refund_id
            WHERE l.sale_item_id=$item
              AND r.state='Committed';
            """;
        command.Parameters.AddWithValue("$item", saleItemId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        reader.Read();
        return (reader.GetInt64(0), reader.GetInt64(1));
    }

    private static long ReadPriorSettlementTotal(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid saleId,
        string method)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COALESCE(SUM(s.amount_cents),0)
            FROM cafe_sale_refund_settlements s
            INNER JOIN cafe_sale_refunds r
              ON r.refund_id=s.refund_id
            WHERE r.sale_id=$sale
              AND r.state='Committed'
              AND s.method=$method;
            """;
        command.Parameters.AddWithValue("$sale", saleId.ToString("D"));
        command.Parameters.AddWithValue("$method", method);
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L);
    }

    private static long ReadCustomerBalance(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid customerId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT balance_after_cents
            FROM accounts_receivable_entries
            WHERE customer_id=$customer
            ORDER BY created_at DESC,id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$customer", customerId.ToString("D"));
        object? value = command.ExecuteScalar();
        return value is null or DBNull
            ? 0
            : Convert.ToInt64(value);
    }

    private static void ValidateCashierSession(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid cashierSessionId,
        Guid actorUserId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM cashier_sessions
            WHERE id=$session
              AND user_id=$actor
              AND status=$open;
            """;
        command.Parameters.AddWithValue("$session", cashierSessionId.ToString("D"));
        command.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
        command.Parameters.AddWithValue("$open", Convert.ToInt32(CashierSessionStatus.Open));

        if (Convert.ToInt64(command.ExecuteScalar() ?? 0L) != 1)
        {
            throw new RefundValidationException(
                "CafeRefund.OpenCashierSessionRequired",
                "Cash refund requires the actor's exact open cashier session.");
        }
    }

    private static Result ValidateAuthority(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId,
        Guid actorUserId,
        string permission)
    {
        using (SqliteCommand store = connection.CreateCommand())
        {
            store.Transaction = transaction;
            store.CommandText = """
                SELECT COUNT(*)
                FROM deployment_identity
                WHERE store_id=$store;
                """;
            store.Parameters.AddWithValue("$store", storeId.ToString("D"));

            if (Convert.ToInt64(store.ExecuteScalar() ?? 0L) != 1)
            {
                return Result.Fail(Error.Conflict(
                    "CafeRefund.StoreAuthorityMismatch",
                    "Refund StoreId does not match local deployment authority."));
            }
        }

        using SqliteCommand permissionCommand = connection.CreateCommand();
        permissionCommand.Transaction = transaction;
        permissionCommand.CommandText = """
            SELECT COUNT(*)
            FROM users u
            INNER JOIN role_permissions rp
              ON rp.role_id=u.role_id
            WHERE u.id=$user
              AND u.status=1
              AND rp.permission=$permission;
            """;
        permissionCommand.Parameters.AddWithValue("$user", actorUserId.ToString("D"));
        permissionCommand.Parameters.AddWithValue("$permission", permission);

        if (Convert.ToInt64(permissionCommand.ExecuteScalar() ?? 0L) != 1)
        {
            return Result.Fail(Error.Forbidden(
                "CafeRefund.PermissionRequired",
                "Refund operation requires permission: " + permission));
        }

        return Result.Success();
    }

    private static bool RefundExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid refundId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM cafe_sale_refunds
            WHERE refund_id=$refund;
            """;
        command.Parameters.AddWithValue("$refund", refundId.ToString("D"));
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L) != 0;
    }

    private static CafeRefundSummary? ReadRefundSummary(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid refundId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT refund_id,sale_id,store_id,current_version,state,reason,
                   requested_by_user_id,requested_at_utc,resolved_by_user_id,
                   resolved_at_utc,resolution_reason,committed_by_user_id,
                   committed_at_utc,total_refund_cents,request_hash
            FROM cafe_sale_refunds
            WHERE refund_id=$refund
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$refund", refundId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new CafeRefundSummary(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            Guid.Parse(reader.GetString(2)),
            reader.GetInt32(3),
            reader.GetString(4),
            reader.GetString(5),
            Guid.Parse(reader.GetString(6)),
            DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture),
            reader.IsDBNull(8) ? null : Guid.Parse(reader.GetString(8)),
            reader.IsDBNull(9)
                ? null
                : DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : Guid.Parse(reader.GetString(11)),
            reader.IsDBNull(12)
                ? null
                : DateTimeOffset.Parse(reader.GetString(12), CultureInfo.InvariantCulture),
            reader.IsDBNull(13) ? null : reader.GetInt64(13),
            reader.GetString(14));
    }

    private static List<CafeRefundLineSnapshot> ReadRefundLines(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid refundId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT refund_line_id,refund_id,sale_item_id,product_id,
                   product_name_snapshot,original_quantity_x1000,
                   refund_quantity_x1000,frozen_unit_price_cents,
                   frozen_discount_cents,refund_amount_cents,
                   stock_disposition,operator_note
            FROM cafe_sale_refund_lines
            WHERE refund_id=$refund
            ORDER BY refund_line_id;
            """;
        command.Parameters.AddWithValue("$refund", refundId.ToString("D"));

        List<CafeRefundLineSnapshot> result = new();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new CafeRefundLineSnapshot(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                Guid.Parse(reader.GetString(2)),
                Guid.Parse(reader.GetString(3)),
                reader.GetString(4),
                reader.GetInt64(5),
                reader.GetInt64(6),
                reader.GetInt64(7),
                reader.GetInt64(8),
                reader.GetInt64(9),
                reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11)));
        }

        return result;
    }

    private static List<CafeRefundSettlementSnapshot> ReadRefundSettlements(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid refundId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT settlement_part_id,refund_id,method,amount_cents,
                   cash_movement_id,receivable_entry_id,created_at_utc
            FROM cafe_sale_refund_settlements
            WHERE refund_id=$refund
            ORDER BY settlement_part_id;
            """;
        command.Parameters.AddWithValue("$refund", refundId.ToString("D"));

        List<CafeRefundSettlementSnapshot> result = new();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new CafeRefundSettlementSnapshot(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.GetString(2),
                reader.GetInt64(3),
                reader.IsDBNull(4) ? null : Guid.Parse(reader.GetString(4)),
                reader.IsDBNull(5) ? null : Guid.Parse(reader.GetString(5)),
                DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture)));
        }

        return result;
    }

    private void EnqueueRefundSync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        Guid deviceId,
        Guid storeId,
        Guid actorUserId,
        Guid refundId,
        Guid saleId,
        string operationType,
        int expectedVersion,
        int resultingVersion,
        string? fromState,
        string toState,
        string requestReason,
        string? resolutionReason,
        string requestHash,
        Guid? cashierSessionId,
        DateTimeOffset occurredAtUtc,
        long refundTotalCents,
        IReadOnlyList<CafeRefundLineSnapshot> lines,
        IReadOnlyList<CafeRefundSettlementSnapshot> settlements)
    {
        CafeRefundOperationV1 payload = new(
            operationType,
            refundId,
            saleId,
            storeId,
            expectedVersion,
            resultingVersion,
            fromState,
            toState,
            requestReason.Trim(),
            Clean(resolutionReason),
            requestHash,
            actorUserId,
            cashierSessionId,
            occurredAtUtc.ToUniversalTime(),
            refundTotalCents,
            lines.Select(line => new CafeRefundLineV1(
                line.RefundLineId,
                line.SaleItemId,
                line.ProductId,
                line.ProductNameSnapshot,
                line.OriginalQuantityX1000,
                line.RefundQuantityX1000,
                line.FrozenUnitPriceCents,
                line.FrozenDiscountCents,
                line.RefundAmountCents,
                line.StockDisposition,
                Clean(line.OperatorNote))).ToArray(),
            settlements.Select(settlement =>
                new CafeRefundSettlementV1(
                    settlement.SettlementPartId,
                    settlement.Method,
                    settlement.AmountCents,
                    settlement.CashMovementId,
                    settlement.ReceivableEntryId,
                    settlement.CreatedAtUtc.ToUniversalTime()))
                .ToArray());

        CafeRefundValidationResult validation =
            CafeRefundOperationV1Validator.Validate(payload);
        if (!validation.IsValid)
        {
            throw new RefundValidationException(
                validation.ErrorCode ?? "CafeRefund.SyncPayloadInvalid",
                validation.ErrorMessage ?? "Refund sync payload is invalid.");
        }

        string json =
            CafeRefundOperationV1CanonicalJson.Serialize(payload);

        _outbox.Enqueue(
            connection,
            transaction,
            new SyncEnqueueRequest(
                deviceId,
                storeId,
                actorUserId,
                operationType,
                CafeRefundSyncContract.SchemaVersion,
                refundId,
                expectedVersion,
                occurredAtUtc,
                json,
                operationId));
    }

    private static void InsertRefundLine(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeRefundLineSnapshot line)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO cafe_sale_refund_lines(
                refund_line_id,refund_id,sale_item_id,product_id,
                product_name_snapshot,original_quantity_x1000,
                refund_quantity_x1000,frozen_unit_price_cents,
                frozen_discount_cents,refund_amount_cents,
                stock_disposition,operator_note)
            VALUES(
                $line,$refund,$item,$product,$name,$originalQty,$refundQty,
                $unit,$discount,$amount,$disposition,$note);
            """;
        command.Parameters.AddWithValue("$line", line.RefundLineId.ToString("D"));
        command.Parameters.AddWithValue("$refund", line.RefundId.ToString("D"));
        command.Parameters.AddWithValue("$item", line.SaleItemId.ToString("D"));
        command.Parameters.AddWithValue("$product", line.ProductId.ToString("D"));
        command.Parameters.AddWithValue("$name", line.ProductNameSnapshot);
        command.Parameters.AddWithValue("$originalQty", line.OriginalQuantityX1000);
        command.Parameters.AddWithValue("$refundQty", line.RefundQuantityX1000);
        command.Parameters.AddWithValue("$unit", line.FrozenUnitPriceCents);
        command.Parameters.AddWithValue("$discount", line.FrozenDiscountCents);
        command.Parameters.AddWithValue("$amount", line.RefundAmountCents);
        command.Parameters.AddWithValue("$disposition", line.StockDisposition);
        command.Parameters.AddWithValue(
            "$note",
            string.IsNullOrWhiteSpace(line.OperatorNote)
                ? DBNull.Value
                : line.OperatorNote.Trim());
        command.ExecuteNonQuery();
    }

    private static void InsertSettlement(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid refundId,
        CafeRefundSettlementMutation settlement,
        Guid? cashMovementId,
        Guid? receivableEntryId,
        DateTimeOffset occurredAtUtc)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO cafe_sale_refund_settlements(
                settlement_part_id,refund_id,method,amount_cents,
                cash_movement_id,receivable_entry_id,created_at_utc)
            VALUES(
                $id,$refund,$method,$amount,$cash,$ar,$at);
            """;
        command.Parameters.AddWithValue("$id", settlement.SettlementPartId.ToString("D"));
        command.Parameters.AddWithValue("$refund", refundId.ToString("D"));
        command.Parameters.AddWithValue("$method", settlement.Method);
        command.Parameters.AddWithValue("$amount", settlement.AmountCents);
        command.Parameters.AddWithValue(
            "$cash",
            cashMovementId?.ToString("D") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue(
            "$ar",
            receivableEntryId?.ToString("D") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue(
            "$at",
            occurredAtUtc.ToUniversalTime().ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void InsertOperationAndEvent(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        Guid deviceId,
        Guid storeId,
        Guid refundId,
        Guid saleId,
        string operationType,
        string payloadHash,
        int expectedVersion,
        int resultingVersion,
        string resultState,
        Guid actorUserId,
        DateTimeOffset occurredAtUtc,
        string action,
        string? fromState,
        string toState,
        string? reason)
    {
        using (SqliteCommand operation = connection.CreateCommand())
        {
            operation.Transaction = transaction;
            operation.CommandText = """
                INSERT INTO cafe_sale_refund_operations(
                    operation_id,device_id,store_id,refund_id,sale_id,
                    operation_type,payload_hash,expected_version,
                    resulting_version,result_state,actor_user_id,occurred_at_utc)
                VALUES(
                    $operation,$device,$store,$refund,$sale,$type,$hash,
                    $expected,$resulting,$state,$actor,$at);
                """;
            operation.Parameters.AddWithValue("$operation", operationId.ToString("D"));
            operation.Parameters.AddWithValue("$device", deviceId.ToString("D"));
            operation.Parameters.AddWithValue("$store", storeId.ToString("D"));
            operation.Parameters.AddWithValue("$refund", refundId.ToString("D"));
            operation.Parameters.AddWithValue("$sale", saleId.ToString("D"));
            operation.Parameters.AddWithValue("$type", operationType);
            operation.Parameters.AddWithValue("$hash", payloadHash);
            operation.Parameters.AddWithValue("$expected", expectedVersion);
            operation.Parameters.AddWithValue("$resulting", resultingVersion);
            operation.Parameters.AddWithValue("$state", resultState);
            operation.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
            operation.Parameters.AddWithValue("$at", occurredAtUtc.ToUniversalTime().ToString("O"));
            operation.ExecuteNonQuery();
        }

        using SqliteCommand evt = connection.CreateCommand();
        evt.Transaction = transaction;
        evt.CommandText = """
            INSERT INTO cafe_sale_refund_events(
                event_id,operation_id,refund_id,sale_id,
                expected_version,resulting_version,action,from_state,to_state,
                actor_user_id,occurred_at_utc,reason,content_hash)
            VALUES(
                $event,$operation,$refund,$sale,$expected,$resulting,$action,
                $from,$to,$actor,$at,$reason,$hash);
            """;
        evt.Parameters.AddWithValue(
            "$event",
            DeterministicGuid(
                "salryn-c5-refund-event-v1",
                operationId.ToString("D")).ToString("D"));
        evt.Parameters.AddWithValue("$operation", operationId.ToString("D"));
        evt.Parameters.AddWithValue("$refund", refundId.ToString("D"));
        evt.Parameters.AddWithValue("$sale", saleId.ToString("D"));
        evt.Parameters.AddWithValue("$expected", expectedVersion);
        evt.Parameters.AddWithValue("$resulting", resultingVersion);
        evt.Parameters.AddWithValue("$action", action);
        evt.Parameters.AddWithValue(
            "$from",
            fromState ?? (object)DBNull.Value);
        evt.Parameters.AddWithValue("$to", toState);
        evt.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
        evt.Parameters.AddWithValue("$at", occurredAtUtc.ToUniversalTime().ToString("O"));
        evt.Parameters.AddWithValue(
            "$reason",
            string.IsNullOrWhiteSpace(reason)
                ? DBNull.Value
                : reason.Trim());
        evt.Parameters.AddWithValue("$hash", payloadHash);
        evt.ExecuteNonQuery();
    }

    private static Result<CafeRefundMutationResult>? TryReplayMutation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        Guid deviceId,
        Guid storeId,
        Guid refundId,
        Guid saleId,
        string operationType,
        string payloadHash,
        int expectedVersion,
        Guid actorUserId)
    {
        ExistingOperation? existing =
            ReadOperation(connection, transaction, operationId);
        if (existing is null)
        {
            return null;
        }

        bool exact =
            existing.DeviceId == deviceId &&
            existing.StoreId == storeId &&
            existing.RefundId == refundId &&
            existing.SaleId == saleId &&
            existing.ExpectedVersion == expectedVersion &&
            existing.ActorUserId == actorUserId &&
            string.Equals(existing.OperationType, operationType, StringComparison.Ordinal) &&
            string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal);

        if (!exact)
        {
            return Result<CafeRefundMutationResult>.Fail(Error.Conflict(
                "CafeRefund.OperationIdPayloadConflict",
                "Refund operation ID already exists with different immutable content."));
        }

        return Result<CafeRefundMutationResult>.Success(
            new CafeRefundMutationResult(
                refundId,
                existing.ResultingVersion,
                existing.ResultState,
                true));
    }

    private static Result<CafeRefundCommitResult>? TryReplayCommit(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeRefundCommitMutation mutation,
        string payloadHash)
    {
        ExistingOperation? existing =
            ReadOperation(
                connection,
                transaction,
                mutation.OperationId);
        if (existing is null)
        {
            return null;
        }

        bool exact =
            existing.DeviceId == mutation.DeviceId &&
            existing.StoreId == mutation.StoreId &&
            existing.RefundId == mutation.RefundId &&
            existing.SaleId == mutation.SaleId &&
            existing.ExpectedVersion == mutation.ExpectedVersion &&
            existing.ActorUserId == mutation.ActorUserId &&
            string.Equals(
                existing.OperationType,
                CafeRefundOperationTypes.Committed,
                StringComparison.Ordinal) &&
            string.Equals(
                existing.PayloadHash,
                payloadHash,
                StringComparison.Ordinal);

        if (!exact)
        {
            return Result<CafeRefundCommitResult>.Fail(Error.Conflict(
                "CafeRefund.OperationIdPayloadConflict",
                "Refund commit OperationId already exists with different immutable content."));
        }

        CafeRefundSummary? refund =
            ReadRefundSummary(
                connection,
                transaction,
                mutation.RefundId);

        if (refund is null ||
            !refund.TotalRefundCents.HasValue)
        {
            return Result<CafeRefundCommitResult>.Fail(Error.Conflict(
                "CafeRefund.ReplayEvidenceMissing",
                "Refund operation exists without retained committed refund evidence."));
        }

        return Result<CafeRefundCommitResult>.Success(
            new CafeRefundCommitResult(
                mutation.RefundId,
                existing.ResultingVersion,
                existing.ResultState,
                refund.TotalRefundCents.Value,
                true));
    }

    private static ExistingOperation? ReadOperation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT device_id,store_id,refund_id,sale_id,operation_type,
                   payload_hash,expected_version,resulting_version,
                   result_state,actor_user_id
            FROM cafe_sale_refund_operations
            WHERE operation_id=$operation
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$operation", operationId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new ExistingOperation(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            Guid.Parse(reader.GetString(2)),
            Guid.Parse(reader.GetString(3)),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetInt32(6),
            reader.GetInt32(7),
            reader.GetString(8),
            Guid.Parse(reader.GetString(9)));
    }

    private static string HashRequest(
        CafeRefundRequestMutation mutation)
    {
        StringBuilder builder = NewHashBuilder(
            mutation.OperationId,
            mutation.DeviceId,
            mutation.StoreId,
            mutation.RefundId,
            mutation.SaleId,
            mutation.ExpectedVersion,
            mutation.ActorUserId,
            mutation.OccurredAtUtc);

        Append(builder, mutation.Reason.Trim());

        foreach (CafeRefundLineMutation line in mutation.Lines
                     .OrderBy(x => x.SaleItemId)
                     .ThenBy(x => x.RefundLineId))
        {
            Append(builder, line.RefundLineId);
            Append(builder, line.SaleItemId);
            Append(builder, line.QuantityX1000);
            Append(builder, line.StockDisposition);
            Append(builder, Clean(line.OperatorNote));
        }

        return Hash(builder);
    }

    private static string HashResolution(
        CafeRefundResolutionMutation mutation)
    {
        StringBuilder builder = NewHashBuilder(
            mutation.OperationId,
            mutation.DeviceId,
            mutation.StoreId,
            mutation.RefundId,
            mutation.SaleId,
            mutation.ExpectedVersion,
            mutation.ActorUserId,
            mutation.OccurredAtUtc);

        Append(builder, mutation.Approve ? "Approved" : "Rejected");
        Append(builder, Clean(mutation.Reason));
        return Hash(builder);
    }

    private static string HashCommit(
        CafeRefundCommitMutation mutation)
    {
        StringBuilder builder = NewHashBuilder(
            mutation.OperationId,
            mutation.DeviceId,
            mutation.StoreId,
            mutation.RefundId,
            mutation.SaleId,
            mutation.ExpectedVersion,
            mutation.ActorUserId,
            mutation.OccurredAtUtc);

        Append(builder, mutation.CashierSessionId);

        foreach (CafeRefundSettlementMutation settlement in mutation.Settlements
                     .OrderBy(x => x.Method, StringComparer.Ordinal)
                     .ThenBy(x => x.SettlementPartId))
        {
            Append(builder, settlement.SettlementPartId);
            Append(builder, settlement.Method);
            Append(builder, settlement.AmountCents);
        }

        return Hash(builder);
    }

    private static StringBuilder NewHashBuilder(
        Guid operationId,
        Guid deviceId,
        Guid storeId,
        Guid refundId,
        Guid saleId,
        int expectedVersion,
        Guid actorUserId,
        DateTimeOffset occurredAtUtc)
    {
        StringBuilder builder = new();
        Append(builder, operationId);
        Append(builder, deviceId);
        Append(builder, storeId);
        Append(builder, refundId);
        Append(builder, saleId);
        Append(builder, expectedVersion);
        Append(builder, actorUserId);
        Append(builder, occurredAtUtc.ToUniversalTime());
        return builder;
    }

    private static void Append(StringBuilder builder, object? value)
    {
        string text = value switch
        {
            null => string.Empty,
            Guid guid => guid.ToString("D"),
            DateTimeOffset at => at.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };

        builder.Append(text.Length).Append(':').Append(text).Append('|');
    }

    private static string Hash(StringBuilder builder) =>
        Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(builder.ToString())))
            .ToLowerInvariant();

    private static Guid DeterministicGuid(
        string scope,
        string seed)
    {
        byte[] hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(scope + "|" + seed));
        byte[] bytes = hash[..16].ToArray();
        return new Guid(bytes);
    }

    private static string? ValidateRequestMutation(
        CafeRefundRequestMutation mutation)
    {
        if (mutation is null)
        {
            return "Refund request is required.";
        }

        if (mutation.OperationId == Guid.Empty ||
            mutation.DeviceId == Guid.Empty ||
            mutation.RefundId == Guid.Empty ||
            mutation.SaleId == Guid.Empty ||
            mutation.StoreId == Guid.Empty ||
            mutation.ActorUserId == Guid.Empty)
        {
            return "Refund request requires stable operation/device/refund/sale/store/actor identities.";
        }

        if (mutation.ExpectedVersion < 0)
        {
            return "Refund expected version cannot be negative.";
        }

        if (string.IsNullOrWhiteSpace(mutation.Reason))
        {
            return "Refund request reason is required.";
        }

        if (mutation.Lines is null ||
            mutation.Lines.Count == 0)
        {
            return "At least one refund line is required.";
        }

        return null;
    }

    private static string? ValidateResolutionMutation(
        CafeRefundResolutionMutation mutation)
    {
        if (mutation is null)
        {
            return "Refund resolution is required.";
        }

        if (mutation.OperationId == Guid.Empty ||
            mutation.DeviceId == Guid.Empty ||
            mutation.RefundId == Guid.Empty ||
            mutation.SaleId == Guid.Empty ||
            mutation.StoreId == Guid.Empty ||
            mutation.ActorUserId == Guid.Empty ||
            mutation.ExpectedVersion <= 0)
        {
            return "Refund resolution requires stable identities and a positive expected version.";
        }

        if (!mutation.Approve &&
            string.IsNullOrWhiteSpace(mutation.Reason))
        {
            return "Rejected refund requires a reason.";
        }

        return null;
    }

    private static string? ValidateCommitMutation(
        CafeRefundCommitMutation mutation)
    {
        if (mutation is null)
        {
            return "Refund commit is required.";
        }

        if (mutation.OperationId == Guid.Empty ||
            mutation.DeviceId == Guid.Empty ||
            mutation.RefundId == Guid.Empty ||
            mutation.SaleId == Guid.Empty ||
            mutation.StoreId == Guid.Empty ||
            mutation.ActorUserId == Guid.Empty ||
            mutation.CashierSessionId == Guid.Empty ||
            mutation.ExpectedVersion <= 0)
        {
            return "Refund commit requires stable identities, cashier session and positive expected version.";
        }

        if (mutation.Settlements is null ||
            mutation.Settlements.Count == 0)
        {
            return "Refund commit requires at least one settlement part.";
        }

        return null;
    }

    private static string? Clean(string? value)
    {
        string normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private sealed record SaleItemAuthority(
        Guid ProductId,
        string ProductNameSnapshot,
        long OriginalQuantityX1000,
        long UnitPriceCents,
        long DiscountCents,
        long LineTotalCents,
        bool TrackStock);

    private sealed record SaleSettlementAuthority(
        SaleStatus Status,
        Guid? CustomerId,
        long ChangeCents,
        long CashTenderCents,
        long AccountsReceivableTenderCents);

    private sealed record SettlementPlan(
        CafeRefundSettlementMutation? CashPart,
        CafeRefundSettlementMutation? ArPart);

    private sealed record ExistingOperation(
        Guid DeviceId,
        Guid StoreId,
        Guid RefundId,
        Guid SaleId,
        string OperationType,
        string PayloadHash,
        int ExpectedVersion,
        int ResultingVersion,
        string ResultState,
        Guid ActorUserId);

    private sealed class RefundValidationException : Exception
    {
        public RefundValidationException(
            string code,
            string message)
            : base(message)
        {
            Code = code;
        }

        public string Code { get; }
    }
}
