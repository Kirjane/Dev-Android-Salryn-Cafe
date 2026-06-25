using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;

namespace Salryn.Infrastructure.Data.Services;

public sealed class SqliteOfficialDocumentAuditEventService : IOfficialDocumentAuditEventPersistence
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteOfficialDocumentAuditEventService(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public Task<Result<OfficialDocumentAuditEventResult>> AppendAsync(
        OfficialDocumentAuditEventPersistenceRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Result validation = ValidateRequest(request);

        if (validation.IsFailure)
        {
            return Task.FromResult(Result<OfficialDocumentAuditEventResult>.Fail(validation.ErrorDetail));
        }

        try
        {
            using SqliteConnection connection = _connectionFactory.CreateConnection();
            connection.Open();

            using SqliteTransaction transaction = connection.BeginTransaction();

            Result<SourceJournalEntry> sourceResult =
                ReadSourceJournalEntry(connection, transaction, request.Request, cancellationToken);

            if (sourceResult.IsFailure)
            {
                transaction.Rollback();
                return Task.FromResult(Result<OfficialDocumentAuditEventResult>.Fail(sourceResult.ErrorDetail));
            }

            SourceJournalEntry source = sourceResult.Value;

            Result sourceValidation = ValidateSourceState(connection, transaction, source, request.Request, cancellationToken);

            if (sourceValidation.IsFailure)
            {
                transaction.Rollback();
                return Task.FromResult(Result<OfficialDocumentAuditEventResult>.Fail(sourceValidation.ErrorDetail));
            }

            InsertAuditEvent(connection, transaction, request, source);

            transaction.Commit();

            return Task.FromResult(Result<OfficialDocumentAuditEventResult>.Success(
                new OfficialDocumentAuditEventResult(
                    request.EventId,
                    source.JournalEntryId,
                    source.OfficialDocumentNumber,
                    request.Request.EventKind,
                    request.Request.Reason!.Trim(),
                    request.CreatedAt)));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException or JsonException)
        {
            return Task.FromResult(Result<OfficialDocumentAuditEventResult>.Fail(Error.Unexpected(
                "OfficialDocumentAuditEvent.SqliteAppendFailed",
                ex.Message)));
        }
    }

    private static Result ValidateRequest(OfficialDocumentAuditEventPersistenceRequest? request)
    {
        if (request is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentAuditEvent.PersistenceRequestRequired",
                "Official document audit event persistence request is required."));
        }

        if (request.EventId == Guid.Empty ||
            request.CreatedAt == default ||
            request.Request is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentAuditEvent.InvalidPersistenceRequest",
                "Official document audit event persistence request identity fields are invalid."));
        }

        return OfficialDocumentAuditEventService.ValidateRequest(request.Request);
    }

    private static Result<SourceJournalEntry> ReadSourceJournalEntry(
        SqliteConnection connection,
        SqliteTransaction transaction,
        OfficialDocumentAuditEventRequest request,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                e.id,
                e.bir_machine_profile_id,
                m.registration_scope_code,
                e.document_kind,
                e.official_document_number,
                e.transaction_timestamp,
                e.snapshot_payload_hash
            FROM bir_electronic_journal_entries e
            INNER JOIN bir_machine_profiles m
                ON m.id = e.bir_machine_profile_id
            WHERE ($journalEntryId IS NULL OR e.id = $journalEntryId)
              AND ($documentNumber IS NULL OR e.official_document_number = $documentNumber)
              AND ($documentKind IS NULL OR e.document_kind = $documentKind)
              AND ($machineProfileId IS NULL OR e.bir_machine_profile_id = $machineProfileId)
              AND ($scopeCode IS NULL OR m.registration_scope_code = $scopeCode)
            ORDER BY e.created_at ASC, e.id ASC
            LIMIT 2;
            """;
        command.Parameters.AddWithValue("$journalEntryId", request.JournalEntryId?.ToString("D") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$documentNumber", NormalizeOptional(request.OfficialDocumentNumber) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$documentKind", request.DocumentKind.HasValue ? ToDatabaseDocumentKind(request.DocumentKind.Value) : (object)DBNull.Value);
        command.Parameters.AddWithValue("$machineProfileId", request.BirMachineProfileId?.ToString("D") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$scopeCode", NormalizeOptional(request.RegistrationScopeCode) ?? (object)DBNull.Value);

        using SqliteDataReader reader = command.ExecuteReader();
        List<SourceJournalEntry> matches = new();

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            matches.Add(new SourceJournalEntry(
                JournalEntryId: Guid.Parse(reader.GetString(0)),
                BirMachineProfileId: Guid.Parse(reader.GetString(1)),
                RegistrationScopeCode: ReadNullableOptionalString(reader, 2),
                DocumentKind: FromDatabaseDocumentKind(reader.GetString(3)),
                OfficialDocumentNumber: ReadNullableString(reader, 4),
                TransactionTimestamp: DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                SnapshotPayloadHash: ReadNullableString(reader, 6)));
        }

        return matches.Count switch
        {
            0 => Result<SourceJournalEntry>.Fail(Error.NotFound(
                "OfficialDocumentAuditEvent.JournalEntryNotFound",
                "Official journal entry was not found for the requested audit event.")),
            > 1 => Result<SourceJournalEntry>.Fail(Error.Conflict(
                "OfficialDocumentAuditEvent.AmbiguousDocumentReference",
                "Official document audit event reference matched more than one journal entry.")),
            _ => Result<SourceJournalEntry>.Success(matches[0])
        };
    }

    private static Result ValidateSourceState(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SourceJournalEntry source,
        OfficialDocumentAuditEventRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(source.OfficialDocumentNumber))
        {
            return Result.Fail(Error.Failure(
                "OfficialDocumentAuditEvent.OfficialDocumentNumberMissing",
                "Official journal entry is missing its official document number."));
        }

        if (string.IsNullOrWhiteSpace(source.SnapshotPayloadHash))
        {
            return Result.Fail(Error.Failure(
                "OfficialDocumentAuditEvent.OfficialEvidenceMissing",
                "Official journal entry is missing snapshot hash evidence."));
        }

        if (!OriginalEventExists(connection, transaction, source.JournalEntryId))
        {
            return Result.Fail(Error.Failure(
                "OfficialDocumentAuditEvent.OriginalEventMissing",
                "Official journal entry has no ORIGINAL event."));
        }

        Result terminalGuard = GuardTerminalEvents(connection, transaction, source.JournalEntryId, request.EventKind);

        if (terminalGuard.IsFailure)
        {
            return terminalGuard;
        }

        if (IsCoveredByFinalizedZReading(connection, transaction, source))
        {
            return Result.Fail(Error.Conflict(
                "OfficialDocumentAuditEvent.PostZReadingChangeDeferred",
                "Official document audit events after a finalized Z-reading require a later adjustment design."));
        }

        return Result.Success();
    }

    private static bool OriginalEventExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid journalEntryId)
    {
        return CountLong(
            connection,
            transaction,
            """
            SELECT COUNT(*)
            FROM bir_electronic_journal_entry_events
            WHERE journal_entry_id = $journalEntryId
              AND event_type = 'ORIGINAL'
              AND original_marker = 1;
            """,
            command => command.Parameters.AddWithValue("$journalEntryId", journalEntryId.ToString("D"))) > 0;
    }

    private static Result GuardTerminalEvents(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid journalEntryId,
        OfficialDocumentAuditEventKind eventKind)
    {
        string? existingTerminalEvent = ReadScalarString(
            connection,
            transaction,
            """
            SELECT event_type
            FROM bir_electronic_journal_entry_events
            WHERE journal_entry_id = $journalEntryId
              AND (
                    event_type IN ('VOID', 'CANCEL')
                    OR void_marker = 1
                    OR cancel_marker = 1
                  )
            ORDER BY created_at ASC, id ASC
            LIMIT 1;
            """,
            command => command.Parameters.AddWithValue("$journalEntryId", journalEntryId.ToString("D")));

        if (string.IsNullOrWhiteSpace(existingTerminalEvent))
        {
            return Result.Success();
        }

        if (eventKind == OfficialDocumentAuditEventKind.Void &&
            string.Equals(existingTerminalEvent, "VOID", StringComparison.Ordinal))
        {
            return Result.Fail(Error.Conflict(
                "OfficialDocumentAuditEvent.AlreadyVoided",
                "Official journal entry has already been voided."));
        }

        if (eventKind == OfficialDocumentAuditEventKind.Cancel &&
            string.Equals(existingTerminalEvent, "CANCEL", StringComparison.Ordinal))
        {
            return Result.Fail(Error.Conflict(
                "OfficialDocumentAuditEvent.AlreadyCancelled",
                "Official journal entry has already been cancelled."));
        }

        if (eventKind is OfficialDocumentAuditEventKind.Void or OfficialDocumentAuditEventKind.Cancel)
        {
            return Result.Fail(Error.Conflict(
                "OfficialDocumentAuditEvent.TerminalEventAlreadyExists",
                "Official journal entry already has a terminal void or cancel event."));
        }

        return Result.Success();
    }

    private static bool IsCoveredByFinalizedZReading(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SourceJournalEntry source)
    {
        return CountLong(
            connection,
            transaction,
            """
            SELECT COUNT(*)
            FROM bir_z_readings
            WHERE status = 'FINALIZED'
              AND document_kind = $documentKind
              AND range_started_at <= $transactionTimestamp
              AND range_ended_at > $transactionTimestamp
              AND (
                    COALESCE(bir_machine_profile_id, '') = $machineProfileId
                    OR
                    ($scopeCode <> '' AND COALESCE(registration_scope_code, '') = $scopeCode)
                  );
            """,
            command =>
            {
                command.Parameters.AddWithValue("$documentKind", ToDatabaseDocumentKind(source.DocumentKind));
                command.Parameters.AddWithValue("$transactionTimestamp", source.TransactionTimestamp.ToString("O", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$machineProfileId", source.BirMachineProfileId.ToString("D"));
                command.Parameters.AddWithValue("$scopeCode", NormalizeOptional(source.RegistrationScopeCode) ?? string.Empty);
            }) > 0;
    }

    private static void InsertAuditEvent(
        SqliteConnection connection,
        SqliteTransaction transaction,
        OfficialDocumentAuditEventPersistenceRequest request,
        SourceJournalEntry source)
    {
        EventDatabaseShape shape = ToEventDatabaseShape(request.Request.EventKind);

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        // Kirch: why - audit events are appended beside the ORIGINAL event; the official journal entry and snapshot hash are never rewritten.
        command.CommandText = """
            INSERT INTO bir_electronic_journal_entry_events (
                id,
                journal_entry_id,
                event_type,
                original_marker,
                reprint_marker,
                void_marker,
                refund_marker,
                cancel_marker,
                cashier_user_id,
                reason,
                preservation_batch_id,
                created_at,
                metadata_json
            )
            VALUES (
                $id,
                $journalEntryId,
                $eventType,
                0,
                0,
                $voidMarker,
                0,
                $cancelMarker,
                $cashierUserId,
                $reason,
                NULL,
                $createdAt,
                $metadataJson
            );
            """;
        command.Parameters.AddWithValue("$id", request.EventId.ToString("D"));
        command.Parameters.AddWithValue("$journalEntryId", source.JournalEntryId.ToString("D"));
        command.Parameters.AddWithValue("$eventType", shape.EventType);
        command.Parameters.AddWithValue("$voidMarker", shape.VoidMarker);
        command.Parameters.AddWithValue("$cancelMarker", shape.CancelMarker);
        command.Parameters.AddWithValue("$cashierUserId", request.Request.ActorUserId.ToString("D"));
        command.Parameters.AddWithValue("$reason", request.Request.Reason!.Trim());
        command.Parameters.AddWithValue("$createdAt", request.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$metadataJson", BuildMetadataJson(request.Request.TerminalId) ?? (object)DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static long CountLong(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        Action<SqliteCommand> configure)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        configure(command);

        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static string? ReadScalarString(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        Action<SqliteCommand> configure)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        configure(command);

        object? value = command.ExecuteScalar();

        return value?.ToString();
    }

    private static string? BuildMetadataJson(string? terminalId)
    {
        string? normalizedTerminalId = NormalizeOptional(terminalId);

        if (normalizedTerminalId is null)
        {
            return null;
        }

        return JsonSerializer.Serialize(new AuditEventMetadata(
            Schema: "SALRYN_OFFICIAL_DOCUMENT_AUDIT_EVENT_METADATA_V1",
            TerminalId: normalizedTerminalId));
    }

    private static EventDatabaseShape ToEventDatabaseShape(OfficialDocumentAuditEventKind eventKind)
    {
        return eventKind switch
        {
            OfficialDocumentAuditEventKind.Void => new EventDatabaseShape("VOID", VoidMarker: 1, CancelMarker: 0),
            OfficialDocumentAuditEventKind.Cancel => new EventDatabaseShape("CANCEL", VoidMarker: 0, CancelMarker: 1),
            OfficialDocumentAuditEventKind.Correction => new EventDatabaseShape("CORRECTION", VoidMarker: 0, CancelMarker: 0),
            _ => throw new InvalidOperationException("Unsupported official document audit event kind.")
        };
    }

    private static string ToDatabaseDocumentKind(ElectronicJournalDocumentKind documentKind)
    {
        return documentKind switch
        {
            ElectronicJournalDocumentKind.OfficialReceipt => "OFFICIAL_RECEIPT",
            ElectronicJournalDocumentKind.OfficialInvoice => "OFFICIAL_INVOICE",
            _ => throw new InvalidOperationException("Unsupported official document audit event document kind.")
        };
    }

    private static ElectronicJournalDocumentKind FromDatabaseDocumentKind(string documentKind)
    {
        return documentKind switch
        {
            "OFFICIAL_RECEIPT" => ElectronicJournalDocumentKind.OfficialReceipt,
            "OFFICIAL_INVOICE" => ElectronicJournalDocumentKind.OfficialInvoice,
            _ => throw new InvalidOperationException("Unsupported official document audit event database document kind.")
        };
    }

    private static string ReadNullableString(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal)
            ? string.Empty
            : reader.GetString(ordinal);
    }

    private static string? ReadNullableOptionalString(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return NormalizeOptional(reader.GetString(ordinal));
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private sealed record SourceJournalEntry(
        Guid JournalEntryId,
        Guid BirMachineProfileId,
        string? RegistrationScopeCode,
        ElectronicJournalDocumentKind DocumentKind,
        string OfficialDocumentNumber,
        DateTimeOffset TransactionTimestamp,
        string SnapshotPayloadHash);

    private sealed record EventDatabaseShape(
        string EventType,
        int VoidMarker,
        int CancelMarker);

    private sealed record AuditEventMetadata(
        string Schema,
        string TerminalId);
}
