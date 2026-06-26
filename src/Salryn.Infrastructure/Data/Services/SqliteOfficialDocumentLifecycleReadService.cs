using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;

namespace Salryn.Infrastructure.Data.Services;

public sealed class SqliteOfficialDocumentLifecycleReadService : IOfficialDocumentLifecycleReadService
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteOfficialDocumentLifecycleReadService(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public Task<Result<OfficialDocumentLifecycleResult>> ReadAsync(
        OfficialDocumentLifecycleReadRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Result validation = OfficialDocumentLifecycleReadService.ValidateRequest(request);

        if (validation.IsFailure)
        {
            return Task.FromResult(Result<OfficialDocumentLifecycleResult>.Fail(validation.ErrorDetail));
        }

        OfficialDocumentLifecycleReadRequest normalizedRequest =
            OfficialDocumentLifecycleReadService.NormalizeRequest(request);

        try
        {
            using SqliteConnection connection = _connectionFactory.CreateConnection();
            connection.Open();

            Result<OfficialDocumentLifecycleSource> sourceResult =
                ReadSourceJournalEntry(connection, normalizedRequest, cancellationToken);

            if (sourceResult.IsFailure)
            {
                return Task.FromResult(Result<OfficialDocumentLifecycleResult>.Fail(sourceResult.ErrorDetail));
            }

            OfficialDocumentLifecycleSource source = sourceResult.Value;

            IReadOnlyList<OfficialDocumentLifecycleEventReadModel> events =
                ReadEventTrail(connection, source.JournalEntryId, cancellationToken);

            OfficialDocumentLifecycleZReadingCoverage zReadingCoverage =
                ReadZReadingCoverage(connection, source);

            OfficialDocumentLifecycleResult result =
                OfficialDocumentLifecycleReadService.BuildResult(source, events, zReadingCoverage);

            return Task.FromResult(Result<OfficialDocumentLifecycleResult>.Success(result));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException)
        {
            return Task.FromResult(Result<OfficialDocumentLifecycleResult>.Fail(Error.Unexpected(
                "OfficialDocumentLifecycle.SqliteReadFailed",
                ex.Message)));
        }
    }

    private static Result<OfficialDocumentLifecycleSource> ReadSourceJournalEntry(
        SqliteConnection connection,
        OfficialDocumentLifecycleReadRequest request,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                e.id,
                e.sale_id,
                e.bir_machine_profile_id,
                m.registration_scope_code,
                e.document_kind,
                e.official_document_number,
                e.transaction_timestamp,
                e.cashier_user_id,
                e.terminal_id
            FROM bir_electronic_journal_entries e
            INNER JOIN bir_machine_profiles m
                ON m.id = e.bir_machine_profile_id
            WHERE ($journalEntryId IS NULL OR e.id = $journalEntryId)
              AND ($documentNumber IS NULL OR e.official_document_number = $documentNumber)
              AND ($documentKind IS NULL OR e.document_kind = $documentKind)
              AND ($machineProfileId IS NULL OR e.bir_machine_profile_id = $machineProfileId)
              AND ($scopeCode IS NULL OR m.registration_scope_code = $scopeCode)
            ORDER BY e.created_at ASC, e.official_document_number ASC, e.id ASC
            LIMIT 2;
            """;
        command.Parameters.AddWithValue("$journalEntryId", request.JournalEntryId?.ToString("D") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$documentNumber", NormalizeOptional(request.OfficialDocumentNumber) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$documentKind", request.DocumentKind.HasValue ? ToDatabaseDocumentKind(request.DocumentKind.Value) : (object)DBNull.Value);
        command.Parameters.AddWithValue("$machineProfileId", request.BirMachineProfileId?.ToString("D") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$scopeCode", NormalizeOptional(request.RegistrationScopeCode) ?? (object)DBNull.Value);

        using SqliteDataReader reader = command.ExecuteReader();
        List<OfficialDocumentLifecycleSource> matches = new();

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            matches.Add(new OfficialDocumentLifecycleSource(
                JournalEntryId: Guid.Parse(reader.GetString(0)),
                SaleId: Guid.Parse(reader.GetString(1)),
                BirMachineProfileId: Guid.Parse(reader.GetString(2)),
                RegistrationScopeCode: ReadNullableOptionalString(reader, 3),
                DocumentKind: FromDatabaseDocumentKind(reader.GetString(4)),
                OfficialDocumentNumber: reader.GetString(5),
                TransactionTimestamp: DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                CashierUserId: Guid.Parse(reader.GetString(7)),
                TerminalId: ReadNullableOptionalString(reader, 8)));
        }

        return matches.Count switch
        {
            0 => Result<OfficialDocumentLifecycleSource>.Fail(Error.NotFound(
                "OfficialDocumentLifecycle.JournalEntryNotFound",
                "Official journal entry was not found for the requested lifecycle read.")),
            > 1 => Result<OfficialDocumentLifecycleSource>.Fail(Error.Conflict(
                "OfficialDocumentLifecycle.AmbiguousDocumentReference",
                "Official document lifecycle reference matched more than one journal entry.")),
            _ => Result<OfficialDocumentLifecycleSource>.Success(matches[0])
        };
    }

    private static IReadOnlyList<OfficialDocumentLifecycleEventReadModel> ReadEventTrail(
        SqliteConnection connection,
        Guid journalEntryId,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        // Kirch: why - lifecycle consumers get a deterministic event trail while the source journal rows stay append-only.
        command.CommandText = """
            SELECT
                id,
                event_type,
                original_marker,
                reprint_marker,
                void_marker,
                refund_marker,
                cancel_marker,
                cashier_user_id,
                reason,
                created_at,
                metadata_json
            FROM bir_electronic_journal_entry_events
            WHERE journal_entry_id = $journalEntryId
            ORDER BY created_at ASC, id ASC;
            """;
        command.Parameters.AddWithValue("$journalEntryId", journalEntryId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        List<OfficialDocumentLifecycleEventReadModel> events = new();

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? metadataJson = ReadNullableOptionalString(reader, 10);

            events.Add(new OfficialDocumentLifecycleEventReadModel(
                EventId: Guid.Parse(reader.GetString(0)),
                EventType: reader.GetString(1),
                OriginalMarker: reader.GetInt32(2) == 1,
                ReprintMarker: reader.GetInt32(3) == 1,
                VoidMarker: reader.GetInt32(4) == 1,
                RefundMarker: reader.GetInt32(5) == 1,
                CancelMarker: reader.GetInt32(6) == 1,
                CashierUserId: Guid.Parse(reader.GetString(7)),
                Reason: ReadNullableOptionalString(reader, 8),
                CreatedAt: DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                TerminalId: ReadTerminalId(metadataJson),
                MetadataJson: metadataJson));
        }

        return events;
    }

    private static OfficialDocumentLifecycleZReadingCoverage ReadZReadingCoverage(
        SqliteConnection connection,
        OfficialDocumentLifecycleSource source)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, finalized_at
            FROM bir_z_readings
            WHERE status = 'FINALIZED'
              AND document_kind = $documentKind
              AND range_started_at <= $transactionTimestamp
              AND range_ended_at > $transactionTimestamp
              AND (
                    COALESCE(bir_machine_profile_id, '') = $machineProfileId
                    OR
                    ($scopeCode <> '' AND COALESCE(registration_scope_code, '') = $scopeCode)
                  )
            ORDER BY finalized_at ASC, id ASC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$documentKind", ToDatabaseDocumentKind(source.DocumentKind));
        command.Parameters.AddWithValue("$transactionTimestamp", source.TransactionTimestamp.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$machineProfileId", source.BirMachineProfileId.ToString("D"));
        command.Parameters.AddWithValue("$scopeCode", NormalizeOptional(source.RegistrationScopeCode) ?? string.Empty);

        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read())
        {
            return new OfficialDocumentLifecycleZReadingCoverage(
                IsCovered: false,
                ZReadingId: null,
                FinalizedAt: null);
        }

        return new OfficialDocumentLifecycleZReadingCoverage(
            IsCovered: true,
            ZReadingId: Guid.Parse(reader.GetString(0)),
            FinalizedAt: DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
    }

    private static string? ReadTerminalId(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(metadataJson);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (TryReadString(document.RootElement, "TerminalId", out string? terminalId) ||
                TryReadString(document.RootElement, "terminalId", out terminalId))
            {
                return NormalizeOptional(terminalId);
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static bool TryReadString(
        JsonElement element,
        string propertyName,
        out string? value)
    {
        value = null;

        if (!element.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();

        return true;
    }

    private static string ToDatabaseDocumentKind(ElectronicJournalDocumentKind documentKind)
    {
        return documentKind switch
        {
            ElectronicJournalDocumentKind.OfficialReceipt => "OFFICIAL_RECEIPT",
            ElectronicJournalDocumentKind.OfficialInvoice => "OFFICIAL_INVOICE",
            _ => throw new InvalidOperationException("Unsupported official document lifecycle document kind.")
        };
    }

    private static ElectronicJournalDocumentKind FromDatabaseDocumentKind(string documentKind)
    {
        return documentKind switch
        {
            "OFFICIAL_RECEIPT" => ElectronicJournalDocumentKind.OfficialReceipt,
            "OFFICIAL_INVOICE" => ElectronicJournalDocumentKind.OfficialInvoice,
            _ => throw new InvalidOperationException("Unsupported official document lifecycle database document kind.")
        };
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
}
