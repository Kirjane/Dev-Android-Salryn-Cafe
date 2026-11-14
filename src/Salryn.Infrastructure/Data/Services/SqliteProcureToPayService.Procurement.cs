using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models.ProcureToPay;
using Salryn.Infrastructure.Data.Seeding;

namespace Salryn.Infrastructure.Data.Services;

public sealed partial class SqliteProcureToPayService
{
    public async Task<Result<IReadOnlyList<PurchaseRequisitionModel>>> GetPurchaseRequisitionsAsync(
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        List<Guid> ids = new();
        await using (SqliteCommand cmd = connection.CreateCommand())
        {
            string filter = status?.Trim() ?? string.Empty;
            cmd.CommandText = """
                SELECT id FROM purchase_requisitions
                WHERE ($status = '' OR status = $status)
                ORDER BY created_at DESC;
                """;
            cmd.Parameters.AddWithValue("$status", filter);
            await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                ids.Add(Guid.Parse(reader.GetString(0)));
            }
        }

        List<PurchaseRequisitionModel> list = new();
        foreach (Guid id in ids)
        {
            PurchaseRequisitionModel? pr = await LoadPurchaseRequisitionAsync(connection, null, id, cancellationToken);
            if (pr is not null) list.Add(pr);
        }

        return Result<IReadOnlyList<PurchaseRequisitionModel>>.Success(list);
    }

    public async Task<Result<PurchaseRequisitionModel>> GetPurchaseRequisitionByIdAsync(
        Guid requisitionId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        PurchaseRequisitionModel? pr = await LoadPurchaseRequisitionAsync(connection, null, requisitionId, cancellationToken);
        return pr is not null
            ? Result<PurchaseRequisitionModel>.Success(pr)
            : Result<PurchaseRequisitionModel>.Fail(Error.NotFound("Lpi.Requisition.NotFound", "Purchase requisition was not found."));
    }

    public async Task<Result<PurchaseRequisitionModel>> CreatePurchaseRequisitionAsync(
        CreatePurchaseRequisitionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.DepartmentName) || string.IsNullOrWhiteSpace(request.Purpose))
        {
            return Result<PurchaseRequisitionModel>.Fail(
                Error.Validation("Lpi.Requisition.MissingFields", "Department name and business purpose are required."));
        }
        if (request.RequestedByUserId == Guid.Empty)
        {
            return Result<PurchaseRequisitionModel>.Fail(
                Error.Validation("Lpi.Requisition.RequesterRequired", "Requesting user is required."));
        }
        if (request.Lines is null || request.Lines.Count == 0)
        {
            return Result<PurchaseRequisitionModel>.Fail(
                Error.Validation("Lpi.Requisition.LinesRequired", "At least one requisition line item is required."));
        }
        foreach (var line in request.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.ItemDescription) || line.RequestedQtyX1000 <= 0 || line.EstimatedUnitCostCents < 0)
            {
                return Result<PurchaseRequisitionModel>.Fail(
                    Error.Validation("Lpi.Requisition.InvalidLine", "Each requisition line requires a description, positive quantity, and non-negative unit cost."));
            }
        }

        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        SqlitePragma.Apply(connection);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        string prNumber = await AllocateSequenceInTransactionAsync(connection, transaction, "PR", cancellationToken);
        Guid prId = Guid.NewGuid();
        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateOnly dateRequested = DateOnly.FromDateTime(now.UtcDateTime);
        string initialStatus = request.SubmitImmediately ? "Submitted" : "Draft";

        long totalEstimatedCents = 0;
        for (int i = 0; i < request.Lines.Count; i++)
        {
            var line = request.Lines[i];
            long lineTotal = (line.RequestedQtyX1000 * line.EstimatedUnitCostCents) / 1000L;
            totalEstimatedCents += lineTotal;
        }

        await using (SqliteCommand cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = """
                INSERT INTO purchase_requisitions (
                    id, pr_number, department_name, requested_by_user_id, date_requested, date_needed,
                    priority, purpose, status, estimated_total_cents, notes, created_at, updated_at
                )
                VALUES (
                    $id, $prNo, $dept, $reqBy, $dateReq, $dateNeeded,
                    $priority, $purpose, $status, $estTotal, $notes, $now, $now
                );
                """;
            cmd.Parameters.AddWithValue("$id", prId.ToString("D"));
            cmd.Parameters.AddWithValue("$prNo", prNumber);
            cmd.Parameters.AddWithValue("$dept", request.DepartmentName.Trim());
            cmd.Parameters.AddWithValue("$reqBy", request.RequestedByUserId.ToString("D"));
            cmd.Parameters.AddWithValue("$dateReq", dateRequested.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("$dateNeeded", request.DateNeeded.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("$priority", string.IsNullOrWhiteSpace(request.Priority) ? "Normal" : request.Priority.Trim());
            cmd.Parameters.AddWithValue("$purpose", request.Purpose.Trim());
            cmd.Parameters.AddWithValue("$status", initialStatus);
            cmd.Parameters.AddWithValue("$estTotal", totalEstimatedCents);
            cmd.Parameters.AddWithValue("$notes", (object?)Clean(request.Notes) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$now", now.ToString("O"));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        for (int i = 0; i < request.Lines.Count; i++)
        {
            var line = request.Lines[i];
            long lineTotal = (line.RequestedQtyX1000 * line.EstimatedUnitCostCents) / 1000L;
            string itemCode = Clean(line.ItemCode) ?? $"ITEM-{i + 1:D2}";
            string categoryName = Clean(line.CategoryName) ?? "General";
            string classification = PurchaseClassifications.Normalize(line.PurchaseClassification);
            string unitName = Clean(line.UnitName) ?? "pc";

            await using SqliteCommand lineCmd = connection.CreateCommand();
            lineCmd.Transaction = transaction;
            lineCmd.CommandText = """
                INSERT INTO purchase_requisition_lines (
                    id, requisition_id, line_number, product_id, item_code, item_description,
                    category_name, purchase_classification, unit_name, requested_qty_x1000,
                    approved_qty_x1000, converted_po_qty_x1000, estimated_unit_cost_cents,
                    estimated_line_total_cents, preferred_supplier_id, specifications, notes
                )
                VALUES (
                    $id, $reqId, $lineNo, $prodId, $code, $desc,
                    $cat, $class, $unit, $reqQty,
                    0, 0, $unitCost,
                    $lineTotal, $prefSup, $specs, $notes
                );
                """;
            lineCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
            lineCmd.Parameters.AddWithValue("$reqId", prId.ToString("D"));
            lineCmd.Parameters.AddWithValue("$lineNo", i + 1);
            lineCmd.Parameters.AddWithValue("$prodId", line.ProductId.HasValue ? line.ProductId.Value.ToString("D") : DBNull.Value);
            lineCmd.Parameters.AddWithValue("$code", itemCode);
            lineCmd.Parameters.AddWithValue("$desc", line.ItemDescription.Trim());
            lineCmd.Parameters.AddWithValue("$cat", categoryName);
            lineCmd.Parameters.AddWithValue("$class", classification);
            lineCmd.Parameters.AddWithValue("$unit", unitName);
            lineCmd.Parameters.AddWithValue("$reqQty", line.RequestedQtyX1000);
            lineCmd.Parameters.AddWithValue("$unitCost", line.EstimatedUnitCostCents);
            lineCmd.Parameters.AddWithValue("$lineTotal", lineTotal);
            lineCmd.Parameters.AddWithValue("$prefSup", line.PreferredSupplierId.HasValue ? line.PreferredSupplierId.Value.ToString("D") : DBNull.Value);
            lineCmd.Parameters.AddWithValue("$specs", (object?)Clean(line.Specifications) ?? DBNull.Value);
            lineCmd.Parameters.AddWithValue("$notes", (object?)Clean(line.Notes) ?? DBNull.Value);
            await lineCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await RecordRequisitionActionAsync(
            connection,
            transaction,
            prId,
            request.SubmitImmediately ? "CreatedAndSubmitted" : "CreatedDraft",
            request.RequestedByUserId,
            "None",
            initialStatus,
            request.Purpose.Trim(),
            now,
            cancellationToken);

        await WriteAuditAsync(
            connection,
            transaction,
            request.RequestedByUserId,
            "PR_CREATED",
            "PurchaseRequisition",
            prId.ToString("D"),
            new { prNumber, status = initialStatus, totalEstimatedCents, lineCount = request.Lines.Count },
            null,
            $"Created purchase requisition {prNumber}.",
            now,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        PurchaseRequisitionModel? created = await LoadPurchaseRequisitionAsync(connection, null, prId, cancellationToken);
        return Result<PurchaseRequisitionModel>.Success(created!);
    }

    public async Task<Result<PurchaseRequisitionModel>> SubmitPurchaseRequisitionAsync(
        Guid requisitionId,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        SqlitePragma.Apply(connection);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        PurchaseRequisitionModel? existing = await LoadPurchaseRequisitionAsync(connection, transaction, requisitionId, cancellationToken);
        if (existing is null)
        {
            return Result<PurchaseRequisitionModel>.Fail(Error.NotFound("Lpi.Requisition.NotFound", "Purchase requisition was not found."));
        }
        if (existing.RequestedByUserId != actorUserId)
        {
            return Result<PurchaseRequisitionModel>.Fail(
                Error.Forbidden("Lpi.Requisition.SubmitOwnerRequired", "Only the requisition requester may submit this draft."));
        }
        if (existing.Status is not ("Draft" or "RevisionRequested"))
        {
            return Result<PurchaseRequisitionModel>.Fail(
                Error.Conflict("Lpi.Requisition.InvalidState", $"Cannot submit requisition in '{existing.Status}' status."));
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        await using (SqliteCommand cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = """
                UPDATE purchase_requisitions
                SET status = 'Submitted',
                    updated_at = $now
                WHERE id = $id
                  AND requested_by_user_id = $actorUserId
                  AND status IN ('Draft', 'RevisionRequested');
                """;
            cmd.Parameters.AddWithValue("$id", requisitionId.ToString("D"));
            cmd.Parameters.AddWithValue("$actorUserId", actorUserId.ToString("D"));
            cmd.Parameters.AddWithValue("$now", now.ToString("O"));
            int affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
            if (affected != 1)
            {
                return Result<PurchaseRequisitionModel>.Fail(
                    Error.Conflict("Lpi.Requisition.SubmitConflict", "The requisition changed before it could be submitted. Reload and try again."));
            }
        }

        await RecordRequisitionActionAsync(
            connection,
            transaction,
            requisitionId,
            "Submitted",
            actorUserId,
            existing.Status,
            "Submitted",
            "Submitted for approval.",
            now,
            cancellationToken);

        await WriteAuditAsync(
            connection,
            transaction,
            actorUserId,
            "PR_SUBMITTED",
            "PurchaseRequisition",
            requisitionId.ToString("D"),
            new { prNumber = existing.PrNumber, status = "Submitted" },
            new { status = existing.Status },
            "Submitted purchase requisition.",
            now,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        PurchaseRequisitionModel? updated = await LoadPurchaseRequisitionAsync(connection, null, requisitionId, cancellationToken);
        return Result<PurchaseRequisitionModel>.Success(updated!);
    }

    public async Task<Result<PurchaseRequisitionModel>> ReviewPurchaseRequisitionAsync(
        ReviewPurchaseRequisitionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.RequisitionId == Guid.Empty || request.ReviewerUserId == Guid.Empty)
        {
            return Result<PurchaseRequisitionModel>.Fail(
                Error.Validation("Lpi.Requisition.InvalidReview", "Requisition ID and reviewer user ID are required."));
        }

        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        SqlitePragma.Apply(connection);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        PurchaseRequisitionModel? existing = await LoadPurchaseRequisitionAsync(connection, transaction, request.RequisitionId, cancellationToken);
        if (existing is null)
        {
            return Result<PurchaseRequisitionModel>.Fail(Error.NotFound("Lpi.Requisition.NotFound", "Purchase requisition was not found."));
        }

        if (existing.Status is not ("Submitted" or "UnderReview" or "Draft" or "RevisionRequested"))
        {
            return Result<PurchaseRequisitionModel>.Fail(
                Error.Conflict("Lpi.Requisition.InvalidState", $"Requisition '{existing.PrNumber}' in '{existing.Status}' status cannot be reviewed."));
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        string decision = request.Decision?.Trim() ?? string.Empty;

        // Separation of duties: requester cannot approve their own PR, unless authorized executive (Admin / Store Manager)
        if (string.Equals(decision, "Approve", StringComparison.OrdinalIgnoreCase) &&
            existing.RequestedByUserId == request.ReviewerUserId)
        {
            bool isPrivilegedSelfApprover = false;
            await using (SqliteCommand roleCmd = connection.CreateCommand())
            {
                roleCmd.Transaction = transaction;
                roleCmd.CommandText = """
                    SELECT r.name, u.role_id
                    FROM users u
                    JOIN roles r ON r.id = u.role_id
                    WHERE u.id = $userId AND u.status = 1;
                    """;
                roleCmd.Parameters.AddWithValue("$userId", request.ReviewerUserId.ToString("D"));
                await using SqliteDataReader roleReader = await roleCmd.ExecuteReaderAsync(cancellationToken);
                if (await roleReader.ReadAsync(cancellationToken))
                {
                    string roleName = roleReader.GetString(0);
                    string userRoleId = roleReader.GetString(1);
                    if (string.Equals(roleName, "Admin", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(roleName, "Manager", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(userRoleId, DefaultSeedData.AdminRoleId, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(userRoleId, DefaultSeedData.ManagerRoleId, StringComparison.OrdinalIgnoreCase))
                    {
                        isPrivilegedSelfApprover = true;
                    }
                }
            }

            bool isExplicitExecutiveOverride = request.Reason != null &&
                (request.Reason.Contains("Executive", StringComparison.OrdinalIgnoreCase) ||
                 request.Reason.Contains("Self-Approved", StringComparison.OrdinalIgnoreCase) ||
                 request.Reason.Contains("Executive self-approval", StringComparison.OrdinalIgnoreCase));

            if (!isPrivilegedSelfApprover || !isExplicitExecutiveOverride)
            {
                await WriteAuditAsync(
                    connection,
                    transaction,
                    request.ReviewerUserId,
                    "PR_SELF_APPROVAL_DENIED",
                    "PurchaseRequisition",
                    existing.Id.ToString("D"),
                    new { prNumber = existing.PrNumber, attemptedBy = request.ReviewerUserId },
                    null,
                    "Blocked self-approval of purchase requisition by unauthorized requester.",
                    now,
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return Result<PurchaseRequisitionModel>.Fail(
                    Error.Forbidden("Lpi.Requisition.SelfApprovalDenied", "Separation of duties violation: standard requesters cannot approve their own purchase requisition."));
            }

            // Record explicit executive self-approval audit log
            await WriteAuditAsync(
                connection,
                transaction,
                request.ReviewerUserId,
                "PR_SELF_APPROVED_BY_EXECUTIVE",
                "PurchaseRequisition",
                existing.Id.ToString("D"),
                new { prNumber = existing.PrNumber, approvedBy = request.ReviewerUserId, policy = "Admin/Manager Self-Confirmation" },
                null,
                "Authorized executive self-approval of purchase requisition recorded with durable audit trail.",
                now,
                cancellationToken);
        }

        string newStatus;
        string actionType;
        if (string.Equals(decision, "Approve", StringComparison.OrdinalIgnoreCase))
        {
            newStatus = "Approved";
            actionType = "Approved";

            Dictionary<Guid, long> overrides = request.LineDecisions?.ToDictionary(x => x.RequisitionLineId, x => x.ApprovedQtyX1000)
                ?? new Dictionary<Guid, long>();

            foreach (var line in existing.Lines)
            {
                long approvedQty = overrides.TryGetValue(line.Id, out long customQty) ? customQty : line.RequestedQtyX1000;
                if (approvedQty < 0 || approvedQty > line.RequestedQtyX1000)
                {
                    return Result<PurchaseRequisitionModel>.Fail(
                        Error.Validation("Lpi.Requisition.InvalidApprovedQty", $"Approved quantity for '{line.ItemDescription}' must be between 0 and requested quantity."));
                }

                await using SqliteCommand lineUpd = connection.CreateCommand();
                lineUpd.Transaction = transaction;
                lineUpd.CommandText = """
                    UPDATE purchase_requisition_lines
                    SET approved_qty_x1000 = $appQty
                    WHERE id = $lineId;
                    """;
                lineUpd.Parameters.AddWithValue("$appQty", approvedQty);
                lineUpd.Parameters.AddWithValue("$lineId", line.Id.ToString("D"));
                await lineUpd.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        else if (string.Equals(decision, "RequestRevision", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.Reason))
            {
                return Result<PurchaseRequisitionModel>.Fail(
                    Error.Validation("Lpi.Requisition.ReasonRequired", "A reason is required when returning a requisition for revision."));
            }
            newStatus = "RevisionRequested";
            actionType = "RevisionRequested";
        }
        else if (string.Equals(decision, "Reject", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.Reason))
            {
                return Result<PurchaseRequisitionModel>.Fail(
                    Error.Validation("Lpi.Requisition.ReasonRequired", "A reason is required when rejecting a purchase requisition."));
            }
            newStatus = "Rejected";
            actionType = "Rejected";
        }
        else
        {
            return Result<PurchaseRequisitionModel>.Fail(
                Error.Validation("Lpi.Requisition.InvalidDecision", "Decision must be 'Approve', 'RequestRevision', or 'Reject'."));
        }

        await using (SqliteCommand upd = connection.CreateCommand())
        {
            upd.Transaction = transaction;
            upd.CommandText = """
                UPDATE purchase_requisitions
                SET status = $status,
                    approved_by_user_id = CASE WHEN $isApproved = 1 THEN $reviewer ELSE approved_by_user_id END,
                    approved_at = CASE WHEN $isApproved = 1 THEN $now ELSE approved_at END,
                    rejection_or_revision_reason = $reason,
                    updated_at = $now
                WHERE id = $id;
                """;
            upd.Parameters.AddWithValue("$status", newStatus);
            upd.Parameters.AddWithValue("$isApproved", newStatus == "Approved" ? 1 : 0);
            upd.Parameters.AddWithValue("$reviewer", request.ReviewerUserId.ToString("D"));
            upd.Parameters.AddWithValue("$reason", (object?)Clean(request.Reason) ?? DBNull.Value);
            upd.Parameters.AddWithValue("$now", now.ToString("O"));
            upd.Parameters.AddWithValue("$id", existing.Id.ToString("D"));
            await upd.ExecuteNonQueryAsync(cancellationToken);
        }

        await RecordRequisitionActionAsync(
            connection,
            transaction,
            existing.Id,
            actionType,
            request.ReviewerUserId,
            existing.Status,
            newStatus,
            request.Reason,
            now,
            cancellationToken);

        await WriteAuditAsync(
            connection,
            transaction,
            request.ReviewerUserId,
            $"PR_{actionType.ToUpperInvariant()}",
            "PurchaseRequisition",
            existing.Id.ToString("D"),
            new { prNumber = existing.PrNumber, status = newStatus, reason = request.Reason },
            new { status = existing.Status },
            request.Reason ?? $"Purchase requisition {actionType}.",
            now,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        PurchaseRequisitionModel? updated = await LoadPurchaseRequisitionAsync(connection, null, existing.Id, cancellationToken);
        return Result<PurchaseRequisitionModel>.Success(updated!);
    }

    public async Task<Result<IReadOnlyList<SupplierQuotationModel>>> GetSupplierQuotationsAsync(
        Guid? requisitionId = null,
        Guid? productId = null,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT q.id, q.requisition_id, q.requisition_line_id, q.supplier_id, s.name,
                   q.product_id, q.item_description, q.quoted_unit_cost_cents, q.minimum_order_qty_x1000,
                   q.lead_time_days, q.payment_terms_days, q.validity_date, q.is_selected,
                   q.selection_reason, q.recorded_by_user_id, q.created_at
            FROM supplier_quotations q
            JOIN suppliers s ON s.id = q.supplier_id
            WHERE ($reqId IS NULL OR q.requisition_id = $reqId)
              AND ($prodId IS NULL OR q.product_id = $prodId)
            ORDER BY q.is_selected DESC, q.quoted_unit_cost_cents ASC;
            """;
        cmd.Parameters.AddWithValue("$reqId", requisitionId?.ToString("D") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$prodId", productId?.ToString("D") ?? (object)DBNull.Value);

        List<SupplierQuotationModel> quotes = new();
        await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            quotes.Add(new SupplierQuotationModel(
                Guid.Parse(reader.GetString(0)),
                reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
                reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2)),
                Guid.Parse(reader.GetString(3)),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : Guid.Parse(reader.GetString(5)),
                reader.GetString(6),
                reader.GetInt64(7),
                reader.GetInt64(8),
                reader.GetInt32(9),
                reader.GetInt32(10),
                reader.IsDBNull(11) ? null : DateOnly.Parse(reader.GetString(11)),
                reader.GetInt64(12) == 1,
                reader.IsDBNull(13) ? null : reader.GetString(13),
                Guid.Parse(reader.GetString(14)),
                DateTimeOffset.Parse(reader.GetString(15))));
        }

        return Result<IReadOnlyList<SupplierQuotationModel>>.Success(quotes);
    }

    public async Task<Result<SupplierQuotationModel>> RecordSupplierQuotationAsync(
        RecordSupplierQuotationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.SupplierId == Guid.Empty || string.IsNullOrWhiteSpace(request.ItemDescription) || request.QuotedUnitCostCents < 0)
        {
            return Result<SupplierQuotationModel>.Fail(
                Error.Validation("Lpi.Quotation.Invalid", "Supplier, item description, and non-negative unit cost are required."));
        }

        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        SqlitePragma.Apply(connection);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        LpiSupplier? supplier = await FindSupplierByIdAsync(connection, transaction, request.SupplierId, cancellationToken);
        if (supplier is null || !supplier.IsActive)
        {
            return Result<SupplierQuotationModel>.Fail(Error.Validation("Lpi.Quotation.SupplierInactive", "An active supplier is required."));
        }

        Guid id = Guid.NewGuid();
        DateTimeOffset now = _timeProvider.GetUtcNow();

        await using (SqliteCommand cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = """
                INSERT INTO supplier_quotations (
                    id, requisition_id, requisition_line_id, supplier_id, product_id, item_description,
                    quoted_unit_cost_cents, minimum_order_qty_x1000, lead_time_days, payment_terms_days,
                    validity_date, is_selected, selection_reason, recorded_by_user_id, created_at
                )
                VALUES (
                    $id, $reqId, $reqLineId, $supId, $prodId, $desc,
                    $cost, $minQty, $lead, $terms,
                    $valDate, $sel, $selReason, $user, $now
                );
                """;
            cmd.Parameters.AddWithValue("$id", id.ToString("D"));
            cmd.Parameters.AddWithValue("$reqId", request.RequisitionId?.ToString("D") ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$reqLineId", request.RequisitionLineId?.ToString("D") ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$supId", request.SupplierId.ToString("D"));
            cmd.Parameters.AddWithValue("$prodId", request.ProductId?.ToString("D") ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$desc", request.ItemDescription.Trim());
            cmd.Parameters.AddWithValue("$cost", request.QuotedUnitCostCents);
            cmd.Parameters.AddWithValue("$minQty", Math.Max(1000L, request.MinimumOrderQtyX1000));
            cmd.Parameters.AddWithValue("$lead", Math.Max(0, request.LeadTimeDays));
            cmd.Parameters.AddWithValue("$terms", Math.Max(0, request.PaymentTermsDays));
            cmd.Parameters.AddWithValue("$valDate", request.ValidityDate?.ToString("yyyy-MM-dd") ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$sel", request.IsSelected ? 1 : 0);
            cmd.Parameters.AddWithValue("$selReason", (object?)Clean(request.SelectionReason) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$user", request.ActorUserId.ToString("D"));
            cmd.Parameters.AddWithValue("$now", now.ToString("O"));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await WriteAuditAsync(
            connection,
            transaction,
            request.ActorUserId,
            "SUPPLIER_QUOTATION_RECORDED",
            "SupplierQuotation",
            id.ToString("D"),
            new { supplier.Name, request.ItemDescription, request.QuotedUnitCostCents, request.IsSelected },
            null,
            request.SelectionReason ?? "Recorded supplier quotation.",
            now,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return Result<SupplierQuotationModel>.Success(new SupplierQuotationModel(
            id,
            request.RequisitionId,
            request.RequisitionLineId,
            request.SupplierId,
            supplier.Name,
            request.ProductId,
            request.ItemDescription.Trim(),
            request.QuotedUnitCostCents,
            Math.Max(1000L, request.MinimumOrderQtyX1000),
            Math.Max(0, request.LeadTimeDays),
            Math.Max(0, request.PaymentTermsDays),
            request.ValidityDate,
            request.IsSelected,
            Clean(request.SelectionReason),
            request.ActorUserId,
            now));
    }

    public async Task<Result<IReadOnlyList<PurchaseOrderModel>>> GetPurchaseOrdersAsync(
        string? status = null,
        Guid? supplierId = null,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        List<Guid> ids = new();
        await using (SqliteCommand cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id FROM purchase_orders
                WHERE ($status = '' OR status = $status)
                  AND ($supId IS NULL OR supplier_id = $supId)
                ORDER BY created_at DESC;
                """;
            cmd.Parameters.AddWithValue("$status", status?.Trim() ?? string.Empty);
            cmd.Parameters.AddWithValue("$supId", supplierId?.ToString("D") ?? (object)DBNull.Value);
            await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                ids.Add(Guid.Parse(reader.GetString(0)));
            }
        }

        List<PurchaseOrderModel> list = new();
        foreach (Guid id in ids)
        {
            PurchaseOrderModel? po = await LoadPurchaseOrderAsync(connection, null, id, cancellationToken);
            if (po is not null) list.Add(po);
        }

        return Result<IReadOnlyList<PurchaseOrderModel>>.Success(list);
    }

    public async Task<Result<PurchaseOrderModel>> GetPurchaseOrderByIdAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        PurchaseOrderModel? po = await LoadPurchaseOrderAsync(connection, null, purchaseOrderId, cancellationToken);
        return po is not null
            ? Result<PurchaseOrderModel>.Success(po)
            : Result<PurchaseOrderModel>.Fail(Error.NotFound("Lpi.PurchaseOrder.NotFound", "Purchase order was not found."));
    }

    public async Task<Result<PurchaseOrderModel>> ConvertRequisitionToPurchaseOrderAsync(
        Guid requisitionId,
        Guid supplierId,
        IReadOnlyList<Guid>? requisitionLineIds = null,
        Guid? deliveryLocationId = null,
        DateOnly? expectedDeliveryDate = null,
        Guid actorUserId = default,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        SqlitePragma.Apply(connection);

        PurchaseRequisitionModel? pr = await LoadPurchaseRequisitionAsync(connection, null, requisitionId, cancellationToken);
        if (pr is null)
        {
            return Result<PurchaseOrderModel>.Fail(Error.NotFound("Lpi.Requisition.NotFound", "Requisition not found."));
        }

        if (pr.Status is not ("Approved" or "PartiallyConverted"))
        {
            return Result<PurchaseOrderModel>.Fail(Error.Conflict("Lpi.Requisition.NotApproved", $"Requisition '{pr.PrNumber}' is not in Approved status (current: {pr.Status})."));
        }

        HashSet<Guid>? allowedLineIds = requisitionLineIds is not null && requisitionLineIds.Count > 0
            ? new HashSet<Guid>(requisitionLineIds)
            : null;

        Dictionary<Guid, (long UnitCost, int Terms, int LeadTime)> winningQuotesByLine = new();
        int minPaymentTermsDays = int.MaxValue;
        bool hasWinningPaymentTerms = false;
        int maxLeadTimeDays = 0;
        bool hasWinningLeadTime = false;
        await using (SqliteCommand quoteCmd = connection.CreateCommand())
        {
            quoteCmd.CommandText = """
                SELECT q.requisition_line_id, q.quoted_unit_cost_cents, q.payment_terms_days, q.lead_time_days
                FROM supplier_quotations q
                WHERE q.requisition_id = $reqId
                  AND q.supplier_id = $supId
                  AND q.is_selected = 1
                ORDER BY q.requisition_line_id ASC;
                """;
            quoteCmd.Parameters.AddWithValue("$reqId", requisitionId.ToString("D"));
            quoteCmd.Parameters.AddWithValue("$supId", supplierId.ToString("D"));
            await using SqliteDataReader qReader = await quoteCmd.ExecuteReaderAsync(cancellationToken);
            while (await qReader.ReadAsync(cancellationToken))
            {
                if (!qReader.IsDBNull(0))
                {
                    Guid lineId = Guid.Parse(qReader.GetString(0));
                    long unitCost = qReader.GetInt64(1);
                    int terms = qReader.GetInt32(2);
                    int leadTime = qReader.GetInt32(3);
                    winningQuotesByLine[lineId] = (unitCost, terms, leadTime);
                    if (allowedLineIds is null || allowedLineIds.Contains(lineId))
                    {
                        if (terms > 0)
                        {
                            // Invariant: Consolidated PO adopts the most conservative (minimum) payment terms
                            // across converted lines to guarantee timely payment compliance without defaulting on any line.
                            minPaymentTermsDays = Math.Min(minPaymentTermsDays, terms);
                            hasWinningPaymentTerms = true;
                        }
                        if (leadTime > 0)
                        {
                            // Invariant: Consolidated PO adopts the maximum delivery lead time across converted lines
                            // ensuring delivery is not expected before the slowest item can arrive.
                            maxLeadTimeDays = Math.Max(maxLeadTimeDays, leadTime);
                            hasWinningLeadTime = true;
                        }
                    }
                }
            }
        }
        int? winningPaymentTermsDays = hasWinningPaymentTerms ? minPaymentTermsDays : null;

        List<CreatePurchaseOrderLineInput> poLines = new();
        foreach (var line in pr.Lines)
        {
            if (allowedLineIds is not null && !allowedLineIds.Contains(line.Id))
                continue;

            long remainingToConvertX1000 = Math.Max(0L, line.ApprovedQtyX1000 - line.ConvertedPoQtyX1000);
            if (remainingToConvertX1000 <= 0)
                continue;

            long unitCost = winningQuotesByLine.TryGetValue(line.Id, out var winLine)
                ? winLine.UnitCost
                : line.EstimatedUnitCostCents;

            poLines.Add(new CreatePurchaseOrderLineInput(
                RequisitionLineId: line.Id,
                ProductId: line.ProductId,
                ItemCode: string.IsNullOrWhiteSpace(line.ItemCode) ? $"PR-{line.LineNumber:D2}" : line.ItemCode,
                ItemDescription: line.ItemDescription,
                PurchaseClassification: line.PurchaseClassification,
                UnitName: line.UnitName,
                OrderedQtyX1000: remainingToConvertX1000,
                UnitCostCents: unitCost,
                DiscountCents: 0,
                TaxRateBp: 0,
                Notes: line.Notes));
        }

        if (poLines.Count == 0)
        {
            return Result<PurchaseOrderModel>.Fail(Error.Validation("Lpi.Requisition.NoLinesToConvert", "No remaining approved requisition lines available to convert."));
        }

        DateOnly deliveryDate = expectedDeliveryDate
            ?? (hasWinningLeadTime ? DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime).AddDays(maxLeadTimeDays) : pr.DateNeeded);
        var poRequest = new CreatePurchaseOrderRequest(
            SupplierId: supplierId,
            SourceRequisitionId: requisitionId,
            ExpectedDeliveryDate: deliveryDate,
            DeliveryLocationId: deliveryLocationId,
            PaymentTermsDays: winningPaymentTermsDays,
            ShippingCents: 0,
            TermsAndConditions: null,
            Notes: $"Converted from PR {pr.PrNumber}",
            SubmitAndApproveImmediately: true,
            Lines: poLines,
            ActorUserId: actorUserId == Guid.Empty ? pr.RequestedByUserId : actorUserId);

        return await CreatePurchaseOrderAsync(poRequest, cancellationToken);
    }

    private static async Task<SupplierQuotationModel?> LoadQuotationAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid quotationId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand cmd = connection.CreateCommand();
        if (transaction is not null) cmd.Transaction = transaction;
        cmd.CommandText = """
            SELECT q.id, q.requisition_id, q.requisition_line_id, q.supplier_id, s.name,
                   q.product_id, q.item_description, q.quoted_unit_cost_cents, q.minimum_order_qty_x1000,
                   q.lead_time_days, q.payment_terms_days, q.validity_date, q.is_selected,
                   q.selection_reason, q.recorded_by_user_id, q.created_at
            FROM supplier_quotations q
            JOIN suppliers s ON s.id = q.supplier_id
            WHERE q.id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", quotationId.ToString("D"));
        await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new SupplierQuotationModel(
                Guid.Parse(reader.GetString(0)),
                reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
                reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2)),
                Guid.Parse(reader.GetString(3)),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : Guid.Parse(reader.GetString(5)),
                reader.GetString(6),
                reader.GetInt64(7),
                reader.GetInt64(8),
                reader.GetInt32(9),
                reader.GetInt32(10),
                reader.IsDBNull(11) ? null : DateOnly.Parse(reader.GetString(11)),
                reader.GetInt64(12) == 1,
                reader.IsDBNull(13) ? null : reader.GetString(13),
                Guid.Parse(reader.GetString(14)),
                DateTimeOffset.Parse(reader.GetString(15)));
        }
        return null;
    }

    public async Task<Result<SupplierQuotationModel>> SelectWinningQuotationAsync(
        Guid quotationId,
        string? reason = null,
        Guid actorUserId = default,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        SqlitePragma.Apply(connection);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        SupplierQuotationModel? quote = await LoadQuotationAsync(connection, transaction, quotationId, cancellationToken);
        if (quote is null)
        {
            return Result<SupplierQuotationModel>.Fail(Error.NotFound("Lpi.Quotation.NotFound", "Quotation not found."));
        }

        if (quote.RequisitionLineId.HasValue)
        {
            await using SqliteCommand unselectCmd = connection.CreateCommand();
            unselectCmd.Transaction = transaction;
            unselectCmd.CommandText = """
                UPDATE supplier_quotations
                SET is_selected = 0, selection_reason = NULL
                WHERE requisition_line_id = $reqLineId;
                """;
            unselectCmd.Parameters.AddWithValue("$reqLineId", quote.RequisitionLineId.Value.ToString("D"));
            await unselectCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using SqliteCommand selectCmd = connection.CreateCommand();
        selectCmd.Transaction = transaction;
        selectCmd.CommandText = """
            UPDATE supplier_quotations
            SET is_selected = 1, selection_reason = $reason
            WHERE id = $id;
            """;
        selectCmd.Parameters.AddWithValue("$id", quotationId.ToString("D"));
        selectCmd.Parameters.AddWithValue("$reason", (object?)Clean(reason) ?? "Selected winning quotation");
        await selectCmd.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        SupplierQuotationModel? updated = await LoadQuotationAsync(connection, null, quotationId, cancellationToken);
        return Result<SupplierQuotationModel>.Success(updated!);
    }

    public async Task<Result<PurchaseOrderModel>> CreatePurchaseOrderAsync(
        CreatePurchaseOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.SupplierId == Guid.Empty || request.ActorUserId == Guid.Empty)
        {
            return Result<PurchaseOrderModel>.Fail(
                Error.Validation("Lpi.PurchaseOrder.SupplierRequired", "Supplier and actor user are required."));
        }
        if (request.Lines is null || request.Lines.Count == 0)
        {
            return Result<PurchaseOrderModel>.Fail(
                Error.Validation("Lpi.PurchaseOrder.LinesRequired", "At least one purchase order line is required."));
        }
        if (request.ShippingCents < 0)
        {
            return Result<PurchaseOrderModel>.Fail(
                Error.Validation("Lpi.PurchaseOrder.InvalidShipping", "Shipping charges cannot be negative."));
        }
        if (!request.SourceRequisitionId.HasValue && string.IsNullOrWhiteSpace(request.Notes))
        {
            return Result<PurchaseOrderModel>.Fail(
                Error.Validation("Lpi.PurchaseOrder.DirectReasonRequired", "A business reason is required for a direct purchase order."));
        }

        List<(CreatePurchaseOrderLineInput Line, long TaxCents, long LineTotalCents)> computedLines = new(request.Lines.Count);
        long subtotalCents = 0;
        long totalDiscountCents = 0;
        long totalTaxCents = 0;
        long totalAmountCents;

        try
        {
            foreach (var line in request.Lines)
            {
                if (string.IsNullOrWhiteSpace(line.ItemDescription) || line.OrderedQtyX1000 <= 0 || line.UnitCostCents < 0 || line.DiscountCents < 0)
                {
                    return Result<PurchaseOrderModel>.Fail(
                        Error.Validation("Lpi.PurchaseOrder.InvalidLine", "Each PO line requires a description, positive ordered quantity, non-negative unit cost, and non-negative discount."));
                }
                if (line.TaxRateBp < 0 || line.TaxRateBp > 10_000)
                {
                    return Result<PurchaseOrderModel>.Fail(
                        Error.Validation("Lpi.PurchaseOrder.InvalidTaxRate", "Tax rate basis points must be between 0 and 10000."));
                }
                checked
                {
                    long gross = (line.OrderedQtyX1000 * line.UnitCostCents) / 1000L;
                    if (line.DiscountCents > gross)
                    {
                        return Result<PurchaseOrderModel>.Fail(
                            Error.Validation("Lpi.PurchaseOrder.DiscountExceedsGross", $"Line discount ({line.DiscountCents}) cannot exceed line gross amount ({gross})."));
                    }
                    long netBeforeTax = gross - line.DiscountCents;
                    long tax = line.TaxRateBp > 0 ? (netBeforeTax * line.TaxRateBp) / 10000L : 0L;
                    long lineTotal = netBeforeTax + tax;
                    subtotalCents += gross;
                    totalDiscountCents += line.DiscountCents;
                    totalTaxCents += tax;
                    computedLines.Add((line, tax, lineTotal));
                }
            }
            checked
            {
                totalAmountCents = subtotalCents - totalDiscountCents + totalTaxCents + request.ShippingCents;
            }
        }
        catch (OverflowException)
        {
            return Result<PurchaseOrderModel>.Fail(
                Error.Validation("Lpi.PurchaseOrder.AmountOverflow", "Purchase order amount calculation overflowed supported bounds."));
        }

        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        SqlitePragma.Apply(connection);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        LpiSupplier? supplier = await FindSupplierByIdAsync(connection, transaction, request.SupplierId, cancellationToken);
        if (supplier is null || !supplier.IsActive)
        {
            return Result<PurchaseOrderModel>.Fail(
                Error.Validation("Lpi.PurchaseOrder.SupplierInactive", "Cannot issue a purchase order to an inactive or unknown supplier."));
        }

        PurchaseRequisitionModel? sourcePr = null;
        if (request.SourceRequisitionId.HasValue)
        {
            sourcePr = await LoadPurchaseRequisitionAsync(connection, transaction, request.SourceRequisitionId.Value, cancellationToken);
            if (sourcePr is null)
            {
                return Result<PurchaseOrderModel>.Fail(
                    Error.NotFound("Lpi.PurchaseOrder.SourcePrNotFound", "Source purchase requisition was not found."));
            }
            if (sourcePr.Status is not ("Approved" or "PartiallyConverted"))
            {
                return Result<PurchaseOrderModel>.Fail(
                    Error.Conflict("Lpi.PurchaseOrder.SourcePrNotApproved", $"Source requisition '{sourcePr.PrNumber}' must be Approved before converting to a PO (current: '{sourcePr.Status}')."));
            }
        }

        string poNumber = await AllocateSequenceInTransactionAsync(connection, transaction, "PO", cancellationToken);
        Guid poId = Guid.NewGuid();
        Guid locationId = request.DeliveryLocationId ?? Guid.Parse(DefaultMainLocationId);
        int paymentTerms = request.PaymentTermsDays ?? supplier.PaymentTermsDays;
        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateOnly orderDate = DateOnly.FromDateTime(now.UtcDateTime);
        string status = request.SubmitAndApproveImmediately ? "Approved" : "Draft";

        await using (SqliteCommand cmd = connection.CreateCommand())
        {
            cmd.Transaction = transaction;
            cmd.CommandText = """
                INSERT INTO purchase_orders (
                    id, po_number, supplier_id, source_requisition_id, order_date, expected_delivery_date,
                    delivery_location_id, payment_terms_days, status, subtotal_cents, discount_cents,
                    tax_cents, shipping_cents, total_amount_cents, created_by_user_id,
                    approved_by_user_id, approved_at, terms_and_conditions, notes, created_at, updated_at
                )
                VALUES (
                    $id, $poNo, $supId, $srcPrId, $orderDate, $expDate,
                    $locId, $terms, $status, $sub, $disc,
                    $tax, $ship, $total, $createdBy,
                    $appBy, $appAt, $tnc, $notes, $now, $now
                );
                """;
            cmd.Parameters.AddWithValue("$id", poId.ToString("D"));
            cmd.Parameters.AddWithValue("$poNo", poNumber);
            cmd.Parameters.AddWithValue("$supId", request.SupplierId.ToString("D"));
            cmd.Parameters.AddWithValue("$srcPrId", request.SourceRequisitionId?.ToString("D") ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("$orderDate", orderDate.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("$expDate", request.ExpectedDeliveryDate.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("$locId", locationId.ToString("D"));
            cmd.Parameters.AddWithValue("$terms", paymentTerms);
            cmd.Parameters.AddWithValue("$status", status);
            cmd.Parameters.AddWithValue("$sub", subtotalCents);
            cmd.Parameters.AddWithValue("$disc", totalDiscountCents);
            cmd.Parameters.AddWithValue("$tax", totalTaxCents);
            cmd.Parameters.AddWithValue("$ship", request.ShippingCents);
            cmd.Parameters.AddWithValue("$total", totalAmountCents);
            cmd.Parameters.AddWithValue("$createdBy", request.ActorUserId.ToString("D"));
            cmd.Parameters.AddWithValue("$appBy", request.SubmitAndApproveImmediately ? request.ActorUserId.ToString("D") : DBNull.Value);
            cmd.Parameters.AddWithValue("$appAt", request.SubmitAndApproveImmediately ? now.ToString("O") : DBNull.Value);
            cmd.Parameters.AddWithValue("$tnc", (object?)Clean(request.TermsAndConditions) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$notes", (object?)Clean(request.Notes) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$now", now.ToString("O"));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        for (int i = 0; i < computedLines.Count; i++)
        {
            var (line, tax, lineTotal) = computedLines[i];

            await using SqliteCommand lineCmd = connection.CreateCommand();
            lineCmd.Transaction = transaction;
            lineCmd.CommandText = """
                INSERT INTO purchase_order_lines (
                    id, purchase_order_id, line_number, requisition_line_id, product_id,
                    item_code, item_description, purchase_classification, unit_name,
                    ordered_qty_x1000, received_accepted_qty_x1000, received_rejected_qty_x1000,
                    cancelled_qty_x1000, unit_cost_cents, discount_cents, tax_rate_bp,
                    tax_amount_cents, line_total_cents, notes
                )
                VALUES (
                    $id, $poId, $lineNo, $reqLineId, $prodId,
                    $code, $desc, $class, $unit,
                    $ordQty, 0, 0,
                    0, $unitCost, $disc, $taxBp,
                    $taxAmt, $lineTotal, $notes
                );
                """;
            lineCmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
            lineCmd.Parameters.AddWithValue("$poId", poId.ToString("D"));
            lineCmd.Parameters.AddWithValue("$lineNo", i + 1);
            lineCmd.Parameters.AddWithValue("$reqLineId", line.RequisitionLineId?.ToString("D") ?? (object)DBNull.Value);
            lineCmd.Parameters.AddWithValue("$prodId", line.ProductId?.ToString("D") ?? (object)DBNull.Value);
            lineCmd.Parameters.AddWithValue("$code", Clean(line.ItemCode) ?? $"PO-ITEM-{i + 1:D2}");
            lineCmd.Parameters.AddWithValue("$desc", line.ItemDescription.Trim());
            lineCmd.Parameters.AddWithValue("$class", PurchaseClassifications.Normalize(line.PurchaseClassification));
            lineCmd.Parameters.AddWithValue("$unit", Clean(line.UnitName) ?? "pc");
            lineCmd.Parameters.AddWithValue("$ordQty", line.OrderedQtyX1000);
            lineCmd.Parameters.AddWithValue("$unitCost", line.UnitCostCents);
            lineCmd.Parameters.AddWithValue("$disc", line.DiscountCents);
            lineCmd.Parameters.AddWithValue("$taxBp", line.TaxRateBp);
            lineCmd.Parameters.AddWithValue("$taxAmt", tax);
            lineCmd.Parameters.AddWithValue("$lineTotal", lineTotal);
            lineCmd.Parameters.AddWithValue("$notes", (object?)Clean(line.Notes) ?? DBNull.Value);
            await lineCmd.ExecuteNonQueryAsync(cancellationToken);

            if (line.RequisitionLineId.HasValue)
            {
                await using SqliteCommand updPrLine = connection.CreateCommand();
                updPrLine.Transaction = transaction;
                updPrLine.CommandText = """
                    UPDATE purchase_requisition_lines
                    SET converted_po_qty_x1000 = converted_po_qty_x1000 + $qty
                    WHERE id = $reqLineId;
                    """;
                updPrLine.Parameters.AddWithValue("$qty", line.OrderedQtyX1000);
                updPrLine.Parameters.AddWithValue("$reqLineId", line.RequisitionLineId.Value.ToString("D"));
                await updPrLine.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        if (sourcePr is not null)
        {
            await UpdateSourceRequisitionConversionStatusAsync(connection, transaction, sourcePr.Id, request.ActorUserId, poNumber, now, cancellationToken);
        }

        await RecordPurchaseOrderHistoryAsync(
            connection,
            transaction,
            poId,
            request.SubmitAndApproveImmediately ? "CreatedAndApproved" : "CreatedDraft",
            request.ActorUserId,
            "None",
            status,
            request.Notes ?? $"Created purchase order {poNumber}.",
            now,
            cancellationToken);

        await WriteAuditAsync(
            connection,
            transaction,
            request.ActorUserId,
            "PO_CREATED",
            "PurchaseOrder",
            poId.ToString("D"),
            new { poNumber, supplier.Name, status, totalAmountCents, sourcePrNumber = sourcePr?.PrNumber },
            null,
            $"Created purchase order {poNumber}.",
            now,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        PurchaseOrderModel? created = await LoadPurchaseOrderAsync(connection, null, poId, cancellationToken);
        return Result<PurchaseOrderModel>.Success(created!);
    }

    public async Task<Result<PurchaseOrderModel>> TransitionPurchaseOrderStatusAsync(
        PurchaseOrderStatusActionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.PurchaseOrderId == Guid.Empty || request.ActorUserId == Guid.Empty)
        {
            return Result<PurchaseOrderModel>.Fail(Error.Validation("Lpi.PurchaseOrder.InvalidAction", "Purchase order ID and actor user ID are required."));
        }

        await using SqliteConnection connection = _connections.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        SqlitePragma.Apply(connection);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        PurchaseOrderModel? existing = await LoadPurchaseOrderAsync(connection, transaction, request.PurchaseOrderId, cancellationToken);
        if (existing is null)
        {
            return Result<PurchaseOrderModel>.Fail(Error.NotFound("Lpi.PurchaseOrder.NotFound", "Purchase order was not found."));
        }

        string action = request.Action?.Trim() ?? string.Empty;
        DateTimeOffset now = _timeProvider.GetUtcNow();
        string newStatus;

        if (string.Equals(action, "Submit", StringComparison.OrdinalIgnoreCase))
        {
            if (existing.Status != "Draft")
                return Result<PurchaseOrderModel>.Fail(Error.Conflict("Lpi.PurchaseOrder.InvalidTransition", "Only Draft POs can be submitted for approval."));
            newStatus = "PendingApproval";
        }
        else if (string.Equals(action, "Approve", StringComparison.OrdinalIgnoreCase))
        {
            if (existing.Status is not ("Draft" or "PendingApproval"))
                return Result<PurchaseOrderModel>.Fail(Error.Conflict("Lpi.PurchaseOrder.InvalidTransition", $"Cannot approve PO in '{existing.Status}' status."));
            newStatus = "Approved";
        }
        else if (string.Equals(action, "MarkSent", StringComparison.OrdinalIgnoreCase))
        {
            if (existing.Status != "Approved")
                return Result<PurchaseOrderModel>.Fail(Error.Conflict("Lpi.PurchaseOrder.InvalidTransition", "PO must be Approved before marking as Sent."));
            newStatus = "Sent";
        }
        else if (string.Equals(action, "MarkConfirmed", StringComparison.OrdinalIgnoreCase))
        {
            if (existing.Status is not ("Approved" or "Sent"))
                return Result<PurchaseOrderModel>.Fail(Error.Conflict("Lpi.PurchaseOrder.InvalidTransition", "PO must be Approved or Sent before supplier confirmation."));
            newStatus = "Confirmed";
        }
        else if (string.Equals(action, "Cancel", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(request.Reason))
                return Result<PurchaseOrderModel>.Fail(Error.Validation("Lpi.PurchaseOrder.CancelReasonRequired", "A cancellation reason is mandatory."));
            if (existing.Status is "PartiallyReceived" or "FullyReceived" or "Closed" or "Cancelled")
                return Result<PurchaseOrderModel>.Fail(Error.Conflict("Lpi.PurchaseOrder.CannotCancelDelivered", $"Cannot cancel PO in '{existing.Status}' status once goods have been received or PO is closed."));
            newStatus = "Cancelled";
        }
        else if (string.Equals(action, "Close", StringComparison.OrdinalIgnoreCase))
        {
            if (existing.Status is not ("FullyReceived" or "PartiallyReceived"))
                return Result<PurchaseOrderModel>.Fail(Error.Conflict("Lpi.PurchaseOrder.InvalidClose", "Only received POs can be closed."));
            newStatus = "Closed";
        }
        else
        {
            return Result<PurchaseOrderModel>.Fail(Error.Validation("Lpi.PurchaseOrder.UnknownAction", $"Unsupported PO action '{action}'."));
        }

        await using (SqliteCommand upd = connection.CreateCommand())
        {
            upd.Transaction = transaction;
            upd.CommandText = """
                UPDATE purchase_orders
                SET status = $status,
                    approved_by_user_id = CASE WHEN $status = 'Approved' THEN $actor ELSE approved_by_user_id END,
                    approved_at = CASE WHEN $status = 'Approved' THEN $now ELSE approved_at END,
                    sent_at = CASE WHEN $status = 'Sent' THEN $now ELSE sent_at END,
                    confirmed_at = CASE WHEN $status = 'Confirmed' THEN $now ELSE confirmed_at END,
                    cancelled_by_user_id = CASE WHEN $status = 'Cancelled' THEN $actor ELSE cancelled_by_user_id END,
                    cancelled_at = CASE WHEN $status = 'Cancelled' THEN $now ELSE cancelled_at END,
                    cancellation_reason = CASE WHEN $status = 'Cancelled' THEN $reason ELSE cancellation_reason END,
                    updated_at = $now
                WHERE id = $id;
                """;
            upd.Parameters.AddWithValue("$status", newStatus);
            upd.Parameters.AddWithValue("$actor", request.ActorUserId.ToString("D"));
            upd.Parameters.AddWithValue("$reason", (object?)Clean(request.Reason) ?? DBNull.Value);
            upd.Parameters.AddWithValue("$now", now.ToString("O"));
            upd.Parameters.AddWithValue("$id", existing.Id.ToString("D"));
            await upd.ExecuteNonQueryAsync(cancellationToken);
        }

        if (newStatus == "Cancelled")
        {
            await using SqliteCommand cancelLines = connection.CreateCommand();
            cancelLines.Transaction = transaction;
            cancelLines.CommandText = """
                UPDATE purchase_order_lines
                SET cancelled_qty_x1000 = MAX(0, ordered_qty_x1000 - received_accepted_qty_x1000)
                WHERE purchase_order_id = $poId;
                """;
            cancelLines.Parameters.AddWithValue("$poId", existing.Id.ToString("D"));
            await cancelLines.ExecuteNonQueryAsync(cancellationToken);
        }

        await RecordPurchaseOrderHistoryAsync(
            connection,
            transaction,
            existing.Id,
            action,
            request.ActorUserId,
            existing.Status,
            newStatus,
            request.Reason,
            now,
            cancellationToken);

        await WriteAuditAsync(
            connection,
            transaction,
            request.ActorUserId,
            $"PO_{newStatus.ToUpperInvariant()}",
            "PurchaseOrder",
            existing.Id.ToString("D"),
            new { poNumber = existing.PoNumber, status = newStatus, reason = request.Reason },
            new { status = existing.Status },
            request.Reason ?? $"Purchase order transitioned to {newStatus}.",
            now,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        PurchaseOrderModel? updated = await LoadPurchaseOrderAsync(connection, null, existing.Id, cancellationToken);
        return Result<PurchaseOrderModel>.Success(updated!);
    }

    private static async Task UpdateSourceRequisitionConversionStatusAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid requisitionId,
        Guid actorUserId,
        string poNumber,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        PurchaseRequisitionModel? pr = await LoadPurchaseRequisitionAsync(connection, transaction, requisitionId, cancellationToken);
        if (pr is null) return;

        bool allConverted = pr.Lines.All(l => l.ConvertedPoQtyX1000 >= (l.ApprovedQtyX1000 > 0 ? l.ApprovedQtyX1000 : l.RequestedQtyX1000));
        string nextStatus = allConverted ? "ConvertedToPo" : "PartiallyConverted";

        await using SqliteCommand upd = connection.CreateCommand();
        upd.Transaction = transaction;
        upd.CommandText = """
            UPDATE purchase_requisitions
            SET status = $status,
                updated_at = $now
            WHERE id = $id;
            """;
        upd.Parameters.AddWithValue("$status", nextStatus);
        upd.Parameters.AddWithValue("$now", now.ToString("O"));
        upd.Parameters.AddWithValue("$id", requisitionId.ToString("D"));
        await upd.ExecuteNonQueryAsync(cancellationToken);

        await RecordRequisitionActionAsync(
            connection,
            transaction,
            requisitionId,
            nextStatus,
            actorUserId,
            pr.Status,
            nextStatus,
            $"Converted to Purchase Order {poNumber}.",
            now,
            cancellationToken);
    }

    private static async Task RecordRequisitionActionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid requisitionId,
        string actionType,
        Guid actorUserId,
        string previousStatus,
        string newStatus,
        string? reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT INTO purchase_requisition_actions (
                id, requisition_id, action_type, actor_user_id, previous_status, new_status, reason, created_at
            )
            VALUES (
                $id, $reqId, $action, $actor, $prev, $next, $reason, $now
            );
            """;
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        cmd.Parameters.AddWithValue("$reqId", requisitionId.ToString("D"));
        cmd.Parameters.AddWithValue("$action", actionType);
        cmd.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
        cmd.Parameters.AddWithValue("$prev", previousStatus);
        cmd.Parameters.AddWithValue("$next", newStatus);
        cmd.Parameters.AddWithValue("$reason", (object?)Clean(reason) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$now", now.ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task RecordPurchaseOrderHistoryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid purchaseOrderId,
        string actionType,
        Guid actorUserId,
        string previousStatus,
        string newStatus,
        string? reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            INSERT INTO purchase_order_history (
                id, purchase_order_id, action_type, actor_user_id, previous_status, new_status, reason, created_at
            )
            VALUES (
                $id, $poId, $action, $actor, $prev, $next, $reason, $now
            );
            """;
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        cmd.Parameters.AddWithValue("$poId", purchaseOrderId.ToString("D"));
        cmd.Parameters.AddWithValue("$action", actionType);
        cmd.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
        cmd.Parameters.AddWithValue("$prev", previousStatus);
        cmd.Parameters.AddWithValue("$next", newStatus);
        cmd.Parameters.AddWithValue("$reason", (object?)Clean(reason) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$now", now.ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string? ExtractSuggestedSupplierName(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return null;
        const string prefix = "Suggested supplier:";
        if (!notes.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        string remainder = notes[prefix.Length..].Trim();
        int pipeIndex = remainder.IndexOf('|');
        string extracted = pipeIndex >= 0 ? remainder[..pipeIndex].Trim() : remainder;
        return string.IsNullOrWhiteSpace(extracted) ? null : extracted;
    }

    private static async Task<PurchaseRequisitionModel?> LoadPurchaseRequisitionAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid requisitionId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            SELECT pr.id, pr.pr_number, pr.department_name, pr.requested_by_user_id,
                   COALESCE(u_req.display_name, u_req.username, 'Operator'),
                   pr.date_requested, pr.date_needed, pr.priority, pr.purpose, pr.status,
                   pr.estimated_total_cents, pr.approved_by_user_id,
                   COALESCE(u_app.display_name, u_app.username),
                   pr.approved_at, pr.rejection_or_revision_reason, pr.notes,
                   pr.created_at, pr.updated_at
            FROM purchase_requisitions pr
            LEFT JOIN users u_req ON u_req.id = pr.requested_by_user_id
            LEFT JOIN users u_app ON u_app.id = pr.approved_by_user_id
            WHERE pr.id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", requisitionId.ToString("D"));

        Guid id;
        string prNumber, dept, reqByName, priority, purpose, status;
        Guid reqById;
        DateOnly dateReq, dateNeeded;
        long estTotal;
        Guid? appById;
        string? appByName, reason, notes;
        DateTimeOffset? appAt;
        DateTimeOffset createdAt, updatedAt;

        await using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken)) return null;
            id = Guid.Parse(reader.GetString(0));
            prNumber = reader.GetString(1);
            dept = reader.GetString(2);
            reqById = Guid.Parse(reader.GetString(3));
            reqByName = reader.GetString(4);
            dateReq = DateOnly.Parse(reader.GetString(5));
            dateNeeded = DateOnly.Parse(reader.GetString(6));
            priority = reader.GetString(7);
            purpose = reader.GetString(8);
            status = reader.GetString(9);
            estTotal = reader.GetInt64(10);
            appById = reader.IsDBNull(11) ? null : Guid.Parse(reader.GetString(11));
            appByName = reader.IsDBNull(12) ? null : reader.GetString(12);
            appAt = reader.IsDBNull(13) ? null : DateTimeOffset.Parse(reader.GetString(13));
            reason = reader.IsDBNull(14) ? null : reader.GetString(14);
            notes = reader.IsDBNull(15) ? null : reader.GetString(15);
            createdAt = DateTimeOffset.Parse(reader.GetString(16));
            updatedAt = DateTimeOffset.Parse(reader.GetString(17));
        }

        List<PurchaseRequisitionLineModel> lines = new();
        await using (SqliteCommand lineCmd = connection.CreateCommand())
        {
            lineCmd.Transaction = transaction;
            lineCmd.CommandText = """
                SELECT l.id, l.requisition_id, l.line_number, l.product_id, l.item_code, l.item_description,
                       l.category_name, l.purchase_classification, l.unit_name, l.requested_qty_x1000,
                       l.approved_qty_x1000, l.converted_po_qty_x1000, l.estimated_unit_cost_cents,
                       l.estimated_line_total_cents, l.preferred_supplier_id, s.name, l.specifications, l.notes
                FROM purchase_requisition_lines l
                LEFT JOIN suppliers s ON s.id = l.preferred_supplier_id
                WHERE l.requisition_id = $id
                ORDER BY l.line_number;
                """;
            lineCmd.Parameters.AddWithValue("$id", requisitionId.ToString("D"));
            await using SqliteDataReader reader = await lineCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                long reqQty = reader.GetInt64(9);
                long appQty = reader.GetInt64(10);
                long convQty = reader.GetInt64(11);
                long basisQty = appQty > 0 ? appQty : reqQty;
                string? supplierName = reader.IsDBNull(15) ? null : reader.GetString(15);
                string? lineNotes = reader.IsDBNull(17) ? null : reader.GetString(17);
                supplierName ??= ExtractSuggestedSupplierName(lineNotes);
                lines.Add(new PurchaseRequisitionLineModel(
                    Guid.Parse(reader.GetString(0)),
                    Guid.Parse(reader.GetString(1)),
                    reader.GetInt32(2),
                    reader.IsDBNull(3) ? null : Guid.Parse(reader.GetString(3)),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    reqQty,
                    appQty,
                    convQty,
                    Math.Max(0, basisQty - convQty),
                    reader.GetInt64(12),
                    reader.GetInt64(13),
                    reader.IsDBNull(14) ? null : Guid.Parse(reader.GetString(14)),
                    supplierName,
                    reader.IsDBNull(16) ? null : reader.GetString(16),
                    lineNotes));
            }
        }

        List<PurchaseRequisitionActionModel> actions = new();
        await using (SqliteCommand actCmd = connection.CreateCommand())
        {
            actCmd.Transaction = transaction;
            actCmd.CommandText = """
                SELECT a.id, a.requisition_id, a.action_type, a.actor_user_id,
                       COALESCE(u.display_name, u.username, 'Operator'),
                       a.previous_status, a.new_status, a.reason, a.created_at
                FROM purchase_requisition_actions a
                LEFT JOIN users u ON u.id = a.actor_user_id
                WHERE a.requisition_id = $id
                ORDER BY a.created_at ASC;
                """;
            actCmd.Parameters.AddWithValue("$id", requisitionId.ToString("D"));
            await using SqliteDataReader reader = await actCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                actions.Add(new PurchaseRequisitionActionModel(
                    Guid.Parse(reader.GetString(0)),
                    Guid.Parse(reader.GetString(1)),
                    reader.GetString(2),
                    Guid.Parse(reader.GetString(3)),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    DateTimeOffset.Parse(reader.GetString(8))));
            }
        }

        return new PurchaseRequisitionModel(
            id, prNumber, dept, reqById, reqByName, dateReq, dateNeeded, priority, purpose,
            status, estTotal, appById, appByName, appAt, reason, notes, createdAt, updatedAt,
            lines, actions);
    }

    private static async Task<PurchaseOrderModel?> LoadPurchaseOrderAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid purchaseOrderId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = """
            SELECT po.id, po.po_number, po.supplier_id, s.name, s.code,
                   po.source_requisition_id, pr.pr_number, po.order_date, po.expected_delivery_date,
                   po.delivery_location_id, loc.name, po.payment_terms_days, po.status,
                   po.subtotal_cents, po.discount_cents, po.tax_cents, po.shipping_cents, po.total_amount_cents,
                   po.created_by_user_id, COALESCE(u_cr.display_name, u_cr.username, 'Operator'),
                   po.approved_by_user_id, COALESCE(u_ap.display_name, u_ap.username),
                   po.approved_at, po.sent_at, po.confirmed_at, po.cancelled_by_user_id,
                   po.cancelled_at, po.cancellation_reason, po.terms_and_conditions, po.notes,
                   po.created_at, po.updated_at
            FROM purchase_orders po
            JOIN suppliers s ON s.id = po.supplier_id
            JOIN inventory_locations loc ON loc.id = po.delivery_location_id
            LEFT JOIN purchase_requisitions pr ON pr.id = po.source_requisition_id
            LEFT JOIN users u_cr ON u_cr.id = po.created_by_user_id
            LEFT JOIN users u_ap ON u_ap.id = po.approved_by_user_id
            WHERE po.id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", purchaseOrderId.ToString("D"));

        Guid id, supId, locId, createdById;
        string poNo, supName, locName, status, createdByName;
        string? supCode, srcPrNo, appByName, cancelReason, tnc, notes;
        Guid? srcPrId, appById, cancelById;
        DateOnly orderDate, expDate;
        int terms;
        long sub, disc, tax, ship, total;
        DateTimeOffset? appAt, sentAt, confAt, cancelAt;
        DateTimeOffset createdAt, updatedAt;

        await using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken)) return null;
            id = Guid.Parse(reader.GetString(0));
            poNo = reader.GetString(1);
            supId = Guid.Parse(reader.GetString(2));
            supName = reader.GetString(3);
            supCode = reader.IsDBNull(4) ? null : reader.GetString(4);
            srcPrId = reader.IsDBNull(5) ? null : Guid.Parse(reader.GetString(5));
            srcPrNo = reader.IsDBNull(6) ? null : reader.GetString(6);
            orderDate = DateOnly.Parse(reader.GetString(7));
            expDate = DateOnly.Parse(reader.GetString(8));
            locId = Guid.Parse(reader.GetString(9));
            locName = reader.GetString(10);
            terms = reader.GetInt32(11);
            status = reader.GetString(12);
            sub = reader.GetInt64(13);
            disc = reader.GetInt64(14);
            tax = reader.GetInt64(15);
            ship = reader.GetInt64(16);
            total = reader.GetInt64(17);
            createdById = Guid.Parse(reader.GetString(18));
            createdByName = reader.GetString(19);
            appById = reader.IsDBNull(20) ? null : Guid.Parse(reader.GetString(20));
            appByName = reader.IsDBNull(21) ? null : reader.GetString(21);
            appAt = reader.IsDBNull(22) ? null : DateTimeOffset.Parse(reader.GetString(22));
            sentAt = reader.IsDBNull(23) ? null : DateTimeOffset.Parse(reader.GetString(23));
            confAt = reader.IsDBNull(24) ? null : DateTimeOffset.Parse(reader.GetString(24));
            cancelById = reader.IsDBNull(25) ? null : Guid.Parse(reader.GetString(25));
            cancelAt = reader.IsDBNull(26) ? null : DateTimeOffset.Parse(reader.GetString(26));
            cancelReason = reader.IsDBNull(27) ? null : reader.GetString(27);
            tnc = reader.IsDBNull(28) ? null : reader.GetString(28);
            notes = reader.IsDBNull(29) ? null : reader.GetString(29);
            createdAt = DateTimeOffset.Parse(reader.GetString(30));
            updatedAt = DateTimeOffset.Parse(reader.GetString(31));
        }

        List<PurchaseOrderLineModel> lines = new();
        await using (SqliteCommand lineCmd = connection.CreateCommand())
        {
            lineCmd.Transaction = transaction;
            lineCmd.CommandText = """
                SELECT id, purchase_order_id, line_number, requisition_line_id, product_id,
                       item_code, item_description, purchase_classification, unit_name,
                       ordered_qty_x1000, received_accepted_qty_x1000, received_rejected_qty_x1000,
                       cancelled_qty_x1000, unit_cost_cents, discount_cents, tax_rate_bp,
                       tax_amount_cents, line_total_cents, notes
                FROM purchase_order_lines
                WHERE purchase_order_id = $id
                ORDER BY line_number;
                """;
            lineCmd.Parameters.AddWithValue("$id", purchaseOrderId.ToString("D"));
            await using SqliteDataReader reader = await lineCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                long ord = reader.GetInt64(9);
                long recAcc = reader.GetInt64(10);
                long recRej = reader.GetInt64(11);
                long canc = reader.GetInt64(12);
                lines.Add(new PurchaseOrderLineModel(
                    Guid.Parse(reader.GetString(0)),
                    Guid.Parse(reader.GetString(1)),
                    reader.GetInt32(2),
                    reader.IsDBNull(3) ? null : Guid.Parse(reader.GetString(3)),
                    reader.IsDBNull(4) ? null : Guid.Parse(reader.GetString(4)),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    ord,
                    recAcc,
                    recRej,
                    canc,
                    Math.Max(0, ord - recAcc - canc),
                    reader.GetInt64(13),
                    reader.GetInt64(14),
                    reader.GetInt32(15),
                    reader.GetInt64(16),
                    reader.GetInt64(17),
                    reader.IsDBNull(18) ? null : reader.GetString(18)));
            }
        }

        List<PurchaseOrderHistoryModel> history = new();
        await using (SqliteCommand histCmd = connection.CreateCommand())
        {
            histCmd.Transaction = transaction;
            histCmd.CommandText = """
                SELECT h.id, h.purchase_order_id, h.action_type, h.actor_user_id,
                       COALESCE(u.display_name, u.username, 'Operator'),
                       h.previous_status, h.new_status, h.reason, h.created_at
                FROM purchase_order_history h
                LEFT JOIN users u ON u.id = h.actor_user_id
                WHERE h.purchase_order_id = $id
                ORDER BY h.created_at ASC;
                """;
            histCmd.Parameters.AddWithValue("$id", purchaseOrderId.ToString("D"));
            await using SqliteDataReader reader = await histCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                history.Add(new PurchaseOrderHistoryModel(
                    Guid.Parse(reader.GetString(0)),
                    Guid.Parse(reader.GetString(1)),
                    reader.GetString(2),
                    Guid.Parse(reader.GetString(3)),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    DateTimeOffset.Parse(reader.GetString(8))));
            }
        }

        return new PurchaseOrderModel(
            id, poNo, supId, supName, supCode, srcPrId, srcPrNo, orderDate, expDate,
            locId, locName, terms, status, sub, disc, tax, ship, total,
            createdById, createdByName, appById, appByName, appAt, sentAt, confAt,
            cancelById, cancelAt, cancelReason, tnc, notes, createdAt, updatedAt,
            lines, history);
    }
}
