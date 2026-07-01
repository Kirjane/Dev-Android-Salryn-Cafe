using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;

namespace Salryn.Infrastructure.Data.Services;

public sealed class SqliteOfficialZReadingReadService : IOfficialZReadingReadService
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteOfficialZReadingReadService(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public Task<Result<OfficialZReadingPersistedRecord>> ReadByIdAsync(
        Guid zReadingId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (zReadingId == Guid.Empty)
        {
            return Task.FromResult(Result<OfficialZReadingPersistedRecord>.Fail(Error.Validation(
                "OfficialZReading.InvalidId",
                "Official Z-reading id is invalid.")));
        }

        try
        {
            using SqliteConnection connection = _connectionFactory.CreateConnection();
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    range_started_at,
                    range_ended_at,
                    finalized_at,
                    bir_machine_profile_id,
                    registration_scope_code,
                    document_kind,
                    entry_count,
                    first_official_document_number,
                    last_official_document_number,
                    gross_sales_cents,
                    subtotal_cents,
                    discount_total_cents,
                    tax_total_cents,
                    net_sales_cents,
                    total_sales_cents,
                    payment_summary_json,
                    tax_mode_summary_json,
                    evidence_manifest_hash,
                    all_snapshot_hashes_match,
                    metadata_json,
                    created_by_user_id,
                    terminal_id
                FROM bir_z_readings
                WHERE id = $id
                  AND status = 'FINALIZED'
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$id", zReadingId.ToString("D"));

            using SqliteDataReader reader = command.ExecuteReader();

            if (!reader.Read())
            {
                return Task.FromResult(Result<OfficialZReadingPersistedRecord>.Fail(Error.NotFound(
                    "OfficialZReading.NotFound",
                    "Official Z-reading was not found.")));
            }

            OfficialZReadingSummary summary = new(
                ZReadingId: zReadingId,
                RangeStartedAt: DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                RangeEndedAt: DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                FinalizedAt: DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                BirMachineProfileId: ReadNullableGuid(reader, 3),
                RegistrationScopeCode: ReadNullableString(reader, 4),
                DocumentKind: ReadDocumentKind(reader.GetString(5)),
                EntryCount: reader.GetInt32(6),
                FirstOfficialDocumentNumber: ReadNullableString(reader, 7),
                LastOfficialDocumentNumber: ReadNullableString(reader, 8),
                GrossSalesCents: reader.GetInt64(9),
                SubtotalCents: reader.GetInt64(10),
                DiscountTotalCents: reader.GetInt64(11),
                TaxTotalCents: reader.GetInt64(12),
                NetSalesCents: reader.GetInt64(13),
                TotalSalesCents: reader.GetInt64(14),
                EvidenceManifestHash: reader.GetString(17),
                AllSnapshotHashesMatch: reader.GetInt32(18) == 1,
                CreatedByUserId: ReadNullableGuid(reader, 20),
                TerminalId: ReadNullableString(reader, 21));

            IReadOnlyList<OfficialXReadingPaymentSummary> payments =
                JsonSerializer.Deserialize<OfficialXReadingPaymentSummary[]>(reader.GetString(15))
                ?? Array.Empty<OfficialXReadingPaymentSummary>();
            IReadOnlyList<OfficialXReadingTaxModeSummary> taxModes =
                JsonSerializer.Deserialize<OfficialXReadingTaxModeSummary[]>(reader.GetString(16))
                ?? Array.Empty<OfficialXReadingTaxModeSummary>();

            Result<OfficialZReadingEnrichmentPayload?> enrichmentResult =
                OfficialZReadingEnrichmentSerializer.TryDeserialize(ReadNullableString(reader, 19));

            if (enrichmentResult.IsFailure)
            {
                return Task.FromResult(Result<OfficialZReadingPersistedRecord>.Fail(enrichmentResult.ErrorDetail));
            }

            return Task.FromResult(Result<OfficialZReadingPersistedRecord>.Success(
                new OfficialZReadingPersistedRecord(
                    summary,
                    payments,
                    taxModes,
                    enrichmentResult.Value)));
        }
        catch (Exception ex) when (ex is SqliteException or JsonException or FormatException)
        {
            return Task.FromResult(Result<OfficialZReadingPersistedRecord>.Fail(Error.Unexpected(
                "OfficialZReading.ReadFailed",
                ex.Message)));
        }
    }

    private static ElectronicJournalDocumentKind ReadDocumentKind(string value)
    {
        return value switch
        {
            "OFFICIAL_RECEIPT" => ElectronicJournalDocumentKind.OfficialReceipt,
            "OFFICIAL_INVOICE" => ElectronicJournalDocumentKind.OfficialInvoice,
            _ => throw new InvalidOperationException("Unsupported official Z-reading document kind.")
        };
    }

    private static Guid? ReadNullableGuid(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return Guid.Parse(reader.GetString(ordinal));
    }

    private static string? ReadNullableString(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }
}
