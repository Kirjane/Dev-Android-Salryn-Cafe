using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Infrastructure.Data.Sync;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Repositories.Orders;

public sealed class SqliteCafeOrderStore : SqliteRepositoryBase
{
    private readonly SqliteSyncOutboxStore _outbox;

    public SqliteCafeOrderStore(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
        _outbox = new SqliteSyncOutboxStore(connectionFactory);
    }

    public Result<CafeOrderMutationResult> SaveRevision(CafeOrderRevisionMutation mutation)
    {
        string? validation = ValidateRevisionMutation(mutation);
        if (validation is not null)
        {
            return Result<CafeOrderMutationResult>.Fail(
                Error.Validation("CafeOrder.RevisionInvalid", validation));
        }

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Result authority = ValidateEnvelopeAuthority(
                connection,
                transaction,
                mutation.DeviceId,
                mutation.StoreId,
                mutation.ActorUserId);
            if (authority.IsFailure)
            {
                transaction.Rollback();
                return Result<CafeOrderMutationResult>.Fail(authority.ErrorDetail);
            }

            Result<CafeOrderMutationResult>? replay =
                TryReplayRevision(connection, transaction, mutation);
            if (replay is not null)
            {
                transaction.Commit();
                return replay;
            }

            OrderRow? existing = ReadOrder(connection, transaction, mutation.OrderId);
            bool creating = mutation.ExpectedVersion == 0;

            if (creating)
            {
                if (existing is not null)
                {
                    transaction.Rollback();
                    return Result<CafeOrderMutationResult>.Fail(
                        Error.Conflict(
                            "CafeOrder.OrderAlreadyExists",
                            "A new order requires an unused OrderId."));
                }
            }
            else
            {
                if (existing is null)
                {
                    transaction.Rollback();
                    return Result<CafeOrderMutationResult>.Fail(
                        Error.NotFound("CafeOrder.OrderMissing", "Order was not found."));
                }

                if (existing.StoreId != mutation.StoreId ||
                    existing.CurrentVersion != mutation.ExpectedVersion)
                {
                    transaction.Rollback();
                    return Result<CafeOrderMutationResult>.Fail(
                        Error.Conflict(
                            "CafeOrder.ExpectedVersionConflict",
                            "Order version/store authority changed; last-write-wins is not allowed."));
                }

                if (existing.State is not CafeOrderStates.Open and not CafeOrderStates.Parked)
                {
                    transaction.Rollback();
                    return Result<CafeOrderMutationResult>.Fail(
                        Error.Conflict(
                            "CafeOrder.RevisionFrozen",
                            "Only Open or Parked orders may create a new immutable revision."));
                }
            }

            string boundMenuRevisionId = creating
                ? ReadCurrentMenuRevisionId(connection, transaction, mutation.StoreId)
                    ?? throw new CafeOrderValidationException(
                        "CafeOrder.MenuPublicationRequired",
                        "Creating an order requires a current MenuPublication.")
                : existing!.MenuPublicationRevisionId;

            MenuAuthority menu = ReadMenuAuthority(
                connection,
                transaction,
                boundMenuRevisionId,
                mutation.StoreId);

            int resultingVersion = checked(mutation.ExpectedVersion + 1);
            Guid revisionId = DeterministicGuid(
                "salryn-c4-order-revision-v1",
                mutation.OperationId.ToString("D"));
            Guid? previousRevisionId = creating ? null : existing!.CurrentRevisionId;
            int revisionNumber = creating
                ? 1
                : ReadNextRevisionNumber(connection, transaction, mutation.OrderId);

            IReadOnlyList<CafeOrderLineSnapshot> lines =
                ResolveRevisionLines(menu, mutation.Lines);

            string revisionContentHash = ComputeRevisionContentHash(
                mutation.OrderId,
                revisionId,
                revisionNumber,
                resultingVersion,
                previousRevisionId,
                boundMenuRevisionId,
                mutation.ServiceMode,
                Clean(mutation.ServiceReference),
                lines);

            CafeOrderRevisionSnapshot revision = new(
                revisionId,
                mutation.OrderId,
                revisionNumber,
                resultingVersion,
                previousRevisionId,
                boundMenuRevisionId,
                mutation.ServiceMode,
                Clean(mutation.ServiceReference),
                revisionContentHash,
                mutation.ActorUserId,
                mutation.OccurredAtUtc.ToUniversalTime(),
                lines);

            string state = creating ? CafeOrderStates.Open : existing!.State;
            string action = creating
                ? CafeOrderLifecycleActions.Created
                : CafeOrderLifecycleActions.Revised;

            CafeOrderOperationV1 payload = ToRevisionOperationPayload(
                mutation,
                revision,
                action,
                creating ? null : state,
                state);

            string payloadJson = CafeOrderOperationCanonicalJson.Serialize(payload);
            string payloadHash = CafeOrderOperationCanonicalJson.ComputeHash(payloadJson);

            if (creating)
            {
                InsertOrder(
                    connection,
                    transaction,
                    mutation,
                    revisionId,
                    boundMenuRevisionId,
                    state);
            }

            InsertRevision(connection, transaction, revision);

            if (!creating)
            {
                using SqliteCommand update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE cafe_orders
                    SET current_version=$version,
                        current_revision_id=$revision,
                        service_mode=$mode,
                        service_reference=$reference,
                        updated_at_utc=$at
                    WHERE order_id=$order
                      AND current_version=$expected
                      AND state IN ('Open','Parked');
                    """;
                update.Parameters.AddWithValue("$version", resultingVersion);
                update.Parameters.AddWithValue("$revision", revisionId.ToString("D"));
                update.Parameters.AddWithValue("$mode", mutation.ServiceMode);
                update.Parameters.AddWithValue("$reference", DbText(mutation.ServiceReference));
                update.Parameters.AddWithValue("$at", mutation.OccurredAtUtc.ToUniversalTime().ToString("O"));
                update.Parameters.AddWithValue("$order", mutation.OrderId.ToString("D"));
                update.Parameters.AddWithValue("$expected", mutation.ExpectedVersion);
                if (update.ExecuteNonQuery() != 1)
                {
                    throw new CafeOrderValidationException(
                        "CafeOrder.ConcurrentRevisionConflict",
                        "Order lost revision update ownership.");
                }
            }

            InsertOperationAndLifecycle(
                connection,
                transaction,
                mutation.OperationId,
                mutation.OrderId,
                CafeOrderOperationContract.OrderRevisionCommittedV1,
                payloadHash,
                mutation.ExpectedVersion,
                resultingVersion,
                revisionId,
                state,
                null,
                mutation.ActorUserId,
                mutation.OccurredAtUtc,
                action,
                creating ? null : state,
                state,
                null,
                revisionId,
                null,
                null);

            Enqueue(
                connection,
                transaction,
                mutation.DeviceId,
                mutation.StoreId,
                mutation.ActorUserId,
                mutation.OperationId,
                mutation.OrderId,
                mutation.ExpectedVersion,
                CafeOrderOperationContract.OrderRevisionCommittedV1,
                mutation.OccurredAtUtc,
                payloadJson);

            transaction.Commit();
            return Result<CafeOrderMutationResult>.Success(
                new CafeOrderMutationResult(
                    mutation.OrderId,
                    resultingVersion,
                    state,
                    revisionId,
                    false));
        }
        catch (CafeOrderValidationException ex)
        {
            return Result<CafeOrderMutationResult>.Fail(
                Error.Conflict(ex.Code, ex.Message));
        }
        catch (Exception ex) when (
            ex is SqliteException or InvalidOperationException or FormatException or OverflowException)
        {
            return Result<CafeOrderMutationResult>.Fail(
                Error.Unexpected("CafeOrder.SaveRevisionFailed", ex.Message));
        }
    }

    public Result<CafeOrderMutationResult> Park(CafeOrderLifecycleMutation mutation) =>
        Transition(
            mutation.OperationId,
            mutation.DeviceId,
            mutation.OrderId,
            mutation.StoreId,
            mutation.ExpectedVersion,
            mutation.ActorUserId,
            mutation.OccurredAtUtc,
            CafeOrderOperationContract.OrderParkedV1,
            CafeOrderLifecycleActions.Parked,
            CafeOrderStates.Open,
            CafeOrderStates.Parked,
            null);

    public Result<CafeOrderMutationResult> Resume(CafeOrderLifecycleMutation mutation) =>
        Transition(
            mutation.OperationId,
            mutation.DeviceId,
            mutation.OrderId,
            mutation.StoreId,
            mutation.ExpectedVersion,
            mutation.ActorUserId,
            mutation.OccurredAtUtc,
            CafeOrderOperationContract.OrderResumedV1,
            CafeOrderLifecycleActions.Resumed,
            CafeOrderStates.Parked,
            CafeOrderStates.Open,
            null);

    public Result<CafeOrderPreparationSubmissionResult> SubmitPreparation(
        CafeOrderPreparationSubmitMutation mutation)
    {
        if (!ValidIdentity(
                mutation.OperationId,
                mutation.DeviceId,
                mutation.OrderId,
                mutation.StoreId,
                mutation.ActorUserId) ||
            mutation.ExpectedVersion <= 0)
        {
            return Result<CafeOrderPreparationSubmissionResult>.Fail(
                Error.Validation(
                    "CafeOrder.SubmitInvalid",
                    "Preparation submission requires stable identities and a positive expected version."));
        }

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Result authority = ValidateEnvelopeAuthority(
                connection,
                transaction,
                mutation.DeviceId,
                mutation.StoreId,
                mutation.ActorUserId);
            if (authority.IsFailure)
            {
                transaction.Rollback();
                return Result<CafeOrderPreparationSubmissionResult>.Fail(authority.ErrorDetail);
            }

            Result<CafeOrderPreparationSubmissionResult>? replay =
                TryReplaySubmission(connection, transaction, mutation);
            if (replay is not null)
            {
                transaction.Commit();
                return replay;
            }

            OrderRow order = RequireOrderVersion(
                connection,
                transaction,
                mutation.OrderId,
                mutation.StoreId,
                mutation.ExpectedVersion,
                CafeOrderStates.Open);

            string? recipeBookRevisionId =
                ReadCurrentRecipeBookRevisionId(connection, transaction, mutation.StoreId);
            int resultingVersion = checked(mutation.ExpectedVersion + 1);

            CafeOrderOperationV1 payload = BuildLifecyclePayload(
                CafeOrderOperationContract.OrderPreparationSubmittedV1,
                order,
                mutation.ExpectedVersion,
                resultingVersion,
                CafeOrderLifecycleActions.PreparationSubmitted,
                CafeOrderStates.Open,
                CafeOrderStates.Submitted,
                mutation.ActorUserId,
                mutation.OccurredAtUtc,
                null,
                order.CurrentRevisionId,
                recipeBookRevisionId,
                null,
                null,
                null,
                Array.Empty<CafeOrderSaleItemLinkV1>());

            string json = CafeOrderOperationCanonicalJson.Serialize(payload);
            string hash = CafeOrderOperationCanonicalJson.ComputeHash(json);

            using (SqliteCommand update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE cafe_orders
                    SET current_version=$version,
                        state='Submitted',
                        submitted_revision_id=current_revision_id,
                        submitted_recipe_book_revision_id=$recipe,
                        updated_at_utc=$at
                    WHERE order_id=$order
                      AND current_version=$expected
                      AND state='Open';
                    """;
                update.Parameters.AddWithValue("$version", resultingVersion);
                update.Parameters.AddWithValue("$recipe", DbText(recipeBookRevisionId));
                update.Parameters.AddWithValue("$at", mutation.OccurredAtUtc.ToUniversalTime().ToString("O"));
                update.Parameters.AddWithValue("$order", mutation.OrderId.ToString("D"));
                update.Parameters.AddWithValue("$expected", mutation.ExpectedVersion);
                if (update.ExecuteNonQuery() != 1)
                {
                    throw new CafeOrderValidationException(
                        "CafeOrder.SubmitConcurrentConflict",
                        "Order lost preparation submission ownership.");
                }
            }

            InsertOperationAndLifecycle(
                connection,
                transaction,
                mutation.OperationId,
                mutation.OrderId,
                CafeOrderOperationContract.OrderPreparationSubmittedV1,
                hash,
                mutation.ExpectedVersion,
                resultingVersion,
                order.CurrentRevisionId,
                CafeOrderStates.Submitted,
                null,
                mutation.ActorUserId,
                mutation.OccurredAtUtc,
                CafeOrderLifecycleActions.PreparationSubmitted,
                CafeOrderStates.Open,
                CafeOrderStates.Submitted,
                null,
                order.CurrentRevisionId,
                recipeBookRevisionId,
                null);

            Enqueue(
                connection,
                transaction,
                mutation.DeviceId,
                mutation.StoreId,
                mutation.ActorUserId,
                mutation.OperationId,
                mutation.OrderId,
                mutation.ExpectedVersion,
                CafeOrderOperationContract.OrderPreparationSubmittedV1,
                mutation.OccurredAtUtc,
                json);

            transaction.Commit();
            return Result<CafeOrderPreparationSubmissionResult>.Success(
                new CafeOrderPreparationSubmissionResult(
                    mutation.OrderId,
                    resultingVersion,
                    order.CurrentRevisionId,
                    recipeBookRevisionId,
                    false));
        }
        catch (CafeOrderValidationException ex)
        {
            return Result<CafeOrderPreparationSubmissionResult>.Fail(
                Error.Conflict(ex.Code, ex.Message));
        }
        catch (Exception ex) when (
            ex is SqliteException or InvalidOperationException or FormatException or OverflowException)
        {
            return Result<CafeOrderPreparationSubmissionResult>.Fail(
                Error.Unexpected("CafeOrder.SubmitFailed", ex.Message));
        }
    }

    public Result<CafeOrderMutationResult> SetPreparationStatus(
        CafeOrderPreparationStatusMutation mutation)
    {
        if (mutation.TargetState == CafeOrderStates.Preparing)
        {
            return Transition(
                mutation.OperationId,
                mutation.DeviceId,
                mutation.OrderId,
                mutation.StoreId,
                mutation.ExpectedVersion,
                mutation.ActorUserId,
                mutation.OccurredAtUtc,
                CafeOrderOperationContract.OrderPreparationStatusChangedV1,
                CafeOrderLifecycleActions.PreparationPreparing,
                CafeOrderStates.Submitted,
                CafeOrderStates.Preparing,
                null);
        }

        if (mutation.TargetState == CafeOrderStates.Ready)
        {
            Result<CafeOrderMutationResult> fromSubmitted = Transition(
                mutation.OperationId,
                mutation.DeviceId,
                mutation.OrderId,
                mutation.StoreId,
                mutation.ExpectedVersion,
                mutation.ActorUserId,
                mutation.OccurredAtUtc,
                CafeOrderOperationContract.OrderPreparationStatusChangedV1,
                CafeOrderLifecycleActions.PreparationReady,
                CafeOrderStates.Submitted,
                CafeOrderStates.Ready,
                null,
                allowStateMismatch: true);

            if (fromSubmitted.IsSuccess)
            {
                return fromSubmitted;
            }

            return Transition(
                mutation.OperationId,
                mutation.DeviceId,
                mutation.OrderId,
                mutation.StoreId,
                mutation.ExpectedVersion,
                mutation.ActorUserId,
                mutation.OccurredAtUtc,
                CafeOrderOperationContract.OrderPreparationStatusChangedV1,
                CafeOrderLifecycleActions.PreparationReady,
                CafeOrderStates.Preparing,
                CafeOrderStates.Ready,
                null);
        }

        return Result<CafeOrderMutationResult>.Fail(
            Error.Validation(
                "CafeOrder.PreparationTargetInvalid",
                "Preparation target must be Preparing or Ready."));
    }

    public Result<CafeOrderMutationResult> Cancel(CafeOrderCancellationMutation mutation)
    {
        if (string.IsNullOrWhiteSpace(mutation.Reason))
        {
            return Result<CafeOrderMutationResult>.Fail(
                Error.Validation(
                    "CafeOrder.CancellationReasonRequired",
                    "Cancellation reason is required."));
        }

        if (!ValidIdentity(
                mutation.OperationId,
                mutation.DeviceId,
                mutation.OrderId,
                mutation.StoreId,
                mutation.ActorUserId) ||
            mutation.ExpectedVersion <= 0)
        {
            return Result<CafeOrderMutationResult>.Fail(
                Error.Validation(
                    "CafeOrder.CancelInvalid",
                    "Cancellation requires stable identities and a positive expected version."));
        }

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Result authority = ValidateEnvelopeAuthority(
                connection,
                transaction,
                mutation.DeviceId,
                mutation.StoreId,
                mutation.ActorUserId);
            if (authority.IsFailure)
            {
                transaction.Rollback();
                return Result<CafeOrderMutationResult>.Fail(authority.ErrorDetail);
            }

            Result<CafeOrderMutationResult>? replay =
                TryReplayLifecycle(
                    connection,
                    transaction,
                    mutation.OperationId,
                    mutation.DeviceId,
                    mutation.OrderId,
                    mutation.StoreId,
                    mutation.ExpectedVersion,
                    mutation.ActorUserId,
                    CafeOrderOperationContract.OrderCancelledV1,
                    mutation.Reason);
            if (replay is not null)
            {
                transaction.Commit();
                return replay;
            }

            OrderRow order = ReadOrder(connection, transaction, mutation.OrderId)
                ?? throw new CafeOrderValidationException(
                    "CafeOrder.OrderMissing",
                    "Order was not found.");

            if (order.StoreId != mutation.StoreId ||
                order.CurrentVersion != mutation.ExpectedVersion)
            {
                throw new CafeOrderValidationException(
                    "CafeOrder.ExpectedVersionConflict",
                    "Order version/store authority changed; last-write-wins is not allowed.");
            }

            if (CafeOrderStates.IsTerminal(order.State))
            {
                throw new CafeOrderValidationException(
                    "CafeOrder.TerminalOrder",
                    "Completed or cancelled orders are immutable.");
            }

            int resultingVersion = checked(mutation.ExpectedVersion + 1);
            string reason = mutation.Reason.Trim();

            CafeOrderOperationV1 payload = BuildLifecyclePayload(
                CafeOrderOperationContract.OrderCancelledV1,
                order,
                mutation.ExpectedVersion,
                resultingVersion,
                CafeOrderLifecycleActions.Cancelled,
                order.State,
                CafeOrderStates.Cancelled,
                mutation.ActorUserId,
                mutation.OccurredAtUtc,
                reason,
                order.SubmittedRevisionId,
                order.SubmittedRecipeBookRevisionId,
                null,
                null,
                null,
                Array.Empty<CafeOrderSaleItemLinkV1>());

            string json = CafeOrderOperationCanonicalJson.Serialize(payload);
            string hash = CafeOrderOperationCanonicalJson.ComputeHash(json);

            using (SqliteCommand update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE cafe_orders
                    SET current_version=$version,
                        state='Cancelled',
                        cancellation_reason=$reason,
                        updated_at_utc=$at
                    WHERE order_id=$order
                      AND current_version=$expected
                      AND state NOT IN ('Completed','Cancelled');
                    """;
                update.Parameters.AddWithValue("$version", resultingVersion);
                update.Parameters.AddWithValue("$reason", reason);
                update.Parameters.AddWithValue("$at", mutation.OccurredAtUtc.ToUniversalTime().ToString("O"));
                update.Parameters.AddWithValue("$order", mutation.OrderId.ToString("D"));
                update.Parameters.AddWithValue("$expected", mutation.ExpectedVersion);
                if (update.ExecuteNonQuery() != 1)
                {
                    throw new CafeOrderValidationException(
                        "CafeOrder.CancelConcurrentConflict",
                        "Order lost cancellation ownership.");
                }
            }

            InsertOperationAndLifecycle(
                connection,
                transaction,
                mutation.OperationId,
                mutation.OrderId,
                CafeOrderOperationContract.OrderCancelledV1,
                hash,
                mutation.ExpectedVersion,
                resultingVersion,
                order.CurrentRevisionId,
                CafeOrderStates.Cancelled,
                null,
                mutation.ActorUserId,
                mutation.OccurredAtUtc,
                CafeOrderLifecycleActions.Cancelled,
                order.State,
                CafeOrderStates.Cancelled,
                reason,
                order.SubmittedRevisionId,
                order.SubmittedRecipeBookRevisionId,
                null);

            Enqueue(
                connection,
                transaction,
                mutation.DeviceId,
                mutation.StoreId,
                mutation.ActorUserId,
                mutation.OperationId,
                mutation.OrderId,
                mutation.ExpectedVersion,
                CafeOrderOperationContract.OrderCancelledV1,
                mutation.OccurredAtUtc,
                json);

            transaction.Commit();
            return Result<CafeOrderMutationResult>.Success(
                new CafeOrderMutationResult(
                    mutation.OrderId,
                    resultingVersion,
                    CafeOrderStates.Cancelled,
                    order.CurrentRevisionId,
                    false));
        }
        catch (CafeOrderValidationException ex)
        {
            return Result<CafeOrderMutationResult>.Fail(
                Error.Conflict(ex.Code, ex.Message));
        }
        catch (Exception ex) when (
            ex is SqliteException or InvalidOperationException or FormatException or OverflowException)
        {
            return Result<CafeOrderMutationResult>.Fail(
                Error.Unexpected("CafeOrder.CancelFailed", ex.Message));
        }
    }

    public Result<CafeOrderDetail?> Find(Guid orderId)
    {
        if (orderId == Guid.Empty)
        {
            return Result<CafeOrderDetail?>.Fail(
                Error.Validation("CafeOrder.OrderIdRequired", "OrderId is required."));
        }

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();
            CafeOrderDetail? detail = ReadDetail(connection, transaction, orderId);
            transaction.Commit();
            return Result<CafeOrderDetail?>.Success(detail);
        }
        catch (Exception ex) when (ex is SqliteException or FormatException or InvalidOperationException)
        {
            return Result<CafeOrderDetail?>.Fail(
                Error.Unexpected("CafeOrder.ReadFailed", ex.Message));
        }
    }

    public Result<IReadOnlyList<CafeOrderSummary>> List(string? state = null)
    {
        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT order_id,store_id,current_version,current_revision_id,
                       menu_publication_revision_id,state,service_mode,service_reference,
                       created_by_user_id,created_at_utc,updated_at_utc,
                       submitted_revision_id,submitted_recipe_book_revision_id,
                       completed_sale_id,cancellation_reason
                FROM cafe_orders
                WHERE $state IS NULL OR state=$state
                ORDER BY updated_at_utc DESC, order_id;
                """;
            command.Parameters.AddWithValue("$state", DbText(state));
            using SqliteDataReader reader = command.ExecuteReader();
            List<CafeOrderSummary> result = new();
            while (reader.Read())
            {
                result.Add(MapSummary(reader));
            }
            return Result<IReadOnlyList<CafeOrderSummary>>.Success(result);
        }
        catch (Exception ex) when (ex is SqliteException or FormatException)
        {
            return Result<IReadOnlyList<CafeOrderSummary>>.Fail(
                Error.Unexpected("CafeOrder.ListFailed", ex.Message));
        }
    }

    public static Result ApplyCanonical(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid sourceOperationId,
        CafeOrderOperationV1 payload)
    {
        string? validation = CafeOrderOperationCanonicalJson.Validate(payload);
        if (validation is not null)
        {
            return Result.Fail(
                Error.Validation("CafeOrder.CanonicalInvalid", validation));
        }

        try
        {
            OperationRow? existing = ReadOperation(
                connection,
                transaction,
                sourceOperationId);
            string json = CafeOrderOperationCanonicalJson.Serialize(payload);
            string hash = CafeOrderOperationCanonicalJson.ComputeHash(json);

            if (existing is not null)
            {
                return existing.OrderId == payload.OrderId &&
                       existing.ExpectedVersion == payload.ExpectedVersion &&
                       existing.ResultingVersion == payload.ResultingVersion &&
                       string.Equals(existing.OperationType, payload.OperationType, StringComparison.Ordinal) &&
                       string.Equals(existing.PayloadHash, hash, StringComparison.Ordinal)
                    ? Result.Success()
                    : Result.Fail(
                        Error.Conflict(
                            "CafeOrder.CanonicalOperationConflict",
                            "Source operation already exists with different canonical order evidence."));
            }

            if (!MenuRevisionExists(
                    connection,
                    transaction,
                    payload.MenuPublicationRevisionId,
                    payload.StoreId))
            {
                return Result.Fail(
                    Error.Conflict(
                        "CafeOrder.MenuPrerequisiteMissing",
                        "Canonical order requires the referenced MenuPublication first."));
            }

            if (payload.RecipeBookRevisionId is not null &&
                !RecipeRevisionExists(
                    connection,
                    transaction,
                    payload.RecipeBookRevisionId,
                    payload.StoreId))
            {
                return Result.Fail(
                    Error.Conflict(
                        "CafeOrder.RecipePrerequisiteMissing",
                        "Canonical order requires the referenced RecipeBook publication first."));
            }

            OrderRow? order = ReadOrder(connection, transaction, payload.OrderId);

            if (payload.ExpectedVersion == 0)
            {
                if (order is not null)
                {
                    return Result.Fail(
                        Error.Conflict(
                            "CafeOrder.RemoteOrderConflict",
                            "Canonical create conflicts with an existing order."));
                }

                if (payload.Revision is null)
                {
                    return Result.Fail(
                        Error.Validation(
                            "CafeOrder.RemoteRevisionMissing",
                            "Canonical create requires revision content."));
                }

                InsertOrderFromCanonical(connection, transaction, payload);
                InsertRevisionFromCanonical(connection, transaction, payload.OrderId, payload.Revision);
            }
            else
            {
                if (order is null ||
                    order.StoreId != payload.StoreId ||
                    order.CurrentVersion != payload.ExpectedVersion)
                {
                    return Result.Fail(
                        Error.Conflict(
                            "CafeOrder.RemoteVersionConflict",
                            "Canonical order ExpectedVersion does not match local projection."));
                }

                if (!string.Equals(
                        order.MenuPublicationRevisionId,
                        payload.MenuPublicationRevisionId,
                        StringComparison.Ordinal))
                {
                    return Result.Fail(
                        Error.Conflict(
                            "CafeOrder.RemoteMenuBindingConflict",
                            "Canonical order attempts to change the immutable MenuPublication binding."));
                }

                if (payload.Revision is not null)
                {
                    InsertRevisionFromCanonical(connection, transaction, payload.OrderId, payload.Revision);
                }

                if (payload.SaleId.HasValue &&
                    !SaleExists(connection, transaction, payload.SaleId.Value))
                {
                    return Result.Fail(
                        Error.Conflict(
                            "CafeOrder.SalePrerequisiteMissing",
                            "Canonical completed order requires the matching Sale first."));
                }

                ApplyCanonicalProjectionUpdate(connection, transaction, payload);
            }

            Guid? resultSale = payload.SaleId;
            InsertOperationAndLifecycle(
                connection,
                transaction,
                sourceOperationId,
                payload.OrderId,
                payload.OperationType,
                hash,
                payload.ExpectedVersion,
                payload.ResultingVersion,
                payload.CurrentRevisionId,
                payload.ToState,
                resultSale,
                payload.ActorUserId,
                payload.OccurredAtUtc,
                payload.Action,
                payload.FromState,
                payload.ToState,
                payload.Reason,
                payload.SubmittedRevisionId,
                payload.RecipeBookRevisionId,
                resultSale);

            if (payload.OperationType == CafeOrderOperationContract.OrderCompletedV1)
            {
                InsertCompletionLinksCanonical(
                    connection,
                    transaction,
                    sourceOperationId,
                    payload);
            }

            return Result.Success();
        }
        catch (Exception ex) when (
            ex is SqliteException or InvalidOperationException or FormatException or OverflowException)
        {
            return Result.Fail(
                Error.Unexpected("CafeOrder.CanonicalApplyFailed", ex.Message));
        }
    }

    public static bool VerifyCanonicalOperation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid sourceOperationId,
        CafeOrderOperationV1 payload)
    {
        OperationRow? operation = ReadOperation(connection, transaction, sourceOperationId);
        if (operation is null)
        {
            return false;
        }

        string json = CafeOrderOperationCanonicalJson.Serialize(payload);
        string hash = CafeOrderOperationCanonicalJson.ComputeHash(json);
        if (operation.OrderId != payload.OrderId ||
            operation.ExpectedVersion != payload.ExpectedVersion ||
            operation.ResultingVersion != payload.ResultingVersion ||
            !string.Equals(operation.OperationType, payload.OperationType, StringComparison.Ordinal) ||
            !string.Equals(operation.PayloadHash, hash, StringComparison.Ordinal))
        {
            return false;
        }

        OrderRow? order = ReadOrder(connection, transaction, payload.OrderId);
        return order is not null &&
            order.StoreId == payload.StoreId &&
            order.CurrentVersion >= payload.ResultingVersion;
    }

    internal static OrderRow? ReadOrder(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid orderId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT order_id,store_id,current_version,current_revision_id,
                   menu_publication_revision_id,state,service_mode,service_reference,
                   created_by_user_id,created_at_utc,updated_at_utc,
                   submitted_revision_id,submitted_recipe_book_revision_id,
                   completed_sale_id,cancellation_reason
            FROM cafe_orders
            WHERE order_id=$order
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$order", orderId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? MapOrder(reader) : null;
    }

    internal static CafeOrderRevisionSnapshot ReadRevision(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid revisionId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT revision_id,order_id,revision_number,order_version,previous_revision_id,
                   menu_publication_revision_id,service_mode,service_reference,content_hash,
                   actor_user_id,created_at_utc
            FROM cafe_order_revisions
            WHERE revision_id=$revision
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$revision", revisionId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException("Cafe order revision was not found.");
        }

        Guid orderId = Guid.Parse(reader.GetString(1));
        CafeOrderRevisionSnapshot header = new(
            Guid.Parse(reader.GetString(0)),
            orderId,
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.IsDBNull(4) ? null : Guid.Parse(reader.GetString(4)),
            reader.GetString(5),
            reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetString(8),
            Guid.Parse(reader.GetString(9)),
            DateTimeOffset.Parse(reader.GetString(10)),
            Array.Empty<CafeOrderLineSnapshot>());

        return header with
        {
            Lines = ReadRevisionLines(connection, transaction, revisionId)
        };
    }

    private Result<CafeOrderMutationResult> Transition(
        Guid operationId,
        Guid deviceId,
        Guid orderId,
        Guid storeId,
        int expectedVersion,
        Guid actorUserId,
        DateTimeOffset occurredAtUtc,
        string operationType,
        string action,
        string fromState,
        string toState,
        string? reason,
        bool allowStateMismatch = false)
    {
        if (!ValidIdentity(operationId, deviceId, orderId, storeId, actorUserId) ||
            expectedVersion <= 0)
        {
            return Result<CafeOrderMutationResult>.Fail(
                Error.Validation(
                    "CafeOrder.TransitionInvalid",
                    "Lifecycle transition requires stable identities and a positive expected version."));
        }

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Result authority = ValidateEnvelopeAuthority(
                connection,
                transaction,
                deviceId,
                storeId,
                actorUserId);
            if (authority.IsFailure)
            {
                transaction.Rollback();
                return Result<CafeOrderMutationResult>.Fail(authority.ErrorDetail);
            }

            Result<CafeOrderMutationResult>? replay =
                TryReplayLifecycle(
                    connection,
                    transaction,
                    operationId,
                    deviceId,
                    orderId,
                    storeId,
                    expectedVersion,
                    actorUserId,
                    operationType,
                    reason);
            if (replay is not null)
            {
                transaction.Commit();
                return replay;
            }

            OrderRow order = ReadOrder(connection, transaction, orderId)
                ?? throw new CafeOrderValidationException(
                    "CafeOrder.OrderMissing",
                    "Order was not found.");

            if (order.StoreId != storeId ||
                order.CurrentVersion != expectedVersion)
            {
                throw new CafeOrderValidationException(
                    "CafeOrder.ExpectedVersionConflict",
                    "Order version/store authority changed; last-write-wins is not allowed.");
            }

            if (!string.Equals(order.State, fromState, StringComparison.Ordinal))
            {
                if (allowStateMismatch)
                {
                    transaction.Rollback();
                    return Result<CafeOrderMutationResult>.Fail(
                        Error.Conflict(
                            "CafeOrder.TransitionSourceMismatch",
                            "Order is not in the attempted lifecycle source state."));
                }

                throw new CafeOrderValidationException(
                    "CafeOrder.IllegalTransition",
                    "Order lifecycle transition is not allowed from the current state.");
            }

            int resultingVersion = checked(expectedVersion + 1);
            CafeOrderOperationV1 payload = BuildLifecyclePayload(
                operationType,
                order,
                expectedVersion,
                resultingVersion,
                action,
                fromState,
                toState,
                actorUserId,
                occurredAtUtc,
                reason,
                order.SubmittedRevisionId,
                order.SubmittedRecipeBookRevisionId,
                null,
                null,
                null,
                Array.Empty<CafeOrderSaleItemLinkV1>());

            string json = CafeOrderOperationCanonicalJson.Serialize(payload);
            string hash = CafeOrderOperationCanonicalJson.ComputeHash(json);

            using (SqliteCommand update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE cafe_orders
                    SET current_version=$version,
                        state=$state,
                        updated_at_utc=$at
                    WHERE order_id=$order
                      AND current_version=$expected
                      AND state=$fromState;
                    """;
                update.Parameters.AddWithValue("$version", resultingVersion);
                update.Parameters.AddWithValue("$state", toState);
                update.Parameters.AddWithValue("$at", occurredAtUtc.ToUniversalTime().ToString("O"));
                update.Parameters.AddWithValue("$order", orderId.ToString("D"));
                update.Parameters.AddWithValue("$expected", expectedVersion);
                update.Parameters.AddWithValue("$fromState", fromState);
                if (update.ExecuteNonQuery() != 1)
                {
                    throw new CafeOrderValidationException(
                        "CafeOrder.TransitionConcurrentConflict",
                        "Order lost lifecycle transition ownership.");
                }
            }

            InsertOperationAndLifecycle(
                connection,
                transaction,
                operationId,
                orderId,
                operationType,
                hash,
                expectedVersion,
                resultingVersion,
                order.CurrentRevisionId,
                toState,
                null,
                actorUserId,
                occurredAtUtc,
                action,
                fromState,
                toState,
                reason,
                order.SubmittedRevisionId,
                order.SubmittedRecipeBookRevisionId,
                null);

            Enqueue(
                connection,
                transaction,
                deviceId,
                storeId,
                actorUserId,
                operationId,
                orderId,
                expectedVersion,
                operationType,
                occurredAtUtc,
                json);

            transaction.Commit();
            return Result<CafeOrderMutationResult>.Success(
                new CafeOrderMutationResult(
                    orderId,
                    resultingVersion,
                    toState,
                    order.CurrentRevisionId,
                    false));
        }
        catch (CafeOrderValidationException ex)
        {
            return Result<CafeOrderMutationResult>.Fail(
                Error.Conflict(ex.Code, ex.Message));
        }
        catch (Exception ex) when (
            ex is SqliteException or InvalidOperationException or FormatException or OverflowException)
        {
            return Result<CafeOrderMutationResult>.Fail(
                Error.Unexpected("CafeOrder.TransitionFailed", ex.Message));
        }
    }

    private Result<CafeOrderMutationResult>? TryReplayRevision(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeOrderRevisionMutation mutation)
    {
        SyncOutboxItem? outbox = _outbox.Find(
            connection,
            transaction,
            mutation.OperationId);
        OperationRow? operation = ReadOperation(
            connection,
            transaction,
            mutation.OperationId);

        if (outbox is null && operation is null)
        {
            return null;
        }

        if (outbox is null || operation is null ||
            !string.Equals(
                outbox.Envelope.OperationType,
                CafeOrderOperationContract.OrderRevisionCommittedV1,
                StringComparison.Ordinal))
        {
            return Result<CafeOrderMutationResult>.Fail(
                Error.Conflict(
                    "CafeOrder.OperationConflict",
                    "OperationId is already bound to different local evidence."));
        }

        CafeOrderCanonicalParseResult parsed =
            CafeOrderOperationCanonicalJson.Parse(
                outbox.Envelope.OperationType,
                outbox.Envelope.PayloadJson);
        if (!parsed.IsValid || parsed.Payload?.Revision is null)
        {
            return Result<CafeOrderMutationResult>.Fail(
                Error.Conflict(
                    "CafeOrder.ReplayEvidenceInvalid",
                    "Retained order revision outbox evidence is invalid."));
        }

        CafeOrderOperationV1 payload = parsed.Payload;
        if (!RevisionRequestMatchesPayload(mutation, payload))
        {
            return Result<CafeOrderMutationResult>.Fail(
                Error.Conflict(
                    "CafeOrder.OperationPayloadConflict",
                    "OperationId already belongs to a different order revision payload."));
        }

        return Result<CafeOrderMutationResult>.Success(
            new CafeOrderMutationResult(
                payload.OrderId,
                payload.ResultingVersion,
                payload.ToState,
                payload.CurrentRevisionId,
                true));
    }

    private Result<CafeOrderMutationResult>? TryReplayLifecycle(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        Guid deviceId,
        Guid orderId,
        Guid storeId,
        int expectedVersion,
        Guid actorUserId,
        string operationType,
        string? reason)
    {
        SyncOutboxItem? outbox = _outbox.Find(
            connection,
            transaction,
            operationId);
        OperationRow? operation = ReadOperation(
            connection,
            transaction,
            operationId);

        if (outbox is null && operation is null)
        {
            return null;
        }

        if (outbox is null || operation is null ||
            outbox.Envelope.DeviceId != deviceId ||
            outbox.Envelope.StoreId != storeId ||
            outbox.Envelope.ActorUserId != actorUserId ||
            !string.Equals(outbox.Envelope.OperationType, operationType, StringComparison.Ordinal))
        {
            return Result<CafeOrderMutationResult>.Fail(
                Error.Conflict(
                    "CafeOrder.OperationConflict",
                    "OperationId is already bound to different local evidence."));
        }

        CafeOrderCanonicalParseResult parsed =
            CafeOrderOperationCanonicalJson.Parse(
                operationType,
                outbox.Envelope.PayloadJson);
        CafeOrderOperationV1? payload = parsed.Payload;
        if (!parsed.IsValid ||
            payload is null ||
            payload.OrderId != orderId ||
            payload.StoreId != storeId ||
            payload.ExpectedVersion != expectedVersion ||
            payload.ActorUserId != actorUserId ||
            !string.Equals(
                Clean(payload.Reason),
                Clean(reason),
                StringComparison.Ordinal))
        {
            return Result<CafeOrderMutationResult>.Fail(
                Error.Conflict(
                    "CafeOrder.OperationPayloadConflict",
                    "OperationId already belongs to a different lifecycle payload."));
        }

        return Result<CafeOrderMutationResult>.Success(
            new CafeOrderMutationResult(
                payload.OrderId,
                payload.ResultingVersion,
                payload.ToState,
                payload.CurrentRevisionId,
                true));
    }

    private Result<CafeOrderPreparationSubmissionResult>? TryReplaySubmission(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeOrderPreparationSubmitMutation mutation)
    {
        Result<CafeOrderMutationResult>? replay = TryReplayLifecycle(
            connection,
            transaction,
            mutation.OperationId,
            mutation.DeviceId,
            mutation.OrderId,
            mutation.StoreId,
            mutation.ExpectedVersion,
            mutation.ActorUserId,
            CafeOrderOperationContract.OrderPreparationSubmittedV1,
            null);

        if (replay is null)
        {
            return null;
        }

        if (replay.IsFailure)
        {
            return Result<CafeOrderPreparationSubmissionResult>.Fail(
                replay.ErrorDetail);
        }

        SyncOutboxItem outbox = _outbox.Find(
            connection,
            transaction,
            mutation.OperationId)!;
        CafeOrderOperationV1 payload =
            CafeOrderOperationCanonicalJson.Parse(
                outbox.Envelope.OperationType,
                outbox.Envelope.PayloadJson).Payload!;

        return Result<CafeOrderPreparationSubmissionResult>.Success(
            new CafeOrderPreparationSubmissionResult(
                payload.OrderId,
                payload.ResultingVersion,
                payload.SubmittedRevisionId!.Value,
                payload.RecipeBookRevisionId,
                true));
    }

    private static string? ValidateRevisionMutation(CafeOrderRevisionMutation mutation)
    {
        if (!ValidIdentity(
                mutation.OperationId,
                mutation.DeviceId,
                mutation.OrderId,
                mutation.StoreId,
                mutation.ActorUserId))
        {
            return "Stable operation/device/order/store/actor identities are required.";
        }

        if (mutation.ExpectedVersion < 0)
        {
            return "ExpectedVersion cannot be negative.";
        }

        if (!CafeOrderServiceModes.IsSupported(mutation.ServiceMode))
        {
            return "Service mode must be DineIn or Takeout.";
        }

        if (mutation.Lines is null || mutation.Lines.Count == 0)
        {
            return "At least one order line is required.";
        }

        HashSet<Guid> lineIds = new();
        foreach (CafeOrderLineMutation line in mutation.Lines)
        {
            if (line.OrderLineId == Guid.Empty ||
                line.ProductId == Guid.Empty ||
                line.QuantityX1000 <= 0 ||
                !lineIds.Add(line.OrderLineId))
            {
                return "Order lines require unique stable identities, a product and positive quantity.";
            }

            if (line.ModifierOptionIds is null ||
                line.ModifierOptionIds.Any(string.IsNullOrWhiteSpace) ||
                line.ModifierOptionIds.Distinct(StringComparer.Ordinal).Count() !=
                    line.ModifierOptionIds.Count)
            {
                return "Modifier option identities must be non-empty and unique.";
            }
        }

        return null;
    }

    private static IReadOnlyList<CafeOrderLineSnapshot> ResolveRevisionLines(
        MenuAuthority menu,
        IReadOnlyList<CafeOrderLineMutation> mutations)
    {
        List<CafeOrderLineSnapshot> result = new();

        foreach (CafeOrderLineMutation mutation in
            mutations.OrderBy(x => x.DisplayOrder).ThenBy(x => x.OrderLineId))
        {
            MenuItemRow item = menu.Items.TryGetValue(mutation.ProductId, out MenuItemRow? itemRow)
                ? itemRow
                : throw new CafeOrderValidationException(
                    "CafeOrder.ProductNotInBoundMenu",
                    "Order Product does not exist in the bound MenuPublication.");

            if (item.IsUnavailable)
            {
                throw new CafeOrderValidationException(
                    "CafeOrder.ProductUnavailable",
                    "Unavailable menu item cannot be selected in a new order revision.");
            }

            MenuVariantRow? variant = null;
            if (!string.IsNullOrWhiteSpace(mutation.VariantId))
            {
                if (!menu.Variants.TryGetValue(mutation.VariantId.Trim(), out variant) ||
                    variant.ProductId != mutation.ProductId)
                {
                    throw new CafeOrderValidationException(
                        "CafeOrder.VariantProductMismatch",
                        "Selected Variant does not belong to the selected Product.");
                }

                if (variant.IsUnavailable)
                {
                    throw new CafeOrderValidationException(
                        "CafeOrder.VariantUnavailable",
                        "Unavailable Variant cannot be selected in a new order revision.");
                }
            }

            HashSet<string> allowedGroups = new(StringComparer.Ordinal);
            if (menu.ItemGroupLinks.TryGetValue(mutation.ProductId.ToString("D"), out HashSet<string>? itemGroups))
            {
                allowedGroups.UnionWith(itemGroups);
            }
            if (variant is not null &&
                menu.VariantGroupLinks.TryGetValue(variant.Id, out HashSet<string>? variantGroups))
            {
                allowedGroups.UnionWith(variantGroups);
            }

            List<MenuOptionRow> selectedOptions = new();
            foreach (string optionIdRaw in mutation.ModifierOptionIds)
            {
                string optionId = optionIdRaw.Trim();
                MenuOptionRow option = menu.Options.TryGetValue(optionId, out MenuOptionRow? optionRow)
                    ? optionRow
                    : throw new CafeOrderValidationException(
                        "CafeOrder.ModifierOptionMissing",
                        "Selected modifier option is not in the bound MenuPublication.");

                if (!allowedGroups.Contains(option.GroupId))
                {
                    throw new CafeOrderValidationException(
                        "CafeOrder.ModifierGroupNotAllowed",
                        "Selected modifier option belongs to a group not linked to the Item/Variant.");
                }

                selectedOptions.Add(option);
            }

            foreach (string groupId in allowedGroups)
            {
                if (!menu.Groups.TryGetValue(groupId, out MenuGroupRow? group))
                {
                    throw new CafeOrderValidationException(
                        "CafeOrder.ModifierGroupMissing",
                        "Bound modifier group is missing from the MenuPublication.");
                }

                int selected = selectedOptions.Count(x =>
                    string.Equals(x.GroupId, groupId, StringComparison.Ordinal));

                if (selected < group.MinSelections ||
                    selected > group.MaxSelections ||
                    (group.IsRequired && selected < 1))
                {
                    throw new CafeOrderValidationException(
                        "CafeOrder.ModifierSelectionConstraint",
                        "Selected modifiers violate bound min/max requirements.");
                }
            }

            long basePrice = item.SellingPriceCents;
            long selectedBasePrice = variant?.SellingPriceCents ?? basePrice;
            long unitPrice = checked(selectedBasePrice + selectedOptions.Sum(x => x.PriceDeltaCents));
            if (unitPrice < 0)
            {
                throw new CafeOrderValidationException(
                    "CafeOrder.NegativeUnitPrice",
                    "Variant/modifier selection cannot produce a negative unit price.");
            }

            long lineTotal = checked((long)Math.Round(
                unitPrice * (mutation.QuantityX1000 / 1000m),
                0,
                MidpointRounding.AwayFromZero));

            IReadOnlyList<CafeOrderModifierSelectionSnapshot> modifiers = selectedOptions
                .Select(option =>
                {
                    MenuGroupRow group = menu.Groups[option.GroupId];
                    return new CafeOrderModifierSelectionSnapshot(
                        group.Id,
                        group.Name,
                        option.Id,
                        option.Name,
                        option.PriceDeltaCents,
                        option.DisplayOrder);
                })
                .OrderBy(x => x.DisplayOrder)
                .ThenBy(x => x.GroupId, StringComparer.Ordinal)
                .ThenBy(x => x.OptionId, StringComparer.Ordinal)
                .ToArray();

            result.Add(new CafeOrderLineSnapshot(
                mutation.OrderLineId,
                mutation.ProductId,
                item.Name,
                item.UnitName,
                mutation.QuantityX1000,
                basePrice,
                variant?.Id,
                variant?.Name,
                variant?.SellingPriceCents,
                modifiers,
                Clean(mutation.Note),
                unitPrice,
                lineTotal,
                mutation.DisplayOrder));
        }

        return result;
    }

    private static MenuAuthority ReadMenuAuthority(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string revisionId,
        Guid storeId)
    {
        if (!MenuRevisionExists(connection, transaction, revisionId, storeId))
        {
            throw new CafeOrderValidationException(
                "CafeOrder.MenuPublicationMissing",
                "Bound MenuPublication does not exist for this store.");
        }

        Dictionary<Guid, MenuItemRow> items = new();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT product_id,name,COALESCE(unit_name,'Unit'),selling_price_cents,
                       is_temporarily_unavailable
                FROM menu_revision_items
                WHERE revision_id=$revision;
                """;
            command.Parameters.AddWithValue("$revision", revisionId);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                Guid id = Guid.Parse(reader.GetString(0));
                items[id] = new MenuItemRow(
                    id,
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt64(3),
                    reader.GetInt32(4) != 0);
            }
        }

        Dictionary<string, MenuVariantRow> variants = new(StringComparer.Ordinal);
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT variant_id,product_id,name,selling_price_cents,is_temporarily_unavailable
                FROM menu_revision_variants
                WHERE revision_id=$revision;
                """;
            command.Parameters.AddWithValue("$revision", revisionId);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                MenuVariantRow row = new(
                    reader.GetString(0),
                    Guid.Parse(reader.GetString(1)),
                    reader.GetString(2),
                    reader.GetInt64(3),
                    reader.GetInt32(4) != 0);
                variants[row.Id] = row;
            }
        }

        Dictionary<string, MenuGroupRow> groups = new(StringComparer.Ordinal);
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT group_id,name,is_required,min_selections,max_selections,display_order
                FROM menu_revision_modifier_groups
                WHERE revision_id=$revision;
                """;
            command.Parameters.AddWithValue("$revision", revisionId);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                MenuGroupRow row = new(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetInt32(2) != 0,
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5));
                groups[row.Id] = row;
            }
        }

        Dictionary<string, MenuOptionRow> options = new(StringComparer.Ordinal);
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT option_id,group_id,name,price_delta_cents,display_order
                FROM menu_revision_modifier_options
                WHERE revision_id=$revision;
                """;
            command.Parameters.AddWithValue("$revision", revisionId);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                MenuOptionRow row = new(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt64(3),
                    reader.GetInt32(4));
                options[row.Id] = row;
            }
        }

        Dictionary<string, HashSet<string>> itemLinks =
            ReadGroupLinks(
                connection,
                transaction,
                "menu_revision_item_modifier_groups",
                "product_id",
                revisionId);
        Dictionary<string, HashSet<string>> variantLinks =
            ReadGroupLinks(
                connection,
                transaction,
                "menu_revision_variant_modifier_groups",
                "variant_id",
                revisionId);

        return new MenuAuthority(items, variants, groups, options, itemLinks, variantLinks);
    }

    private static Dictionary<string, HashSet<string>> ReadGroupLinks(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string ownerColumn,
        string revisionId)
    {
        Dictionary<string, HashSet<string>> result = new(StringComparer.Ordinal);
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT " + ownerColumn + ",group_id FROM " + table +
            " WHERE revision_id=$revision ORDER BY display_order,group_id;";
        command.Parameters.AddWithValue("$revision", revisionId);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            string owner = reader.GetString(0);
            if (!result.TryGetValue(owner, out HashSet<string>? groups))
            {
                groups = new HashSet<string>(StringComparer.Ordinal);
                result[owner] = groups;
            }
            groups.Add(reader.GetString(1));
        }
        return result;
    }

    internal static string ComputeRevisionContentHash(
        Guid orderId,
        Guid revisionId,
        int revisionNumber,
        int orderVersion,
        Guid? previousRevisionId,
        string menuRevisionId,
        string serviceMode,
        string? serviceReference,
        IReadOnlyList<CafeOrderLineSnapshot> lines)
    {
        StringBuilder canonical = new();
        canonical.Append("salryn-c4-order-revision-v1|")
            .Append(orderId.ToString("D")).Append('|')
            .Append(revisionId.ToString("D")).Append('|')
            .Append(revisionNumber).Append('|')
            .Append(orderVersion).Append('|')
            .Append(previousRevisionId?.ToString("D") ?? string.Empty).Append('|')
            .Append(menuRevisionId).Append('|')
            .Append(serviceMode).Append('|')
            .Append(serviceReference ?? string.Empty);

        foreach (CafeOrderLineSnapshot line in
            lines.OrderBy(x => x.DisplayOrder).ThenBy(x => x.OrderLineId))
        {
            canonical.Append("|L|")
                .Append(line.OrderLineId.ToString("D")).Append('|')
                .Append(line.ProductId.ToString("D")).Append('|')
                .Append(line.ProductNameSnapshot).Append('|')
                .Append(line.UnitNameSnapshot).Append('|')
                .Append(line.QuantityX1000).Append('|')
                .Append(line.BaseUnitPriceCents).Append('|')
                .Append(line.VariantId ?? string.Empty).Append('|')
                .Append(line.VariantNameSnapshot ?? string.Empty).Append('|')
                .Append(line.VariantUnitPriceCents?.ToString() ?? string.Empty).Append('|')
                .Append(line.Note ?? string.Empty).Append('|')
                .Append(line.CalculatedUnitPriceCents).Append('|')
                .Append(line.LineTotalCents).Append('|')
                .Append(line.DisplayOrder);

            foreach (CafeOrderModifierSelectionSnapshot modifier in line.ModifierSelections)
            {
                canonical.Append("|M|")
                    .Append(modifier.GroupId).Append('|')
                    .Append(modifier.GroupNameSnapshot).Append('|')
                    .Append(modifier.OptionId).Append('|')
                    .Append(modifier.OptionNameSnapshot).Append('|')
                    .Append(modifier.PriceDeltaCents).Append('|')
                    .Append(modifier.DisplayOrder);
            }
        }

        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
    }

    private static CafeOrderOperationV1 ToRevisionOperationPayload(
        CafeOrderRevisionMutation mutation,
        CafeOrderRevisionSnapshot revision,
        string action,
        string? fromState,
        string toState) =>
        new(
            CafeOrderOperationContract.OrderRevisionCommittedV1,
            mutation.OrderId,
            mutation.StoreId,
            mutation.ExpectedVersion,
            revision.OrderVersion,
            action,
            fromState,
            toState,
            mutation.ActorUserId,
            mutation.OccurredAtUtc.ToUniversalTime(),
            revision.RevisionId,
            revision.MenuPublicationRevisionId,
            revision.ServiceMode,
            revision.ServiceReference,
            ToContractRevision(revision),
            null,
            null,
            null,
            null,
            null,
            null,
            Array.Empty<CafeOrderSaleItemLinkV1>());

    internal static CafeOrderRevisionSnapshotV1 ToContractRevision(
        CafeOrderRevisionSnapshot revision) =>
        new(
            revision.RevisionId,
            revision.RevisionNumber,
            revision.OrderVersion,
            revision.PreviousRevisionId,
            revision.MenuPublicationRevisionId,
            revision.ServiceMode,
            revision.ServiceReference,
            revision.ContentHash,
            revision.ActorUserId,
            revision.CreatedAtUtc.ToUniversalTime(),
            revision.Lines.Select(line => new CafeOrderLineSnapshotV1(
                line.OrderLineId,
                line.ProductId,
                line.ProductNameSnapshot,
                line.UnitNameSnapshot,
                line.QuantityX1000,
                line.BaseUnitPriceCents,
                line.VariantId,
                line.VariantNameSnapshot,
                line.VariantUnitPriceCents,
                line.ModifierSelections.Select(modifier =>
                    new CafeOrderModifierSnapshotV1(
                        modifier.GroupId,
                        modifier.GroupNameSnapshot,
                        modifier.OptionId,
                        modifier.OptionNameSnapshot,
                        modifier.PriceDeltaCents,
                        modifier.DisplayOrder)).ToArray(),
                line.Note,
                line.CalculatedUnitPriceCents,
                line.LineTotalCents,
                line.DisplayOrder)).ToArray());

    internal static CafeOrderOperationV1 BuildLifecyclePayload(
        string operationType,
        OrderRow order,
        int expectedVersion,
        int resultingVersion,
        string action,
        string? fromState,
        string toState,
        Guid actorUserId,
        DateTimeOffset occurredAtUtc,
        string? reason,
        Guid? submittedRevisionId,
        string? recipeBookRevisionId,
        Guid? saleId,
        Guid? saleCommittedOperationId,
        string? receiptNumber,
        IReadOnlyList<CafeOrderSaleItemLinkV1> saleItemLinks) =>
        new(
            operationType,
            order.OrderId,
            order.StoreId,
            expectedVersion,
            resultingVersion,
            action,
            fromState,
            toState,
            actorUserId,
            occurredAtUtc.ToUniversalTime(),
            order.CurrentRevisionId,
            order.MenuPublicationRevisionId,
            order.ServiceMode,
            order.ServiceReference,
            null,
            submittedRevisionId,
            recipeBookRevisionId,
            reason,
            saleId,
            saleCommittedOperationId,
            receiptNumber,
            saleItemLinks);

    internal static void InsertOperationAndLifecycle(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        Guid orderId,
        string operationType,
        string payloadHash,
        int expectedVersion,
        int resultingVersion,
        Guid? resultRevisionId,
        string resultState,
        Guid? resultSaleId,
        Guid actorUserId,
        DateTimeOffset occurredAtUtc,
        string action,
        string? fromState,
        string toState,
        string? reason,
        Guid? orderRevisionId,
        string? recipeBookRevisionId,
        Guid? saleId)
    {
        using (SqliteCommand operation = connection.CreateCommand())
        {
            operation.Transaction = transaction;
            operation.CommandText = """
                INSERT INTO cafe_order_operations(
                    operation_id,order_id,operation_type,payload_hash,
                    expected_version,resulting_version,result_revision_id,
                    result_state,result_sale_id,actor_user_id,occurred_at_utc)
                VALUES(
                    $operation,$order,$type,$hash,
                    $expected,$resulting,$revision,
                    $state,$sale,$actor,$at);
                """;
            operation.Parameters.AddWithValue("$operation", operationId.ToString("D"));
            operation.Parameters.AddWithValue("$order", orderId.ToString("D"));
            operation.Parameters.AddWithValue("$type", operationType);
            operation.Parameters.AddWithValue("$hash", payloadHash);
            operation.Parameters.AddWithValue("$expected", expectedVersion);
            operation.Parameters.AddWithValue("$resulting", resultingVersion);
            operation.Parameters.AddWithValue("$revision", DbGuid(resultRevisionId));
            operation.Parameters.AddWithValue("$state", resultState);
            operation.Parameters.AddWithValue("$sale", DbGuid(resultSaleId));
            operation.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
            operation.Parameters.AddWithValue("$at", occurredAtUtc.ToUniversalTime().ToString("O"));
            operation.ExecuteNonQuery();
        }

        using SqliteCommand lifecycle = connection.CreateCommand();
        lifecycle.Transaction = transaction;
        lifecycle.CommandText = """
            INSERT INTO cafe_order_lifecycle_events(
                event_id,operation_id,order_id,expected_version,resulting_version,
                action,from_state,to_state,actor_user_id,occurred_at_utc,
                reason,order_revision_id,recipe_book_revision_id,sale_id,content_hash)
            VALUES(
                $event,$operation,$order,$expected,$resulting,
                $action,$from,$to,$actor,$at,
                $reason,$revision,$recipe,$sale,$hash);
            """;
        lifecycle.Parameters.AddWithValue(
            "$event",
            DeterministicGuid(
                "salryn-c4-order-event-v1",
                operationId.ToString("D")).ToString("D"));
        lifecycle.Parameters.AddWithValue("$operation", operationId.ToString("D"));
        lifecycle.Parameters.AddWithValue("$order", orderId.ToString("D"));
        lifecycle.Parameters.AddWithValue("$expected", expectedVersion);
        lifecycle.Parameters.AddWithValue("$resulting", resultingVersion);
        lifecycle.Parameters.AddWithValue("$action", action);
        lifecycle.Parameters.AddWithValue("$from", DbText(fromState));
        lifecycle.Parameters.AddWithValue("$to", toState);
        lifecycle.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
        lifecycle.Parameters.AddWithValue("$at", occurredAtUtc.ToUniversalTime().ToString("O"));
        lifecycle.Parameters.AddWithValue("$reason", DbText(reason));
        lifecycle.Parameters.AddWithValue("$revision", DbGuid(orderRevisionId));
        lifecycle.Parameters.AddWithValue("$recipe", DbText(recipeBookRevisionId));
        lifecycle.Parameters.AddWithValue("$sale", DbGuid(saleId));
        lifecycle.Parameters.AddWithValue("$hash", payloadHash);
        lifecycle.ExecuteNonQuery();
    }

    private void Enqueue(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid deviceId,
        Guid storeId,
        Guid actorUserId,
        Guid operationId,
        Guid orderId,
        int baseVersion,
        string operationType,
        DateTimeOffset occurredAtUtc,
        string payloadJson)
    {
        _outbox.Enqueue(
            connection,
            transaction,
            new SyncEnqueueRequest(
                deviceId,
                storeId,
                actorUserId,
                operationType,
                CafeOrderOperationContract.SchemaVersion,
                orderId,
                baseVersion,
                occurredAtUtc,
                payloadJson,
                operationId));
    }

    private static void InsertOrder(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeOrderRevisionMutation mutation,
        Guid revisionId,
        string menuRevisionId,
        string state)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO cafe_orders(
                order_id,store_id,current_version,current_revision_id,
                menu_publication_revision_id,state,service_mode,service_reference,
                created_by_user_id,created_at_utc,updated_at_utc,
                submitted_revision_id,submitted_recipe_book_revision_id,
                completed_sale_id,cancellation_reason)
            VALUES(
                $order,$store,1,$revision,
                $menu,$state,$mode,$reference,
                $actor,$at,$at,
                NULL,NULL,NULL,NULL);
            """;
        command.Parameters.AddWithValue("$order", mutation.OrderId.ToString("D"));
        command.Parameters.AddWithValue("$store", mutation.StoreId.ToString("D"));
        command.Parameters.AddWithValue("$revision", revisionId.ToString("D"));
        command.Parameters.AddWithValue("$menu", menuRevisionId);
        command.Parameters.AddWithValue("$state", state);
        command.Parameters.AddWithValue("$mode", mutation.ServiceMode);
        command.Parameters.AddWithValue("$reference", DbText(mutation.ServiceReference));
        command.Parameters.AddWithValue("$actor", mutation.ActorUserId.ToString("D"));
        command.Parameters.AddWithValue("$at", mutation.OccurredAtUtc.ToUniversalTime().ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void InsertOrderFromCanonical(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeOrderOperationV1 payload)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO cafe_orders(
                order_id,store_id,current_version,current_revision_id,
                menu_publication_revision_id,state,service_mode,service_reference,
                created_by_user_id,created_at_utc,updated_at_utc,
                submitted_revision_id,submitted_recipe_book_revision_id,
                completed_sale_id,cancellation_reason)
            VALUES(
                $order,$store,$version,$revision,
                $menu,$state,$mode,$reference,
                $actor,$at,$at,
                $submitted,$recipe,$sale,$reason);
            """;
        command.Parameters.AddWithValue("$order", payload.OrderId.ToString("D"));
        command.Parameters.AddWithValue("$store", payload.StoreId.ToString("D"));
        command.Parameters.AddWithValue("$version", payload.ResultingVersion);
        command.Parameters.AddWithValue("$revision", payload.CurrentRevisionId.ToString("D"));
        command.Parameters.AddWithValue("$menu", payload.MenuPublicationRevisionId);
        command.Parameters.AddWithValue("$state", payload.ToState);
        command.Parameters.AddWithValue("$mode", payload.ServiceMode);
        command.Parameters.AddWithValue("$reference", DbText(payload.ServiceReference));
        command.Parameters.AddWithValue("$actor", payload.ActorUserId.ToString("D"));
        command.Parameters.AddWithValue("$at", payload.OccurredAtUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$submitted", DbGuid(payload.SubmittedRevisionId));
        command.Parameters.AddWithValue("$recipe", DbText(payload.RecipeBookRevisionId));
        command.Parameters.AddWithValue("$sale", DbGuid(payload.SaleId));
        command.Parameters.AddWithValue("$reason", DbText(payload.Reason));
        command.ExecuteNonQuery();
    }

    private static void ApplyCanonicalProjectionUpdate(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeOrderOperationV1 payload)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE cafe_orders
            SET current_version=$version,
                current_revision_id=$revision,
                state=$state,
                service_mode=$mode,
                service_reference=$reference,
                submitted_revision_id=COALESCE($submitted,submitted_revision_id),
                submitted_recipe_book_revision_id=COALESCE($recipe,submitted_recipe_book_revision_id),
                completed_sale_id=COALESCE($sale,completed_sale_id),
                cancellation_reason=COALESCE($reason,cancellation_reason),
                updated_at_utc=$at
            WHERE order_id=$order
              AND current_version=$expected;
            """;
        command.Parameters.AddWithValue("$version", payload.ResultingVersion);
        command.Parameters.AddWithValue("$revision", payload.CurrentRevisionId.ToString("D"));
        command.Parameters.AddWithValue("$state", payload.ToState);
        command.Parameters.AddWithValue("$mode", payload.ServiceMode);
        command.Parameters.AddWithValue("$reference", DbText(payload.ServiceReference));
        command.Parameters.AddWithValue("$submitted", DbGuid(payload.SubmittedRevisionId));
        command.Parameters.AddWithValue("$recipe", DbText(payload.RecipeBookRevisionId));
        command.Parameters.AddWithValue("$sale", DbGuid(payload.SaleId));
        command.Parameters.AddWithValue("$reason", DbText(payload.Reason));
        command.Parameters.AddWithValue("$at", payload.OccurredAtUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$order", payload.OrderId.ToString("D"));
        command.Parameters.AddWithValue("$expected", payload.ExpectedVersion);
        if (command.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException(
                "Canonical C4 projection lost ExpectedVersion ownership.");
        }
    }

    internal static void InsertRevision(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeOrderRevisionSnapshot revision)
    {
        InsertRevisionHeader(
            connection,
            transaction,
            revision.RevisionId,
            revision.OrderId,
            revision.RevisionNumber,
            revision.OrderVersion,
            revision.PreviousRevisionId,
            revision.MenuPublicationRevisionId,
            revision.ServiceMode,
            revision.ServiceReference,
            revision.ContentHash,
            revision.ActorUserId,
            revision.CreatedAtUtc);

        foreach (CafeOrderLineSnapshot line in revision.Lines)
        {
            InsertRevisionLine(connection, transaction, revision.RevisionId, line);
        }
    }

    private static void InsertRevisionFromCanonical(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid orderId,
        CafeOrderRevisionSnapshotV1 revision)
    {
        InsertRevisionHeader(
            connection,
            transaction,
            revision.RevisionId,
            orderId,
            revision.RevisionNumber,
            revision.OrderVersion,
            revision.PreviousRevisionId,
            revision.MenuPublicationRevisionId,
            revision.ServiceMode,
            revision.ServiceReference,
            revision.ContentHash,
            revision.ActorUserId,
            revision.CreatedAtUtc);

        foreach (CafeOrderLineSnapshotV1 line in revision.Lines)
        {
            CafeOrderLineSnapshot core = new(
                line.OrderLineId,
                line.ProductId,
                line.ProductNameSnapshot,
                line.UnitNameSnapshot,
                line.QuantityX1000,
                line.BaseUnitPriceCents,
                line.VariantId,
                line.VariantNameSnapshot,
                line.VariantUnitPriceCents,
                line.ModifierSelections.Select(x =>
                    new CafeOrderModifierSelectionSnapshot(
                        x.GroupId,
                        x.GroupNameSnapshot,
                        x.OptionId,
                        x.OptionNameSnapshot,
                        x.PriceDeltaCents,
                        x.DisplayOrder)).ToArray(),
                line.Note,
                line.CalculatedUnitPriceCents,
                line.LineTotalCents,
                line.DisplayOrder);
            InsertRevisionLine(connection, transaction, revision.RevisionId, core);
        }
    }

    private static void InsertRevisionHeader(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid revisionId,
        Guid orderId,
        int revisionNumber,
        int orderVersion,
        Guid? previousRevisionId,
        string menuRevisionId,
        string serviceMode,
        string? serviceReference,
        string contentHash,
        Guid actorUserId,
        DateTimeOffset createdAtUtc)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO cafe_order_revisions(
                revision_id,order_id,revision_number,order_version,previous_revision_id,
                menu_publication_revision_id,service_mode,service_reference,content_hash,
                actor_user_id,created_at_utc)
            VALUES(
                $revision,$order,$number,$version,$previous,
                $menu,$mode,$reference,$hash,$actor,$at);
            """;
        command.Parameters.AddWithValue("$revision", revisionId.ToString("D"));
        command.Parameters.AddWithValue("$order", orderId.ToString("D"));
        command.Parameters.AddWithValue("$number", revisionNumber);
        command.Parameters.AddWithValue("$version", orderVersion);
        command.Parameters.AddWithValue("$previous", DbGuid(previousRevisionId));
        command.Parameters.AddWithValue("$menu", menuRevisionId);
        command.Parameters.AddWithValue("$mode", serviceMode);
        command.Parameters.AddWithValue("$reference", DbText(serviceReference));
        command.Parameters.AddWithValue("$hash", contentHash);
        command.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
        command.Parameters.AddWithValue("$at", createdAtUtc.ToUniversalTime().ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void InsertRevisionLine(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid revisionId,
        CafeOrderLineSnapshot line)
    {
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO cafe_order_revision_lines(
                    revision_id,order_line_id,product_id,product_name_snapshot,
                    unit_name_snapshot,quantity_x1000,base_unit_price_cents,
                    variant_id,variant_name_snapshot,variant_unit_price_cents,note,
                    calculated_unit_price_cents,line_total_cents,display_order)
                VALUES(
                    $revision,$line,$product,$name,$unit,$quantity,$basePrice,
                    $variant,$variantName,$variantPrice,$note,
                    $unitPrice,$lineTotal,$displayOrder);
                """;
            command.Parameters.AddWithValue("$revision", revisionId.ToString("D"));
            command.Parameters.AddWithValue("$line", line.OrderLineId.ToString("D"));
            command.Parameters.AddWithValue("$product", line.ProductId.ToString("D"));
            command.Parameters.AddWithValue("$name", line.ProductNameSnapshot);
            command.Parameters.AddWithValue("$unit", line.UnitNameSnapshot);
            command.Parameters.AddWithValue("$quantity", line.QuantityX1000);
            command.Parameters.AddWithValue("$basePrice", line.BaseUnitPriceCents);
            command.Parameters.AddWithValue("$variant", DbText(line.VariantId));
            command.Parameters.AddWithValue("$variantName", DbText(line.VariantNameSnapshot));
            command.Parameters.AddWithValue("$variantPrice", line.VariantUnitPriceCents ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$note", DbText(line.Note));
            command.Parameters.AddWithValue("$unitPrice", line.CalculatedUnitPriceCents);
            command.Parameters.AddWithValue("$lineTotal", line.LineTotalCents);
            command.Parameters.AddWithValue("$displayOrder", line.DisplayOrder);
            command.ExecuteNonQuery();
        }

        foreach (CafeOrderModifierSelectionSnapshot modifier in line.ModifierSelections)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO cafe_order_revision_modifiers(
                    revision_id,order_line_id,group_id,group_name_snapshot,
                    option_id,option_name_snapshot,price_delta_cents,display_order)
                VALUES(
                    $revision,$line,$group,$groupName,
                    $option,$optionName,$price,$displayOrder);
                """;
            command.Parameters.AddWithValue("$revision", revisionId.ToString("D"));
            command.Parameters.AddWithValue("$line", line.OrderLineId.ToString("D"));
            command.Parameters.AddWithValue("$group", modifier.GroupId);
            command.Parameters.AddWithValue("$groupName", modifier.GroupNameSnapshot);
            command.Parameters.AddWithValue("$option", modifier.OptionId);
            command.Parameters.AddWithValue("$optionName", modifier.OptionNameSnapshot);
            command.Parameters.AddWithValue("$price", modifier.PriceDeltaCents);
            command.Parameters.AddWithValue("$displayOrder", modifier.DisplayOrder);
            command.ExecuteNonQuery();
        }
    }

    private static IReadOnlyList<CafeOrderLineSnapshot> ReadRevisionLines(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid revisionId)
    {
        List<RawLine> raw = new();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT order_line_id,product_id,product_name_snapshot,unit_name_snapshot,
                       quantity_x1000,base_unit_price_cents,variant_id,variant_name_snapshot,
                       variant_unit_price_cents,note,calculated_unit_price_cents,
                       line_total_cents,display_order
                FROM cafe_order_revision_lines
                WHERE revision_id=$revision
                ORDER BY display_order,order_line_id;
                """;
            command.Parameters.AddWithValue("$revision", revisionId.ToString("D"));
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                raw.Add(new RawLine(
                    Guid.Parse(reader.GetString(0)),
                    Guid.Parse(reader.GetString(1)),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt64(4),
                    reader.GetInt64(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetInt64(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.GetInt64(10),
                    reader.GetInt64(11),
                    reader.GetInt32(12)));
            }
        }

        return raw.Select(line => new CafeOrderLineSnapshot(
            line.OrderLineId,
            line.ProductId,
            line.ProductNameSnapshot,
            line.UnitNameSnapshot,
            line.QuantityX1000,
            line.BaseUnitPriceCents,
            line.VariantId,
            line.VariantNameSnapshot,
            line.VariantUnitPriceCents,
            ReadModifiers(connection, transaction, revisionId, line.OrderLineId),
            line.Note,
            line.CalculatedUnitPriceCents,
            line.LineTotalCents,
            line.DisplayOrder)).ToArray();
    }

    private static IReadOnlyList<CafeOrderModifierSelectionSnapshot> ReadModifiers(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid revisionId,
        Guid lineId)
    {
        List<CafeOrderModifierSelectionSnapshot> result = new();
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT group_id,group_name_snapshot,option_id,option_name_snapshot,
                   price_delta_cents,display_order
            FROM cafe_order_revision_modifiers
            WHERE revision_id=$revision AND order_line_id=$line
            ORDER BY display_order,group_id,option_id;
            """;
        command.Parameters.AddWithValue("$revision", revisionId.ToString("D"));
        command.Parameters.AddWithValue("$line", lineId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new CafeOrderModifierSelectionSnapshot(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt64(4),
                reader.GetInt32(5)));
        }
        return result;
    }

    private static CafeOrderDetail? ReadDetail(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid orderId)
    {
        OrderRow? row = ReadOrder(connection, transaction, orderId);
        if (row is null)
        {
            return null;
        }

        CafeOrderSummary summary = ToSummary(row);
        CafeOrderRevisionSnapshot revision =
            ReadRevision(connection, transaction, row.CurrentRevisionId);
        List<CafeOrderLifecycleEvent> events = new();

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT event_id,operation_id,expected_version,resulting_version,
                   action,from_state,to_state,actor_user_id,occurred_at_utc,
                   reason,order_revision_id,recipe_book_revision_id,sale_id,content_hash
            FROM cafe_order_lifecycle_events
            WHERE order_id=$order
            ORDER BY resulting_version,occurred_at_utc,event_id;
            """;
        command.Parameters.AddWithValue("$order", orderId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            events.Add(new CafeOrderLifecycleEvent(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                orderId,
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6),
                Guid.Parse(reader.GetString(7)),
                DateTimeOffset.Parse(reader.GetString(8)),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : Guid.Parse(reader.GetString(10)),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : Guid.Parse(reader.GetString(12)),
                reader.GetString(13)));
        }

        return new CafeOrderDetail(summary, revision, events);
    }

    private static bool RevisionRequestMatchesPayload(
        CafeOrderRevisionMutation request,
        CafeOrderOperationV1 payload)
    {
        CafeOrderRevisionSnapshotV1 revision = payload.Revision!;
        if (payload.OrderId != request.OrderId ||
            payload.StoreId != request.StoreId ||
            payload.ExpectedVersion != request.ExpectedVersion ||
            payload.ActorUserId != request.ActorUserId ||
            !string.Equals(payload.ServiceMode, request.ServiceMode, StringComparison.Ordinal) ||
            !string.Equals(Clean(payload.ServiceReference), Clean(request.ServiceReference), StringComparison.Ordinal) ||
            revision.Lines.Count != request.Lines.Count)
        {
            return false;
        }

        Dictionary<Guid, CafeOrderLineSnapshotV1> persisted =
            revision.Lines.ToDictionary(x => x.OrderLineId);
        foreach (CafeOrderLineMutation requested in request.Lines)
        {
            if (!persisted.TryGetValue(requested.OrderLineId, out CafeOrderLineSnapshotV1? line) ||
                line.ProductId != requested.ProductId ||
                !string.Equals(line.VariantId, Clean(requested.VariantId), StringComparison.Ordinal) ||
                line.QuantityX1000 != requested.QuantityX1000 ||
                !string.Equals(Clean(line.Note), Clean(requested.Note), StringComparison.Ordinal) ||
                line.DisplayOrder != requested.DisplayOrder)
            {
                return false;
            }

            string[] a = line.ModifierSelections
                .Select(x => x.OptionId)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();
            string[] b = requested.ModifierOptionIds
                .Select(x => x.Trim())
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();
            if (!a.SequenceEqual(b, StringComparer.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static OrderRow RequireOrderVersion(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid orderId,
        Guid storeId,
        int expectedVersion,
        string requiredState)
    {
        OrderRow order = ReadOrder(connection, transaction, orderId)
            ?? throw new CafeOrderValidationException(
                "CafeOrder.OrderMissing",
                "Order was not found.");

        if (order.StoreId != storeId ||
            order.CurrentVersion != expectedVersion)
        {
            throw new CafeOrderValidationException(
                "CafeOrder.ExpectedVersionConflict",
                "Order version/store authority changed; last-write-wins is not allowed.");
        }

        if (!string.Equals(order.State, requiredState, StringComparison.Ordinal))
        {
            throw new CafeOrderValidationException(
                "CafeOrder.IllegalTransition",
                "Order is not in the required lifecycle state.");
        }

        return order;
    }

    private static Result ValidateEnvelopeAuthority(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid deviceId,
        Guid storeId,
        Guid actorUserId)
    {
        using SqliteCommand store = connection.CreateCommand();
        store.Transaction = transaction;
        store.CommandText = """
            SELECT COUNT(*) FROM deployment_identity
            WHERE store_id=$store;
            """;
        store.Parameters.AddWithValue("$store", storeId.ToString("D"));
        if (Convert.ToInt64(store.ExecuteScalar() ?? 0L) != 1)
        {
            return Result.Fail(
                Error.Conflict(
                    "CafeOrder.StoreAuthorityMismatch",
                    "Order StoreId does not match local deployment authority."));
        }

        using SqliteCommand actor = connection.CreateCommand();
        actor.Transaction = transaction;
        actor.CommandText = "SELECT COUNT(*) FROM users WHERE id=$actor AND status=1;";
        actor.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
        if (Convert.ToInt64(actor.ExecuteScalar() ?? 0L) != 1)
        {
            return Result.Fail(
                Error.Conflict(
                    "CafeOrder.ActorUnavailable",
                    "Order actor is not an active local user."));
        }

        if (deviceId == Guid.Empty)
        {
            return Result.Fail(
                Error.Validation(
                    "CafeOrder.DeviceRequired",
                    "Order mutation requires a stable DeviceId."));
        }

        return Result.Success();
    }

    private static string? ReadCurrentMenuRevisionId(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT revision_id
            FROM menu_current_publication
            WHERE store_id=$store
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$store", storeId.ToString("D"));
        return command.ExecuteScalar() as string;
    }

    private static string? ReadCurrentRecipeBookRevisionId(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT revision_id
            FROM recipe_book_current_publication
            WHERE store_id=$store
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$store", storeId.ToString("D"));
        return command.ExecuteScalar() as string;
    }

    private static int ReadNextRevisionNumber(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid orderId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COALESCE(MAX(revision_number),0)+1
            FROM cafe_order_revisions
            WHERE order_id=$order;
            """;
        command.Parameters.AddWithValue("$order", orderId.ToString("D"));
        return Convert.ToInt32(command.ExecuteScalar() ?? 1);
    }

    private static OperationRow? ReadOperation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT order_id,operation_type,payload_hash,expected_version,
                   resulting_version,result_revision_id,result_state,result_sale_id
            FROM cafe_order_operations
            WHERE operation_id=$operation
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$operation", operationId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? new OperationRow(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.IsDBNull(5) ? null : Guid.Parse(reader.GetString(5)),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : Guid.Parse(reader.GetString(7)))
            : null;
    }

    private static bool MenuRevisionExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string revisionId,
        Guid storeId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*) FROM menu_revisions
            WHERE revision_id=$revision AND store_id=$store;
            """;
        command.Parameters.AddWithValue("$revision", revisionId);
        command.Parameters.AddWithValue("$store", storeId.ToString("D"));
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L) == 1;
    }

    private static bool RecipeRevisionExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string revisionId,
        Guid storeId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*) FROM recipe_book_revisions
            WHERE revision_id=$revision AND store_id=$store;
            """;
        command.Parameters.AddWithValue("$revision", revisionId);
        command.Parameters.AddWithValue("$store", storeId.ToString("D"));
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L) == 1;
    }

    private static bool SaleExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid saleId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM sales WHERE id=$sale;";
        command.Parameters.AddWithValue("$sale", saleId.ToString("D"));
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L) == 1;
    }

    private static void InsertCompletionLinksCanonical(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid sourceOperationId,
        CafeOrderOperationV1 payload)
    {
        using (SqliteCommand link = connection.CreateCommand())
        {
            link.Transaction = transaction;
            link.CommandText = """
                INSERT INTO cafe_order_sale_links(
                    order_id,order_revision_id,sale_id,completion_operation_id,
                    sale_committed_operation_id,completed_at_utc)
                VALUES($order,$revision,$sale,$completion,$saleOperation,$at);
                """;
            link.Parameters.AddWithValue("$order", payload.OrderId.ToString("D"));
            link.Parameters.AddWithValue("$revision", payload.CurrentRevisionId.ToString("D"));
            link.Parameters.AddWithValue("$sale", payload.SaleId!.Value.ToString("D"));
            link.Parameters.AddWithValue("$completion", sourceOperationId.ToString("D"));
            link.Parameters.AddWithValue(
                "$saleOperation",
                payload.SaleCommittedOperationId!.Value.ToString("D"));
            link.Parameters.AddWithValue("$at", payload.OccurredAtUtc.ToUniversalTime().ToString("O"));
            link.ExecuteNonQuery();
        }

        foreach (CafeOrderSaleItemLinkV1 itemLink in payload.SaleItemLinks)
        {
            using SqliteCommand link = connection.CreateCommand();
            link.Transaction = transaction;
            link.CommandText = """
                INSERT INTO sale_item_order_links(
                    sale_item_id,order_id,order_revision_id,order_line_id)
                VALUES($saleItem,$order,$revision,$line);
                """;
            link.Parameters.AddWithValue("$saleItem", itemLink.SaleItemId.ToString("D"));
            link.Parameters.AddWithValue("$order", payload.OrderId.ToString("D"));
            link.Parameters.AddWithValue("$revision", payload.CurrentRevisionId.ToString("D"));
            link.Parameters.AddWithValue("$line", itemLink.OrderLineId.ToString("D"));
            link.ExecuteNonQuery();
        }
    }

    private static void InsertRevisionFromCanonicalIfNeeded(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid orderId,
        CafeOrderRevisionSnapshotV1? revision)
    {
        if (revision is not null)
        {
            InsertRevisionFromCanonical(connection, transaction, orderId, revision);
        }
    }

    private static CafeOrderSummary ToSummary(OrderRow row) =>
        new(
            row.OrderId,
            row.StoreId,
            row.CurrentVersion,
            row.CurrentRevisionId,
            row.MenuPublicationRevisionId,
            row.State,
            row.ServiceMode,
            row.ServiceReference,
            row.CreatedByUserId,
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.SubmittedRevisionId,
            row.SubmittedRecipeBookRevisionId,
            row.CompletedSaleId,
            row.CancellationReason);

    private static CafeOrderSummary MapSummary(SqliteDataReader reader) =>
        ToSummary(MapOrder(reader));

    private static OrderRow MapOrder(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            reader.GetInt32(2),
            Guid.Parse(reader.GetString(3)),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            Guid.Parse(reader.GetString(8)),
            DateTimeOffset.Parse(reader.GetString(9)),
            DateTimeOffset.Parse(reader.GetString(10)),
            reader.IsDBNull(11) ? null : Guid.Parse(reader.GetString(11)),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.IsDBNull(13) ? null : Guid.Parse(reader.GetString(13)),
            reader.IsDBNull(14) ? null : reader.GetString(14));

    internal static Guid DeterministicGuid(string scope, string value)
    {
        byte[] hash = SHA256.HashData(
            Encoding.UTF8.GetBytes(scope + "|" + value));
        byte[] bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes);
    }

    private static bool ValidIdentity(params Guid[] values) =>
        values.All(x => x != Guid.Empty);

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static object DbText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static object DbGuid(Guid? value) =>
        value.HasValue && value.Value != Guid.Empty
            ? value.Value.ToString("D")
            : DBNull.Value;

    internal sealed record OrderRow(
        Guid OrderId,
        Guid StoreId,
        int CurrentVersion,
        Guid CurrentRevisionId,
        string MenuPublicationRevisionId,
        string State,
        string ServiceMode,
        string? ServiceReference,
        Guid CreatedByUserId,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc,
        Guid? SubmittedRevisionId,
        string? SubmittedRecipeBookRevisionId,
        Guid? CompletedSaleId,
        string? CancellationReason);

    private sealed record OperationRow(
        Guid OrderId,
        string OperationType,
        string PayloadHash,
        int ExpectedVersion,
        int ResultingVersion,
        Guid? ResultRevisionId,
        string ResultState,
        Guid? ResultSaleId);

    private sealed record RawLine(
        Guid OrderLineId,
        Guid ProductId,
        string ProductNameSnapshot,
        string UnitNameSnapshot,
        long QuantityX1000,
        long BaseUnitPriceCents,
        string? VariantId,
        string? VariantNameSnapshot,
        long? VariantUnitPriceCents,
        string? Note,
        long CalculatedUnitPriceCents,
        long LineTotalCents,
        int DisplayOrder);

    private sealed record MenuItemRow(
        Guid Id,
        string Name,
        string UnitName,
        long SellingPriceCents,
        bool IsUnavailable);

    private sealed record MenuVariantRow(
        string Id,
        Guid ProductId,
        string Name,
        long SellingPriceCents,
        bool IsUnavailable);

    private sealed record MenuGroupRow(
        string Id,
        string Name,
        bool IsRequired,
        int MinSelections,
        int MaxSelections,
        int DisplayOrder);

    private sealed record MenuOptionRow(
        string Id,
        string GroupId,
        string Name,
        long PriceDeltaCents,
        int DisplayOrder);

    private sealed record MenuAuthority(
        IReadOnlyDictionary<Guid, MenuItemRow> Items,
        IReadOnlyDictionary<string, MenuVariantRow> Variants,
        IReadOnlyDictionary<string, MenuGroupRow> Groups,
        IReadOnlyDictionary<string, MenuOptionRow> Options,
        IReadOnlyDictionary<string, HashSet<string>> ItemGroupLinks,
        IReadOnlyDictionary<string, HashSet<string>> VariantGroupLinks);

    private sealed class CafeOrderValidationException : Exception
    {
        public CafeOrderValidationException(string code, string message)
            : base(message)
        {
            Code = code;
        }

        public string Code { get; }
    }
}
