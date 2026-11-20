using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Infrastructure.Data.Sync;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Repositories.Inventory;

public sealed class SqliteCafeInventoryStore : SqliteRepositoryBase
{
    private readonly SqliteSyncOutboxStore _outbox;

    public SqliteCafeInventoryStore(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
        _outbox = new SqliteSyncOutboxStore(connectionFactory);
    }

    public Result<CafeInventoryCommitResult<PurchaseReceiptEvidence>> CommitPurchase(
        PurchaseReceiptMutation mutation)
    {
        string? validation = ValidatePurchaseMutation(mutation);
        if (validation is not null)
        {
            return Result<CafeInventoryCommitResult<PurchaseReceiptEvidence>>.Fail(
                Error.Validation("CafeInventory.PurchaseInvalid", validation));
        }

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Result localAuthority = ValidateEnvelopeAuthority(
                connection, transaction, mutation.StoreId, mutation.ActorUserId);
            if (localAuthority.IsFailure)
            {
                transaction.Rollback();
                return Result<CafeInventoryCommitResult<PurchaseReceiptEvidence>>.Fail(localAuthority.ErrorDetail);
            }

            PurchaseReceiptEvidence? existing =
                ReadPurchaseByOperation(connection, transaction, mutation.OperationId);
            if (existing is not null)
            {
                bool same = PurchaseMatchesMutation(existing, mutation);
                transaction.Commit();
                return same
                    ? Result<CafeInventoryCommitResult<PurchaseReceiptEvidence>>.Success(
                        new CafeInventoryCommitResult<PurchaseReceiptEvidence>(existing, true))
                    : Result<CafeInventoryCommitResult<PurchaseReceiptEvidence>>.Fail(
                        Error.Conflict(
                            "CafeInventory.OperationConflict",
                            "OperationId already belongs to a different purchase payload."));
            }

            if (ExistsById(connection, transaction, "purchase_receipts", "purchase_receipt_id", mutation.PurchaseReceiptId))
            {
                transaction.Rollback();
                return Result<CafeInventoryCommitResult<PurchaseReceiptEvidence>>.Fail(
                    Error.Conflict("CafeInventory.PurchaseIdConflict", "PurchaseReceiptId already exists."));
            }

            SupplierRow supplier = ReadSupplier(connection, transaction, mutation.SupplierId)
                ?? throw new InventoryValidationException(
                    "CafeInventory.SupplierMissing",
                    "Purchase supplier does not exist.");

            if (mutation.CorrectionOfPurchaseReceiptId.HasValue &&
                !ExistsById(
                    connection,
                    transaction,
                    "purchase_receipts",
                    "purchase_receipt_id",
                    mutation.CorrectionOfPurchaseReceiptId.Value))
            {
                throw new InventoryValidationException(
                    "CafeInventory.PurchaseCorrectionOriginalMissing",
                    "Purchase correction requires an existing original receipt.");
            }

            List<ResolvedTargetLine> lines = new();
            foreach (PurchaseReceiptLineMutation line in mutation.Lines)
            {
                TargetSnapshot target = ReadTarget(connection, transaction, line.StockTargetType, line.StockTargetId)
                    ?? throw new InventoryValidationException(
                        "CafeInventory.TargetMissing",
                        "Purchase stock target does not exist.");

                long quantity = ValidateAndNormalizePurchaseQuantity(line, target, mutation.CorrectionOfPurchaseReceiptId.HasValue);

                lines.Add(new ResolvedTargetLine(
                    line.LineId.GetValueOrDefault() == Guid.Empty ? Guid.NewGuid() : line.LineId!.Value,
                    target,
                    quantity,
                    line.LineAcquisitionCostCents,
                    line.PurchasePackageLabel,
                    line.PurchasePackageQuantityX1000,
                    line.PurchasePackageContentX1000));
            }

            EnsureUniqueTargets(lines);
            EnsureResultingStockNonNegative(connection, transaction, lines.Select(x => (x.Target, x.QuantityDeltaX1000)));

            PurchaseReceiptCommittedV1 payload = new(
                mutation.PurchaseReceiptId,
                mutation.SupplierId,
                supplier.Name,
                Clean(mutation.ExternalReference),
                mutation.ReceivedAtUtc.ToUniversalTime(),
                mutation.ActorUserId,
                Clean(mutation.Notes),
                lines.Sum(x => x.AcquisitionCostCents ?? 0L),
                mutation.CorrectionOfPurchaseReceiptId,
                lines.Select(x => new CafeInventoryTargetDeltaV1(
                    x.LineId,
                    x.Target.TargetType,
                    x.Target.Id,
                    x.Target.Name,
                    x.Target.StockUnit,
                    x.QuantityDeltaX1000,
                    x.AcquisitionCostCents,
                    x.PurchasePackageLabel,
                    x.PurchasePackageQuantityX1000,
                    x.PurchasePackageContentX1000))
                .ToArray());

            string payloadJson = CafeInventoryOperationCanonicalJson.Serialize(payload);
            string contentHash = CafeInventoryOperationCanonicalJson.ComputeHash(payloadJson);
            InsertPurchase(connection, transaction, mutation, supplier, lines, contentHash);

            foreach (ResolvedTargetLine line in lines)
            {
                ApplyStockDelta(
                    connection,
                    transaction,
                    line.Target,
                    line.QuantityDeltaX1000,
                    mutation.CorrectionOfPurchaseReceiptId.HasValue
                        ? CafeInventoryMovementKinds.PurchaseCorrection
                        : CafeInventoryMovementKinds.PurchaseReceiving,
                    "PurchaseReceipt",
                    mutation.PurchaseReceiptId,
                    line.LineId,
                    mutation.CorrectionOfPurchaseReceiptId,
                    mutation.ActorUserId,
                    mutation.ReceivedAtUtc,
                    line.AcquisitionCostCents,
                    mutation.CorrectionOfPurchaseReceiptId.HasValue
                        ? "Purchase correction"
                        : "Purchase receiving");
            }

            _outbox.Enqueue(
                connection,
                transaction,
                new SyncEnqueueRequest(
                    mutation.DeviceId,
                    mutation.StoreId,
                    mutation.ActorUserId,
                    CafeInventoryOperationContract.PurchaseReceiptCommittedV1,
                    CafeInventoryOperationContract.SchemaVersion,
                    mutation.PurchaseReceiptId,
                    null,
                    mutation.ReceivedAtUtc,
                    payloadJson,
                    mutation.OperationId));

            PurchaseReceiptEvidence evidence =
                ReadPurchaseByOperation(connection, transaction, mutation.OperationId)
                ?? throw new InvalidOperationException("Committed purchase evidence could not be re-read.");

            transaction.Commit();
            return Result<CafeInventoryCommitResult<PurchaseReceiptEvidence>>.Success(
                new CafeInventoryCommitResult<PurchaseReceiptEvidence>(evidence, false));
        }
        catch (InventoryValidationException ex)
        {
            return Result<CafeInventoryCommitResult<PurchaseReceiptEvidence>>.Fail(
                Error.Conflict(ex.Code, ex.Message));
        }
        catch (SqliteException ex) when (IsUniqueViolation(ex))
        {
            return Result<CafeInventoryCommitResult<PurchaseReceiptEvidence>>.Fail(
                Error.Conflict("CafeInventory.PurchaseConflict", ex.Message));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or OverflowException)
        {
            return Result<CafeInventoryCommitResult<PurchaseReceiptEvidence>>.Fail(
                Error.Unexpected("CafeInventory.PurchaseCommitFailed", ex.Message));
        }
    }

    public Result<CafeInventoryCommitResult<InventoryWasteEvidence>> CommitWaste(
        InventoryWasteMutation mutation)
    {
        if (mutation.OperationId == Guid.Empty ||
            mutation.WasteEventId == Guid.Empty ||
            mutation.DeviceId == Guid.Empty ||
            mutation.StoreId == Guid.Empty ||
            mutation.ActorUserId == Guid.Empty ||
            mutation.Lines is null ||
            mutation.Lines.Count == 0 ||
            !CafeInventoryWasteReasons.IsValid(mutation.Reason))
        {
            return Result<CafeInventoryCommitResult<InventoryWasteEvidence>>.Fail(
                Error.Validation(
                    "CafeInventory.WasteInvalid",
                    "Waste requires identities, a supported reason and at least one line."));
        }

        if (mutation.Lines.Any(x =>
                x.StockTargetId == Guid.Empty ||
                x.QuantityWastedX1000 <= 0 ||
                !CafeStockTargetTypes.IsValid(x.StockTargetType)))
        {
            return Result<CafeInventoryCommitResult<InventoryWasteEvidence>>.Fail(
                Error.Validation(
                    "CafeInventory.WasteLineInvalid",
                    "Waste lines require typed targets and positive quantities."));
        }

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Result authority = ValidateEnvelopeAuthority(
                connection, transaction, mutation.StoreId, mutation.ActorUserId);
            if (authority.IsFailure)
            {
                transaction.Rollback();
                return Result<CafeInventoryCommitResult<InventoryWasteEvidence>>.Fail(authority.ErrorDetail);
            }

            InventoryWasteEvidence? existing =
                ReadWasteByOperation(connection, transaction, mutation.OperationId);
            if (existing is not null)
            {
                bool same = WasteMatchesMutation(existing, mutation);
                transaction.Commit();
                return same
                    ? Result<CafeInventoryCommitResult<InventoryWasteEvidence>>.Success(
                        new CafeInventoryCommitResult<InventoryWasteEvidence>(existing, true))
                    : Result<CafeInventoryCommitResult<InventoryWasteEvidence>>.Fail(
                        Error.Conflict(
                            "CafeInventory.OperationConflict",
                            "OperationId already belongs to a different waste payload."));
            }

            if (mutation.CorrectionOfWasteEventId.HasValue &&
                !ExistsById(
                    connection,
                    transaction,
                    "inventory_waste_events",
                    "waste_event_id",
                    mutation.CorrectionOfWasteEventId.Value))
            {
                throw new InventoryValidationException(
                    "CafeInventory.WasteCorrectionOriginalMissing",
                    "Waste correction requires an existing original waste event.");
            }

            long sign = mutation.CorrectionOfWasteEventId.HasValue ? 1L : -1L;
            List<ResolvedTargetLine> lines = new();
            foreach (InventoryWasteLineMutation line in mutation.Lines)
            {
                TargetSnapshot target = ReadTarget(connection, transaction, line.StockTargetType, line.StockTargetId)
                    ?? throw new InventoryValidationException(
                        "CafeInventory.TargetMissing",
                        "Waste stock target does not exist.");

                lines.Add(new ResolvedTargetLine(
                    line.LineId.GetValueOrDefault() == Guid.Empty ? Guid.NewGuid() : line.LineId!.Value,
                    target,
                    checked(sign * line.QuantityWastedX1000),
                    null,
                    null,
                    null,
                    null));
            }

            EnsureUniqueTargets(lines);
            EnsureResultingStockNonNegative(connection, transaction, lines.Select(x => (x.Target, x.QuantityDeltaX1000)));

            InventoryWasteCommittedV1 payload = new(
                mutation.WasteEventId,
                mutation.Reason,
                mutation.ActorUserId,
                mutation.OccurredAtUtc.ToUniversalTime(),
                Clean(mutation.Notes),
                mutation.CorrectionOfWasteEventId,
                lines.Select(x => new CafeInventoryTargetDeltaV1(
                    x.LineId,
                    x.Target.TargetType,
                    x.Target.Id,
                    x.Target.Name,
                    x.Target.StockUnit,
                    x.QuantityDeltaX1000))
                .ToArray());

            string payloadJson = CafeInventoryOperationCanonicalJson.Serialize(payload);
            string contentHash = CafeInventoryOperationCanonicalJson.ComputeHash(payloadJson);
            InsertWaste(connection, transaction, mutation, lines, contentHash);

            foreach (ResolvedTargetLine line in lines)
            {
                ApplyStockDelta(
                    connection,
                    transaction,
                    line.Target,
                    line.QuantityDeltaX1000,
                    mutation.CorrectionOfWasteEventId.HasValue
                        ? CafeInventoryMovementKinds.WasteCorrection
                        : CafeInventoryMovementKinds.Waste,
                    "InventoryWaste",
                    mutation.WasteEventId,
                    line.LineId,
                    mutation.CorrectionOfWasteEventId,
                    mutation.ActorUserId,
                    mutation.OccurredAtUtc,
                    null,
                    mutation.Reason);
            }

            _outbox.Enqueue(
                connection,
                transaction,
                new SyncEnqueueRequest(
                    mutation.DeviceId,
                    mutation.StoreId,
                    mutation.ActorUserId,
                    CafeInventoryOperationContract.InventoryWasteCommittedV1,
                    CafeInventoryOperationContract.SchemaVersion,
                    mutation.WasteEventId,
                    null,
                    mutation.OccurredAtUtc,
                    payloadJson,
                    mutation.OperationId));

            InventoryWasteEvidence evidence =
                ReadWasteByOperation(connection, transaction, mutation.OperationId)
                ?? throw new InvalidOperationException("Committed waste evidence could not be re-read.");

            transaction.Commit();
            return Result<CafeInventoryCommitResult<InventoryWasteEvidence>>.Success(
                new CafeInventoryCommitResult<InventoryWasteEvidence>(evidence, false));
        }
        catch (InventoryValidationException ex)
        {
            return Result<CafeInventoryCommitResult<InventoryWasteEvidence>>.Fail(
                Error.Conflict(ex.Code, ex.Message));
        }
        catch (SqliteException ex) when (IsUniqueViolation(ex))
        {
            return Result<CafeInventoryCommitResult<InventoryWasteEvidence>>.Fail(
                Error.Conflict("CafeInventory.WasteConflict", ex.Message));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or OverflowException)
        {
            return Result<CafeInventoryCommitResult<InventoryWasteEvidence>>.Fail(
                Error.Unexpected("CafeInventory.WasteCommitFailed", ex.Message));
        }
    }

    public Result<CafeInventoryCommitResult<InventoryAdjustmentEvidence>> CommitAdjustment(
        InventoryAdjustmentMutation mutation)
    {
        if (mutation.OperationId == Guid.Empty ||
            mutation.AdjustmentEventId == Guid.Empty ||
            mutation.DeviceId == Guid.Empty ||
            mutation.StoreId == Guid.Empty ||
            mutation.ActorUserId == Guid.Empty ||
            mutation.StockTargetId == Guid.Empty ||
            mutation.QuantityDeltaX1000 == 0 ||
            !CafeStockTargetTypes.IsValid(mutation.StockTargetType) ||
            string.IsNullOrWhiteSpace(mutation.Reason))
        {
            return Result<CafeInventoryCommitResult<InventoryAdjustmentEvidence>>.Fail(
                Error.Validation(
                    "CafeInventory.AdjustmentInvalid",
                    "Adjustment requires identities, typed target, reason and non-zero delta."));
        }

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Result authority = ValidateEnvelopeAuthority(
                connection, transaction, mutation.StoreId, mutation.ActorUserId);
            if (authority.IsFailure)
            {
                transaction.Rollback();
                return Result<CafeInventoryCommitResult<InventoryAdjustmentEvidence>>.Fail(authority.ErrorDetail);
            }

            InventoryAdjustmentEvidence? existing =
                ReadAdjustmentByOperation(connection, transaction, mutation.OperationId);
            if (existing is not null)
            {
                bool same = AdjustmentMatchesMutation(existing, mutation);
                transaction.Commit();
                return same
                    ? Result<CafeInventoryCommitResult<InventoryAdjustmentEvidence>>.Success(
                        new CafeInventoryCommitResult<InventoryAdjustmentEvidence>(existing, true))
                    : Result<CafeInventoryCommitResult<InventoryAdjustmentEvidence>>.Fail(
                        Error.Conflict(
                            "CafeInventory.OperationConflict",
                            "OperationId already belongs to a different adjustment payload."));
            }

            TargetSnapshot target = ReadTarget(
                connection, transaction, mutation.StockTargetType, mutation.StockTargetId)
                ?? throw new InventoryValidationException(
                    "CafeInventory.TargetMissing",
                    "Adjustment stock target does not exist.");

            EnsureResultingStockNonNegative(
                connection,
                transaction,
                new[] { (target, mutation.QuantityDeltaX1000) });

            InventoryAdjustmentCommittedV1 payload = new(
                mutation.AdjustmentEventId,
                target.TargetType,
                target.Id,
                target.Name,
                target.StockUnit,
                mutation.QuantityDeltaX1000,
                mutation.Reason.Trim(),
                Clean(mutation.ReferenceType),
                mutation.ReferenceId,
                mutation.CorrectionOfReferenceId,
                mutation.ActorUserId,
                mutation.OccurredAtUtc.ToUniversalTime());

            string payloadJson = CafeInventoryOperationCanonicalJson.Serialize(payload);
            string contentHash = CafeInventoryOperationCanonicalJson.ComputeHash(payloadJson);

            using (SqliteCommand insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO inventory_adjustment_events(
                        adjustment_event_id,operation_id,store_id,
                        stock_target_type,stock_target_id,name_snapshot,stock_unit_snapshot,
                        quantity_delta_x1000,reason,reference_type,reference_id,
                        correction_of_reference_id,actor_user_id,occurred_at_utc,
                        content_hash,created_at_utc)
                    VALUES(
                        $id,$operation,$store,
                        $type,$target,$name,$unit,
                        $delta,$reason,$referenceType,$referenceId,
                        $correction,$actor,$occurred,
                        $hash,$created);
                    """;
                insert.Parameters.AddWithValue("$id", mutation.AdjustmentEventId.ToString("D"));
                insert.Parameters.AddWithValue("$operation", mutation.OperationId.ToString("D"));
                insert.Parameters.AddWithValue("$store", mutation.StoreId.ToString("D"));
                insert.Parameters.AddWithValue("$type", target.TargetType);
                insert.Parameters.AddWithValue("$target", target.Id.ToString("D"));
                insert.Parameters.AddWithValue("$name", target.Name);
                insert.Parameters.AddWithValue("$unit", target.StockUnit);
                insert.Parameters.AddWithValue("$delta", mutation.QuantityDeltaX1000);
                insert.Parameters.AddWithValue("$reason", mutation.Reason.Trim());
                insert.Parameters.AddWithValue("$referenceType", Db(Clean(mutation.ReferenceType)));
                insert.Parameters.AddWithValue("$referenceId", Db(mutation.ReferenceId));
                insert.Parameters.AddWithValue("$correction", Db(mutation.CorrectionOfReferenceId));
                insert.Parameters.AddWithValue("$actor", mutation.ActorUserId.ToString("D"));
                insert.Parameters.AddWithValue("$occurred", mutation.OccurredAtUtc.ToUniversalTime().ToString("O"));
                insert.Parameters.AddWithValue("$hash", contentHash);
                insert.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
                insert.ExecuteNonQuery();
            }

            ApplyStockDelta(
                connection,
                transaction,
                target,
                mutation.QuantityDeltaX1000,
                CafeInventoryMovementKinds.ManualAdjustment,
                Clean(mutation.ReferenceType) ?? "InventoryAdjustment",
                mutation.ReferenceId ?? mutation.AdjustmentEventId,
                mutation.AdjustmentEventId,
                mutation.CorrectionOfReferenceId,
                mutation.ActorUserId,
                mutation.OccurredAtUtc,
                null,
                mutation.Reason);

            _outbox.Enqueue(
                connection,
                transaction,
                new SyncEnqueueRequest(
                    mutation.DeviceId,
                    mutation.StoreId,
                    mutation.ActorUserId,
                    CafeInventoryOperationContract.InventoryAdjustmentCommittedV1,
                    CafeInventoryOperationContract.SchemaVersion,
                    mutation.AdjustmentEventId,
                    null,
                    mutation.OccurredAtUtc,
                    payloadJson,
                    mutation.OperationId));

            InventoryAdjustmentEvidence evidence =
                ReadAdjustmentByOperation(connection, transaction, mutation.OperationId)
                ?? throw new InvalidOperationException("Committed adjustment evidence could not be re-read.");

            transaction.Commit();
            return Result<CafeInventoryCommitResult<InventoryAdjustmentEvidence>>.Success(
                new CafeInventoryCommitResult<InventoryAdjustmentEvidence>(evidence, false));
        }
        catch (InventoryValidationException ex)
        {
            return Result<CafeInventoryCommitResult<InventoryAdjustmentEvidence>>.Fail(
                Error.Conflict(ex.Code, ex.Message));
        }
        catch (SqliteException ex) when (IsUniqueViolation(ex))
        {
            return Result<CafeInventoryCommitResult<InventoryAdjustmentEvidence>>.Fail(
                Error.Conflict("CafeInventory.AdjustmentConflict", ex.Message));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or OverflowException)
        {
            return Result<CafeInventoryCommitResult<InventoryAdjustmentEvidence>>.Fail(
                Error.Unexpected("CafeInventory.AdjustmentCommitFailed", ex.Message));
        }
    }

    public Result<InventoryCountSession> SaveCountDraft(InventoryCountStartMutation mutation)
    {
        if (mutation.CountSessionId == Guid.Empty ||
            mutation.StoreId == Guid.Empty ||
            mutation.StartedByUserId == Guid.Empty ||
            mutation.Lines is null ||
            mutation.Lines.Count == 0)
        {
            return Result<InventoryCountSession>.Fail(
                Error.Validation(
                    "CafeInventory.CountDraftInvalid",
                    "Count draft requires session/store/user identities and at least one line."));
        }

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Result authority = ValidateEnvelopeAuthority(
                connection, transaction, mutation.StoreId, mutation.StartedByUserId);
            if (authority.IsFailure)
            {
                transaction.Rollback();
                return Result<InventoryCountSession>.Fail(authority.ErrorDetail);
            }

            string? state = ScalarString(
                connection,
                transaction,
                "SELECT state FROM inventory_count_sessions WHERE count_session_id=$id;",
                ("$id", mutation.CountSessionId.ToString("D")));

            if (string.Equals(state, CafeInventoryCountStates.Finalized, StringComparison.Ordinal))
            {
                transaction.Rollback();
                return Result<InventoryCountSession>.Fail(
                    Error.Conflict(
                        "CafeInventory.CountFinalized",
                        "Finalized count sessions are immutable."));
            }

            List<CountDraftLine> resolved = new();
            HashSet<string> targets = new(StringComparer.Ordinal);
            foreach (InventoryCountLineMutation line in mutation.Lines)
            {
                if (line.StockTargetId == Guid.Empty ||
                    line.ExpectedBeforeX1000 < 0 ||
                    line.CountedX1000 < 0 ||
                    !CafeStockTargetTypes.IsValid(line.StockTargetType))
                {
                    throw new InventoryValidationException(
                        "CafeInventory.CountLineInvalid",
                        "Count lines require typed targets and non-negative expected/counted quantities.");
                }

                TargetSnapshot target = ReadTarget(connection, transaction, line.StockTargetType, line.StockTargetId)
                    ?? throw new InventoryValidationException(
                        "CafeInventory.TargetMissing",
                        "Count stock target does not exist.");

                long current = ReadStock(connection, transaction, target);
                if (current != line.ExpectedBeforeX1000)
                {
                    throw new InventoryValidationException(
                        "CafeInventory.CountCaptureStale",
                        "Count expected-before value does not match current local stock.");
                }

                string key = target.TargetType + ":" + target.Id.ToString("D");
                if (!targets.Add(key))
                {
                    throw new InventoryValidationException(
                        "CafeInventory.CountDuplicateTarget",
                        "Count session cannot contain the same target twice.");
                }

                resolved.Add(new CountDraftLine(
                    line.LineId.GetValueOrDefault() == Guid.Empty ? Guid.NewGuid() : line.LineId!.Value,
                    target,
                    line.ExpectedBeforeX1000,
                    line.CountedX1000,
                    checked(line.CountedX1000 - line.ExpectedBeforeX1000)));
            }

            if (state is null)
            {
                using SqliteCommand session = connection.CreateCommand();
                session.Transaction = transaction;
                session.CommandText = """
                    INSERT INTO inventory_count_sessions(
                        count_session_id,store_id,state,started_by_user_id,started_at_utc,
                        finalized_by_user_id,finalized_at_utc,notes,
                        finalize_operation_id,finalized_content_hash)
                    VALUES($id,$store,'Draft',$user,$at,NULL,NULL,$notes,NULL,NULL);
                    """;
                session.Parameters.AddWithValue("$id", mutation.CountSessionId.ToString("D"));
                session.Parameters.AddWithValue("$store", mutation.StoreId.ToString("D"));
                session.Parameters.AddWithValue("$user", mutation.StartedByUserId.ToString("D"));
                session.Parameters.AddWithValue("$at", mutation.StartedAtUtc.ToUniversalTime().ToString("O"));
                session.Parameters.AddWithValue("$notes", Db(Clean(mutation.Notes)));
                session.ExecuteNonQuery();
            }
            else
            {
                using SqliteCommand session = connection.CreateCommand();
                session.Transaction = transaction;
                session.CommandText = """
                    UPDATE inventory_count_sessions
                    SET notes=$notes
                    WHERE count_session_id=$id AND state='Draft';
                    """;
                session.Parameters.AddWithValue("$notes", Db(Clean(mutation.Notes)));
                session.Parameters.AddWithValue("$id", mutation.CountSessionId.ToString("D"));
                if (session.ExecuteNonQuery() != 1)
                {
                    throw new InvalidOperationException("Count draft update lost ownership.");
                }

                using SqliteCommand clear = connection.CreateCommand();
                clear.Transaction = transaction;
                clear.CommandText = """
                    DELETE FROM inventory_count_lines
                    WHERE count_session_id=$id;
                    """;
                clear.Parameters.AddWithValue("$id", mutation.CountSessionId.ToString("D"));
                clear.ExecuteNonQuery();
            }

            foreach (CountDraftLine line in resolved)
            {
                InsertCountLine(connection, transaction, mutation.CountSessionId, line);
            }

            InventoryCountSession result = ReadCountSession(
                connection, transaction, mutation.CountSessionId)
                ?? throw new InvalidOperationException("Saved count draft could not be re-read.");

            transaction.Commit();
            return Result<InventoryCountSession>.Success(result);
        }
        catch (InventoryValidationException ex)
        {
            return Result<InventoryCountSession>.Fail(Error.Conflict(ex.Code, ex.Message));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or OverflowException)
        {
            return Result<InventoryCountSession>.Fail(
                Error.Unexpected("CafeInventory.CountDraftSaveFailed", ex.Message));
        }
    }

    public Result<CafeInventoryCommitResult<InventoryCountSession>> FinalizeCount(
        InventoryCountFinalizeMutation mutation)
    {
        if (mutation.OperationId == Guid.Empty ||
            mutation.DeviceId == Guid.Empty ||
            mutation.StoreId == Guid.Empty ||
            mutation.CountSessionId == Guid.Empty ||
            mutation.FinalizedByUserId == Guid.Empty)
        {
            return Result<CafeInventoryCommitResult<InventoryCountSession>>.Fail(
                Error.Validation(
                    "CafeInventory.CountFinalizeInvalid",
                    "Count finalization requires operation/device/store/session/finalizer identities."));
        }

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Result authority = ValidateEnvelopeAuthority(
                connection, transaction, mutation.StoreId, mutation.FinalizedByUserId);
            if (authority.IsFailure)
            {
                transaction.Rollback();
                return Result<CafeInventoryCommitResult<InventoryCountSession>>.Fail(authority.ErrorDetail);
            }

            InventoryCountSession session = ReadCountSession(
                connection, transaction, mutation.CountSessionId)
                ?? throw new InventoryValidationException(
                    "CafeInventory.CountMissing",
                    "Count session does not exist.");

            if (string.Equals(session.State, CafeInventoryCountStates.Finalized, StringComparison.Ordinal))
            {
                bool same =
                    session.FinalizeOperationId == mutation.OperationId &&
                    session.FinalizedByUserId == mutation.FinalizedByUserId &&
                    session.FinalizedAtUtc?.ToUniversalTime() == mutation.FinalizedAtUtc.ToUniversalTime();
                transaction.Commit();
                return same
                    ? Result<CafeInventoryCommitResult<InventoryCountSession>>.Success(
                        new CafeInventoryCommitResult<InventoryCountSession>(session, true))
                    : Result<CafeInventoryCommitResult<InventoryCountSession>>.Fail(
                        Error.Conflict(
                            "CafeInventory.CountAlreadyFinalized",
                            "Count session has already been finalized by different evidence."));
            }

            List<(InventoryCountLine Line, TargetSnapshot Target)> resolved = new();
            foreach (InventoryCountLine line in session.Lines)
            {
                TargetSnapshot target = ReadTarget(
                    connection, transaction, line.StockTargetType, line.StockTargetId)
                    ?? throw new InventoryValidationException(
                        "CafeInventory.TargetMissing",
                        "Count target no longer exists.");

                if (!string.Equals(target.Name, line.NameSnapshot, StringComparison.Ordinal) ||
                    !string.Equals(target.StockUnit, line.StockUnitSnapshot, StringComparison.Ordinal))
                {
                    throw new InventoryValidationException(
                        "CafeInventory.CountTargetChanged",
                        "Count target identity/unit snapshot changed before finalization.");
                }

                long current = ReadStock(connection, transaction, target);
                if (current != line.ExpectedBeforeX1000)
                {
                    throw new InventoryValidationException(
                        "CafeInventory.CountStale",
                        "Stock changed after count capture; count must be restarted or refreshed.");
                }

                resolved.Add((line, target));
            }

            InventoryCountFinalizedV1 payload = new(
                session.CountSessionId,
                mutation.FinalizedByUserId,
                mutation.FinalizedAtUtc.ToUniversalTime(),
                resolved.Select(x => new CafeInventoryTargetDeltaV1(
                    x.Line.LineId,
                    x.Target.TargetType,
                    x.Target.Id,
                    x.Line.NameSnapshot,
                    x.Line.StockUnitSnapshot,
                    x.Line.QuantityDeltaX1000,
                    ExpectedBeforeX1000: x.Line.ExpectedBeforeX1000,
                    CountedX1000: x.Line.CountedX1000))
                .ToArray());

            string payloadJson = CafeInventoryOperationCanonicalJson.Serialize(payload);
            string contentHash = CafeInventoryOperationCanonicalJson.ComputeHash(payloadJson);

            foreach ((InventoryCountLine line, TargetSnapshot target) in resolved)
            {
                if (line.QuantityDeltaX1000 == 0)
                {
                    continue;
                }

                ApplyStockDelta(
                    connection,
                    transaction,
                    target,
                    line.QuantityDeltaX1000,
                    CafeInventoryMovementKinds.CountCorrection,
                    "InventoryCount",
                    session.CountSessionId,
                    line.LineId,
                    null,
                    mutation.FinalizedByUserId,
                    mutation.FinalizedAtUtc,
                    null,
                    "Physical count finalization");
            }

            using (SqliteCommand finalize = connection.CreateCommand())
            {
                finalize.Transaction = transaction;
                finalize.CommandText = """
                    UPDATE inventory_count_sessions
                    SET state='Finalized',
                        finalized_by_user_id=$user,
                        finalized_at_utc=$at,
                        finalize_operation_id=$operation,
                        finalized_content_hash=$hash
                    WHERE count_session_id=$id AND state='Draft';
                    """;
                finalize.Parameters.AddWithValue("$user", mutation.FinalizedByUserId.ToString("D"));
                finalize.Parameters.AddWithValue("$at", mutation.FinalizedAtUtc.ToUniversalTime().ToString("O"));
                finalize.Parameters.AddWithValue("$operation", mutation.OperationId.ToString("D"));
                finalize.Parameters.AddWithValue("$hash", contentHash);
                finalize.Parameters.AddWithValue("$id", mutation.CountSessionId.ToString("D"));

                if (finalize.ExecuteNonQuery() != 1)
                {
                    throw new InvalidOperationException("Count finalization lost draft ownership.");
                }
            }

            _outbox.Enqueue(
                connection,
                transaction,
                new SyncEnqueueRequest(
                    mutation.DeviceId,
                    mutation.StoreId,
                    mutation.FinalizedByUserId,
                    CafeInventoryOperationContract.InventoryCountFinalizedV1,
                    CafeInventoryOperationContract.SchemaVersion,
                    mutation.CountSessionId,
                    null,
                    mutation.FinalizedAtUtc,
                    payloadJson,
                    mutation.OperationId));

            InventoryCountSession finalized = ReadCountSession(
                connection, transaction, mutation.CountSessionId)
                ?? throw new InvalidOperationException("Finalized count could not be re-read.");

            transaction.Commit();
            return Result<CafeInventoryCommitResult<InventoryCountSession>>.Success(
                new CafeInventoryCommitResult<InventoryCountSession>(finalized, false));
        }
        catch (InventoryValidationException ex)
        {
            return Result<CafeInventoryCommitResult<InventoryCountSession>>.Fail(
                Error.Conflict(ex.Code, ex.Message));
        }
        catch (SqliteException ex) when (IsUniqueViolation(ex))
        {
            return Result<CafeInventoryCommitResult<InventoryCountSession>>.Fail(
                Error.Conflict("CafeInventory.CountFinalizeConflict", ex.Message));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or OverflowException)
        {
            return Result<CafeInventoryCommitResult<InventoryCountSession>>.Fail(
                Error.Unexpected("CafeInventory.CountFinalizeFailed", ex.Message));
        }
    }

    public Result<IReadOnlyList<CafeLowStockItem>> GetLowStock()
    {
        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            List<CafeLowStockItem> items = new();

            using (SqliteCommand ingredients = connection.CreateCommand())
            {
                ingredients.CommandText = """
                    SELECT id,name,stock_unit,stock_on_hand_x1000,reorder_level_x1000
                    FROM ingredients
                    WHERE is_active=1
                      AND reorder_level_x1000 > 0
                      AND stock_on_hand_x1000 <= reorder_level_x1000;
                    """;
                using SqliteDataReader reader = ingredients.ExecuteReader();
                while (reader.Read())
                {
                    items.Add(new CafeLowStockItem(
                        CafeStockTargetTypes.Ingredient,
                        Guid.Parse(reader.GetString(0)),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetInt64(3),
                        reader.GetInt64(4)));
                }
            }

            using (SqliteCommand products = connection.CreateCommand())
            {
                products.CommandText = """
                    SELECT p.id,p.name,u.name,p.stock_on_hand_x1000,p.reorder_level_x1000
                    FROM products p
                    INNER JOIN units u ON u.id=p.unit_id
                    WHERE p.is_active=1
                      AND p.track_stock=1
                      AND p.reorder_level_x1000 > 0
                      AND p.stock_on_hand_x1000 <= p.reorder_level_x1000;
                    """;
                using SqliteDataReader reader = products.ExecuteReader();
                while (reader.Read())
                {
                    items.Add(new CafeLowStockItem(
                        CafeStockTargetTypes.Product,
                        Guid.Parse(reader.GetString(0)),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetInt64(3),
                        reader.GetInt64(4)));
                }
            }

            return Result<IReadOnlyList<CafeLowStockItem>>.Success(
                items
                    .OrderBy(x => x.StockTargetType, StringComparer.Ordinal)
                    .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(x => x.StockTargetId)
                    .ToArray());
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            return Result<IReadOnlyList<CafeLowStockItem>>.Fail(
                Error.Unexpected("CafeInventory.LowStockReadFailed", ex.Message));
        }
    }

    public Result<PurchaseReceiptEvidence?> GetPurchase(Guid purchaseReceiptId)
    {
        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();
            PurchaseReceiptEvidence? result = ReadPurchaseById(connection, transaction, purchaseReceiptId);
            transaction.Commit();
            return Result<PurchaseReceiptEvidence?>.Success(result);
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            return Result<PurchaseReceiptEvidence?>.Fail(
                Error.Unexpected("CafeInventory.PurchaseReadFailed", ex.Message));
        }
    }

    public Result<InventoryCountSession?> GetCount(Guid countSessionId)
    {
        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();
            InventoryCountSession? result = ReadCountSession(connection, transaction, countSessionId);
            transaction.Commit();
            return Result<InventoryCountSession?>.Success(result);
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            return Result<InventoryCountSession?>.Fail(
                Error.Unexpected("CafeInventory.CountReadFailed", ex.Message));
        }
    }

    public static Result ApplyCanonicalPurchase(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId,
        Guid operationId,
        PurchaseReceiptCommittedV1 payload)
    {
        if (ReadPurchaseByOperation(connection, transaction, operationId) is PurchaseReceiptEvidence existing)
        {
            string json = CafeInventoryOperationCanonicalJson.Serialize(payload);
            return string.Equals(
                    existing.ContentHash,
                    CafeInventoryOperationCanonicalJson.ComputeHash(json),
                    StringComparison.Ordinal)
                ? Result.Success()
                : Result.Fail(Error.Conflict(
                    "CafeInventory.RemotePurchaseConflict",
                    "Purchase operation exists with different immutable content."));
        }

        if (!ExistsById(connection, transaction, "suppliers", "id", payload.SupplierId))
        {
            using SqliteCommand supplier = connection.CreateCommand();
            supplier.Transaction = transaction;
            supplier.CommandText = """
                INSERT INTO suppliers(id,name,contact_number,notes,is_active)
                VALUES($id,$name,NULL,'Created from accepted purchase supplier snapshot',0);
                """;
            supplier.Parameters.AddWithValue("$id", payload.SupplierId.ToString("D"));
            supplier.Parameters.AddWithValue("$name", payload.SupplierNameSnapshot);
            supplier.ExecuteNonQuery();
        }

        List<ResolvedTargetLine> lines = new();
        foreach (CafeInventoryTargetDeltaV1 line in payload.Lines)
        {
            TargetSnapshot? target = ReadTarget(
                connection,
                transaction,
                line.StockTargetType,
                line.StockTargetId);

            if (target is null)
            {
                return Result.Fail(Error.Conflict(
                    "CafeInventory.RemoteTargetMissing",
                    "Purchase target prerequisite is missing."));
            }

            if (!string.Equals(target.Name, line.NameSnapshot, StringComparison.Ordinal) ||
                !string.Equals(target.StockUnit, line.StockUnitSnapshot, StringComparison.Ordinal))
            {
                return Result.Fail(Error.Conflict(
                    "CafeInventory.RemoteTargetSnapshotConflict",
                    "Purchase target name/unit contradicts local prerequisite."));
            }

            lines.Add(new ResolvedTargetLine(
                line.LineId,
                target,
                line.QuantityDeltaX1000,
                line.AcquisitionCostCents,
                line.PurchasePackageLabel,
                line.PurchasePackageQuantityX1000,
                line.PurchasePackageContentX1000));
        }

        try
        {
            EnsureResultingStockNonNegative(connection, transaction, lines.Select(x => (x.Target, x.QuantityDeltaX1000)));
        }
        catch (InventoryValidationException ex)
        {
            return Result.Fail(Error.Conflict(ex.Code, ex.Message));
        }

        string payloadJson = CafeInventoryOperationCanonicalJson.Serialize(payload);
        string contentHash = CafeInventoryOperationCanonicalJson.ComputeHash(payloadJson);
        InsertPurchaseCanonical(connection, transaction, storeId, operationId, payload, lines, contentHash);

        foreach (ResolvedTargetLine line in lines)
        {
            ApplyStockDelta(
                connection,
                transaction,
                line.Target,
                line.QuantityDeltaX1000,
                payload.CorrectionOfPurchaseReceiptId.HasValue
                    ? CafeInventoryMovementKinds.PurchaseCorrection
                    : CafeInventoryMovementKinds.PurchaseReceiving,
                "PurchaseReceipt",
                payload.PurchaseReceiptId,
                line.LineId,
                payload.CorrectionOfPurchaseReceiptId,
                payload.ActorUserId,
                payload.ReceivedAtUtc,
                line.AcquisitionCostCents,
                payload.CorrectionOfPurchaseReceiptId.HasValue ? "Purchase correction" : "Purchase receiving");
        }

        return Result.Success();
    }

    public static Result ApplyCanonicalWaste(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId,
        Guid operationId,
        InventoryWasteCommittedV1 payload)
    {
        if (ReadWasteByOperation(connection, transaction, operationId) is InventoryWasteEvidence existing)
        {
            string json = CafeInventoryOperationCanonicalJson.Serialize(payload);
            return string.Equals(
                    existing.ContentHash,
                    CafeInventoryOperationCanonicalJson.ComputeHash(json),
                    StringComparison.Ordinal)
                ? Result.Success()
                : Result.Fail(Error.Conflict(
                    "CafeInventory.RemoteWasteConflict",
                    "Waste operation exists with different immutable content."));
        }

        List<ResolvedTargetLine> lines = new();
        foreach (CafeInventoryTargetDeltaV1 line in payload.Lines)
        {
            TargetSnapshot? target = ReadTarget(
                connection,
                transaction,
                line.StockTargetType,
                line.StockTargetId);

            if (target is null)
            {
                return Result.Fail(Error.Conflict(
                    "CafeInventory.RemoteTargetMissing",
                    "Waste target prerequisite is missing."));
            }

            if (!string.Equals(target.Name, line.NameSnapshot, StringComparison.Ordinal) ||
                !string.Equals(target.StockUnit, line.StockUnitSnapshot, StringComparison.Ordinal))
            {
                return Result.Fail(Error.Conflict(
                    "CafeInventory.RemoteTargetSnapshotConflict",
                    "Waste target name/unit contradicts local prerequisite."));
            }

            lines.Add(new ResolvedTargetLine(line.LineId, target, line.QuantityDeltaX1000, null, null, null, null));
        }

        try
        {
            EnsureResultingStockNonNegative(connection, transaction, lines.Select(x => (x.Target, x.QuantityDeltaX1000)));
        }
        catch (InventoryValidationException ex)
        {
            return Result.Fail(Error.Conflict(ex.Code, ex.Message));
        }

        string jsonPayload = CafeInventoryOperationCanonicalJson.Serialize(payload);
        string hash = CafeInventoryOperationCanonicalJson.ComputeHash(jsonPayload);
        InsertWasteCanonical(connection, transaction, storeId, operationId, payload, lines, hash);

        foreach (ResolvedTargetLine line in lines)
        {
            ApplyStockDelta(
                connection, transaction, line.Target, line.QuantityDeltaX1000,
                payload.CorrectionOfWasteEventId.HasValue
                    ? CafeInventoryMovementKinds.WasteCorrection
                    : CafeInventoryMovementKinds.Waste,
                "InventoryWaste", payload.WasteEventId, line.LineId,
                payload.CorrectionOfWasteEventId, payload.ActorUserId, payload.OccurredAtUtc,
                null, payload.Reason);
        }

        return Result.Success();
    }

    public static Result ApplyCanonicalAdjustment(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId,
        Guid operationId,
        InventoryAdjustmentCommittedV1 payload)
    {
        if (ReadAdjustmentByOperation(connection, transaction, operationId) is InventoryAdjustmentEvidence existing)
        {
            string json = CafeInventoryOperationCanonicalJson.Serialize(payload);
            return string.Equals(
                    existing.ContentHash,
                    CafeInventoryOperationCanonicalJson.ComputeHash(json),
                    StringComparison.Ordinal)
                ? Result.Success()
                : Result.Fail(Error.Conflict(
                    "CafeInventory.RemoteAdjustmentConflict",
                    "Adjustment operation exists with different immutable content."));
        }

        TargetSnapshot? target = ReadTarget(
            connection,
            transaction,
            payload.StockTargetType,
            payload.StockTargetId);

        if (target is null)
        {
            return Result.Fail(Error.Conflict(
                "CafeInventory.RemoteTargetMissing",
                "Adjustment target prerequisite is missing."));
        }

        if (!string.Equals(target.Name, payload.NameSnapshot, StringComparison.Ordinal) ||
            !string.Equals(target.StockUnit, payload.StockUnitSnapshot, StringComparison.Ordinal))
        {
            return Result.Fail(Error.Conflict(
                "CafeInventory.RemoteTargetSnapshotConflict",
                "Adjustment target name/unit contradicts local prerequisite."));
        }

        try
        {
            EnsureResultingStockNonNegative(
                connection, transaction, new[] { (target, payload.QuantityDeltaX1000) });
        }
        catch (InventoryValidationException ex)
        {
            return Result.Fail(Error.Conflict(ex.Code, ex.Message));
        }

        string jsonPayload = CafeInventoryOperationCanonicalJson.Serialize(payload);
        string hash = CafeInventoryOperationCanonicalJson.ComputeHash(jsonPayload);

        using (SqliteCommand insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO inventory_adjustment_events(
                    adjustment_event_id,operation_id,store_id,
                    stock_target_type,stock_target_id,name_snapshot,stock_unit_snapshot,
                    quantity_delta_x1000,reason,reference_type,reference_id,
                    correction_of_reference_id,actor_user_id,occurred_at_utc,
                    content_hash,created_at_utc)
                VALUES(
                    $id,$operation,$store,$type,$target,$name,$unit,$delta,$reason,
                    $referenceType,$referenceId,$correction,$actor,$occurred,$hash,$created);
                """;
            insert.Parameters.AddWithValue("$id", payload.AdjustmentEventId.ToString("D"));
            insert.Parameters.AddWithValue("$operation", operationId.ToString("D"));
            insert.Parameters.AddWithValue("$store", storeId.ToString("D"));
            insert.Parameters.AddWithValue("$type", target.TargetType);
            insert.Parameters.AddWithValue("$target", target.Id.ToString("D"));
            insert.Parameters.AddWithValue("$name", payload.NameSnapshot);
            insert.Parameters.AddWithValue("$unit", payload.StockUnitSnapshot);
            insert.Parameters.AddWithValue("$delta", payload.QuantityDeltaX1000);
            insert.Parameters.AddWithValue("$reason", payload.Reason);
            insert.Parameters.AddWithValue("$referenceType", Db(payload.ReferenceType));
            insert.Parameters.AddWithValue("$referenceId", Db(payload.ReferenceId));
            insert.Parameters.AddWithValue("$correction", Db(payload.CorrectionOfReferenceId));
            insert.Parameters.AddWithValue("$actor", payload.ActorUserId.ToString("D"));
            insert.Parameters.AddWithValue("$occurred", payload.OccurredAtUtc.ToUniversalTime().ToString("O"));
            insert.Parameters.AddWithValue("$hash", hash);
            insert.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
            insert.ExecuteNonQuery();
        }

        ApplyStockDelta(
            connection, transaction, target, payload.QuantityDeltaX1000,
            CafeInventoryMovementKinds.ManualAdjustment,
            payload.ReferenceType ?? "InventoryAdjustment",
            payload.ReferenceId ?? payload.AdjustmentEventId,
            payload.AdjustmentEventId,
            payload.CorrectionOfReferenceId,
            payload.ActorUserId,
            payload.OccurredAtUtc,
            null,
            payload.Reason);

        return Result.Success();
    }

    public static Result ApplyCanonicalCount(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId,
        Guid operationId,
        InventoryCountFinalizedV1 payload)
    {
        InventoryCountSession? existing = ReadCountSession(connection, transaction, payload.CountSessionId);
        if (existing is not null && string.Equals(existing.State, CafeInventoryCountStates.Finalized, StringComparison.Ordinal))
        {
            string hash = CafeInventoryOperationCanonicalJson.ComputeHash(
                CafeInventoryOperationCanonicalJson.Serialize(payload));
            return existing.FinalizeOperationId == operationId &&
                   string.Equals(existing.FinalizedContentHash, hash, StringComparison.Ordinal)
                ? Result.Success()
                : Result.Fail(Error.Conflict(
                    "CafeInventory.RemoteCountConflict",
                    "Finalized count exists with different immutable evidence."));
        }

        List<(CafeInventoryTargetDeltaV1 Line, TargetSnapshot Target)> resolved = new();
        foreach (CafeInventoryTargetDeltaV1 line in payload.Lines)
        {
            TargetSnapshot? target = ReadTarget(
                connection,
                transaction,
                line.StockTargetType,
                line.StockTargetId);

            if (target is null)
            {
                return Result.Fail(Error.Conflict(
                    "CafeInventory.RemoteTargetMissing",
                    "Count target prerequisite is missing."));
            }

            if (!string.Equals(target.Name, line.NameSnapshot, StringComparison.Ordinal) ||
                !string.Equals(target.StockUnit, line.StockUnitSnapshot, StringComparison.Ordinal))
            {
                return Result.Fail(Error.Conflict(
                    "CafeInventory.RemoteTargetSnapshotConflict",
                    "Count target name/unit contradicts local prerequisite."));
            }

            long current = ReadStock(connection, transaction, target);
            if (!line.ExpectedBeforeX1000.HasValue || current != line.ExpectedBeforeX1000.Value)
            {
                return Result.Fail(Error.Conflict(
                    "CafeInventory.CountExpectedBeforeMismatch",
                    "Central/local count prerequisite does not match ExpectedBefore evidence."));
            }

            resolved.Add((line, target));
        }

        string jsonPayload = CafeInventoryOperationCanonicalJson.Serialize(payload);
        string contentHash = CafeInventoryOperationCanonicalJson.ComputeHash(jsonPayload);

        if (existing is null)
        {
            using SqliteCommand session = connection.CreateCommand();
            session.Transaction = transaction;
            session.CommandText = """
                INSERT INTO inventory_count_sessions(
                    count_session_id,store_id,state,started_by_user_id,started_at_utc,
                    finalized_by_user_id,finalized_at_utc,notes,
                    finalize_operation_id,finalized_content_hash)
                VALUES(
                    $id,$store,'Finalized',$user,$at,
                    $user,$at,'Projected accepted count',
                    $operation,$hash);
                """;
            session.Parameters.AddWithValue("$id", payload.CountSessionId.ToString("D"));
            session.Parameters.AddWithValue("$store", storeId.ToString("D"));
            session.Parameters.AddWithValue("$user", payload.FinalizedByUserId.ToString("D"));
            session.Parameters.AddWithValue("$at", payload.FinalizedAtUtc.ToUniversalTime().ToString("O"));
            session.Parameters.AddWithValue("$operation", operationId.ToString("D"));
            session.Parameters.AddWithValue("$hash", contentHash);
            session.ExecuteNonQuery();

            foreach ((CafeInventoryTargetDeltaV1 line, TargetSnapshot target) in resolved)
            {
                InsertCountLine(
                    connection,
                    transaction,
                    payload.CountSessionId,
                    new CountDraftLine(
                        line.LineId,
                        target,
                        line.ExpectedBeforeX1000!.Value,
                        line.CountedX1000!.Value,
                        line.QuantityDeltaX1000));
            }
        }
        else
        {
            return Result.Fail(Error.Conflict(
                "CafeInventory.RemoteCountDraftConflict",
                "Remote accepted count conflicts with an existing local draft session."));
        }

        foreach ((CafeInventoryTargetDeltaV1 line, TargetSnapshot target) in resolved)
        {
            if (line.QuantityDeltaX1000 == 0)
            {
                continue;
            }

            ApplyStockDelta(
                connection, transaction, target, line.QuantityDeltaX1000,
                CafeInventoryMovementKinds.CountCorrection,
                "InventoryCount", payload.CountSessionId, line.LineId, null,
                payload.FinalizedByUserId, payload.FinalizedAtUtc, null,
                "Physical count finalization");
        }

        return Result.Success();
    }

    public static bool VerifyCanonicalFact(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid sourceOperationId,
        string operationType,
        string payloadJson)
    {
        string expected = CafeInventoryOperationCanonicalJson.ComputeHash(payloadJson);

        return operationType switch
        {
            CafeInventoryOperationContract.PurchaseReceiptCommittedV1 =>
                ReadPurchaseByOperation(connection, transaction, sourceOperationId) is { } p &&
                string.Equals(p.ContentHash, expected, StringComparison.Ordinal),

            CafeInventoryOperationContract.InventoryWasteCommittedV1 =>
                ReadWasteByOperation(connection, transaction, sourceOperationId) is { } w &&
                string.Equals(w.ContentHash, expected, StringComparison.Ordinal),

            CafeInventoryOperationContract.InventoryAdjustmentCommittedV1 =>
                ReadAdjustmentByOperation(connection, transaction, sourceOperationId) is { } a &&
                string.Equals(a.ContentHash, expected, StringComparison.Ordinal),

            CafeInventoryOperationContract.InventoryCountFinalizedV1 =>
                ReadCountByOperation(connection, transaction, sourceOperationId) is { } c &&
                string.Equals(c.FinalizedContentHash, expected, StringComparison.Ordinal),

            _ => false
        };
    }

    private static string? ValidatePurchaseMutation(PurchaseReceiptMutation mutation)
    {
        if (mutation.OperationId == Guid.Empty ||
            mutation.PurchaseReceiptId == Guid.Empty ||
            mutation.DeviceId == Guid.Empty ||
            mutation.StoreId == Guid.Empty ||
            mutation.SupplierId == Guid.Empty ||
            mutation.ActorUserId == Guid.Empty ||
            mutation.Lines is null ||
            mutation.Lines.Count == 0)
        {
            return "Purchase requires operation/receipt/device/store/supplier/actor identities and lines.";
        }

        foreach (PurchaseReceiptLineMutation line in mutation.Lines)
        {
            if (line.StockTargetId == Guid.Empty ||
                line.QuantityReceivedX1000 == 0 ||
                !CafeStockTargetTypes.IsValid(line.StockTargetType))
            {
                return "Purchase lines require typed targets and non-zero quantities.";
            }

            if (!mutation.CorrectionOfPurchaseReceiptId.HasValue &&
                (line.QuantityReceivedX1000 <= 0 || line.LineAcquisitionCostCents < 0))
            {
                return "Normal purchase receiving requires positive quantity and non-negative acquisition cost.";
            }

            bool packageAny =
                line.PurchasePackageQuantityX1000.HasValue ||
                line.PurchasePackageContentX1000.HasValue ||
                !string.IsNullOrWhiteSpace(line.PurchasePackageLabel);

            if (packageAny &&
                (!line.PurchasePackageQuantityX1000.HasValue ||
                 !line.PurchasePackageContentX1000.HasValue ||
                 string.IsNullOrWhiteSpace(line.PurchasePackageLabel)))
            {
                return "Package conversion requires label, package quantity and package content together.";
            }
        }

        return null;
    }

    private static long ValidateAndNormalizePurchaseQuantity(
        PurchaseReceiptLineMutation line,
        TargetSnapshot target,
        bool correction)
    {
        long quantity = line.QuantityReceivedX1000;

        if (line.PurchasePackageQuantityX1000.HasValue &&
            line.PurchasePackageContentX1000.HasValue)
        {
            if (!string.Equals(target.TargetType, CafeStockTargetTypes.Ingredient, StringComparison.Ordinal))
            {
                throw new InventoryValidationException(
                    "CafeInventory.PackageConversionTargetInvalid",
                    "Package-to-canonical conversion is only accepted for Ingredient targets.");
            }

            long packageQuantity = line.PurchasePackageQuantityX1000.Value;
            long packageContent = line.PurchasePackageContentX1000.Value;
            if (packageQuantity <= 0 || packageContent <= 0)
            {
                throw new InventoryValidationException(
                    "CafeInventory.PackageConversionInvalid",
                    "Package quantity/content must be positive.");
            }

            long numerator = checked(packageQuantity * packageContent);
            if (numerator % 1000L != 0)
            {
                throw new InventoryValidationException(
                    "CafeInventory.PackageConversionRemainder",
                    "Package conversion must resolve exactly to canonical x1000 quantity.");
            }

            long normalized = numerator / 1000L;
            if (!correction && quantity != normalized)
            {
                throw new InventoryValidationException(
                    "CafeInventory.PackageConversionMismatch",
                    "Received ingredient quantity does not match declared package conversion.");
            }

            if (correction && Math.Abs(quantity) != normalized)
            {
                throw new InventoryValidationException(
                    "CafeInventory.PackageCorrectionMismatch",
                    "Correction quantity magnitude does not match declared package conversion.");
            }
        }

        return quantity;
    }

    private static Result ValidateEnvelopeAuthority(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId,
        Guid actorUserId)
    {
        Guid localStore = ReadStoreId(connection, transaction);
        if (localStore != storeId)
        {
            return Result.Fail(Error.Conflict(
                "CafeInventory.StoreMismatch",
                "Operation StoreId does not match local deployment authority."));
        }

        if (!ExistsById(connection, transaction, "users", "id", actorUserId))
        {
            return Result.Fail(Error.NotFound(
                "CafeInventory.ActorMissing",
                "Operation actor user does not exist."));
        }

        return Result.Success();
    }

    private static void InsertPurchase(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PurchaseReceiptMutation mutation,
        SupplierRow supplier,
        IReadOnlyList<ResolvedTargetLine> lines,
        string contentHash)
    {
        InsertPurchaseCanonical(
            connection,
            transaction,
            mutation.StoreId,
            mutation.OperationId,
            new PurchaseReceiptCommittedV1(
                mutation.PurchaseReceiptId,
                mutation.SupplierId,
                supplier.Name,
                Clean(mutation.ExternalReference),
                mutation.ReceivedAtUtc.ToUniversalTime(),
                mutation.ActorUserId,
                Clean(mutation.Notes),
                lines.Sum(x => x.AcquisitionCostCents ?? 0L),
                mutation.CorrectionOfPurchaseReceiptId,
                lines.Select(x => new CafeInventoryTargetDeltaV1(
                    x.LineId,
                    x.Target.TargetType,
                    x.Target.Id,
                    x.Target.Name,
                    x.Target.StockUnit,
                    x.QuantityDeltaX1000,
                    x.AcquisitionCostCents,
                    x.PurchasePackageLabel,
                    x.PurchasePackageQuantityX1000,
                    x.PurchasePackageContentX1000)).ToArray()),
            lines,
            contentHash);
    }

    private static void InsertPurchaseCanonical(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId,
        Guid operationId,
        PurchaseReceiptCommittedV1 payload,
        IReadOnlyList<ResolvedTargetLine> lines,
        string contentHash)
    {
        using (SqliteCommand header = connection.CreateCommand())
        {
            header.Transaction = transaction;
            header.CommandText = """
                INSERT INTO purchase_receipts(
                    purchase_receipt_id,operation_id,store_id,supplier_id,supplier_name_snapshot,
                    external_reference,received_at_utc,actor_user_id,notes,
                    total_acquisition_cost_cents,content_hash,correction_of_purchase_receipt_id,
                    created_at_utc)
                VALUES(
                    $id,$operation,$store,$supplier,$supplierName,
                    $reference,$received,$actor,$notes,
                    $total,$hash,$correction,$created);
                """;
            header.Parameters.AddWithValue("$id", payload.PurchaseReceiptId.ToString("D"));
            header.Parameters.AddWithValue("$operation", operationId.ToString("D"));
            header.Parameters.AddWithValue("$store", storeId.ToString("D"));
            header.Parameters.AddWithValue("$supplier", payload.SupplierId.ToString("D"));
            header.Parameters.AddWithValue("$supplierName", payload.SupplierNameSnapshot);
            header.Parameters.AddWithValue("$reference", Db(payload.ExternalReference));
            header.Parameters.AddWithValue("$received", payload.ReceivedAtUtc.ToUniversalTime().ToString("O"));
            header.Parameters.AddWithValue("$actor", payload.ActorUserId.ToString("D"));
            header.Parameters.AddWithValue("$notes", Db(payload.Notes));
            header.Parameters.AddWithValue("$total", payload.TotalAcquisitionCostCents);
            header.Parameters.AddWithValue("$hash", contentHash);
            header.Parameters.AddWithValue("$correction", Db(payload.CorrectionOfPurchaseReceiptId));
            header.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
            header.ExecuteNonQuery();
        }

        foreach (ResolvedTargetLine line in lines)
        {
            using SqliteCommand insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO purchase_receipt_lines(
                    line_id,purchase_receipt_id,stock_target_type,stock_target_id,
                    name_snapshot,stock_unit_snapshot,quantity_received_x1000,
                    purchase_package_label,purchase_package_quantity_x1000,
                    purchase_package_content_x1000,line_acquisition_cost_cents)
                VALUES(
                    $line,$receipt,$type,$target,$name,$unit,$quantity,
                    $package,$packageQuantity,$packageContent,$cost);
                """;
            insert.Parameters.AddWithValue("$line", line.LineId.ToString("D"));
            insert.Parameters.AddWithValue("$receipt", payload.PurchaseReceiptId.ToString("D"));
            insert.Parameters.AddWithValue("$type", line.Target.TargetType);
            insert.Parameters.AddWithValue("$target", line.Target.Id.ToString("D"));
            insert.Parameters.AddWithValue("$name", line.Target.Name);
            insert.Parameters.AddWithValue("$unit", line.Target.StockUnit);
            insert.Parameters.AddWithValue("$quantity", line.QuantityDeltaX1000);
            insert.Parameters.AddWithValue("$package", Db(line.PurchasePackageLabel));
            insert.Parameters.AddWithValue("$packageQuantity", Db(line.PurchasePackageQuantityX1000));
            insert.Parameters.AddWithValue("$packageContent", Db(line.PurchasePackageContentX1000));
            insert.Parameters.AddWithValue("$cost", line.AcquisitionCostCents ?? 0L);
            insert.ExecuteNonQuery();
        }
    }

    private static void InsertWaste(
        SqliteConnection connection,
        SqliteTransaction transaction,
        InventoryWasteMutation mutation,
        IReadOnlyList<ResolvedTargetLine> lines,
        string contentHash)
    {
        InsertWasteCanonical(
            connection,
            transaction,
            mutation.StoreId,
            mutation.OperationId,
            new InventoryWasteCommittedV1(
                mutation.WasteEventId,
                mutation.Reason,
                mutation.ActorUserId,
                mutation.OccurredAtUtc.ToUniversalTime(),
                Clean(mutation.Notes),
                mutation.CorrectionOfWasteEventId,
                lines.Select(x => new CafeInventoryTargetDeltaV1(
                    x.LineId,
                    x.Target.TargetType,
                    x.Target.Id,
                    x.Target.Name,
                    x.Target.StockUnit,
                    x.QuantityDeltaX1000)).ToArray()),
            lines,
            contentHash);
    }

    private static void InsertWasteCanonical(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid storeId,
        Guid operationId,
        InventoryWasteCommittedV1 payload,
        IReadOnlyList<ResolvedTargetLine> lines,
        string contentHash)
    {
        using (SqliteCommand header = connection.CreateCommand())
        {
            header.Transaction = transaction;
            header.CommandText = """
                INSERT INTO inventory_waste_events(
                    waste_event_id,operation_id,store_id,reason,actor_user_id,
                    occurred_at_utc,notes,content_hash,correction_of_waste_event_id,created_at_utc)
                VALUES($id,$operation,$store,$reason,$actor,$occurred,$notes,$hash,$correction,$created);
                """;
            header.Parameters.AddWithValue("$id", payload.WasteEventId.ToString("D"));
            header.Parameters.AddWithValue("$operation", operationId.ToString("D"));
            header.Parameters.AddWithValue("$store", storeId.ToString("D"));
            header.Parameters.AddWithValue("$reason", payload.Reason);
            header.Parameters.AddWithValue("$actor", payload.ActorUserId.ToString("D"));
            header.Parameters.AddWithValue("$occurred", payload.OccurredAtUtc.ToUniversalTime().ToString("O"));
            header.Parameters.AddWithValue("$notes", Db(payload.Notes));
            header.Parameters.AddWithValue("$hash", contentHash);
            header.Parameters.AddWithValue("$correction", Db(payload.CorrectionOfWasteEventId));
            header.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
            header.ExecuteNonQuery();
        }

        foreach (ResolvedTargetLine line in lines)
        {
            using SqliteCommand insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO inventory_waste_lines(
                    line_id,waste_event_id,stock_target_type,stock_target_id,
                    name_snapshot,stock_unit_snapshot,quantity_wasted_x1000)
                VALUES($line,$event,$type,$target,$name,$unit,$quantity);
                """;
            insert.Parameters.AddWithValue("$line", line.LineId.ToString("D"));
            insert.Parameters.AddWithValue("$event", payload.WasteEventId.ToString("D"));
            insert.Parameters.AddWithValue("$type", line.Target.TargetType);
            insert.Parameters.AddWithValue("$target", line.Target.Id.ToString("D"));
            insert.Parameters.AddWithValue("$name", line.Target.Name);
            insert.Parameters.AddWithValue("$unit", line.Target.StockUnit);
            insert.Parameters.AddWithValue("$quantity", Math.Abs(line.QuantityDeltaX1000));
            insert.ExecuteNonQuery();
        }
    }

    private static void ApplyStockDelta(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TargetSnapshot target,
        long delta,
        string ingredientMovementKind,
        string referenceType,
        Guid referenceId,
        Guid referenceLineId,
        Guid? correctionOfReferenceId,
        Guid actorUserId,
        DateTimeOffset occurredAtUtc,
        long? acquisitionCostCents,
        string reason)
    {
        if (delta == 0)
        {
            return;
        }

        if (string.Equals(target.TargetType, CafeStockTargetTypes.Ingredient, StringComparison.Ordinal))
        {
            using (SqliteCommand balance = connection.CreateCommand())
            {
                balance.Transaction = transaction;
                balance.CommandText = """
                    UPDATE ingredients
                    SET stock_on_hand_x1000=stock_on_hand_x1000+$delta,
                        updated_at_utc=$at
                    WHERE id=$id
                      AND stock_on_hand_x1000+$delta >= 0;
                    """;
                balance.Parameters.AddWithValue("$delta", delta);
                balance.Parameters.AddWithValue("$at", occurredAtUtc.ToUniversalTime().ToString("O"));
                balance.Parameters.AddWithValue("$id", target.Id.ToString("D"));
                if (balance.ExecuteNonQuery() != 1)
                {
                    throw new InventoryValidationException(
                        "CafeInventory.InsufficientIngredientStock",
                        "Ingredient stock cannot become negative.");
                }
            }

            using SqliteCommand movement = connection.CreateCommand();
            movement.Transaction = transaction;
            movement.CommandText = """
                INSERT INTO ingredient_stock_movements(
                    id,ingredient_id,sale_id,sale_item_id,
                    consumption_line_id,original_consumption_line_id,
                    reference_type,reference_id,reference_line_id,correction_of_reference_id,
                    movement_kind,quantity_delta_x1000,actor_user_id,created_at_utc)
                VALUES(
                    $id,$ingredient,NULL,NULL,NULL,NULL,
                    $referenceType,$referenceId,$referenceLine,$correction,
                    $kind,$delta,$actor,$at);
                """;
            movement.Parameters.AddWithValue("$id", DeriveMovementId(referenceId, referenceLineId, target.Id, ingredientMovementKind).ToString("D"));
            movement.Parameters.AddWithValue("$ingredient", target.Id.ToString("D"));
            movement.Parameters.AddWithValue("$referenceType", referenceType);
            movement.Parameters.AddWithValue("$referenceId", referenceId.ToString("D"));
            movement.Parameters.AddWithValue("$referenceLine", referenceLineId.ToString("D"));
            movement.Parameters.AddWithValue("$correction", Db(correctionOfReferenceId));
            movement.Parameters.AddWithValue("$kind", ingredientMovementKind);
            movement.Parameters.AddWithValue("$delta", delta);
            movement.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
            movement.Parameters.AddWithValue("$at", occurredAtUtc.ToUniversalTime().ToString("O"));
            movement.ExecuteNonQuery();
            return;
        }

        using (SqliteCommand balance = connection.CreateCommand())
        {
            balance.Transaction = transaction;
            balance.CommandText = """
                UPDATE products
                SET stock_on_hand_x1000=stock_on_hand_x1000+$delta,
                    updated_at=$at
                WHERE id=$id
                  AND track_stock=1
                  AND stock_on_hand_x1000+$delta >= 0;
                """;
            balance.Parameters.AddWithValue("$delta", delta);
            balance.Parameters.AddWithValue("$at", occurredAtUtc.ToUniversalTime().ToString("O"));
            balance.Parameters.AddWithValue("$id", target.Id.ToString("D"));
            if (balance.ExecuteNonQuery() != 1)
            {
                throw new InventoryValidationException(
                    "CafeInventory.InsufficientProductStock",
                    "Product stock cannot become negative.");
            }
        }

        StockMovementType movementType = ingredientMovementKind switch
        {
            CafeInventoryMovementKinds.PurchaseReceiving => StockMovementType.StockIn,
            CafeInventoryMovementKinds.PurchaseCorrection =>
                delta >= 0 ? StockMovementType.AdjustmentAdd : StockMovementType.AdjustmentRemove,
            CafeInventoryMovementKinds.Waste =>
                string.Equals(reason, CafeInventoryWasteReasons.Expired, StringComparison.Ordinal)
                    ? StockMovementType.Expired
                    : StockMovementType.Damage,
            CafeInventoryMovementKinds.WasteCorrection => StockMovementType.AdjustmentAdd,
            CafeInventoryMovementKinds.CountCorrection => StockMovementType.CountCorrection,
            _ => delta >= 0 ? StockMovementType.AdjustmentAdd : StockMovementType.AdjustmentRemove
        };

        using SqliteCommand movementCommand = connection.CreateCommand();
        movementCommand.Transaction = transaction;
        movementCommand.CommandText = """
            INSERT INTO stock_movements(
                id,product_id,movement_type,qty_change_x1000,cost_price_cents,
                reference_type,reference_id,reason,user_id,created_at)
            VALUES($id,$product,$type,$delta,$cost,$referenceType,$referenceId,$reason,$user,$at);
            """;
        movementCommand.Parameters.AddWithValue("$id", DeriveMovementId(referenceId, referenceLineId, target.Id, ingredientMovementKind).ToString("D"));
        movementCommand.Parameters.AddWithValue("$product", target.Id.ToString("D"));
        movementCommand.Parameters.AddWithValue("$type", Convert.ToInt32(movementType));
        movementCommand.Parameters.AddWithValue("$delta", delta);
        movementCommand.Parameters.AddWithValue("$cost", Db(acquisitionCostCents));
        movementCommand.Parameters.AddWithValue("$referenceType", referenceType);
        movementCommand.Parameters.AddWithValue("$referenceId", referenceId.ToString("D"));
        movementCommand.Parameters.AddWithValue("$reason", reason);
        movementCommand.Parameters.AddWithValue("$user", actorUserId.ToString("D"));
        movementCommand.Parameters.AddWithValue("$at", occurredAtUtc.ToUniversalTime().ToString("O"));
        movementCommand.ExecuteNonQuery();
    }

    private static TargetSnapshot? ReadTarget(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string targetType,
        Guid targetId)
    {
        if (string.Equals(targetType, CafeStockTargetTypes.Ingredient, StringComparison.Ordinal))
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT id,name,stock_unit,stock_on_hand_x1000,reorder_level_x1000,is_active
                FROM ingredients WHERE id=$id LIMIT 1;
                """;
            command.Parameters.AddWithValue("$id", targetId.ToString("D"));
            using SqliteDataReader reader = command.ExecuteReader();
            return reader.Read()
                ? new TargetSnapshot(
                    CafeStockTargetTypes.Ingredient,
                    Guid.Parse(reader.GetString(0)),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt64(3),
                    reader.GetInt64(4),
                    reader.GetInt32(5) == 1)
                : null;
        }

        if (string.Equals(targetType, CafeStockTargetTypes.Product, StringComparison.Ordinal))
        {
            using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                SELECT p.id,p.name,u.name,p.stock_on_hand_x1000,p.reorder_level_x1000,
                       p.is_active,p.track_stock
                FROM products p
                INNER JOIN units u ON u.id=p.unit_id
                WHERE p.id=$id
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$id", targetId.ToString("D"));
            using SqliteDataReader reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            if (reader.GetInt32(6) != 1)
            {
                throw new InventoryValidationException(
                    "CafeInventory.ProductNotStockTracked",
                    "Product target must have track_stock enabled.");
            }

            return new TargetSnapshot(
                CafeStockTargetTypes.Product,
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3),
                reader.GetInt64(4),
                reader.GetInt32(5) == 1);
        }

        return null;
    }

    private static long ReadStock(
        SqliteConnection connection,
        SqliteTransaction transaction,
        TargetSnapshot target)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = string.Equals(
                target.TargetType,
                CafeStockTargetTypes.Ingredient,
                StringComparison.Ordinal)
            ? "SELECT stock_on_hand_x1000 FROM ingredients WHERE id=$id;"
            : "SELECT stock_on_hand_x1000 FROM products WHERE id=$id;";
        command.Parameters.AddWithValue("$id", target.Id.ToString("D"));
        return Convert.ToInt64(
            command.ExecuteScalar()
            ?? throw new InvalidOperationException("Stock target disappeared."));
    }

    private static void EnsureResultingStockNonNegative(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IEnumerable<(TargetSnapshot Target, long Delta)> deltas)
    {
        foreach ((TargetSnapshot target, long delta) in deltas)
        {
            long current = ReadStock(connection, transaction, target);
            long result = checked(current + delta);
            if (result < 0)
            {
                throw new InventoryValidationException(
                    "CafeInventory.InsufficientStock",
                    "Inventory operation would make stock negative.");
            }
        }
    }

    private static void EnsureUniqueTargets(IEnumerable<ResolvedTargetLine> lines)
    {
        HashSet<string> targets = new(StringComparer.Ordinal);
        HashSet<Guid> lineIds = new();
        foreach (ResolvedTargetLine line in lines)
        {
            if (!lineIds.Add(line.LineId))
            {
                throw new InventoryValidationException(
                    "CafeInventory.DuplicateLineId",
                    "Inventory line IDs must be unique.");
            }

            string key = line.Target.TargetType + ":" + line.Target.Id.ToString("D");
            if (!targets.Add(key))
            {
                throw new InventoryValidationException(
                    "CafeInventory.DuplicateTarget",
                    "A stock target may appear only once in a factual operation.");
            }
        }
    }

    private static void InsertCountLine(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid sessionId,
        CountDraftLine line)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO inventory_count_lines(
                line_id,count_session_id,stock_target_type,stock_target_id,
                name_snapshot,stock_unit_snapshot,expected_before_x1000,
                counted_x1000,quantity_delta_x1000)
            VALUES($line,$session,$type,$target,$name,$unit,$expected,$counted,$delta);
            """;
        command.Parameters.AddWithValue("$line", line.LineId.ToString("D"));
        command.Parameters.AddWithValue("$session", sessionId.ToString("D"));
        command.Parameters.AddWithValue("$type", line.Target.TargetType);
        command.Parameters.AddWithValue("$target", line.Target.Id.ToString("D"));
        command.Parameters.AddWithValue("$name", line.Target.Name);
        command.Parameters.AddWithValue("$unit", line.Target.StockUnit);
        command.Parameters.AddWithValue("$expected", line.ExpectedBeforeX1000);
        command.Parameters.AddWithValue("$counted", line.CountedX1000);
        command.Parameters.AddWithValue("$delta", line.QuantityDeltaX1000);
        command.ExecuteNonQuery();
    }

    internal static PurchaseReceiptEvidence? ReadPurchaseByOperation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId)
    {
        string? id = ScalarString(
            connection,
            transaction,
            "SELECT purchase_receipt_id FROM purchase_receipts WHERE operation_id=$id;",
            ("$id", operationId.ToString("D")));
        return Guid.TryParse(id, out Guid purchaseId)
            ? ReadPurchaseById(connection, transaction, purchaseId)
            : null;
    }

    private static PurchaseReceiptEvidence? ReadPurchaseById(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid purchaseId)
    {
        using SqliteCommand header = connection.CreateCommand();
        header.Transaction = transaction;
        header.CommandText = """
            SELECT operation_id,store_id,supplier_id,supplier_name_snapshot,
                   external_reference,received_at_utc,actor_user_id,notes,
                   total_acquisition_cost_cents,content_hash,correction_of_purchase_receipt_id
            FROM purchase_receipts
            WHERE purchase_receipt_id=$id
            LIMIT 1;
            """;
        header.Parameters.AddWithValue("$id", purchaseId.ToString("D"));
        using SqliteDataReader reader = header.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        Guid operationId = Guid.Parse(reader.GetString(0));
        Guid storeId = Guid.Parse(reader.GetString(1));
        Guid supplierId = Guid.Parse(reader.GetString(2));
        string supplierName = reader.GetString(3);
        string? reference = reader.IsDBNull(4) ? null : reader.GetString(4);
        DateTimeOffset received = DateTimeOffset.Parse(reader.GetString(5));
        Guid actor = Guid.Parse(reader.GetString(6));
        string? notes = reader.IsDBNull(7) ? null : reader.GetString(7);
        long total = reader.GetInt64(8);
        string hash = reader.GetString(9);
        Guid? correction = reader.IsDBNull(10) ? null : Guid.Parse(reader.GetString(10));
        reader.Close();

        List<PurchaseReceiptLine> lines = new();
        using SqliteCommand lineCommand = connection.CreateCommand();
        lineCommand.Transaction = transaction;
        lineCommand.CommandText = """
            SELECT line_id,stock_target_type,stock_target_id,name_snapshot,stock_unit_snapshot,
                   quantity_received_x1000,purchase_package_label,
                   purchase_package_quantity_x1000,purchase_package_content_x1000,
                   line_acquisition_cost_cents
            FROM purchase_receipt_lines
            WHERE purchase_receipt_id=$id
            ORDER BY line_id;
            """;
        lineCommand.Parameters.AddWithValue("$id", purchaseId.ToString("D"));
        using SqliteDataReader lineReader = lineCommand.ExecuteReader();
        while (lineReader.Read())
        {
            lines.Add(new PurchaseReceiptLine(
                Guid.Parse(lineReader.GetString(0)),
                lineReader.GetString(1),
                Guid.Parse(lineReader.GetString(2)),
                lineReader.GetString(3),
                lineReader.GetString(4),
                lineReader.GetInt64(5),
                lineReader.IsDBNull(6) ? null : lineReader.GetString(6),
                lineReader.IsDBNull(7) ? null : lineReader.GetInt64(7),
                lineReader.IsDBNull(8) ? null : lineReader.GetInt64(8),
                lineReader.GetInt64(9)));
        }

        return new PurchaseReceiptEvidence(
            purchaseId, operationId, storeId, supplierId, supplierName, reference,
            received, actor, notes, total, hash, correction, lines);
    }

    internal static InventoryWasteEvidence? ReadWasteByOperation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId)
    {
        using SqliteCommand header = connection.CreateCommand();
        header.Transaction = transaction;
        header.CommandText = """
            SELECT waste_event_id,store_id,reason,actor_user_id,occurred_at_utc,
                   notes,content_hash,correction_of_waste_event_id
            FROM inventory_waste_events
            WHERE operation_id=$operation
            LIMIT 1;
            """;
        header.Parameters.AddWithValue("$operation", operationId.ToString("D"));
        using SqliteDataReader reader = header.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        Guid wasteId = Guid.Parse(reader.GetString(0));
        Guid storeId = Guid.Parse(reader.GetString(1));
        string reason = reader.GetString(2);
        Guid actor = Guid.Parse(reader.GetString(3));
        DateTimeOffset occurred = DateTimeOffset.Parse(reader.GetString(4));
        string? notes = reader.IsDBNull(5) ? null : reader.GetString(5);
        string hash = reader.GetString(6);
        Guid? correction = reader.IsDBNull(7) ? null : Guid.Parse(reader.GetString(7));
        reader.Close();

        List<InventoryWasteLine> lines = new();
        using SqliteCommand lineCommand = connection.CreateCommand();
        lineCommand.Transaction = transaction;
        lineCommand.CommandText = """
            SELECT line_id,stock_target_type,stock_target_id,name_snapshot,
                   stock_unit_snapshot,quantity_wasted_x1000
            FROM inventory_waste_lines
            WHERE waste_event_id=$id
            ORDER BY line_id;
            """;
        lineCommand.Parameters.AddWithValue("$id", wasteId.ToString("D"));
        using SqliteDataReader lineReader = lineCommand.ExecuteReader();
        while (lineReader.Read())
        {
            lines.Add(new InventoryWasteLine(
                Guid.Parse(lineReader.GetString(0)),
                lineReader.GetString(1),
                Guid.Parse(lineReader.GetString(2)),
                lineReader.GetString(3),
                lineReader.GetString(4),
                lineReader.GetInt64(5)));
        }

        return new InventoryWasteEvidence(
            wasteId, operationId, storeId, reason, actor, occurred,
            notes, hash, correction, lines);
    }

    internal static InventoryAdjustmentEvidence? ReadAdjustmentByOperation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT adjustment_event_id,store_id,stock_target_type,stock_target_id,
                   name_snapshot,stock_unit_snapshot,quantity_delta_x1000,reason,
                   reference_type,reference_id,correction_of_reference_id,
                   actor_user_id,occurred_at_utc,content_hash
            FROM inventory_adjustment_events
            WHERE operation_id=$operation
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$operation", operationId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? new InventoryAdjustmentEvidence(
                Guid.Parse(reader.GetString(0)),
                operationId,
                Guid.Parse(reader.GetString(1)),
                reader.GetString(2),
                Guid.Parse(reader.GetString(3)),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetInt64(6),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : Guid.Parse(reader.GetString(9)),
                reader.IsDBNull(10) ? null : Guid.Parse(reader.GetString(10)),
                Guid.Parse(reader.GetString(11)),
                DateTimeOffset.Parse(reader.GetString(12)),
                reader.GetString(13))
            : null;
    }

    internal static InventoryCountSession? ReadCountByOperation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId)
    {
        string? session = ScalarString(
            connection,
            transaction,
            "SELECT count_session_id FROM inventory_count_sessions WHERE finalize_operation_id=$operation;",
            ("$operation", operationId.ToString("D")));
        return Guid.TryParse(session, out Guid sessionId)
            ? ReadCountSession(connection, transaction, sessionId)
            : null;
    }

    internal static InventoryCountSession? ReadCountSession(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid sessionId)
    {
        using SqliteCommand header = connection.CreateCommand();
        header.Transaction = transaction;
        header.CommandText = """
            SELECT store_id,state,started_by_user_id,started_at_utc,
                   finalized_by_user_id,finalized_at_utc,notes,
                   finalize_operation_id,finalized_content_hash
            FROM inventory_count_sessions
            WHERE count_session_id=$id
            LIMIT 1;
            """;
        header.Parameters.AddWithValue("$id", sessionId.ToString("D"));
        using SqliteDataReader reader = header.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        Guid store = Guid.Parse(reader.GetString(0));
        string state = reader.GetString(1);
        Guid startedBy = Guid.Parse(reader.GetString(2));
        DateTimeOffset started = DateTimeOffset.Parse(reader.GetString(3));
        Guid? finalizedBy = reader.IsDBNull(4) ? null : Guid.Parse(reader.GetString(4));
        DateTimeOffset? finalizedAt = reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5));
        string? notes = reader.IsDBNull(6) ? null : reader.GetString(6);
        Guid? operation = reader.IsDBNull(7) ? null : Guid.Parse(reader.GetString(7));
        string? contentHash = reader.IsDBNull(8) ? null : reader.GetString(8);
        reader.Close();

        List<InventoryCountLine> lines = new();
        using SqliteCommand lineCommand = connection.CreateCommand();
        lineCommand.Transaction = transaction;
        lineCommand.CommandText = """
            SELECT line_id,stock_target_type,stock_target_id,name_snapshot,stock_unit_snapshot,
                   expected_before_x1000,counted_x1000,quantity_delta_x1000
            FROM inventory_count_lines
            WHERE count_session_id=$id
            ORDER BY line_id;
            """;
        lineCommand.Parameters.AddWithValue("$id", sessionId.ToString("D"));
        using SqliteDataReader lineReader = lineCommand.ExecuteReader();
        while (lineReader.Read())
        {
            lines.Add(new InventoryCountLine(
                Guid.Parse(lineReader.GetString(0)),
                lineReader.GetString(1),
                Guid.Parse(lineReader.GetString(2)),
                lineReader.GetString(3),
                lineReader.GetString(4),
                lineReader.GetInt64(5),
                lineReader.GetInt64(6),
                lineReader.GetInt64(7)));
        }

        return new InventoryCountSession(
            sessionId, store, state, startedBy, started, finalizedBy, finalizedAt,
            notes, operation, contentHash, lines);
    }

    private static SupplierRow? ReadSupplier(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid supplierId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT name,is_active FROM suppliers WHERE id=$id LIMIT 1;";
        command.Parameters.AddWithValue("$id", supplierId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? new SupplierRow(reader.GetString(0), reader.GetInt32(1) == 1)
            : null;
    }

    private static bool PurchaseMatchesMutation(
        PurchaseReceiptEvidence evidence,
        PurchaseReceiptMutation mutation)
    {
        if (evidence.PurchaseReceiptId != mutation.PurchaseReceiptId ||
            evidence.StoreId != mutation.StoreId ||
            evidence.SupplierId != mutation.SupplierId ||
            evidence.ActorUserId != mutation.ActorUserId ||
            evidence.ReceivedAtUtc.ToUniversalTime() != mutation.ReceivedAtUtc.ToUniversalTime() ||
            evidence.CorrectionOfPurchaseReceiptId != mutation.CorrectionOfPurchaseReceiptId ||
            !string.Equals(evidence.ExternalReference, Clean(mutation.ExternalReference), StringComparison.Ordinal) ||
            !string.Equals(evidence.Notes, Clean(mutation.Notes), StringComparison.Ordinal) ||
            evidence.Lines.Count != mutation.Lines.Count)
        {
            return false;
        }

        foreach (PurchaseReceiptLineMutation requested in mutation.Lines)
        {
            PurchaseReceiptLine? retained = requested.LineId.HasValue && requested.LineId.Value != Guid.Empty
                ? evidence.Lines.SingleOrDefault(x => x.LineId == requested.LineId.Value)
                : evidence.Lines.SingleOrDefault(x =>
                    x.StockTargetType == requested.StockTargetType &&
                    x.StockTargetId == requested.StockTargetId);

            if (retained is null ||
                retained.StockTargetType != requested.StockTargetType ||
                retained.StockTargetId != requested.StockTargetId ||
                retained.QuantityReceivedX1000 != requested.QuantityReceivedX1000 ||
                retained.LineAcquisitionCostCents != requested.LineAcquisitionCostCents ||
                !string.Equals(retained.PurchasePackageLabel, Clean(requested.PurchasePackageLabel), StringComparison.Ordinal) ||
                retained.PurchasePackageQuantityX1000 != requested.PurchasePackageQuantityX1000 ||
                retained.PurchasePackageContentX1000 != requested.PurchasePackageContentX1000)
            {
                return false;
            }
        }

        return true;
    }

    private static bool WasteMatchesMutation(
        InventoryWasteEvidence evidence,
        InventoryWasteMutation mutation)
    {
        if (evidence.WasteEventId != mutation.WasteEventId ||
            evidence.StoreId != mutation.StoreId ||
            evidence.ActorUserId != mutation.ActorUserId ||
            evidence.OccurredAtUtc.ToUniversalTime() != mutation.OccurredAtUtc.ToUniversalTime() ||
            evidence.CorrectionOfWasteEventId != mutation.CorrectionOfWasteEventId ||
            !string.Equals(evidence.Reason, mutation.Reason, StringComparison.Ordinal) ||
            !string.Equals(evidence.Notes, Clean(mutation.Notes), StringComparison.Ordinal) ||
            evidence.Lines.Count != mutation.Lines.Count)
        {
            return false;
        }

        foreach (InventoryWasteLineMutation requested in mutation.Lines)
        {
            InventoryWasteLine? retained = requested.LineId.HasValue && requested.LineId.Value != Guid.Empty
                ? evidence.Lines.SingleOrDefault(x => x.LineId == requested.LineId.Value)
                : evidence.Lines.SingleOrDefault(x =>
                    x.StockTargetType == requested.StockTargetType &&
                    x.StockTargetId == requested.StockTargetId);

            if (retained is null ||
                retained.QuantityWastedX1000 != requested.QuantityWastedX1000)
            {
                return false;
            }
        }

        return true;
    }

    private static bool AdjustmentMatchesMutation(
        InventoryAdjustmentEvidence evidence,
        InventoryAdjustmentMutation mutation) =>
        evidence.AdjustmentEventId == mutation.AdjustmentEventId &&
        evidence.StoreId == mutation.StoreId &&
        evidence.StockTargetType == mutation.StockTargetType &&
        evidence.StockTargetId == mutation.StockTargetId &&
        evidence.QuantityDeltaX1000 == mutation.QuantityDeltaX1000 &&
        evidence.ActorUserId == mutation.ActorUserId &&
        evidence.OccurredAtUtc.ToUniversalTime() == mutation.OccurredAtUtc.ToUniversalTime() &&
        evidence.ReferenceId == mutation.ReferenceId &&
        evidence.CorrectionOfReferenceId == mutation.CorrectionOfReferenceId &&
        string.Equals(evidence.ReferenceType, Clean(mutation.ReferenceType), StringComparison.Ordinal) &&
        string.Equals(evidence.Reason, mutation.Reason.Trim(), StringComparison.Ordinal);

    private static Guid ReadStoreId(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        string? text = ScalarString(
            connection,
            transaction,
            "SELECT store_id FROM deployment_identity ORDER BY created_at LIMIT 1;");
        return Guid.TryParse(text, out Guid id)
            ? id
            : throw new InvalidOperationException("Deployment StoreId is missing.");
    }

    private static bool ExistsById(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string column,
        Guid id)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM " + table + " WHERE " + column + "=$id LIMIT 1;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        return command.ExecuteScalar() is not null;
    }

    private static string? ScalarString(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        object? result = command.ExecuteScalar();
        return result is null or DBNull ? null : Convert.ToString(result);
    }

    private static Guid DeriveMovementId(
        Guid referenceId,
        Guid referenceLineId,
        Guid targetId,
        string kind)
    {
        string seed = referenceId.ToString("D") + "|" +
            referenceLineId.ToString("D") + "|" +
            targetId.ToString("D") + "|" + kind;
        byte[] hash = global::System.Security.Cryptography.SHA256.HashData(
            global::System.Text.Encoding.UTF8.GetBytes(seed));
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80);
        return new Guid(bytes);
    }

    private static object Db(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static object Db(Guid? value) =>
        value.HasValue && value.Value != Guid.Empty
            ? value.Value.ToString("D")
            : DBNull.Value;

    private static object Db(long? value) =>
        value.HasValue ? value.Value : DBNull.Value;

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsUniqueViolation(SqliteException ex) =>
        ex.SqliteExtendedErrorCode is 1555 or 2067 ||
        ex.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase);

    private sealed record TargetSnapshot(
        string TargetType,
        Guid Id,
        string Name,
        string StockUnit,
        long StockOnHandX1000,
        long ReorderLevelX1000,
        bool IsActive);

    private sealed record SupplierRow(string Name, bool IsActive);

    private sealed record ResolvedTargetLine(
        Guid LineId,
        TargetSnapshot Target,
        long QuantityDeltaX1000,
        long? AcquisitionCostCents,
        string? PurchasePackageLabel,
        long? PurchasePackageQuantityX1000,
        long? PurchasePackageContentX1000);

    private sealed record CountDraftLine(
        Guid LineId,
        TargetSnapshot Target,
        long ExpectedBeforeX1000,
        long CountedX1000,
        long QuantityDeltaX1000);

    private sealed class InventoryValidationException : Exception
    {
        public InventoryValidationException(string code, string message)
            : base(message)
        {
            Code = code;
        }

        public string Code { get; }
    }
}
