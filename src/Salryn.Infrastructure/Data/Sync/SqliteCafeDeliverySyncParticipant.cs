using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Sync;

public static class SqliteCafeDeliverySyncParticipant
{
    public static Result ApplyCanonical(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncOperationEnvelopeV1 envelope,
        CafeDeliveryOperationV1 payload)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(payload);

        string? contractError = CafeDeliveryOperationCanonicalJson.Validate(
            envelope.OperationType,
            payload);
        if (contractError is not null)
        {
            return Result.Fail(
                Error.Conflict(
                    "CafeDeliverySync.PayloadInvalid",
                    contractError));
        }

        if (envelope.SchemaVersion != CafeDeliverySyncContract.SchemaVersion ||
            envelope.OperationId != payload.OperationId ||
            envelope.AggregateId != payload.DeliveryId ||
            envelope.StoreId != payload.StoreId ||
            envelope.ActorUserId != payload.ActorUserId ||
            envelope.BaseVersion != payload.ExpectedVersion)
        {
            return Result.Fail(
                Error.Conflict(
                    "CafeDeliverySync.EnvelopeAuthorityMismatch",
                    "Delivery envelope aggregate/store/actor/base-version does not match canonical payload."));
        }

        if (!StoreMatches(connection, transaction, payload.StoreId))
        {
            return Result.Fail(
                Error.Conflict(
                    "CafeDeliverySync.StoreAuthorityMismatch",
                    "Delivery StoreId does not match local deployment authority."));
        }

        if (!ActorExists(connection, transaction, payload.ActorUserId))
        {
            return Result.Fail(
                Error.Conflict(
                    "CafeDeliverySync.ActorPrerequisiteMissing",
                    "Delivery actor prerequisite is missing locally."));
        }

        OperationRow? existing = ReadOperation(
            connection,
            transaction,
            envelope.OperationId);
        if (existing is not null)
        {
            return VerifyCanonicalOperation(
                connection,
                transaction,
                envelope,
                payload)
                ? Result.Success()
                : Result.Fail(
                    Error.Conflict(
                        "CafeDeliverySync.OperationCollision",
                        "OperationId already exists with different canonical delivery evidence."));
        }

        try
        {
            if (string.Equals(
                    envelope.OperationType,
                    CafeDeliverySyncContract.DeliveryConfiguredV1,
                    StringComparison.Ordinal))
            {
                return ApplyConfigured(
                    connection,
                    transaction,
                    envelope,
                    payload);
            }

            return ApplyLifecycle(
                connection,
                transaction,
                envelope,
                payload);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return Result.Fail(
                Error.Conflict(
                    "CafeDeliverySync.ConstraintConflict",
                    ex.Message));
        }
        catch (Exception ex) when (
            ex is SqliteException or InvalidOperationException or
            FormatException or OverflowException or ArgumentException)
        {
            return Result.Fail(
                Error.Unexpected(
                    "CafeDeliverySync.ApplyFailed",
                    ex.Message));
        }
    }

    public static bool VerifyCanonical(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncOperationEnvelopeV1 envelope,
        CafeDeliveryOperationV1 payload) =>
        VerifyCanonicalOperation(
            connection,
            transaction,
            envelope,
            payload);

    public static bool VerifyCanonicalOperation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncOperationEnvelopeV1 envelope,
        CafeDeliveryOperationV1 payload)
    {
        OperationRow? operation = ReadOperation(
            connection,
            transaction,
            envelope.OperationId);
        if (operation is null)
            return false;

        if (envelope.OperationId != payload.OperationId ||
            operation.DeliveryId != payload.DeliveryId ||
            operation.DeviceId != envelope.DeviceId ||
            operation.StoreId != payload.StoreId ||
            !string.Equals(
                operation.OperationType,
                payload.EvidenceOperationType,
                StringComparison.Ordinal) ||
            !string.Equals(
                operation.PayloadHash,
                payload.EvidencePayloadHash,
                StringComparison.Ordinal) ||
            operation.ExpectedVersion != payload.ExpectedVersion ||
            operation.ResultingVersion != payload.ResultingVersion ||
            operation.ResultRevisionId != payload.CurrentRevisionId ||
            !string.Equals(
                operation.ResultStatus,
                payload.ToStatus,
                StringComparison.Ordinal) ||
            operation.ActorUserId != payload.ActorUserId ||
            operation.OccurredAtUtc.ToUniversalTime() !=
                payload.OccurredAtUtc.ToUniversalTime())
        {
            return false;
        }

        EventRow? evt = ReadEvent(
            connection,
            transaction,
            envelope.OperationId);
        if (evt is null ||
            evt.DeliveryId != payload.DeliveryId ||
            evt.OrderId != payload.OrderId ||
            evt.ExpectedVersion != payload.ExpectedVersion ||
            evt.ResultingVersion != payload.ResultingVersion ||
            !string.Equals(evt.Action, payload.Action, StringComparison.Ordinal) ||
            !string.Equals(evt.FromStatus, payload.FromStatus, StringComparison.Ordinal) ||
            !string.Equals(evt.ToStatus, payload.ToStatus, StringComparison.Ordinal) ||
            evt.ActorUserId != payload.ActorUserId ||
            evt.OccurredAtUtc.ToUniversalTime() !=
                payload.OccurredAtUtc.ToUniversalTime() ||
            !string.Equals(evt.Reason, payload.Reason, StringComparison.Ordinal) ||
            evt.RevisionId != payload.CurrentRevisionId ||
            evt.SaleId != payload.SaleId ||
            !string.Equals(
                evt.ContentHash,
                payload.EvidencePayloadHash,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (payload.Revision is null)
            return true;

        RevisionRow? revision = ReadRevision(
            connection,
            transaction,
            payload.Revision.RevisionId);
        return revision is not null &&
            revision.DeliveryId == payload.DeliveryId &&
            revision.OrderId == payload.OrderId &&
            revision.RevisionNumber == payload.Revision.RevisionNumber &&
            revision.DeliveryVersion == payload.Revision.DeliveryVersion &&
            string.Equals(
                revision.RecipientName,
                payload.Revision.RecipientName,
                StringComparison.Ordinal) &&
            string.Equals(
                revision.ContactPhone,
                payload.Revision.ContactPhone,
                StringComparison.Ordinal) &&
            string.Equals(
                revision.DeliveryAddress,
                payload.Revision.DeliveryAddress,
                StringComparison.Ordinal) &&
            string.Equals(
                revision.Landmark,
                payload.Revision.Landmark,
                StringComparison.Ordinal) &&
            string.Equals(
                revision.Instructions,
                payload.Revision.Instructions,
                StringComparison.Ordinal) &&
            revision.DeliveryFeeCents == payload.Revision.DeliveryFeeCents &&
            string.Equals(
                revision.CourierReference,
                payload.Revision.CourierReference,
                StringComparison.Ordinal) &&
            string.Equals(
                revision.ContentHash,
                payload.Revision.ContentHash,
                StringComparison.Ordinal) &&
            revision.ActorUserId == payload.Revision.ActorUserId &&
            revision.CreatedAtUtc.ToUniversalTime() ==
                payload.Revision.CreatedAtUtc.ToUniversalTime();
    }

    private static Result ApplyConfigured(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncOperationEnvelopeV1 envelope,
        CafeDeliveryOperationV1 payload)
    {
        CafeDeliveryRevisionEvidenceV1 revision =
            payload.Revision
            ?? throw new InvalidOperationException(
                "Configured delivery payload lost immutable revision evidence.");

        bool create = string.Equals(
            payload.EvidenceOperationType,
            CafeDeliverySyncContract.DeliveryCreatedEvidenceV1,
            StringComparison.Ordinal);

        OrderRow? order = ReadOrder(
            connection,
            transaction,
            payload.OrderId);
        if (order is null)
        {
            return Result.Fail(
                Error.Conflict(
                    "CafeDeliverySync.OrderPrerequisiteMissing",
                    "C8 delivery configuration requires the referenced C4 order."));
        }

        if (order.StoreId != payload.StoreId ||
            !string.Equals(
                order.ServiceMode,
                CafeOrderServiceModes.Takeout,
                StringComparison.Ordinal))
        {
            return Result.Fail(
                Error.Conflict(
                    "CafeDeliverySync.OrderAuthorityConflict",
                    "C8 delivery requires the referenced same-store C4 Takeout order."));
        }

        if (create)
        {
            if (string.Equals(order.State, CafeOrderStates.Completed, StringComparison.Ordinal) ||
                string.Equals(order.State, CafeOrderStates.Cancelled, StringComparison.Ordinal))
            {
                return Result.Fail(
                    Error.Conflict(
                        "CafeDeliverySync.OrderTerminalConflict",
                        "C8 delivery cannot be configured after the referenced C4 order is terminal."));
            }

            if (ReadDelivery(connection, transaction, payload.DeliveryId) is not null ||
                OrderHasDelivery(connection, transaction, payload.OrderId))
            {
                return Result.Fail(
                    Error.Conflict(
                        "CafeDeliverySync.DuplicateDeliveryConflict",
                        "C8 permits exactly one delivery fulfillment per C4 order."));
            }

            InsertFulfillment(
                connection,
                transaction,
                payload,
                envelope.DeviceId);
        }
        else
        {
            DeliveryRow? delivery = ReadDelivery(
                connection,
                transaction,
                payload.DeliveryId);
            if (delivery is null)
            {
                return Result.Fail(
                    Error.Conflict(
                        "CafeDeliverySync.DeliveryPrerequisiteMissing",
                        "Delivery revision requires the prior configured fulfillment."));
            }

            if (delivery.OrderId != payload.OrderId ||
                delivery.StoreId != payload.StoreId ||
                delivery.Version != payload.ExpectedVersion ||
                !string.Equals(
                    delivery.Status,
                    CafeDeliveryStatuses.Pending,
                    StringComparison.Ordinal) ||
                delivery.CurrentRevisionId ==
                    payload.CurrentRevisionId)
            {
                return Result.Fail(
                    Error.Conflict(
                        "CafeDeliverySync.VersionConflict",
                        "Delivery revision does not extend the exact Pending delivery version."));
            }

            int expectedRevisionNumber =
                ReadRevisionNumber(
                    connection,
                    transaction,
                    delivery.CurrentRevisionId) + 1;
            if (revision.RevisionNumber != expectedRevisionNumber)
            {
                return Result.Fail(
                    Error.Conflict(
                        "CafeDeliverySync.RevisionSequenceConflict",
                        "Delivery revision number is not the next immutable revision."));
            }

            InsertRevision(
                connection,
                transaction,
                payload,
                revision);

            using SqliteCommand update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE cafe_delivery_fulfillments
                SET current_version=$version,
                    current_revision_id=$revision,
                    updated_at_utc=$at
                WHERE delivery_id=$delivery
                  AND current_version=$expected
                  AND status='Pending';
                """;
            update.Parameters.AddWithValue("$version", payload.ResultingVersion);
            update.Parameters.AddWithValue("$revision", payload.CurrentRevisionId.ToString("D"));
            update.Parameters.AddWithValue(
                "$at",
                payload.OccurredAtUtc.ToUniversalTime().ToString("O"));
            update.Parameters.AddWithValue("$delivery", payload.DeliveryId.ToString("D"));
            update.Parameters.AddWithValue("$expected", payload.ExpectedVersion);

            if (update.ExecuteNonQuery() != 1)
            {
                return Result.Fail(
                    Error.Conflict(
                        "CafeDeliverySync.VersionConflict",
                        "Delivery revision lost exact-version ownership."));
            }
        }

        InsertOperationAndEvent(
            connection,
            transaction,
            envelope,
            payload);

        return Result.Success();
    }

    private static Result ApplyLifecycle(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncOperationEnvelopeV1 envelope,
        CafeDeliveryOperationV1 payload)
    {
        DeliveryRow? delivery = ReadDelivery(
            connection,
            transaction,
            payload.DeliveryId);
        if (delivery is null)
        {
            return Result.Fail(
                Error.Conflict(
                    "CafeDeliverySync.DeliveryPrerequisiteMissing",
                    "Delivery lifecycle operation requires prior configured fulfillment."));
        }

        if (delivery.OrderId != payload.OrderId ||
            delivery.StoreId != payload.StoreId ||
            delivery.Version != payload.ExpectedVersion ||
            delivery.CurrentRevisionId != payload.CurrentRevisionId ||
            !string.Equals(
                delivery.Status,
                payload.FromStatus,
                StringComparison.Ordinal))
        {
            return Result.Fail(
                Error.Conflict(
                    "CafeDeliverySync.VersionConflict",
                    "Delivery lifecycle operation does not extend exact retained version/state."));
        }

        if (CafeDeliveryStatuses.IsTerminal(delivery.Status))
        {
            return Result.Fail(
                Error.Conflict(
                    "CafeDeliverySync.TerminalConflict",
                    "Terminal C8 delivery state is immutable."));
        }

        if (payload.SaleId.HasValue)
        {
            Guid? completedSale = ReadCompletedSale(
                connection,
                transaction,
                payload.OrderId);
            if (!completedSale.HasValue)
            {
                return Result.Fail(
                    Error.Conflict(
                        "CafeDeliverySync.SalePrerequisiteMissing",
                        "Dispatch/resolution requires the referenced C4 completed Sale."));
            }

            if (completedSale.Value != payload.SaleId.Value)
            {
                return Result.Fail(
                    Error.Conflict(
                        "CafeDeliverySync.SalePrerequisiteConflict",
                        "Delivery lifecycle Sale identity conflicts with retained C4 completion."));
            }
        }

        string? terminalReason =
            string.Equals(payload.ToStatus, CafeDeliveryStatuses.Failed, StringComparison.Ordinal) ||
            string.Equals(payload.ToStatus, CafeDeliveryStatuses.Cancelled, StringComparison.Ordinal)
                ? payload.Reason
                : null;

        using SqliteCommand update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            UPDATE cafe_delivery_fulfillments
            SET current_version=$version,
                status=$status,
                updated_at_utc=$at,
                terminal_reason=$reason
            WHERE delivery_id=$delivery
              AND current_version=$expected
              AND status=$fromStatus;
            """;
        update.Parameters.AddWithValue("$version", payload.ResultingVersion);
        update.Parameters.AddWithValue("$status", payload.ToStatus);
        update.Parameters.AddWithValue(
            "$at",
            payload.OccurredAtUtc.ToUniversalTime().ToString("O"));
        update.Parameters.AddWithValue(
            "$reason",
            terminalReason is null ? DBNull.Value : terminalReason);
        update.Parameters.AddWithValue("$delivery", payload.DeliveryId.ToString("D"));
        update.Parameters.AddWithValue("$expected", payload.ExpectedVersion);
        update.Parameters.AddWithValue("$fromStatus", payload.FromStatus!);

        if (update.ExecuteNonQuery() != 1)
        {
            return Result.Fail(
                Error.Conflict(
                    "CafeDeliverySync.VersionConflict",
                    "Delivery lifecycle transition lost exact-version ownership."));
        }

        InsertOperationAndEvent(
            connection,
            transaction,
            envelope,
            payload);

        return Result.Success();
    }

    private static void InsertFulfillment(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeDeliveryOperationV1 payload,
        Guid deviceId)
    {
        CafeDeliveryRevisionEvidenceV1 revision =
            payload.Revision!;

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO cafe_delivery_fulfillments(
                    delivery_id,order_id,store_id,current_version,
                    current_revision_id,status,created_by_user_id,
                    created_at_utc,updated_at_utc,terminal_reason)
                VALUES(
                    $delivery,$order,$store,$version,
                    $revision,$status,$actor,$at,$at,NULL);
                """;
            command.Parameters.AddWithValue("$delivery", payload.DeliveryId.ToString("D"));
            command.Parameters.AddWithValue("$order", payload.OrderId.ToString("D"));
            command.Parameters.AddWithValue("$store", payload.StoreId.ToString("D"));
            command.Parameters.AddWithValue("$version", payload.ResultingVersion);
            command.Parameters.AddWithValue("$revision", payload.CurrentRevisionId.ToString("D"));
            command.Parameters.AddWithValue("$status", payload.ToStatus);
            command.Parameters.AddWithValue("$actor", payload.ActorUserId.ToString("D"));
            command.Parameters.AddWithValue(
                "$at",
                payload.OccurredAtUtc.ToUniversalTime().ToString("O"));
            command.ExecuteNonQuery();
        }

        InsertRevision(
            connection,
            transaction,
            payload,
            revision);
    }

    private static void InsertRevision(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeDeliveryOperationV1 payload,
        CafeDeliveryRevisionEvidenceV1 revision)
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
        command.Parameters.AddWithValue("$revision", revision.RevisionId.ToString("D"));
        command.Parameters.AddWithValue("$delivery", payload.DeliveryId.ToString("D"));
        command.Parameters.AddWithValue("$order", payload.OrderId.ToString("D"));
        command.Parameters.AddWithValue("$number", revision.RevisionNumber);
        command.Parameters.AddWithValue("$version", revision.DeliveryVersion);
        command.Parameters.AddWithValue("$recipient", revision.RecipientName);
        command.Parameters.AddWithValue(
            "$phone",
            revision.ContactPhone is null ? DBNull.Value : revision.ContactPhone);
        command.Parameters.AddWithValue("$address", revision.DeliveryAddress);
        command.Parameters.AddWithValue(
            "$landmark",
            revision.Landmark is null ? DBNull.Value : revision.Landmark);
        command.Parameters.AddWithValue(
            "$instructions",
            revision.Instructions is null ? DBNull.Value : revision.Instructions);
        command.Parameters.AddWithValue("$fee", revision.DeliveryFeeCents);
        command.Parameters.AddWithValue(
            "$courier",
            revision.CourierReference is null ? DBNull.Value : revision.CourierReference);
        command.Parameters.AddWithValue("$hash", revision.ContentHash);
        command.Parameters.AddWithValue("$actor", revision.ActorUserId.ToString("D"));
        command.Parameters.AddWithValue(
            "$at",
            revision.CreatedAtUtc.ToUniversalTime().ToString("O"));
        command.ExecuteNonQuery();
    }

    private static void InsertOperationAndEvent(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncOperationEnvelopeV1 envelope,
        CafeDeliveryOperationV1 payload)
    {
        using (SqliteCommand operation = connection.CreateCommand())
        {
            operation.Transaction = transaction;
            operation.CommandText = """
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
            operation.Parameters.AddWithValue("$operation", envelope.OperationId.ToString("D"));
            operation.Parameters.AddWithValue("$delivery", payload.DeliveryId.ToString("D"));
            operation.Parameters.AddWithValue("$device", envelope.DeviceId.ToString("D"));
            operation.Parameters.AddWithValue("$store", payload.StoreId.ToString("D"));
            operation.Parameters.AddWithValue("$type", payload.EvidenceOperationType);
            operation.Parameters.AddWithValue("$hash", payload.EvidencePayloadHash);
            operation.Parameters.AddWithValue("$expected", payload.ExpectedVersion);
            operation.Parameters.AddWithValue("$resulting", payload.ResultingVersion);
            operation.Parameters.AddWithValue("$revision", payload.CurrentRevisionId.ToString("D"));
            operation.Parameters.AddWithValue("$status", payload.ToStatus);
            operation.Parameters.AddWithValue("$actor", payload.ActorUserId.ToString("D"));
            operation.Parameters.AddWithValue(
                "$at",
                payload.OccurredAtUtc.ToUniversalTime().ToString("O"));
            operation.ExecuteNonQuery();
        }

        using SqliteCommand evt = connection.CreateCommand();
        evt.Transaction = transaction;
        evt.CommandText = """
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
        evt.Parameters.AddWithValue(
            "$event",
            DeterministicGuid(
                "salryn-c8-delivery-event-v1",
                envelope.OperationId.ToString("D")).ToString("D"));
        evt.Parameters.AddWithValue("$operation", envelope.OperationId.ToString("D"));
        evt.Parameters.AddWithValue("$delivery", payload.DeliveryId.ToString("D"));
        evt.Parameters.AddWithValue("$order", payload.OrderId.ToString("D"));
        evt.Parameters.AddWithValue("$expected", payload.ExpectedVersion);
        evt.Parameters.AddWithValue("$resulting", payload.ResultingVersion);
        evt.Parameters.AddWithValue("$action", payload.Action);
        evt.Parameters.AddWithValue(
            "$from",
            payload.FromStatus is null ? DBNull.Value : payload.FromStatus);
        evt.Parameters.AddWithValue("$to", payload.ToStatus);
        evt.Parameters.AddWithValue("$actor", payload.ActorUserId.ToString("D"));
        evt.Parameters.AddWithValue(
            "$at",
            payload.OccurredAtUtc.ToUniversalTime().ToString("O"));
        evt.Parameters.AddWithValue(
            "$reason",
            payload.Reason is null ? DBNull.Value : payload.Reason);
        evt.Parameters.AddWithValue("$revision", payload.CurrentRevisionId.ToString("D"));
        evt.Parameters.AddWithValue(
            "$sale",
            payload.SaleId.HasValue
                ? payload.SaleId.Value.ToString("D")
                : DBNull.Value);
        evt.Parameters.AddWithValue("$hash", payload.EvidencePayloadHash);
        evt.ExecuteNonQuery();
    }

    private static bool StoreMatches(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM deployment_identity
            WHERE store_id=$store;
            """;
        command.Parameters.AddWithValue("$store", storeId.ToString("D"));
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L) == 1;
    }

    private static bool ActorExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid actorUserId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM users
            WHERE id=$user;
            """;
        command.Parameters.AddWithValue("$user", actorUserId.ToString("D"));
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L) == 1;
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
        command.Parameters.AddWithValue("$delivery", deliveryId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        return !reader.Read()
            ? null
            : new DeliveryRow(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                Guid.Parse(reader.GetString(2)),
                reader.GetInt32(3),
                Guid.Parse(reader.GetString(4)),
                reader.GetString(5));
    }

    private static OrderRow? ReadOrder(
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
        command.Parameters.AddWithValue("$order", orderId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        return !reader.Read()
            ? null
            : new OrderRow(
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
        OrderRow? order = ReadOrder(connection, transaction, orderId);
        return order is not null &&
               string.Equals(order.State, CafeOrderStates.Completed, StringComparison.Ordinal)
            ? order.CompletedSaleId
            : null;
    }

    private static bool OrderHasDelivery(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid orderId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT COUNT(*) FROM cafe_delivery_fulfillments WHERE order_id=$order;";
        command.Parameters.AddWithValue("$order", orderId.ToString("D"));
        return Convert.ToInt64(command.ExecuteScalar() ?? 0L) != 0;
    }

    private static int ReadRevisionNumber(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid revisionId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT revision_number FROM cafe_delivery_revisions WHERE revision_id=$revision;";
        command.Parameters.AddWithValue("$revision", revisionId.ToString("D"));
        return Convert.ToInt32(
            command.ExecuteScalar()
            ?? throw new InvalidOperationException("Delivery revision prerequisite is missing."));
    }

    private static OperationRow? ReadOperation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT delivery_id,device_id,store_id,operation_type,payload_hash,
                   expected_version,resulting_version,result_revision_id,
                   result_status,actor_user_id,occurred_at_utc
            FROM cafe_delivery_operations
            WHERE operation_id=$operation
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$operation", operationId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        return !reader.Read()
            ? null
            : new OperationRow(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                Guid.Parse(reader.GetString(2)),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                Guid.Parse(reader.GetString(7)),
                reader.GetString(8),
                Guid.Parse(reader.GetString(9)),
                DateTimeOffset.Parse(reader.GetString(10)));
    }

    private static EventRow? ReadEvent(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT delivery_id,order_id,expected_version,resulting_version,
                   action,from_status,to_status,actor_user_id,occurred_at_utc,
                   reason,delivery_revision_id,sale_id,content_hash
            FROM cafe_delivery_events
            WHERE operation_id=$operation
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$operation", operationId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        return !reader.Read()
            ? null
            : new EventRow(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6),
                Guid.Parse(reader.GetString(7)),
                DateTimeOffset.Parse(reader.GetString(8)),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                Guid.Parse(reader.GetString(10)),
                reader.IsDBNull(11) ? null : Guid.Parse(reader.GetString(11)),
                reader.GetString(12));
    }

    private static RevisionRow? ReadRevision(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid revisionId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT delivery_id,order_id,revision_number,delivery_version,
                   recipient_name,contact_phone,delivery_address,landmark,
                   instructions,delivery_fee_cents,courier_reference,
                   content_hash,actor_user_id,created_at_utc
            FROM cafe_delivery_revisions
            WHERE revision_id=$revision
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$revision", revisionId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        return !reader.Read()
            ? null
            : new RevisionRow(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.GetInt64(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.GetString(11),
                Guid.Parse(reader.GetString(12)),
                DateTimeOffset.Parse(reader.GetString(13)));
    }

    private static Guid DeterministicGuid(string scope, string value)
    {
        byte[] bytes = SHA256.HashData(
            Encoding.UTF8.GetBytes(scope + "|" + value));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private sealed record DeliveryRow(
        Guid DeliveryId,
        Guid OrderId,
        Guid StoreId,
        int Version,
        Guid CurrentRevisionId,
        string Status);

    private sealed record OrderRow(
        Guid StoreId,
        string ServiceMode,
        string State,
        Guid? CompletedSaleId);

    private sealed record OperationRow(
        Guid DeliveryId,
        Guid DeviceId,
        Guid StoreId,
        string OperationType,
        string PayloadHash,
        int ExpectedVersion,
        int ResultingVersion,
        Guid ResultRevisionId,
        string ResultStatus,
        Guid ActorUserId,
        DateTimeOffset OccurredAtUtc);

    private sealed record EventRow(
        Guid DeliveryId,
        Guid OrderId,
        int ExpectedVersion,
        int ResultingVersion,
        string Action,
        string? FromStatus,
        string ToStatus,
        Guid ActorUserId,
        DateTimeOffset OccurredAtUtc,
        string? Reason,
        Guid RevisionId,
        Guid? SaleId,
        string ContentHash);

    private sealed record RevisionRow(
        Guid DeliveryId,
        Guid OrderId,
        int RevisionNumber,
        int DeliveryVersion,
        string RecipientName,
        string? ContactPhone,
        string DeliveryAddress,
        string? Landmark,
        string? Instructions,
        long DeliveryFeeCents,
        string? CourierReference,
        string ContentHash,
        Guid ActorUserId,
        DateTimeOffset CreatedAtUtc);
}
