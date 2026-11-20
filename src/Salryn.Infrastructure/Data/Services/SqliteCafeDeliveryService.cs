using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Data.Sync;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Services;

public sealed class SqliteCafeDeliveryService :
    SqliteRepositoryBase,
    ICafeDeliveryService
{
    private readonly SqliteSyncOutboxStore _outbox;

    public SqliteCafeDeliveryService(
        SqliteConnectionFactory connectionFactory)
        : base(
            connectionFactory ??
            throw new ArgumentNullException(nameof(connectionFactory)))
    {
        _outbox = new SqliteSyncOutboxStore(connectionFactory);
    }

    public Result<CafeDeliveryMutationResult> Create(
        CafeDeliveryCreateMutation mutation)
    {
        string? validation = ValidateCreate(mutation);
        if (validation is not null)
        {
            return Result<CafeDeliveryMutationResult>.Fail(
                Error.Validation(
                    "CafeDelivery.CreateInvalid",
                    validation));
        }

        string recipient = mutation.RecipientName.Trim();
        string address = mutation.DeliveryAddress.Trim();
        string? phone = NormalizeOptional(mutation.ContactPhone);
        string? landmark = NormalizeOptional(mutation.Landmark);
        string? instructions = NormalizeOptional(mutation.Instructions);
        string? courier = NormalizeOptional(mutation.CourierReference);
        string payloadHash = HashCreate(
            mutation,
            recipient,
            phone,
            address,
            landmark,
            instructions,
            courier);

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Result authority = ValidateAuthority(
                connection,
                transaction,
                mutation.StoreId,
                mutation.ActorUserId,
                SalrynPermissions.DeliveryManage);
            if (authority.IsFailure)
            {
                transaction.Rollback();
                return Result<CafeDeliveryMutationResult>.Fail(
                    authority.ErrorDetail);
            }

            Result<CafeDeliveryMutationResult>? replay =
                TryReplay(
                    connection,
                    transaction,
                    mutation.OperationId,
                    mutation.DeliveryId,
                    mutation.DeviceId,
                    mutation.StoreId,
                    payloadHash);
            if (replay is not null)
            {
                transaction.Commit();
                return replay;
            }

            OrderAuthority order = ReadOrder(
                connection,
                transaction,
                mutation.OrderId)
                ?? throw new DeliveryValidationException(
                    "CafeDelivery.OrderMissing",
                    "Delivery requires an existing C4 order.");

            if (order.StoreId != mutation.StoreId ||
                !string.Equals(
                    order.ServiceMode,
                    CafeOrderServiceModes.Takeout,
                    StringComparison.Ordinal))
            {
                throw new DeliveryValidationException(
                    "CafeDelivery.OrderAuthorityInvalid",
                    "Delivery requires a same-store C4 Takeout order.");
            }

            if (string.Equals(
                    order.State,
                    CafeOrderStates.Completed,
                    StringComparison.Ordinal) ||
                string.Equals(
                    order.State,
                    CafeOrderStates.Cancelled,
                    StringComparison.Ordinal))
            {
                throw new DeliveryValidationException(
                    "CafeDelivery.OrderTerminal",
                    "Delivery must be configured before the C4 order becomes terminal.");
            }

            if (DeliveryIdExists(
                    connection,
                    transaction,
                    mutation.DeliveryId) ||
                OrderHasDelivery(
                    connection,
                    transaction,
                    mutation.OrderId))
            {
                throw new DeliveryValidationException(
                    "CafeDelivery.DuplicateDelivery",
                    "One C8 delivery fulfillment is allowed per C4 order.");
            }

            Guid revisionId = DeterministicGuid(
                "salryn-c8-delivery-revision-v1",
                mutation.OperationId.ToString("D"));

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO cafe_delivery_fulfillments(
                        delivery_id,order_id,store_id,current_version,
                        current_revision_id,status,created_by_user_id,
                        created_at_utc,updated_at_utc,terminal_reason)
                    VALUES(
                        $delivery,$order,$store,1,
                        $revision,'Pending',$actor,
                        $at,$at,NULL);
                    """;
                command.Parameters.AddWithValue(
                    "$delivery",
                    mutation.DeliveryId.ToString("D"));
                command.Parameters.AddWithValue(
                    "$order",
                    mutation.OrderId.ToString("D"));
                command.Parameters.AddWithValue(
                    "$store",
                    mutation.StoreId.ToString("D"));
                command.Parameters.AddWithValue(
                    "$revision",
                    revisionId.ToString("D"));
                command.Parameters.AddWithValue(
                    "$actor",
                    mutation.ActorUserId.ToString("D"));
                command.Parameters.AddWithValue(
                    "$at",
                    mutation.OccurredAtUtc
                        .ToUniversalTime()
                        .ToString("O"));
                command.ExecuteNonQuery();
            }

            InsertRevision(
                connection,
                transaction,
                revisionId,
                mutation.DeliveryId,
                mutation.OrderId,
                1,
                1,
                recipient,
                phone,
                address,
                landmark,
                instructions,
                mutation.DeliveryFeeCents,
                courier,
                payloadHash,
                mutation.ActorUserId,
                mutation.OccurredAtUtc);

            InsertOperation(
                connection,
                transaction,
                mutation.OperationId,
                mutation.DeliveryId,
                mutation.DeviceId,
                mutation.StoreId,
                "DeliveryCreatedV1",
                payloadHash,
                0,
                1,
                revisionId,
                CafeDeliveryStatuses.Pending,
                mutation.ActorUserId,
                mutation.OccurredAtUtc);

            InsertEvent(
                connection,
                transaction,
                mutation.OperationId,
                mutation.DeliveryId,
                mutation.OrderId,
                0,
                1,
                CafeDeliveryActions.Created,
                null,
                CafeDeliveryStatuses.Pending,
                mutation.ActorUserId,
                mutation.OccurredAtUtc,
                null,
                revisionId,
                null,
                payloadHash);

            CafeDeliveryOperationV1 syncPayload = new(
                mutation.OperationId,
                CafeDeliverySyncContract.DeliveryCreatedEvidenceV1,
                CafeDeliveryActions.Created,
                mutation.DeliveryId,
                mutation.OrderId,
                mutation.StoreId,
                0,
                1,
                CafeDeliveryActions.Created,
                null,
                CafeDeliveryStatuses.Pending,
                mutation.ActorUserId,
                mutation.OccurredAtUtc.ToUniversalTime(),
                revisionId,
                new CafeDeliveryRevisionEvidenceV1(
                    revisionId,
                    1,
                    1,
                    recipient,
                    phone,
                    address,
                    landmark,
                    instructions,
                    mutation.DeliveryFeeCents,
                    courier,
                    payloadHash,
                    mutation.ActorUserId,
                    mutation.OccurredAtUtc.ToUniversalTime()),
                null,
                null,
                payloadHash);

            string syncJson = CafeDeliveryOperationCanonicalJson.Serialize(
                CafeDeliverySyncContract.DeliveryConfiguredV1,
                syncPayload);

            _outbox.Enqueue(
                connection,
                transaction,
                new SyncEnqueueRequest(
                    mutation.DeviceId,
                    mutation.StoreId,
                    mutation.ActorUserId,
                    CafeDeliverySyncContract.DeliveryConfiguredV1,
                    CafeDeliverySyncContract.SchemaVersion,
                    mutation.DeliveryId,
                    0,
                    mutation.OccurredAtUtc,
                    syncJson,
                    mutation.OperationId));

            transaction.Commit();

            return Result<CafeDeliveryMutationResult>.Success(
                new CafeDeliveryMutationResult(
                    mutation.DeliveryId,
                    mutation.OrderId,
                    1,
                    revisionId,
                    CafeDeliveryStatuses.Pending,
                    false));
        }
        catch (DeliveryValidationException ex)
        {
            return Result<CafeDeliveryMutationResult>.Fail(
                Error.Conflict(ex.Code, ex.Message));
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return Result<CafeDeliveryMutationResult>.Fail(
                Error.Conflict(
                    "CafeDelivery.ConstraintConflict",
                    ex.Message));
        }
        catch (Exception ex) when (
            ex is SqliteException or
            InvalidOperationException or
            FormatException or
            OverflowException or
            ArgumentException)
        {
            return Result<CafeDeliveryMutationResult>.Fail(
                Error.Unexpected(
                    "CafeDelivery.CreateFailed",
                    ex.Message));
        }
    }

    public Result<CafeDeliveryMutationResult> Revise(
        CafeDeliveryReviseMutation mutation)
    {
        string? validation = ValidateRevise(mutation);
        if (validation is not null)
        {
            return Result<CafeDeliveryMutationResult>.Fail(
                Error.Validation(
                    "CafeDelivery.ReviseInvalid",
                    validation));
        }

        string recipient = mutation.RecipientName.Trim();
        string address = mutation.DeliveryAddress.Trim();
        string? phone = NormalizeOptional(mutation.ContactPhone);
        string? landmark = NormalizeOptional(mutation.Landmark);
        string? instructions = NormalizeOptional(mutation.Instructions);
        string? courier = NormalizeOptional(mutation.CourierReference);
        string payloadHash = HashRevise(
            mutation,
            recipient,
            phone,
            address,
            landmark,
            instructions,
            courier);

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Result authority = ValidateAuthority(
                connection,
                transaction,
                mutation.StoreId,
                mutation.ActorUserId,
                SalrynPermissions.DeliveryManage);
            if (authority.IsFailure)
            {
                transaction.Rollback();
                return Result<CafeDeliveryMutationResult>.Fail(
                    authority.ErrorDetail);
            }

            Result<CafeDeliveryMutationResult>? replay =
                TryReplay(
                    connection,
                    transaction,
                    mutation.OperationId,
                    mutation.DeliveryId,
                    mutation.DeviceId,
                    mutation.StoreId,
                    payloadHash);
            if (replay is not null)
            {
                transaction.Commit();
                return replay;
            }

            DeliveryRow delivery = ReadDelivery(
                connection,
                transaction,
                mutation.DeliveryId)
                ?? throw new DeliveryValidationException(
                    "CafeDelivery.Missing",
                    "Delivery fulfillment was not found.");

            if (delivery.StoreId != mutation.StoreId ||
                delivery.Version != mutation.ExpectedVersion)
            {
                throw new DeliveryValidationException(
                    "CafeDelivery.ExpectedVersionConflict",
                    "Delivery version/store authority changed; last-write-wins is not allowed.");
            }

            if (!string.Equals(
                    delivery.Status,
                    CafeDeliveryStatuses.Pending,
                    StringComparison.Ordinal))
            {
                throw new DeliveryValidationException(
                    "CafeDelivery.RevisionNotPending",
                    "Delivery details may be revised only while Pending.");
            }

            int resultingVersion = checked(mutation.ExpectedVersion + 1);
            int revisionNumber = checked(
                ReadCurrentRevisionNumber(
                    connection,
                    transaction,
                    delivery.CurrentRevisionId) + 1);
            Guid revisionId = DeterministicGuid(
                "salryn-c8-delivery-revision-v1",
                mutation.OperationId.ToString("D"));

            InsertRevision(
                connection,
                transaction,
                revisionId,
                mutation.DeliveryId,
                delivery.OrderId,
                revisionNumber,
                resultingVersion,
                recipient,
                phone,
                address,
                landmark,
                instructions,
                mutation.DeliveryFeeCents,
                courier,
                payloadHash,
                mutation.ActorUserId,
                mutation.OccurredAtUtc);

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE cafe_delivery_fulfillments
                    SET current_version=$version,
                        current_revision_id=$revision,
                        updated_at_utc=$at
                    WHERE delivery_id=$delivery
                      AND current_version=$expected
                      AND status='Pending';
                    """;
                command.Parameters.AddWithValue(
                    "$version",
                    resultingVersion);
                command.Parameters.AddWithValue(
                    "$revision",
                    revisionId.ToString("D"));
                command.Parameters.AddWithValue(
                    "$at",
                    mutation.OccurredAtUtc
                        .ToUniversalTime()
                        .ToString("O"));
                command.Parameters.AddWithValue(
                    "$delivery",
                    mutation.DeliveryId.ToString("D"));
                command.Parameters.AddWithValue(
                    "$expected",
                    mutation.ExpectedVersion);

                if (command.ExecuteNonQuery() != 1)
                {
                    throw new DeliveryValidationException(
                        "CafeDelivery.ConcurrentRevisionConflict",
                        "Delivery lost Pending revision ownership.");
                }
            }

            InsertOperation(
                connection,
                transaction,
                mutation.OperationId,
                mutation.DeliveryId,
                mutation.DeviceId,
                mutation.StoreId,
                "DeliveryRevisedV1",
                payloadHash,
                mutation.ExpectedVersion,
                resultingVersion,
                revisionId,
                CafeDeliveryStatuses.Pending,
                mutation.ActorUserId,
                mutation.OccurredAtUtc);

            InsertEvent(
                connection,
                transaction,
                mutation.OperationId,
                mutation.DeliveryId,
                delivery.OrderId,
                mutation.ExpectedVersion,
                resultingVersion,
                CafeDeliveryActions.Revised,
                CafeDeliveryStatuses.Pending,
                CafeDeliveryStatuses.Pending,
                mutation.ActorUserId,
                mutation.OccurredAtUtc,
                null,
                revisionId,
                null,
                payloadHash);

            CafeDeliveryOperationV1 syncPayload = new(
                mutation.OperationId,
                CafeDeliverySyncContract.DeliveryRevisedEvidenceV1,
                CafeDeliveryActions.Revised,
                mutation.DeliveryId,
                delivery.OrderId,
                mutation.StoreId,
                mutation.ExpectedVersion,
                resultingVersion,
                CafeDeliveryActions.Revised,
                CafeDeliveryStatuses.Pending,
                CafeDeliveryStatuses.Pending,
                mutation.ActorUserId,
                mutation.OccurredAtUtc.ToUniversalTime(),
                revisionId,
                new CafeDeliveryRevisionEvidenceV1(
                    revisionId,
                    revisionNumber,
                    resultingVersion,
                    recipient,
                    phone,
                    address,
                    landmark,
                    instructions,
                    mutation.DeliveryFeeCents,
                    courier,
                    payloadHash,
                    mutation.ActorUserId,
                    mutation.OccurredAtUtc.ToUniversalTime()),
                null,
                null,
                payloadHash);

            string syncJson = CafeDeliveryOperationCanonicalJson.Serialize(
                CafeDeliverySyncContract.DeliveryConfiguredV1,
                syncPayload);

            _outbox.Enqueue(
                connection,
                transaction,
                new SyncEnqueueRequest(
                    mutation.DeviceId,
                    mutation.StoreId,
                    mutation.ActorUserId,
                    CafeDeliverySyncContract.DeliveryConfiguredV1,
                    CafeDeliverySyncContract.SchemaVersion,
                    mutation.DeliveryId,
                    mutation.ExpectedVersion,
                    mutation.OccurredAtUtc,
                    syncJson,
                    mutation.OperationId));

            transaction.Commit();

            return Result<CafeDeliveryMutationResult>.Success(
                new CafeDeliveryMutationResult(
                    mutation.DeliveryId,
                    delivery.OrderId,
                    resultingVersion,
                    revisionId,
                    CafeDeliveryStatuses.Pending,
                    false));
        }
        catch (DeliveryValidationException ex)
        {
            return Result<CafeDeliveryMutationResult>.Fail(
                Error.Conflict(ex.Code, ex.Message));
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return Result<CafeDeliveryMutationResult>.Fail(
                Error.Conflict(
                    "CafeDelivery.ConstraintConflict",
                    ex.Message));
        }
        catch (Exception ex) when (
            ex is SqliteException or
            InvalidOperationException or
            FormatException or
            OverflowException or
            ArgumentException)
        {
            return Result<CafeDeliveryMutationResult>.Fail(
                Error.Unexpected(
                    "CafeDelivery.ReviseFailed",
                    ex.Message));
        }
    }

    public Result<CafeDeliveryMutationResult> Advance(
        CafeDeliveryAdvanceMutation mutation)
    {
        string? validation = ValidateAdvance(mutation);
        if (validation is not null)
        {
            return Result<CafeDeliveryMutationResult>.Fail(
                Error.Validation(
                    "CafeDelivery.AdvanceInvalid",
                    validation));
        }

        string? reason = NormalizeOptional(mutation.Reason);
        string payloadHash = HashAdvance(mutation, reason);

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            bool cancellation = string.Equals(
                mutation.TargetStatus,
                CafeDeliveryStatuses.Cancelled,
                StringComparison.Ordinal);

            Result authority = ValidateAuthority(
                connection,
                transaction,
                mutation.StoreId,
                mutation.ActorUserId,
                cancellation
                    ? SalrynPermissions.DeliveryManage
                    : SalrynPermissions.DeliveryAdvance);
            if (authority.IsFailure)
            {
                transaction.Rollback();
                return Result<CafeDeliveryMutationResult>.Fail(
                    authority.ErrorDetail);
            }

            Result<CafeDeliveryMutationResult>? replay =
                TryReplay(
                    connection,
                    transaction,
                    mutation.OperationId,
                    mutation.DeliveryId,
                    mutation.DeviceId,
                    mutation.StoreId,
                    payloadHash);
            if (replay is not null)
            {
                transaction.Commit();
                return replay;
            }

            DeliveryRow delivery = ReadDelivery(
                connection,
                transaction,
                mutation.DeliveryId)
                ?? throw new DeliveryValidationException(
                    "CafeDelivery.Missing",
                    "Delivery fulfillment was not found.");

            if (delivery.StoreId != mutation.StoreId ||
                delivery.Version != mutation.ExpectedVersion)
            {
                throw new DeliveryValidationException(
                    "CafeDelivery.ExpectedVersionConflict",
                    "Delivery version/store authority changed; last-write-wins is not allowed.");
            }

            ValidateTransition(delivery.Status, mutation.TargetStatus, reason);

            Guid? saleId = null;
            if (string.Equals(
                    mutation.TargetStatus,
                    CafeDeliveryStatuses.Dispatched,
                    StringComparison.Ordinal) ||
                string.Equals(
                    delivery.Status,
                    CafeDeliveryStatuses.Dispatched,
                    StringComparison.Ordinal))
            {
                saleId = ReadCompletedSale(
                    connection,
                    transaction,
                    delivery.OrderId)
                    ?? throw new DeliveryValidationException(
                        "CafeDelivery.CompletedSaleRequired",
                        "Dispatch/resolution requires the C4 order to be completed into one accepted Sale.");
            }

            int resultingVersion = checked(mutation.ExpectedVersion + 1);
            string? terminalReason =
                string.Equals(
                    mutation.TargetStatus,
                    CafeDeliveryStatuses.Failed,
                    StringComparison.Ordinal) ||
                string.Equals(
                    mutation.TargetStatus,
                    CafeDeliveryStatuses.Cancelled,
                    StringComparison.Ordinal)
                    ? reason
                    : null;

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    UPDATE cafe_delivery_fulfillments
                    SET current_version=$version,
                        status=$status,
                        updated_at_utc=$at,
                        terminal_reason=$reason
                    WHERE delivery_id=$delivery
                      AND current_version=$expected
                      AND status=$fromStatus;
                    """;
                command.Parameters.AddWithValue(
                    "$version",
                    resultingVersion);
                command.Parameters.AddWithValue(
                    "$status",
                    mutation.TargetStatus);
                command.Parameters.AddWithValue(
                    "$at",
                    mutation.OccurredAtUtc
                        .ToUniversalTime()
                        .ToString("O"));
                command.Parameters.AddWithValue(
                    "$reason",
                    terminalReason is null
                        ? DBNull.Value
                        : terminalReason);
                command.Parameters.AddWithValue(
                    "$delivery",
                    mutation.DeliveryId.ToString("D"));
                command.Parameters.AddWithValue(
                    "$expected",
                    mutation.ExpectedVersion);
                command.Parameters.AddWithValue(
                    "$fromStatus",
                    delivery.Status);

                if (command.ExecuteNonQuery() != 1)
                {
                    throw new DeliveryValidationException(
                        "CafeDelivery.ConcurrentTransitionConflict",
                        "Delivery lost lifecycle transition ownership.");
                }
            }

            string action = ResolveAction(mutation.TargetStatus);
            string operationType = ResolveOperationType(
                mutation.TargetStatus);

            InsertOperation(
                connection,
                transaction,
                mutation.OperationId,
                mutation.DeliveryId,
                mutation.DeviceId,
                mutation.StoreId,
                operationType,
                payloadHash,
                mutation.ExpectedVersion,
                resultingVersion,
                delivery.CurrentRevisionId,
                mutation.TargetStatus,
                mutation.ActorUserId,
                mutation.OccurredAtUtc);

            InsertEvent(
                connection,
                transaction,
                mutation.OperationId,
                mutation.DeliveryId,
                delivery.OrderId,
                mutation.ExpectedVersion,
                resultingVersion,
                action,
                delivery.Status,
                mutation.TargetStatus,
                mutation.ActorUserId,
                mutation.OccurredAtUtc,
                reason,
                delivery.CurrentRevisionId,
                saleId,
                payloadHash);

            string syncOperationType =
                string.Equals(
                    mutation.TargetStatus,
                    CafeDeliveryStatuses.Dispatched,
                    StringComparison.Ordinal)
                    ? CafeDeliverySyncContract.DeliveryDispatchedV1
                    : string.Equals(
                        mutation.TargetStatus,
                        CafeDeliveryStatuses.Cancelled,
                        StringComparison.Ordinal)
                        ? CafeDeliverySyncContract.DeliveryCancelledV1
                        : CafeDeliverySyncContract.DeliveryResolvedV1;

            CafeDeliveryOperationV1 syncPayload = new(
                mutation.OperationId,
                operationType,
                null,
                mutation.DeliveryId,
                delivery.OrderId,
                mutation.StoreId,
                mutation.ExpectedVersion,
                resultingVersion,
                action,
                delivery.Status,
                mutation.TargetStatus,
                mutation.ActorUserId,
                mutation.OccurredAtUtc.ToUniversalTime(),
                delivery.CurrentRevisionId,
                null,
                saleId,
                reason,
                payloadHash);

            string syncJson = CafeDeliveryOperationCanonicalJson.Serialize(
                syncOperationType,
                syncPayload);

            _outbox.Enqueue(
                connection,
                transaction,
                new SyncEnqueueRequest(
                    mutation.DeviceId,
                    mutation.StoreId,
                    mutation.ActorUserId,
                    syncOperationType,
                    CafeDeliverySyncContract.SchemaVersion,
                    mutation.DeliveryId,
                    mutation.ExpectedVersion,
                    mutation.OccurredAtUtc,
                    syncJson,
                    mutation.OperationId));

            transaction.Commit();

            return Result<CafeDeliveryMutationResult>.Success(
                new CafeDeliveryMutationResult(
                    mutation.DeliveryId,
                    delivery.OrderId,
                    resultingVersion,
                    delivery.CurrentRevisionId,
                    mutation.TargetStatus,
                    false));
        }
        catch (DeliveryValidationException ex)
        {
            return Result<CafeDeliveryMutationResult>.Fail(
                Error.Conflict(ex.Code, ex.Message));
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return Result<CafeDeliveryMutationResult>.Fail(
                Error.Conflict(
                    "CafeDelivery.ConstraintConflict",
                    ex.Message));
        }
        catch (Exception ex) when (
            ex is SqliteException or
            InvalidOperationException or
            FormatException or
            OverflowException or
            ArgumentException)
        {
            return Result<CafeDeliveryMutationResult>.Fail(
                Error.Unexpected(
                    "CafeDelivery.AdvanceFailed",
                    ex.Message));
        }
    }

    public Result<IReadOnlyList<CafeDeliverySummary>> List(
        Guid storeId,
        Guid actorUserId,
        string? status = null)
    {
        if (storeId == Guid.Empty ||
            actorUserId == Guid.Empty)
        {
            return Result<IReadOnlyList<CafeDeliverySummary>>.Fail(
                Error.Validation(
                    "CafeDelivery.ListIdentityInvalid",
                    "Store and actor identity are required."));
        }

        string? normalizedStatus =
            string.IsNullOrWhiteSpace(status)
                ? null
                : status.Trim();

        if (normalizedStatus is not null &&
            !CafeDeliveryStatuses.IsKnown(normalizedStatus))
        {
            return Result<IReadOnlyList<CafeDeliverySummary>>.Fail(
                Error.Validation(
                    "CafeDelivery.StatusInvalid",
                    "Delivery status filter is invalid."));
        }

        try
        {
            using SqliteConnection connection =
                CreateOpenConnection();
            using SqliteTransaction transaction =
                connection.BeginTransaction();

            Result authority = ValidateAuthority(
                connection,
                transaction,
                storeId,
                actorUserId,
                SalrynPermissions.DeliveryView);

            if (authority.IsFailure)
            {
                transaction.Rollback();

                return Result<IReadOnlyList<CafeDeliverySummary>>.Fail(
                    authority.ErrorDetail);
            }

            using SqliteCommand command =
                connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                SELECT
                    delivery_id,
                    order_id,
                    store_id,
                    current_version,
                    current_revision_id,
                    status,
                    created_by_user_id,
                    created_at_utc,
                    updated_at_utc,
                    terminal_reason
                FROM cafe_delivery_fulfillments
                WHERE store_id=$store
                  AND ($status IS NULL OR status=$status)
                ORDER BY
                    CASE
                        WHEN status IN ('Pending','Dispatched')
                            THEN 0
                        ELSE 1
                    END,
                    updated_at_utc DESC,
                    delivery_id ASC;
                """;
            command.Parameters.AddWithValue(
                "$store",
                storeId.ToString("D"));
            command.Parameters.AddWithValue(
                "$status",
                normalizedStatus is null
                    ? DBNull.Value
                    : normalizedStatus);

            List<CafeDeliverySummary> deliveries = new();

            using SqliteDataReader reader =
                command.ExecuteReader();

            while (reader.Read())
            {
                deliveries.Add(
                    new CafeDeliverySummary(
                        Guid.Parse(reader.GetString(0)),
                        Guid.Parse(reader.GetString(1)),
                        Guid.Parse(reader.GetString(2)),
                        reader.GetInt32(3),
                        Guid.Parse(reader.GetString(4)),
                        reader.GetString(5),
                        Guid.Parse(reader.GetString(6)),
                        ParseDate(reader.GetString(7)),
                        ParseDate(reader.GetString(8)),
                        reader.IsDBNull(9)
                            ? null
                            : reader.GetString(9)));
            }

            transaction.Commit();

            return Result<IReadOnlyList<CafeDeliverySummary>>.Success(
                deliveries);
        }
        catch (Exception ex) when (
            ex is SqliteException or
            InvalidOperationException or
            FormatException)
        {
            return Result<IReadOnlyList<CafeDeliverySummary>>.Fail(
                Error.Unexpected(
                    "CafeDelivery.ListFailed",
                    ex.Message));
        }
    }

    public Result<CafeDeliveryDetail?> Get(
        Guid storeId,
        Guid actorUserId,
        Guid deliveryId) =>
        ReadAuthorized(
            storeId,
            actorUserId,
            deliveryId,
            byOrder: false);

    public Result<CafeDeliveryDetail?> GetByOrder(
        Guid storeId,
        Guid actorUserId,
        Guid orderId) =>
        ReadAuthorized(
            storeId,
            actorUserId,
            orderId,
            byOrder: true);

    private Result<CafeDeliveryDetail?> ReadAuthorized(
        Guid storeId,
        Guid actorUserId,
        Guid id,
        bool byOrder)
    {
        if (storeId == Guid.Empty ||
            actorUserId == Guid.Empty ||
            id == Guid.Empty)
        {
            return Result<CafeDeliveryDetail?>.Fail(
                Error.Validation(
                    "CafeDelivery.ReadIdentityInvalid",
                    "Store, actor and delivery/order identity are required."));
        }

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Result authority = ValidateAuthority(
                connection,
                transaction,
                storeId,
                actorUserId,
                SalrynPermissions.DeliveryView);
            if (authority.IsFailure)
            {
                transaction.Rollback();
                return Result<CafeDeliveryDetail?>.Fail(
                    authority.ErrorDetail);
            }

            CafeDeliveryDetail? detail = ReadDetail(
                connection,
                transaction,
                storeId,
                id,
                byOrder);

            transaction.Commit();
            return Result<CafeDeliveryDetail?>.Success(detail);
        }
        catch (Exception ex) when (
            ex is SqliteException or
            InvalidOperationException or
            FormatException)
        {
            return Result<CafeDeliveryDetail?>.Fail(
                Error.Unexpected(
                    "CafeDelivery.ReadFailed",
                    ex.Message));
        }
    }

    private static Result<CafeDeliveryMutationResult>? TryReplay(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        Guid deliveryId,
        Guid deviceId,
        Guid storeId,
        string payloadHash)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT o.delivery_id,o.device_id,o.store_id,
                   o.operation_type,o.payload_hash,
                   o.expected_version,o.resulting_version,
                   o.result_revision_id,o.result_status,
                   f.order_id
            FROM cafe_delivery_operations o
            INNER JOIN cafe_delivery_fulfillments f
              ON f.delivery_id=o.delivery_id
            WHERE o.operation_id=$operation
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "$operation",
            operationId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        Guid retainedDeliveryId = Guid.Parse(reader.GetString(0));
        Guid retainedDeviceId = Guid.Parse(reader.GetString(1));
        Guid retainedStoreId = Guid.Parse(reader.GetString(2));
        string retainedOperationType = reader.GetString(3);
        string retainedHash = reader.GetString(4);
        int expectedVersion = reader.GetInt32(5);
        int resultingVersion = reader.GetInt32(6);
        Guid revisionId = Guid.Parse(reader.GetString(7));
        string resultStatus = reader.GetString(8);
        Guid orderId = Guid.Parse(reader.GetString(9));
        reader.Close();

        if (retainedDeliveryId != deliveryId ||
            retainedDeviceId != deviceId ||
            retainedStoreId != storeId ||
            !string.Equals(
                retainedHash,
                payloadHash,
                StringComparison.Ordinal))
        {
            return Result<CafeDeliveryMutationResult>.Fail(
                Error.Conflict(
                    "CafeDelivery.OperationCollision",
                    "OperationId already exists with different canonical delivery evidence."));
        }

        Result outboxIntegrity = VerifyRetainedSemanticOutbox(
            connection,
            transaction,
            operationId,
            retainedDeliveryId,
            orderId,
            retainedDeviceId,
            retainedStoreId,
            retainedOperationType,
            retainedHash,
            expectedVersion,
            resultingVersion,
            revisionId,
            resultStatus);
        if (outboxIntegrity.IsFailure)
        {
            return Result<CafeDeliveryMutationResult>.Fail(
                outboxIntegrity.ErrorDetail);
        }

        return Result<CafeDeliveryMutationResult>.Success(
            new CafeDeliveryMutationResult(
                retainedDeliveryId,
                orderId,
                resultingVersion,
                revisionId,
                resultStatus,
                true));
    }

    private static Result VerifyRetainedSemanticOutbox(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        Guid deliveryId,
        Guid orderId,
        Guid deviceId,
        Guid storeId,
        string localOperationType,
        string evidencePayloadHash,
        int expectedVersion,
        int resultingVersion,
        Guid currentRevisionId,
        string resultingStatus)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT device_id,store_id,operation_type,schema_version,
                   aggregate_id,base_version,payload_json
            FROM sync_outbox
            WHERE operation_id=$operation
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "$operation",
            operationId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return Result.Fail(
                Error.Conflict(
                    "CafeDelivery.SyncOutboxMissing",
                    "Retained C8 operation is missing its atomic semantic outbox evidence."));
        }

        Guid outboxDeviceId = Guid.Parse(reader.GetString(0));
        Guid outboxStoreId = Guid.Parse(reader.GetString(1));
        string semanticOperationType = reader.GetString(2);
        int schemaVersion = reader.GetInt32(3);
        Guid aggregateId = Guid.Parse(reader.GetString(4));
        long? baseVersion = reader.IsDBNull(5)
            ? null
            : reader.GetInt64(5);
        string payloadJson = reader.GetString(6);
        reader.Close();

        CafeDeliveryCanonicalParseResult parsed =
            CafeDeliveryOperationCanonicalJson.Parse(
                semanticOperationType,
                payloadJson);
        if (!parsed.IsValid || parsed.Payload is null)
        {
            return Result.Fail(
                Error.Conflict(
                    "CafeDelivery.SyncOutboxCorrupt",
                    parsed.ErrorMessage ??
                    "Retained C8 semantic outbox payload is invalid."));
        }

        CafeDeliveryOperationV1 payload = parsed.Payload;
        bool matches =
            outboxDeviceId == deviceId &&
            outboxStoreId == storeId &&
            schemaVersion == CafeDeliverySyncContract.SchemaVersion &&
            aggregateId == deliveryId &&
            baseVersion == expectedVersion &&
            payload.OperationId == operationId &&
            payload.DeliveryId == deliveryId &&
            payload.OrderId == orderId &&
            payload.StoreId == storeId &&
            payload.ExpectedVersion == expectedVersion &&
            payload.ResultingVersion == resultingVersion &&
            payload.CurrentRevisionId == currentRevisionId &&
            string.Equals(
                payload.ToStatus,
                resultingStatus,
                StringComparison.Ordinal) &&
            string.Equals(
                payload.EvidenceOperationType,
                localOperationType,
                StringComparison.Ordinal) &&
            string.Equals(
                payload.EvidencePayloadHash,
                evidencePayloadHash,
                StringComparison.Ordinal);

        return matches
            ? Result.Success()
            : Result.Fail(
                Error.Conflict(
                    "CafeDelivery.SyncOutboxMismatch",
                    "Retained C8 semantic outbox conflicts with immutable local delivery evidence."));
    }

    private static CafeDeliveryDetail? ReadDetail(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId,
        Guid id,
        bool byOrder)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            SELECT f.delivery_id,f.order_id,f.store_id,
                   f.current_version,f.current_revision_id,
                   f.status,f.created_by_user_id,
                   f.created_at_utc,f.updated_at_utc,
                   f.terminal_reason,
                   r.revision_id,r.revision_number,
                   r.delivery_version,r.recipient_name,
                   r.contact_phone,r.delivery_address,
                   r.landmark,r.instructions,
                   r.delivery_fee_cents,r.courier_reference,
                   r.content_hash,r.actor_user_id,
                   r.created_at_utc
            FROM cafe_delivery_fulfillments f
            INNER JOIN cafe_delivery_revisions r
              ON r.revision_id=f.current_revision_id
            WHERE f.store_id=$store
              AND {(byOrder ? "f.order_id" : "f.delivery_id")}=$id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "$store",
            storeId.ToString("D"));
        command.Parameters.AddWithValue(
            "$id",
            id.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        CafeDeliverySummary summary = new(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            Guid.Parse(reader.GetString(2)),
            reader.GetInt32(3),
            Guid.Parse(reader.GetString(4)),
            reader.GetString(5),
            Guid.Parse(reader.GetString(6)),
            ParseDate(reader.GetString(7)),
            ParseDate(reader.GetString(8)),
            reader.IsDBNull(9) ? null : reader.GetString(9));

        CafeDeliveryRevision revision = new(
            Guid.Parse(reader.GetString(10)),
            summary.DeliveryId,
            summary.OrderId,
            reader.GetInt32(11),
            reader.GetInt32(12),
            reader.GetString(13),
            reader.IsDBNull(14) ? null : reader.GetString(14),
            reader.GetString(15),
            reader.IsDBNull(16) ? null : reader.GetString(16),
            reader.IsDBNull(17) ? null : reader.GetString(17),
            reader.GetInt64(18),
            reader.IsDBNull(19) ? null : reader.GetString(19),
            reader.GetString(20),
            Guid.Parse(reader.GetString(21)),
            ParseDate(reader.GetString(22)));

        return new CafeDeliveryDetail(summary, revision);
    }

    private static DeliveryRow? ReadDelivery(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid deliveryId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT delivery_id,order_id,store_id,current_version,
                   current_revision_id,status
            FROM cafe_delivery_fulfillments
            WHERE delivery_id=$delivery
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "$delivery",
            deliveryId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        return new DeliveryRow(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            Guid.Parse(reader.GetString(2)),
            reader.GetInt32(3),
            Guid.Parse(reader.GetString(4)),
            reader.GetString(5));
    }

    private static OrderAuthority? ReadOrder(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid orderId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT store_id,service_mode,state,completed_sale_id
            FROM cafe_orders
            WHERE order_id=$order
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "$order",
            orderId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        return new OrderAuthority(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3)
                ? null
                : Guid.Parse(reader.GetString(3)));
    }

    private static Guid? ReadCompletedSale(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid orderId)
    {
        OrderAuthority? order =
            ReadOrder(connection, transaction, orderId);

        return order is not null &&
               string.Equals(
                   order.State,
                   CafeOrderStates.Completed,
                   StringComparison.Ordinal)
            ? order.CompletedSaleId
            : null;
    }

    private static int ReadCurrentRevisionNumber(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid revisionId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT revision_number
            FROM cafe_delivery_revisions
            WHERE revision_id=$revision;
            """;
        command.Parameters.AddWithValue(
            "$revision",
            revisionId.ToString("D"));
        return Convert.ToInt32(
            command.ExecuteScalar() ??
            throw new InvalidOperationException(
                "Current delivery revision is missing."));
    }

    private static bool DeliveryIdExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid deliveryId) =>
        Exists(
            connection,
            transaction,
            "SELECT COUNT(*) FROM cafe_delivery_fulfillments WHERE delivery_id=$id;",
            deliveryId);

    private static bool OrderHasDelivery(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid orderId) =>
        Exists(
            connection,
            transaction,
            "SELECT COUNT(*) FROM cafe_delivery_fulfillments WHERE order_id=$id;",
            orderId);

    private static bool Exists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        Guid id)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L) != 0;
    }

    private static void InsertRevision(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid revisionId,
        Guid deliveryId,
        Guid orderId,
        int revisionNumber,
        int deliveryVersion,
        string recipientName,
        string? phone,
        string address,
        string? landmark,
        string? instructions,
        long feeCents,
        string? courier,
        string contentHash,
        Guid actorUserId,
        DateTimeOffset occurredAtUtc)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO cafe_delivery_revisions(
                revision_id,delivery_id,order_id,revision_number,
                delivery_version,recipient_name,contact_phone,
                delivery_address,landmark,instructions,
                delivery_fee_cents,courier_reference,
                content_hash,actor_user_id,created_at_utc)
            VALUES(
                $revision,$delivery,$order,$number,
                $version,$recipient,$phone,
                $address,$landmark,$instructions,
                $fee,$courier,$hash,$actor,$at);
            """;
        command.Parameters.AddWithValue("$revision", revisionId.ToString("D"));
        command.Parameters.AddWithValue("$delivery", deliveryId.ToString("D"));
        command.Parameters.AddWithValue("$order", orderId.ToString("D"));
        command.Parameters.AddWithValue("$number", revisionNumber);
        command.Parameters.AddWithValue("$version", deliveryVersion);
        command.Parameters.AddWithValue("$recipient", recipientName);
        command.Parameters.AddWithValue("$phone", phone is null ? DBNull.Value : phone);
        command.Parameters.AddWithValue("$address", address);
        command.Parameters.AddWithValue("$landmark", landmark is null ? DBNull.Value : landmark);
        command.Parameters.AddWithValue("$instructions", instructions is null ? DBNull.Value : instructions);
        command.Parameters.AddWithValue("$fee", feeCents);
        command.Parameters.AddWithValue("$courier", courier is null ? DBNull.Value : courier);
        command.Parameters.AddWithValue("$hash", contentHash);
        command.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
        command.Parameters.AddWithValue(
            "$at",
            occurredAtUtc.ToUniversalTime().ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void InsertOperation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        Guid deliveryId,
        Guid deviceId,
        Guid storeId,
        string operationType,
        string payloadHash,
        int expectedVersion,
        int resultingVersion,
        Guid revisionId,
        string status,
        Guid actorUserId,
        DateTimeOffset occurredAtUtc)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO cafe_delivery_operations(
                operation_id,delivery_id,device_id,store_id,
                operation_type,payload_hash,
                expected_version,resulting_version,
                result_revision_id,result_status,
                actor_user_id,occurred_at_utc)
            VALUES(
                $operation,$delivery,$device,$store,
                $type,$hash,$expected,$resulting,
                $revision,$status,$actor,$at);
            """;
        command.Parameters.AddWithValue("$operation", operationId.ToString("D"));
        command.Parameters.AddWithValue("$delivery", deliveryId.ToString("D"));
        command.Parameters.AddWithValue("$device", deviceId.ToString("D"));
        command.Parameters.AddWithValue("$store", storeId.ToString("D"));
        command.Parameters.AddWithValue("$type", operationType);
        command.Parameters.AddWithValue("$hash", payloadHash);
        command.Parameters.AddWithValue("$expected", expectedVersion);
        command.Parameters.AddWithValue("$resulting", resultingVersion);
        command.Parameters.AddWithValue("$revision", revisionId.ToString("D"));
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
        command.Parameters.AddWithValue(
            "$at",
            occurredAtUtc.ToUniversalTime().ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void InsertEvent(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        Guid deliveryId,
        Guid orderId,
        int expectedVersion,
        int resultingVersion,
        string action,
        string? fromStatus,
        string toStatus,
        Guid actorUserId,
        DateTimeOffset occurredAtUtc,
        string? reason,
        Guid revisionId,
        Guid? saleId,
        string contentHash)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO cafe_delivery_events(
                event_id,operation_id,delivery_id,order_id,
                expected_version,resulting_version,action,
                from_status,to_status,actor_user_id,
                occurred_at_utc,reason,delivery_revision_id,
                sale_id,content_hash)
            VALUES(
                $event,$operation,$delivery,$order,
                $expected,$resulting,$action,
                $from,$to,$actor,$at,$reason,$revision,
                $sale,$hash);
            """;
        command.Parameters.AddWithValue(
            "$event",
            DeterministicGuid(
                "salryn-c8-delivery-event-v1",
                operationId.ToString("D")).ToString("D"));
        command.Parameters.AddWithValue("$operation", operationId.ToString("D"));
        command.Parameters.AddWithValue("$delivery", deliveryId.ToString("D"));
        command.Parameters.AddWithValue("$order", orderId.ToString("D"));
        command.Parameters.AddWithValue("$expected", expectedVersion);
        command.Parameters.AddWithValue("$resulting", resultingVersion);
        command.Parameters.AddWithValue("$action", action);
        command.Parameters.AddWithValue("$from", fromStatus is null ? DBNull.Value : fromStatus);
        command.Parameters.AddWithValue("$to", toStatus);
        command.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
        command.Parameters.AddWithValue("$at", occurredAtUtc.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$reason", reason is null ? DBNull.Value : reason);
        command.Parameters.AddWithValue("$revision", revisionId.ToString("D"));
        command.Parameters.AddWithValue("$sale", saleId.HasValue ? saleId.Value.ToString("D") : DBNull.Value);
        command.Parameters.AddWithValue("$hash", contentHash);
        command.ExecuteNonQuery();
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
                return Result.Fail(
                    Error.Conflict(
                        "CafeDelivery.StoreAuthorityMismatch",
                        "Delivery StoreId does not match local deployment authority."));
            }
        }

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM users u
            INNER JOIN role_permissions rp
              ON rp.role_id=u.role_id
            WHERE u.id=$user
              AND u.status=1
              AND rp.permission=$permission;
            """;
        command.Parameters.AddWithValue("$user", actorUserId.ToString("D"));
        command.Parameters.AddWithValue("$permission", permission);

        return Convert.ToInt64(command.ExecuteScalar() ?? 0L) == 1
            ? Result.Success()
            : Result.Fail(
                Error.Forbidden(
                    "CafeDelivery.PermissionRequired",
                    "Delivery operation requires permission: " + permission));
    }

    private static void ValidateTransition(
        string current,
        string target,
        string? reason)
    {
        bool valid =
            (current == CafeDeliveryStatuses.Pending &&
             (target == CafeDeliveryStatuses.Dispatched ||
              target == CafeDeliveryStatuses.Cancelled)) ||
            (current == CafeDeliveryStatuses.Dispatched &&
             (target == CafeDeliveryStatuses.Delivered ||
              target == CafeDeliveryStatuses.Failed));

        if (!valid)
        {
            throw new DeliveryValidationException(
                "CafeDelivery.InvalidTransition",
                "Delivery lifecycle transition is not allowed.");
        }

        if ((target == CafeDeliveryStatuses.Failed ||
             target == CafeDeliveryStatuses.Cancelled) &&
            string.IsNullOrWhiteSpace(reason))
        {
            throw new DeliveryValidationException(
                "CafeDelivery.ReasonRequired",
                "Failed/Cancelled delivery requires a reason.");
        }
    }

    private static string ResolveAction(string target) =>
        target switch
        {
            CafeDeliveryStatuses.Dispatched => CafeDeliveryActions.Dispatched,
            CafeDeliveryStatuses.Delivered => CafeDeliveryActions.Delivered,
            CafeDeliveryStatuses.Failed => CafeDeliveryActions.Failed,
            CafeDeliveryStatuses.Cancelled => CafeDeliveryActions.Cancelled,
            _ => throw new InvalidOperationException("Unsupported delivery action.")
        };

    private static string ResolveOperationType(string target) =>
        target switch
        {
            CafeDeliveryStatuses.Dispatched => "DeliveryDispatchedV1",
            CafeDeliveryStatuses.Delivered => "DeliveryResolvedV1",
            CafeDeliveryStatuses.Failed => "DeliveryResolvedV1",
            CafeDeliveryStatuses.Cancelled => "DeliveryCancelledV1",
            _ => throw new InvalidOperationException("Unsupported delivery operation type.")
        };

    private static string? ValidateCreate(CafeDeliveryCreateMutation mutation)
    {
        if (mutation.OperationId == Guid.Empty ||
            mutation.DeviceId == Guid.Empty ||
            mutation.DeliveryId == Guid.Empty ||
            mutation.OrderId == Guid.Empty ||
            mutation.StoreId == Guid.Empty ||
            mutation.ActorUserId == Guid.Empty)
            return "Operation, device, delivery, order, store and actor IDs are required.";

        if (string.IsNullOrWhiteSpace(mutation.RecipientName))
            return "Recipient name is required.";
        if (string.IsNullOrWhiteSpace(mutation.DeliveryAddress))
            return "Delivery address is required.";
        if (mutation.DeliveryFeeCents < 0)
            return "Delivery fee cannot be negative.";

        return null;
    }

    private static string? ValidateRevise(CafeDeliveryReviseMutation mutation)
    {
        if (mutation.OperationId == Guid.Empty ||
            mutation.DeviceId == Guid.Empty ||
            mutation.DeliveryId == Guid.Empty ||
            mutation.StoreId == Guid.Empty ||
            mutation.ActorUserId == Guid.Empty ||
            mutation.ExpectedVersion <= 0)
            return "Operation, device, delivery, store, actor and positive expected version are required.";

        if (string.IsNullOrWhiteSpace(mutation.RecipientName))
            return "Recipient name is required.";
        if (string.IsNullOrWhiteSpace(mutation.DeliveryAddress))
            return "Delivery address is required.";
        if (mutation.DeliveryFeeCents < 0)
            return "Delivery fee cannot be negative.";

        return null;
    }

    private static string? ValidateAdvance(CafeDeliveryAdvanceMutation mutation)
    {
        if (mutation.OperationId == Guid.Empty ||
            mutation.DeviceId == Guid.Empty ||
            mutation.DeliveryId == Guid.Empty ||
            mutation.StoreId == Guid.Empty ||
            mutation.ActorUserId == Guid.Empty ||
            mutation.ExpectedVersion <= 0)
            return "Operation, device, delivery, store, actor and positive expected version are required.";

        if (mutation.TargetStatus != CafeDeliveryStatuses.Dispatched &&
            mutation.TargetStatus != CafeDeliveryStatuses.Delivered &&
            mutation.TargetStatus != CafeDeliveryStatuses.Failed &&
            mutation.TargetStatus != CafeDeliveryStatuses.Cancelled)
            return "Delivery target status is invalid.";

        return null;
    }

    private static string HashCreate(
        CafeDeliveryCreateMutation mutation,
        string recipient,
        string? phone,
        string address,
        string? landmark,
        string? instructions,
        string? courier)
    {
        StringBuilder b = new();
        Append(b, "DeliveryCreatedV1");
        Append(b, mutation.OperationId);
        Append(b, mutation.DeviceId);
        Append(b, mutation.DeliveryId);
        Append(b, mutation.OrderId);
        Append(b, mutation.StoreId);
        Append(b, mutation.ActorUserId);
        Append(b, mutation.OccurredAtUtc.ToUniversalTime());
        Append(b, recipient);
        Append(b, phone);
        Append(b, address);
        Append(b, landmark);
        Append(b, instructions);
        Append(b, mutation.DeliveryFeeCents);
        Append(b, courier);
        return Hash(b.ToString());
    }

    private static string HashRevise(
        CafeDeliveryReviseMutation mutation,
        string recipient,
        string? phone,
        string address,
        string? landmark,
        string? instructions,
        string? courier)
    {
        StringBuilder b = new();
        Append(b, "DeliveryRevisedV1");
        Append(b, mutation.OperationId);
        Append(b, mutation.DeviceId);
        Append(b, mutation.DeliveryId);
        Append(b, mutation.StoreId);
        Append(b, mutation.ExpectedVersion);
        Append(b, mutation.ActorUserId);
        Append(b, mutation.OccurredAtUtc.ToUniversalTime());
        Append(b, recipient);
        Append(b, phone);
        Append(b, address);
        Append(b, landmark);
        Append(b, instructions);
        Append(b, mutation.DeliveryFeeCents);
        Append(b, courier);
        return Hash(b.ToString());
    }

    private static string HashAdvance(
        CafeDeliveryAdvanceMutation mutation,
        string? reason)
    {
        StringBuilder b = new();
        Append(b, ResolveOperationType(mutation.TargetStatus));
        Append(b, mutation.OperationId);
        Append(b, mutation.DeviceId);
        Append(b, mutation.DeliveryId);
        Append(b, mutation.StoreId);
        Append(b, mutation.ExpectedVersion);
        Append(b, mutation.ActorUserId);
        Append(b, mutation.OccurredAtUtc.ToUniversalTime());
        Append(b, mutation.TargetStatus);
        Append(b, reason);
        return Hash(b.ToString());
    }

    private static void Append(StringBuilder builder, object? value)
    {
        string text = value switch
        {
            null => string.Empty,
            DateTimeOffset dto => dto.ToUniversalTime().ToString("O"),
            Guid guid => guid.ToString("D"),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };
        builder.Append(text.Length)
            .Append(':')
            .Append(text)
            .Append('|');
    }

    private static string Hash(string value) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static Guid DeterministicGuid(
        string scope,
        string value)
    {
        byte[] bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(scope + "|" + value));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private static string? NormalizeOptional(string? value)
    {
        string normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

    private sealed record DeliveryRow(
        Guid DeliveryId,
        Guid OrderId,
        Guid StoreId,
        int Version,
        Guid CurrentRevisionId,
        string Status);

    private sealed record OrderAuthority(
        Guid StoreId,
        string ServiceMode,
        string State,
        Guid? CompletedSaleId);

    private sealed class DeliveryValidationException : Exception
    {
        public DeliveryValidationException(
            string code,
            string message)
            : base(message)
        {
            Code = code;
        }

        public string Code { get; }
    }
}
