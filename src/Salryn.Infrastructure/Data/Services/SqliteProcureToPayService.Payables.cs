using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models.ProcureToPay;

namespace Salryn.Infrastructure.Data.Services;

public sealed partial class SqliteProcureToPayService
{
    public async Task<Result<IReadOnlyList<SupplierInvoiceModel>>> GetSupplierInvoicesAsync(
        Guid? supplierId = null,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        List<Guid> ids = new();
        await using (SqliteCommand cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id FROM supplier_invoices
                WHERE ($supId IS NULL OR supplier_id = $supId)
                  AND ($status = '' OR status = $status)
                ORDER BY due_date ASC, created_at DESC;
                """;
            cmd.Parameters.AddWithValue("$supId", supplierId?.ToString("D") ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$status", status?.Trim() ?? string.Empty);
            await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                ids.Add(Guid.Parse(reader.GetString(0)));
            }
        }

        DateOnly today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        List<SupplierInvoiceModel> list = new();
        foreach (Guid id in ids)
        {
            SupplierInvoiceModel? inv = await LoadSupplierInvoiceAsync(connection, null, id, today, cancellationToken);
            if (inv is not null) list.Add(inv);
        }

        return Result<IReadOnlyList<SupplierInvoiceModel>>.Success(list);
    }

    public async Task<Result<SupplierInvoiceModel>> GetSupplierInvoiceByIdAsync(
        Guid supplierInvoiceId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        DateOnly today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        SupplierInvoiceModel? inv = await LoadSupplierInvoiceAsync(connection, null, supplierInvoiceId, today, cancellationToken);
        return inv is not null
            ? Result<SupplierInvoiceModel>.Success(inv)
            : Result<SupplierInvoiceModel>.Fail(Error.NotFound("Lpi.Payables.InvoiceNotFound", "Supplier invoice was not found."));
    }

    public async Task<Result<SupplierInvoiceModel>> CreateSupplierInvoiceAsync(
        CreateSupplierInvoiceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.SupplierId == Guid.Empty || request.RecordedByUserId == Guid.Empty)
        {
            return Result<SupplierInvoiceModel>.Fail(
                Error.Validation("Lpi.Payables.MissingFields", "Supplier and recording user are required."));
        }
        if (string.IsNullOrWhiteSpace(request.SupplierInvoiceNo))
        {
            return Result<SupplierInvoiceModel>.Fail(
                Error.Validation("Lpi.Payables.InvoiceNoRequired", "Supplier invoice number is required."));
        }
        if (request.Lines is null || request.Lines.Count == 0)
        {
            return Result<SupplierInvoiceModel>.Fail(
                Error.Validation("Lpi.Payables.LinesRequired", "At least one supplier invoice line is required."));
        }
        if (request.DiscountCents < 0 || request.WithholdingTaxCents < 0)
        {
            return Result<SupplierInvoiceModel>.Fail(
                Error.Validation("Lpi.Payables.NegativeAdjustments", "Discounts and withholding tax cannot be negative."));
        }

        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        SqlitePragma.Apply(connection);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        LpiSupplier? supplier = await FindSupplierByIdAsync(connection, transaction, request.SupplierId, cancellationToken);
        if (supplier is null)
        {
            return Result<SupplierInvoiceModel>.Fail(Error.NotFound("Lpi.Payables.SupplierNotFound", "Supplier was not found."));
        }

        string normalizedInvNo = NormalizeInvoiceNumber(request.SupplierInvoiceNo);
        if (await DuplicateSupplierInvoiceExistsAsync(connection, transaction, request.SupplierId, normalizedInvNo, cancellationToken))
        {
            return Result<SupplierInvoiceModel>.Fail(
                Error.Conflict(
                    "Lpi.Payables.DuplicateSupplierInvoice",
                    $"Duplicate supplier invoice prevented: Invoice '{request.SupplierInvoiceNo.Trim()}' has already been recorded for supplier '{supplier.Name}'."));
        }

        PurchaseOrderModel? po = null;
        if (request.PurchaseOrderId.HasValue)
        {
            po = await LoadPurchaseOrderAsync(connection, transaction, request.PurchaseOrderId.Value, cancellationToken);
            if (po is null || po.SupplierId != request.SupplierId)
            {
                return Result<SupplierInvoiceModel>.Fail(
                    Error.Validation("Lpi.Payables.InvalidPo", "Linked Purchase Order was not found or belongs to a different supplier."));
            }
        }

        ReceivingReportModel? rr = null;
        if (request.ReceivingReportId.HasValue)
        {
            rr = await LoadReceivingReportAsync(connection, transaction, request.ReceivingReportId.Value, cancellationToken);
            if (rr is null || rr.SupplierId != request.SupplierId || rr.Status != "Posted")
            {
                return Result<SupplierInvoiceModel>.Fail(
                    Error.Validation("Lpi.Payables.InvalidRr", "Linked Receiving Report must belong to the supplier and be in Posted status."));
            }

            if (po is not null && rr.PurchaseOrderId != po.Id)
            {
                return Result<SupplierInvoiceModel>.Fail(
                    Error.Validation("Lpi.Payables.PoRrMismatch", "The linked Receiving Report does not belong to the linked Purchase Order."));
            }

            if (po is null)
            {
                po = await LoadPurchaseOrderAsync(connection, transaction, rr.PurchaseOrderId, cancellationToken);
            }
        }

        Dictionary<Guid, PurchaseOrderLineModel> poLineMap = po?.Lines.ToDictionary(x => x.Id) ?? new();
        Dictionary<Guid, ReceivingReportLineModel> rrLineMap = rr?.Lines.ToDictionary(x => x.Id) ?? new();

        long subtotalCents = 0;
        long totalTaxCents = 0;
        bool hasQtyVariance = false;
        bool hasPriceVariance = false;
        List<string> varianceMessages = new();
        HashSet<Guid> seenRrLineIds = new();
        HashSet<Guid> seenPoLineIdsWithoutRr = new();

        var preparedLines = new List<(
            CreateSupplierInvoiceLineInput Input,
            Guid? ResolvedPoLineId,
            Guid? ResolvedRrLineId,
            long PoOrdQtyX1000,
            long RrAccQtyX1000,
            long PoUnitCostCents,
            long LineTotalCents,
            long QtyVarX1000,
            long CostVarCents,
            string LineMatchStatus)>();

        foreach (var line in request.Lines)
        {
            if (line is null || string.IsNullOrWhiteSpace(line.ItemDescription) || line.InvoicedQtyX1000 <= 0 || line.InvoicedUnitCostCents <= 0 || line.TaxCents < 0)
            {
                return Result<SupplierInvoiceModel>.Fail(
                    Error.Validation("Lpi.Payables.InvalidLine", "Each invoice line requires description, positive invoiced quantity, and positive unit cost."));
            }

            if (line.ReceivingReportLineId.HasValue && !seenRrLineIds.Add(line.ReceivingReportLineId.Value))
            {
                return Result<SupplierInvoiceModel>.Fail(
                    Error.Validation("Lpi.Payables.DuplicateReceivingLine",
                        $"Duplicate receiving report line '{line.ReceivingReportLineId.Value:D}' in the same supplier invoice request is not allowed."));
            }

            PurchaseOrderLineModel? poLine = line.PurchaseOrderLineId.HasValue && poLineMap.TryGetValue(line.PurchaseOrderLineId.Value, out var pl) ? pl : null;
            ReceivingReportLineModel? rrLine = line.ReceivingReportLineId.HasValue && rrLineMap.TryGetValue(line.ReceivingReportLineId.Value, out var rl) ? rl : null;

            if (poLine is null && rrLine is not null && poLineMap.TryGetValue(rrLine.PurchaseOrderLineId, out var derivedPl))
            {
                poLine = derivedPl;
            }

            if (poLine is null && line.ProductId.HasValue && po is not null)
            {
                var matchingPoLines = po.Lines.Where(x => x.ProductId == line.ProductId.Value).ToList();
                if (matchingPoLines.Count == 1)
                {
                    poLine = matchingPoLines[0];
                }
                else if (matchingPoLines.Count > 1)
                {
                    return Result<SupplierInvoiceModel>.Fail(
                        Error.Validation(
                            "Lpi.Payables.AmbiguousPoLine",
                            $"Multiple Purchase Order lines found for product '{line.ItemDescription}' on PO '{po.PoNumber}'. Explicit PurchaseOrderLineId is required to disambiguate."));
                }
            }

            if (rrLine is null && line.ProductId.HasValue && rr is not null)
            {
                var matchingRrLines = rr.Lines.Where(x => x.ProductId == line.ProductId.Value).ToList();
                if (matchingRrLines.Count == 1)
                {
                    rrLine = matchingRrLines[0];
                    if (poLine is null && poLineMap.TryGetValue(rrLine.PurchaseOrderLineId, out var derivedPlFromRr))
                    {
                        poLine = derivedPlFromRr;
                    }
                }
                else if (matchingRrLines.Count > 1)
                {
                    return Result<SupplierInvoiceModel>.Fail(
                        Error.Validation(
                            "Lpi.Payables.AmbiguousRrLine",
                            $"Multiple Receiving Report lines found for product '{line.ItemDescription}' on RR '{rr.RrNumber}'. Explicit ReceivingReportLineId is required to disambiguate."));
                }
            }

            if (po is not null && poLine is null)
            {
                return Result<SupplierInvoiceModel>.Fail(
                    Error.Validation("Lpi.Payables.InvalidPoLine", $"Invoice line '{line.ItemDescription}' does not match any line on Purchase Order '{po.PoNumber}'."));
            }

            if (rr is not null && rrLine is null)
            {
                return Result<SupplierInvoiceModel>.Fail(
                    Error.Validation("Lpi.Payables.InvalidRrLine", $"Invoice line '{line.ItemDescription}' does not match any line on Receiving Report '{rr.RrNumber}'."));
            }

            if (rrLine is not null && !line.ReceivingReportLineId.HasValue && !seenRrLineIds.Add(rrLine.Id))
            {
                return Result<SupplierInvoiceModel>.Fail(
                    Error.Validation("Lpi.Payables.DuplicateReceivingLine",
                        $"Duplicate receiving report line '{rrLine.Id:D}' in the same supplier invoice request is not allowed."));
            }

            if (rr is null && poLine is not null && !seenPoLineIdsWithoutRr.Add(poLine.Id))
            {
                return Result<SupplierInvoiceModel>.Fail(
                    Error.Validation("Lpi.Payables.DuplicatePurchaseOrderLine",
                        $"Duplicate purchase order line '{poLine.Id:D}' in the same supplier invoice request is not allowed."));
            }

            if (poLine is not null && rrLine is not null && rrLine.PurchaseOrderLineId != poLine.Id)
            {
                return Result<SupplierInvoiceModel>.Fail(
                    Error.Validation("Lpi.Payables.LinePoMismatch", $"Invoice line '{line.ItemDescription}' Receiving Report line does not match the linked Purchase Order line."));
            }

            long poOrdQty = poLine?.OrderedQtyX1000 ?? line.InvoicedQtyX1000;
            long poUnitCost = poLine?.UnitCostCents ?? line.InvoicedUnitCostCents;

            long priorInvoicedX1000 = 0;
            if (rrLine is not null)
            {
                await using SqliteCommand priorBilledCmd = connection.CreateCommand();
                priorBilledCmd.Transaction = transaction;
                priorBilledCmd.CommandText = """
                    SELECT COALESCE(SUM(sil.invoiced_qty_x1000), 0)
                    FROM supplier_invoice_lines sil
                    JOIN supplier_invoices si ON si.id = sil.supplier_invoice_id
                    WHERE sil.receiving_report_line_id = $rrLineId
                      AND si.status != 'Cancelled';
                    """;
                priorBilledCmd.Parameters.AddWithValue("$rrLineId", rrLine.Id.ToString("D"));
                object? sumObj = await priorBilledCmd.ExecuteScalarAsync(cancellationToken);
                priorInvoicedX1000 = sumObj is long sumVal ? sumVal : Convert.ToInt64(sumObj ?? 0);
            }

            long rrAccQty = rrLine?.AcceptedQtyX1000 ?? line.InvoicedQtyX1000;
            long remainingUnbilledAcceptedX1000 = rrLine is not null ? Math.Max(0, rrLine.AcceptedQtyX1000 - priorInvoicedX1000) : 0;

            long qtyVar = rrLine is not null ? (line.InvoicedQtyX1000 - remainingUnbilledAcceptedX1000) : 0;
            long costVar = poLine is not null ? (line.InvoicedUnitCostCents - poUnitCost) : 0;

            string lineStatus;
            if (po is null || rr is null)
            {
                lineStatus = SupplierInvoiceMatchStatuses.PendingDocuments;
                varianceMessages.Add($"{line.ItemDescription}: Missing supporting PO or Receiving Report; invoice requires supporting documents before matching.");
            }
            else
            {
                bool lineOverbilled = qtyVar > 0;
                bool linePriceMismatch = costVar != 0;

                if (lineOverbilled && linePriceMismatch)
                {
                    lineStatus = SupplierInvoiceMatchStatuses.QtyAndPriceVariance;
                    hasQtyVariance = true;
                    hasPriceVariance = true;
                    varianceMessages.Add($"{line.ItemDescription}: Invoiced qty exceeds remaining unbilled accepted qty ({line.InvoicedQtyX1000 / 1000m:N2} > {remainingUnbilledAcceptedX1000 / 1000m:N2}) and unit cost mismatch ({line.InvoicedUnitCostCents / 100m:N2} vs PO {poUnitCost / 100m:N2})");
                }
                else if (lineOverbilled)
                {
                    lineStatus = SupplierInvoiceMatchStatuses.Overbilled;
                    hasQtyVariance = true;
                    varianceMessages.Add($"{line.ItemDescription}: Overbilling detected: Invoiced qty ({line.InvoicedQtyX1000 / 1000m:N2}) exceeds remaining unbilled accepted qty ({remainingUnbilledAcceptedX1000 / 1000m:N2}). Total Accepted: {rrAccQty / 1000m:N2}, Previously Invoiced: {priorInvoicedX1000 / 1000m:N2}");
                }
                else if (linePriceMismatch)
                {
                    lineStatus = SupplierInvoiceMatchStatuses.PriceVariance;
                    hasPriceVariance = true;
                    varianceMessages.Add($"{line.ItemDescription}: Price variance: Invoiced unit cost {line.InvoicedUnitCostCents / 100m:N2} vs PO {poUnitCost / 100m:N2}");
                }
                else
                {
                    lineStatus = SupplierInvoiceMatchStatuses.Matched;
                }
            }

            long grossLine;
            long lineTotal;
            try
            {
                checked
                {
                    grossLine = (line.InvoicedQtyX1000 * line.InvoicedUnitCostCents) / 1000L;
                    lineTotal = grossLine + line.TaxCents;
                    subtotalCents += grossLine;
                    totalTaxCents += line.TaxCents;
                }
            }
            catch (OverflowException)
            {
                return Result<SupplierInvoiceModel>.Fail(
                    Error.Validation("Lpi.Payables.AmountOverflow", "Supplier invoice line amount exceeds maximum supported value."));
            }

            preparedLines.Add((
                line,
                poLine?.Id ?? line.PurchaseOrderLineId,
                rrLine?.Id ?? line.ReceivingReportLineId,
                poOrdQty,
                rrAccQty,
                poUnitCost,
                lineTotal,
                qtyVar,
                costVar,
                lineStatus));
        }

        long totalInvoiceCents;
        try
        {
            checked
            {
                totalInvoiceCents = subtotalCents - request.DiscountCents + totalTaxCents - request.WithholdingTaxCents;
            }
        }
        catch (OverflowException)
        {
            return Result<SupplierInvoiceModel>.Fail(
                Error.Validation("Lpi.Payables.AmountOverflow", "Supplier invoice total exceeds maximum supported value."));
        }

        if (totalInvoiceCents <= 0)
            return Result<SupplierInvoiceModel>.Fail(Error.Validation(
                "Lpi.Payables.ZeroValueInvoice", "Zero-value invoices cannot be posted or recorded as payable liabilities."));

        string overallMatchStatus;
        string initialStatus;
        bool hasVariance;

        if (po is null || rr is null)
        {
            overallMatchStatus = SupplierInvoiceMatchStatuses.PendingDocuments;
            initialStatus = "PendingVerification";
            hasVariance = true;
        }
        else if (hasQtyVariance && hasPriceVariance)
        {
            overallMatchStatus = SupplierInvoiceMatchStatuses.QtyAndPriceVariance;
            initialStatus = "VarianceFlagged";
            hasVariance = true;
        }
        else if (hasQtyVariance)
        {
            overallMatchStatus = SupplierInvoiceMatchStatuses.QtyVariance;
            initialStatus = "VarianceFlagged";
            hasVariance = true;
        }
        else if (hasPriceVariance)
        {
            overallMatchStatus = SupplierInvoiceMatchStatuses.PriceVariance;
            initialStatus = "VarianceFlagged";
            hasVariance = true;
        }
        else
        {
            overallMatchStatus = SupplierInvoiceMatchStatuses.Matched;
            initialStatus = "Matched";
            hasVariance = false;
        }

        string? varianceNotes = varianceMessages.Count > 0 ? string.Join(" | ", varianceMessages) : null;
        string apVoucherNo = await AllocateSequenceInTransactionAsync(connection, transaction, "APV", cancellationToken);
        Guid invId = Guid.NewGuid();
        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateOnly dueDate = request.DueDate ?? request.InvoiceDate.AddDays(Math.Max(0, supplier.PaymentTermsDays));

        await using (SqliteCommand cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = """
                INSERT INTO supplier_invoices (
                    id, ap_voucher_number, supplier_id, normalized_supplier_invoice_no, supplier_invoice_no,
                    purchase_order_id, receiving_report_id, invoice_date, due_date,
                    status, match_status, subtotal_cents, discount_cents, tax_cents, withholding_tax_cents,
                    total_invoice_cents, credit_memo_applied_cents, paid_amount_cents, remaining_balance_cents,
                    variance_notes, recorded_by_user_id, created_at, updated_at
                )
                VALUES (
                    $id, $apvNo, $supId, $normNo, $invNo,
                    $poId, $rrId, $invDate, $dueDate,
                    $status, $matchStatus, $sub, $disc, $tax, $ewt,
                    $total, 0, 0, $total,
                    $varNotes, $recBy, $now, $now
                );
                """;
            cmd.Parameters.AddWithValue("$id", invId.ToString("D"));
            cmd.Parameters.AddWithValue("$apvNo", apVoucherNo);
            cmd.Parameters.AddWithValue("$supId", request.SupplierId.ToString("D"));
            cmd.Parameters.AddWithValue("$normNo", normalizedInvNo);
            cmd.Parameters.AddWithValue("$invNo", request.SupplierInvoiceNo.Trim());
            cmd.Parameters.AddWithValue("$poId", request.PurchaseOrderId?.ToString("D") ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$rrId", request.ReceivingReportId?.ToString("D") ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$invDate", request.InvoiceDate.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("$dueDate", dueDate.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("$status", initialStatus);
            cmd.Parameters.AddWithValue("$matchStatus", overallMatchStatus);
            cmd.Parameters.AddWithValue("$sub", subtotalCents);
            cmd.Parameters.AddWithValue("$disc", request.DiscountCents);
            cmd.Parameters.AddWithValue("$tax", totalTaxCents);
            cmd.Parameters.AddWithValue("$ewt", request.WithholdingTaxCents);
            cmd.Parameters.AddWithValue("$total", totalInvoiceCents);
            cmd.Parameters.AddWithValue("$varNotes", (object?)varianceNotes ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$recBy", request.RecordedByUserId.ToString("D"));
            cmd.Parameters.AddWithValue("$now", now.ToString("O"));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var pLine in preparedLines)
        {
            await using SqliteCommand lineCmd = connection.CreateCommand();
            lineCmd.Transaction = transaction;
            lineCmd.CommandText = """
                INSERT INTO supplier_invoice_lines (
                    id, supplier_invoice_id, purchase_order_line_id, receiving_report_line_id, product_id,
                    item_description, po_ordered_qty_x1000, rr_accepted_qty_x1000, invoiced_qty_x1000,
                    po_unit_cost_cents, invoiced_unit_cost_cents, tax_cents, line_total_cents,
                    qty_variance_x1000, unit_cost_variance_cents, line_match_status
                )
                VALUES (
                    $id, $invId, $poLineId, $rrLineId, $prodId,
                    $desc, $poQty, $rrQty, $invQty,
                    $poCost, $invCost, $tax, $lineTotal,
                    $qtyVar, $costVar, $matchStatus
                );
                """;
            lineCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
            lineCmd.Parameters.AddWithValue("$invId", invId.ToString("D"));
            lineCmd.Parameters.AddWithValue("$poLineId", pLine.ResolvedPoLineId?.ToString("D") ?? (object)DBNull.Value);
            lineCmd.Parameters.AddWithValue("$rrLineId", pLine.ResolvedRrLineId?.ToString("D") ?? (object)DBNull.Value);
            lineCmd.Parameters.AddWithValue("$prodId", pLine.Input.ProductId?.ToString("D") ?? (object)DBNull.Value);
            lineCmd.Parameters.AddWithValue("$desc", pLine.Input.ItemDescription.Trim());
            lineCmd.Parameters.AddWithValue("$poQty", pLine.PoOrdQtyX1000);
            lineCmd.Parameters.AddWithValue("$rrQty", pLine.RrAccQtyX1000);
            lineCmd.Parameters.AddWithValue("$invQty", pLine.Input.InvoicedQtyX1000);
            lineCmd.Parameters.AddWithValue("$poCost", pLine.PoUnitCostCents);
            lineCmd.Parameters.AddWithValue("$invCost", pLine.Input.InvoicedUnitCostCents);
            lineCmd.Parameters.AddWithValue("$tax", pLine.Input.TaxCents);
            lineCmd.Parameters.AddWithValue("$lineTotal", pLine.LineTotalCents);
            lineCmd.Parameters.AddWithValue("$qtyVar", pLine.QtyVarX1000);
            lineCmd.Parameters.AddWithValue("$costVar", pLine.CostVarCents);
            lineCmd.Parameters.AddWithValue("$matchStatus", pLine.LineMatchStatus);
            await lineCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        if (!hasVariance && request.PostToApIfMatched)
        {
            await PostSupplierInvoiceInternalAsync(
                connection,
                transaction,
                invId,
                request.SupplierId,
                apVoucherNo,
                request.SupplierInvoiceNo.Trim(),
                totalInvoiceCents,
                request.RecordedByUserId,
                now,
                cancellationToken);
        }
        else
        {
            await WriteAuditAsync(
                connection,
                transaction,
                request.RecordedByUserId,
                hasVariance ? "AP_INVOICE_VARIANCE_FLAGGED" : "AP_INVOICE_RECORDED",
                "SupplierInvoice",
                invId.ToString("D"),
                new { apVoucherNo, request.SupplierInvoiceNo, overallMatchStatus, totalInvoiceCents, varianceNotes },
                null,
                varianceNotes ?? $"Recorded supplier invoice {apVoucherNo}.",
                now,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        DateOnly today = DateOnly.FromDateTime(now.UtcDateTime);
        SupplierInvoiceModel? created = await LoadSupplierInvoiceAsync(connection, null, invId, today, cancellationToken);
        return Result<SupplierInvoiceModel>.Success(created!);
    }

    public async Task<Result<SupplierInvoiceModel>> ApproveInvoiceVarianceAsync(
        ApproveInvoiceVarianceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.SupplierInvoiceId == Guid.Empty || request.ApproverUserId == Guid.Empty || string.IsNullOrWhiteSpace(request.ApprovalReason))
        {
            return Result<SupplierInvoiceModel>.Fail(
                Error.Validation("Lpi.Payables.ApprovalReasonRequired", "Invoice ID, approver user, and variance approval reason are required."));
        }

        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        SqlitePragma.Apply(connection);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateOnly today = DateOnly.FromDateTime(now.UtcDateTime);
        SupplierInvoiceModel? existing = await LoadSupplierInvoiceAsync(connection, transaction, request.SupplierInvoiceId, today, cancellationToken);
        if (existing is null)
        {
            return Result<SupplierInvoiceModel>.Fail(Error.NotFound("Lpi.Payables.InvoiceNotFound", "Supplier invoice was not found."));
        }
        if (existing.Status != "VarianceFlagged")
        {
            return Result<SupplierInvoiceModel>.Fail(
                Error.Conflict("Lpi.Payables.NotVarianceFlagged", $"Supplier invoice '{existing.ApVoucherNumber}' is in '{existing.Status}' status and does not require variance approval."));
        }

        await using (SqliteCommand upd = connection.CreateCommand())
        {
            upd.Transaction = transaction;
            upd.CommandText = """
                UPDATE supplier_invoices
                SET status = 'VarianceApproved',
                    match_status = 'VarianceApproved',
                    variance_approved_by_user_id = $approver,
                    variance_approval_reason = $reason,
                    updated_at = $now
                WHERE id = $id;
                """;
            upd.Parameters.AddWithValue("$approver", request.ApproverUserId.ToString("D"));
            upd.Parameters.AddWithValue("$reason", request.ApprovalReason.Trim());
            upd.Parameters.AddWithValue("$now", now.ToString("O"));
            upd.Parameters.AddWithValue("$id", existing.Id.ToString("D"));
            await upd.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteAuditAsync(
            connection,
            transaction,
            request.ApproverUserId,
            "AP_INVOICE_VARIANCE_APPROVED",
            "SupplierInvoice",
            existing.Id.ToString("D"),
            new { existing.ApVoucherNumber, existing.MatchStatus, approvalReason = request.ApprovalReason.Trim() },
            new { existing.Status },
            request.ApprovalReason.Trim(),
            now,
            cancellationToken);

        if (request.PostToApImmediately)
        {
            await PostSupplierInvoiceInternalAsync(
                connection,
                transaction,
                existing.Id,
                existing.SupplierId,
                existing.ApVoucherNumber,
                existing.SupplierInvoiceNo,
                existing.TotalInvoiceCents,
                request.ApproverUserId,
                now,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        SupplierInvoiceModel? updated = await LoadSupplierInvoiceAsync(connection, null, existing.Id, today, cancellationToken);
        return Result<SupplierInvoiceModel>.Success(updated!);
    }

    public async Task<Result<SupplierInvoiceModel>> PostSupplierInvoiceToApAsync(
        Guid supplierInvoiceId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        SqlitePragma.Apply(connection);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateOnly today = DateOnly.FromDateTime(now.UtcDateTime);
        SupplierInvoiceModel? existing = await LoadSupplierInvoiceAsync(connection, transaction, supplierInvoiceId, today, cancellationToken);
        if (existing is null)
        {
            return Result<SupplierInvoiceModel>.Fail(Error.NotFound("Lpi.Payables.InvoiceNotFound", "Supplier invoice was not found."));
        }
        if (existing.Status is "PostedOpen" or "PartiallyPaid" or "Paid")
        {
            return Result<SupplierInvoiceModel>.Success(existing);
        }

        if (existing.Status is not ("Matched" or "VarianceApproved"))
        {
            return Result<SupplierInvoiceModel>.Fail(
                Error.Conflict(
                    "Lpi.Payables.UnverifiedPostingForbidden",
                    $"Cannot post invoice '{existing.SupplierInvoiceNo}' in '{existing.Status}' status to Accounts Payable. Only Matched or VarianceApproved invoices with verified purchasing and receiving documentation may post."));
        }

        if (!existing.PurchaseOrderId.HasValue || !existing.ReceivingReportId.HasValue)
        {
            return Result<SupplierInvoiceModel>.Fail(
                Error.Validation(
                    "Lpi.Payables.MissingSourceDocuments",
                    $"Cannot post invoice '{existing.SupplierInvoiceNo}' without verified Purchase Order and Receiving Report references."));
        }

        await PostSupplierInvoiceInternalAsync(
            connection,
            transaction,
            existing.Id,
            existing.SupplierId,
            existing.ApVoucherNumber,
            existing.SupplierInvoiceNo,
            existing.TotalInvoiceCents,
            actorUserId,
            now,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        SupplierInvoiceModel? posted = await LoadSupplierInvoiceAsync(connection, null, existing.Id, today, cancellationToken);
        return Result<SupplierInvoiceModel>.Success(posted!);
    }

    // Philippines store-local business calendar: Asia/Manila, fixed UTC+08:00 (no DST).
    // Independent of the host machine's OS local timezone.
    private static DateOnly SupplierPaymentBusinessDate(DateTimeOffset instant) =>
        DateOnly.FromDateTime(instant.ToOffset(TimeSpan.FromHours(8)).DateTime);

    public async Task<Result<SupplierPaymentModel>> RecordSupplierPaymentAsync(
        RecordSupplierPaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.SupplierInvoiceId == Guid.Empty || request.RecordedByUserId == Guid.Empty)
        {
            return Result<SupplierPaymentModel>.Fail(
                Error.Validation("Lpi.Payment.MissingFields", "Supplier invoice ID and recording user ID are required."));
        }
        if (request.AmountCents <= 0)
        {
            return Result<SupplierPaymentModel>.Fail(
                Error.Validation("Lpi.Payment.InvalidAmount", "Payment amount must be greater than zero."));
        }
        if (string.IsNullOrWhiteSpace(request.ReferenceNo))
        {
            return Result<SupplierPaymentModel>.Fail(
                Error.Validation("Lpi.Payment.ReferenceRequired", "Payment reference number (bank ref / check no / voucher ref) is required."));
        }

        string method = Clean(request.PaymentMethod) ?? "BankTransfer";
        string source = Clean(request.DisbursementSource) ?? "HeadOfficeBank";

        // Policy safeguard: never allow POS Cashier Drawer as disbursement source
        if (source.Contains("CashierDrawer", StringComparison.OrdinalIgnoreCase) ||
            source.Contains("ShiftDrawer", StringComparison.OrdinalIgnoreCase))
        {
            return Result<SupplierPaymentModel>.Fail(
                Error.Validation("Lpi.Payment.ForbiddenDrawerSource", "Supplier AP disbursements cannot be drawn directly from the POS cashier shift drawer."));
        }

        string payloadHash = ComputePaymentPayloadHash(
            request.SupplierInvoiceId,
            request.AmountCents,
            method,
            source,
            request.ReferenceNo,
            request.CheckNo,
            request.BankName);

        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        SqlitePragma.Apply(connection);

        string? clientReqId = Clean(request.ClientRequestId);
        if (!string.IsNullOrWhiteSpace(clientReqId))
        {
            await using SqliteCommand checkCmd = connection.CreateCommand();
            checkCmd.CommandText = "SELECT id, request_payload_hash FROM supplier_payments WHERE client_request_id = $reqId LIMIT 1;";
            checkCmd.Parameters.AddWithValue("$reqId", clientReqId);
            await using SqliteDataReader checkReader = await checkCmd.ExecuteReaderAsync(cancellationToken);
            if (await checkReader.ReadAsync(cancellationToken))
            {
                Guid existingPayId = Guid.Parse(checkReader.GetString(0));
                string? existingHash = checkReader.IsDBNull(1) ? null : checkReader.GetString(1);
                checkReader.Close();

                if (existingHash is not null && !string.Equals(existingHash, payloadHash, StringComparison.Ordinal))
                {
                    return Result<SupplierPaymentModel>.Fail(
                        Error.Conflict(
                            "Lpi.Payables.IdempotencyConflict",
                            $"A supplier payment with request ID '{clientReqId}' was already submitted with different transaction parameters."));
                }

                SupplierPaymentModel? replay = await LoadSupplierPaymentByIdAsync(connection, null, existingPayId, cancellationToken);
                if (replay is not null) return Result<SupplierPaymentModel>.Success(replay);
            }
        }

        // Recording is intentionally non-financial. Settlement must be a separate, explicit
        // confirmation action so an optional service argument cannot bypass the operator safeguard.
        if (request.ApprovedByUserId.HasValue)
        {
            return Result<SupplierPaymentModel>.Fail(
                Error.Validation("Lpi.Payment.SeparateSettlementRequired", "Record the payment as pending, then confirm actual settlement through the settlement action."));
        }

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateOnly today = SupplierPaymentBusinessDate(now);
        SupplierInvoiceModel? invoice = await LoadSupplierInvoiceAsync(connection, transaction, request.SupplierInvoiceId, today, cancellationToken);
        if (invoice is null)
        {
            return Result<SupplierPaymentModel>.Fail(Error.NotFound("Lpi.Payment.InvoiceNotFound", "Supplier invoice was not found."));
        }
        if (invoice.Status is not ("PostedOpen" or "PartiallyPaid"))
        {
            return Result<SupplierPaymentModel>.Fail(
                Error.Conflict("Lpi.Payment.InvoiceNotPayable", $"Supplier invoice '{invoice.ApVoucherNumber}' is in '{invoice.Status}' status and cannot be paid."));
        }
        if (request.AmountCents > invoice.RemainingBalanceCents)
        {
            return Result<SupplierPaymentModel>.Fail(
                Error.Conflict("Lpi.Payment.OverpaymentBlocked", $"Payment amount ({request.AmountCents / 100m:N2}) exceeds remaining payable balance ({invoice.RemainingBalanceCents / 100m:N2})."));
        }

        string payNumber = await AllocateSequenceInTransactionAsync(connection, transaction, "PAY", cancellationToken);
        Guid payId = Guid.NewGuid();
        DateOnly payDate = request.PaymentDate ?? today;

        await using (SqliteCommand ins = connection.CreateCommand())
        {
            ins.Transaction = transaction;
            ins.CommandText = """
                INSERT INTO supplier_payments (
                    id, payment_number, supplier_invoice_id, supplier_id, payment_date,
                    amount_cents, payment_method, disbursement_source, reference_no,
                    check_no, bank_name, remarks, status, recorded_by_user_id, approved_by_user_id,
                    approved_at, client_request_id, request_payload_hash, created_at
                )
                VALUES (
                    $id, $payNo, $invId, $supId, $payDate,
                    $amt, $method, $source, $refNo,
                    $checkNo, $bank, $remarks, $status, $recBy, $appBy,
                    $appAt, $clientReqId, $hash, $now
                );
                """;
            ins.Parameters.AddWithValue("$id", payId.ToString("D"));
            ins.Parameters.AddWithValue("$payNo", payNumber);
            ins.Parameters.AddWithValue("$invId", invoice.Id.ToString("D"));
            ins.Parameters.AddWithValue("$supId", invoice.SupplierId.ToString("D"));
            ins.Parameters.AddWithValue("$payDate", payDate.ToString("yyyy-MM-dd"));
            ins.Parameters.AddWithValue("$amt", request.AmountCents);
            ins.Parameters.AddWithValue("$method", method);
            ins.Parameters.AddWithValue("$source", source);
            ins.Parameters.AddWithValue("$refNo", request.ReferenceNo.Trim());
            ins.Parameters.AddWithValue("$checkNo", (object?)Clean(request.CheckNo) ?? DBNull.Value);
            ins.Parameters.AddWithValue("$bank", (object?)Clean(request.BankName) ?? DBNull.Value);
            ins.Parameters.AddWithValue("$remarks", (object?)Clean(request.Remarks) ?? DBNull.Value);
            ins.Parameters.AddWithValue("$status", "PendingApproval");
            ins.Parameters.AddWithValue("$recBy", request.RecordedByUserId.ToString("D"));
            ins.Parameters.AddWithValue("$appBy", DBNull.Value);
            ins.Parameters.AddWithValue("$appAt", DBNull.Value);
            ins.Parameters.AddWithValue("$clientReqId", (object?)clientReqId ?? DBNull.Value);
            ins.Parameters.AddWithValue("$hash", payloadHash);
            ins.Parameters.AddWithValue("$now", now.ToString("O"));
            await ins.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteAuditAsync(
                connection,
                transaction,
                request.RecordedByUserId,
                "AP_PAYMENT_RECORDED_PENDING_APPROVAL",
                "SupplierPayment",
                payId.ToString("D"),
                new
                {
                    payNumber,
                    invoice.ApVoucherNumber,
                    invoice.SupplierInvoiceNo,
                    request.AmountCents,
                    invoice.RemainingBalanceCents,
                    method,
                    source,
                    request.ReferenceNo
                },
                null,
                $"Recorded supplier payment {payNumber} for {invoice.ApVoucherNumber} (Pending Settlement — Not Yet Disbursed).",
                now,
                cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        SupplierPaymentModel? created = await LoadSupplierPaymentByIdAsync(connection, null, payId, cancellationToken);
        return Result<SupplierPaymentModel>.Success(created!);
    }

    public async Task<Result<SupplierPaymentModel>> ApproveSupplierPaymentAsync(
        ApproveSupplierPaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.PaymentId == Guid.Empty || request.ApproverUserId == Guid.Empty)
        {
            return Result<SupplierPaymentModel>.Fail(
                Error.Validation("Lpi.Payment.ApproverRequired", "Payment ID and approver user are required."));
        }
        if (!string.Equals(request.SettlementConfirmation?.Trim(), "YES", StringComparison.Ordinal))
        {
            return Result<SupplierPaymentModel>.Fail(
                Error.Validation("Lpi.Payment.SettlementConfirmationRequired", "Type the exact uppercase text YES to confirm that money was actually paid or transferred."));
        }

        if (string.IsNullOrWhiteSpace(request.ActualDisbursementDate))
        {
            return Result<SupplierPaymentModel>.Fail(
                Error.Validation("Lpi.Payment.ActualDisbursementDateRequired", "Explicitly enter the date funds actually moved; a payment request date is not proof of transfer."));
        }
        if (!DateOnly.TryParseExact(request.ActualDisbursementDate, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None,
                out DateOnly actualDisbursementDate) ||
            !string.Equals(request.ActualDisbursementDate,
                actualDisbursementDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                StringComparison.Ordinal))
        {
            return Result<SupplierPaymentModel>.Fail(
                Error.Validation("Lpi.Payment.ActualDisbursementDateInvalid", "Actual funds-transfer date must be an exact, valid yyyy-MM-dd date."));
        }
        DateOnly businessToday = SupplierPaymentBusinessDate(_timeProvider.GetUtcNow());
        if (actualDisbursementDate > businessToday)
        {
            return Result<SupplierPaymentModel>.Fail(
                Error.Validation("Lpi.Payment.ActualDisbursementDateFuture", "Actual funds-transfer date cannot be later than today's Philippine business date."));
        }

        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        SqlitePragma.Apply(connection);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        SupplierPaymentModel? payment = await LoadSupplierPaymentByIdAsync(connection, transaction, request.PaymentId, cancellationToken);
        if (payment is null)
        {
            return Result<SupplierPaymentModel>.Fail(Error.NotFound("Lpi.Payment.NotFound", "Supplier payment was not found."));
        }
        if (payment.Status != "PendingApproval")
        {
            return Result<SupplierPaymentModel>.Fail(
                Error.Conflict("Lpi.Payment.AlreadyFinalized", $"Payment '{payment.PaymentNumber}' is already in status '{payment.Status}'."));
        }

        if (payment.AmountCents <= 0 || string.IsNullOrWhiteSpace(payment.ReferenceNo))
        {
            return Result<SupplierPaymentModel>.Fail(
                Error.Validation("Lpi.Payment.InvalidPendingPayment", "Pending payment amount and reference must be valid before settlement."));
        }
        if (payment.DisbursementSource.Contains("Drawer", StringComparison.OrdinalIgnoreCase) ||
            payment.DisbursementSource.Contains("Cashier", StringComparison.OrdinalIgnoreCase))
        {
            return Result<SupplierPaymentModel>.Fail(
                Error.Validation("Lpi.Payment.ForbiddenDrawerSource", "Supplier AP settlement cannot use the POS cashier shift drawer."));
        }

        DateOnly today = SupplierPaymentBusinessDate(now);
        if (actualDisbursementDate > today)
        {
            return Result<SupplierPaymentModel>.Fail(
                Error.Validation("Lpi.Payment.ActualDisbursementDateFuture", "Actual funds-transfer date cannot be later than today's Philippine business date."));
        }
        SupplierInvoiceModel? invoice = await LoadSupplierInvoiceAsync(connection, transaction, payment.SupplierInvoiceId, today, cancellationToken);
        if (invoice is null)
        {
            return Result<SupplierPaymentModel>.Fail(Error.NotFound("Lpi.Payment.InvoiceNotFound", "Linked supplier invoice was not found."));
        }
        // Both supplier identities are persisted facts read within this settlement transaction.
        // Never settle a legacy/corrupted payment against a different supplier's invoice.
        if (payment.SupplierId != invoice.SupplierId)
        {
            return Result<SupplierPaymentModel>.Fail(
                Error.Conflict("Lpi.Payment.SupplierInvoiceMismatch",
                    $"Payment '{payment.PaymentNumber}' does not match the supplier on linked invoice '{invoice.ApVoucherNumber}'. Settlement was not posted."));
        }
        if (invoice.Status is not ("PostedOpen" or "PartiallyPaid"))
        {
            return Result<SupplierPaymentModel>.Fail(
                Error.Conflict("Lpi.Payment.InvoiceNotPayable", $"Supplier invoice '{invoice.ApVoucherNumber}' is in '{invoice.Status}' status and cannot be settled."));
        }
        if (payment.AmountCents > invoice.RemainingBalanceCents)
        {
            return Result<SupplierPaymentModel>.Fail(
                Error.Conflict("Lpi.Payment.OverpaymentBlocked", $"Payment amount ({payment.AmountCents / 100m:N2}) exceeds remaining payable balance ({invoice.RemainingBalanceCents / 100m:N2})."));
        }

        await using (SqliteCommand updPay = connection.CreateCommand())
        {
            updPay.Transaction = transaction;
            updPay.CommandText = """
                UPDATE supplier_payments
                SET status = 'Settled',
                    approved_by_user_id = $appBy,
                    approved_at = $now,
                    actual_disbursement_date = $actualDate,
                    remarks = CASE WHEN $remarks IS NULL OR $remarks = '' THEN remarks ELSE COALESCE(remarks, '') || ' | ' || $remarks END
                WHERE id = $id AND status = 'PendingApproval' AND actual_disbursement_date IS NULL;
                """;
            updPay.Parameters.AddWithValue("$actualDate", actualDisbursementDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            updPay.Parameters.AddWithValue("$appBy", request.ApproverUserId.ToString("D"));
            updPay.Parameters.AddWithValue("$now", now.ToString("O"));
            updPay.Parameters.AddWithValue("$remarks", (object?)Clean(request.Remarks) ?? DBNull.Value);
            updPay.Parameters.AddWithValue("$id", payment.Id.ToString("D"));
            int changed = await updPay.ExecuteNonQueryAsync(cancellationToken);
            if (changed != 1)
            {
                return Result<SupplierPaymentModel>.Fail(
                    Error.Conflict("Lpi.Payment.AlreadyFinalized", $"Payment '{payment.PaymentNumber}' was already finalized by another request."));
            }
        }

        long newPaidCents = checked(invoice.PaidAmountCents + payment.AmountCents);
        long newRemainingCents = invoice.RemainingBalanceCents - payment.AmountCents;
        string nextInvStatus = newRemainingCents == 0 ? "Paid" : "PartiallyPaid";

        await using (SqliteCommand updInv = connection.CreateCommand())
        {
            updInv.Transaction = transaction;
            updInv.CommandText = """
                UPDATE supplier_invoices
                SET paid_amount_cents = $paid,
                    remaining_balance_cents = $rem,
                    status = $status,
                    updated_at = $now
                    WHERE id = $id
                      AND remaining_balance_cents = $previousRemaining
                      AND status IN ('PostedOpen', 'PartiallyPaid');
                """;
            updInv.Parameters.AddWithValue("$paid", newPaidCents);
            updInv.Parameters.AddWithValue("$rem", newRemainingCents);
            updInv.Parameters.AddWithValue("$status", nextInvStatus);
            updInv.Parameters.AddWithValue("$now", now.ToString("O"));
            updInv.Parameters.AddWithValue("$id", invoice.Id.ToString("D"));
            updInv.Parameters.AddWithValue("$previousRemaining", invoice.RemainingBalanceCents);
            int invoiceChanged = await updInv.ExecuteNonQueryAsync(cancellationToken);
            if (invoiceChanged != 1)
            {
                return Result<SupplierPaymentModel>.Fail(
                    Error.Conflict("Lpi.Payment.InvoiceBalanceChanged", "The payable balance changed before settlement. Refresh and review the pending payment."));
            }
        }

        await AppendPayableLedgerEntryAsync(
            connection,
            transaction,
            invoice.SupplierId,
            invoice.Id,
            "PaymentApplied",
            payment.PaymentNumber,
            debitCents: payment.AmountCents,
            creditCents: 0,
            note: $"Operator-affirmed transfer date {actualDisbursementDate:yyyy-MM-dd}; settlement posted at {now:O}; payment {payment.PaymentNumber} ({payment.PaymentMethod} / {payment.DisbursementSource}, Ref: {payment.ReferenceNo})",
            actorUserId: request.ApproverUserId,
            now: now,
            cancellationToken: cancellationToken);

        await WriteAuditAsync(
            connection,
            transaction,
            request.ApproverUserId,
            "AP_PAYMENT_CONFIRMED_AND_SETTLED",
            "SupplierPayment",
            payment.Id.ToString("D"),
            new
            {
                PaymentId = payment.Id,
                payment.PaymentNumber,
                payment.SupplierId,
                payment.SupplierInvoiceId,
                SupplierInvoiceReference = invoice.SupplierInvoiceNo,
                payment.AmountCents,
                payment.PaymentMethod,
                payment.DisbursementSource,
                PaymentTransactionReference = payment.ReferenceNo,
                payment.RecordedByUserId,
                SettlementActorUserId = request.ApproverUserId,
                SameUserRecordedAndSettled = payment.RecordedByUserId == request.ApproverUserId,
                SettlementTimestamp = now,
                RequestedPaymentDate = payment.PaymentDate,
                ActualDisbursementDate = actualDisbursementDate,
                OperatorConfirmedActualPayment = true,
                PreviousInvoiceStatus = invoice.Status,
                NewInvoiceStatus = nextInvStatus,
                PreviousPaidAmountCents = invoice.PaidAmountCents,
                NewPaidAmountCents = newPaidCents,
                PreviousOutstandingBalanceCents = invoice.RemainingBalanceCents,
                NewOutstandingBalanceCents = newRemainingCents,
                AppliedApLedgerAmountCents = payment.AmountCents,
                ApLedgerReference = payment.PaymentNumber
            },
            new
            {
                PaymentStatus = payment.Status,
                RequestedPaymentDate = payment.PaymentDate,
                ActualDisbursementDate = payment.ActualDisbursementDate,
                InvoiceStatus = invoice.Status,
                PaidAmountCents = invoice.PaidAmountCents,
                OutstandingBalanceCents = invoice.RemainingBalanceCents
            },
            $"Confirmed actual disbursement and settled supplier payment {payment.PaymentNumber} for invoice {invoice.ApVoucherNumber}.",
            now,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        SupplierPaymentModel? settled = await LoadSupplierPaymentByIdAsync(connection, null, payment.Id, cancellationToken);
        return Result<SupplierPaymentModel>.Success(settled!);
    }

    public async Task<Result<IReadOnlyList<SupplierPaymentModel>>> GetSupplierPaymentsAsync(
        Guid? supplierId = null,
        Guid? supplierInvoiceId = null,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT p.id, p.payment_number, p.supplier_invoice_id, i.ap_voucher_number, i.supplier_invoice_no,
                   p.supplier_id, s.name, p.payment_date, p.amount_cents, p.payment_method,
                   p.disbursement_source, p.reference_no, p.check_no, p.bank_name, p.remarks,
                   p.status, p.recorded_by_user_id, p.approved_by_user_id, p.approved_at,
                   p.request_payload_hash, p.created_at, p.actual_disbursement_date
            FROM supplier_payments p
            JOIN supplier_invoices i ON i.id = p.supplier_invoice_id
            JOIN suppliers s ON s.id = p.supplier_id
            WHERE ($supId IS NULL OR p.supplier_id = $supId)
              AND ($invId IS NULL OR p.supplier_invoice_id = $invId)
            ORDER BY p.created_at DESC;
            """;
        cmd.Parameters.AddWithValue("$supId", supplierId?.ToString("D") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$invId", supplierInvoiceId?.ToString("D") ?? (object)DBNull.Value);

        List<SupplierPaymentModel> list = new();
        await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new SupplierPaymentModel(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                Guid.Parse(reader.GetString(2)),
                reader.GetString(3),
                reader.GetString(4),
                Guid.Parse(reader.GetString(5)),
                reader.GetString(6),
                DateOnly.Parse(reader.GetString(7)),
                reader.GetInt64(8),
                reader.GetString(9),
                reader.GetString(10),
                reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.IsDBNull(15) ? "Settled" : reader.GetString(15),
                Guid.Parse(reader.GetString(16)),
                reader.IsDBNull(17) ? null : Guid.Parse(reader.GetString(17)),
                reader.IsDBNull(18) ? null : DateTimeOffset.Parse(reader.GetString(18)),
                reader.IsDBNull(19) ? null : reader.GetString(19),
                DateTimeOffset.Parse(reader.GetString(20)),
                reader.IsDBNull(21) ? null : DateOnly.ParseExact(reader.GetString(21), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
        }

        return Result<IReadOnlyList<SupplierPaymentModel>>.Success(list);
    }

    private static async Task<SupplierPaymentModel?> LoadSupplierPaymentByIdAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            SELECT p.id, p.payment_number, p.supplier_invoice_id, i.ap_voucher_number, i.supplier_invoice_no,
                   p.supplier_id, s.name, p.payment_date, p.amount_cents, p.payment_method,
                   p.disbursement_source, p.reference_no, p.check_no, p.bank_name, p.remarks,
                   p.status, p.recorded_by_user_id, p.approved_by_user_id, p.approved_at,
                   p.request_payload_hash, p.created_at, p.actual_disbursement_date
            FROM supplier_payments p
            JOIN supplier_invoices i ON i.id = p.supplier_invoice_id
            JOIN suppliers s ON s.id = p.supplier_id
            WHERE p.id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", paymentId.ToString("D"));
        await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        return new SupplierPaymentModel(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            Guid.Parse(reader.GetString(2)),
            reader.GetString(3),
            reader.GetString(4),
            Guid.Parse(reader.GetString(5)),
            reader.GetString(6),
            DateOnly.Parse(reader.GetString(7)),
            reader.GetInt64(8),
            reader.GetString(9),
            reader.GetString(10),
            reader.GetString(11),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.IsDBNull(13) ? null : reader.GetString(13),
            reader.IsDBNull(14) ? null : reader.GetString(14),
            reader.IsDBNull(15) ? "Settled" : reader.GetString(15),
            Guid.Parse(reader.GetString(16)),
            reader.IsDBNull(17) ? null : Guid.Parse(reader.GetString(17)),
            reader.IsDBNull(18) ? null : DateTimeOffset.Parse(reader.GetString(18)),
            reader.IsDBNull(19) ? null : reader.GetString(19),
            DateTimeOffset.Parse(reader.GetString(20)),
            reader.IsDBNull(21) ? null : DateOnly.ParseExact(reader.GetString(21), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string ComputePaymentPayloadHash(
        Guid invoiceId,
        long amountCents,
        string? method,
        string? source,
        string? referenceNo,
        string? checkNo,
        string? bankName)
    {
        string raw = $"{invoiceId:D}|{amountCents}|{method?.Trim()}|{source?.Trim()}|{referenceNo?.Trim()}|{checkNo?.Trim()}|{bankName?.Trim()}";
        using var sha = System.Security.Cryptography.SHA256.Create();
        byte[] bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }

    public async Task<Result<IReadOnlyList<SupplierPayableEntryModel>>> GetSupplierPayableLedgerAsync(
        Guid? supplierId = null,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT e.id, e.supplier_id, s.name, e.supplier_invoice_id, i.ap_voucher_number,
                   e.entry_type, e.reference_number, e.debit_cents, e.credit_cents,
                   e.balance_after_cents, e.note, e.actor_user_id, e.created_at
            FROM supplier_payable_entries e
            JOIN suppliers s ON s.id = e.supplier_id
            JOIN supplier_invoices i ON i.id = e.supplier_invoice_id
            WHERE ($supId IS NULL OR e.supplier_id = $supId)
            ORDER BY e.created_at DESC, e.id DESC;
            """;
        cmd.Parameters.AddWithValue("$supId", supplierId?.ToString("D") ?? (object)DBNull.Value);

        List<SupplierPayableEntryModel> list = new();
        await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new SupplierPayableEntryModel(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.GetString(2),
                Guid.Parse(reader.GetString(3)),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetInt64(7),
                reader.GetInt64(8),
                reader.GetInt64(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                Guid.Parse(reader.GetString(11)),
                DateTimeOffset.Parse(reader.GetString(12))));
        }

        return Result<IReadOnlyList<SupplierPayableEntryModel>>.Success(list);
    }

    public async Task<Result<ApAgingSummaryModel>> GetApAgingSummaryAsync(
        DateOnly? asOfDate = null,
        CancellationToken cancellationToken = default)
    {
        DateOnly effectiveAsOf = asOfDate ?? DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        Result<IReadOnlyList<SupplierInvoiceModel>> allInvoicesResult = await GetSupplierInvoicesAsync(null, null, cancellationToken);
        if (allInvoicesResult.IsFailure) return Result<ApAgingSummaryModel>.Fail(allInvoicesResult.ErrorDetail);

        List<SupplierInvoiceModel> openInvoices = new();
        long totalOpen = 0, current = 0, b1To30 = 0, b31To60 = 0, b61To90 = 0, bOver90 = 0;
        int overdueCount = 0;
        int varianceFlaggedCount = allInvoicesResult.Value.Count(x => x.Status == "VarianceFlagged");

        foreach (var inv in allInvoicesResult.Value)
        {
            if (inv.Status is not ("PostedOpen" or "PartiallyPaid") || inv.RemainingBalanceCents <= 0)
            {
                continue;
            }

            int daysPastDue = Math.Max(0, effectiveAsOf.DayNumber - inv.DueDate.DayNumber);
            string bucket = ComputeAgingBucket(daysPastDue);
            SupplierInvoiceModel aged = inv with { DaysPastDue = daysPastDue, AgingBucket = bucket };
            openInvoices.Add(aged);

            totalOpen += aged.RemainingBalanceCents;
            if (daysPastDue <= 0) current += aged.RemainingBalanceCents;
            else if (daysPastDue <= 30) { b1To30 += aged.RemainingBalanceCents; overdueCount++; }
            else if (daysPastDue <= 60) { b31To60 += aged.RemainingBalanceCents; overdueCount++; }
            else if (daysPastDue <= 90) { b61To90 += aged.RemainingBalanceCents; overdueCount++; }
            else { bOver90 += aged.RemainingBalanceCents; overdueCount++; }
        }

        return Result<ApAgingSummaryModel>.Success(new ApAgingSummaryModel(
            totalOpen,
            current,
            b1To30,
            b31To60,
            b61To90,
            bOver90,
            openInvoices.Count,
            overdueCount,
            varianceFlaggedCount,
            openInvoices));
    }

    public async Task<Result<LpiDashboardOverviewModel>> GetDashboardOverviewAsync(
        CancellationToken cancellationToken = default)
    {
        Result<ApAgingSummaryModel> agingResult = await GetApAgingSummaryAsync(null, cancellationToken);
        if (agingResult.IsFailure) return Result<LpiDashboardOverviewModel>.Fail(agingResult.ErrorDetail);

        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        int activeSuppliers = await ScalarIntAsync(connection, "SELECT COUNT(*) FROM suppliers WHERE is_active = 1;", cancellationToken);
        int pendingPrs = await ScalarIntAsync(connection, "SELECT COUNT(*) FROM purchase_requisitions WHERE status IN ('Submitted', 'UnderReview', 'RevisionRequested');", cancellationToken);
        int approvedPrs = await ScalarIntAsync(connection, "SELECT COUNT(*) FROM purchase_requisitions WHERE status IN ('Approved', 'PartiallyConverted');", cancellationToken);
        int openPos = await ScalarIntAsync(connection, "SELECT COUNT(*) FROM purchase_orders WHERE status IN ('Approved', 'Sent', 'Confirmed', 'PartiallyReceived');", cancellationToken);
        int partialPos = await ScalarIntAsync(connection, "SELECT COUNT(*) FROM purchase_orders WHERE status = 'PartiallyReceived';", cancellationToken);
        int postedRrs = await ScalarIntAsync(connection, "SELECT COUNT(*) FROM receiving_reports WHERE status = 'Posted';", cancellationToken);
        int exceptionRrs = await ScalarIntAsync(connection, "SELECT COUNT(*) FROM receiving_reports WHERE status = 'PendingExceptionApproval';", cancellationToken);
        int lowStockProducts = await ScalarIntAsync(connection, "SELECT COUNT(*) FROM products WHERE is_active = 1 AND track_stock = 1 AND stock_on_hand_x1000 <= reorder_level_x1000;", cancellationToken);
        long totalValuationCents = await ScalarLongAsync(connection, "SELECT COALESCE(SUM((MAX(0, stock_on_hand_x1000) * cost_price_cents) / 1000), 0) FROM products WHERE is_active = 1 AND track_stock = 1;", cancellationToken);
        long totalPaymentsCents = await ScalarLongAsync(connection, "SELECT COALESCE(SUM(amount_cents), 0) FROM supplier_payments WHERE status = 'Settled';", cancellationToken);

        int missingCostProductCount = await ScalarIntAsync(connection, """
            SELECT COUNT(*) FROM products
            WHERE is_active = 1
              AND track_stock = 1
              AND stock_on_hand_x1000 > 0
              AND (cost_price_cents IS NULL OR cost_price_cents = 0);
            """, cancellationToken);
        bool costCoverageIncomplete = missingCostProductCount > 0;

        ApAgingSummaryModel aging = agingResult.Value;
        long overdueCents = aging.Days1To30Cents + aging.Days31To60Cents + aging.Days61To90Cents + aging.DaysOver90Cents;

        return Result<LpiDashboardOverviewModel>.Success(new LpiDashboardOverviewModel(
            activeSuppliers,
            pendingPrs,
            approvedPrs,
            openPos,
            partialPos,
            postedRrs,
            exceptionRrs,
            lowStockProducts,
            totalValuationCents,
            aging.TotalOpenPayablesCents,
            overdueCents,
            aging.VarianceFlaggedInvoiceCount,
            totalPaymentsCents,
            aging,
            costCoverageIncomplete,
            missingCostProductCount));
    }

    private async Task PostSupplierInvoiceInternalAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid invoiceId,
        Guid supplierId,
        string apVoucherNo,
        string supplierInvoiceNo,
        long totalInvoiceCents,
        Guid actorUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using (SqliteCommand upd = connection.CreateCommand())
        {
            upd.Transaction = transaction;
            upd.CommandText = """
                UPDATE supplier_invoices
                SET status = 'PostedOpen',
                    posted_by_user_id = $actor,
                    posted_at = $now,
                    updated_at = $now
                WHERE id = $id;
                """;
            upd.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
            upd.Parameters.AddWithValue("$now", now.ToString("O"));
            upd.Parameters.AddWithValue("$id", invoiceId.ToString("D"));
            await upd.ExecuteNonQueryAsync(cancellationToken);
        }

        await AppendPayableLedgerEntryAsync(
            connection,
            transaction,
            supplierId,
            invoiceId,
            "InvoicePosted",
            apVoucherNo,
            debitCents: 0,
            creditCents: totalInvoiceCents, // Credit AP increases liability
            note: $"Posted supplier invoice {supplierInvoiceNo} ({apVoucherNo})",
            actorUserId: actorUserId,
            now: now,
            cancellationToken: cancellationToken);

        await WriteAuditAsync(
            connection,
            transaction,
            actorUserId,
            "AP_INVOICE_POSTED",
            "SupplierInvoice",
            invoiceId.ToString("D"),
            new { apVoucherNo, supplierInvoiceNo, status = "PostedOpen", totalInvoiceCents },
            null,
            $"Posted supplier invoice {apVoucherNo} to Accounts Payable ledger.",
            now,
            cancellationToken);
    }

    private async Task ApplySupplierReturnCreditToInvoiceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid invoiceId,
        string sretNumber,
        long returnCreditCents,
        Guid actorUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        DateOnly today = DateOnly.FromDateTime(now.UtcDateTime);
        SupplierInvoiceModel? inv = await LoadSupplierInvoiceAsync(connection, transaction, invoiceId, today, cancellationToken);
        if (inv is null || inv.RemainingBalanceCents <= 0) return;

        long appliedCredit = Math.Min(inv.RemainingBalanceCents, returnCreditCents);
        long nextRemaining = Math.Max(0, inv.RemainingBalanceCents - appliedCredit);
        string nextStatus = inv.Status is "PostedOpen" or "PartiallyPaid"
            ? (nextRemaining == 0 ? "Paid" : "PartiallyPaid")
            : inv.Status;

        await using (SqliteCommand upd = connection.CreateCommand())
        {
            upd.Transaction = transaction;
            upd.CommandText = """
                UPDATE supplier_invoices
                SET credit_memo_applied_cents = credit_memo_applied_cents + $credit,
                    remaining_balance_cents = $rem,
                    status = $status,
                    updated_at = $now
                WHERE id = $id;
                """;
            upd.Parameters.AddWithValue("$credit", appliedCredit);
            upd.Parameters.AddWithValue("$rem", nextRemaining);
            upd.Parameters.AddWithValue("$status", nextStatus);
            upd.Parameters.AddWithValue("$now", now.ToString("O"));
            upd.Parameters.AddWithValue("$id", inv.Id.ToString("D"));
            await upd.ExecuteNonQueryAsync(cancellationToken);
        }

        if (inv.Status is "PostedOpen" or "PartiallyPaid")
        {
            await AppendPayableLedgerEntryAsync(
                connection,
                transaction,
                inv.SupplierId,
                inv.Id,
                "SupplierReturnCredit",
                sretNumber,
                debitCents: appliedCredit, // Debit AP reduces liability
                creditCents: 0,
                note: $"Supplier Return credit {sretNumber} applied to {inv.ApVoucherNumber}",
                actorUserId: actorUserId,
                now: now,
                cancellationToken: cancellationToken);
        }
    }

    private static async Task AppendPayableLedgerEntryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid supplierId,
        Guid supplierInvoiceId,
        string entryType,
        string referenceNumber,
        long debitCents,
        long creditCents,
        string? note,
        Guid actorUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        long priorBalance = 0;
        await using (SqliteCommand balCmd = connection.CreateCommand())
        {
            balCmd.Transaction = transaction;
            balCmd.CommandText = """
                SELECT COALESCE(SUM(credit_cents - debit_cents), 0)
                FROM supplier_payable_entries
                WHERE supplier_id = $supId;
                """;
            balCmd.Parameters.AddWithValue("$supId", supplierId.ToString("D"));
            priorBalance = Convert.ToInt64(await balCmd.ExecuteScalarAsync(cancellationToken));
        }

        long balanceAfter = priorBalance + creditCents - debitCents;

        await using SqliteCommand ins = connection.CreateCommand();
        ins.Transaction = transaction;
        ins.CommandText = """
            INSERT INTO supplier_payable_entries (
                id, supplier_id, supplier_invoice_id, entry_type, reference_number,
                debit_cents, credit_cents, balance_after_cents, note, actor_user_id, created_at
            )
            VALUES (
                $id, $supId, $invId, $type, $refNo,
                $debit, $credit, $balAfter, $note, $actor, $now
            );
            """;
        ins.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        ins.Parameters.AddWithValue("$supId", supplierId.ToString("D"));
        ins.Parameters.AddWithValue("$invId", supplierInvoiceId.ToString("D"));
        ins.Parameters.AddWithValue("$type", entryType);
        ins.Parameters.AddWithValue("$refNo", referenceNumber);
        ins.Parameters.AddWithValue("$debit", debitCents);
        ins.Parameters.AddWithValue("$credit", creditCents);
        ins.Parameters.AddWithValue("$balAfter", balanceAfter);
        ins.Parameters.AddWithValue("$note", (object?)Clean(note) ?? DBNull.Value);
        ins.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
        ins.Parameters.AddWithValue("$now", now.ToString("O"));
        await ins.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<SupplierInvoiceModel?> LoadSupplierInvoiceAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid invoiceId,
        DateOnly asOfDate,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            SELECT i.id, i.ap_voucher_number, i.supplier_id, s.name, i.supplier_invoice_no,
                   i.purchase_order_id, po.po_number, i.receiving_report_id, rr.rr_number,
                   i.invoice_date, i.due_date, i.status, i.match_status,
                   i.subtotal_cents, i.discount_cents, i.tax_cents, i.withholding_tax_cents,
                   i.total_invoice_cents, i.credit_memo_applied_cents, i.paid_amount_cents,
                   i.remaining_balance_cents, i.variance_notes, i.variance_approved_by_user_id,
                   i.variance_approval_reason, i.recorded_by_user_id, i.posted_by_user_id,
                   i.posted_at, i.created_at, i.updated_at
            FROM supplier_invoices i
            JOIN suppliers s ON s.id = i.supplier_id
            LEFT JOIN purchase_orders po ON po.id = i.purchase_order_id
            LEFT JOIN receiving_reports rr ON rr.id = i.receiving_report_id
            WHERE i.id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", invoiceId.ToString("D"));

        Guid id, supId, recBy;
        string apvNo, supName, invNo, status, matchStatus;
        Guid? poId, rrId, varAppBy, postBy;
        string? poNo, rrNo, varNotes, varReason;
        DateOnly invDate, dueDate;
        long sub, disc, tax, ewt, total, creditMemo, paid, rem;
        DateTimeOffset? postAt;
        DateTimeOffset createdAt, updatedAt;

        await using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken)) return null;
            id = Guid.Parse(reader.GetString(0));
            apvNo = reader.GetString(1);
            supId = Guid.Parse(reader.GetString(2));
            supName = reader.GetString(3);
            invNo = reader.GetString(4);
            poId = reader.IsDBNull(5) ? null : Guid.Parse(reader.GetString(5));
            poNo = reader.IsDBNull(6) ? null : reader.GetString(6);
            rrId = reader.IsDBNull(7) ? null : Guid.Parse(reader.GetString(7));
            rrNo = reader.IsDBNull(8) ? null : reader.GetString(8);
            invDate = DateOnly.Parse(reader.GetString(9));
            dueDate = DateOnly.Parse(reader.GetString(10));
            status = reader.GetString(11);
            matchStatus = reader.GetString(12);
            sub = reader.GetInt64(13);
            disc = reader.GetInt64(14);
            tax = reader.GetInt64(15);
            ewt = reader.GetInt64(16);
            total = reader.GetInt64(17);
            creditMemo = reader.GetInt64(18);
            paid = reader.GetInt64(19);
            rem = reader.GetInt64(20);
            varNotes = reader.IsDBNull(21) ? null : reader.GetString(21);
            varAppBy = reader.IsDBNull(22) ? null : Guid.Parse(reader.GetString(22));
            varReason = reader.IsDBNull(23) ? null : reader.GetString(23);
            recBy = Guid.Parse(reader.GetString(24));
            postBy = reader.IsDBNull(25) ? null : Guid.Parse(reader.GetString(25));
            postAt = reader.IsDBNull(26) ? null : DateTimeOffset.Parse(reader.GetString(26));
            createdAt = DateTimeOffset.Parse(reader.GetString(27));
            updatedAt = DateTimeOffset.Parse(reader.GetString(28));
        }

        List<SupplierInvoiceLineModel> lines = new();
        await using (SqliteCommand lineCmd = connection.CreateCommand())
        {
            lineCmd.Transaction = transaction;
            lineCmd.CommandText = """
                SELECT id, supplier_invoice_id, purchase_order_line_id, receiving_report_line_id,
                       product_id, item_description, po_ordered_qty_x1000, rr_accepted_qty_x1000,
                       invoiced_qty_x1000, po_unit_cost_cents, invoiced_unit_cost_cents,
                       tax_cents, line_total_cents, qty_variance_x1000, unit_cost_variance_cents,
                       line_match_status
                FROM supplier_invoice_lines
                WHERE supplier_invoice_id = $id;
                """;
            lineCmd.Parameters.AddWithValue("$id", invoiceId.ToString("D"));
            await using SqliteDataReader reader = await lineCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                lines.Add(new SupplierInvoiceLineModel(
                    Guid.Parse(reader.GetString(0)),
                    Guid.Parse(reader.GetString(1)),
                    reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2)),
                    reader.IsDBNull(3) ? null : Guid.Parse(reader.GetString(3)),
                    reader.IsDBNull(4) ? null : Guid.Parse(reader.GetString(4)),
                    reader.GetString(5),
                    reader.GetInt64(6),
                    reader.GetInt64(7),
                    reader.GetInt64(8),
                    reader.GetInt64(9),
                    reader.GetInt64(10),
                    reader.GetInt64(11),
                    reader.GetInt64(12),
                    reader.GetInt64(13),
                    reader.GetInt64(14),
                    reader.GetString(15)));
            }
        }

        int daysPastDue = rem > 0 ? Math.Max(0, asOfDate.DayNumber - dueDate.DayNumber) : 0;
        string agingBucket = rem > 0 ? ComputeAgingBucket(daysPastDue) : "Paid";

        return new SupplierInvoiceModel(
            id, apvNo, supId, supName, invNo, poId, poNo, rrId, rrNo,
            invDate, dueDate, status, matchStatus, sub, disc, tax, ewt,
            total, creditMemo, paid, rem, daysPastDue, agingBucket,
            varNotes, varAppBy, varReason, recBy, postBy, postAt, createdAt, updatedAt, lines);
    }

    private static string NormalizeInvoiceNumber(string raw) =>
        new string(raw.Trim().ToUpperInvariant().Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());

    private static async Task<bool> DuplicateSupplierInvoiceExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid supplierId,
        string normalizedInvoiceNo,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            SELECT COUNT(*) FROM supplier_invoices
            WHERE supplier_id = $supId
              AND normalized_supplier_invoice_no = $normNo
              AND status <> 'Voided';
            """;
        cmd.Parameters.AddWithValue("$supId", supplierId.ToString("D"));
        cmd.Parameters.AddWithValue("$normNo", normalizedInvoiceNo);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static string ComputeAgingBucket(int daysPastDue) =>
        daysPastDue switch
        {
            <= 0 => "Current",
            <= 30 => "1-30 Days",
            <= 60 => "31-60 Days",
            <= 90 => "61-90 Days",
            _ => "Over 90 Days"
        };

    private static async Task<int> ScalarIntAsync(SqliteConnection connection, string sql, CancellationToken ct)
    {
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    private static async Task<long> ScalarLongAsync(SqliteConnection connection, string sql, CancellationToken ct)
    {
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }
}
