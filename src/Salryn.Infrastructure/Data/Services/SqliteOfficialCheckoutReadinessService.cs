using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Services;

namespace Salryn.Infrastructure.Data.Services;

// Kirch: why - official checkout must fail closed before sequence, sale, or journal rows are touched.
public sealed class SqliteOfficialCheckoutReadinessService : IOfficialCheckoutReadinessService
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteOfficialCheckoutReadinessService(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public Task<Result<OfficialCheckoutReadinessResult>> CheckAsync(
        OfficialCheckoutReadinessRequest request,
        CancellationToken cancellationToken = default)
    {
        Result validation = ValidateRequest(request);

        if (validation.IsFailure)
        {
            return Task.FromResult(Result<OfficialCheckoutReadinessResult>.Fail(validation.ErrorDetail));
        }

        cancellationToken.ThrowIfCancellationRequested();

        string documentKind = NormalizeDocumentKind(request.DocumentKind);
        string sequenceScopeCode = NormalizeScope(request.SequenceScopeCode);

        try
        {
            using SqliteConnection connection = _connectionFactory.CreateConnection();
            connection.Open();

            List<OfficialCheckoutReadinessBlock> blocks = new();
            ActiveMachineProfile? profile = ReadMachineProfile(connection, request.BirMachineProfileId);
            OfficialDocumentSnapshotDraftMachineProfileInput? snapshotMachineProfile = null;

            if (profile is null)
            {
                blocks.Add(new OfficialCheckoutReadinessBlock(
                    "OfficialCheckoutReadiness.MachineProfileMissing",
                    "The requested BIR machine profile was not found."));
            }
            else
            {
                snapshotMachineProfile = profile.ToSnapshotInput();
                AddMachineProfileBlocks(profile, blocks);
            }

            SequenceReadiness sequenceReadiness = ReadSequenceReadiness(
                connection,
                request.BirMachineProfileId,
                documentKind,
                sequenceScopeCode,
                request.Year);

            blocks.AddRange(sequenceReadiness.Blocks);

            OfficialCheckoutReadinessResult result = new(
                request.BirMachineProfileId,
                documentKind,
                sequenceScopeCode,
                request.Year,
                sequenceReadiness.ActiveSequenceId,
                snapshotMachineProfile,
                blocks);

            return Task.FromResult(Result<OfficialCheckoutReadinessResult>.Success(result));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException or OverflowException)
        {
            return Task.FromResult(Result<OfficialCheckoutReadinessResult>.Fail(Error.Unexpected(
                "OfficialCheckoutReadiness.CheckFailed",
                ex.Message)));
        }
    }

    private static Result ValidateRequest(OfficialCheckoutReadinessRequest? request)
    {
        if (request is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialCheckoutReadiness.RequestRequired",
                "Official checkout readiness request is required."));
        }

        if (request.BirMachineProfileId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "OfficialCheckoutReadiness.MachineProfileRequired",
                "BIR machine profile id is required."));
        }

        string documentKind = NormalizeDocumentKind(request.DocumentKind);

        if (documentKind is not "OFFICIAL_RECEIPT" and not "OFFICIAL_INVOICE")
        {
            return Result.Fail(Error.Validation(
                "OfficialCheckoutReadiness.InvalidDocumentKind",
                "Official checkout readiness requires OFFICIAL_RECEIPT or OFFICIAL_INVOICE."));
        }

        if (string.IsNullOrWhiteSpace(request.SequenceScopeCode))
        {
            return Result.Fail(Error.Validation(
                "OfficialCheckoutReadiness.ScopeRequired",
                "Official checkout readiness requires a sequence scope code."));
        }

        if (request.Year < 2000 || request.Year > 9999)
        {
            return Result.Fail(Error.Validation(
                "OfficialCheckoutReadiness.InvalidYear",
                "Official checkout readiness year is invalid."));
        }

        return Result.Success();
    }

    private static ActiveMachineProfile? ReadMachineProfile(SqliteConnection connection, Guid profileId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                registered_taxpayer_name,
                business_name,
                registered_address,
                taxpayer_identification_number,
                branch_code,
                machine_identification_number,
                machine_serial_number,
                accreditation_number,
                permit_to_use_number,
                software_name,
                software_version,
                compliance_mode,
                official_output_enabled,
                is_active
            FROM bir_machine_profiles
            WHERE id = $profileId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$profileId", profileId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read())
        {
            return null;
        }

        return new ActiveMachineProfile(
            ReadNullableString(reader, 0),
            ReadNullableString(reader, 1),
            ReadNullableString(reader, 2),
            ReadNullableString(reader, 3),
            ReadNullableString(reader, 4),
            ReadNullableString(reader, 5),
            ReadNullableString(reader, 6),
            ReadNullableString(reader, 7),
            ReadNullableString(reader, 8),
            ReadNullableString(reader, 9),
            ReadNullableString(reader, 10),
            ReadNullableString(reader, 11),
            reader.GetInt32(12),
            reader.GetInt32(13));
    }

    private static void AddMachineProfileBlocks(
        ActiveMachineProfile profile,
        List<OfficialCheckoutReadinessBlock> blocks)
    {
        if (profile.IsActive != 1)
        {
            blocks.Add(new OfficialCheckoutReadinessBlock(
                "OfficialCheckoutReadiness.MachineProfileInactive",
                "The BIR machine profile is not active."));
        }

        if (!string.Equals(profile.ComplianceMode?.Trim(), "ACCREDITED", StringComparison.OrdinalIgnoreCase))
        {
            blocks.Add(new OfficialCheckoutReadinessBlock(
                "OfficialCheckoutReadiness.MachineProfileNotAccredited",
                "The BIR machine profile is not in ACCREDITED compliance mode."));
        }

        if (profile.OfficialOutputEnabled != 1)
        {
            blocks.Add(new OfficialCheckoutReadinessBlock(
                "OfficialCheckoutReadiness.OfficialOutputDisabled",
                "Official output is not enabled for the BIR machine profile."));
        }

        AddRequiredTextBlock(profile.RegisteredTaxpayerName, blocks, "TaxpayerNameMissing", "Registered taxpayer name is required.");
        AddRequiredTextBlock(profile.RegisteredBusinessAddress, blocks, "RegisteredAddressMissing", "Registered business address is required.");
        AddRequiredTextBlock(profile.TaxpayerIdentificationNumber, blocks, "TinMissing", "Taxpayer identification number is required.");
        AddRequiredTextBlock(profile.BranchCode, blocks, "BranchCodeMissing", "Branch code is required.");
        AddRequiredTextBlock(profile.MachineSerialNumber, blocks, "MachineSerialMissing", "Machine serial number is required.");
        AddRequiredTextBlock(profile.MachineIdentificationNumber, blocks, "MachineIdentificationMissing", "Machine identification number is required for official output.");
        AddRequiredTextBlock(profile.AccreditationNumber, blocks, "AccreditationNumberMissing", "Accreditation number is required for official output.");
        AddRequiredTextBlock(profile.PermitToUseNumber, blocks, "PermitToUseNumberMissing", "Permit to use number is required for official output.");
        AddRequiredTextBlock(profile.SoftwareName, blocks, "SoftwareNameMissing", "Software name is required.");
        AddRequiredTextBlock(profile.SoftwareVersion, blocks, "SoftwareVersionMissing", "Software version is required.");
    }

    private static SequenceReadiness ReadSequenceReadiness(
        SqliteConnection connection,
        Guid profileId,
        string documentKind,
        string sequenceScopeCode,
        int year)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, current_number, end_number
            FROM bir_official_document_sequences
            WHERE bir_machine_profile_id = $profileId
              AND document_kind = $documentKind
              AND sequence_scope_code = $sequenceScopeCode
              AND year = $year
              AND is_active = 1
              AND is_exhausted = 0
            ORDER BY created_at, id
            LIMIT 2;
            """;
        command.Parameters.AddWithValue("$profileId", profileId.ToString("D"));
        command.Parameters.AddWithValue("$documentKind", documentKind);
        command.Parameters.AddWithValue("$sequenceScopeCode", sequenceScopeCode);
        command.Parameters.AddWithValue("$year", year);

        using SqliteDataReader reader = command.ExecuteReader();
        List<ActiveSequence> sequences = new();

        while (reader.Read())
        {
            sequences.Add(new ActiveSequence(
                Guid.Parse(reader.GetString(0)),
                reader.GetInt64(1),
                reader.IsDBNull(2) ? null : reader.GetInt64(2)));
        }

        List<OfficialCheckoutReadinessBlock> blocks = new();

        if (sequences.Count == 0)
        {
            blocks.Add(new OfficialCheckoutReadinessBlock(
                "OfficialCheckoutReadiness.ActiveSequenceMissing",
                "No active official document sequence was found for the requested profile, document kind, scope, and year."));

            return new SequenceReadiness(null, blocks);
        }

        if (sequences.Count > 1)
        {
            // Kirch: why - competing active ranges make official numbering ambiguous even before allocation.
            blocks.Add(new OfficialCheckoutReadinessBlock(
                "OfficialCheckoutReadiness.MultipleActiveSequences",
                "More than one active official document sequence exists for the requested profile, document kind, scope, and year."));

            return new SequenceReadiness(null, blocks);
        }

        ActiveSequence sequence = sequences[0];
        long nextNumber = checked(sequence.CurrentNumber + 1);

        if (sequence.EndNumber.HasValue && nextNumber > sequence.EndNumber.Value)
        {
            blocks.Add(new OfficialCheckoutReadinessBlock(
                "OfficialCheckoutReadiness.SequenceExhausted",
                "The active official document sequence range is exhausted."));
        }

        return new SequenceReadiness(sequence.Id, blocks);
    }

    private static void AddRequiredTextBlock(
        string? value,
        List<OfficialCheckoutReadinessBlock> blocks,
        string suffix,
        string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            blocks.Add(new OfficialCheckoutReadinessBlock(
                "OfficialCheckoutReadiness." + suffix,
                message));
        }
    }

    private static string? ReadNullableString(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
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

    private sealed record ActiveMachineProfile(
        string? RegisteredTaxpayerName,
        string? BusinessName,
        string? RegisteredBusinessAddress,
        string? TaxpayerIdentificationNumber,
        string? BranchCode,
        string? MachineIdentificationNumber,
        string? MachineSerialNumber,
        string? AccreditationNumber,
        string? PermitToUseNumber,
        string? SoftwareName,
        string? SoftwareVersion,
        string? ComplianceMode,
        int OfficialOutputEnabled,
        int IsActive)
    {
        public OfficialDocumentSnapshotDraftMachineProfileInput ToSnapshotInput()
        {
            return new OfficialDocumentSnapshotDraftMachineProfileInput(
                RegisteredTaxpayerName ?? string.Empty,
                BusinessName,
                TaxpayerIdentificationNumber ?? string.Empty,
                BranchCode ?? string.Empty,
                RegisteredBusinessAddress ?? string.Empty,
                MachineSerialNumber,
                MachineIdentificationNumber,
                AccreditationNumber,
                PermitToUseNumber);
        }
    }

    private sealed record ActiveSequence(Guid Id, long CurrentNumber, long? EndNumber);

    private sealed record SequenceReadiness(
        Guid? ActiveSequenceId,
        IReadOnlyList<OfficialCheckoutReadinessBlock> Blocks);
}
