using System.Text.Json;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Official;
using Salryn.Core.Services;

namespace Salryn.Infrastructure.Data.Services;

/// <summary>
/// RMO-BIR-M8 internal dry-run package persistence.
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

    public SqliteHostOfficialDryRunPackageService(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
        _planner = new SqliteHostOfficialDryRunPostingPlanner(connectionFactory);
        _preview = new HostOfficialReportPreviewService();
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
        Guid id = Guid.NewGuid();
        string publicId = "DRY-" + id.ToString("N")[..12].ToUpperInvariant();
        DateTimeOffset createdAt = DateTimeOffset.UtcNow;

        List<OfficialDryRunPackageWarning> warnings = plan.Warnings
            .Select(w => new OfficialDryRunPackageWarning(w.Code, w.Message))
            .ToList();
        warnings.Add(new OfficialDryRunPackageWarning(
            "HostOfficialDryRunPackage.InternalOnly",
            "Internal dry-run package only. Not an official receipt or invoice."));
        warnings.Add(new OfficialDryRunPackageWarning(
            "HostOfficialDryRunPackage.SourceSaleDeferred",
            "Operational sale read-only source mode is deferred; package is synthetic."));

        List<OfficialDryRunPackageBlocker> blockers = plan.Blockers
            .Select(b => new OfficialDryRunPackageBlocker(b.Code, b.Message))
            .ToList();

        string summary =
            "Internal dry-run package stored for engineering. " +
            "No official number issued. No sales/sequence/EJ live mutation. No legal output.";

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
                        $id, $publicId, $createdAt, $userId, 'Synthetic', NULL,
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

        return Result<OfficialDryRunPackageContract>.Success(ToContract(
            id, publicId, createdAt, createdByUserId, plan, preview, warnings, blockers, summary));
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
                SELECT id, public_id, created_at_utc, created_by_user_id, source_mode,
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

            OfficialDryRunPostingPlan plan = JsonSerializer.Deserialize<OfficialDryRunPostingPlan>(
                reader.GetString(5), JsonOptions)
                ?? throw new InvalidOperationException("plan_json deserialize failed.");
            OfficialReportPreviewPackageContract? preview = reader.IsDBNull(6)
                ? null
                : JsonSerializer.Deserialize<OfficialReportPreviewPackageContract>(
                    reader.GetString(6), JsonOptions);
            List<OfficialDryRunPackageWarning> warnings = reader.IsDBNull(7)
                ? new List<OfficialDryRunPackageWarning>()
                : JsonSerializer.Deserialize<List<OfficialDryRunPackageWarning>>(reader.GetString(7), JsonOptions)
                  ?? new List<OfficialDryRunPackageWarning>();
            List<OfficialDryRunPackageBlocker> blockers = reader.IsDBNull(8)
                ? new List<OfficialDryRunPackageBlocker>()
                : JsonSerializer.Deserialize<List<OfficialDryRunPackageBlocker>>(reader.GetString(8), JsonOptions)
                  ?? new List<OfficialDryRunPackageBlocker>();

            return Task.FromResult(Result<OfficialDryRunPackageContract>.Success(ToContract(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                DateTimeOffset.Parse(reader.GetString(2)),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                plan,
                preview,
                warnings,
                blockers,
                reader.IsDBNull(9) ? string.Empty : reader.GetString(9))));
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            return Task.FromResult(Result<OfficialDryRunPackageContract>.Fail(Error.Unexpected(
                "HostOfficialDryRunPackage.GetFailed",
                "Failed to load dry-run package: " + ex.Message)));
        }
    }

    private static OfficialDryRunPackageContract ToContract(
        Guid id,
        string publicId,
        DateTimeOffset createdAt,
        string? createdByUserId,
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
            SourceMode: OfficialDryRunPackageSourceMode.Synthetic,
            SourceSaleId: null,
            Status: OfficialDryRunPackagePersistenceStatus.PreviewBuilt,
            Flags: new OfficialDryRunPackageFlags(
                DryRunOnly: true,
                NonOfficial: true,
                LiveOfficialPostingAllowed: false,
                OfficialCheckoutAllocationEnabled: false,
                CheckoutUsesAllocator: false,
                SequenceWillMutate: false,
                SalesWillMutate: false,
                EjWillAppendLive: false,
                OutputWillGenerate: false,
                OfficialReceiptIssued: false,
                ComplianceClaimed: false),
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
