using System.Globalization;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;

namespace Salryn.Infrastructure.Data.Services;

public sealed class SqliteOfficialEvidenceReadService : IOfficialEvidenceReadService
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly OfficialEvidenceExportBuilder _exportBuilder;
    private readonly Func<DateTimeOffset> _clock;

    public SqliteOfficialEvidenceReadService(
        SqliteConnectionFactory connectionFactory,
        OfficialEvidenceExportBuilder? exportBuilder = null,
        Func<DateTimeOffset>? clock = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _exportBuilder = exportBuilder ?? new OfficialEvidenceExportBuilder();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public Task<Result<OfficialEvidenceExportResult>> ExportAsync(
        OfficialEvidenceReadRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Result validation = OfficialEvidenceExportBuilder.ValidateRequest(request);

        if (validation.IsFailure)
        {
            return Task.FromResult(Result<OfficialEvidenceExportResult>.Fail(validation.ErrorDetail));
        }

        try
        {
            IReadOnlyList<OfficialEvidenceEntryReadModel> entries =
                ReadEntries(request, cancellationToken);

            return Task.FromResult(_exportBuilder.Build(
                request,
                entries,
                _clock()));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException)
        {
            return Task.FromResult(Result<OfficialEvidenceExportResult>.Fail(Error.Unexpected(
                "OfficialEvidence.SqliteReadFailed",
                ex.Message)));
        }
    }

    private IReadOnlyList<OfficialEvidenceEntryReadModel> ReadEntries(
        OfficialEvidenceReadRequest request,
        CancellationToken cancellationToken)
    {
        using SqliteConnection connection = _connectionFactory.CreateConnection();
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        // Kirch: why - official evidence export uses half-open ranges so adjacent daily readings do not double-count boundary evidence.
        command.CommandText = """
            SELECT
                e.id,
                e.sale_id,
                e.bir_machine_profile_id,
                e.document_kind,
                e.official_document_number,
                e.official_document_sequence_id,
                e.snapshot_payload_json,
                e.snapshot_payload_hash,
                e.snapshot_hash_algorithm,
                ev.id,
                e.created_at,
                e.cashier_user_id,
                e.terminal_id
            FROM bir_electronic_journal_entries e
            INNER JOIN bir_electronic_journal_entry_events ev
                ON ev.journal_entry_id = e.id
               AND ev.event_type = 'ORIGINAL'
               AND ev.original_marker = 1
            INNER JOIN bir_machine_profiles m
                ON m.id = e.bir_machine_profile_id
            WHERE e.created_at >= $rangeStartedAt
              AND e.created_at < $rangeEndedAt
              AND e.document_kind = $documentKind
              AND ($machineProfileId IS NULL OR e.bir_machine_profile_id = $machineProfileId)
              AND ($scopeCode IS NULL OR m.registration_scope_code = $scopeCode)
            ORDER BY
                e.created_at ASC,
                e.official_document_number ASC,
                e.id ASC;
            """;
        command.Parameters.AddWithValue("$rangeStartedAt", request.RangeStartedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$rangeEndedAt", request.RangeEndedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$documentKind", ToDatabaseDocumentKind(request.DocumentKind));
        command.Parameters.AddWithValue("$machineProfileId", request.BirMachineProfileId?.ToString("D") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$scopeCode", string.IsNullOrWhiteSpace(request.RegistrationScopeCode) ? DBNull.Value : request.RegistrationScopeCode.Trim());

        using SqliteDataReader reader = command.ExecuteReader();
        List<OfficialEvidenceEntryReadModel> entries = new();

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            entries.Add(new OfficialEvidenceEntryReadModel(
                JournalEntryId: Guid.Parse(reader.GetString(0)),
                SaleId: Guid.Parse(reader.GetString(1)),
                BirMachineProfileId: Guid.Parse(reader.GetString(2)),
                DocumentKind: FromDatabaseDocumentKind(reader.GetString(3)),
                OfficialDocumentNumber: reader.GetString(4),
                OfficialDocumentSequenceId: Guid.Parse(reader.GetString(5)),
                SnapshotPayloadJson: ReadNullableString(reader, 6),
                SnapshotPayloadHash: ReadNullableString(reader, 7),
                SnapshotHashAlgorithm: ReadNullableString(reader, 8),
                SnapshotHashMatches: false,
                RecomputedSnapshotPayloadHash: string.Empty,
                OriginalEventId: Guid.Parse(reader.GetString(9)),
                AppendedAt: DateTimeOffset.Parse(reader.GetString(10), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                CashierUserId: Guid.Parse(reader.GetString(11)),
                TerminalId: ReadNullableOptionalString(reader, 12)));
        }

        return entries;
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

        string value = reader.GetString(ordinal);

        return string.IsNullOrWhiteSpace(value)
            ? null
            : value;
    }

    private static string ToDatabaseDocumentKind(ElectronicJournalDocumentKind documentKind)
    {
        return documentKind switch
        {
            ElectronicJournalDocumentKind.OfficialReceipt => "OFFICIAL_RECEIPT",
            ElectronicJournalDocumentKind.OfficialInvoice => "OFFICIAL_INVOICE",
            _ => throw new InvalidOperationException("Unsupported official evidence document kind.")
        };
    }

    private static ElectronicJournalDocumentKind FromDatabaseDocumentKind(string documentKind)
    {
        return documentKind switch
        {
            "OFFICIAL_RECEIPT" => ElectronicJournalDocumentKind.OfficialReceipt,
            "OFFICIAL_INVOICE" => ElectronicJournalDocumentKind.OfficialInvoice,
            _ => throw new InvalidOperationException("Unsupported official evidence database document kind.")
        };
    }
}
