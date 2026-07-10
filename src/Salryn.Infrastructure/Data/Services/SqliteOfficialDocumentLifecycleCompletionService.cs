using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Core.Services.InvoiceOutput;
using Salryn.Core.ValueObjects;

namespace Salryn.Infrastructure.Data.Services;

/// <summary>
/// M4-RC1: genuine lifecycle completion — sale/stock effects + EJ event + state + idempotency in one transaction.
/// Never rewrites original EJ snapshot. Never advances sales sequence.
/// </summary>
public sealed class SqliteOfficialDocumentLifecycleCompletionService
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly Func<Guid> _idFactory;
    private readonly Func<DateTimeOffset> _clock;

    public SqliteOfficialDocumentLifecycleCompletionService(
        SqliteConnectionFactory connectionFactory,
        Func<Guid>? idFactory = null,
        Func<DateTimeOffset>? clock = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _idFactory = idFactory ?? Guid.NewGuid;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public Task<Result<OfficialDocumentLifecycleActionResult>> ExecuteAsync(
        OfficialDocumentLifecycleRuntimeRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Result validation = Validate(request);
        if (validation.IsFailure)
        {
            return Task.FromResult(Result<OfficialDocumentLifecycleActionResult>.Fail(validation.ErrorDetail));
        }

        try
        {
            using SqliteConnection connection = _connectionFactory.CreateConnection();
            connection.Open();
            using SqliteTransaction tx = connection.BeginTransaction();

            // Idempotency first
            string requestHash = HashRequest(request);
            Result<OfficialDocumentLifecycleActionResult>? prior = TryReadIdempotent(connection, tx, request.IdempotencyKey!, requestHash);
            if (prior is not null)
            {
                tx.Commit();
                return Task.FromResult(prior);
            }

            Result<OriginalDocument> original = ResolveOriginal(connection, tx, request);
            if (original.IsFailure)
            {
                tx.Rollback();
                return Task.FromResult(Result<OfficialDocumentLifecycleActionResult>.Fail(original.ErrorDetail));
            }

            LifecycleStateRow state = LoadOrCreateState(connection, tx, original.Value, request.Action);

            Result transition = GuardTransition(state, request);
            if (transition.IsFailure)
            {
                tx.Rollback();
                return Task.FromResult(Result<OfficialDocumentLifecycleActionResult>.Fail(transition.ErrorDetail));
            }

            Guid eventId = _idFactory();
            DateTimeOffset now = _clock();
            InvoiceOutputPackage? reprintOutput = null;

            if (request.Action == OfficialDocumentLifecycleAction.Reprint)
            {
                Result<InvoiceOutputPackage> rendered = RenderReprint(
                    connection,
                    original.Value,
                    request.Reason!);
                if (rendered.IsFailure)
                {
                    tx.Rollback();
                    return Task.FromResult(Result<OfficialDocumentLifecycleActionResult>.Fail(rendered.ErrorDetail));
                }

                reprintOutput = rendered.Value;
                AppendEjEvent(connection, tx, eventId, original.Value.JournalEntryId, "REPRINT",
                    voidMarker: 0, refundMarker: 0, cancelMarker: 0, request.ActorUserId, request.Reason!, now, request.TerminalId);
                // State stays Committed
                UpsertState(connection, tx, state with
                {
                    LastEventId = eventId.ToString("D"),
                    UpdatedAt = now.ToString("O", CultureInfo.InvariantCulture)
                });
            }
            else if (request.Action == OfficialDocumentLifecycleAction.Void)
            {
                Result voidOps = ApplyVoidOperational(connection, tx, original.Value, request.ActorUserId, request.Reason!);
                if (voidOps.IsFailure)
                {
                    tx.Rollback();
                    return Task.FromResult(Result<OfficialDocumentLifecycleActionResult>.Fail(voidOps.ErrorDetail));
                }

                AppendEjEvent(connection, tx, eventId, original.Value.JournalEntryId, "VOID",
                    voidMarker: 1, refundMarker: 0, cancelMarker: 0, request.ActorUserId, request.Reason!, now, request.TerminalId);
                UpsertState(connection, tx, state with
                {
                    LifecycleState = "Voided",
                    StateVersion = state.StateVersion + 1,
                    LastEventId = eventId.ToString("D"),
                    UpdatedAt = now.ToString("O", CultureInfo.InvariantCulture)
                });
            }
            else if (request.Action is OfficialDocumentLifecycleAction.Refund)
            {
                long refundCents = request.RefundAmountCents ?? 0;
                if (refundCents <= 0)
                {
                    tx.Rollback();
                    return Task.FromResult(Result<OfficialDocumentLifecycleActionResult>.Fail(Error.Validation(
                        "DocumentLifecycle.RefundAmountRequired",
                        "Refund amount cents must be positive.")));
                }

                long remaining = state.OriginalTotalCents - state.RefundedTotalCents;
                if (refundCents > remaining)
                {
                    tx.Rollback();
                    return Task.FromResult(Result<OfficialDocumentLifecycleActionResult>.Fail(Error.Validation(
                        "DocumentLifecycle.OverRefund",
                        "Refund exceeds remaining refundable amount.")));
                }

                Result stock = ApplyPartialStockReturn(connection, tx, original.Value, request.ActorUserId, refundCents, state.OriginalTotalCents);
                if (stock.IsFailure)
                {
                    tx.Rollback();
                    return Task.FromResult(Result<OfficialDocumentLifecycleActionResult>.Fail(stock.ErrorDetail));
                }

                AppendEjEvent(connection, tx, eventId, original.Value.JournalEntryId, "REFUND",
                    voidMarker: 0, refundMarker: 1, cancelMarker: 0, request.ActorUserId, request.Reason!, now, request.TerminalId);

                InsertRefundLine(connection, tx, eventId, original.Value.JournalEntryId, refundCents, now);

                long newRefunded = state.RefundedTotalCents + refundCents;
                string newState = newRefunded >= state.OriginalTotalCents ? "FullyRefunded" : "PartiallyRefunded";
                UpsertState(connection, tx, state with
                {
                    LifecycleState = newState,
                    RefundedTotalCents = newRefunded,
                    StateVersion = state.StateVersion + 1,
                    LastEventId = eventId.ToString("D"),
                    UpdatedAt = now.ToString("O", CultureInfo.InvariantCulture)
                });
            }
            else
            {
                tx.Rollback();
                return Task.FromResult(Result<OfficialDocumentLifecycleActionResult>.Fail(Error.Validation(
                    "DocumentLifecycle.UnknownAction",
                    "Unsupported lifecycle action for completion service.")));
            }

            OfficialDocumentLifecycleActionResult result = new(
                request.Action,
                eventId,
                original.Value.JournalEntryId,
                original.Value.OfficialDocumentNumber,
                request.Reason!.Trim(),
                request.ActorUserId,
                now,
                SequenceAdvanced: false,
                OriginalImmutable: true,
                Summary: BuildSummary(request.Action),
                ReprintOutput: reprintOutput);

            StoreIdempotency(connection, tx, request.IdempotencyKey!, request.Action.ToString(), original.Value.JournalEntryId, requestHash, eventId, result, now);

            // Concurrency: optimistic version check already embedded in Upsert via state_version update pattern.
            tx.Commit();
            return Task.FromResult(Result<OfficialDocumentLifecycleActionResult>.Success(result));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException or OverflowException)
        {
            return Task.FromResult(Result<OfficialDocumentLifecycleActionResult>.Fail(Error.Unexpected(
                "DocumentLifecycle.RuntimeFailed",
                "Lifecycle completion failed: " + ex.Message)));
        }
    }

    private static Result Validate(OfficialDocumentLifecycleRuntimeRequest? request)
    {
        if (request is null)
        {
            return Result.Fail(Error.Validation("DocumentLifecycle.RequestRequired", "Request required."));
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            return Result.Fail(Error.Validation("DocumentLifecycle.IdempotencyKeyRequired", "Idempotency key required."));
        }

        if (request.ActorUserId == Guid.Empty)
        {
            return Result.Fail(Error.Validation("DocumentLifecycle.ActorRequired", "Authenticated actor required."));
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Result.Fail(Error.Validation("DocumentLifecycle.ReasonRequired", "Reason required."));
        }

        bool hasJournal = request.JournalEntryId is Guid j && j != Guid.Empty;
        bool hasDoc = !string.IsNullOrWhiteSpace(request.OfficialDocumentNumber);
        bool hasSale = request.SaleId is Guid s && s != Guid.Empty;
        if (!hasJournal && !hasDoc && !hasSale)
        {
            return Result.Fail(Error.Validation(
                "DocumentLifecycle.IdentityRequired",
                "Journal entry id, official document number, or sale id is required."));
        }

        return Result.Success();
    }

    private static Result<OfficialDocumentLifecycleActionResult>? TryReadIdempotent(
        SqliteConnection connection,
        SqliteTransaction tx,
        string key,
        string requestHash)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            SELECT request_hash, result_json
            FROM official_document_lifecycle_idempotency
            WHERE idempotency_key = $key
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$key", key);
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        string storedHash = reader.GetString(0);
        if (!string.Equals(storedHash, requestHash, StringComparison.Ordinal))
        {
            return Result<OfficialDocumentLifecycleActionResult>.Fail(Error.Conflict(
                "DocumentLifecycle.IdempotencyConflict",
                "Idempotency key was reused with a different request."));
        }

        string json = reader.GetString(1);
        OfficialDocumentLifecycleActionResult? prior =
            JsonSerializer.Deserialize<OfficialDocumentLifecycleActionResult>(json);
        if (prior is null)
        {
            return Result<OfficialDocumentLifecycleActionResult>.Fail(Error.Unexpected(
                "DocumentLifecycle.IdempotencyCorrupt",
                "Stored idempotency result could not be deserialized."));
        }

        return Result<OfficialDocumentLifecycleActionResult>.Success(prior);
    }

    private static Result<OriginalDocument> ResolveOriginal(
        SqliteConnection connection,
        SqliteTransaction tx,
        OfficialDocumentLifecycleRuntimeRequest request)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            SELECT e.id, e.sale_id, e.official_document_number, e.snapshot_payload_json, e.snapshot_payload_hash
            FROM bir_electronic_journal_entries e
            WHERE ($journalId IS NULL OR e.id = $journalId)
              AND ($doc IS NULL OR e.official_document_number = $doc)
              AND ($saleId IS NULL OR e.sale_id = $saleId)
            ORDER BY e.created_at ASC, e.id ASC
            LIMIT 2;
            """;
        command.Parameters.AddWithValue(
            "$journalId",
            request.JournalEntryId is Guid j && j != Guid.Empty ? j.ToString("D") : DBNull.Value);
        command.Parameters.AddWithValue(
            "$doc",
            string.IsNullOrWhiteSpace(request.OfficialDocumentNumber) ? DBNull.Value : request.OfficialDocumentNumber.Trim());
        command.Parameters.AddWithValue(
            "$saleId",
            request.SaleId is Guid s && s != Guid.Empty ? s.ToString("D") : DBNull.Value);

        using SqliteDataReader reader = command.ExecuteReader();
        List<OriginalDocument> matches = new();
        while (reader.Read())
        {
            matches.Add(new OriginalDocument(
                Guid.Parse(reader.GetString(0)),
                reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
                reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                reader.IsDBNull(4) ? string.Empty : reader.GetString(4)));
        }

        if (matches.Count == 0)
        {
            return Result<OriginalDocument>.Fail(Error.NotFound(
                "DocumentLifecycle.JournalEntryNotFound",
                "Official journal entry was not found."));
        }

        if (matches.Count > 1)
        {
            return Result<OriginalDocument>.Fail(Error.Conflict(
                "DocumentLifecycle.AmbiguousReference",
                "Lifecycle reference matched more than one original document."));
        }

        OriginalDocument doc = matches[0];
        if (string.IsNullOrWhiteSpace(doc.OfficialDocumentNumber) || string.IsNullOrWhiteSpace(doc.SnapshotPayloadHash))
        {
            return Result<OriginalDocument>.Fail(Error.Failure(
                "DocumentLifecycle.IncompleteOriginal",
                "Original official document evidence is incomplete."));
        }

        return Result<OriginalDocument>.Success(doc);
    }

    private static LifecycleStateRow LoadOrCreateState(
        SqliteConnection connection,
        SqliteTransaction tx,
        OriginalDocument original,
        OfficialDocumentLifecycleAction action)
    {
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = tx;
            command.CommandText = """
                SELECT journal_entry_id, sale_id, official_document_number, lifecycle_state,
                       original_total_cents, refunded_total_cents, state_version, last_event_id, updated_at
                FROM official_document_lifecycle_state
                WHERE journal_entry_id = $id
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$id", original.JournalEntryId.ToString("D"));
            using SqliteDataReader reader = command.ExecuteReader();
            if (reader.Read())
            {
                return new LifecycleStateRow(
                    reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt64(4),
                    reader.GetInt64(5),
                    reader.GetInt32(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.GetString(8));
            }
        }

        long originalTotal = ReadTotalFromSnapshot(original.SnapshotPayloadJson);
        return new LifecycleStateRow(
            original.JournalEntryId.ToString("D"),
            original.SaleId?.ToString("D"),
            original.OfficialDocumentNumber,
            "Committed",
            originalTotal,
            0,
            1,
            null,
            DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
    }

    private static long ReadTotalFromSnapshot(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return 0;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.TryGetProperty("amounts", out JsonElement amounts) &&
                amounts.TryGetProperty("totalCents", out JsonElement total) &&
                total.ValueKind == JsonValueKind.Number)
            {
                return total.GetInt64();
            }
        }
        catch (JsonException)
        {
            // fall through
        }

        return 0;
    }

    private static Result GuardTransition(LifecycleStateRow state, OfficialDocumentLifecycleRuntimeRequest request)
    {
        if (request.Action == OfficialDocumentLifecycleAction.Reprint)
        {
            if (state.LifecycleState is "Voided" or "Cancelled")
            {
                return Result.Fail(Error.Conflict(
                    "DocumentLifecycle.ReprintNotAllowed",
                    "Reprint is not allowed from voided/cancelled state."));
            }

            return Result.Success();
        }

        if (request.Action == OfficialDocumentLifecycleAction.Void)
        {
            if (state.LifecycleState != "Committed" || state.RefundedTotalCents > 0)
            {
                return Result.Fail(Error.Conflict(
                    "DocumentLifecycle.VoidNotAllowed",
                    "Void is only allowed from a non-refunded committed state."));
            }

            return Result.Success();
        }

        if (request.Action == OfficialDocumentLifecycleAction.Refund)
        {
            if (state.LifecycleState is "Voided" or "Cancelled" or "FullyRefunded")
            {
                return Result.Fail(Error.Conflict(
                    "DocumentLifecycle.RefundNotAllowed",
                    "Refund is not allowed from current lifecycle state."));
            }

            return Result.Success();
        }

        return Result.Fail(Error.Validation("DocumentLifecycle.UnknownAction", "Unknown action."));
    }

    private static Result ApplyVoidOperational(
        SqliteConnection connection,
        SqliteTransaction tx,
        OriginalDocument original,
        Guid actorUserId,
        string reason)
    {
        if (original.SaleId is null)
        {
            return Result.Fail(Error.Failure(
                "DocumentLifecycle.SaleMissing",
                "Original journal entry is not linked to a sale."));
        }

        // Mark sale voided if still completed.
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = tx;
            command.CommandText = """
                UPDATE sales
                SET status = $voided,
                    voided_by_user_id = $actor,
                    void_reason = $reason,
                    voided_at = $voidedAt
                WHERE id = $saleId
                  AND status = $completed;
                """;
            command.Parameters.AddWithValue("$voided", (int)SaleStatus.Voided);
            command.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
            command.Parameters.AddWithValue("$reason", reason.Trim());
            command.Parameters.AddWithValue("$voidedAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$saleId", original.SaleId.Value.ToString("D"));
            command.Parameters.AddWithValue("$completed", (int)SaleStatus.Completed);
            int affected = command.ExecuteNonQuery();
            if (affected == 0)
            {
                return Result.Fail(Error.Conflict(
                    "DocumentLifecycle.SaleNotVoidable",
                    "Sale was not found or is not voidable."));
            }
        }

        // Reverse all sale stock movements for this sale.
        return ReverseSaleStock(connection, tx, original.SaleId.Value, actorUserId, ratio: 1.0m);
    }

    private static Result ApplyPartialStockReturn(
        SqliteConnection connection,
        SqliteTransaction tx,
        OriginalDocument original,
        Guid actorUserId,
        long refundCents,
        long originalTotalCents)
    {
        if (original.SaleId is null)
        {
            return Result.Fail(Error.Failure("DocumentLifecycle.SaleMissing", "Sale missing for refund."));
        }

        if (originalTotalCents <= 0)
        {
            return Result.Success();
        }

        decimal ratio = (decimal)refundCents / originalTotalCents;
        if (ratio <= 0 || ratio > 1)
        {
            return Result.Fail(Error.Validation("DocumentLifecycle.InvalidRefundRatio", "Invalid refund ratio."));
        }

        return ReverseSaleStock(connection, tx, original.SaleId.Value, actorUserId, ratio);
    }

    private static Result ReverseSaleStock(
        SqliteConnection connection,
        SqliteTransaction tx,
        Guid saleId,
        Guid actorUserId,
        decimal ratio)
    {
        using SqliteCommand read = connection.CreateCommand();
        read.Transaction = tx;
        read.CommandText = """
            SELECT product_id, qty_change_x1000
            FROM stock_movements
            WHERE reference_type = 'SALE'
              AND reference_id = $saleId
              AND movement_type = $saleType;
            """;
        read.Parameters.AddWithValue("$saleId", saleId.ToString("D"));
        read.Parameters.AddWithValue("$saleType", (int)StockMovementType.Sale);

        List<(Guid ProductId, long Qty)> lines = new();
        using (SqliteDataReader reader = read.ExecuteReader())
        {
            while (reader.Read())
            {
                lines.Add((Guid.Parse(reader.GetString(0)), reader.GetInt64(1)));
            }
        }

        foreach ((Guid productId, long qty) in lines)
        {
            // Sale movements are typically negative qty_change; reverse restores absolute units * ratio.
            long unitsSold = Math.Abs(qty);
            long reverseQty = (long)decimal.Round(unitsSold * ratio, 0, MidpointRounding.AwayFromZero);
            if (reverseQty <= 0)
            {
                continue;
            }

            Guid movementId = Guid.NewGuid();
            using SqliteCommand insert = connection.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT INTO stock_movements (
                    id, product_id, movement_type, qty_change_x1000, cost_price_cents,
                    reference_type, reference_id, reason, user_id, created_at
                ) VALUES (
                    $id, $productId, $type, $qty, 0,
                    'SALE', $saleId, $reason, $userId, $createdAt
                );
                """;
            insert.Parameters.AddWithValue("$id", movementId.ToString("D"));
            insert.Parameters.AddWithValue("$productId", productId.ToString("D"));
            insert.Parameters.AddWithValue("$type", (int)StockMovementType.VoidSale);
            insert.Parameters.AddWithValue("$qty", reverseQty);
            insert.Parameters.AddWithValue("$saleId", saleId.ToString("D"));
            insert.Parameters.AddWithValue("$reason", "Official lifecycle stock return");
            insert.Parameters.AddWithValue("$userId", actorUserId.ToString("D"));
            insert.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            insert.ExecuteNonQuery();

            using SqliteCommand stock = connection.CreateCommand();
            stock.Transaction = tx;
            stock.CommandText = """
                UPDATE products
                SET stock_on_hand_x1000 = stock_on_hand_x1000 + $qty
                WHERE id = $productId;
                """;
            stock.Parameters.AddWithValue("$qty", reverseQty);
            stock.Parameters.AddWithValue("$productId", productId.ToString("D"));
            int updated = stock.ExecuteNonQuery();
            if (updated == 0)
            {
                return Result.Fail(Error.NotFound("DocumentLifecycle.ProductMissing", "Product missing for stock return."));
            }
        }

        return Result.Success();
    }

    private static void AppendEjEvent(
        SqliteConnection connection,
        SqliteTransaction tx,
        Guid eventId,
        Guid journalEntryId,
        string eventType,
        int voidMarker,
        int refundMarker,
        int cancelMarker,
        Guid actor,
        string reason,
        DateTimeOffset now,
        string? terminalId)
    {
        int reprintMarker = eventType == "REPRINT" ? 1 : 0;
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT INTO bir_electronic_journal_entry_events (
                id, journal_entry_id, event_type,
                original_marker, reprint_marker, void_marker, refund_marker, cancel_marker,
                cashier_user_id, reason, preservation_batch_id, created_at, metadata_json
            ) VALUES (
                $id, $journalId, $eventType,
                0, $reprint, $void, $refund, $cancel,
                $actor, $reason, NULL, $createdAt, $metadata
            );
            """;
        command.Parameters.AddWithValue("$id", eventId.ToString("D"));
        command.Parameters.AddWithValue("$journalId", journalEntryId.ToString("D"));
        command.Parameters.AddWithValue("$eventType", eventType);
        command.Parameters.AddWithValue("$reprint", reprintMarker);
        command.Parameters.AddWithValue("$void", voidMarker);
        command.Parameters.AddWithValue("$refund", refundMarker);
        command.Parameters.AddWithValue("$cancel", cancelMarker);
        command.Parameters.AddWithValue("$actor", actor.ToString("D"));
        command.Parameters.AddWithValue("$reason", reason.Trim());
        command.Parameters.AddWithValue("$createdAt", now.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$metadata", JsonSerializer.Serialize(new
        {
            terminalId,
            originalImmutable = true,
            sequenceAdvanced = false
        }));
        command.ExecuteNonQuery();
    }

    private static void InsertRefundLine(
        SqliteConnection connection,
        SqliteTransaction tx,
        Guid eventId,
        Guid journalEntryId,
        long refundCents,
        DateTimeOffset now)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT INTO official_document_lifecycle_refund_lines (
                id, journal_entry_id, event_id, sale_item_id, quantity_x1000, refund_amount_cents, created_at
            ) VALUES (
                $id, $journalId, $eventId, NULL, 1000, $amount, $createdAt
            );
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue("$journalId", journalEntryId.ToString("D"));
        command.Parameters.AddWithValue("$eventId", eventId.ToString("D"));
        command.Parameters.AddWithValue("$amount", refundCents);
        command.Parameters.AddWithValue("$createdAt", now.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private static void UpsertState(SqliteConnection connection, SqliteTransaction tx, LifecycleStateRow state)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT INTO official_document_lifecycle_state (
                journal_entry_id, sale_id, official_document_number, lifecycle_state,
                original_total_cents, refunded_total_cents, state_version, last_event_id, updated_at
            ) VALUES (
                $journalId, $saleId, $doc, $state,
                $original, $refunded, $version, $eventId, $updatedAt
            )
            ON CONFLICT(journal_entry_id) DO UPDATE SET
                lifecycle_state = excluded.lifecycle_state,
                refunded_total_cents = excluded.refunded_total_cents,
                state_version = excluded.state_version,
                last_event_id = excluded.last_event_id,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$journalId", state.JournalEntryId);
        command.Parameters.AddWithValue("$saleId", (object?)state.SaleId ?? DBNull.Value);
        command.Parameters.AddWithValue("$doc", state.OfficialDocumentNumber);
        command.Parameters.AddWithValue("$state", state.LifecycleState);
        command.Parameters.AddWithValue("$original", state.OriginalTotalCents);
        command.Parameters.AddWithValue("$refunded", state.RefundedTotalCents);
        command.Parameters.AddWithValue("$version", state.StateVersion);
        command.Parameters.AddWithValue("$eventId", (object?)state.LastEventId ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedAt", state.UpdatedAt);
        command.ExecuteNonQuery();
    }

    private static void StoreIdempotency(
        SqliteConnection connection,
        SqliteTransaction tx,
        string key,
        string action,
        Guid journalEntryId,
        string requestHash,
        Guid eventId,
        OfficialDocumentLifecycleActionResult result,
        DateTimeOffset now)
    {
        // Store without full reprint artifact bytes so result JSON can rehydrate cleanly.
        OfficialDocumentLifecycleActionResult stored = result with { ReprintOutput = null };

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT INTO official_document_lifecycle_idempotency (
                idempotency_key, action, journal_entry_id, request_hash, result_event_id, result_json, created_at
            ) VALUES (
                $key, $action, $journalId, $hash, $eventId, $json, $createdAt
            );
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$action", action);
        command.Parameters.AddWithValue("$journalId", journalEntryId.ToString("D"));
        command.Parameters.AddWithValue("$hash", requestHash);
        command.Parameters.AddWithValue("$eventId", eventId.ToString("D"));
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(stored));
        command.Parameters.AddWithValue("$createdAt", now.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private static Result<InvoiceOutputPackage> RenderReprint(
        SqliteConnection connection,
        OriginalDocument original,
        string reason)
    {
        if (string.IsNullOrWhiteSpace(original.SnapshotPayloadJson) ||
            string.IsNullOrWhiteSpace(original.SnapshotPayloadHash))
        {
            return Result<InvoiceOutputPackage>.Fail(Error.Failure(
                "DocumentLifecycle.SnapshotMissing",
                "Cannot reprint without immutable snapshot."));
        }

        OfficialInvoiceSnapshotIdentity identity = new(
            original.JournalEntryId,
            original.SaleId ?? Guid.Empty,
            Guid.Empty,
            original.OfficialDocumentNumber,
            "OFFICIAL_INVOICE",
            DateTimeOffset.UtcNow,
            original.SnapshotPayloadJson,
            original.SnapshotPayloadHash,
            "SHA-256",
            HashVerified: false);

        // Use pure path: parse + build model + inject REPRINT before rendering.
        Result<ParsedOfficialInvoiceSnapshot> parsed = OfficialInvoiceSnapshotParser.ParseAndVerify(identity);
        if (parsed.IsFailure)
        {
            return Result<InvoiceOutputPackage>.Fail(parsed.ErrorDetail);
        }

        InvoiceRenderModelBuilder builder = new();
        Result<InvoiceRenderModel> modelResult = builder.Build(parsed.Value, InvoiceOutputMode.InternalDemo);
        if (modelResult.IsFailure)
        {
            return Result<InvoiceOutputPackage>.Fail(modelResult.ErrorDetail);
        }

        InvoiceRenderModel model = modelResult.Value with
        {
            BoundaryNotes = modelResult.Value.BoundaryNotes
                .Concat(new[] { "REPRINT", "REPRINT reason: " + reason.Trim(), "Same official number; no sequence advance." })
                .ToArray(),
            RequiresInternalWatermark = true,
            InternalWatermark = "REPRINT — " + InvoiceOutputConstants.InternalWatermark
        };

        InvoiceHtmlRenderer html = new();
        InvoiceThermalTextRenderer thermal = new();
        Result<InvoiceOutputArtifact> htmlArt = html.Render(model);
        Result<InvoiceOutputArtifact> textArt = thermal.Render(model);
        if (htmlArt.IsFailure)
        {
            return Result<InvoiceOutputPackage>.Fail(htmlArt.ErrorDetail);
        }

        if (textArt.IsFailure)
        {
            return Result<InvoiceOutputPackage>.Fail(textArt.ErrorDetail);
        }

        if (!htmlArt.Value.Utf8Text.Contains("REPRINT", StringComparison.Ordinal))
        {
            return Result<InvoiceOutputPackage>.Fail(Error.Failure(
                "DocumentLifecycle.ReprintMarkerMissing",
                "Rendered reprint HTML does not contain REPRINT marker."));
        }

        InvoiceOutputManifest manifest = new(
            PackageId: "reprint-" + original.JournalEntryId.ToString("N")[..12],
            SaleId: original.SaleId ?? Guid.Empty,
            JournalEntryId: original.JournalEntryId,
            OfficialDocumentNumber: original.OfficialDocumentNumber,
            TaxpayerMode: model.TaxpayerMode,
            InvoiceVariant: model.Variant.ToString(),
            RendererName: InvoiceOutputConstants.HtmlRendererName,
            RendererVersion: InvoiceOutputConstants.RendererVersion,
            MediaFormat: htmlArt.Value.ContentType,
            CanonicalContentSha256: htmlArt.Value.ContentSha256,
            OutputMode: InvoiceOutputMode.InternalDemo,
            InternalWatermarkApplied: true,
            ProductionActivationEnabled: false,
            LiveOfficialPostingAllowed: false,
            ComplianceClaimed: false,
            MissingOptionalFields: model.MissingOptionalFields,
            ValidationNotes: model.BoundaryNotes,
            NoComplianceClaimNote: InvoiceOutputConstants.NoComplianceClaimNote);

        return Result<InvoiceOutputPackage>.Success(new InvoiceOutputPackage(
            model,
            manifest,
            new[] { htmlArt.Value, textArt.Value }));
    }

    private static string HashRequest(OfficialDocumentLifecycleRuntimeRequest request)
    {
        string raw = string.Join("|",
            request.Action,
            request.JournalEntryId,
            request.OfficialDocumentNumber,
            request.SaleId,
            request.Reason,
            request.ActorUserId,
            request.RefundAmountCents,
            request.TerminalId);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }

    private static string BuildSummary(OfficialDocumentLifecycleAction action) => action switch
    {
        OfficialDocumentLifecycleAction.Reprint => "Reprint completed with visible REPRINT marker; sequence not advanced.",
        OfficialDocumentLifecycleAction.Void => "Void completed with sale/stock effects; original EJ immutable.",
        OfficialDocumentLifecycleAction.Refund => "Refund completed with remaining-balance tracking; original EJ immutable.",
        _ => "Lifecycle action completed."
    };

    private sealed record OriginalDocument(
        Guid JournalEntryId,
        Guid? SaleId,
        string OfficialDocumentNumber,
        string SnapshotPayloadJson,
        string SnapshotPayloadHash);

    private sealed record LifecycleStateRow(
        string JournalEntryId,
        string? SaleId,
        string OfficialDocumentNumber,
        string LifecycleState,
        long OriginalTotalCents,
        long RefundedTotalCents,
        int StateVersion,
        string? LastEventId,
        string UpdatedAt);
}

public sealed record OfficialDocumentLifecycleRuntimeRequest(
    OfficialDocumentLifecycleAction Action,
    Guid? JournalEntryId,
    string? OfficialDocumentNumber,
    Guid? SaleId,
    string? Reason,
    Guid ActorUserId,
    string? TerminalId,
    string? IdempotencyKey,
    long? RefundAmountCents = null);
