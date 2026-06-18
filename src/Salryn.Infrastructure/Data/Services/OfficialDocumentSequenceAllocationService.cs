using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data.Repositories;

namespace Salryn.Infrastructure.Data.Services;

public sealed record OfficialDocumentSequenceAllocationRequest(
    Guid BirMachineProfileId,
    string DocumentKind,
    string SequenceScopeCode,
    int Year);

public sealed record OfficialDocumentSequenceAllocation(
    Guid SequenceId,
    Guid BirMachineProfileId,
    string DocumentKind,
    string SequenceScopeCode,
    string Prefix,
    int Year,
    long Number,
    int PaddingDigits,
    string FormattedDocumentNumber);

public interface IOfficialDocumentSequenceAllocationService
{
    Task<Result<OfficialDocumentSequenceAllocation>> AllocateNextAsync(
        OfficialDocumentSequenceAllocationRequest request,
        CancellationToken cancellationToken = default);
}

// Kirch strict dev guide:
// J21-3B owns official sequence allocation only.
// It advances bir_official_document_sequences.current_number transactionally.
// It does not create sales, print receipts, render receipt/invoice text, write snapshots,
// wire checkout, resume multi-tenant, or claim accreditation readiness.
public sealed class OfficialDocumentSequenceAllocationService :
    SqliteRepositoryBase,
    IOfficialDocumentSequenceAllocationService
{
    public OfficialDocumentSequenceAllocationService(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    public Task<Result<OfficialDocumentSequenceAllocation>> AllocateNextAsync(
        OfficialDocumentSequenceAllocationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Result validation = ValidateRequest(request);

        if (validation.IsFailure)
        {
            return Task.FromResult(Result<OfficialDocumentSequenceAllocation>.Fail(validation.ErrorDetail));
        }

        string documentKind = NormalizeDocumentKind(request.DocumentKind);
        string sequenceScopeCode = NormalizeScope(request.SequenceScopeCode);

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            // Kirch strict dev guide:
            // This is the machine/profile gate before official number consumption.
            // A dev/QA or not-accredited profile must not consume official sequence ranges.
            if (!HasAccreditedActiveMachineProfile(connection, transaction, request.BirMachineProfileId))
            {
                transaction.Rollback();

                return Task.FromResult(Result<OfficialDocumentSequenceAllocation>.Fail(Error.Forbidden(
                    "OfficialSequence.MachineProfileNotReady",
                    "Official sequence allocation requires an active accredited machine profile with official output enabled.")));
            }

            ActiveSequence? sequence = GetActiveSequence(
                connection,
                transaction,
                request.BirMachineProfileId,
                documentKind,
                sequenceScopeCode,
                request.Year);

            if (sequence is null)
            {
                transaction.Rollback();

                return Task.FromResult(Result<OfficialDocumentSequenceAllocation>.Fail(Error.NotFound(
                    "OfficialSequence.ActiveSequenceNotFound",
                    "No active official document sequence was found for the requested machine profile, document kind, scope, and year.")));
            }

            long nextNumber = checked(sequence.CurrentNumber + 1);

            if (sequence.EndNumber.HasValue && nextNumber > sequence.EndNumber.Value)
            {
                transaction.Rollback();

                return Task.FromResult(Result<OfficialDocumentSequenceAllocation>.Fail(Error.Conflict(
                    "OfficialSequence.SequenceExhausted",
                    "Official document sequence range is exhausted.")));
            }

            Result<ReceiptNumber> formattedNumber = ReceiptNumber.Create(
                sequence.Prefix,
                sequence.Year,
                nextNumber,
                sequence.PaddingDigits);

            if (formattedNumber.IsFailure)
            {
                transaction.Rollback();

                return Task.FromResult(Result<OfficialDocumentSequenceAllocation>.Fail(Error.Conflict(
                    "OfficialSequence.InvalidSequenceFormat",
                    "Official document sequence configuration cannot produce a deterministic padded number.")));
            }

            int affectedRows = AdvanceSequence(
                connection,
                transaction,
                sequence,
                nextNumber,
                DateTimeOffset.UtcNow);

            if (affectedRows != 1)
            {
                transaction.Rollback();

                return Task.FromResult(Result<OfficialDocumentSequenceAllocation>.Fail(Error.Conflict(
                    "OfficialSequence.ConcurrentAllocationConflict",
                    "Official document sequence changed during allocation. Retry the allocation.")));
            }

            transaction.Commit();

            OfficialDocumentSequenceAllocation allocation = new(
                sequence.Id,
                request.BirMachineProfileId,
                documentKind,
                sequenceScopeCode,
                sequence.Prefix,
                sequence.Year,
                nextNumber,
                sequence.PaddingDigits,
                formattedNumber.Value.Value);

            return Task.FromResult(Result<OfficialDocumentSequenceAllocation>.Success(allocation));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException or OverflowException)
        {
            return Task.FromResult(Unexpected<OfficialDocumentSequenceAllocation>(
                "OfficialSequence.AllocationFailed",
                ex));
        }
    }

    private static Result ValidateRequest(OfficialDocumentSequenceAllocationRequest request)
    {
        if (request is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialSequence.RequestRequired",
                "Official sequence allocation request is required."));
        }

        if (request.BirMachineProfileId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "OfficialSequence.MachineProfileRequired",
                "BIR machine profile ID is required."));
        }

        string documentKind = NormalizeDocumentKind(request.DocumentKind);

        if (documentKind is not "OFFICIAL_RECEIPT" and not "OFFICIAL_INVOICE")
        {
            return Result.Fail(Error.Validation(
                "OfficialSequence.InvalidDocumentKind",
                "Official document kind must be OFFICIAL_RECEIPT or OFFICIAL_INVOICE."));
        }

        if (string.IsNullOrWhiteSpace(request.SequenceScopeCode))
        {
            return Result.Fail(Error.Validation(
                "OfficialSequence.ScopeRequired",
                "Official sequence scope code is required."));
        }

        if (request.Year < 2000 || request.Year > 9999)
        {
            return Result.Fail(Error.Validation(
                "OfficialSequence.InvalidYear",
                "Official sequence year is invalid."));
        }

        return Result.Success();
    }

    private static bool HasAccreditedActiveMachineProfile(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid profileId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM bir_machine_profiles
            WHERE id = $profileId
              AND is_active = 1
              AND compliance_mode = 'ACCREDITED'
              AND official_output_enabled = 1;
            """;
        command.Parameters.AddWithValue("$profileId", profileId.ToString("D"));

        long count = Convert.ToInt64(command.ExecuteScalar());

        return count == 1;
    }

    private static ActiveSequence? GetActiveSequence(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid profileId,
        string documentKind,
        string sequenceScopeCode,
        int year)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                id,
                prefix,
                year,
                current_number,
                end_number,
                padding_digits
            FROM bir_official_document_sequences
            WHERE bir_machine_profile_id = $profileId
              AND document_kind = $documentKind
              AND sequence_scope_code = $sequenceScopeCode
              AND year = $year
              AND is_active = 1
              AND is_exhausted = 0
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$profileId", profileId.ToString("D"));
        command.Parameters.AddWithValue("$documentKind", documentKind);
        command.Parameters.AddWithValue("$sequenceScopeCode", sequenceScopeCode);
        command.Parameters.AddWithValue("$year", year);

        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read())
        {
            return null;
        }

        long? endNumber = reader.IsDBNull(4)
            ? null
            : reader.GetInt64(4);

        return new ActiveSequence(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.GetInt64(3),
            endNumber,
            reader.GetInt32(5));
    }

    private static int AdvanceSequence(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ActiveSequence sequence,
        long nextNumber,
        DateTimeOffset updatedAt)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE bir_official_document_sequences
            SET
                current_number = $nextNumber,
                updated_at = $updatedAt,
                is_exhausted = CASE
                    WHEN end_number IS NOT NULL AND $nextNumber >= end_number THEN 1
                    ELSE 0
                END,
                is_active = CASE
                    WHEN end_number IS NOT NULL AND $nextNumber >= end_number THEN 0
                    ELSE is_active
                END,
                deactivated_at = CASE
                    WHEN end_number IS NOT NULL AND $nextNumber >= end_number THEN $updatedAt
                    ELSE deactivated_at
                END
            WHERE id = $sequenceId
              AND current_number = $currentNumber
              AND is_active = 1
              AND is_exhausted = 0;
            """;
        command.Parameters.AddWithValue("$sequenceId", sequence.Id.ToString("D"));
        command.Parameters.AddWithValue("$currentNumber", sequence.CurrentNumber);
        command.Parameters.AddWithValue("$nextNumber", nextNumber);
        command.Parameters.AddWithValue("$updatedAt", updatedAt.ToString("O"));

        return command.ExecuteNonQuery();
    }

    private static string NormalizeDocumentKind(string? documentKind)
    {
        return string.IsNullOrWhiteSpace(documentKind)
            ? string.Empty
            : documentKind.Trim().ToUpperInvariant();
    }

    private static string NormalizeScope(string sequenceScopeCode)
    {
        return sequenceScopeCode.Trim().ToUpperInvariant();
    }

    private sealed record ActiveSequence(
        Guid Id,
        string Prefix,
        int Year,
        long CurrentNumber,
        long? EndNumber,
        int PaddingDigits);
}