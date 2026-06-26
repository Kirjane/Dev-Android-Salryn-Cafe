using System.Globalization;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;

namespace Salryn.Infrastructure.Data.Services;

public sealed class SqliteOfficialOutputSourceReadService : IOfficialOutputSourceReadService
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly IOfficialDocumentLifecycleReadService _lifecycleRead;

    public SqliteOfficialOutputSourceReadService(
        SqliteConnectionFactory connectionFactory,
        IOfficialDocumentLifecycleReadService lifecycleRead)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _lifecycleRead = lifecycleRead ?? throw new ArgumentNullException(nameof(lifecycleRead));
    }

    public async Task<Result<OfficialOutputSourceResult>> ReadAsync(
        OfficialOutputSourceReadRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Result validation = OfficialOutputSourceReadService.ValidateRequest(request);

        if (validation.IsFailure)
        {
            return Result<OfficialOutputSourceResult>.Fail(validation.ErrorDetail);
        }

        OfficialOutputSourceReadRequest normalizedRequest =
            OfficialOutputSourceReadService.NormalizeRequest(request);

        try
        {
            using SqliteConnection connection = _connectionFactory.CreateConnection();
            connection.Open();

            Result<OfficialOutputSourceEvidence> sourceResult =
                ReadSourceEvidence(connection, normalizedRequest, cancellationToken);

            if (sourceResult.IsFailure)
            {
                return Result<OfficialOutputSourceResult>.Fail(sourceResult.ErrorDetail);
            }

            OfficialOutputSourceEvidence source = sourceResult.Value;

            Result<Guid> originalEventIdResult =
                ReadOriginalEventId(connection, source.JournalEntryId);

            if (originalEventIdResult.IsFailure)
            {
                return Result<OfficialOutputSourceResult>.Fail(originalEventIdResult.ErrorDetail);
            }

            source = source with
            {
                OriginalEventId = originalEventIdResult.Value
            };

            Result<OfficialDocumentLifecycleResult> lifecycleResult =
                await _lifecycleRead.ReadAsync(new OfficialDocumentLifecycleReadRequest(
                    JournalEntryId: source.JournalEntryId,
                    OfficialDocumentNumber: null,
                    DocumentKind: source.DocumentKind,
                    BirMachineProfileId: source.BirMachineProfileId,
                    RegistrationScopeCode: null), cancellationToken).ConfigureAwait(false);

            if (lifecycleResult.IsFailure)
            {
                return Result<OfficialOutputSourceResult>.Fail(Error.Failure(
                    "OfficialOutputSource.LifecycleReadFailed",
                    lifecycleResult.Error));
            }

            return OfficialOutputSourceReadService.BuildResult(source, lifecycleResult.Value);
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException)
        {
            return Result<OfficialOutputSourceResult>.Fail(Error.Unexpected(
                "OfficialOutputSource.SqliteReadFailed",
                ex.Message));
        }
    }

    private static Result<OfficialOutputSourceEvidence> ReadSourceEvidence(
        SqliteConnection connection,
        OfficialOutputSourceReadRequest request,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        // Kirch: why - output source reads official journal snapshot evidence and machine facts; renderers must not query tables directly.
        command.CommandText = """
            SELECT
                e.id,
                e.sale_id,
                e.bir_machine_profile_id,
                e.official_document_sequence_id,
                e.document_kind,
                e.official_document_number,
                e.transaction_timestamp,
                e.cashier_user_id,
                e.terminal_id,
                e.snapshot_payload_json,
                e.snapshot_payload_hash,
                e.snapshot_hash_algorithm,
                m.registration_scope_code,
                m.registered_taxpayer_name,
                m.business_name,
                m.registered_address,
                m.branch_address,
                m.taxpayer_identification_number,
                m.branch_code,
                m.machine_identification_number,
                m.machine_serial_number,
                m.pos_terminal_number,
                m.accreditation_number,
                m.permit_to_use_number,
                m.software_name,
                m.software_version,
                m.software_build,
                m.compliance_mode
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
        List<OfficialOutputSourceEvidence> matches = new();

        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();

            Guid birMachineProfileId = Guid.Parse(reader.GetString(2));
            string? registrationScopeCode = ReadNullableOptionalString(reader, 12);

            OfficialOutputMachineProfile machineProfile = new(
                BirMachineProfileId: birMachineProfileId,
                RegistrationScopeCode: registrationScopeCode,
                RegisteredTaxpayerName: ReadRequiredString(reader, 13),
                BusinessName: ReadNullableOptionalString(reader, 14),
                RegisteredAddress: ReadRequiredString(reader, 15),
                BranchAddress: ReadNullableOptionalString(reader, 16),
                TaxpayerIdentificationNumber: ReadRequiredString(reader, 17),
                BranchCode: ReadRequiredString(reader, 18),
                MachineIdentificationNumber: ReadNullableOptionalString(reader, 19),
                MachineSerialNumber: ReadNullableOptionalString(reader, 20),
                PosTerminalNumber: ReadNullableOptionalString(reader, 21),
                AccreditationNumber: ReadNullableOptionalString(reader, 22),
                PermitToUseNumber: ReadNullableOptionalString(reader, 23),
                SoftwareName: ReadNullableOptionalString(reader, 24),
                SoftwareVersion: ReadNullableOptionalString(reader, 25),
                SoftwareBuild: ReadNullableOptionalString(reader, 26),
                ComplianceMode: ReadNullableOptionalString(reader, 27));

            matches.Add(new OfficialOutputSourceEvidence(
                JournalEntryId: Guid.Parse(reader.GetString(0)),
                SaleId: Guid.Parse(reader.GetString(1)),
                BirMachineProfileId: birMachineProfileId,
                OfficialDocumentSequenceId: Guid.Parse(reader.GetString(3)),
                DocumentKind: FromDatabaseDocumentKind(reader.GetString(4)),
                OfficialDocumentNumber: ReadRequiredString(reader, 5),
                TransactionTimestamp: DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                CashierUserId: Guid.Parse(reader.GetString(7)),
                TerminalId: ReadNullableOptionalString(reader, 8),
                SnapshotPayloadJson: ReadNullableString(reader, 9),
                SnapshotPayloadHash: ReadNullableString(reader, 10),
                SnapshotHashAlgorithm: ReadNullableString(reader, 11),
                OriginalEventId: Guid.Empty,
                MachineProfile: machineProfile));
        }

        return matches.Count switch
        {
            0 => Result<OfficialOutputSourceEvidence>.Fail(Error.NotFound(
                "OfficialOutputSource.JournalEntryNotFound",
                "Official journal entry was not found for the requested output source.")),
            > 1 => Result<OfficialOutputSourceEvidence>.Fail(Error.Conflict(
                "OfficialOutputSource.AmbiguousDocumentReference",
                "Official output source reference matched more than one journal entry.")),
            _ => Result<OfficialOutputSourceEvidence>.Success(matches[0])
        };
    }

    private static Result<Guid> ReadOriginalEventId(
        SqliteConnection connection,
        Guid journalEntryId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id
            FROM bir_electronic_journal_entry_events
            WHERE journal_entry_id = $journalEntryId
              AND event_type = 'ORIGINAL'
              AND original_marker = 1
            ORDER BY created_at ASC, id ASC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$journalEntryId", journalEntryId.ToString("D"));

        object? value = command.ExecuteScalar();

        if (value is null)
        {
            return Result<Guid>.Fail(Error.Failure(
                "OfficialOutputSource.OriginalEventMissing",
                "Official journal entry has no ORIGINAL event."));
        }

        return Result<Guid>.Success(Guid.Parse(value.ToString()!));
    }

    private static string ToDatabaseDocumentKind(ElectronicJournalDocumentKind documentKind)
    {
        return documentKind switch
        {
            ElectronicJournalDocumentKind.OfficialReceipt => "OFFICIAL_RECEIPT",
            ElectronicJournalDocumentKind.OfficialInvoice => "OFFICIAL_INVOICE",
            _ => throw new InvalidOperationException("Unsupported official output source document kind.")
        };
    }

    private static ElectronicJournalDocumentKind FromDatabaseDocumentKind(string documentKind)
    {
        return documentKind switch
        {
            "OFFICIAL_RECEIPT" => ElectronicJournalDocumentKind.OfficialReceipt,
            "OFFICIAL_INVOICE" => ElectronicJournalDocumentKind.OfficialInvoice,
            _ => throw new InvalidOperationException("Unsupported official output source database document kind.")
        };
    }

    private static string ReadRequiredString(SqliteDataReader reader, int ordinal)
    {
        string value = ReadNullableString(reader, ordinal);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Official output source database text field is required.");
        }

        return value.Trim();
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
}
