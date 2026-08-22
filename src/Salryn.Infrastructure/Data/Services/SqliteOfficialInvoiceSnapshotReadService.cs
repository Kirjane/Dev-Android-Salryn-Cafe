using System.Globalization;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Services.InvoiceOutput;

namespace Salryn.Infrastructure.Data.Services;

/// <summary>
/// Reads committed EJ snapshot payload for invoice output. Read-only. No sequence/sale mutation.
/// </summary>
public sealed class SqliteOfficialInvoiceSnapshotReadService : IOfficialInvoiceSnapshotReadService
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteOfficialInvoiceSnapshotReadService(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public Task<Result<OfficialInvoiceSnapshotIdentity>> GetBySaleIdAsync(
        Guid saleId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (saleId == Guid.Empty)
        {
            return Task.FromResult(Result<OfficialInvoiceSnapshotIdentity>.Fail(Error.Validation(
                "InvoiceSnapshot.SaleIdRequired",
                "Sale ID is required.")));
        }

        return Task.FromResult(ReadOne(
            """
            SELECT e.id, e.sale_id, e.bir_machine_profile_id, e.official_document_number, e.document_kind,
                   e.created_at, e.snapshot_payload_json, e.snapshot_payload_hash, e.snapshot_hash_algorithm
            FROM bir_electronic_journal_entries e
            WHERE e.sale_id = $saleId
            ORDER BY e.created_at ASC, e.id ASC
            LIMIT 1;
            """,
            ("$saleId", saleId.ToString("D"))));
    }

    public Task<Result<OfficialInvoiceSnapshotIdentity>> GetByOfficialDocumentNumberAsync(
        string officialDocumentNumber,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(officialDocumentNumber))
        {
            return Task.FromResult(Result<OfficialInvoiceSnapshotIdentity>.Fail(Error.Validation(
                "InvoiceSnapshot.DocumentNumberRequired",
                "Official document number is required.")));
        }

        return Task.FromResult(ReadOne(
            """
            SELECT e.id, e.sale_id, e.bir_machine_profile_id, e.official_document_number, e.document_kind,
                   e.created_at, e.snapshot_payload_json, e.snapshot_payload_hash, e.snapshot_hash_algorithm
            FROM bir_electronic_journal_entries e
            WHERE e.official_document_number = $doc
            ORDER BY e.created_at ASC, e.id ASC
            LIMIT 1;
            """,
            ("$doc", officialDocumentNumber.Trim())));
    }

    private Result<OfficialInvoiceSnapshotIdentity> ReadOne(
        string sql,
        params (string Name, object Value)[] parameters)
    {
        try
        {
            using SqliteConnection connection = _connectionFactory.CreateConnection();
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            foreach ((string name, object value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return Result<OfficialInvoiceSnapshotIdentity>.Fail(Error.NotFound(
                    "InvoiceSnapshot.NotFound",
                    "No committed electronic-journal snapshot was found for the requested identity."));
            }

            string? payload = reader.IsDBNull(6) ? null : reader.GetString(6);
            string? hash = reader.IsDBNull(7) ? null : reader.GetString(7);
            string? algorithm = reader.IsDBNull(8) ? null : reader.GetString(8);

            if (string.IsNullOrWhiteSpace(payload) || string.IsNullOrWhiteSpace(hash))
            {
                return Result<OfficialInvoiceSnapshotIdentity>.Fail(Error.Validation(
                    "InvoiceSnapshot.Incomplete",
                    "Committed journal entry does not contain a complete snapshot payload/hash."));
            }

            return Result<OfficialInvoiceSnapshotIdentity>.Success(new OfficialInvoiceSnapshotIdentity(
                JournalEntryId: Guid.Parse(reader.GetString(0)),
                SaleId: Guid.Parse(reader.GetString(1)),
                BirMachineProfileId: Guid.Parse(reader.GetString(2)),
                OfficialDocumentNumber: reader.GetString(3),
                DocumentKind: reader.GetString(4),
                AppendedAt: DateTimeOffset.Parse(
                    reader.GetString(5),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind),
                SnapshotPayloadJson: payload,
                SnapshotPayloadHash: hash,
                SnapshotHashAlgorithm: algorithm ?? "SHA-256",
                HashVerified: false));
        }
        catch (Exception ex) when (ex is SqliteException or FormatException or InvalidOperationException)
        {
            return Result<OfficialInvoiceSnapshotIdentity>.Fail(Error.Unexpected(
                "InvoiceSnapshot.ReadFailed",
                "Failed to read committed invoice snapshot."));
        }
    }
}
