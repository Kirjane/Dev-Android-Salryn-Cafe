using System.Text.Json;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Official;
using Salryn.Core.Services;

namespace Salryn.Infrastructure.Data.Services;

/// <summary>
/// RMO-BIR-M8/M9 internal dry-run package persistence.
/// Creates packages only; never mutates sales, sequences, or live EJ.
/// </summary>
public sealed class SqliteHostOfficialDryRunPackageService : IHostOfficialDryRunPackageService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly IHostOfficialDryRunPostingPlanner _planner;
    private readonly IHostOfficialReportPreviewService _preview;
    private readonly IHostOfficialSourceSaleReadOnlyAdapter _sourceSaleAdapter;

    public SqliteHostOfficialDryRunPackageService(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
        _planner = new SqliteHostOfficialDryRunPostingPlanner(connectionFactory);
        _preview = new HostOfficialReportPreviewService();
        _sourceSaleAdapter = new SqliteHostOfficialSourceSaleReadOnlyAdapter(connectionFactory);
    }

    public async Task<Result<OfficialDryRunPackageContract>> CreateAsync(
        OfficialDryRunPackageCreateRequest request,
        string? createdByUserId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        OfficialDryRunPostingRequest postingRequest = request?.PostingRequest
            ?? new OfficialDryRunPostingRequest(
                DocumentKind: "OFFICIAL_INVOICE",
                OperationalPosNumberHint: null,
                TaxpayerNameHint: null,
                Lines: Array.Empty<OfficialDryRunLineItem>(),
                Totals: null);

        Result<OfficialDryRunPostingPlan> planResult =
            await _planner.BuildPlanAsync(postingRequest, cancellationToken).ConfigureAwait(false);
        if (planResult.IsFailure)
        {
            return Result<OfficialDryRunPackageContract>.Fail(planResult.ErrorDetail);
        }

        OfficialDryRunPostingPlan plan = planResult.Value;
        Result<OfficialReportPreviewPackageContract> previewResult =
            await _preview.BuildPreviewAsync(plan, cancellationToken).ConfigureAwait(false);
        if (previewResult.IsFailure)
        {
            return Result<OfficialDryRunPackageContract>.Fail(previewResult.ErrorDetail);
        }

        OfficialReportPreviewPackageContract preview = previewResult.Value;

        List<OfficialDryRunPackageWarning> warnings = plan.Warnings
            .Select(w => new OfficialDryRunPackageWarning(w.Code, w.Message))
            .ToList();
        warnings.Add(new OfficialDryRunPackageWarning(
            "HostOfficialDryRunPackage.InternalOnly",
            OfficialSafetyConstants.InternalOnlyNote));
        warnings.Add(new OfficialDryRunPackageWarning(
            "HostOfficialDryRunPackage.SyntheticSource",
            "Package source mode is synthetic (not operational sale read-only)."));

        List<OfficialDryRunPackageBlocker> blockers = plan.Blockers
            .Select(b => new OfficialDryRunPackageBlocker(b.Code, b.Message))
            .ToList();

        string summary =
            "Internal dry-run package stored for engineering. " +
            "No official number issued. No sales/sequence/EJ live mutation. No legal output.";

        return await PersistAsync(
            postingRequest,
            plan,
            preview,
            OfficialDryRunPackageSourceMode.Synthetic,
            sourceSaleId: null,
            sourceSaleReadOnly: false,
            createdByUserId,
            warnings,
            blockers,
            summary,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<OfficialDryRunPackageContract>> CreateFromSaleAsync(
        Guid saleId,
        string? createdByUserId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Result<OfficialSourceSaleReadOnlyResult> readResult = await _sourceSaleAdapter.ReadAsync(
            new OfficialSourceSaleReadOnlyRequest(saleId, "OFFICIAL_INVOICE"),
            cancellationToken).ConfigureAwait(false);
        if (readResult.IsFailure)
        {
            return Result<OfficialDryRunPackageContract>.Fail(readResult.ErrorDetail);
        }

        OfficialSourceSaleReadOnlyResult source = readResult.Value;
        if (source.PostingRequest is null)
        {
            return Result<OfficialDryRunPackageContract>.Fail(Error.Validation(
                "HostOfficialDryRunPackage.SourceSalePostingRequestMissing",
                "Source-sale read-only adapter did not produce a posting request."));
        }

        // Rebuild plan via planner with operational lines; force sale source flag on plan below.
        Result<OfficialDryRunPostingPlan> planResult =
            await _planner.BuildPlanAsync(source.PostingRequest, cancellationToken).ConfigureAwait(false);
        if (planResult.IsFailure)
        {
            return Result<OfficialDryRunPackageContract>.Fail(planResult.ErrorDetail);
        }

        OfficialDryRunPostingPlan planned = planResult.Value;
        OfficialDryRunPostingPlan plan = planned with
        {
            SaleSource = OfficialDryRunSaleSource.OperationalSaleReadOnly,
            Summary =
                "Dry-run official posting plan built from operational sale (read-only). " +
                "No official document is issued. No legal output is produced. " +
                "Operational POS number remains separate from official document number."
        };

        Result<OfficialReportPreviewPackageContract> previewResult =
            await _preview.BuildPreviewAsync(plan, cancellationToken).ConfigureAwait(false);
        if (previewResult.IsFailure)
        {
            return Result<OfficialDryRunPackageContract>.Fail(previewResult.ErrorDetail);
        }

        List<OfficialDryRunPackageWarning> warnings = plan.Warnings
            .Select(w => new OfficialDryRunPackageWarning(w.Code, w.Message))
            .ToList();
        warnings.AddRange(source.Warnings.Select(w => new OfficialDryRunPackageWarning(w.Code, w.Message)));
        warnings.Add(new OfficialDryRunPackageWarning(
            "HostOfficialDryRunPackage.InternalOnly",
            OfficialSafetyConstants.InternalOnlyNote));
        warnings.Add(new OfficialDryRunPackageWarning(
            "HostOfficialDryRunPackage.SourceSaleReadOnly",
            "Package built from operational sale in source-sale read-only mode. No mutation."));

        List<OfficialDryRunPackageBlocker> blockers = plan.Blockers
            .Select(b => new OfficialDryRunPackageBlocker(b.Code, b.Message))
            .ToList();
        blockers.AddRange(source.Blockers.Select(b => new OfficialDryRunPackageBlocker(b.Code, b.Message)));

        string summary =
            "Internal dry-run package from operational sale (read-only). " +
            "No official number issued. No sales/sequence/EJ live mutation. No legal output.";

        return await PersistAsync(
            source.PostingRequest,
            plan,
            previewResult.Value,
            OfficialDryRunPackageSourceMode.OperationalSaleReadOnly,
            sourceSaleId: saleId,
            sourceSaleReadOnly: true,
            createdByUserId,
            warnings,
            blockers,
            summary,
            cancellationToken).ConfigureAwait(false);
    }

    public Task<Result<IReadOnlyList<OfficialDryRunPackageSummaryContract>>> ListRecentAsync(
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        take = Math.Clamp(take, 1, 100);

        try
        {
            using SqliteConnection connection = _connectionFactory.CreateConnection();
            connection.Open();
            List<OfficialDryRunPackageSummaryContract> list = new();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, public_id, created_at_utc, source_mode, status, summary
                FROM official_dry_run_packages
                ORDER BY created_at_utc DESC
                LIMIT $take;
                """;
            command.Parameters.AddWithValue("$take", take);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new OfficialDryRunPackageSummaryContract(
                    Id: Guid.Parse(reader.GetString(0)),
                    PublicId: reader.GetString(1),
                    CreatedAtUtc: DateTimeOffset.Parse(reader.GetString(2)),
                    SourceMode: ParseSourceMode(reader.GetString(3)),
                    Status: ParseStatus(reader.GetString(4)),
                    Summary: reader.IsDBNull(5) ? null : reader.GetString(5)));
            }

            return Task.FromResult(
                Result<IReadOnlyList<OfficialDryRunPackageSummaryContract>>.Success(list));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result<IReadOnlyList<OfficialDryRunPackageSummaryContract>>.Fail(
                Error.Unexpected(
                    "HostOfficialDryRunPackage.ListFailed",
                    "Failed to list dry-run packages: " + ex.Message)));
        }
    }

    public Task<Result<OfficialDryRunPackageContract>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using SqliteConnection connection = _connectionFactory.CreateConnection();
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, public_id, created_at_utc, created_by_user_id, source_mode, source_sale_id,
                       plan_json, report_package_json, warnings_json, blockers_json, summary
                FROM official_dry_run_packages
                WHERE id = $id
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$id", id.ToString("D"));
            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return Task.FromResult(Result<OfficialDryRunPackageContract>.Fail(Error.NotFound(
                    "HostOfficialDryRunPackage.NotFound",
                    "Internal dry-run package was not found.")));
            }

            OfficialDryRunPackageSourceMode sourceMode = ParseSourceMode(reader.GetString(4));
            Guid? sourceSaleId = reader.IsDBNull(5) ? null : Guid.Parse(reader.GetString(5));
            OfficialDryRunPostingPlan plan = JsonSerializer.Deserialize<OfficialDryRunPostingPlan>(
                reader.GetString(6), JsonOptions)
                ?? throw new InvalidOperationException("plan_json deserialize failed.");
            OfficialReportPreviewPackageContract? preview = reader.IsDBNull(7)
                ? null
                : JsonSerializer.Deserialize<OfficialReportPreviewPackageContract>(
                    reader.GetString(7), JsonOptions);
            List<OfficialDryRunPackageWarning> warnings = reader.IsDBNull(8)
                ? new List<OfficialDryRunPackageWarning>()
                : JsonSerializer.Deserialize<List<OfficialDryRunPackageWarning>>(reader.GetString(8), JsonOptions)
                  ?? new List<OfficialDryRunPackageWarning>();
            List<OfficialDryRunPackageBlocker> blockers = reader.IsDBNull(9)
                ? new List<OfficialDryRunPackageBlocker>()
                : JsonSerializer.Deserialize<List<OfficialDryRunPackageBlocker>>(reader.GetString(9), JsonOptions)
                  ?? new List<OfficialDryRunPackageBlocker>();

            return Task.FromResult(Result<OfficialDryRunPackageContract>.Success(ToContract(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                DateTimeOffset.Parse(reader.GetString(2)),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                sourceMode,
                sourceSaleId,
                plan,
                preview,
                warnings,
                blockers,
                reader.IsDBNull(10) ? string.Empty : reader.GetString(10))));
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            return Task.FromResult(Result<OfficialDryRunPackageContract>.Fail(Error.Unexpected(
                "HostOfficialDryRunPackage.GetFailed",
                "Failed to load dry-run package: " + ex.Message)));
        }
    }

    private async Task<Result<OfficialDryRunPackageContract>> PersistAsync(
        OfficialDryRunPostingRequest postingRequest,
        OfficialDryRunPostingPlan plan,
        OfficialReportPreviewPackageContract preview,
        OfficialDryRunPackageSourceMode sourceMode,
        Guid? sourceSaleId,
        bool sourceSaleReadOnly,
        string? createdByUserId,
        List<OfficialDryRunPackageWarning> warnings,
        List<OfficialDryRunPackageBlocker> blockers,
        string summary,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Guid id = Guid.NewGuid();
        string publicId = "DRY-" + id.ToString("N")[..12].ToUpperInvariant();
        DateTimeOffset createdAt = DateTimeOffset.UtcNow;
        string sourceModeValue = sourceMode == OfficialDryRunPackageSourceMode.OperationalSaleReadOnly
            ? "OperationalSaleReadOnly"
            : "Synthetic";

        try
        {
            using SqliteConnection connection = _connectionFactory.CreateConnection();
            connection.Open();
            using SqliteTransaction tx = connection.BeginTransaction();
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = tx;
                command.CommandText = """
                    INSERT INTO official_dry_run_packages (
                        id, public_id, created_at_utc, created_by_user_id, source_mode, source_sale_id,
                        contract_version, dry_run_only, non_official, live_official_posting_allowed,
                        official_checkout_allocation_enabled, checkout_uses_allocator,
                        sequence_will_mutate, sales_will_mutate, ej_will_append_live, output_will_generate,
                        official_receipt_issued, compliance_claimed,
                        request_json, plan_json, report_package_json, status, warnings_json, blockers_json, summary)
                    VALUES (
                        $id, $publicId, $createdAt, $userId, $sourceMode, $sourceSaleId,
                        $contractVersion, 1, 1, 0,
                        0, 0,
                        0, 0, 0, 0,
                        0, 0,
                        $requestJson, $planJson, $reportJson, 'PreviewBuilt', $warningsJson, $blockersJson, $summary);
                    """;
                command.Parameters.AddWithValue("$id", id.ToString("D"));
                command.Parameters.AddWithValue("$publicId", publicId);
                command.Parameters.AddWithValue("$createdAt", createdAt.ToString("O"));
                command.Parameters.AddWithValue(
                    "$userId",
                    string.IsNullOrWhiteSpace(createdByUserId) ? DBNull.Value : createdByUserId);
                command.Parameters.AddWithValue("$sourceMode", sourceModeValue);
                command.Parameters.AddWithValue(
                    "$sourceSaleId",
                    sourceSaleId.HasValue ? sourceSaleId.Value.ToString("D") : DBNull.Value);
                command.Parameters.AddWithValue("$contractVersion", OfficialContractVersions.M3);
                command.Parameters.AddWithValue("$requestJson", JsonSerializer.Serialize(postingRequest, JsonOptions));
                command.Parameters.AddWithValue("$planJson", JsonSerializer.Serialize(plan, JsonOptions));
                command.Parameters.AddWithValue("$reportJson", JsonSerializer.Serialize(preview, JsonOptions));
                command.Parameters.AddWithValue("$warningsJson", JsonSerializer.Serialize(warnings, JsonOptions));
                command.Parameters.AddWithValue("$blockersJson", JsonSerializer.Serialize(blockers, JsonOptions));
                command.Parameters.AddWithValue("$summary", summary);
                command.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch (Exception ex)
        {
            return Result<OfficialDryRunPackageContract>.Fail(Error.Unexpected(
                "HostOfficialDryRunPackage.CreateFailed",
                "Failed to persist internal dry-run package: " + ex.Message));
        }

        // Touch flags helper so source-sale read-only flag is centralized for gates.
        _ = OfficialSafetyConstants.ForcedNonLivePackageFlags(sourceSaleReadOnly);

        return Result<OfficialDryRunPackageContract>.Success(ToContract(
            id, publicId, createdAt, createdByUserId, sourceMode, sourceSaleId,
            plan, preview, warnings, blockers, summary));
    }

    private static OfficialDryRunPackageContract ToContract(
        Guid id,
        string publicId,
        DateTimeOffset createdAt,
        string? createdByUserId,
        OfficialDryRunPackageSourceMode sourceMode,
        Guid? sourceSaleId,
        OfficialDryRunPostingPlan plan,
        OfficialReportPreviewPackageContract? preview,
        IReadOnlyList<OfficialDryRunPackageWarning> warnings,
        IReadOnlyList<OfficialDryRunPackageBlocker> blockers,
        string summary) =>
        new(
            ContractVersion: OfficialContractVersions.M3,
            Id: id,
            PublicId: publicId,
            CreatedAtUtc: createdAt,
            CreatedByUserId: createdByUserId,
            SourceMode: sourceMode,
            SourceSaleId: sourceSaleId,
            Status: OfficialDryRunPackagePersistenceStatus.PreviewBuilt,
            Flags: OfficialSafetyConstants.ForcedNonLivePackageFlags(
                sourceSaleReadOnly: sourceMode == OfficialDryRunPackageSourceMode.OperationalSaleReadOnly),
            Plan: plan,
            ReportPreview: preview,
            Warnings: warnings,
            Blockers: blockers,
            Summary: summary);

    private static OfficialDryRunPackageSourceMode ParseSourceMode(string value) =>
        string.Equals(value, "OperationalSaleReadOnly", StringComparison.OrdinalIgnoreCase)
            ? OfficialDryRunPackageSourceMode.OperationalSaleReadOnly
            : OfficialDryRunPackageSourceMode.Synthetic;

    private static OfficialDryRunPackagePersistenceStatus ParseStatus(string value) =>
        string.Equals(value, "PreviewBuilt", StringComparison.OrdinalIgnoreCase)
            ? OfficialDryRunPackagePersistenceStatus.PreviewBuilt
            : OfficialDryRunPackagePersistenceStatus.Created;
}
