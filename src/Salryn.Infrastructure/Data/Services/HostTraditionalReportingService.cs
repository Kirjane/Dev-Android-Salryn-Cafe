using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Official;
using Salryn.Core.Services;
using Salryn.Core.Services.Reporting;
// OfficialSafetyConstants / ProductionOfficialPostingActivation

namespace Salryn.Infrastructure.Data.Services;

/// <summary>
/// M5-RC2 host traditional reporting: X/Z/EJ/eSales over canonical lifecycle-aware projection.
/// </summary>
public sealed class HostTraditionalReportingService : IHostTraditionalReportingService
{
    private readonly IOfficialReportingProjectionService _projection;
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly ITraditionalESalesSchemaProvider _schemaProvider;
    private readonly Func<Guid> _idFactory;
    private readonly Func<DateTimeOffset> _clock;

    public HostTraditionalReportingService(
        IOfficialReportingProjectionService projection,
        SqliteConnectionFactory connectionFactory,
        ITraditionalESalesSchemaProvider? schemaProvider = null,
        Func<Guid>? idFactory = null,
        Func<DateTimeOffset>? clock = null)
    {
        _projection = projection ?? throw new ArgumentNullException(nameof(projection));
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _schemaProvider = schemaProvider ?? new UnconfirmedTraditionalESalesSchemaProvider();
        _idFactory = idFactory ?? Guid.NewGuid;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public Result<TraditionalReportingStatus> GetStatus()
    {
        return Result<TraditionalReportingStatus>.Success(new TraditionalReportingStatus(
            ProductionActivationEnabled: ProductionOfficialPostingActivation.IsEnabled,
            LiveOfficialPostingAllowed: false,
            XReadingEnginePresent: true,
            ZReadingEnginePresent: true,
            EjExportImplemented: true,
            TraditionalESalesDraftImplemented: true,
            BirESalesSchemaConfirmed: _schemaProvider.BirAuthoritativeSchemaConfirmed,
            TransmissionEnabled: _schemaProvider.TransmissionEnabled,
            GrandTotalResetImplemented: false,
            LifecycleAwareTotals: true,
            Summary:
            "Traditional reporting: lifecycle-aware X/Z/EJ/eSales draft. BIR schema unconfirmed. Transmission disabled. Activation OFF.",
            Notes: new[]
            {
                _schemaProvider.SchemaBoundaryNote,
                "Grand-total reset is not implemented without source-backed design.",
                "Exports do not submit to BIR.",
                "Manual acceptance remains pending.",
                OfficialSafetyConstants.LivePostingBlocked,
                ProductionOfficialPostingActivation.NotAComplianceClaimNote
            }));
    }

    public async Task<Result<TraditionalXReadingView>> PreviewXReadingAsync(
        OfficialReportingProjectionRequest request,
        CancellationToken cancellationToken = default)
    {
        Result<OfficialReportingProjection> projection =
            await _projection.ProjectAsync(request, cancellationToken).ConfigureAwait(false);
        if (projection.IsFailure)
        {
            return Result<TraditionalXReadingView>.Fail(projection.ErrorDetail);
        }

        return Result<TraditionalXReadingView>.Success(new TraditionalXReadingView(
            projection.Value,
            "X-reading preview (read-only). Net after voids/refunds. Sequence unchanged."));
    }

    public async Task<Result<TraditionalZReadingView>> FinalizeZReadingAsync(
        OfficialReportingProjectionRequest request,
        Guid actorUserId,
        string? terminalId,
        string? failureInjectionStage = null,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty)
        {
            return Result<TraditionalZReadingView>.Fail(Error.Validation(
                "OfficialReporting.ActorRequired",
                "Authenticated actor required for Z finalization."));
        }

        Result<OfficialReportingProjection> projection =
            await _projection.ProjectAsync(request, cancellationToken).ConfigureAwait(false);
        if (projection.IsFailure)
        {
            return Result<TraditionalZReadingView>.Fail(projection.ErrorDetail);
        }

        try
        {
            using SqliteConnection connection = _connectionFactory.CreateConnection();
            connection.Open();
            using SqliteTransaction tx = connection.BeginTransaction();

            Inject(failureInjectionStage, "before_z_insert");

            // Duplicate/overlap guard (same unique key as bir_z_readings index).
            Result guard = GuardZRange(connection, tx, request);
            if (guard.IsFailure)
            {
                tx.Rollback();
                return Result<TraditionalZReadingView>.Fail(guard.ErrorDetail);
            }

            Guid zId = _idFactory();
            DateTimeOffset now = _clock();
            string kind = request.DocumentKind == ElectronicJournalDocumentKind.OfficialReceipt
                ? "OFFICIAL_RECEIPT"
                : "OFFICIAL_INVOICE";

            OfficialReportingProjection p = projection.Value;
            string firstDoc = p.Documents.FirstOrDefault()?.OfficialDocumentNumber;
            string lastDoc = p.Documents.LastOrDefault()?.OfficialDocumentNumber;
            string metadata = JsonSerializer.Serialize(new
            {
                schema = "SALRYN_Z_LIFECYCLE_CONTROL_TOTALS_V1",
                projectionContentSha256 = p.ProjectionContentSha256,
                grossPostedSalesCents = p.GrossPostedSalesCents,
                voidedSalesCents = p.VoidedSalesCents,
                refundedValueCents = p.RefundedValueCents,
                netSalesCents = p.NetSalesCents,
                vatableSalesCents = p.VatableSalesCents,
                vatAmountCents = p.VatAmountCents,
                vatExemptSalesCents = p.VatExemptSalesCents,
                zeroRatedSalesCents = p.ZeroRatedSalesCents,
                ssptSalesCents = p.SsptSalesCents,
                nonVatExemptSalesCents = p.NonVatExemptSalesCents,
                cashSettlementRefundCents = p.CashSettlementRefundCents,
                reprintEventCount = p.ReprintEventCount,
                refundEventCount = p.RefundEventCount,
                adjustmentEventCount = p.AdjustmentEventCount,
                birAuthoritativeESalesSchemaConfirmed = false,
                transmissionEnabled = false,
                productionActivationEnabled = ProductionOfficialPostingActivation.IsEnabled
            });

            Inject(failureInjectionStage, "after_metadata");

            using (SqliteCommand insert = connection.CreateCommand())
            {
                insert.Transaction = tx;
                insert.CommandText = """
                    INSERT INTO bir_z_readings (
                        id, bir_machine_profile_id, registration_scope_code, document_kind,
                        range_started_at, range_ended_at, finalized_at, entry_count,
                        first_official_document_number, last_official_document_number,
                        gross_sales_cents, subtotal_cents, discount_total_cents, tax_total_cents,
                        net_sales_cents, total_sales_cents, payment_summary_json, tax_mode_summary_json,
                        evidence_manifest_hash, all_snapshot_hashes_match, created_by_user_id, terminal_id,
                        status, created_at, metadata_json
                    ) VALUES (
                        $id, $machineId, $scope, $kind,
                        $start, $end, $finalized, $count,
                        $first, $last,
                        $gross, $subtotal, 0, $tax,
                        $net, $total, $payments, $taxModes,
                        $hash, $match, $actor, $terminal,
                        'FINALIZED', $createdAt, $metadata
                    );
                    """;
                insert.Parameters.AddWithValue("$id", zId.ToString("D"));
                insert.Parameters.AddWithValue(
                    "$machineId",
                    request.BirMachineProfileId is Guid m && m != Guid.Empty ? m.ToString("D") : DBNull.Value);
                insert.Parameters.AddWithValue(
                    "$scope",
                    string.IsNullOrWhiteSpace(request.RegistrationScopeCode)
                        ? "LOCAL_SINGLE_STORE"
                        : request.RegistrationScopeCode.Trim());
                insert.Parameters.AddWithValue("$kind", kind);
                insert.Parameters.AddWithValue("$start", request.RangeStart.ToString("O", CultureInfo.InvariantCulture));
                insert.Parameters.AddWithValue("$end", request.RangeEnd.ToString("O", CultureInfo.InvariantCulture));
                insert.Parameters.AddWithValue("$finalized", now.ToString("O", CultureInfo.InvariantCulture));
                insert.Parameters.AddWithValue("$count", p.DocumentCount);
                insert.Parameters.AddWithValue("$first", (object?)firstDoc ?? DBNull.Value);
                insert.Parameters.AddWithValue("$last", (object?)lastDoc ?? DBNull.Value);
                insert.Parameters.AddWithValue("$gross", p.GrossPostedSalesCents);
                insert.Parameters.AddWithValue("$subtotal", p.NetSalesCents);
                insert.Parameters.AddWithValue("$tax", p.VatAmountCents);
                insert.Parameters.AddWithValue("$net", p.NetSalesCents);
                insert.Parameters.AddWithValue("$total", p.NetSalesCents);
                insert.Parameters.AddWithValue("$payments", JsonSerializer.Serialize(new[]
                {
                    new { method = "CashRefunds", amountCents = p.CashSettlementRefundCents }
                }));
                insert.Parameters.AddWithValue("$taxModes", JsonSerializer.Serialize(new
                {
                    vatable = p.VatableSalesCents,
                    vat = p.VatAmountCents,
                    exempt = p.VatExemptSalesCents,
                    zeroRated = p.ZeroRatedSalesCents,
                    sspt = p.SsptSalesCents,
                    nonVatExempt = p.NonVatExemptSalesCents
                }));
                insert.Parameters.AddWithValue("$hash", p.ProjectionContentSha256);
                insert.Parameters.AddWithValue("$match", p.AllSnapshotHashesMatch ? 1 : 0);
                insert.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
                insert.Parameters.AddWithValue("$terminal", (object?)terminalId ?? DBNull.Value);
                insert.Parameters.AddWithValue("$createdAt", now.ToString("O", CultureInfo.InvariantCulture));
                insert.Parameters.AddWithValue("$metadata", metadata);
                insert.ExecuteNonQuery();
            }

            Inject(failureInjectionStage, "before_commit");
            tx.Commit();

            return Result<TraditionalZReadingView>.Success(new TraditionalZReadingView(
                zId,
                p.ProjectionContentSha256,
                p.ProjectionContentSha256,
                p,
                "Z-reading finalized with lifecycle control totals. Sales sequence not advanced. EJ not rewritten."));
        }
        catch (FailureInjectionException ex)
        {
            return Result<TraditionalZReadingView>.Fail(Error.Unexpected(
                "OfficialReporting.InjectedFailure",
                "Injected Z failure at " + ex.Stage));
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return Result<TraditionalZReadingView>.Fail(Error.Conflict(
                "OfficialZReading.DuplicateRange",
                "A finalized Z-reading already exists for this machine/scope/range."));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException)
        {
            return Result<TraditionalZReadingView>.Fail(Error.Unexpected(
                "OfficialReporting.ZFinalizeFailed",
                ex.Message));
        }
    }

    public async Task<Result<TraditionalReportingExportPackage>> ExportEjAsync(
        OfficialReportingProjectionRequest request,
        CancellationToken cancellationToken = default)
    {
        Result<OfficialReportingProjection> projection =
            await _projection.ProjectAsync(request, cancellationToken).ConfigureAwait(false);
        if (projection.IsFailure)
        {
            return Result<TraditionalReportingExportPackage>.Fail(projection.ErrorDetail);
        }

        OfficialReportingProjection p = projection.Value;
        StringBuilder sb = new();
        sb.AppendLine("SALRYN_TRAD_EJ_EXPORT_V2");
        sb.AppendLine("Lifecycle-aware examiner-readable electronic journal export.");
        sb.AppendLine("Not a BIR-accredited export package. Production activation OFF.");
        sb.Append("RangeStart=").Append(p.RangeStart.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)).AppendLine();
        sb.Append("RangeEnd=").Append(p.RangeEnd.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)).AppendLine();
        sb.Append("DocumentCount=").Append(p.DocumentCount).AppendLine();
        sb.Append("EventCount=").Append(p.Events.Count).AppendLine();
        sb.Append("GrossPosted=").Append(p.GrossPostedSalesCents).AppendLine();
        sb.Append("Voided=").Append(p.VoidedSalesCents).AppendLine();
        sb.Append("Refunded=").Append(p.RefundedValueCents).AppendLine();
        sb.Append("NetSales=").Append(p.NetSalesCents).AppendLine();
        sb.Append("ProjectionSha256=").Append(p.ProjectionContentSha256).AppendLine();
        sb.AppendLine("---DOCUMENTS---");
        foreach (OfficialReportingDocumentRow d in p.Documents)
        {
            sb.Append(d.OfficialDocumentNumber).Append('\t')
                .Append(d.PostedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)).Append('\t')
                .Append(d.LifecycleState).Append('\t')
                .Append(d.GrossTotalCents).Append('\t')
                .Append(d.RefundedTotalCents).Append('\t')
                .Append(d.NetTotalCents).Append('\t')
                .Append(d.SnapshotPayloadHash).AppendLine();
        }

        sb.AppendLine("---EVENTS---");
        foreach (OfficialReportingEventRow e in p.Events)
        {
            sb.Append(e.EventType).Append('\t')
                .Append(e.OfficialDocumentNumber).Append('\t')
                .Append(e.CreatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)).Append('\t')
                .Append(e.Reason ?? "").AppendLine();
        }

        string text = sb.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
        string hash = Sha256(text);
        return Result<TraditionalReportingExportPackage>.Success(new TraditionalReportingExportPackage(
            "EJ",
            "SALRYN_TRAD_EJ_EXPORT_V2",
            p.RangeStart,
            p.RangeEnd,
            text,
            hash,
            p.Events.Count,
            p.NetSalesCents,
            false,
            false,
            "Lifecycle-aware EJ export generated. Does not submit to BIR.",
            p));
    }

    public async Task<Result<TraditionalReportingExportPackage>> ExportESalesDraftAsync(
        OfficialReportingProjectionRequest request,
        CancellationToken cancellationToken = default)
    {
        Result<OfficialReportingProjection> projection =
            await _projection.ProjectAsync(request, cancellationToken).ConfigureAwait(false);
        if (projection.IsFailure)
        {
            return Result<TraditionalReportingExportPackage>.Fail(projection.ErrorDetail);
        }

        OfficialReportingProjection p = projection.Value;
        StringBuilder sb = new();
        sb.AppendLine("schema_code,official_document_number,posted_at_utc,lifecycle_state,gross_cents,refunded_cents,net_cents,vatable_base_cents,vat_amount_cents,vat_exempt_cents,zero_rated_cents,sspt_cents,nonvat_exempt_cents,is_voided");
        foreach (OfficialReportingDocumentRow d in p.Documents)
        {
            // Internal-only adjustment artifacts are not separate EJ sale docs; document rows are official sales.
            sb.Append(Escape(_schemaProvider.InternalSchemaCode)).Append(',');
            sb.Append(Escape(d.OfficialDocumentNumber)).Append(',');
            sb.Append(d.PostedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(Escape(d.LifecycleState)).Append(',');
            sb.Append(d.GrossTotalCents).Append(',');
            sb.Append(d.RefundedTotalCents).Append(',');
            sb.Append(d.NetTotalCents).Append(',');
            sb.Append(Math.Max(0, d.VatableBaseCents - d.RefundedVatableBaseCents)).Append(',');
            sb.Append(Math.Max(0, d.VatAmountCents - d.RefundedVatAmountCents)).Append(',');
            sb.Append(Math.Max(0, d.VatExemptCents - d.RefundedVatExemptCents)).Append(',');
            sb.Append(Math.Max(0, d.ZeroRatedCents - d.RefundedZeroRatedCents)).Append(',');
            sb.Append(Math.Max(0, d.SsptCents - d.RefundedSsptCents)).Append(',');
            sb.Append(Math.Max(0, d.NonVatExemptCents - d.RefundedNonVatExemptCents)).Append(',');
            sb.Append(d.IsVoided ? "1" : "0");
            sb.Append('\n');
        }

        // Control footer
        sb.AppendLine("# control_totals");
        sb.Append("# net_sales_cents=").Append(p.NetSalesCents).AppendLine();
        sb.Append("# gross_posted_cents=").Append(p.GrossPostedSalesCents).AppendLine();
        sb.Append("# refunded_cents=").Append(p.RefundedValueCents).AppendLine();
        sb.Append("# voided_cents=").Append(p.VoidedSalesCents).AppendLine();
        sb.Append("# projection_sha256=").Append(p.ProjectionContentSha256).AppendLine();
        sb.Append("# bir_authoritative_schema_confirmed=").Append(_schemaProvider.BirAuthoritativeSchemaConfirmed ? "true" : "false").AppendLine();
        sb.Append("# transmission_enabled=").Append(_schemaProvider.TransmissionEnabled ? "true" : "false").AppendLine();

        string csv = sb.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
        return Result<TraditionalReportingExportPackage>.Success(new TraditionalReportingExportPackage(
            "ESALES_DRAFT",
            _schemaProvider.InternalSchemaCode,
            p.RangeStart,
            p.RangeEnd,
            csv,
            Sha256(csv),
            p.DocumentCount,
            p.NetSalesCents,
            _schemaProvider.BirAuthoritativeSchemaConfirmed,
            _schemaProvider.TransmissionEnabled,
            "Internal eSales draft from lifecycle-aware projection. " + _schemaProvider.SchemaBoundaryNote,
            p));
    }

    public async Task<Result<TraditionalGrandTotalSnapshot>> GetGrandTotalSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        Result<OfficialReportingProjection> projection = await _projection.ProjectAsync(
            new OfficialReportingProjectionRequest(
                new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero),
                DateTimeOffset.UtcNow.AddDays(1),
                null,
                "LOCAL_SINGLE_STORE",
                ElectronicJournalDocumentKind.OfficialInvoice),
            cancellationToken).ConfigureAwait(false);

        if (projection.IsFailure)
        {
            return Result<TraditionalGrandTotalSnapshot>.Fail(projection.ErrorDetail);
        }

        OfficialReportingProjection p = projection.Value;
        return Result<TraditionalGrandTotalSnapshot>.Success(new TraditionalGrandTotalSnapshot(
            p.DocumentCount,
            p.GrossPostedSalesCents,
            p.NetSalesCents,
            p.RefundedValueCents,
            p.VoidedSalesCents,
            ResetCounterImplemented: false,
            Summary: "Grand totals from lifecycle-aware projection. Reset not implemented."));
    }

    private static Result GuardZRange(
        SqliteConnection connection,
        SqliteTransaction tx,
        OfficialReportingProjectionRequest request)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            SELECT COUNT(*)
            FROM bir_z_readings
            WHERE status = 'FINALIZED'
              AND document_kind = $kind
              AND range_started_at = $start
              AND range_ended_at = $end
              AND COALESCE(bir_machine_profile_id, '') = COALESCE($machineId, '')
              AND COALESCE(registration_scope_code, '') = COALESCE($scope, '');
            """;
        command.Parameters.AddWithValue(
            "$kind",
            request.DocumentKind == ElectronicJournalDocumentKind.OfficialReceipt ? "OFFICIAL_RECEIPT" : "OFFICIAL_INVOICE");
        command.Parameters.AddWithValue("$start", request.RangeStart.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$end", request.RangeEnd.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue(
            "$machineId",
            request.BirMachineProfileId is Guid m && m != Guid.Empty ? m.ToString("D") : "");
        command.Parameters.AddWithValue(
            "$scope",
            string.IsNullOrWhiteSpace(request.RegistrationScopeCode) ? "LOCAL_SINGLE_STORE" : request.RegistrationScopeCode.Trim());
        int count = Convert.ToInt32(command.ExecuteScalar());
        if (count > 0)
        {
            return Result.Fail(Error.Conflict(
                "OfficialZReading.DuplicateRange",
                "A finalized Z-reading already exists for this range."));
        }

        return Result.Success();
    }

    private static void Inject(string? stage, string expected)
    {
        if (!string.IsNullOrWhiteSpace(stage) &&
            string.Equals(stage, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new FailureInjectionException(expected);
        }
    }

    private sealed class FailureInjectionException : Exception
    {
        public string Stage { get; }
        public FailureInjectionException(string stage) => Stage = stage;
    }

    private static string Sha256(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static string Escape(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n'))
        {
            return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        return value;
    }
}
