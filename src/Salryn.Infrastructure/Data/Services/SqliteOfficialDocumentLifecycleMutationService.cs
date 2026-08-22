using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Services;

namespace Salryn.Infrastructure.Data.Services;

/// <summary>
/// Append-only VOID/REFUND/CANCEL lifecycle events on EJ. Never rewrites original journal entry.
/// Never advances official sales sequence.
/// </summary>
public sealed class SqliteOfficialDocumentLifecycleMutationService : IOfficialDocumentLifecycleMutation
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly Func<Guid> _idFactory;
    private readonly Func<DateTimeOffset> _clock;

    public SqliteOfficialDocumentLifecycleMutationService(
        SqliteConnectionFactory connectionFactory,
        Func<Guid>? idFactory = null,
        Func<DateTimeOffset>? clock = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _idFactory = idFactory ?? Guid.NewGuid;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public Task<Result<OfficialDocumentLifecycleMutationResult>> AppendAsync(
        OfficialDocumentLifecycleMutationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request is null)
        {
            return Task.FromResult(Result<OfficialDocumentLifecycleMutationResult>.Fail(Error.Validation(
                "DocumentLifecycle.MutationRequestRequired",
                "Lifecycle mutation request is required.")));
        }

        string eventType = (request.EventType ?? string.Empty).Trim().ToUpperInvariant();
        if (eventType is not ("VOID" or "REFUND" or "CANCEL"))
        {
            return Task.FromResult(Result<OfficialDocumentLifecycleMutationResult>.Fail(Error.Validation(
                "DocumentLifecycle.UnsupportedEventType",
                "Event type must be VOID, REFUND, or CANCEL.")));
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Task.FromResult(Result<OfficialDocumentLifecycleMutationResult>.Fail(Error.Validation(
                "DocumentLifecycle.ReasonRequired",
                "Reason is required for void/refund/cancel.")));
        }

        try
        {
            using SqliteConnection connection = _connectionFactory.CreateConnection();
            connection.Open();
            using SqliteTransaction tx = connection.BeginTransaction();

            Result<SourceRow> source = ReadSource(connection, tx, request);
            if (source.IsFailure)
            {
                tx.Rollback();
                return Task.FromResult(Result<OfficialDocumentLifecycleMutationResult>.Fail(source.ErrorDetail));
            }

            Guid eventId = _idFactory();
            DateTimeOffset now = _clock();
            string metadata = JsonSerializer.Serialize(new
            {
                terminalId = request.TerminalId,
                lifecycle = eventType,
                originalImmutable = true,
                sequenceAdvanced = false,
                engineeringOnly = true
            });

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = tx;
                command.CommandText = """
                    INSERT INTO bir_electronic_journal_entry_events (
                        id, journal_entry_id, event_type,
                        original_marker, reprint_marker, void_marker, refund_marker, cancel_marker,
                        cashier_user_id, reason, preservation_batch_id, created_at, metadata_json
                    ) VALUES (
                        $id, $journalEntryId, $eventType,
                        0,
                        0,
                        $voidMarker,
                        $refundMarker,
                        $cancelMarker,
                        $actor, $reason, NULL, $createdAt, $metadata
                    );
                    """;
                command.Parameters.AddWithValue("$id", eventId.ToString("D"));
                command.Parameters.AddWithValue("$journalEntryId", source.Value.JournalEntryId.ToString("D"));
                command.Parameters.AddWithValue("$eventType", eventType);
                command.Parameters.AddWithValue("$voidMarker", eventType == "VOID" ? 1 : 0);
                command.Parameters.AddWithValue("$refundMarker", eventType == "REFUND" ? 1 : 0);
                command.Parameters.AddWithValue("$cancelMarker", eventType == "CANCEL" ? 1 : 0);
                command.Parameters.AddWithValue("$actor", request.ActorUserId.ToString("D"));
                command.Parameters.AddWithValue("$reason", request.Reason.Trim());
                command.Parameters.AddWithValue("$createdAt", now.ToString("O", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$metadata", metadata);
                command.ExecuteNonQuery();
            }

            tx.Commit();
            return Task.FromResult(Result<OfficialDocumentLifecycleMutationResult>.Success(
                new OfficialDocumentLifecycleMutationResult(
                    eventId,
                    source.Value.JournalEntryId,
                    source.Value.OfficialDocumentNumber,
                    request.Reason.Trim(),
                    request.ActorUserId,
                    now)));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException)
        {
            return Task.FromResult(Result<OfficialDocumentLifecycleMutationResult>.Fail(Error.Unexpected(
                "DocumentLifecycle.MutationFailed",
                "Failed to append lifecycle event.")));
        }
    }

    private static Result<SourceRow> ReadSource(
        SqliteConnection connection,
        SqliteTransaction tx,
        OfficialDocumentLifecycleMutationRequest request)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            SELECT e.id, e.official_document_number
            FROM bir_electronic_journal_entries e
            WHERE ($journalEntryId IS NULL OR e.id = $journalEntryId)
              AND ($documentNumber IS NULL OR e.official_document_number = $documentNumber)
            ORDER BY e.created_at ASC, e.id ASC
            LIMIT 2;
            """;
        command.Parameters.AddWithValue(
            "$journalEntryId",
            request.JournalEntryId?.ToString("D") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue(
            "$documentNumber",
            string.IsNullOrWhiteSpace(request.OfficialDocumentNumber)
                ? DBNull.Value
                : request.OfficialDocumentNumber.Trim());

        using SqliteDataReader reader = command.ExecuteReader();
        List<SourceRow> rows = new();
        while (reader.Read())
        {
            rows.Add(new SourceRow(
                Guid.Parse(reader.GetString(0)),
                reader.IsDBNull(1) ? string.Empty : reader.GetString(1)));
        }

        return rows.Count switch
        {
            0 => Result<SourceRow>.Fail(Error.NotFound(
                "DocumentLifecycle.JournalEntryNotFound",
                "Official journal entry was not found.")),
            > 1 => Result<SourceRow>.Fail(Error.Conflict(
                "DocumentLifecycle.AmbiguousReference",
                "Lifecycle reference matched more than one journal entry.")),
            _ => string.IsNullOrWhiteSpace(rows[0].OfficialDocumentNumber)
                ? Result<SourceRow>.Fail(Error.Failure(
                    "DocumentLifecycle.DocumentNumberMissing",
                    "Journal entry is missing official document number."))
                : Result<SourceRow>.Success(rows[0])
        };
    }

    private sealed record SourceRow(Guid JournalEntryId, string OfficialDocumentNumber);
}
