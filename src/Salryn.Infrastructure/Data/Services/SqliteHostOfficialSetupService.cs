using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Official;
using Salryn.Core.Services;

namespace Salryn.Infrastructure.Data.Services;

/// <summary>
/// Host official setup persistence (RMO-BIR-M4).
/// Uses bir_machine_profiles + bir_official_document_sequences + official_receipt_profiles.
/// Never sets ACCREDITED or official_output_enabled=1. Never advances series as sale allocation.
/// </summary>
public sealed class SqliteHostOfficialSetupService : IHostOfficialSetupService
{
    private static readonly string[] SetupWarnings =
    {
        "Official output is not implemented.",
        "This setup does not make Salryn BIR/RMO compliant.",
        "This setup does not authorize official receipt issuance.",
        "liveOfficialPostingAllowed remains false.",
        "Document series setup does not allocate numbers during checkout."
    };

    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteHostOfficialSetupService(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public Task<Result<OfficialSetupSnapshotContract>> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using SqliteConnection connection = _connectionFactory.CreateConnection();
            connection.Open();
            return Task.FromResult(Result<OfficialSetupSnapshotContract>.Success(ReadSnapshot(connection)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<OfficialSetupSnapshotContract>.Fail(Error.Unexpected(
                "HostOfficialSetup.GetFailed",
                "Failed to load official setup: " + ex.Message)));
        }
    }

    public Task<Result<OfficialSetupSnapshotContract>> SaveBusinessAsync(
        OfficialSetupBusinessSaveRequest request,
        string? updatedByUserId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Result validation = ValidateBusiness(request);
        if (validation.IsFailure)
        {
            return Task.FromResult(Result<OfficialSetupSnapshotContract>.Fail(validation.ErrorDetail));
        }

        try
        {
            using SqliteConnection connection = _connectionFactory.CreateConnection();
            connection.Open();
            using SqliteTransaction tx = connection.BeginTransaction();
            ActiveProfileRow profile = EnsureActiveProfile(connection, tx);
            string now = DateTimeOffset.UtcNow.ToString("O");

            Result<TaxpayerRegistrationMode?> modeParse =
                TaxpayerRegistrationModeCodes.ParseOptional(request.VatRegistrationMode);
            if (modeParse.IsFailure)
            {
                return Task.FromResult(Result<OfficialSetupSnapshotContract>.Fail(modeParse.ErrorDetail));
            }

            string? persistedMode = modeParse.Value is null
                ? null
                : TaxpayerRegistrationModeCodes.ToPersistedCode(modeParse.Value.Value);

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = tx;
                command.CommandText = """
                    UPDATE bir_machine_profiles
                    SET registered_taxpayer_name = $name,
                        business_name = $businessName,
                        registered_address = $address,
                        branch_address = $branchAddress,
                        taxpayer_identification_number = $tin,
                        branch_code = $branchCode,
                        vat_registration_mode = $vatMode,
                        compliance_mode = 'NOT_ACCREDITED',
                        official_output_enabled = 0,
                        updated_at = $updatedAt
                    WHERE id = $id;
                    """;
                command.Parameters.AddWithValue("$name", request.RegisteredTaxpayerName.Trim());
                command.Parameters.AddWithValue(
                    "$businessName",
                    string.IsNullOrWhiteSpace(request.BusinessName) ? DBNull.Value : request.BusinessName.Trim());
                command.Parameters.AddWithValue("$address", request.RegisteredAddress.Trim());
                command.Parameters.AddWithValue(
                    "$branchAddress",
                    string.IsNullOrWhiteSpace(request.BranchAddress) ? DBNull.Value : request.BranchAddress.Trim());
                command.Parameters.AddWithValue("$tin", request.TaxpayerIdentificationNumber.Trim());
                command.Parameters.AddWithValue("$branchCode", request.BranchCode.Trim());
                command.Parameters.AddWithValue(
                    "$vatMode",
                    persistedMode is null ? DBNull.Value : persistedMode);
                command.Parameters.AddWithValue("$updatedAt", now);
                command.Parameters.AddWithValue("$id", profile.Id.ToString("D"));
                command.ExecuteNonQuery();
            }

            tx.Commit();
            return Task.FromResult(Result<OfficialSetupSnapshotContract>.Success(ReadSnapshot(connection)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<OfficialSetupSnapshotContract>.Fail(Error.Unexpected(
                "HostOfficialSetup.SaveBusinessFailed",
                "Failed to save business profile: " + ex.Message)));
        }
    }

    public Task<Result<OfficialSetupSnapshotContract>> SaveMachineAsync(
        OfficialSetupMachineSaveRequest request,
        string? updatedByUserId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(request.MachineSerialNumber))
        {
            return Task.FromResult(Result<OfficialSetupSnapshotContract>.Fail(Error.Validation(
                "HostOfficialSetup.MachineSerialRequired",
                "Machine serial number is required.")));
        }

        try
        {
            using SqliteConnection connection = _connectionFactory.CreateConnection();
            connection.Open();
            using SqliteTransaction tx = connection.BeginTransaction();
            ActiveProfileRow profile = EnsureActiveProfile(connection, tx);
            string now = DateTimeOffset.UtcNow.ToString("O");

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = tx;
                command.CommandText = """
                    UPDATE bir_machine_profiles
                    SET machine_serial_number = $serial,
                        machine_identification_number = $min,
                        pos_terminal_number = $terminal,
                        accreditation_number = $an,
                        permit_to_use_number = $ptu,
                        software_version = $swVersion,
                        software_build = $swBuild,
                        compliance_mode = 'NOT_ACCREDITED',
                        official_output_enabled = 0,
                        updated_at = $updatedAt
                    WHERE id = $id;
                    """;
                command.Parameters.AddWithValue("$serial", request.MachineSerialNumber.Trim());
                command.Parameters.AddWithValue(
                    "$min",
                    string.IsNullOrWhiteSpace(request.MachineIdentificationNumber)
                        ? DBNull.Value
                        : request.MachineIdentificationNumber.Trim());
                command.Parameters.AddWithValue(
                    "$terminal",
                    string.IsNullOrWhiteSpace(request.PosTerminalNumber)
                        ? DBNull.Value
                        : request.PosTerminalNumber.Trim());
                command.Parameters.AddWithValue(
                    "$an",
                    string.IsNullOrWhiteSpace(request.AccreditationNumber)
                        ? DBNull.Value
                        : request.AccreditationNumber.Trim());
                command.Parameters.AddWithValue(
                    "$ptu",
                    string.IsNullOrWhiteSpace(request.PermitToUseNumber)
                        ? DBNull.Value
                        : request.PermitToUseNumber.Trim());
                command.Parameters.AddWithValue(
                    "$swVersion",
                    string.IsNullOrWhiteSpace(request.SoftwareVersion)
                        ? "UNSET"
                        : request.SoftwareVersion.Trim());
                command.Parameters.AddWithValue(
                    "$swBuild",
                    string.IsNullOrWhiteSpace(request.SoftwareBuild)
                        ? DBNull.Value
                        : request.SoftwareBuild.Trim());
                command.Parameters.AddWithValue("$updatedAt", now);
                command.Parameters.AddWithValue("$id", profile.Id.ToString("D"));
                command.ExecuteNonQuery();
            }

            tx.Commit();
            return Task.FromResult(Result<OfficialSetupSnapshotContract>.Success(ReadSnapshot(connection)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<OfficialSetupSnapshotContract>.Fail(Error.Unexpected(
                "HostOfficialSetup.SaveMachineFailed",
                "Failed to save machine profile: " + ex.Message)));
        }
    }

    public Task<Result<OfficialSetupSnapshotContract>> SaveReceiptAsync(
        OfficialSetupReceiptSaveRequest request,
        string? updatedByUserId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(request.DocumentTitle))
        {
            return Task.FromResult(Result<OfficialSetupSnapshotContract>.Fail(Error.Validation(
                "HostOfficialSetup.DocumentTitleRequired",
                "Document title is required.")));
        }

        string? documentKind = NormalizeDocumentKind(request.DocumentKind);
        if (documentKind is null)
        {
            return Task.FromResult(Result<OfficialSetupSnapshotContract>.Fail(Error.Validation(
                "HostOfficialSetup.InvalidDocumentKind",
                "Document kind must be OFFICIAL_INVOICE or OFFICIAL_RECEIPT.")));
        }

        try
        {
            using SqliteConnection connection = _connectionFactory.CreateConnection();
            connection.Open();
            using SqliteTransaction tx = connection.BeginTransaction();
            ActiveProfileRow profile = EnsureActiveProfile(connection, tx);
            string now = DateTimeOffset.UtcNow.ToString("O");
            Guid receiptId = Guid.NewGuid();

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = tx;
                command.CommandText = """
                    INSERT INTO official_receipt_profiles (
                        id, bir_machine_profile_id, document_title, document_kind,
                        footer_legal_text, supplementary_disclaimer_text,
                        reprint_marker_text, training_marker_text, void_refund_marker_text,
                        invoice_or_transition_mode, sc_pwd_placeholder_enabled, dry_run_only,
                        created_at, updated_at, updated_by_user_id
                    ) VALUES (
                        $id, $profileId, $title, $kind,
                        $footer, $supplementary,
                        $reprint, $training, $voidRefund,
                        $transition, $scPwd, 1,
                        $createdAt, $updatedAt, $updatedBy
                    )
                    ON CONFLICT(bir_machine_profile_id) DO UPDATE SET
                        document_title = excluded.document_title,
                        document_kind = excluded.document_kind,
                        footer_legal_text = excluded.footer_legal_text,
                        supplementary_disclaimer_text = excluded.supplementary_disclaimer_text,
                        reprint_marker_text = excluded.reprint_marker_text,
                        training_marker_text = excluded.training_marker_text,
                        void_refund_marker_text = excluded.void_refund_marker_text,
                        invoice_or_transition_mode = excluded.invoice_or_transition_mode,
                        sc_pwd_placeholder_enabled = excluded.sc_pwd_placeholder_enabled,
                        dry_run_only = 1,
                        updated_at = excluded.updated_at,
                        updated_by_user_id = excluded.updated_by_user_id;
                    """;
                command.Parameters.AddWithValue("$id", receiptId.ToString("D"));
                command.Parameters.AddWithValue("$profileId", profile.Id.ToString("D"));
                command.Parameters.AddWithValue("$title", request.DocumentTitle.Trim());
                command.Parameters.AddWithValue("$kind", documentKind);
                command.Parameters.AddWithValue("$footer", NullIfEmpty(request.FooterLegalText));
                command.Parameters.AddWithValue("$supplementary", NullIfEmpty(request.SupplementaryDisclaimerText));
                command.Parameters.AddWithValue("$reprint", NullIfEmpty(request.ReprintMarkerText));
                command.Parameters.AddWithValue("$training", NullIfEmpty(request.TrainingMarkerText));
                command.Parameters.AddWithValue("$voidRefund", NullIfEmpty(request.VoidRefundMarkerText));
                command.Parameters.AddWithValue("$transition", NullIfEmpty(request.InvoiceOrTransitionMode));
                command.Parameters.AddWithValue("$scPwd", request.ScPwdPlaceholderEnabled ? 1 : 0);
                command.Parameters.AddWithValue("$createdAt", now);
                command.Parameters.AddWithValue("$updatedAt", now);
                command.Parameters.AddWithValue(
                    "$updatedBy",
                    string.IsNullOrWhiteSpace(updatedByUserId) ? DBNull.Value : updatedByUserId.Trim());
                command.ExecuteNonQuery();
            }

            tx.Commit();
            return Task.FromResult(Result<OfficialSetupSnapshotContract>.Success(ReadSnapshot(connection)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<OfficialSetupSnapshotContract>.Fail(Error.Unexpected(
                "HostOfficialSetup.SaveReceiptFailed",
                "Failed to save receipt profile: " + ex.Message)));
        }
    }

    public Task<Result<OfficialSetupSnapshotContract>> SaveSeriesAsync(
        OfficialSetupSeriesSaveRequest request,
        string? updatedByUserId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string? documentKind = NormalizeDocumentKind(request.DocumentKind);
        if (documentKind is null)
        {
            return Task.FromResult(Result<OfficialSetupSnapshotContract>.Fail(Error.Validation(
                "HostOfficialSetup.InvalidDocumentKind",
                "Document kind must be OFFICIAL_INVOICE or OFFICIAL_RECEIPT.")));
        }

        if (string.IsNullOrWhiteSpace(request.Prefix) || request.Prefix.Trim().Length > 12)
        {
            return Task.FromResult(Result<OfficialSetupSnapshotContract>.Fail(Error.Validation(
                "HostOfficialSetup.InvalidPrefix",
                "Series prefix is required (max 12 characters).")));
        }

        if (request.Year < 2000 || request.Year > 9999)
        {
            return Task.FromResult(Result<OfficialSetupSnapshotContract>.Fail(Error.Validation(
                "HostOfficialSetup.InvalidYear",
                "Year must be between 2000 and 9999.")));
        }

        if (request.StartNumber <= 0)
        {
            return Task.FromResult(Result<OfficialSetupSnapshotContract>.Fail(Error.Validation(
                "HostOfficialSetup.InvalidStartNumber",
                "Start number must be greater than zero.")));
        }

        if (request.PaddingDigits < 6 || request.PaddingDigits > 12)
        {
            return Task.FromResult(Result<OfficialSetupSnapshotContract>.Fail(Error.Validation(
                "HostOfficialSetup.InvalidPadding",
                "Padding digits must be between 6 and 12.")));
        }

        if (request.EndNumber is int end && end < request.StartNumber)
        {
            return Task.FromResult(Result<OfficialSetupSnapshotContract>.Fail(Error.Validation(
                "HostOfficialSetup.InvalidEndNumber",
                "End number must be greater than or equal to start number.")));
        }

        try
        {
            using SqliteConnection connection = _connectionFactory.CreateConnection();
            connection.Open();
            using SqliteTransaction tx = connection.BeginTransaction();
            ActiveProfileRow profile = EnsureActiveProfile(connection, tx);
            string now = DateTimeOffset.UtcNow.ToString("O");

            // Deactivate existing active series for this profile/kind/scope/year (setup replace).
            using (SqliteCommand deactivate = connection.CreateCommand())
            {
                deactivate.Transaction = tx;
                deactivate.CommandText = """
                    UPDATE bir_official_document_sequences
                    SET is_active = 0,
                        deactivated_at = $now,
                        updated_at = $now
                    WHERE bir_machine_profile_id = $profileId
                      AND document_kind = $kind
                      AND sequence_scope_code = 'LOCAL_SINGLE_STORE'
                      AND year = $year
                      AND is_active = 1;
                    """;
                deactivate.Parameters.AddWithValue("$now", now);
                deactivate.Parameters.AddWithValue("$profileId", profile.Id.ToString("D"));
                deactivate.Parameters.AddWithValue("$kind", documentKind);
                deactivate.Parameters.AddWithValue("$year", request.Year);
                deactivate.ExecuteNonQuery();
            }

            // Setup only: current_number = start - 1 so next official allocate would be start_number.
            // M4 does not allocate during checkout.
            int currentNumber = request.StartNumber - 1;
            Guid seriesId = Guid.NewGuid();

            using (SqliteCommand insert = connection.CreateCommand())
            {
                insert.Transaction = tx;
                insert.CommandText = """
                    INSERT INTO bir_official_document_sequences (
                        id, bir_machine_profile_id, document_kind, sequence_scope_code,
                        prefix, year, start_number, current_number, end_number, padding_digits,
                        is_active, is_exhausted, created_at, updated_at, deactivated_at
                    ) VALUES (
                        $id, $profileId, $kind, 'LOCAL_SINGLE_STORE',
                        $prefix, $year, $start, $current, $end, $padding,
                        1, 0, $createdAt, $updatedAt, NULL
                    );
                    """;
                insert.Parameters.AddWithValue("$id", seriesId.ToString("D"));
                insert.Parameters.AddWithValue("$profileId", profile.Id.ToString("D"));
                insert.Parameters.AddWithValue("$kind", documentKind);
                insert.Parameters.AddWithValue("$prefix", request.Prefix.Trim());
                insert.Parameters.AddWithValue("$year", request.Year);
                insert.Parameters.AddWithValue("$start", request.StartNumber);
                insert.Parameters.AddWithValue("$current", currentNumber);
                insert.Parameters.AddWithValue(
                    "$end",
                    request.EndNumber.HasValue ? request.EndNumber.Value : DBNull.Value);
                insert.Parameters.AddWithValue("$padding", request.PaddingDigits);
                insert.Parameters.AddWithValue("$createdAt", now);
                insert.Parameters.AddWithValue("$updatedAt", now);
                insert.ExecuteNonQuery();
            }

            tx.Commit();
            return Task.FromResult(Result<OfficialSetupSnapshotContract>.Success(ReadSnapshot(connection)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<OfficialSetupSnapshotContract>.Fail(Error.Unexpected(
                "HostOfficialSetup.SaveSeriesFailed",
                "Failed to save document series: " + ex.Message)));
        }
    }

    private static Result ValidateBusiness(OfficialSetupBusinessSaveRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RegisteredTaxpayerName))
        {
            return Result.Fail(Error.Validation(
                "HostOfficialSetup.TaxpayerNameRequired",
                "Registered taxpayer name is required."));
        }

        if (string.IsNullOrWhiteSpace(request.RegisteredAddress))
        {
            return Result.Fail(Error.Validation(
                "HostOfficialSetup.AddressRequired",
                "Registered address is required."));
        }

        if (string.IsNullOrWhiteSpace(request.TaxpayerIdentificationNumber))
        {
            return Result.Fail(Error.Validation(
                "HostOfficialSetup.TinRequired",
                "TIN is required."));
        }

        if (string.IsNullOrWhiteSpace(request.BranchCode))
        {
            return Result.Fail(Error.Validation(
                "HostOfficialSetup.BranchCodeRequired",
                "Branch code is required."));
        }

        Result<TaxpayerRegistrationMode?> modeParse =
            TaxpayerRegistrationModeCodes.ParseOptional(request.VatRegistrationMode);
        if (modeParse.IsFailure)
        {
            return Result.Fail(modeParse.ErrorDetail);
        }

        return Result.Success();
    }

    private static string? NormalizeDocumentKind(string? documentKind)
    {
        if (string.IsNullOrWhiteSpace(documentKind))
        {
            return "OFFICIAL_INVOICE";
        }

        string kind = documentKind.Trim().ToUpperInvariant();
        if (kind is "OFFICIAL_INVOICE" or "OFFICIAL_RECEIPT")
        {
            return kind;
        }

        if (kind is "INVOICE")
        {
            return "OFFICIAL_INVOICE";
        }

        if (kind is "RECEIPT" or "OR")
        {
            return "OFFICIAL_RECEIPT";
        }

        return null;
    }

    private static object NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static ActiveProfileRow EnsureActiveProfile(SqliteConnection connection, SqliteTransaction tx)
    {
        ActiveProfileRow? existing = TryReadActiveProfile(connection, tx);
        if (existing is not null)
        {
            return existing;
        }

        Guid id = Guid.NewGuid();
        string now = DateTimeOffset.UtcNow.ToString("O");
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT INTO bir_machine_profiles (
                id, registration_scope_code, store_settings_id,
                registered_taxpayer_name, business_name, registered_address, branch_address,
                taxpayer_identification_number, branch_code,
                machine_identification_number, machine_serial_number, pos_terminal_number,
                accreditation_number, permit_to_use_number, permit_to_use_issued_at,
                accreditation_issued_at, accreditation_valid_until,
                software_name, software_version, software_build,
                compliance_mode, official_output_enabled, is_active,
                created_at, updated_at, deactivated_at
            ) VALUES (
                $id, 'LOCAL_SINGLE_STORE', NULL,
                'UNSET', NULL, 'UNSET', NULL,
                'UNSET', '00000',
                NULL, 'UNSET', NULL,
                NULL, NULL, NULL,
                NULL, NULL,
                'Salryn', 'UNSET', NULL,
                'NOT_ACCREDITED', 0, 1,
                $createdAt, $updatedAt, NULL
            );
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        command.Parameters.AddWithValue("$createdAt", now);
        command.Parameters.AddWithValue("$updatedAt", now);
        command.ExecuteNonQuery();

        return new ActiveProfileRow(id);
    }

    private static ActiveProfileRow? TryReadActiveProfile(SqliteConnection connection, SqliteTransaction? tx)
    {
        using SqliteCommand command = connection.CreateCommand();
        if (tx is not null)
        {
            command.Transaction = tx;
        }

        command.CommandText = """
            SELECT id
            FROM bir_machine_profiles
            WHERE registration_scope_code = 'LOCAL_SINGLE_STORE'
              AND is_active = 1
            LIMIT 1;
            """;
        object? value = command.ExecuteScalar();
        if (value is null or DBNull)
        {
            return null;
        }

        return Guid.TryParse(Convert.ToString(value), out Guid id)
            ? new ActiveProfileRow(id)
            : null;
    }

    private OfficialSetupSnapshotContract ReadSnapshot(SqliteConnection connection)
    {
        OfficialSetupBusinessProfileContract? business = null;
        OfficialSetupMachineProfileContract? machine = null;
        OfficialSetupReceiptProfileContract? receipt = null;
        OfficialSetupDocumentSeriesContract? series = null;

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id,
                       registered_taxpayer_name, business_name, registered_address, branch_address,
                       taxpayer_identification_number, branch_code,
                       machine_serial_number, machine_identification_number, pos_terminal_number,
                       accreditation_number, permit_to_use_number,
                       software_version, software_build,
                       compliance_mode, official_output_enabled, is_active,
                       vat_registration_mode
                FROM bir_machine_profiles
                WHERE registration_scope_code = 'LOCAL_SINGLE_STORE'
                  AND is_active = 1
                LIMIT 1;
                """;
            using SqliteDataReader reader = command.ExecuteReader();
            if (reader.Read())
            {
                Guid profileId = Guid.Parse(reader.GetString(0));
                string? vatModeRaw = reader.IsDBNull(17) ? null : reader.GetString(17);
                string? vatModeNormalized = null;
                if (!string.IsNullOrWhiteSpace(vatModeRaw))
                {
                    Result<TaxpayerRegistrationMode> parsed = TaxpayerRegistrationModeCodes.Parse(vatModeRaw);
                    if (parsed.IsSuccess)
                    {
                        vatModeNormalized = TaxpayerRegistrationModeCodes.ToPersistedCode(parsed.Value);
                    }
                }

                business = new OfficialSetupBusinessProfileContract(
                    RegisteredTaxpayerName: reader.GetString(1),
                    BusinessName: reader.IsDBNull(2) ? null : reader.GetString(2),
                    RegisteredAddress: reader.GetString(3),
                    BranchAddress: reader.IsDBNull(4) ? null : reader.GetString(4),
                    TaxpayerIdentificationNumber: reader.GetString(5),
                    BranchCode: reader.GetString(6),
                    VatRegistrationMode: vatModeNormalized,
                    ContactNote: null);
                machine = new OfficialSetupMachineProfileContract(
                    ProfileId: profileId,
                    MachineSerialNumber: reader.GetString(7),
                    MachineIdentificationNumber: reader.IsDBNull(8) ? null : reader.GetString(8),
                    PosTerminalNumber: reader.IsDBNull(9) ? null : reader.GetString(9),
                    AccreditationNumber: reader.IsDBNull(10) ? null : reader.GetString(10),
                    PermitToUseNumber: reader.IsDBNull(11) ? null : reader.GetString(11),
                    SoftwareVersion: reader.IsDBNull(12) ? null : reader.GetString(12),
                    SoftwareBuild: reader.IsDBNull(13) ? null : reader.GetString(13),
                    ComplianceMode: reader.GetString(14),
                    OfficialOutputEnabledInConfig: reader.GetInt32(15) == 1,
                    IsActive: reader.GetInt32(16) == 1);

                receipt = ReadReceipt(connection, profileId);
                series = ReadSeries(connection, profileId);
            }
        }

        bool setupPartial = business is not null || machine is not null || receipt is not null || series is not null;
        string summary = setupPartial
            ? "Official setup data is present for internal dry-run configuration only. Official output is not implemented."
            : "No official setup data yet. Official output is not implemented.";

        return new OfficialSetupSnapshotContract(
            ContractVersion: OfficialContractVersions.M3,
            Business: business,
            Machine: machine,
            Receipt: receipt,
            Series: series,
            LiveOfficialPostingAllowed: false,
            Warnings: SetupWarnings,
            Summary: summary);
    }

    private static OfficialSetupReceiptProfileContract? ReadReceipt(SqliteConnection connection, Guid profileId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, document_title, document_kind, footer_legal_text, supplementary_disclaimer_text,
                   reprint_marker_text, training_marker_text, void_refund_marker_text,
                   invoice_or_transition_mode, sc_pwd_placeholder_enabled, dry_run_only
            FROM official_receipt_profiles
            WHERE bir_machine_profile_id = $profileId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$profileId", profileId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new OfficialSetupReceiptProfileContract(
            ProfileId: Guid.Parse(reader.GetString(0)),
            DocumentTitle: reader.GetString(1),
            DocumentKind: reader.GetString(2),
            FooterLegalText: reader.IsDBNull(3) ? null : reader.GetString(3),
            SupplementaryDisclaimerText: reader.IsDBNull(4) ? null : reader.GetString(4),
            ReprintMarkerText: reader.IsDBNull(5) ? null : reader.GetString(5),
            TrainingMarkerText: reader.IsDBNull(6) ? null : reader.GetString(6),
            VoidRefundMarkerText: reader.IsDBNull(7) ? null : reader.GetString(7),
            InvoiceOrTransitionMode: reader.IsDBNull(8) ? null : reader.GetString(8),
            ScPwdPlaceholderEnabled: reader.GetInt32(9) == 1,
            DryRunOnly: reader.GetInt32(10) == 1);
    }

    private static OfficialSetupDocumentSeriesContract? ReadSeries(SqliteConnection connection, Guid profileId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, document_kind, prefix, year, start_number, current_number, end_number,
                   padding_digits, is_active, is_exhausted
            FROM bir_official_document_sequences
            WHERE bir_machine_profile_id = $profileId
              AND is_active = 1
            ORDER BY year DESC, created_at DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$profileId", profileId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new OfficialSetupDocumentSeriesContract(
            SeriesId: Guid.Parse(reader.GetString(0)),
            BirMachineProfileId: profileId,
            DocumentKind: reader.GetString(1),
            Prefix: reader.GetString(2),
            Year: reader.GetInt32(3),
            StartNumber: Convert.ToInt32(reader.GetInt64(4)),
            CurrentNumber: Convert.ToInt32(reader.GetInt64(5)),
            EndNumber: reader.IsDBNull(6) ? null : Convert.ToInt32(reader.GetInt64(6)),
            PaddingDigits: reader.GetInt32(7),
            IsActive: reader.GetInt32(8) == 1,
            IsExhausted: reader.GetInt32(9) == 1,
            DryRunOnly: true);
    }

    private sealed record ActiveProfileRow(Guid Id);
}
