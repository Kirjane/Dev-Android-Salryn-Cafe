using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;

namespace Salryn.Infrastructure.Data.Services;

public sealed class SqliteOfficialZReadingService : IOfficialZReadingPersistence
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteOfficialZReadingService(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public Task<Result<OfficialZReadingPersistenceResult>> SaveAsync(
        OfficialZReadingPersistenceRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Result validation = ValidateRequest(request);

        if (validation.IsFailure)
        {
            return Task.FromResult(Result<OfficialZReadingPersistenceResult>.Fail(validation.ErrorDetail));
        }

        try
        {
            using SqliteConnection connection = _connectionFactory.CreateConnection();
            connection.Open();

            using SqliteTransaction transaction = connection.BeginTransaction();

            Result guardResult = GuardDuplicateAndOverlap(
                connection,
                transaction,
                request.Summary,
                cancellationToken);

            if (guardResult.IsFailure)
            {
                transaction.Rollback();
                return Task.FromResult(Result<OfficialZReadingPersistenceResult>.Fail(guardResult.ErrorDetail));
            }

            InsertZReading(connection, transaction, request);

            transaction.Commit();

            return Task.FromResult(Result<OfficialZReadingPersistenceResult>.Success(
                new OfficialZReadingPersistenceResult(request.Summary.ZReadingId)));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException or JsonException)
        {
            return Task.FromResult(Result<OfficialZReadingPersistenceResult>.Fail(Error.Unexpected(
                "OfficialZReading.SqlitePersistenceFailed",
                ex.Message)));
        }
    }

    private static Result ValidateRequest(OfficialZReadingPersistenceRequest? request)
    {
        if (request is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialZReading.PersistenceRequestRequired",
                "Official Z-reading persistence request is required."));
        }

        if (request.Summary is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialZReading.SummaryRequired",
                "Official Z-reading summary is required."));
        }

        OfficialZReadingSummary summary = request.Summary;

        if (summary.ZReadingId == Guid.Empty ||
            summary.RangeStartedAt == default ||
            summary.RangeEndedAt == default ||
            summary.RangeEndedAt < summary.RangeStartedAt ||
            summary.FinalizedAt == default)
        {
            return Result.Fail(Error.Validation(
                "OfficialZReading.InvalidSummary",
                "Official Z-reading summary identity and range fields are invalid."));
        }

        if (!summary.BirMachineProfileId.HasValue &&
            string.IsNullOrWhiteSpace(summary.RegistrationScopeCode))
        {
            return Result.Fail(Error.Validation(
                "OfficialZReading.MachineProfileOrScopeRequired",
                "Official Z-reading persistence requires a machine profile id or registration scope."));
        }

        if (summary.BirMachineProfileId.HasValue &&
            summary.BirMachineProfileId.Value == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "OfficialZReading.InvalidMachineProfileId",
                "Official Z-reading machine profile id is invalid."));
        }

        if (!Enum.IsDefined(typeof(ElectronicJournalDocumentKind), summary.DocumentKind))
        {
            return Result.Fail(Error.Validation(
                "OfficialZReading.UnsupportedDocumentKind",
                "Official Z-reading document kind is unsupported."));
        }

        if (summary.EntryCount < 0 ||
            summary.GrossSalesCents < 0 ||
            summary.SubtotalCents < 0 ||
            summary.DiscountTotalCents < 0 ||
            summary.TaxTotalCents < 0 ||
            summary.NetSalesCents < 0 ||
            summary.TotalSalesCents < 0 ||
            string.IsNullOrWhiteSpace(summary.EvidenceManifestHash) ||
            !summary.AllSnapshotHashesMatch)
        {
            return Result.Fail(Error.Validation(
                "OfficialZReading.InvalidSummary",
                "Official Z-reading summary totals or evidence fields are invalid."));
        }

        if ((string.IsNullOrWhiteSpace(summary.FirstOfficialDocumentNumber) &&
             !string.IsNullOrWhiteSpace(summary.LastOfficialDocumentNumber)) ||
            (!string.IsNullOrWhiteSpace(summary.FirstOfficialDocumentNumber) &&
             string.IsNullOrWhiteSpace(summary.LastOfficialDocumentNumber)))
        {
            return Result.Fail(Error.Validation(
                "OfficialZReading.InvalidDocumentRange",
                "Official Z-reading document range must include both first and last numbers or neither."));
        }

        if (request.Payments is null || request.TaxModes is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialZReading.SummaryDetailsRequired",
                "Official Z-reading payment and tax-mode summaries are required."));
        }

        return Result.Success();
    }

    private static Result GuardDuplicateAndOverlap(
        SqliteConnection connection,
        SqliteTransaction transaction,
        OfficialZReadingSummary summary,
        CancellationToken cancellationToken)
    {
        foreach (ExistingZReadingRange existing in ReadExistingRanges(connection, transaction, summary, cancellationToken))
        {
            if (SameInstant(existing.RangeStartedAt, summary.RangeStartedAt) &&
                SameInstant(existing.RangeEndedAt, summary.RangeEndedAt))
            {
                return Result.Fail(Error.Conflict(
                    "OfficialZReading.DuplicateRange",
                    "An official Z-reading already exists for the same machine/profile, document kind, and exact range."));
            }

            if (RangesOverlapInclusive(
                existing.RangeStartedAt,
                existing.RangeEndedAt,
                summary.RangeStartedAt,
                summary.RangeEndedAt))
            {
                return Result.Fail(Error.Conflict(
                    "OfficialZReading.OverlappingRange",
                    "An official Z-reading already exists with an overlapping range for the same machine/profile and document kind."));
            }
        }

        return Result.Success();
    }

    private static IReadOnlyList<ExistingZReadingRange> ReadExistingRanges(
        SqliteConnection connection,
        SqliteTransaction transaction,
        OfficialZReadingSummary summary,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, range_started_at, range_ended_at
            FROM bir_z_readings
            WHERE status = 'FINALIZED'
              AND document_kind = $documentKind
              AND COALESCE(bir_machine_profile_id, '') = $machineProfileKey
              AND COALESCE(registration_scope_code, '') = $scopeKey;
            """;
        command.Parameters.AddWithValue("$documentKind", ToDatabaseDocumentKind(summary.DocumentKind));
        command.Parameters.AddWithValue("$machineProfileKey", summary.BirMachineProfileId?.ToString("D") ?? string.Empty);
        command.Parameters.AddWithValue("$scopeKey", NormalizeOptional(summary.RegistrationScopeCode) ?? string.Empty);

        using SqliteDataReader reader = command.ExecuteReader();
        List<ExistingZReadingRange> ranges = new();

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            ranges.Add(new ExistingZReadingRange(
                Guid.Parse(reader.GetString(0)),
                DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        }

        return ranges;
    }

    private static void InsertZReading(
        SqliteConnection connection,
        SqliteTransaction transaction,
        OfficialZReadingPersistenceRequest request)
    {
        OfficialZReadingSummary summary = request.Summary;
        string paymentSummaryJson = JsonSerializer.Serialize(request.Payments);
        string taxModeSummaryJson = JsonSerializer.Serialize(request.TaxModes);

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        // Kirch: why - this insert records the final Z-reading proof only; original journal evidence remains append-only.
        command.CommandText = """
            INSERT INTO bir_z_readings (
                id,
                bir_machine_profile_id,
                registration_scope_code,
                document_kind,
                range_started_at,
                range_ended_at,
                finalized_at,
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
                created_by_user_id,
                terminal_id,
                status,
                created_at
            )
            VALUES (
                $id,
                $machineProfileId,
                $scopeCode,
                $documentKind,
                $rangeStartedAt,
                $rangeEndedAt,
                $finalizedAt,
                $entryCount,
                $firstDocumentNumber,
                $lastDocumentNumber,
                $grossSales,
                $subtotal,
                $discountTotal,
                $taxTotal,
                $netSales,
                $totalSales,
                $paymentSummary,
                $taxModeSummary,
                $evidenceManifestHash,
                $allSnapshotHashesMatch,
                $createdByUserId,
                $terminalId,
                'FINALIZED',
                $createdAt
            );
            """;
        command.Parameters.AddWithValue("$id", summary.ZReadingId.ToString("D"));
        command.Parameters.AddWithValue("$machineProfileId", summary.BirMachineProfileId?.ToString("D") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$scopeCode", NormalizeOptional(summary.RegistrationScopeCode) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$documentKind", ToDatabaseDocumentKind(summary.DocumentKind));
        command.Parameters.AddWithValue("$rangeStartedAt", summary.RangeStartedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$rangeEndedAt", summary.RangeEndedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$finalizedAt", summary.FinalizedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$entryCount", summary.EntryCount);
        command.Parameters.AddWithValue("$firstDocumentNumber", NormalizeOptional(summary.FirstOfficialDocumentNumber) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$lastDocumentNumber", NormalizeOptional(summary.LastOfficialDocumentNumber) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$grossSales", summary.GrossSalesCents);
        command.Parameters.AddWithValue("$subtotal", summary.SubtotalCents);
        command.Parameters.AddWithValue("$discountTotal", summary.DiscountTotalCents);
        command.Parameters.AddWithValue("$taxTotal", summary.TaxTotalCents);
        command.Parameters.AddWithValue("$netSales", summary.NetSalesCents);
        command.Parameters.AddWithValue("$totalSales", summary.TotalSalesCents);
        command.Parameters.AddWithValue("$paymentSummary", paymentSummaryJson);
        command.Parameters.AddWithValue("$taxModeSummary", taxModeSummaryJson);
        command.Parameters.AddWithValue("$evidenceManifestHash", summary.EvidenceManifestHash);
        command.Parameters.AddWithValue("$allSnapshotHashesMatch", summary.AllSnapshotHashesMatch ? 1 : 0);
        command.Parameters.AddWithValue("$createdByUserId", summary.CreatedByUserId?.ToString("D") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$terminalId", NormalizeOptional(summary.TerminalId) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", summary.FinalizedAt.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private static bool SameInstant(DateTimeOffset left, DateTimeOffset right)
    {
        return left.UtcDateTime == right.UtcDateTime;
    }

    private static bool RangesOverlapInclusive(
        DateTimeOffset existingStart,
        DateTimeOffset existingEnd,
        DateTimeOffset newStart,
        DateTimeOffset newEnd)
    {
        return existingStart.UtcDateTime <= newEnd.UtcDateTime &&
               existingEnd.UtcDateTime >= newStart.UtcDateTime;
    }

    private static string ToDatabaseDocumentKind(ElectronicJournalDocumentKind documentKind)
    {
        return documentKind switch
        {
            ElectronicJournalDocumentKind.OfficialReceipt => "OFFICIAL_RECEIPT",
            ElectronicJournalDocumentKind.OfficialInvoice => "OFFICIAL_INVOICE",
            _ => throw new InvalidOperationException("Unsupported official Z-reading document kind.")
        };
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private sealed record ExistingZReadingRange(
        Guid Id,
        DateTimeOffset RangeStartedAt,
        DateTimeOffset RangeEndedAt);
}
