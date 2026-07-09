using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Official;
using Salryn.Core.Services;

namespace Salryn.Infrastructure.Data.Services;

/// <summary>
/// RMO-BIR-M5 host numbering preview/validation.
/// Reads bir_official_document_sequences only. Never mutates current_number.
/// Never posts sales. Never enables live official allocation.
/// </summary>
public sealed class SqliteHostOfficialNumberingService : IHostOfficialNumberingService
{
    private static readonly string[] BaseWarnings =
    {
        "This preview does not issue an official number.",
        "Checkout is not connected to official numbering.",
        "liveOfficialPostingAllowed remains false.",
        "officialCheckoutAllocationEnabled remains false.",
        "Official output is not implemented.",
        "This does not make Salryn BIR/RMO compliant."
    };

    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteHostOfficialNumberingService(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public Task<Result<OfficialNumberingPreviewContract>> GetPreviewAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using SqliteConnection connection = _connectionFactory.CreateConnection();
            connection.Open();
            return Task.FromResult(Result<OfficialNumberingPreviewContract>.Success(BuildPreview(connection)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<OfficialNumberingPreviewContract>.Fail(Error.Unexpected(
                "HostOfficialNumbering.PreviewFailed",
                "Failed to load official numbering preview: " + ex.Message)));
        }
    }

    public Task<Result<OfficialNumberingValidationResult>> ValidateSeriesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using SqliteConnection connection = _connectionFactory.CreateConnection();
            connection.Open();
            return Task.FromResult(Result<OfficialNumberingValidationResult>.Success(BuildValidation(connection)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<OfficialNumberingValidationResult>.Fail(Error.Unexpected(
                "HostOfficialNumbering.ValidateFailed",
                "Failed to validate official document series: " + ex.Message)));
        }
    }

    private static OfficialNumberingPreviewContract BuildPreview(SqliteConnection connection)
    {
        List<OfficialNumberingBlocker> blockers = new();
        OfficialNumberingSeriesStateContract? series = TryReadActiveSeries(connection);

        EvaluateSeries(series, blockers, out long nextNumber, out string? formatted, out bool previewOk);

        string summary = previewOk
            ? "Preview computed next official document number for engineering visibility only. Sequence was not mutated. Checkout does not allocate official numbers."
            : "Official document series is not ready for preview. See blockers. No official number was issued.";

        string? blockedReason = previewOk
            ? "Live official allocation is not implemented; preview only."
            : string.Join(" ", blockers.Select(b => b.Message));

        return new OfficialNumberingPreviewContract(
            ContractVersion: OfficialContractVersions.M3,
            DocumentType: series?.DocumentKind ?? "UNSET",
            Prefix: series?.Prefix ?? string.Empty,
            CurrentNumber: series?.CurrentNumber ?? 0,
            NextNumber: nextNumber,
            FormattedNextNumber: formatted ?? string.Empty,
            RangeStart: series?.StartNumber,
            RangeEnd: series?.EndNumber,
            SequenceScopeCode: series?.SequenceScopeCode ?? "LOCAL_SINGLE_STORE",
            BirMachineProfileId: series?.BirMachineProfileId,
            SeriesId: series?.SeriesId,
            DryRunOnly: true,
            LiveOfficialPostingAllowed: false,
            OfficialCheckoutAllocationEnabled: false,
            CheckoutUsesAllocator: false,
            PreviewMutatesSequence: false,
            AllocationAllowed: false,
            Mode: OfficialNumberingAllocationMode.PreviewOnly,
            Series: series,
            Blockers: blockers,
            Warnings: BaseWarnings,
            Summary: summary,
            BlockedReason: blockedReason);
    }

    private static OfficialNumberingValidationResult BuildValidation(SqliteConnection connection)
    {
        List<OfficialNumberingBlocker> blockers = new();
        OfficialNumberingSeriesStateContract? series = TryReadActiveSeries(connection);

        EvaluateSeries(series, blockers, out long nextNumber, out string? formatted, out bool previewOk);

        string summary = previewOk
            ? "Document series fields are valid for numbering preview only. Live allocation remains blocked. Checkout is not connected."
            : "Document series validation failed. Live allocation remains blocked.";

        return new OfficialNumberingValidationResult(
            ContractVersion: OfficialContractVersions.M3,
            IsValidForPreview: previewOk,
            LiveAllocationAllowed: false,
            OfficialCheckoutAllocationEnabled: false,
            CheckoutUsesAllocator: false,
            LiveOfficialPostingAllowed: false,
            Series: series,
            PreviewNextNumber: previewOk ? nextNumber : null,
            FormattedNextNumber: previewOk ? formatted : null,
            Blockers: blockers,
            Warnings: BaseWarnings,
            Summary: summary);
    }

    private static void EvaluateSeries(
        OfficialNumberingSeriesStateContract? series,
        List<OfficialNumberingBlocker> blockers,
        out long nextNumber,
        out string? formattedNext,
        out bool previewOk)
    {
        nextNumber = 0;
        formattedNext = null;
        previewOk = false;

        if (series is null)
        {
            blockers.Add(new OfficialNumberingBlocker(
                "HostOfficialNumbering.SeriesMissing",
                "No active official document series is configured for the host machine profile."));
            return;
        }

        if (string.IsNullOrWhiteSpace(series.Prefix))
        {
            blockers.Add(new OfficialNumberingBlocker(
                "HostOfficialNumbering.PrefixRequired",
                "Document series prefix is required."));
        }

        if (series.PaddingDigits < 6 || series.PaddingDigits > 12)
        {
            blockers.Add(new OfficialNumberingBlocker(
                "HostOfficialNumbering.PaddingInvalid",
                "Padding digits must be between 6 and 12."));
        }

        if (series.StartNumber <= 0)
        {
            blockers.Add(new OfficialNumberingBlocker(
                "HostOfficialNumbering.StartInvalid",
                "Start number must be greater than zero."));
        }

        if (series.CurrentNumber < series.StartNumber - 1)
        {
            blockers.Add(new OfficialNumberingBlocker(
                "HostOfficialNumbering.CurrentBelowStart",
                "Current number is below the allowed start-1 bound."));
        }

        if (!series.IsActive)
        {
            blockers.Add(new OfficialNumberingBlocker(
                "HostOfficialNumbering.SeriesInactive",
                "Active document series flag is off."));
        }

        if (series.IsExhausted)
        {
            blockers.Add(new OfficialNumberingBlocker(
                "HostOfficialNumbering.SeriesExhausted",
                "Document series is exhausted; no next official number is available."));
        }

        long candidate = series.CurrentNumber + 1;
        if (series.EndNumber is long end && candidate > end)
        {
            blockers.Add(new OfficialNumberingBlocker(
                "HostOfficialNumbering.WouldExceedEnd",
                "Next number would exceed configured end of range."));
        }

        if (blockers.Count > 0)
        {
            return;
        }

        nextNumber = candidate;
        formattedNext = series.Prefix + nextNumber.ToString().PadLeft(series.PaddingDigits, '0');
        previewOk = true;
    }

    private static OfficialNumberingSeriesStateContract? TryReadActiveSeries(SqliteConnection connection)
    {
        Guid? profileId = null;
        using (SqliteCommand profileCommand = connection.CreateCommand())
        {
            profileCommand.CommandText = """
                SELECT id
                FROM bir_machine_profiles
                WHERE registration_scope_code = 'LOCAL_SINGLE_STORE'
                  AND is_active = 1
                LIMIT 1;
                """;
            object? scalar = profileCommand.ExecuteScalar();
            if (scalar is string idText && Guid.TryParse(idText, out Guid parsed))
            {
                profileId = parsed;
            }
        }

        if (profileId is null)
        {
            return null;
        }

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, document_kind, sequence_scope_code, prefix, year,
                   start_number, current_number, end_number, padding_digits,
                   is_active, is_exhausted
            FROM bir_official_document_sequences
            WHERE bir_machine_profile_id = $profileId
              AND is_active = 1
            ORDER BY year DESC, created_at DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$profileId", profileId.Value.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new OfficialNumberingSeriesStateContract(
            SeriesId: Guid.Parse(reader.GetString(0)),
            BirMachineProfileId: profileId,
            DocumentKind: reader.GetString(1),
            SequenceScopeCode: reader.GetString(2),
            Prefix: reader.GetString(3),
            Year: reader.GetInt32(4),
            StartNumber: reader.GetInt64(5),
            CurrentNumber: reader.GetInt64(6),
            EndNumber: reader.IsDBNull(7) ? null : reader.GetInt64(7),
            PaddingDigits: reader.GetInt32(8),
            IsActive: reader.GetInt32(9) == 1,
            IsExhausted: reader.GetInt32(10) == 1);
    }
}
