using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Core.Services.Reporting;
// ProductionOfficialPostingActivation lives in Salryn.Core.Services.

namespace Salryn.Infrastructure.Data.Services;

/// <summary>
/// M5-RC2: single SQLite projection over official EJ + M4-RC2 lifecycle accounting tables.
/// Fail-closed on hash mismatch. No mutations.
/// </summary>
public sealed class SqliteOfficialReportingProjectionService : IOfficialReportingProjectionService
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteOfficialReportingProjectionService(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public Task<Result<OfficialReportingProjection>> ProjectAsync(
        OfficialReportingProjectionRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.RangeEnd <= request.RangeStart)
        {
            return Task.FromResult(Result<OfficialReportingProjection>.Fail(Error.Validation(
                "OfficialReporting.InvalidRange",
                "Range end must be after range start.")));
        }

        try
        {
            using SqliteConnection connection = _connectionFactory.CreateConnection();
            connection.Open();

            List<OfficialReportingDocumentRow> documents = ReadDocuments(connection, request, cancellationToken);
            List<OfficialReportingEventRow> events = ReadEvents(connection, request, cancellationToken);

            long gross = 0, voided = 0, refunded = 0, net = 0;
            long vatable = 0, vat = 0, exempt = 0, zero = 0, sspt = 0, nv = 0;
            long cashRefund = 0;
            int voidedDocs = 0, refundEvents = 0, reprintEvents = 0, adjEvents = 0;
            bool allHashesMatch = true;

            foreach (OfficialReportingDocumentRow doc in documents)
            {
                if (!doc.SnapshotHashMatches)
                {
                    allHashesMatch = false;
                }

                gross = checked(gross + doc.GrossTotalCents);
                refunded = checked(refunded + doc.RefundedTotalCents);
                if (doc.IsVoided)
                {
                    voided = checked(voided + doc.GrossTotalCents);
                    voidedDocs++;
                    // Voided documents contribute 0 net (not double-subtract refunds).
                }
                else
                {
                    net = checked(net + doc.NetTotalCents);
                    vatable = checked(vatable + Math.Max(0, doc.VatableBaseCents - doc.RefundedVatableBaseCents));
                    vat = checked(vat + Math.Max(0, doc.VatAmountCents - doc.RefundedVatAmountCents));
                    exempt = checked(exempt + Math.Max(0, doc.VatExemptCents - doc.RefundedVatExemptCents));
                    zero = checked(zero + Math.Max(0, doc.ZeroRatedCents - doc.RefundedZeroRatedCents));
                    sspt = checked(sspt + Math.Max(0, doc.SsptCents - doc.RefundedSsptCents));
                    nv = checked(nv + Math.Max(0, doc.NonVatExemptCents - doc.RefundedNonVatExemptCents));
                }

                reprintEvents = checked(reprintEvents + doc.ReprintCount);
                refundEvents = checked(refundEvents + doc.RefundCount);
                adjEvents = checked(adjEvents + doc.AdjustmentCount);
            }

            cashRefund = ReadCashSettlements(connection, request);

            if (!allHashesMatch)
            {
                return Task.FromResult(Result<OfficialReportingProjection>.Fail(Error.Validation(
                    "OfficialReporting.SnapshotHashMismatch",
                    "One or more official snapshots failed hash verification.")));
            }

            OfficialReportingProjection projection = new(
                request.RangeStart,
                request.RangeEnd,
                request.BirMachineProfileId,
                request.RegistrationScopeCode,
                request.DocumentKind,
                documents,
                events,
                gross,
                voided,
                refunded,
                net,
                vatable,
                vat,
                exempt,
                zero,
                sspt,
                nv,
                cashRefund,
                documents.Count,
                voidedDocs,
                refundEvents,
                reprintEvents,
                adjEvents,
                ComputeProjectionHash(documents, events, gross, voided, refunded, net),
                AllSnapshotHashesMatch: true,
                ProductionActivationEnabled: ProductionOfficialPostingActivation.IsEnabled,
                BirAuthoritativeESalesSchemaConfirmed: false,
                TransmissionEnabled: false,
                Summary: "Lifecycle-aware official reporting projection. Activation OFF. BIR eSales schema unconfirmed.");

            return Task.FromResult(Result<OfficialReportingProjection>.Success(projection));
        }
        catch (Exception ex) when (ex is SqliteException or FormatException or JsonException or OverflowException)
        {
            return Task.FromResult(Result<OfficialReportingProjection>.Fail(Error.Unexpected(
                "OfficialReporting.ProjectionFailed",
                "Reporting projection failed: " + ex.Message)));
        }
    }

    private static List<OfficialReportingDocumentRow> ReadDocuments(
        SqliteConnection connection,
        OfficialReportingProjectionRequest request,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                e.id, e.sale_id, e.official_document_number, e.created_at,
                e.snapshot_payload_json, e.snapshot_payload_hash, e.document_kind,
                COALESCE(s.lifecycle_state, 'Committed'),
                COALESCE(s.refunded_total_cents, 0),
                COALESCE(s.refunded_vatable_base_cents, 0),
                COALESCE(s.refunded_vat_amount_cents, 0),
                COALESCE(s.refunded_vat_exempt_cents, 0),
                COALESCE(s.refunded_zero_rated_cents, 0),
                COALESCE(s.refunded_sspt_cents, 0),
                COALESCE(s.refunded_nonvat_exempt_cents, 0)
            FROM bir_electronic_journal_entries e
            LEFT JOIN official_document_lifecycle_state s ON s.journal_entry_id = e.id
            WHERE e.created_at >= $start
              AND e.created_at < $end
              AND ($machineId IS NULL OR e.bir_machine_profile_id = $machineId)
              AND e.document_kind = $kind
            ORDER BY e.created_at ASC, e.official_document_number ASC, e.id ASC;
            """;
        command.Parameters.AddWithValue("$start", request.RangeStart.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$end", request.RangeEnd.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue(
            "$machineId",
            request.BirMachineProfileId is Guid m && m != Guid.Empty ? m.ToString("D") : DBNull.Value);
        command.Parameters.AddWithValue("$kind", ToDbKind(request.DocumentKind));

        List<OfficialReportingDocumentRow> rows = new();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            Guid journalId = Guid.Parse(reader.GetString(0));
            Guid? saleId = reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1));
            string docNo = reader.IsDBNull(2) ? "" : reader.GetString(2);
            DateTimeOffset postedAt = DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            string payload = reader.IsDBNull(4) ? "" : reader.GetString(4);
            string hash = reader.IsDBNull(5) ? "" : reader.GetString(5);
            string state = reader.GetString(7);
            long refundedTotal = reader.GetInt64(8);
            long refVb = reader.GetInt64(9);
            long refVa = reader.GetInt64(10);
            long refVe = reader.GetInt64(11);
            long refZr = reader.GetInt64(12);
            long refSs = reader.GetInt64(13);
            long refNv = reader.GetInt64(14);

            if (string.IsNullOrWhiteSpace(payload) || string.IsNullOrWhiteSpace(hash))
            {
                throw new InvalidOperationException("Incomplete official snapshot for " + docNo);
            }

            string recomputed = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
            bool hashOk = string.Equals(recomputed, hash.Trim(), StringComparison.OrdinalIgnoreCase);
            if (!hashOk)
            {
                // Fail closed later via allHashesMatch; still record mismatch.
            }

            SnapshotMoney money = ParseSnapshotMoney(payload);
            bool isVoided = string.Equals(state, "Voided", StringComparison.OrdinalIgnoreCase);
            // Voided: net 0. Partial refund: gross - refunded. FullyRefunded: net 0.
            long net = isVoided || string.Equals(state, "FullyRefunded", StringComparison.OrdinalIgnoreCase)
                ? 0
                : Math.Max(0, money.TotalCents - refundedTotal);

            EventCounts counts = CountEvents(connection, journalId);

            rows.Add(new OfficialReportingDocumentRow(
                journalId,
                saleId,
                docNo,
                postedAt,
                money.Schema,
                hash,
                hashOk,
                state,
                money.TotalCents,
                refundedTotal,
                net,
                isVoided,
                money.VatableBase,
                money.VatAmount,
                money.VatExempt,
                money.ZeroRated,
                money.Sspt,
                money.NonVatExempt,
                refVb,
                refVa,
                refVe,
                refZr,
                refSs,
                refNv,
                counts.Reprint,
                counts.Void,
                counts.Refund,
                counts.Adjustment));
        }

        return rows;
    }

    private static List<OfficialReportingEventRow> ReadEvents(
        SqliteConnection connection,
        OfficialReportingProjectionRequest request,
        CancellationToken cancellationToken)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                ev.id, e.id, e.official_document_number, ev.event_type, ev.created_at,
                ev.reason, ev.cashier_user_id, ev.metadata_json
            FROM bir_electronic_journal_entry_events ev
            INNER JOIN bir_electronic_journal_entries e ON e.id = ev.journal_entry_id
            WHERE e.created_at >= $start
              AND e.created_at < $end
              AND ($machineId IS NULL OR e.bir_machine_profile_id = $machineId)
              AND e.document_kind = $kind
            ORDER BY ev.created_at ASC, ev.id ASC;
            """;
        command.Parameters.AddWithValue("$start", request.RangeStart.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$end", request.RangeEnd.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue(
            "$machineId",
            request.BirMachineProfileId is Guid m && m != Guid.Empty ? m.ToString("D") : DBNull.Value);
        command.Parameters.AddWithValue("$kind", ToDbKind(request.DocumentKind));

        List<OfficialReportingEventRow> rows = new();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            rows.Add(new OfficialReportingEventRow(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.IsDBNull(2) ? "" : reader.GetString(2),
                reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : Guid.Parse(reader.GetString(6)),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return rows;
    }

    private static long ReadCashSettlements(SqliteConnection connection, OfficialReportingProjectionRequest request)
    {
        try
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT COALESCE(SUM(st.amount_cents), 0)
                FROM official_document_lifecycle_settlements st
                INNER JOIN bir_electronic_journal_entries e ON e.id = st.journal_entry_id
                WHERE e.created_at >= $start
                  AND e.created_at < $end
                  AND ($machineId IS NULL OR e.bir_machine_profile_id = $machineId)
                  AND e.document_kind = $kind
                  AND st.method = 'Cash'
                  AND st.status = 'Completed';
                """;
            command.Parameters.AddWithValue("$start", request.RangeStart.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$end", request.RangeEnd.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue(
                "$machineId",
                request.BirMachineProfileId is Guid m && m != Guid.Empty ? m.ToString("D") : DBNull.Value);
            command.Parameters.AddWithValue("$kind", ToDbKind(request.DocumentKind));
            return Convert.ToInt64(command.ExecuteScalar());
        }
        catch (SqliteException)
        {
            return 0;
        }
    }

    private sealed record EventCounts(int Reprint, int Void, int Refund, int Adjustment);

    private static EventCounts CountEvents(SqliteConnection connection, Guid journalId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                COALESCE(SUM(CASE WHEN event_type = 'REPRINT' THEN 1 ELSE 0 END), 0),
                COALESCE(SUM(CASE WHEN event_type = 'VOID' THEN 1 ELSE 0 END), 0),
                COALESCE(SUM(CASE WHEN event_type = 'REFUND' THEN 1 ELSE 0 END), 0),
                COALESCE(SUM(CASE WHEN event_type = 'ADJUSTMENT' THEN 1 ELSE 0 END), 0)
            FROM bir_electronic_journal_entry_events
            WHERE journal_entry_id = $id;
            """;
        command.Parameters.AddWithValue("$id", journalId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return new EventCounts(0, 0, 0, 0);
        }

        return new EventCounts(
            Convert.ToInt32(reader.GetValue(0)),
            Convert.ToInt32(reader.GetValue(1)),
            Convert.ToInt32(reader.GetValue(2)),
            Convert.ToInt32(reader.GetValue(3)));
    }

    private sealed record SnapshotMoney(
        string Schema,
        long TotalCents,
        long VatableBase,
        long VatAmount,
        long VatExempt,
        long ZeroRated,
        long Sspt,
        long NonVatExempt);

    private static SnapshotMoney ParseSnapshotMoney(string payload)
    {
        using JsonDocument doc = JsonDocument.Parse(payload);
        JsonElement root = doc.RootElement;
        string schema = root.TryGetProperty("schema", out JsonElement s) ? s.GetString() ?? "" : "";
        long total = 0;
        if (root.TryGetProperty("amounts", out JsonElement amounts) &&
            amounts.TryGetProperty("totalCents", out JsonElement t) &&
            t.ValueKind == JsonValueKind.Number)
        {
            total = t.GetInt64();
        }

        long vb = 0, va = 0, ve = 0, zr = 0, ss = 0, nv = 0;
        if (root.TryGetProperty("taxBuckets", out JsonElement tb) &&
            tb.ValueKind == JsonValueKind.Object &&
            tb.TryGetProperty("buckets", out JsonElement buckets) &&
            buckets.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement b in buckets.EnumerateArray())
            {
                string kind = b.TryGetProperty("kind", out JsonElement k) ? k.GetString() ?? "" : "";
                long sales = b.TryGetProperty("salesAmountCents", out JsonElement sa) && sa.ValueKind == JsonValueKind.Number ? sa.GetInt64() : 0;
                long tax = b.TryGetProperty("taxAmountCents", out JsonElement ta) && ta.ValueKind == JsonValueKind.Number ? ta.GetInt64() : 0;
                switch (kind)
                {
                    case "VatableSales": vb = sales; break;
                    case "VatAmount": va = tax; break;
                    case "VatExemptSales": ve = sales; break;
                    case "ZeroRatedSales": zr = sales; break;
                    case "SsptSales":
                    case "NonVatSales": ss = sales; break;
                    case "NonVatExemptSales": nv = sales; break;
                }
            }
        }
        else if (root.TryGetProperty("lines", out JsonElement lines) && lines.ValueKind == JsonValueKind.Array)
        {
            // V3 line-level sum if aggregate buckets missing.
            foreach (JsonElement line in lines.EnumerateArray())
            {
                vb += line.TryGetProperty("vatableBaseCents", out JsonElement x) && x.ValueKind == JsonValueKind.Number ? x.GetInt64() : 0;
                va += line.TryGetProperty("vatAmountCents", out JsonElement y) && y.ValueKind == JsonValueKind.Number ? y.GetInt64() : 0;
                ve += line.TryGetProperty("vatExemptCents", out JsonElement z) && z.ValueKind == JsonValueKind.Number ? z.GetInt64() : 0;
                zr += line.TryGetProperty("zeroRatedCents", out JsonElement w) && w.ValueKind == JsonValueKind.Number ? w.GetInt64() : 0;
                ss += line.TryGetProperty("ssptCents", out JsonElement u) && u.ValueKind == JsonValueKind.Number ? u.GetInt64() : 0;
                nv += line.TryGetProperty("nonVatExemptCents", out JsonElement v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : 0;
            }
        }

        return new SnapshotMoney(schema, total, vb, va, ve, zr, ss, nv);
    }

    private static string ToDbKind(ElectronicJournalDocumentKind kind) => kind switch
    {
        ElectronicJournalDocumentKind.OfficialReceipt => "OFFICIAL_RECEIPT",
        _ => "OFFICIAL_INVOICE"
    };

    private static string ComputeProjectionHash(
        IReadOnlyList<OfficialReportingDocumentRow> docs,
        IReadOnlyList<OfficialReportingEventRow> events,
        long gross,
        long voided,
        long refunded,
        long net)
    {
        StringBuilder sb = new();
        sb.Append(gross).Append('|').Append(voided).Append('|').Append(refunded).Append('|').Append(net).Append('|');
        foreach (OfficialReportingDocumentRow d in docs)
        {
            sb.Append(d.JournalEntryId).Append(':').Append(d.NetTotalCents).Append(':').Append(d.LifecycleState).Append(';');
        }

        foreach (OfficialReportingEventRow e in events)
        {
            sb.Append(e.EventId).Append(':').Append(e.EventType).Append(';');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }
}
