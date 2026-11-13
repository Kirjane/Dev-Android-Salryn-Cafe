using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Models.ProcureToPay;
using Salryn.Core.Services;

namespace Salryn.Infrastructure.Data.Services;

public sealed partial class SqliteProcureToPayService
{
    public async Task<Result<IReadOnlyList<ReceivingReportModel>>> GetReceivingReportsAsync(
        Guid? purchaseOrderId = null,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        List<Guid> ids = new();
        await using (SqliteCommand cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id FROM receiving_reports
                WHERE ($poId IS NULL OR purchase_order_id = $poId)
                ORDER BY created_at DESC;
                """;
            cmd.Parameters.AddWithValue("$poId", purchaseOrderId?.ToString("D") ?? (object)DBNull.Value);
            await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                ids.Add(Guid.Parse(reader.GetString(0)));
            }
        }

        List<ReceivingReportModel> list = new();
        foreach (Guid id in ids)
        {
            ReceivingReportModel? rr = await LoadReceivingReportAsync(connection, null, id, cancellationToken);
            if (rr is not null) list.Add(rr);
        }

        return Result<IReadOnlyList<ReceivingReportModel>>.Success(list);
    }

    public async Task<Result<ReceivingReportModel>> GetReceivingReportByIdAsync(
        Guid receivingReportId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        ReceivingReportModel? rr = await LoadReceivingReportAsync(connection, null, receivingReportId, cancellationToken);
        return rr is not null
            ? Result<ReceivingReportModel>.Success(rr)
            : Result<ReceivingReportModel>.Fail(Error.NotFound("Lpi.Receiving.NotFound", "Receiving report was not found."));
    }

    public async Task<Result<ReceivingReportModel>> CreateReceivingReportAsync(
        CreateReceivingReportRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.PurchaseOrderId == Guid.Empty || request.ReceivedByUserId == Guid.Empty)
        {
            return Result<ReceivingReportModel>.Fail(
                Error.Validation("Lpi.Receiving.MissingFields", "Purchase order ID and receiving user ID are required."));
        }
        if (string.IsNullOrWhiteSpace(request.DeliveryReceiptNo))
        {
            return Result<ReceivingReportModel>.Fail(
                Error.Validation("Lpi.Receiving.DeliveryReceiptRequired", "Supplier Delivery Receipt (DR) number is required."));
        }
        if (request.Lines is null || request.Lines.Count == 0)
        {
            return Result<ReceivingReportModel>.Fail(
                Error.Validation("Lpi.Receiving.LinesRequired", "At least one receiving line item is required."));
        }

        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        SqlitePragma.Apply(connection);

        string payloadHash = ComputeReceivingPayloadHash(
            request.PurchaseOrderId,
            request.LocationId,
            request.DeliveryReceiptNo,
            request.Lines);

        string? clientReqId = Clean(request.ClientRequestId);
        if (!string.IsNullOrWhiteSpace(clientReqId))
        {
            await using SqliteCommand checkCmd = connection.CreateCommand();
            checkCmd.CommandText = "SELECT id, request_payload_hash FROM receiving_reports WHERE client_request_id = $reqId LIMIT 1;";
            checkCmd.Parameters.AddWithValue("$reqId", clientReqId);
            await using SqliteDataReader checkReader = await checkCmd.ExecuteReaderAsync(cancellationToken);
            if (await checkReader.ReadAsync(cancellationToken))
            {
                Guid existingRrId = Guid.Parse(checkReader.GetString(0));
                string? existingHash = checkReader.IsDBNull(1) ? null : checkReader.GetString(1);
                checkReader.Close();

                if (existingHash is not null && !string.Equals(existingHash, payloadHash, StringComparison.Ordinal))
                {
                    return Result<ReceivingReportModel>.Fail(
                        Error.Conflict(
                            "Lpi.Receiving.IdempotencyConflict",
                            $"A receiving report with client_request_id '{clientReqId}' was already submitted with different lines or quantities."));
                }

                ReceivingReportModel? replay = await LoadReceivingReportAsync(connection, null, existingRrId, cancellationToken);
                if (replay is not null) return Result<ReceivingReportModel>.Success(replay);
            }
        }

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        PurchaseOrderModel? po = await LoadPurchaseOrderAsync(connection, transaction, request.PurchaseOrderId, cancellationToken);
        if (po is null)
        {
            return Result<ReceivingReportModel>.Fail(Error.NotFound("Lpi.Receiving.PoNotFound", "Purchase order was not found."));
        }
        if (po.Status is not ("Approved" or "Sent" or "Confirmed" or "PartiallyReceived"))
        {
            return Result<ReceivingReportModel>.Fail(
                Error.Conflict("Lpi.Receiving.InvalidPoState", $"Cannot receive goods against PO '{po.PoNumber}' in '{po.Status}' status."));
        }

        Dictionary<Guid, PurchaseOrderLineModel> poLineMap = po.Lines.ToDictionary(x => x.Id);
        bool hasOverdelivery = false;

        foreach (var line in request.Lines)
        {
            if (!poLineMap.TryGetValue(line.PurchaseOrderLineId, out PurchaseOrderLineModel? poLine))
            {
                return Result<ReceivingReportModel>.Fail(
                    Error.Validation("Lpi.Receiving.InvalidPoLine", "One or more receiving lines do not belong to the target Purchase Order."));
            }
            if (line.ActualDeliveredQtyX1000 < 0 || line.AcceptedQtyX1000 < 0 || line.RejectedQtyX1000 < 0)
            {
                return Result<ReceivingReportModel>.Fail(
                    Error.Validation("Lpi.Receiving.NegativeQty", "Delivered, accepted, and rejected quantities cannot be negative."));
            }
            if (line.AcceptedQtyX1000 + line.RejectedQtyX1000 != line.ActualDeliveredQtyX1000)
            {
                return Result<ReceivingReportModel>.Fail(
                    Error.Validation("Lpi.Receiving.QtyMismatch", $"For '{poLine.ItemDescription}', Accepted ({line.AcceptedQtyX1000}) + Rejected ({line.RejectedQtyX1000}) must equal Actual Delivered ({line.ActualDeliveredQtyX1000})."));
            }
            if (line.RejectedQtyX1000 > 0 && string.IsNullOrWhiteSpace(line.RejectionReason))
            {
                return Result<ReceivingReportModel>.Fail(
                    Error.Validation("Lpi.Receiving.RejectionReasonRequired", $"A rejection reason is required when rejecting quantity on '{poLine.ItemDescription}'."));
            }

            long remainingOrdered = Math.Max(0, poLine.OrderedQtyX1000 - poLine.ReceivedAcceptedQtyX1000 - poLine.CancelledQtyX1000);
            if (line.AcceptedQtyX1000 > remainingOrdered)
            {
                hasOverdelivery = true;
            }
        }

        // Separation of duties enforcement:
        if (hasOverdelivery && request.OverdeliveryApproverUserId.HasValue && request.OverdeliveryApproverUserId.Value == request.ReceivedByUserId)
        {
            return Result<ReceivingReportModel>.Fail(
                Error.Validation("Lpi.Receiving.SelfApprovalForbidden", "Separation of duties required: receiving operator cannot approve their own overdelivery exception."));
        }
        string rrNumber = await AllocateSequenceInTransactionAsync(connection, transaction, "RR", cancellationToken);
        Guid rrId = Guid.NewGuid();
        Guid locationId = request.LocationId ?? po.DeliveryLocationId;
        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateOnly receivedDate = request.ReceivedDate ?? DateOnly.FromDateTime(now.UtcDateTime);
        string initialStatus = hasOverdelivery && !request.OverdeliveryApproverUserId.HasValue
            ? "PendingExceptionApproval"
            : "Draft";

        await using (SqliteCommand cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = """
                INSERT INTO receiving_reports (
                    id, rr_number, purchase_order_id, supplier_id, location_id,
                    delivery_receipt_no, supplier_invoice_ref, carrier_or_driver,
                    received_date, status, has_overdelivery,
                    overdelivery_approved_by_user_id, overdelivery_approval_reason,
                    received_by_user_id, inspected_by_user_id, client_request_id,
                    request_payload_hash, notes, created_at, updated_at
                )
                VALUES (
                    $id, $rrNo, $poId, $supId, $locId,
                    $drNo, $invRef, $carrier,
                    $recDate, $status, $hasOver,
                    $overAppBy, $overReason,
                    $recBy, $recBy, $clientReqId,
                    $hash, $notes, $now, $now
                );
                """;
            cmd.Parameters.AddWithValue("$id", rrId.ToString("D"));
            cmd.Parameters.AddWithValue("$rrNo", rrNumber);
            cmd.Parameters.AddWithValue("$poId", po.Id.ToString("D"));
            cmd.Parameters.AddWithValue("$supId", po.SupplierId.ToString("D"));
            cmd.Parameters.AddWithValue("$locId", locationId.ToString("D"));
            cmd.Parameters.AddWithValue("$drNo", request.DeliveryReceiptNo.Trim());
            cmd.Parameters.AddWithValue("$invRef", (object?)Clean(request.SupplierInvoiceRef) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$carrier", (object?)Clean(request.CarrierOrDriver) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$recDate", receivedDate.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("$status", initialStatus);
            cmd.Parameters.AddWithValue("$hasOver", hasOverdelivery ? 1 : 0);
            cmd.Parameters.AddWithValue("$overAppBy", request.OverdeliveryApproverUserId?.ToString("D") ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$overReason", (object?)Clean(request.OverdeliveryApprovalReason) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$recBy", request.ReceivedByUserId.ToString("D"));
            cmd.Parameters.AddWithValue("$clientReqId", (object?)clientReqId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$hash", payloadHash);
            cmd.Parameters.AddWithValue("$notes", (object?)Clean(request.Notes) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$now", now.ToString("O"));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var line in request.Lines)
        {
            PurchaseOrderLineModel poLine = poLineMap[line.PurchaseOrderLineId];
            long prevReceived = poLine.ReceivedAcceptedQtyX1000;
            long remainingOrdered = Math.Max(0, poLine.OrderedQtyX1000 - prevReceived - poLine.CancelledQtyX1000);
            long shortage = Math.Max(0, remainingOrdered - line.AcceptedQtyX1000);
            long overdelivery = Math.Max(0, line.AcceptedQtyX1000 - remainingOrdered);
            long acceptedLineTotal = (line.AcceptedQtyX1000 * poLine.UnitCostCents) / 1000L;
            string disposition = line.RejectedQtyX1000 > 0
                ? (Clean(line.RejectionDisposition) ?? "QuarantineHold")
                : "None";

            await using SqliteCommand lineCmd = connection.CreateCommand();
            lineCmd.Transaction = transaction;
            lineCmd.CommandText = """
                INSERT INTO receiving_report_lines (
                    id, receiving_report_id, purchase_order_line_id, product_id,
                    item_code, item_description, purchase_classification, unit_name,
                    ordered_qty_x1000, previously_received_qty_x1000, actual_delivered_qty_x1000,
                    accepted_qty_x1000, rejected_qty_x1000, shortage_qty_x1000, overdelivery_qty_x1000,
                    unit_cost_cents, accepted_line_total_cents, rejection_reason, rejection_disposition,
                    lot_or_batch_no, expiry_date
                )
                VALUES (
                    $id, $rrId, $poLineId, $prodId,
                    $code, $desc, $class, $unit,
                    $ordQty, $prevQty, $actQty,
                    $accQty, $rejQty, $shortQty, $overQty,
                    $unitCost, $accTotal, $rejReason, $disp,
                    $lot, $exp
                );
                """;
            lineCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
            lineCmd.Parameters.AddWithValue("$rrId", rrId.ToString("D"));
            lineCmd.Parameters.AddWithValue("$poLineId", poLine.Id.ToString("D"));
            lineCmd.Parameters.AddWithValue("$prodId", poLine.ProductId?.ToString("D") ?? (object)DBNull.Value);
            lineCmd.Parameters.AddWithValue("$code", poLine.ItemCode);
            lineCmd.Parameters.AddWithValue("$desc", poLine.ItemDescription);
            lineCmd.Parameters.AddWithValue("$class", poLine.PurchaseClassification);
            lineCmd.Parameters.AddWithValue("$unit", poLine.UnitName);
            lineCmd.Parameters.AddWithValue("$ordQty", poLine.OrderedQtyX1000);
            lineCmd.Parameters.AddWithValue("$prevQty", prevReceived);
            lineCmd.Parameters.AddWithValue("$actQty", line.ActualDeliveredQtyX1000);
            lineCmd.Parameters.AddWithValue("$accQty", line.AcceptedQtyX1000);
            lineCmd.Parameters.AddWithValue("$rejQty", line.RejectedQtyX1000);
            lineCmd.Parameters.AddWithValue("$shortQty", shortage);
            lineCmd.Parameters.AddWithValue("$overQty", overdelivery);
            lineCmd.Parameters.AddWithValue("$unitCost", poLine.UnitCostCents);
            lineCmd.Parameters.AddWithValue("$accTotal", acceptedLineTotal);
            lineCmd.Parameters.AddWithValue("$rejReason", (object?)Clean(line.RejectionReason) ?? DBNull.Value);
            lineCmd.Parameters.AddWithValue("$disp", disposition);
            lineCmd.Parameters.AddWithValue("$lot", (object?)Clean(line.LotOrBatchNo) ?? DBNull.Value);
            lineCmd.Parameters.AddWithValue("$exp", line.ExpiryDate?.ToString("yyyy-MM-dd") ?? (object)DBNull.Value);
            await lineCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        if (request.PostImmediately)
        {
            Result postResult = await PostReceivingReportInternalAsync(
                connection,
                transaction,
                rrId,
                request.OverdeliveryApproverUserId,
                request.OverdeliveryApprovalReason,
                request.ReceivedByUserId,
                now,
                cancellationToken);
            if (postResult.IsFailure)
            {
                return Result<ReceivingReportModel>.Fail(postResult.ErrorDetail);
            }
        }
        else
        {
            await WriteAuditAsync(
                connection,
                transaction,
                request.ReceivedByUserId,
                "RR_CREATED_DRAFT",
                "ReceivingReport",
                rrId.ToString("D"),
                new { rrNumber, po.PoNumber, status = initialStatus, hasOverdelivery },
                null,
                $"Created receiving report {rrNumber}.",
                now,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        ReceivingReportModel? created = await LoadReceivingReportAsync(connection, null, rrId, cancellationToken);
        return Result<ReceivingReportModel>.Success(created!);
    }

    public async Task<Result<ReceivingReportModel>> PostReceivingReportAsync(
        PostReceivingReportRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ReceivingReportId == Guid.Empty || request.ActorUserId == Guid.Empty)
        {
            return Result<ReceivingReportModel>.Fail(
                Error.Validation("Lpi.Receiving.InvalidPost", "Receiving report ID and actor user ID are required."));
        }

        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        SqlitePragma.Apply(connection);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        ReceivingReportModel? existing = await LoadReceivingReportAsync(connection, transaction, request.ReceivingReportId, cancellationToken);
        if (existing is null)
        {
            return Result<ReceivingReportModel>.Fail(Error.NotFound("Lpi.Receiving.NotFound", "Receiving report was not found."));
        }
        if (existing.Status == "Posted")
        {
            return Result<ReceivingReportModel>.Success(existing);
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        Result postResult = await PostReceivingReportInternalAsync(
            connection,
            transaction,
            request.ReceivingReportId,
            request.OverdeliveryApproverUserId,
            request.OverdeliveryApprovalReason,
            request.ActorUserId,
            now,
            cancellationToken);
        if (postResult.IsFailure)
        {
            return Result<ReceivingReportModel>.Fail(postResult.ErrorDetail);
        }

        await transaction.CommitAsync(cancellationToken);

        ReceivingReportModel? posted = await LoadReceivingReportAsync(connection, null, request.ReceivingReportId, cancellationToken);
        return Result<ReceivingReportModel>.Success(posted!);
    }

    private async Task<Result> PostReceivingReportInternalAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid receivingReportId,
        Guid? overdeliveryApproverId,
        string? overdeliveryReason,
        Guid actorUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ReceivingReportModel? rr = await LoadReceivingReportAsync(connection, transaction, receivingReportId, cancellationToken);
        if (rr is null)
        {
            return Result.Fail(Error.NotFound("Lpi.Receiving.NotFound", "Receiving report was not found."));
        }
        if (rr.Status == "Posted")
        {
            return Result.Success();
        }
        if (rr.Status == "Cancelled")
        {
            return Result.Fail(Error.Conflict("Lpi.Receiving.Cancelled", "Cannot post a cancelled receiving report."));
        }

        Guid? effectiveApprover = null;
        string? effectiveReason = null;

        if (rr.HasOverdelivery)
        {
            await using SqliteCommand checkAppCmd = connection.CreateCommand();
            checkAppCmd.Transaction = transaction;
            checkAppCmd.CommandText = """
                SELECT approver_user_id, approval_reason, approved_lines_json
                FROM lpi_overdelivery_approvals
                WHERE receiving_report_id = $rrId AND is_revoked = 0
                ORDER BY approved_at DESC LIMIT 1;
                """;
            checkAppCmd.Parameters.AddWithValue("$rrId", rr.Id.ToString("D"));
            await using SqliteDataReader appReader = await checkAppCmd.ExecuteReaderAsync(cancellationToken);
            if (!await appReader.ReadAsync(cancellationToken))
            {
                return Result.Fail(Error.Conflict(
                    "Lpi.Receiving.OverdeliveryApprovalRequired",
                    $"Receiving report '{rr.RrNumber}' contains overdelivery quantities and requires an independent approved exception before posting."));
            }

            Guid approverId = Guid.Parse(appReader.GetString(0));
            effectiveReason = appReader.GetString(1);
            string approvedLinesJson = appReader.GetString(2);
            appReader.Close();

            // Separation of duties enforcement:
            if (approverId == actorUserId || approverId == rr.ReceivedByUserId)
            {
                return Result.Fail(Error.Validation(
                    "Lpi.Receiving.SelfApprovalForbidden",
                    "Separation of duties required: receiving operator cannot approve their own overdelivery exception."));
            }

            // Fail-closed approval snapshot validation
            if (string.IsNullOrWhiteSpace(approvedLinesJson))
            {
                return Result.Fail(Error.Conflict(
                    "Lpi.Receiving.InvalidApprovalSnapshot",
                    "Overdelivery approval snapshot is missing or empty. Posting cannot proceed."));
            }

            List<OverdeliveryApprovedLineModel>? approvedLines;
            try
            {
                approvedLines = System.Text.Json.JsonSerializer.Deserialize<List<OverdeliveryApprovedLineModel>>(approvedLinesJson);
            }
            catch (Exception ex)
            {
                return Result.Fail(Error.Conflict(
                    "Lpi.Receiving.InvalidApprovalSnapshot",
                    $"Overdelivery approval snapshot contains invalid JSON: {ex.Message}"));
            }

            if (approvedLines is null || approvedLines.Count == 0)
            {
                return Result.Fail(Error.Conflict(
                    "Lpi.Receiving.InvalidApprovalSnapshot",
                    "Overdelivery approval snapshot contains no approved lines."));
            }

            Dictionary<Guid, OverdeliveryApprovedLineModel> approvedMap = new();
            foreach (var appLine in approvedLines)
            {
                if (!approvedMap.TryAdd(appLine.PurchaseOrderLineId, appLine))
                {
                    return Result.Fail(Error.Conflict(
                        "Lpi.Receiving.InvalidApprovalSnapshot",
                        $"Overdelivery approval snapshot contains duplicate purchase order line '{appLine.PurchaseOrderLineId}'."));
                }
            }

            foreach (var line in rr.Lines)
            {
                if (!approvedMap.TryGetValue(line.PurchaseOrderLineId, out var appLine))
                {
                    return Result.Fail(Error.Conflict(
                        "Lpi.Receiving.UnapprovedLineDetected",
                        $"Receiving report line '{line.ItemDescription}' (POLine: {line.PurchaseOrderLineId}) has no entry in the approved snapshot."));
                }

                if (line.AcceptedQtyX1000 > appLine.ApprovedQtyX1000)
                {
                    return Result.Fail(Error.Conflict(
                        "Lpi.Receiving.OverdeliveryQuantityChanged",
                        $"Receiving report '{rr.RrNumber}' line '{line.ItemDescription}' quantity ({line.AcceptedQtyX1000 / 1000m:N2}) exceeds approved overdelivery quantity ({appLine.ApprovedQtyX1000 / 1000m:N2}); independent re-approval is required."));
                }
            }

            effectiveApprover = approverId;
        }

        foreach (ReceivingReportLineModel line in rr.Lines)
        {
            await using (SqliteCommand updPoLine = connection.CreateCommand())
            {
                updPoLine.Transaction = transaction;
                updPoLine.CommandText = """
                    UPDATE purchase_order_lines
                    SET received_accepted_qty_x1000 = received_accepted_qty_x1000 + $accQty,
                        received_rejected_qty_x1000 = received_rejected_qty_x1000 + $rejQty
                    WHERE id = $poLineId;
                    """;
                updPoLine.Parameters.AddWithValue("$accQty", line.AcceptedQtyX1000);
                updPoLine.Parameters.AddWithValue("$rejQty", line.RejectedQtyX1000);
                updPoLine.Parameters.AddWithValue("$poLineId", line.PurchaseOrderLineId.ToString("D"));
                await updPoLine.ExecuteNonQueryAsync(cancellationToken);
            }

            if (line.ProductId.HasValue)
            {
                Guid productId = line.ProductId.Value;
                (bool exists, bool trackStock, long currentStockX1000, long currentCostCents) =
                    await ReadProductStockStateAsync(connection, transaction, productId, cancellationToken);

                if (!exists)
                {
                    return Result.Fail(Error.NotFound("Lpi.Receiving.ProductNotFound", $"Linked product '{line.ItemDescription}' was not found."));
                }

                if (line.RejectedQtyX1000 > 0)
                {
                    bool isDamaged = string.Equals(line.RejectionDisposition, "DamagedWriteOff", StringComparison.OrdinalIgnoreCase);
                    Guid targetLocId = Guid.Parse(isDamaged ? DefaultDamagedLocationId : DefaultQuarantineLocationId);
                    string bucketStatus = isDamaged ? StockBucketStatuses.Damaged : StockBucketStatuses.Quarantine;

                    await UpsertStockBucketDeltaAsync(
                        connection,
                        transaction,
                        productId,
                        targetLocId,
                        bucketStatus,
                        line.RejectedQtyX1000,
                        now,
                        cancellationToken);
                }

                if (line.AcceptedQtyX1000 > 0 &&
                    trackStock &&
                    PurchaseClassifications.CanAffectSellableStock(line.PurchaseClassification))
                {
                    long nextStockX1000 = currentStockX1000 + line.AcceptedQtyX1000;
                    long nextCostCents = currentStockX1000 > 0 && nextStockX1000 > 0
                        ? ((currentStockX1000 * currentCostCents) + (line.AcceptedQtyX1000 * line.UnitCostCents)) / nextStockX1000
                        : line.UnitCostCents;

                    await using (SqliteCommand updProd = connection.CreateCommand())
                    {
                        updProd.Transaction = transaction;
                        updProd.CommandText = """
                            UPDATE products
                            SET stock_on_hand_x1000 = $after,
                                cost_price_cents = $cost,
                                supplier_id = COALESCE(supplier_id, $supId),
                                updated_at = $now
                            WHERE id = $prodId;
                            """;
                        updProd.Parameters.AddWithValue("$after", nextStockX1000);
                        updProd.Parameters.AddWithValue("$cost", nextCostCents);
                        updProd.Parameters.AddWithValue("$supId", rr.SupplierId.ToString("D"));
                        updProd.Parameters.AddWithValue("$now", now.ToString("O"));
                        updProd.Parameters.AddWithValue("$prodId", productId.ToString("D"));
                        await updProd.ExecuteNonQueryAsync(cancellationToken);
                    }

                    await UpsertStockBucketDeltaAsync(
                        connection,
                        transaction,
                        productId,
                        rr.LocationId,
                        StockBucketStatuses.Available,
                        line.AcceptedQtyX1000,
                        now,
                        cancellationToken);

                    Guid movementId = Guid.NewGuid();
                    await using (SqliteCommand movCmd = connection.CreateCommand())
                    {
                        movCmd.Transaction = transaction;
                        movCmd.CommandText = """
                            INSERT INTO stock_movements (
                                id, product_id, movement_type, qty_change_x1000,
                                cost_price_cents, reference_type, reference_id,
                                reason, user_id, created_at
                            )
                            VALUES (
                                $id, $prodId, $movType, $delta,
                                $unitCost, 'RECEIVING_REPORT', $refId,
                                $reason, $actor, $now
                            );
                            """;
                        movCmd.Parameters.AddWithValue("$id", movementId.ToString("D"));
                        movCmd.Parameters.AddWithValue("$prodId", productId.ToString("D"));
                        movCmd.Parameters.AddWithValue("$movType", (int)StockMovementType.StockIn);
                        movCmd.Parameters.AddWithValue("$delta", line.AcceptedQtyX1000);
                        movCmd.Parameters.AddWithValue("$unitCost", line.UnitCostCents);
                        movCmd.Parameters.AddWithValue("$refId", rr.Id.ToString("D"));
                        movCmd.Parameters.AddWithValue("$reason", $"RR {rr.RrNumber} (PO {rr.PoNumber}, DR {rr.DeliveryReceiptNo})");
                        movCmd.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
                        movCmd.Parameters.AddWithValue("$now", now.ToString("O"));
                        await movCmd.ExecuteNonQueryAsync(cancellationToken);
                    }

                    await using (SqliteCommand linkMov = connection.CreateCommand())
                    {
                        linkMov.Transaction = transaction;
                        linkMov.CommandText = """
                            UPDATE receiving_report_lines
                            SET stock_movement_id = $movId
                            WHERE id = $rrLineId;
                            """;
                        linkMov.Parameters.AddWithValue("$movId", movementId.ToString("D"));
                        linkMov.Parameters.AddWithValue("$rrLineId", line.Id.ToString("D"));
                        await linkMov.ExecuteNonQueryAsync(cancellationToken);
                    }
                }
            }
        }

        await using (SqliteCommand updRr = connection.CreateCommand())
        {
            updRr.Transaction = transaction;
            updRr.CommandText = """
                UPDATE receiving_reports
                SET status = 'Posted',
                    overdelivery_approved_by_user_id = $overApp,
                    overdelivery_approval_reason = $overReason,
                    posted_by_user_id = $actor,
                    posted_at = $now,
                    updated_at = $now
                WHERE id = $id;
                """;
            updRr.Parameters.AddWithValue("$overApp", effectiveApprover?.ToString("D") ?? (object)DBNull.Value);
            updRr.Parameters.AddWithValue("$overReason", (object?)effectiveReason ?? DBNull.Value);
            updRr.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
            updRr.Parameters.AddWithValue("$now", now.ToString("O"));
            updRr.Parameters.AddWithValue("$id", rr.Id.ToString("D"));
            await updRr.ExecuteNonQueryAsync(cancellationToken);
        }

        PurchaseOrderModel? updatedPo = await LoadPurchaseOrderAsync(connection, transaction, rr.PurchaseOrderId, cancellationToken);
        if (updatedPo is not null)
        {
            bool allReceived = updatedPo.Lines.All(l => l.ReceivedAcceptedQtyX1000 + l.CancelledQtyX1000 >= l.OrderedQtyX1000);
            bool anyReceived = updatedPo.Lines.Any(l => l.ReceivedAcceptedQtyX1000 > 0);
            string nextPoStatus = allReceived
                ? "FullyReceived"
                : (anyReceived ? "PartiallyReceived" : updatedPo.Status);

            if (nextPoStatus != updatedPo.Status)
            {
                await using SqliteCommand updPo = connection.CreateCommand();
                updPo.Transaction = transaction;
                updPo.CommandText = """
                    UPDATE purchase_orders
                    SET status = $status,
                        updated_at = $now
                    WHERE id = $poId;
                    """;
                updPo.Parameters.AddWithValue("$status", nextPoStatus);
                updPo.Parameters.AddWithValue("$now", now.ToString("O"));
                updPo.Parameters.AddWithValue("$poId", updatedPo.Id.ToString("D"));
                await updPo.ExecuteNonQueryAsync(cancellationToken);

                await RecordPurchaseOrderHistoryAsync(
                    connection,
                    transaction,
                    updatedPo.Id,
                    "GoodsReceived",
                    actorUserId,
                    updatedPo.Status,
                    nextPoStatus,
                    $"Posted Receiving Report {rr.RrNumber} (DR {rr.DeliveryReceiptNo}).",
                    now,
                    cancellationToken);
            }
        }

        await WriteAuditAsync(
            connection,
            transaction,
            actorUserId,
            OperatorAuditActions.StockInRecorded,
            "ReceivingReport",
            rr.Id.ToString("D"),
            new
            {
                rr.RrNumber,
                rr.PoNumber,
                rr.DeliveryReceiptNo,
                status = "Posted",
                acceptedTotalCents = rr.Lines.Sum(l => l.AcceptedLineTotalCents)
            },
            new { status = rr.Status },
            $"Posted receiving report {rr.RrNumber} against PO {rr.PoNumber}.",
            now,
            cancellationToken);

        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<StockCountAdjustmentModel>>> GetStockCountAdjustmentsAsync(
        Guid? productId = null,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT a.id, a.adjustment_number, a.product_id, p.name, p.sku,
                   a.location_id, loc.name, a.system_qty_x1000, a.actual_counted_qty_x1000,
                   a.variance_qty_x1000, a.unit_cost_cents, a.variance_value_cents,
                   a.reason_code, a.notes, a.status,
                   a.counted_by_user_id, COALESCE(uc.display_name, uc.username, 'Operator'),
                   a.approved_by_user_id, COALESCE(ua.display_name, ua.username, 'Manager'),
                   a.stock_movement_id, a.created_at, a.posted_at
            FROM stock_count_adjustments a
            JOIN products p ON p.id = a.product_id
            JOIN inventory_locations loc ON loc.id = a.location_id
            LEFT JOIN users uc ON uc.id = a.counted_by_user_id
            LEFT JOIN users ua ON ua.id = a.approved_by_user_id
            WHERE ($prodId IS NULL OR a.product_id = $prodId)
            ORDER BY a.created_at DESC;
            """;
        cmd.Parameters.AddWithValue("$prodId", productId?.ToString("D") ?? (object)DBNull.Value);

        List<StockCountAdjustmentModel> list = new();
        await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new StockCountAdjustmentModel(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                Guid.Parse(reader.GetString(2)),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                Guid.Parse(reader.GetString(5)),
                reader.GetString(6),
                reader.GetInt64(7),
                reader.GetInt64(8),
                reader.GetInt64(9),
                reader.GetInt64(10),
                reader.GetInt64(11),
                reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.GetString(14),
                Guid.Parse(reader.GetString(15)),
                reader.GetString(16),
                Guid.Parse(reader.GetString(17)),
                reader.GetString(18),
                reader.IsDBNull(19) ? null : Guid.Parse(reader.GetString(19)),
                DateTimeOffset.Parse(reader.GetString(20)),
                reader.IsDBNull(21) ? null : DateTimeOffset.Parse(reader.GetString(21))));
        }

        return Result<IReadOnlyList<StockCountAdjustmentModel>>.Success(list);
    }

    public async Task<Result<StockCountAdjustmentModel>> RecordStockCountAdjustmentAsync(
        RecordStockCountAdjustmentRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ProductId == Guid.Empty || request.CountedByUserId == Guid.Empty || request.ApprovedByUserId == Guid.Empty)
        {
            return Result<StockCountAdjustmentModel>.Fail(
                Error.Validation("Lpi.Adjustment.InvalidActors", "Product, counter user, and approver user are required."));
        }
        if (request.ActualCountedQtyX1000 < 0)
        {
            return Result<StockCountAdjustmentModel>.Fail(
                Error.Validation("Lpi.Adjustment.NegativeCount", "Actual counted quantity cannot be negative."));
        }
        if (string.IsNullOrWhiteSpace(request.ReasonCode))
        {
            return Result<StockCountAdjustmentModel>.Fail(
                Error.Validation("Lpi.Adjustment.ReasonRequired", "An adjustment reason code is required."));
        }

        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        SqlitePragma.Apply(connection);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        (bool exists, bool trackStock, long systemQtyX1000, long unitCostCents) =
            await ReadProductStockStateAsync(connection, transaction, request.ProductId, cancellationToken);
        if (!exists || !trackStock)
        {
            return Result<StockCountAdjustmentModel>.Fail(
                Error.Validation("Lpi.Adjustment.ProductNotTracked", "Target product was not found or does not track stock."));
        }

        Guid locationId = request.LocationId ?? Guid.Parse(DefaultMainLocationId);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        long currentLocQtyX1000 = 0;
        await using (SqliteCommand locReadCmd = connection.CreateCommand())
        {
            locReadCmd.Transaction = transaction;
            locReadCmd.CommandText = """
                SELECT quantity_x1000 FROM product_stock_buckets
                WHERE product_id = $prodId AND location_id = $locId AND bucket_status = 'Available'
                LIMIT 1;
                """;
            locReadCmd.Parameters.AddWithValue("$prodId", request.ProductId.ToString("D"));
            locReadCmd.Parameters.AddWithValue("$locId", locationId.ToString("D"));
            object? val = await locReadCmd.ExecuteScalarAsync(cancellationToken);
            if (val is not null && val is not DBNull)
            {
                currentLocQtyX1000 = Convert.ToInt64(val);
            }
            else
            {
                await using (SqliteCommand anyBucketCmd = connection.CreateCommand())
                {
                    anyBucketCmd.Transaction = transaction;
                    anyBucketCmd.CommandText = "SELECT COUNT(*) FROM product_stock_buckets WHERE product_id = $prodId;";
                    anyBucketCmd.Parameters.AddWithValue("$prodId", request.ProductId.ToString("D"));
                    long bucketCount = Convert.ToInt64(await anyBucketCmd.ExecuteScalarAsync(cancellationToken));
                    currentLocQtyX1000 = bucketCount == 0 ? systemQtyX1000 : 0L;
                }
            }
        }

        long locationVarianceQtyX1000 = request.ActualCountedQtyX1000 - currentLocQtyX1000;
        long varianceValueCents = (locationVarianceQtyX1000 * unitCostCents) / 1000L;
        string adjNumber = await AllocateSequenceInTransactionAsync(connection, transaction, "ADJ", cancellationToken);
        Guid adjId = Guid.NewGuid();
        Guid? movementId = null;

        if (locationVarianceQtyX1000 != 0)
        {
            long newProductStockX1000 = checked(systemQtyX1000 + locationVarianceQtyX1000);
            if (newProductStockX1000 < 0)
            {
                return Result<StockCountAdjustmentModel>.Fail(
                    Error.Conflict("Lpi.Adjustment.NegativeProductStock", "Count adjustment would make total product stock negative."));
            }

            await using (SqliteCommand updProd = connection.CreateCommand())
            {
                updProd.Transaction = transaction;
                updProd.CommandText = """
                    UPDATE products
                    SET stock_on_hand_x1000 = $actual,
                        updated_at = $now
                    WHERE id = $prodId;
                    """;
                updProd.Parameters.AddWithValue("$actual", newProductStockX1000);
                updProd.Parameters.AddWithValue("$now", now.ToString("O"));
                updProd.Parameters.AddWithValue("$prodId", request.ProductId.ToString("D"));
                await updProd.ExecuteNonQueryAsync(cancellationToken);
            }

            // Explicitly set the location bucket quantity to the actual counted quantity
            await using (SqliteCommand bucketCmd = connection.CreateCommand())
            {
                bucketCmd.Transaction = transaction;
                bucketCmd.CommandText = """
                    INSERT INTO product_stock_buckets (id, product_id, location_id, bucket_status, quantity_x1000, updated_at)
                    VALUES ($id, $prodId, $locId, 'Available', $qty, $now)
                    ON CONFLICT(product_id, location_id, bucket_status)
                    DO UPDATE SET quantity_x1000 = $qty, updated_at = $now;
                    """;
                bucketCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
                bucketCmd.Parameters.AddWithValue("$prodId", request.ProductId.ToString("D"));
                bucketCmd.Parameters.AddWithValue("$locId", locationId.ToString("D"));
                bucketCmd.Parameters.AddWithValue("$qty", request.ActualCountedQtyX1000);
                bucketCmd.Parameters.AddWithValue("$now", now.ToString("O"));
                await bucketCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            movementId = Guid.NewGuid();
            StockMovementType movType = locationVarianceQtyX1000 > 0
                ? StockMovementType.AdjustmentAdd
                : (request.ReasonCode.Contains("Damage", StringComparison.OrdinalIgnoreCase)
                    ? StockMovementType.Damage
                    : StockMovementType.AdjustmentRemove);

            await using (SqliteCommand movCmd = connection.CreateCommand())
            {
                movCmd.Transaction = transaction;
                movCmd.CommandText = """
                    INSERT INTO stock_movements (
                        id, product_id, movement_type, qty_change_x1000,
                        cost_price_cents, reference_type, reference_id,
                        reason, user_id, created_at
                    )
                    VALUES (
                        $id, $prodId, $movType, $delta,
                        $unitCost, 'STOCK_COUNT_ADJUSTMENT', $refId,
                        $reason, $actor, $now
                    );
                    """;
                movCmd.Parameters.AddWithValue("$id", movementId.Value.ToString("D"));
                movCmd.Parameters.AddWithValue("$prodId", request.ProductId.ToString("D"));
                movCmd.Parameters.AddWithValue("$movType", (int)movType);
                movCmd.Parameters.AddWithValue("$delta", locationVarianceQtyX1000);
                movCmd.Parameters.AddWithValue("$unitCost", unitCostCents);
                movCmd.Parameters.AddWithValue("$refId", adjId.ToString("D"));
                movCmd.Parameters.AddWithValue("$reason", $"{adjNumber} ({request.ReasonCode.Trim()}): {Clean(request.Notes) ?? "Physical count reconciliation"}");
                movCmd.Parameters.AddWithValue("$actor", request.ApprovedByUserId.ToString("D"));
                movCmd.Parameters.AddWithValue("$now", now.ToString("O"));
                await movCmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await using (SqliteCommand ins = connection.CreateCommand())
        {
            ins.Transaction = transaction;
            ins.CommandText = """
                INSERT INTO stock_count_adjustments (
                    id, adjustment_number, product_id, location_id,
                    system_qty_x1000, actual_counted_qty_x1000, variance_qty_x1000,
                    unit_cost_cents, variance_value_cents, reason_code, notes,
                    status, counted_by_user_id, approved_by_user_id, stock_movement_id,
                    created_at, posted_at
                )
                VALUES (
                    $id, $adjNo, $prodId, $locId,
                    $sysQty, $actQty, $varQty,
                    $unitCost, $varVal, $reason, $notes,
                    'Posted', $countedBy, $approvedBy, $movId,
                    $now, $now
                );
                """;
            ins.Parameters.AddWithValue("$id", adjId.ToString("D"));
            ins.Parameters.AddWithValue("$adjNo", adjNumber);
            ins.Parameters.AddWithValue("$prodId", request.ProductId.ToString("D"));
            ins.Parameters.AddWithValue("$locId", locationId.ToString("D"));
            ins.Parameters.AddWithValue("$sysQty", systemQtyX1000);
            ins.Parameters.AddWithValue("$actQty", request.ActualCountedQtyX1000);
            ins.Parameters.AddWithValue("$varQty", locationVarianceQtyX1000);
            ins.Parameters.AddWithValue("$unitCost", unitCostCents);
            ins.Parameters.AddWithValue("$varVal", varianceValueCents);
            ins.Parameters.AddWithValue("$reason", request.ReasonCode.Trim());
            ins.Parameters.AddWithValue("$notes", (object?)Clean(request.Notes) ?? DBNull.Value);
            ins.Parameters.AddWithValue("$countedBy", request.CountedByUserId.ToString("D"));
            ins.Parameters.AddWithValue("$approvedBy", request.ApprovedByUserId.ToString("D"));
            ins.Parameters.AddWithValue("$movId", movementId?.ToString("D") ?? (object)DBNull.Value);
            ins.Parameters.AddWithValue("$now", now.ToString("O"));
            await ins.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteAuditAsync(
            connection,
            transaction,
            request.ApprovedByUserId,
            OperatorAuditActions.StockAdjustmentRecorded,
            "StockCountAdjustment",
            adjId.ToString("D"),
            new { adjNumber, systemQtyX1000, request.ActualCountedQtyX1000, locationVarianceQtyX1000, varianceValueCents, request.ReasonCode },
            new { systemQtyX1000 },
            $"Physical count adjustment {adjNumber}: {request.ReasonCode}",
            now,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        var all = await GetStockCountAdjustmentsAsync(request.ProductId, cancellationToken);
        return Result<StockCountAdjustmentModel>.Success(all.Value.First(x => x.Id == adjId));
    }

    public async Task<Result<IReadOnlyList<SupplierReturnModel>>> GetSupplierReturnsAsync(
        Guid? supplierId = null,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        List<Guid> ids = new();
        await using (SqliteCommand cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id FROM supplier_returns
                WHERE ($supId IS NULL OR supplier_id = $supId)
                ORDER BY created_at DESC;
                """;
            cmd.Parameters.AddWithValue("$supId", supplierId?.ToString("D") ?? (object)DBNull.Value);
            await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                ids.Add(Guid.Parse(reader.GetString(0)));
            }
        }

        List<SupplierReturnModel> list = new();
        foreach (Guid id in ids)
        {
            SupplierReturnModel? ret = await LoadSupplierReturnAsync(connection, null, id, cancellationToken);
            if (ret is not null) list.Add(ret);
        }

        return Result<IReadOnlyList<SupplierReturnModel>>.Success(list);
    }

    public async Task<Result<SupplierReturnModel>> CreateSupplierReturnAsync(
        CreateSupplierReturnRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ReceivingReportId == Guid.Empty || string.IsNullOrWhiteSpace(request.Reason) || request.Lines is null || request.Lines.Count == 0)
        {
            return Result<SupplierReturnModel>.Fail(
                Error.Validation("Lpi.SupplierReturn.Invalid", "Receiving report, reason, and at least one return line are required."));
        }

        if (request.SupplierInvoiceId == Guid.Empty)
        {
            return Result<SupplierReturnModel>.Fail(
                Error.Validation("Lpi.SupplierReturn.InvalidInvoiceId", "Target supplier invoice ID cannot be empty."));
        }

        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        SqlitePragma.Apply(connection);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        ReceivingReportModel? rr = await LoadReceivingReportAsync(connection, transaction, request.ReceivingReportId, cancellationToken);
        if (rr is null || rr.Status != "Posted")
        {
            return Result<SupplierReturnModel>.Fail(
                Error.Conflict("Lpi.SupplierReturn.RrNotPosted", "Supplier returns after acceptance require a Posted receiving report."));
        }

        Dictionary<Guid, ReceivingReportLineModel> rrLines = rr.Lines.ToDictionary(x => x.Id);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateOnly returnDate = DateOnly.FromDateTime(now.UtcDateTime);
        long totalReturnCents = 0;
        Dictionary<Guid, long> requestedQtyByRrLine = new();
        Dictionary<Guid, long> requestedStockByProduct = new();

        foreach (var line in request.Lines)
        {
            if (!rrLines.TryGetValue(line.ReceivingReportLineId, out ReceivingReportLineModel? rrLine) || !rrLine.ProductId.HasValue)
            {
                return Result<SupplierReturnModel>.Fail(
                    Error.Validation("Lpi.SupplierReturn.InvalidLine", "Return line must reference an accepted product line on the Receiving Report."));
            }

            long previouslyReturnedX1000 = 0;
            await using (SqliteCommand priorRetCmd = connection.CreateCommand())
            {
                priorRetCmd.Transaction = transaction;
                priorRetCmd.CommandText = """
                    SELECT COALESCE(SUM(srl.return_qty_x1000), 0)
                    FROM supplier_return_lines srl
                    INNER JOIN supplier_returns sr ON sr.id = srl.supplier_return_id
                    WHERE srl.receiving_report_line_id = $rrLineId
                      AND sr.status = 'Posted';
                    """;
                priorRetCmd.Parameters.AddWithValue("$rrLineId", line.ReceivingReportLineId.ToString("D"));
                previouslyReturnedX1000 = Convert.ToInt64(await priorRetCmd.ExecuteScalarAsync(cancellationToken) ?? 0L);
            }

            long remainingAcceptedX1000 = Math.Max(0L, rrLine.AcceptedQtyX1000 - previouslyReturnedX1000);
            long nextCumulativeForLine = checked(requestedQtyByRrLine.GetValueOrDefault(line.ReceivingReportLineId) + line.ReturnQtyX1000);

            if (line.ReturnQtyX1000 <= 0 || nextCumulativeForLine > remainingAcceptedX1000)
            {
                return Result<SupplierReturnModel>.Fail(
                    Error.Validation("Lpi.SupplierReturn.InvalidQty",
                        $"Return quantity for '{rrLine.ItemDescription}' ({nextCumulativeForLine}) exceeds remaining accepted quantity ({remainingAcceptedX1000}). Previously returned: {previouslyReturnedX1000}."));
            }

            requestedQtyByRrLine[line.ReceivingReportLineId] = nextCumulativeForLine;
            Guid productId = rrLine.ProductId.Value;
            requestedStockByProduct[productId] = checked(requestedStockByProduct.GetValueOrDefault(productId) + line.ReturnQtyX1000);
            long lineTotalCents = checked(line.ReturnQtyX1000 * rrLine.UnitCostCents) / 1000L;
            totalReturnCents = checked(totalReturnCents + lineTotalCents);
        }

        foreach (var stockNeed in requestedStockByProduct)
        {
            (bool exists, bool trackStock, long currentStockX1000, _) =
                await ReadProductStockStateAsync(connection, transaction, stockNeed.Key, cancellationToken);
            if (!exists || (trackStock && currentStockX1000 < stockNeed.Value))
            {
                string itemName = rr.Lines.FirstOrDefault(l => l.ProductId == stockNeed.Key)?.ItemDescription ?? stockNeed.Key.ToString("D");
                return Result<SupplierReturnModel>.Fail(
                    Error.Conflict("Lpi.SupplierReturn.InsufficientStock",
                        $"Insufficient stock on hand ({currentStockX1000}) to return {stockNeed.Value} of '{itemName}'."));
            }
        }

        if (request.SupplierInvoiceId.HasValue)
        {
            Result invoiceValidation = await ValidateSupplierReturnTargetInvoiceAsync(
                connection,
                transaction,
                rr,
                rrLines,
                request.SupplierInvoiceId.Value,
                totalReturnCents,
                returnDate,
                cancellationToken);
            if (invoiceValidation.IsFailure)
            {
                return Result<SupplierReturnModel>.Fail(invoiceValidation.ErrorDetail);
            }
        }

        string sretNumber = await AllocateSequenceInTransactionAsync(connection, transaction, "SRET", cancellationToken);
        Guid sretId = Guid.NewGuid();

        await using (SqliteCommand ins = connection.CreateCommand())
        {
            ins.Transaction = transaction;
            ins.CommandText = """
                INSERT INTO supplier_returns (
                    id, return_number, supplier_id, receiving_report_id, purchase_order_id,
                    supplier_invoice_id, return_date, status, total_return_amount_cents,
                    reason, created_by_user_id, approved_by_user_id, posted_at, created_at
                )
                VALUES (
                    $id, $retNo, $supId, $rrId, $poId,
                    $invId, $retDate, 'Posted', $total,
                    $reason, $createdBy, $approvedBy, $now, $now
                );
                """;
            ins.Parameters.AddWithValue("$id", sretId.ToString("D"));
            ins.Parameters.AddWithValue("$retNo", sretNumber);
            ins.Parameters.AddWithValue("$supId", rr.SupplierId.ToString("D"));
            ins.Parameters.AddWithValue("$rrId", rr.Id.ToString("D"));
            ins.Parameters.AddWithValue("$poId", rr.PurchaseOrderId.ToString("D"));
            ins.Parameters.AddWithValue("$invId", request.SupplierInvoiceId?.ToString("D") ?? (object)DBNull.Value);
            ins.Parameters.AddWithValue("$retDate", returnDate.ToString("yyyy-MM-dd"));
            ins.Parameters.AddWithValue("$total", totalReturnCents);
            ins.Parameters.AddWithValue("$reason", request.Reason.Trim());
            ins.Parameters.AddWithValue("$createdBy", request.CreatedByUserId.ToString("D"));
            ins.Parameters.AddWithValue("$approvedBy", request.ApprovedByUserId.ToString("D"));
            ins.Parameters.AddWithValue("$now", now.ToString("O"));
            await ins.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var line in request.Lines)
        {
            ReceivingReportLineModel rrLine = rrLines[line.ReceivingReportLineId];
            Guid productId = rrLine.ProductId!.Value;
            long lineTotalCents = (line.ReturnQtyX1000 * rrLine.UnitCostCents) / 1000L;

            (bool exists, bool trackStock, long currentStockX1000, _) =
                await ReadProductStockStateAsync(connection, transaction, productId, cancellationToken);
            if (!exists || (trackStock && currentStockX1000 < line.ReturnQtyX1000))
            {
                return Result<SupplierReturnModel>.Fail(
                    Error.Conflict("Lpi.SupplierReturn.InsufficientStock", $"Insufficient stock on hand ({currentStockX1000}) to return {line.ReturnQtyX1000} of '{rrLine.ItemDescription}'."));
            }

            Guid? movementId = null;
            if (trackStock)
            {
                long nextStockX1000 = currentStockX1000 - line.ReturnQtyX1000;
                await using (SqliteCommand updProd = connection.CreateCommand())
                {
                    updProd.Transaction = transaction;
                    updProd.CommandText = """
                        UPDATE products
                        SET stock_on_hand_x1000 = $after,
                            updated_at = $now
                        WHERE id = $prodId;
                        """;
                    updProd.Parameters.AddWithValue("$after", nextStockX1000);
                    updProd.Parameters.AddWithValue("$now", now.ToString("O"));
                    updProd.Parameters.AddWithValue("$prodId", productId.ToString("D"));
                    await updProd.ExecuteNonQueryAsync(cancellationToken);
                }

                await UpsertStockBucketDeltaAsync(
                    connection,
                    transaction,
                    productId,
                    rr.LocationId,
                    StockBucketStatuses.Available,
                    -line.ReturnQtyX1000,
                    now,
                    cancellationToken);

                movementId = Guid.NewGuid();
                await using (SqliteCommand movCmd = connection.CreateCommand())
                {
                    movCmd.Transaction = transaction;
                    movCmd.CommandText = """
                        INSERT INTO stock_movements (
                            id, product_id, movement_type, qty_change_x1000,
                            cost_price_cents, reference_type, reference_id,
                            reason, user_id, created_at
                        )
                        VALUES (
                            $id, $prodId, $movType, $delta,
                            $unitCost, 'SUPPLIER_RETURN', $refId,
                            $reason, $actor, $now
                        );
                        """;
                    movCmd.Parameters.AddWithValue("$id", movementId.Value.ToString("D"));
                    movCmd.Parameters.AddWithValue("$prodId", productId.ToString("D"));
                    movCmd.Parameters.AddWithValue("$movType", (int)StockMovementType.AdjustmentRemove);
                    movCmd.Parameters.AddWithValue("$delta", -line.ReturnQtyX1000);
                    movCmd.Parameters.AddWithValue("$unitCost", rrLine.UnitCostCents);
                    movCmd.Parameters.AddWithValue("$refId", sretId.ToString("D"));
                    movCmd.Parameters.AddWithValue("$reason", $"Supplier Return {sretNumber} (RR {rr.RrNumber}): {line.Reason.Trim()}");
                    movCmd.Parameters.AddWithValue("$actor", request.ApprovedByUserId.ToString("D"));
                    movCmd.Parameters.AddWithValue("$now", now.ToString("O"));
                    await movCmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            await using (SqliteCommand updPoLine = connection.CreateCommand())
            {
                updPoLine.Transaction = transaction;
                updPoLine.CommandText = """
                    UPDATE purchase_order_lines
                    SET received_accepted_qty_x1000 = MAX(0, received_accepted_qty_x1000 - $retQty)
                    WHERE id = $poLineId;
                    """;
                updPoLine.Parameters.AddWithValue("$retQty", line.ReturnQtyX1000);
                updPoLine.Parameters.AddWithValue("$poLineId", rrLine.PurchaseOrderLineId.ToString("D"));
                await updPoLine.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (SqliteCommand insLine = connection.CreateCommand())
            {
                insLine.Transaction = transaction;
                insLine.CommandText = """
                    INSERT INTO supplier_return_lines (
                        id, supplier_return_id, receiving_report_line_id, product_id,
                        return_qty_x1000, unit_cost_cents, line_total_cents, reason, stock_movement_id
                    )
                    VALUES (
                        $id, $sretId, $rrLineId, $prodId,
                        $retQty, $unitCost, $lineTotal, $reason, $movId
                    );
                    """;
                insLine.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
                insLine.Parameters.AddWithValue("$sretId", sretId.ToString("D"));
                insLine.Parameters.AddWithValue("$rrLineId", rrLine.Id.ToString("D"));
                insLine.Parameters.AddWithValue("$prodId", productId.ToString("D"));
                insLine.Parameters.AddWithValue("$retQty", line.ReturnQtyX1000);
                insLine.Parameters.AddWithValue("$unitCost", rrLine.UnitCostCents);
                insLine.Parameters.AddWithValue("$lineTotal", lineTotalCents);
                insLine.Parameters.AddWithValue("$reason", line.Reason.Trim());
                insLine.Parameters.AddWithValue("$movId", movementId?.ToString("D") ?? (object)DBNull.Value);
                await insLine.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        if (request.SupplierInvoiceId.HasValue && totalReturnCents > 0)
        {
            await ApplySupplierReturnCreditToInvoiceAsync(
                connection,
                transaction,
                rr,
                rrLines,
                request.SupplierInvoiceId.Value,
                sretNumber,
                totalReturnCents,
                request.ApprovedByUserId,
                now,
                cancellationToken);
        }

        await WriteAuditAsync(
            connection,
            transaction,
            request.ApprovedByUserId,
            "SUPPLIER_RETURN_POSTED",
            "SupplierReturn",
            sretId.ToString("D"),
            new { sretNumber, rr.RrNumber, totalReturnCents, request.Reason },
            null,
            $"Posted supplier return {sretNumber}.",
            now,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        SupplierReturnModel? created = await LoadSupplierReturnAsync(connection, null, sretId, cancellationToken);
        return Result<SupplierReturnModel>.Success(created!);
    }

    public async Task<Result<IReadOnlyList<ProductStockCardModel>>> GetProductStockCardsAsync(
        string? search = null,
        Guid? locationId = null,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        List<Guid> productIds = new();
        await using (SqliteCommand listCmd = connection.CreateCommand())
        {
            if (!string.IsNullOrWhiteSpace(search))
            {
                listCmd.CommandText = """
                    SELECT id FROM products
                    WHERE is_active = 1
                      AND (name LIKE $s OR sku LIKE $s OR barcode LIKE $s)
                    ORDER BY name ASC;
                    """;
                listCmd.Parameters.AddWithValue("$s", $"%{search.Trim()}%");
            }
            else
            {
                listCmd.CommandText = "SELECT id FROM products WHERE is_active = 1 ORDER BY name ASC;";
            }

            await using SqliteDataReader reader = await listCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                productIds.Add(Guid.Parse(reader.GetString(0)));
            }
        }

        List<ProductStockCardModel> cards = new();
        foreach (Guid prodId in productIds)
        {
            Result<ProductStockCardModel> cardRes = await GetProductStockCardAsync(prodId, cancellationToken);
            if (cardRes.IsSuccess)
            {
                if (locationId.HasValue)
                {
                    bool matchesLocation = cardRes.Value.Buckets.Any(b => b.LocationId == locationId.Value);
                    if (matchesLocation) cards.Add(cardRes.Value);
                }
                else
                {
                    cards.Add(cardRes.Value);
                }
            }
        }

        return Result<IReadOnlyList<ProductStockCardModel>>.Success(cards);
    }

    public async Task<Result<ProductStockCardModel>> GetProductStockCardAsync(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using SqliteCommand prodCmd = connection.CreateCommand();
        prodCmd.CommandText = """
            SELECT p.id, p.name, p.sku, p.barcode, COALESCE(u.abbreviation, u.name, 'pc'),
                   p.stock_on_hand_x1000, p.reorder_level_x1000, COALESCE(p.cost_price_cents, 0), p.selling_price_cents
            FROM products p
            LEFT JOIN units u ON u.id = p.unit_id
            WHERE p.id = $id;
            """;
        prodCmd.Parameters.AddWithValue("$id", productId.ToString("D"));

        Guid id;
        string name, unitName;
        string? sku, barcode;
        long onHand, reorder, cost, selling;

        await using (SqliteDataReader reader = await prodCmd.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return Result<ProductStockCardModel>.Fail(Error.NotFound("Lpi.StockCard.ProductNotFound", "Product was not found."));
            }
            id = Guid.Parse(reader.GetString(0));
            name = reader.GetString(1);
            sku = reader.IsDBNull(2) ? null : reader.GetString(2);
            barcode = reader.IsDBNull(3) ? null : reader.GetString(3);
            unitName = reader.GetString(4);
            onHand = reader.GetInt64(5);
            reorder = reader.GetInt64(6);
            cost = reader.GetInt64(7);
            selling = reader.GetInt64(8);
        }

        var bucketsResult = await GetProductStockBucketsAsync(productId, cancellationToken);

        List<(Guid Id, DateTimeOffset CreatedAt, string TypeName, long DeltaX1000, long? UnitCostCents, string? RefType, string? RefId, string? Reason, string ActorName)> rawMovements = new();
        await using (SqliteCommand movCmd = connection.CreateCommand())
        {
            movCmd.CommandText = """
                SELECT m.id, m.created_at, m.movement_type, m.qty_change_x1000,
                       m.cost_price_cents, m.reference_type, m.reference_id, m.reason,
                       COALESCE(u.display_name, u.username, 'System')
                FROM stock_movements m
                LEFT JOIN users u ON u.id = m.user_id
                WHERE m.product_id = $prodId
                ORDER BY m.created_at ASC, m.id ASC;
                """;
            movCmd.Parameters.AddWithValue("$prodId", productId.ToString("D"));
            await using SqliteDataReader reader = await movCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                int movTypeInt = reader.GetInt32(2);
                string movTypeName = Enum.IsDefined(typeof(StockMovementType), movTypeInt)
                    ? ((StockMovementType)movTypeInt).ToString()
                    : $"Type-{movTypeInt}";

                rawMovements.Add((
                    Guid.Parse(reader.GetString(0)),
                    DateTimeOffset.Parse(reader.GetString(1)),
                    movTypeName,
                    reader.GetInt64(3),
                    reader.IsDBNull(4) ? null : reader.GetInt64(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.GetString(8)));
            }
        }

        // Compute running before/after balances across movements
        long totalDelta = rawMovements.Sum(x => x.DeltaX1000);
        long runningQty = onHand - totalDelta;
        List<ProductStockCardEntryModel> movements = new();
        foreach (var raw in rawMovements)
        {
            long before = runningQty;
            long after = before + raw.DeltaX1000;
            runningQty = after;
            movements.Add(new ProductStockCardEntryModel(
                raw.Id,
                raw.CreatedAt,
                raw.TypeName,
                raw.DeltaX1000,
                before,
                after,
                raw.UnitCostCents,
                raw.RefType,
                raw.RefId,
                raw.Reason,
                raw.Reason,
                raw.ActorName));
        }

        movements.Reverse();

        long valuationCents = (Math.Max(0, onHand) * cost) / 1000L;
        return Result<ProductStockCardModel>.Success(new ProductStockCardModel(
            id,
            name,
            sku,
            barcode,
            unitName,
            onHand,
            reorder,
            cost,
            selling,
            valuationCents,
            onHand <= reorder,
            bucketsResult.Value,
            movements));
    }

    public async Task<Result<IReadOnlyList<ProductStockBucketModel>>> GetProductStockBucketsAsync(
        Guid? productId = null,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT b.id, b.product_id, p.name, p.sku, b.location_id, loc.code, loc.name,
                   b.bucket_status, b.quantity_x1000, b.updated_at
            FROM product_stock_buckets b
            JOIN products p ON p.id = b.product_id
            JOIN inventory_locations loc ON loc.id = b.location_id
            WHERE ($prodId IS NULL OR b.product_id = $prodId)
            ORDER BY p.name COLLATE NOCASE, loc.code, b.bucket_status;
            """;
        cmd.Parameters.AddWithValue("$prodId", productId?.ToString("D") ?? (object)DBNull.Value);

        List<ProductStockBucketModel> list = new();
        await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new ProductStockBucketModel(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                Guid.Parse(reader.GetString(4)),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetInt64(8),
                DateTimeOffset.Parse(reader.GetString(9))));
        }

        return Result<IReadOnlyList<ProductStockBucketModel>>.Success(list);
    }

    private static async Task<(bool Exists, bool TrackStock, long StockOnHandX1000, long CostPriceCents)> ReadProductStockStateAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid productId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            SELECT track_stock, stock_on_hand_x1000, COALESCE(cost_price_cents, 0)
            FROM products
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", productId.ToString("D"));
        await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return (false, false, 0, 0);
        }

        return (true, reader.GetInt64(0) == 1, reader.GetInt64(1), reader.GetInt64(2));
    }

    private static async Task UpsertStockBucketDeltaAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid productId,
        Guid locationId,
        string bucketStatus,
        long deltaQtyX1000,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        long currentQtyX1000 = 0;
        bool exists = false;
        await using (SqliteCommand readCmd = connection.CreateCommand())
        {
            readCmd.Transaction = transaction;
            readCmd.CommandText = """
                SELECT quantity_x1000 FROM product_stock_buckets
                WHERE product_id = $prodId AND location_id = $locId AND bucket_status = $status
                LIMIT 1;
                """;
            readCmd.Parameters.AddWithValue("$prodId", productId.ToString("D"));
            readCmd.Parameters.AddWithValue("$locId", locationId.ToString("D"));
            readCmd.Parameters.AddWithValue("$status", bucketStatus);
            object? val = await readCmd.ExecuteScalarAsync(cancellationToken);
            if (val is not null && val is not DBNull)
            {
                exists = true;
                currentQtyX1000 = Convert.ToInt64(val);
            }
        }

        long newQtyX1000 = checked(currentQtyX1000 + deltaQtyX1000);
        if (newQtyX1000 < 0)
        {
            throw new InvalidOperationException($"Lpi.Inventory.InsufficientBucketStock: Insufficient stock in bucket '{bucketStatus}' at location '{locationId}'. In-bucket available: {currentQtyX1000 / 1000.0:F3}, requested deduction: {(-deltaQtyX1000) / 1000.0:F3}.");
        }

        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        if (exists)
        {
            cmd.CommandText = """
                UPDATE product_stock_buckets
                SET quantity_x1000 = $newQty,
                    updated_at = $now
                WHERE product_id = $prodId AND location_id = $locId AND bucket_status = $status;
                """;
        }
        else
        {
            cmd.CommandText = """
                INSERT INTO product_stock_buckets (id, product_id, location_id, bucket_status, quantity_x1000, updated_at)
                VALUES ($id, $prodId, $locId, $status, $newQty, $now);
                """;
            cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        }
        cmd.Parameters.AddWithValue("$prodId", productId.ToString("D"));
        cmd.Parameters.AddWithValue("$locId", locationId.ToString("D"));
        cmd.Parameters.AddWithValue("$status", bucketStatus);
        cmd.Parameters.AddWithValue("$newQty", newQtyX1000);
        cmd.Parameters.AddWithValue("$now", now.ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }


    public async Task<Result<ReceivingReportModel>> ApproveOverdeliveryAsync(
        ApproveOverdeliveryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.ReceivingReportId == Guid.Empty || request.ApproverUserId == Guid.Empty || string.IsNullOrWhiteSpace(request.ApprovalReason))
        {
            return Result<ReceivingReportModel>.Fail(
                Error.Validation("Lpi.Receiving.ApprovalReasonRequired", "Receiving report ID, approver user, and approval reason are required."));
        }

        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        SqlitePragma.Apply(connection);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        ReceivingReportModel? rr = await LoadReceivingReportAsync(connection, transaction, request.ReceivingReportId, cancellationToken);
        if (rr is null)
        {
            return Result<ReceivingReportModel>.Fail(Error.NotFound("Lpi.Receiving.NotFound", "Receiving report was not found."));
        }
        if (rr.Status is not ("Draft" or "PendingExceptionApproval"))
        {
            return Result<ReceivingReportModel>.Fail(
                Error.Conflict("Lpi.Receiving.InvalidStatus", $"Cannot approve overdelivery on receiving report in '{rr.Status}' status."));
        }
        if (!rr.HasOverdelivery)
        {
            return Result<ReceivingReportModel>.Fail(
                Error.Validation("Lpi.Receiving.NoOverdelivery", $"Receiving report '{rr.RrNumber}' does not contain overdelivery quantities."));
        }

        // Separation of duties enforcement:
        if (rr.ReceivedByUserId == request.ApproverUserId)
        {
            return Result<ReceivingReportModel>.Fail(
                Error.Validation("Lpi.Receiving.SelfApprovalForbidden", "Separation of duties required: receiving operator cannot approve their own overdelivery exception."));
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();

        var approvedLines = rr.Lines.Select(l => new OverdeliveryApprovedLineModel(
            l.PurchaseOrderLineId,
            l.ProductId,
            l.ItemDescription,
            l.AcceptedQtyX1000)).ToList();

        string linesJson = System.Text.Json.JsonSerializer.Serialize(approvedLines);

        await using (SqliteCommand appCmd = connection.CreateCommand())
        {
            appCmd.Transaction = transaction;
            appCmd.CommandText = """
                INSERT INTO lpi_overdelivery_approvals (
                    id, receiving_report_id, purchase_order_id, approver_user_id,
                    approval_reason, approved_lines_json, approved_at, is_revoked
                )
                VALUES (
                    $id, $rrId, $poId, $appBy,
                    $reason, $lines, $now, 0
                );
                """;
            appCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
            appCmd.Parameters.AddWithValue("$rrId", rr.Id.ToString("D"));
            appCmd.Parameters.AddWithValue("$poId", rr.PurchaseOrderId.ToString("D"));
            appCmd.Parameters.AddWithValue("$appBy", request.ApproverUserId.ToString("D"));
            appCmd.Parameters.AddWithValue("$reason", request.ApprovalReason.Trim());
            appCmd.Parameters.AddWithValue("$lines", linesJson);
            appCmd.Parameters.AddWithValue("$now", now.ToString("O"));
            await appCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (SqliteCommand updRr = connection.CreateCommand())
        {
            updRr.Transaction = transaction;
            updRr.CommandText = """
                UPDATE receiving_reports
                SET overdelivery_approved_by_user_id = $appBy,
                    overdelivery_approval_reason = $reason,
                    status = 'Draft',
                    updated_at = $now
                WHERE id = $id;
                """;
            updRr.Parameters.AddWithValue("$appBy", request.ApproverUserId.ToString("D"));
            updRr.Parameters.AddWithValue("$reason", request.ApprovalReason.Trim());
            updRr.Parameters.AddWithValue("$now", now.ToString("O"));
            updRr.Parameters.AddWithValue("$id", rr.Id.ToString("D"));
            await updRr.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteAuditAsync(
            connection,
            transaction,
            request.ApproverUserId,
            "LPI_RR_OVERDELIVERY_APPROVED",
            "ReceivingReport",
            rr.Id.ToString("D"),
            new { rr.RrNumber, rr.PoNumber, request.ApprovalReason, ApproverUserId = request.ApproverUserId },
            null,
            $"Approved overdelivery exception for receiving report {rr.RrNumber}: {request.ApprovalReason.Trim()}",
            now,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        ReceivingReportModel? updated = await LoadReceivingReportAsync(connection, null, rr.Id, cancellationToken);
        return Result<ReceivingReportModel>.Success(updated!);
    }

    private static string ComputeReceivingPayloadHash(
        Guid poId,
        Guid? locationId,
        string? deliveryReceiptNo,
        IReadOnlyList<CreateReceivingReportLineInput> lines)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"{poId:D}|{locationId?.ToString("D")}|{deliveryReceiptNo?.Trim()}|");
        foreach (var line in lines.OrderBy(l => l.PurchaseOrderLineId))
        {
            sb.Append($"{line.PurchaseOrderLineId:D}:{line.ActualDeliveredQtyX1000}:{line.AcceptedQtyX1000}:{line.RejectedQtyX1000};");
        }
        using var sha = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(sb.ToString())));
    }
    private static async Task<ReceivingReportModel?> LoadReceivingReportAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid receivingReportId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            SELECT rr.id, rr.rr_number, rr.purchase_order_id, po.po_number,
                   rr.supplier_id, s.name, rr.location_id, loc.name,
                   rr.delivery_receipt_no, rr.supplier_invoice_ref, rr.carrier_or_driver,
                   rr.received_date, rr.status, rr.has_overdelivery,
                   rr.overdelivery_approved_by_user_id, rr.overdelivery_approval_reason,
                   rr.received_by_user_id, COALESCE(u.display_name, u.username, 'Operator'),
                   rr.inspected_by_user_id, rr.posted_by_user_id, rr.posted_at,
                   rr.notes, rr.request_payload_hash, rr.created_at, rr.updated_at
            FROM receiving_reports rr
            JOIN purchase_orders po ON po.id = rr.purchase_order_id
            JOIN suppliers s ON s.id = rr.supplier_id
            JOIN inventory_locations loc ON loc.id = rr.location_id
            LEFT JOIN users u ON u.id = rr.received_by_user_id
            WHERE rr.id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", receivingReportId.ToString("D"));

        Guid id, poId, supId, locId, recById;
        string rrNo, poNo, supName, locName, drNo, status, recByName;
        string? invRef, carrier, overReason, notes, payloadHash;
        DateOnly recDate;
        bool hasOver;
        Guid? overAppBy, inspBy, postBy;
        DateTimeOffset? postAt;
        DateTimeOffset createdAt, updatedAt;

        await using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken)) return null;
            id = Guid.Parse(reader.GetString(0));
            rrNo = reader.GetString(1);
            poId = Guid.Parse(reader.GetString(2));
            poNo = reader.GetString(3);
            supId = Guid.Parse(reader.GetString(4));
            supName = reader.GetString(5);
            locId = Guid.Parse(reader.GetString(6));
            locName = reader.GetString(7);
            drNo = reader.GetString(8);
            invRef = reader.IsDBNull(9) ? null : reader.GetString(9);
            carrier = reader.IsDBNull(10) ? null : reader.GetString(10);
            recDate = DateOnly.Parse(reader.GetString(11));
            status = reader.GetString(12);
            hasOver = reader.GetInt64(13) == 1;
            overAppBy = reader.IsDBNull(14) ? null : Guid.Parse(reader.GetString(14));
            overReason = reader.IsDBNull(15) ? null : reader.GetString(15);
            recById = Guid.Parse(reader.GetString(16));
            recByName = reader.GetString(17);
            inspBy = reader.IsDBNull(18) ? null : Guid.Parse(reader.GetString(18));
            postBy = reader.IsDBNull(19) ? null : Guid.Parse(reader.GetString(19));
            postAt = reader.IsDBNull(20) ? null : DateTimeOffset.Parse(reader.GetString(20));
            notes = reader.IsDBNull(21) ? null : reader.GetString(21);
            payloadHash = reader.IsDBNull(22) ? null : reader.GetString(22);
            createdAt = DateTimeOffset.Parse(reader.GetString(23));
            updatedAt = DateTimeOffset.Parse(reader.GetString(24));
        }

        List<ReceivingReportLineModel> lines = new();
        await using (SqliteCommand lineCmd = connection.CreateCommand())
        {
            lineCmd.Transaction = transaction;
            lineCmd.CommandText = """
                SELECT id, receiving_report_id, purchase_order_line_id, product_id,
                       item_code, item_description, purchase_classification, unit_name,
                       ordered_qty_x1000, previously_received_qty_x1000, actual_delivered_qty_x1000,
                       accepted_qty_x1000, rejected_qty_x1000, shortage_qty_x1000, overdelivery_qty_x1000,
                       unit_cost_cents, accepted_line_total_cents, rejection_reason, rejection_disposition,
                       lot_or_batch_no, expiry_date, stock_movement_id
                FROM receiving_report_lines
                WHERE receiving_report_id = $id;
                """;
            lineCmd.Parameters.AddWithValue("$id", receivingReportId.ToString("D"));
            await using SqliteDataReader reader = await lineCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                lines.Add(new ReceivingReportLineModel(
                    Guid.Parse(reader.GetString(0)),
                    Guid.Parse(reader.GetString(1)),
                    Guid.Parse(reader.GetString(2)),
                    reader.IsDBNull(3) ? null : Guid.Parse(reader.GetString(3)),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetInt64(8),
                    reader.GetInt64(9),
                    reader.GetInt64(10),
                    reader.GetInt64(11),
                    reader.GetInt64(12),
                    reader.GetInt64(13),
                    reader.GetInt64(14),
                    reader.GetInt64(15),
                    reader.GetInt64(16),
                    reader.IsDBNull(17) ? null : reader.GetString(17),
                    reader.GetString(18),
                    reader.IsDBNull(19) ? null : reader.GetString(19),
                    reader.IsDBNull(20) ? null : DateOnly.Parse(reader.GetString(20)),
                    reader.IsDBNull(21) ? null : Guid.Parse(reader.GetString(21))));
            }
        }

        return new ReceivingReportModel(
            id, rrNo, poId, poNo, supId, supName, locId, locName, drNo, invRef, carrier,
            recDate, status, hasOver, overAppBy, overReason, recById, recByName, inspBy,
            postBy, postAt, notes, createdAt, updatedAt, lines, payloadHash);
    }

    private static async Task<SupplierReturnModel?> LoadSupplierReturnAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid supplierReturnId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            SELECT sr.id, sr.return_number, sr.supplier_id, s.name,
                   sr.receiving_report_id, rr.rr_number, sr.purchase_order_id, po.po_number,
                   sr.supplier_invoice_id, sr.return_date, sr.status, sr.total_return_amount_cents,
                   sr.reason, sr.created_by_user_id, sr.approved_by_user_id, sr.posted_at, sr.created_at
            FROM supplier_returns sr
            JOIN suppliers s ON s.id = sr.supplier_id
            JOIN receiving_reports rr ON rr.id = sr.receiving_report_id
            JOIN purchase_orders po ON po.id = sr.purchase_order_id
            WHERE sr.id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", supplierReturnId.ToString("D"));

        Guid id, supId, rrId, poId, createdBy, approvedBy;
        string retNo, supName, rrNo, poNo, status, reason;
        Guid? invId;
        DateOnly retDate;
        long totalCents;
        DateTimeOffset postedAt, createdAt;

        await using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken)) return null;
            id = Guid.Parse(reader.GetString(0));
            retNo = reader.GetString(1);
            supId = Guid.Parse(reader.GetString(2));
            supName = reader.GetString(3);
            rrId = Guid.Parse(reader.GetString(4));
            rrNo = reader.GetString(5);
            poId = Guid.Parse(reader.GetString(6));
            poNo = reader.GetString(7);
            invId = reader.IsDBNull(8) ? null : Guid.Parse(reader.GetString(8));
            retDate = DateOnly.Parse(reader.GetString(9));
            status = reader.GetString(10);
            totalCents = reader.GetInt64(11);
            reason = reader.GetString(12);
            createdBy = Guid.Parse(reader.GetString(13));
            approvedBy = Guid.Parse(reader.GetString(14));
            postedAt = DateTimeOffset.Parse(reader.GetString(15));
            createdAt = DateTimeOffset.Parse(reader.GetString(16));
        }

        List<SupplierReturnLineModel> lines = new();
        await using (SqliteCommand lineCmd = connection.CreateCommand())
        {
            lineCmd.Transaction = transaction;
            lineCmd.CommandText = """
                SELECT l.id, l.supplier_return_id, l.receiving_report_line_id, l.product_id,
                       p.name, l.return_qty_x1000, l.unit_cost_cents, l.line_total_cents,
                       l.reason, l.stock_movement_id
                FROM supplier_return_lines l
                JOIN products p ON p.id = l.product_id
                WHERE l.supplier_return_id = $id;
                """;
            lineCmd.Parameters.AddWithValue("$id", supplierReturnId.ToString("D"));
            await using SqliteDataReader reader = await lineCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                lines.Add(new SupplierReturnLineModel(
                    Guid.Parse(reader.GetString(0)),
                    Guid.Parse(reader.GetString(1)),
                    Guid.Parse(reader.GetString(2)),
                    Guid.Parse(reader.GetString(3)),
                    reader.GetString(4),
                    reader.GetInt64(5),
                    reader.GetInt64(6),
                    reader.GetInt64(7),
                    reader.GetString(8),
                    reader.IsDBNull(9) ? null : Guid.Parse(reader.GetString(9))));
            }
        }

        return new SupplierReturnModel(
            id, retNo, supId, supName, rrId, rrNo, poId, poNo, invId,
            retDate, status, totalCents, reason, createdBy, approvedBy, postedAt, createdAt, lines);
    }
}
