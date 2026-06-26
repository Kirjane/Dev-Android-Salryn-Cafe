using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;

namespace Salryn.Infrastructure.Data.Services;

public sealed class SqliteOfficialDocumentReprintService : IOfficialDocumentReprintPersistence
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteOfficialDocumentReprintService(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public Task<Result<OfficialDocumentReprintResult>> AppendAsync(
        OfficialDocumentReprintPersistenceRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Result validation = ValidateRequest(request);

        if (validation.IsFailure)
        {
            return Task.FromResult(Result<OfficialDocumentReprintResult>.Fail(validation.ErrorDetail));
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
                return Task.FromResult(Result<OfficialDocumentReprintResult>.Fail(sourceResult.ErrorDetail));
            }

            SourceJournalEntry source = sourceResult.Value;

            Result sourceValidation = ValidateSourceState(connection, transaction, source);

            if (sourceValidation.IsFailure)
            {
                transaction.Rollback();
                return Task.FromResult(Result<OfficialDocumentReprintResult>.Fail(sourceValidation.ErrorDetail));
            }

            InsertReprintEvent(connection, transaction, request, source);

            transaction.Commit();

            return Task.FromResult(Result<OfficialDocumentReprintResult>.Success(
                new OfficialDocumentReprintResult(
                    request.EventId,
                    source.JournalEntryId,
                    source.OfficialDocumentNumber,
                    request.Request.Reason!.Trim(),
                    request.Request.ActorUserId,
                    request.CreatedAt)));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException or JsonException)
        {
            return Task.FromResult(Result<OfficialDocumentReprintResult>.Fail(Error.Unexpected(
                "OfficialDocumentReprint.SqliteAppendFailed",
                ex.Message)));
        }
    }

    private static Result ValidateRequest(OfficialDocumentReprintPersistenceRequest? request)
    {
        if (request is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentReprint.PersistenceRequestRequired",
                "Official document reprint persistence request is required."));
        }

        if (request.EventId == Guid.Empty ||
            request.CreatedAt == default ||
            request.Request is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentReprint.InvalidPersistenceRequest",
                "Official document reprint persistence request identity fields are invalid."));
        }

        return OfficialDocumentReprintService.ValidateRequest(request.Request);
    }

    private static Result<SourceJournalEntry> ReadSourceJournalEntry(
        SqliteConnection connection,
        SqliteTransaction transaction,
        OfficialDocumentReprintRequest request,
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
                e.snapshot_payload_hash,
                e.snapshot_hash_algorithm
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
                SnapshotPayloadHash: ReadNullableString(reader, 5),
                SnapshotHashAlgorithm: ReadNullableString(reader, 6)));
        }

        return matches.Count switch
        {
            0 => Result<SourceJournalEntry>.Fail(Error.NotFound(
                "OfficialDocumentReprint.JournalEntryNotFound",
                "Official journal entry was not found for the requested reprint.")),
            > 1 => Result<SourceJournalEntry>.Fail(Error.Conflict(
                "OfficialDocumentReprint.AmbiguousDocumentReference",
                "Official document reprint reference matched more than one journal entry.")),
            _ => Result<SourceJournalEntry>.Success(matches[0])
        };
    }

    private static Result ValidateSourceState(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SourceJournalEntry source)
    {
        if (string.IsNullOrWhiteSpace(source.OfficialDocumentNumber))
        {
            return Result.Fail(Error.Failure(
                "OfficialDocumentReprint.OfficialDocumentNumberMissing",
                "Official journal entry is missing its official document number."));
        }

        if (string.IsNullOrWhiteSpace(source.SnapshotPayloadHash) ||
            string.IsNullOrWhiteSpace(source.SnapshotHashAlgorithm))
        {
            return Result.Fail(Error.Failure(
                "OfficialDocumentReprint.OfficialEvidenceMissing",
                "Official journal entry is missing snapshot hash evidence."));
        }

        if (!OriginalEventExists(connection, transaction, source.JournalEntryId))
        {
            return Result.Fail(Error.Failure(
                "OfficialDocumentReprint.OriginalEventMissing",
                "Official journal entry has no ORIGINAL event."));
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

    private static void InsertReprintEvent(
        SqliteConnection connection,
        SqliteTransaction transaction,
        OfficialDocumentReprintPersistenceRequest request,
        SourceJournalEntry source)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        // Kirch: why - REPRINT is an append-only copy marker; it never advances numbering or edits the original journal entry.
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
                'REPRINT',
                0,
                1,
                0,
                0,
                0,
                $cashierUserId,
                $reason,
                NULL,
                $createdAt,
                $metadataJson
            );
            """;
        command.Parameters.AddWithValue("$id", request.EventId.ToString("D"));
        command.Parameters.AddWithValue("$journalEntryId", source.JournalEntryId.ToString("D"));
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

    private static string? BuildMetadataJson(string? terminalId)
    {
        string? normalizedTerminalId = NormalizeOptional(terminalId);

        if (normalizedTerminalId is null)
        {
            return null;
        }

        return JsonSerializer.Serialize(new ReprintEventMetadata(
            Schema: "SALRYN_OFFICIAL_DOCUMENT_REPRINT_METADATA_V1",
            TerminalId: normalizedTerminalId));
    }

    private static string ToDatabaseDocumentKind(ElectronicJournalDocumentKind documentKind)
    {
        return documentKind switch
        {
            ElectronicJournalDocumentKind.OfficialReceipt => "OFFICIAL_RECEIPT",
            ElectronicJournalDocumentKind.OfficialInvoice => "OFFICIAL_INVOICE",
            _ => throw new InvalidOperationException("Unsupported official document reprint document kind.")
        };
    }

    private static ElectronicJournalDocumentKind FromDatabaseDocumentKind(string documentKind)
    {
        return documentKind switch
        {
            "OFFICIAL_RECEIPT" => ElectronicJournalDocumentKind.OfficialReceipt,
            "OFFICIAL_INVOICE" => ElectronicJournalDocumentKind.OfficialInvoice,
            _ => throw new InvalidOperationException("Unsupported official document reprint database document kind.")
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
        string SnapshotPayloadHash,
        string SnapshotHashAlgorithm);

    private sealed record ReprintEventMetadata(
        string Schema,
        string TerminalId);
}
