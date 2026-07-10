using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Core.Services.InvoiceOutput;
using Salryn.Core.Services.Lifecycle;

namespace Salryn.Infrastructure.Data.Services;

/// <summary>
/// M4-RC2: tax-aware official document lifecycle accounting in one SQLite transaction.
/// Line-level refunds, stock dispositions, settlements, internal adjustment documents.
/// Never mutates original EJ snapshot/hash. Never invents legal adjustment series.
/// </summary>
public sealed class SqliteLifecycleAccountingService
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly Func<Guid> _idFactory;
    private readonly Func<DateTimeOffset> _clock;

    public SqliteLifecycleAccountingService(
        SqliteConnectionFactory connectionFactory,
        Func<Guid>? idFactory = null,
        Func<DateTimeOffset>? clock = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _idFactory = idFactory ?? Guid.NewGuid;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public Task<Result<LifecycleAccountingResult>> ExecuteAsync(
        LifecycleAccountingCommand command,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Result validation = Validate(command);
        if (validation.IsFailure)
        {
            return Task.FromResult(Result<LifecycleAccountingResult>.Fail(validation.ErrorDetail));
        }

        DateTimeOffset operationTimestamp = _clock();
        Guid operationId = _idFactory();

        try
        {
            using SqliteConnection connection = _connectionFactory.CreateConnection();
            connection.Open();
            // Immediate lock reduces write races across connections.
            using SqliteTransaction tx = connection.BeginTransaction(System.Data.IsolationLevel.Serializable);

            Inject(command, "before_resolve");

            string requestHash = HashCommand(command);
            Result<LifecycleAccountingResult>? prior = TryReadIdempotent(connection, tx, command.IdempotencyKey, requestHash, command);
            if (prior is not null)
            {
                tx.Commit();
                return Task.FromResult(prior);
            }

            Result<FrozenSnapshotDocument> original = ResolveAndFreezeOriginal(connection, tx, command);
            if (original.IsFailure)
            {
                tx.Rollback();
                return Task.FromResult(Result<LifecycleAccountingResult>.Fail(original.ErrorDetail));
            }

            Inject(command, "after_state_read");

            StateRow state = LoadOrCreateState(connection, tx, original.Value, operationTimestamp);
            Result guard = LifecycleStateMachine.Guard(LifecycleStateMachine.Parse(state.LifecycleState), command.Action);
            if (guard.IsFailure)
            {
                tx.Rollback();
                return Task.FromResult(Result<LifecycleAccountingResult>.Fail(guard.ErrorDetail));
            }

            Guid eventId = _idFactory();
            LifecycleAccountingResult result;

            if (command.Action == OfficialDocumentLifecycleAction.Reprint)
            {
                Result<InvoiceOutputPackage> rendered = RenderReprint(original.Value, command.Reason, operationTimestamp);
                if (rendered.IsFailure)
                {
                    tx.Rollback();
                    return Task.FromResult(Result<LifecycleAccountingResult>.Fail(rendered.ErrorDetail));
                }

                AppendEjEvent(connection, tx, eventId, original.Value.JournalEntryId, "REPRINT",
                    0, 0, 0, command.ActorUserId, command.Reason, operationTimestamp, command.TerminalId);
                Result versioned = UpsertStateOptimistic(connection, tx, state with
                {
                    LastEventId = eventId.ToString("D"),
                    UpdatedAt = operationTimestamp.ToString("O", CultureInfo.InvariantCulture)
                }, state.StateVersion);
                if (versioned.IsFailure)
                {
                    tx.Rollback();
                    return Task.FromResult(Result<LifecycleAccountingResult>.Fail(versioned.ErrorDetail));
                }

                InsertOperation(connection, tx, operationId, original.Value.JournalEntryId, "Reprint", state.LifecycleState,
                    command, eventId, null, null, null, operationTimestamp);

                result = BuildResult(command, operationId, eventId, original.Value, operationTimestamp, state.LifecycleState,
                    state.StateVersion + 0, "Reprint completed with REPRINT marker; sequence not advanced.",
                    null, null, null, null, null, null, null, rendered.Value);
            }
            else if (command.Action == OfficialDocumentLifecycleAction.Void)
            {
                if (state.RefundedTotalCents > 0)
                {
                    tx.Rollback();
                    return Task.FromResult(Result<LifecycleAccountingResult>.Fail(Error.Conflict(
                        "DocumentLifecycle.VoidNotAllowed",
                        "Void is only allowed from a non-refunded committed state.")));
                }

                Result voidOps = ApplyVoidOperational(connection, tx, original.Value, command.ActorUserId, command.Reason, operationTimestamp);
                if (voidOps.IsFailure)
                {
                    tx.Rollback();
                    return Task.FromResult(Result<LifecycleAccountingResult>.Fail(voidOps.ErrorDetail));
                }

                Inject(command, "after_stock");

                Guid settlementId = _idFactory();
                Result settle = InsertSettlement(connection, tx, settlementId, operationId, original.Value.JournalEntryId,
                    command.Settlement, original.Value.TotalCents, original.Value.SaleId, command.ActorUserId, operationTimestamp);
                if (settle.IsFailure)
                {
                    tx.Rollback();
                    return Task.FromResult(Result<LifecycleAccountingResult>.Fail(settle.ErrorDetail));
                }

                AppendEjEvent(connection, tx, eventId, original.Value.JournalEntryId, "VOID",
                    1, 0, 0, command.ActorUserId, command.Reason, operationTimestamp, command.TerminalId);

                Result versioned = UpsertStateOptimistic(connection, tx, state with
                {
                    LifecycleState = "Voided",
                    StateVersion = state.StateVersion + 1,
                    LastEventId = eventId.ToString("D"),
                    UpdatedAt = operationTimestamp.ToString("O", CultureInfo.InvariantCulture)
                }, state.StateVersion);
                if (versioned.IsFailure)
                {
                    tx.Rollback();
                    return Task.FromResult(Result<LifecycleAccountingResult>.Fail(versioned.ErrorDetail));
                }

                InsertOperation(connection, tx, operationId, original.Value.JournalEntryId, "Void", "Voided",
                    command, eventId, null, settlementId, null, operationTimestamp);

                result = BuildResult(command, operationId, eventId, original.Value, operationTimestamp, "Voided",
                    state.StateVersion + 1, "Void completed with sale/stock effects; original EJ immutable.",
                    null, null, settlementId, RefundSettlementStatus.Completed, null, null, null, null);
            }
            else if (command.Action == OfficialDocumentLifecycleAction.Refund)
            {
                IReadOnlyList<RefundLineCalculator.PriorReturn> priors = LoadPriorReturns(connection, tx, original.Value.JournalEntryId);
                Result<(IReadOnlyList<LifecycleRefundLineResult> Lines, LifecycleTaxReversalTotals Totals)> calc =
                    RefundLineCalculator.Calculate(original.Value, command.RefundLines!, priors);
                if (calc.IsFailure)
                {
                    tx.Rollback();
                    return Task.FromResult(Result<LifecycleAccountingResult>.Fail(calc.ErrorDetail));
                }

                Inject(command, "after_refund_calc");

                if (command.ExpectedTotalRefundCents is long expectedTotal &&
                    expectedTotal != calc.Value.Totals.GrossRefundCents)
                {
                    tx.Rollback();
                    return Task.FromResult(Result<LifecycleAccountingResult>.Fail(Error.Validation(
                        "DocumentLifecycle.ExpectedTotalMismatch",
                        "Client expected total refund does not match server calculation.")));
                }

                // Cumulative tax bucket guard
                if (state.RefundedVatableBase + calc.Value.Totals.VatableBaseCents > original.Value.OriginalVatableBaseCents ||
                    state.RefundedVatAmount + calc.Value.Totals.VatAmountCents > original.Value.OriginalVatAmountCents ||
                    state.RefundedVatExempt + calc.Value.Totals.VatExemptCents > original.Value.OriginalVatExemptCents ||
                    state.RefundedZeroRated + calc.Value.Totals.ZeroRatedCents > original.Value.OriginalZeroRatedCents ||
                    state.RefundedSspt + calc.Value.Totals.SsptCents > original.Value.OriginalSsptCents ||
                    state.RefundedNonVatExempt + calc.Value.Totals.NonVatExemptCents > original.Value.OriginalNonVatExemptCents ||
                    state.RefundedTotalCents + calc.Value.Totals.GrossRefundCents > original.Value.TotalCents)
                {
                    tx.Rollback();
                    return Task.FromResult(Result<LifecycleAccountingResult>.Fail(Error.Validation(
                        "DocumentLifecycle.OverRefund",
                        "Refund exceeds remaining refundable amounts/tax buckets.")));
                }

                Result stock = ApplyLineStockReturns(connection, tx, original.Value, calc.Value.Lines, command.ActorUserId, operationId, operationTimestamp);
                if (stock.IsFailure)
                {
                    tx.Rollback();
                    return Task.FromResult(Result<LifecycleAccountingResult>.Fail(stock.ErrorDetail));
                }

                Inject(command, "after_stock");

                Guid settlementId = _idFactory();
                Result settle = InsertSettlement(connection, tx, settlementId, operationId, original.Value.JournalEntryId,
                    command.Settlement ?? new LifecycleRefundSettlementRequest(RefundSettlementMethod.Cash),
                    calc.Value.Totals.GrossRefundCents, original.Value.SaleId, command.ActorUserId, operationTimestamp);
                if (settle.IsFailure)
                {
                    tx.Rollback();
                    return Task.FromResult(Result<LifecycleAccountingResult>.Fail(settle.ErrorDetail));
                }

                Inject(command, "after_payment");

                foreach (LifecycleRefundLineResult line in calc.Value.Lines)
                {
                    InsertRefundLine(connection, tx, operationId, eventId, original.Value.JournalEntryId, line, operationTimestamp);
                }

                AppendEjEvent(connection, tx, eventId, original.Value.JournalEntryId, "REFUND",
                    0, 1, 0, command.ActorUserId, command.Reason, operationTimestamp, command.TerminalId);

                long newRefunded = state.RefundedTotalCents + calc.Value.Totals.GrossRefundCents;
                bool pending = command.Settlement?.Method == RefundSettlementMethod.CardExternal &&
                               !(command.Settlement?.ImmediateComplete ?? false);
                string newState = pending
                    ? "RefundPendingSettlement"
                    : newRefunded >= original.Value.TotalCents ? "FullyRefunded" : "PartiallyRefunded";

                // Internal adjustment document (no legal series).
                Guid adjustmentId = _idFactory();
                string adjNumber = "IADJ-" + operationId.ToString("N")[..12].ToUpperInvariant();
                string adjJson = BuildInternalAdjustmentJson(original.Value, calc.Value.Lines, calc.Value.Totals, adjNumber, command.Reason, operationTimestamp);
                string adjHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(adjJson))).ToLowerInvariant();
                InsertAdjustmentDocument(connection, tx, adjustmentId, operationId, original.Value, adjNumber, adjJson, adjHash, command, operationTimestamp);

                Result versioned = UpsertStateOptimistic(connection, tx, state with
                {
                    LifecycleState = newState,
                    RefundedTotalCents = newRefunded,
                    RefundedVatableBase = state.RefundedVatableBase + calc.Value.Totals.VatableBaseCents,
                    RefundedVatAmount = state.RefundedVatAmount + calc.Value.Totals.VatAmountCents,
                    RefundedVatExempt = state.RefundedVatExempt + calc.Value.Totals.VatExemptCents,
                    RefundedZeroRated = state.RefundedZeroRated + calc.Value.Totals.ZeroRatedCents,
                    RefundedSspt = state.RefundedSspt + calc.Value.Totals.SsptCents,
                    RefundedNonVatExempt = state.RefundedNonVatExempt + calc.Value.Totals.NonVatExemptCents,
                    StateVersion = state.StateVersion + 1,
                    LastEventId = eventId.ToString("D"),
                    UpdatedAt = operationTimestamp.ToString("O", CultureInfo.InvariantCulture)
                }, state.StateVersion);
                if (versioned.IsFailure)
                {
                    tx.Rollback();
                    return Task.FromResult(Result<LifecycleAccountingResult>.Fail(versioned.ErrorDetail));
                }

                InsertOperation(connection, tx, operationId, original.Value.JournalEntryId, "Refund", newState,
                    command, eventId, adjustmentId, settlementId, JsonSerializer.Serialize(calc.Value.Totals), operationTimestamp);

                Result<InvoiceOutputPackage> adjOutput = RenderInternalAdjustment(adjJson, adjHash, adjNumber, original.Value, command.Reason, operationTimestamp);

                result = BuildResult(command, operationId, eventId, original.Value, operationTimestamp, newState,
                    state.StateVersion + 1,
                    "Refund completed with line-level tax/stock/settlement evidence; original EJ immutable.",
                    calc.Value.Totals, calc.Value.Lines, settlementId,
                    pending ? RefundSettlementStatus.PendingExternal : RefundSettlementStatus.Completed,
                    adjustmentId, adjNumber, adjHash, adjOutput.IsSuccess ? adjOutput.Value : null);
            }
            else if (command.Action == OfficialDocumentLifecycleAction.Cancel)
            {
                tx.Rollback();
                return Task.FromResult(Result<LifecycleAccountingResult>.Fail(Error.Validation(
                    "DocumentLifecycle.PostCommitCancelForbidden",
                    "Cancel after official commit is forbidden. Use void/refund/adjustment. Pre-commit cancel uses checkout draft path.")));
            }
            else if (command.Action == OfficialDocumentLifecycleAction.Adjust)
            {
                // Neutral internal adjustment without inventing legal series — record-only linkage.
                Guid adjustmentId = _idFactory();
                string adjNumber = "IADJ-" + operationId.ToString("N")[..12].ToUpperInvariant();
                string adjJson = BuildInternalAdjustmentJson(original.Value, Array.Empty<LifecycleRefundLineResult>(),
                    new LifecycleTaxReversalTotals(0, 0, 0, 0, 0, 0, 0), adjNumber, command.Reason, operationTimestamp);
                string adjHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(adjJson))).ToLowerInvariant();
                InsertAdjustmentDocument(connection, tx, adjustmentId, operationId, original.Value, adjNumber, adjJson, adjHash, command, operationTimestamp);
                AppendEjEvent(connection, tx, eventId, original.Value.JournalEntryId, "ADJUSTMENT",
                    0, 0, 0, command.ActorUserId, command.Reason, operationTimestamp, command.TerminalId);
                Result versioned = UpsertStateOptimistic(connection, tx, state with
                {
                    LifecycleState = "Adjusted",
                    StateVersion = state.StateVersion + 1,
                    LastEventId = eventId.ToString("D"),
                    UpdatedAt = operationTimestamp.ToString("O", CultureInfo.InvariantCulture)
                }, state.StateVersion);
                if (versioned.IsFailure)
                {
                    tx.Rollback();
                    return Task.FromResult(Result<LifecycleAccountingResult>.Fail(versioned.ErrorDetail));
                }

                InsertOperation(connection, tx, operationId, original.Value.JournalEntryId, "Adjust", "Adjusted",
                    command, eventId, adjustmentId, null, null, operationTimestamp);
                result = BuildResult(command, operationId, eventId, original.Value, operationTimestamp, "Adjusted",
                    state.StateVersion + 1,
                    "Internal adjustment document recorded (no legal series allocation).",
                    null, null, null, null, adjustmentId, adjNumber, adjHash, null);
            }
            else
            {
                tx.Rollback();
                return Task.FromResult(Result<LifecycleAccountingResult>.Fail(Error.Validation(
                    "DocumentLifecycle.UnknownAction", "Unsupported lifecycle action.")));
            }

            Inject(command, "before_idempotency");
            StoreIdempotency(connection, tx, command, requestHash, operationId, eventId, result, operationTimestamp);
            Inject(command, "before_commit");
            tx.Commit();
            return Task.FromResult(Result<LifecycleAccountingResult>.Success(result));
        }
        catch (FailureInjectionException ex)
        {
            return Task.FromResult(Result<LifecycleAccountingResult>.Fail(Error.Unexpected(
                "DocumentLifecycle.InjectedFailure",
                "Injected failure at stage: " + ex.Stage)));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException or OverflowException or JsonException)
        {
            return Task.FromResult(Result<LifecycleAccountingResult>.Fail(Error.Unexpected(
                "DocumentLifecycle.RuntimeFailed",
                "Lifecycle accounting failed: " + ex.Message)));
        }
    }

    /// <summary>Pre-commit cancel for draft sales (no official EJ). Idempotent.</summary>
    public Task<Result<LifecycleAccountingResult>> CancelDraftSaleAsync(
        Guid saleId,
        Guid actorUserId,
        string reason,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DateTimeOffset now = _clock();
        try
        {
            using SqliteConnection connection = _connectionFactory.CreateConnection();
            connection.Open();
            using SqliteTransaction tx = connection.BeginTransaction();

            // Reject if official EJ exists for sale.
            using (SqliteCommand check = connection.CreateCommand())
            {
                check.Transaction = tx;
                check.CommandText = "SELECT COUNT(*) FROM bir_electronic_journal_entries WHERE sale_id = $saleId;";
                check.Parameters.AddWithValue("$saleId", saleId.ToString("D"));
                if (Convert.ToInt32(check.ExecuteScalar()) > 0)
                {
                    tx.Rollback();
                    return Task.FromResult(Result<LifecycleAccountingResult>.Fail(Error.Conflict(
                        "DocumentLifecycle.PostCommitCancelForbidden",
                        "Sale already has official journal evidence; use void/refund.")));
                }
            }

            using (SqliteCommand voidDraft = connection.CreateCommand())
            {
                voidDraft.Transaction = tx;
                voidDraft.CommandText = """
                    UPDATE sales
                    SET status = $voided, voided_by_user_id = $actor, void_reason = $reason, voided_at = $at
                    WHERE id = $saleId AND status = $draft;
                    """;
                voidDraft.Parameters.AddWithValue("$voided", (int)SaleStatus.Voided);
                voidDraft.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
                voidDraft.Parameters.AddWithValue("$reason", reason.Trim());
                voidDraft.Parameters.AddWithValue("$at", now.ToString("O", CultureInfo.InvariantCulture));
                voidDraft.Parameters.AddWithValue("$saleId", saleId.ToString("D"));
                voidDraft.Parameters.AddWithValue("$draft", (int)SaleStatus.Draft);
                int n = voidDraft.ExecuteNonQuery();
                if (n == 0)
                {
                    // Idempotent: already voided draft
                    using SqliteCommand status = connection.CreateCommand();
                    status.Transaction = tx;
                    status.CommandText = "SELECT status FROM sales WHERE id = $id;";
                    status.Parameters.AddWithValue("$id", saleId.ToString("D"));
                    object? s = status.ExecuteScalar();
                    if (s is null)
                    {
                        tx.Rollback();
                        return Task.FromResult(Result<LifecycleAccountingResult>.Fail(Error.NotFound(
                            "DocumentLifecycle.SaleNotFound", "Draft sale not found.")));
                    }

                    if (Convert.ToInt32(s) != (int)SaleStatus.Voided)
                    {
                        tx.Rollback();
                        return Task.FromResult(Result<LifecycleAccountingResult>.Fail(Error.Conflict(
                            "DocumentLifecycle.DraftNotCancellable", "Sale is not a cancellable draft.")));
                    }
                }
            }

            tx.Commit();
            Guid op = _idFactory();
            return Task.FromResult(Result<LifecycleAccountingResult>.Success(new LifecycleAccountingResult(
                OfficialDocumentLifecycleAction.Cancel,
                op,
                op,
                Guid.Empty,
                string.Empty,
                reason.Trim(),
                actorUserId,
                now,
                SequenceAdvanced: false,
                OriginalImmutable: true,
                LifecycleState: "CancelledBeforeCommit",
                StateVersion: 1,
                Summary: "Pre-commit draft cancel; no official number or EJ original.",
                TaxReversal: null,
                RefundLines: null,
                SettlementId: null,
                SettlementStatus: null,
                AdjustmentDocumentId: null,
                AdjustmentDocumentNumber: null,
                AdjustmentSnapshotHash: null,
                Output: null)));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            return Task.FromResult(Result<LifecycleAccountingResult>.Fail(Error.Unexpected(
                "DocumentLifecycle.DraftCancelFailed", ex.Message)));
        }
    }

    private static void Inject(LifecycleAccountingCommand command, string stage)
    {
        if (!string.IsNullOrWhiteSpace(command.FailureInjectionStage) &&
            string.Equals(command.FailureInjectionStage, stage, StringComparison.OrdinalIgnoreCase))
        {
            throw new FailureInjectionException(stage);
        }
    }

    private sealed class FailureInjectionException : Exception
    {
        public string Stage { get; }
        public FailureInjectionException(string stage) => Stage = stage;
    }

    private static Result Validate(LifecycleAccountingCommand? command)
    {
        if (command is null)
        {
            return Result.Fail(Error.Validation("DocumentLifecycle.RequestRequired", "Request required."));
        }

        if (string.IsNullOrWhiteSpace(command.IdempotencyKey) || command.IdempotencyKey.Trim().Length > 200)
        {
            return Result.Fail(Error.Validation("DocumentLifecycle.IdempotencyKeyRequired", "Idempotency key required (max 200)."));
        }

        if (command.ActorUserId == Guid.Empty)
        {
            return Result.Fail(Error.Validation("DocumentLifecycle.ActorRequired", "Authenticated actor required."));
        }

        if (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Trim().Length > 500)
        {
            return Result.Fail(Error.Validation("DocumentLifecycle.ReasonRequired", "Reason required (max 500)."));
        }

        bool hasJournal = command.JournalEntryId is Guid j && j != Guid.Empty;
        bool hasDoc = !string.IsNullOrWhiteSpace(command.OfficialDocumentNumber);
        bool hasSale = command.SaleId is Guid s && s != Guid.Empty;
        if (!hasJournal && !hasDoc && !hasSale)
        {
            return Result.Fail(Error.Validation(
                "DocumentLifecycle.IdentityRequired",
                "Journal entry id, official document number, or sale id is required."));
        }

        if (command.Action == OfficialDocumentLifecycleAction.Refund &&
            (command.RefundLines is null || command.RefundLines.Count == 0))
        {
            return Result.Fail(Error.Validation(
                "DocumentLifecycle.RefundLinesRequired",
                "Refund requires explicit line quantities; amount-only ratio refunds are not accepted."));
        }

        return Result.Success();
    }

    private Result<LifecycleAccountingResult>? TryReadIdempotent(
        SqliteConnection connection,
        SqliteTransaction tx,
        string key,
        string requestHash,
        LifecycleAccountingCommand command)
    {
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT request_hash, result_json, output_content_sha256
            FROM official_document_lifecycle_idempotency
            WHERE idempotency_key = $key
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$key", key);
        using SqliteDataReader reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        string storedHash = reader.GetString(0);
        if (!string.Equals(storedHash, requestHash, StringComparison.Ordinal))
        {
            return Result<LifecycleAccountingResult>.Fail(Error.Conflict(
                "DocumentLifecycle.IdempotencyConflict",
                "Idempotency key was reused with a different request."));
        }

        string json = reader.GetString(1);
        LifecycleAccountingResult? prior = JsonSerializer.Deserialize<LifecycleAccountingResult>(json);
        if (prior is null)
        {
            return Result<LifecycleAccountingResult>.Fail(Error.Unexpected(
                "DocumentLifecycle.IdempotencyCorrupt",
                "Stored idempotency result could not be deserialized."));
        }

        // Deterministic reprint replay without second EJ event.
        if (command.Action == OfficialDocumentLifecycleAction.Reprint && prior.Output is null)
        {
            Result<FrozenSnapshotDocument> original = ResolveAndFreezeOriginal(connection, tx, command);
            if (original.IsSuccess)
            {
                Result<InvoiceOutputPackage> rendered = RenderReprint(original.Value, command.Reason, prior.OperationTimestamp);
                if (rendered.IsSuccess)
                {
                    prior = prior with { Output = rendered.Value };
                }
            }
        }

        return Result<LifecycleAccountingResult>.Success(prior);
    }

    private static Result<FrozenSnapshotDocument> ResolveAndFreezeOriginal(
        SqliteConnection connection,
        SqliteTransaction tx,
        LifecycleAccountingCommand command)
    {
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT e.id, e.sale_id, e.official_document_number, e.snapshot_payload_json, e.snapshot_payload_hash
            FROM bir_electronic_journal_entries e
            WHERE ($journalId IS NULL OR e.id = $journalId)
              AND ($doc IS NULL OR e.official_document_number = $doc)
              AND ($saleId IS NULL OR e.sale_id = $saleId)
            ORDER BY e.created_at ASC, e.id ASC
            LIMIT 2;
            """;
        cmd.Parameters.AddWithValue(
            "$journalId",
            command.JournalEntryId is Guid j && j != Guid.Empty ? j.ToString("D") : DBNull.Value);
        cmd.Parameters.AddWithValue(
            "$doc",
            string.IsNullOrWhiteSpace(command.OfficialDocumentNumber) ? DBNull.Value : command.OfficialDocumentNumber.Trim());
        cmd.Parameters.AddWithValue(
            "$saleId",
            command.SaleId is Guid s && s != Guid.Empty ? s.ToString("D") : DBNull.Value);

        using SqliteDataReader reader = cmd.ExecuteReader();
        List<(Guid Id, Guid? SaleId, string Doc, string Payload, string Hash)> matches = new();
        while (reader.Read())
        {
            matches.Add((
                Guid.Parse(reader.GetString(0)),
                reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
                reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                reader.IsDBNull(4) ? string.Empty : reader.GetString(4)));
        }

        if (matches.Count == 0)
        {
            return Result<FrozenSnapshotDocument>.Fail(Error.NotFound(
                "DocumentLifecycle.JournalEntryNotFound", "Official journal entry was not found."));
        }

        if (matches.Count > 1)
        {
            return Result<FrozenSnapshotDocument>.Fail(Error.Conflict(
                "DocumentLifecycle.AmbiguousReference",
                "Lifecycle reference matched more than one original document."));
        }

        var m = matches[0];
        if (string.IsNullOrWhiteSpace(m.Doc) || string.IsNullOrWhiteSpace(m.Hash) || string.IsNullOrWhiteSpace(m.Payload))
        {
            return Result<FrozenSnapshotDocument>.Fail(Error.Failure(
                "DocumentLifecycle.IncompleteOriginal", "Original official document evidence is incomplete."));
        }

        string recomputed = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(m.Payload))).ToLowerInvariant();
        if (!string.Equals(recomputed, m.Hash.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return Result<FrozenSnapshotDocument>.Fail(Error.Validation(
                "DocumentLifecycle.SnapshotHashMismatch",
                "Original snapshot hash does not match payload."));
        }

        return ParseFrozen(m.Id, m.SaleId, m.Doc, m.Payload, m.Hash);
    }

    private static Result<FrozenSnapshotDocument> ParseFrozen(
        Guid journalId,
        Guid? saleId,
        string docNumber,
        string payload,
        string hash)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(payload);
            JsonElement root = doc.RootElement;
            string schema = root.TryGetProperty("schema", out JsonElement s) ? s.GetString() ?? "" : "";
            long total = 0;
            if (root.TryGetProperty("amounts", out JsonElement amounts) &&
                amounts.TryGetProperty("totalCents", out JsonElement totalEl) &&
                totalEl.ValueKind == JsonValueKind.Number)
            {
                total = totalEl.GetInt64();
            }

            OfficialDocumentTaxBucketDraftTaxpayerMode mode = OfficialDocumentTaxBucketDraftTaxpayerMode.Unknown;
            long ov = 0, ovat = 0, oe = 0, oz = 0, os = 0, on = 0;
            if (root.TryGetProperty("taxBuckets", out JsonElement tb) && tb.ValueKind == JsonValueKind.Object)
            {
                if (tb.TryGetProperty("taxpayerMode", out JsonElement modeEl) &&
                    Enum.TryParse(modeEl.GetString(), true, out OfficialDocumentTaxBucketDraftTaxpayerMode parsed))
                {
                    mode = parsed;
                }

                if (tb.TryGetProperty("buckets", out JsonElement buckets) && buckets.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement b in buckets.EnumerateArray())
                    {
                        string kind = b.TryGetProperty("kind", out JsonElement k) ? k.GetString() ?? "" : "";
                        long sales = b.TryGetProperty("salesAmountCents", out JsonElement sa) && sa.ValueKind == JsonValueKind.Number ? sa.GetInt64() : 0;
                        long tax = b.TryGetProperty("taxAmountCents", out JsonElement ta) && ta.ValueKind == JsonValueKind.Number ? ta.GetInt64() : 0;
                        switch (kind)
                        {
                            case "VatableSales": ov = sales; break;
                            case "VatAmount": ovat = tax; break;
                            case "VatExemptSales": oe = sales; break;
                            case "ZeroRatedSales": oz = sales; break;
                            case "SsptSales":
                            case "NonVatSales": os = sales; break;
                            case "NonVatExemptSales": on = sales; break;
                        }
                    }
                }
            }

            List<FrozenSnapshotLine> lines = new();
            if (root.TryGetProperty("lines", out JsonElement linesEl) && linesEl.ValueKind == JsonValueKind.Array)
            {
                int idx = 0;
                foreach (JsonElement line in linesEl.EnumerateArray())
                {
                    if (!line.TryGetProperty("taxClassification", out JsonElement tc) ||
                        !line.TryGetProperty("saleItemId", out JsonElement siEl))
                    {
                        idx++;
                        continue;
                    }

                    if (!Guid.TryParse(siEl.GetString(), out Guid saleItemId))
                    {
                        idx++;
                        continue;
                    }

                    OfficialDocumentLineTaxClassification classification =
                        Enum.TryParse(tc.GetString(), true, out OfficialDocumentLineTaxClassification c)
                            ? c
                            : OfficialDocumentLineTaxClassification.Vatable;

                    Guid productId = Guid.Empty;
                    if (line.TryGetProperty("productId", out JsonElement p) && Guid.TryParse(p.GetString(), out Guid pid))
                    {
                        productId = pid;
                    }

                    int lineIndex = line.TryGetProperty("lineIndex", out JsonElement li) && li.ValueKind == JsonValueKind.Number
                        ? li.GetInt32()
                        : idx;

                    lines.Add(new FrozenSnapshotLine(
                        lineIndex,
                        saleItemId,
                        productId,
                        line.TryGetProperty("description", out JsonElement d) ? d.GetString() ?? "" : "",
                        line.TryGetProperty("unitName", out JsonElement u) ? u.GetString() ?? "pc" : "pc",
                        line.TryGetProperty("quantityX1000", out JsonElement q) ? q.GetInt64() : 0,
                        line.TryGetProperty("unitPriceCents", out JsonElement up) ? up.GetInt64() : 0,
                        line.TryGetProperty("discountCents", out JsonElement dc) ? dc.GetInt64() : 0,
                        line.TryGetProperty("lineTotalCents", out JsonElement lt) ? lt.GetInt64() : 0,
                        classification,
                        line.TryGetProperty("vatableBaseCents", out JsonElement vb) ? vb.GetInt64() : 0,
                        line.TryGetProperty("vatAmountCents", out JsonElement va) ? va.GetInt64() : 0,
                        line.TryGetProperty("vatExemptCents", out JsonElement ve) ? ve.GetInt64() : 0,
                        line.TryGetProperty("zeroRatedCents", out JsonElement zr) ? zr.GetInt64() : 0,
                        line.TryGetProperty("ssptCents", out JsonElement sp) ? sp.GetInt64() : 0,
                        line.TryGetProperty("nonVatExemptCents", out JsonElement nv) ? nv.GetInt64() : 0,
                        line.TryGetProperty("stockTracked", out JsonElement st) && st.ValueKind == JsonValueKind.True));
                    idx++;
                }
            }

            // If V3 lines missing but refund needs them, calculator will fail closed.
            // Resolve product ids from sale_items when productId empty.
            return Result<FrozenSnapshotDocument>.Success(new FrozenSnapshotDocument(
                schema,
                journalId,
                saleId,
                docNumber,
                payload,
                hash,
                total,
                mode,
                lines,
                ov, ovat, oe, oz, os, on));
        }
        catch (JsonException ex)
        {
            return Result<FrozenSnapshotDocument>.Fail(Error.Validation(
                "DocumentLifecycle.SnapshotMalformed",
                "Original snapshot JSON is malformed: " + ex.Message));
        }
    }

    private sealed record StateRow(
        string JournalEntryId,
        string? SaleId,
        string OfficialDocumentNumber,
        string LifecycleState,
        long OriginalTotalCents,
        long RefundedTotalCents,
        long RefundedVatableBase,
        long RefundedVatAmount,
        long RefundedVatExempt,
        long RefundedZeroRated,
        long RefundedSspt,
        long RefundedNonVatExempt,
        int StateVersion,
        string? LastEventId,
        string UpdatedAt);

    private static StateRow LoadOrCreateState(
        SqliteConnection connection,
        SqliteTransaction tx,
        FrozenSnapshotDocument original,
        DateTimeOffset now)
    {
        using (SqliteCommand cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT journal_entry_id, sale_id, official_document_number, lifecycle_state,
                       original_total_cents, refunded_total_cents,
                       refunded_vatable_base_cents, refunded_vat_amount_cents, refunded_vat_exempt_cents,
                       refunded_zero_rated_cents, refunded_sspt_cents, refunded_nonvat_exempt_cents,
                       state_version, last_event_id, updated_at
                FROM official_document_lifecycle_state
                WHERE journal_entry_id = $id
                LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("$id", original.JournalEntryId.ToString("D"));
            using SqliteDataReader reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                return new StateRow(
                    reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt64(4),
                    reader.GetInt64(5),
                    reader.IsDBNull(6) ? 0 : reader.GetInt64(6),
                    reader.IsDBNull(7) ? 0 : reader.GetInt64(7),
                    reader.IsDBNull(8) ? 0 : reader.GetInt64(8),
                    reader.IsDBNull(9) ? 0 : reader.GetInt64(9),
                    reader.IsDBNull(10) ? 0 : reader.GetInt64(10),
                    reader.IsDBNull(11) ? 0 : reader.GetInt64(11),
                    reader.GetInt32(12),
                    reader.IsDBNull(13) ? null : reader.GetString(13),
                    reader.GetString(14));
            }
        }

        return new StateRow(
            original.JournalEntryId.ToString("D"),
            original.SaleId?.ToString("D"),
            original.OfficialDocumentNumber,
            "Committed",
            original.TotalCents,
            0, 0, 0, 0, 0, 0, 0,
            1,
            null,
            now.ToString("O", CultureInfo.InvariantCulture));
    }

    private static Result UpsertStateOptimistic(SqliteConnection connection, SqliteTransaction tx, StateRow state, int expectedVersion)
    {
        // Insert if missing
        using (SqliteCommand exists = connection.CreateCommand())
        {
            exists.Transaction = tx;
            exists.CommandText = "SELECT COUNT(*) FROM official_document_lifecycle_state WHERE journal_entry_id = $id;";
            exists.Parameters.AddWithValue("$id", state.JournalEntryId);
            int count = Convert.ToInt32(exists.ExecuteScalar());
            if (count == 0)
            {
                using SqliteCommand insert = connection.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText = """
                    INSERT INTO official_document_lifecycle_state (
                        journal_entry_id, sale_id, official_document_number, lifecycle_state,
                        original_total_cents, refunded_total_cents,
                        refunded_vatable_base_cents, refunded_vat_amount_cents, refunded_vat_exempt_cents,
                        refunded_zero_rated_cents, refunded_sspt_cents, refunded_nonvat_exempt_cents,
                        state_version, last_event_id, updated_at
                    ) VALUES (
                        $journalId, $saleId, $doc, $state,
                        $original, $refunded,
                        $rvb, $rva, $rve, $rz, $rs, $rn,
                        $version, $eventId, $updatedAt
                    );
                    """;
                BindState(insert, state);
                try
                {
                    insert.ExecuteNonQuery();
                    return Result.Success();
                }
                catch (SqliteException)
                {
                    return Result.Fail(Error.Conflict("DocumentLifecycle.ConcurrencyConflict", "Lifecycle state race on create."));
                }
            }
        }

        using SqliteCommand update = connection.CreateCommand();
        update.Transaction = tx;
        update.CommandText = """
            UPDATE official_document_lifecycle_state
            SET lifecycle_state = $state,
                refunded_total_cents = $refunded,
                refunded_vatable_base_cents = $rvb,
                refunded_vat_amount_cents = $rva,
                refunded_vat_exempt_cents = $rve,
                refunded_zero_rated_cents = $rz,
                refunded_sspt_cents = $rs,
                refunded_nonvat_exempt_cents = $rn,
                state_version = $version,
                last_event_id = $eventId,
                updated_at = $updatedAt
            WHERE journal_entry_id = $journalId
              AND state_version = $expected;
            """;
        update.Parameters.AddWithValue("$state", state.LifecycleState);
        update.Parameters.AddWithValue("$refunded", state.RefundedTotalCents);
        update.Parameters.AddWithValue("$rvb", state.RefundedVatableBase);
        update.Parameters.AddWithValue("$rva", state.RefundedVatAmount);
        update.Parameters.AddWithValue("$rve", state.RefundedVatExempt);
        update.Parameters.AddWithValue("$rz", state.RefundedZeroRated);
        update.Parameters.AddWithValue("$rs", state.RefundedSspt);
        update.Parameters.AddWithValue("$rn", state.RefundedNonVatExempt);
        update.Parameters.AddWithValue("$version", state.StateVersion);
        update.Parameters.AddWithValue("$eventId", (object?)state.LastEventId ?? DBNull.Value);
        update.Parameters.AddWithValue("$updatedAt", state.UpdatedAt);
        update.Parameters.AddWithValue("$journalId", state.JournalEntryId);
        update.Parameters.AddWithValue("$expected", expectedVersion);
        int affected = update.ExecuteNonQuery();
        if (affected != 1)
        {
            return Result.Fail(Error.Conflict(
                "DocumentLifecycle.ConcurrencyConflict",
                "Lifecycle state version conflict; reload and retry."));
        }

        return Result.Success();
    }

    private static void BindState(SqliteCommand command, StateRow state)
    {
        command.Parameters.AddWithValue("$journalId", state.JournalEntryId);
        command.Parameters.AddWithValue("$saleId", (object?)state.SaleId ?? DBNull.Value);
        command.Parameters.AddWithValue("$doc", state.OfficialDocumentNumber);
        command.Parameters.AddWithValue("$state", state.LifecycleState);
        command.Parameters.AddWithValue("$original", state.OriginalTotalCents);
        command.Parameters.AddWithValue("$refunded", state.RefundedTotalCents);
        command.Parameters.AddWithValue("$rvb", state.RefundedVatableBase);
        command.Parameters.AddWithValue("$rva", state.RefundedVatAmount);
        command.Parameters.AddWithValue("$rve", state.RefundedVatExempt);
        command.Parameters.AddWithValue("$rz", state.RefundedZeroRated);
        command.Parameters.AddWithValue("$rs", state.RefundedSspt);
        command.Parameters.AddWithValue("$rn", state.RefundedNonVatExempt);
        command.Parameters.AddWithValue("$version", state.StateVersion);
        command.Parameters.AddWithValue("$eventId", (object?)state.LastEventId ?? DBNull.Value);
        command.Parameters.AddWithValue("$updatedAt", state.UpdatedAt);
    }

    private static IReadOnlyList<RefundLineCalculator.PriorReturn> LoadPriorReturns(
        SqliteConnection connection,
        SqliteTransaction tx,
        Guid journalId)
    {
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT sale_item_id, quantity_x1000, refund_amount_cents
            FROM official_document_lifecycle_refund_lines
            WHERE journal_entry_id = $j AND sale_item_id IS NOT NULL;
            """;
        cmd.Parameters.AddWithValue("$j", journalId.ToString("D"));
        List<RefundLineCalculator.PriorReturn> list = new();
        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (Guid.TryParse(reader.GetString(0), out Guid saleItemId))
            {
                list.Add(new RefundLineCalculator.PriorReturn(saleItemId, reader.GetInt64(1), reader.GetInt64(2)));
            }
        }

        return list;
    }

    private static Result ApplyVoidOperational(
        SqliteConnection connection,
        SqliteTransaction tx,
        FrozenSnapshotDocument original,
        Guid actorUserId,
        string reason,
        DateTimeOffset now)
    {
        if (original.SaleId is null)
        {
            return Result.Fail(Error.Failure("DocumentLifecycle.SaleMissing", "Original journal entry is not linked to a sale."));
        }

        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = tx;
            command.CommandText = """
                UPDATE sales
                SET status = $voided, voided_by_user_id = $actor, void_reason = $reason, voided_at = $voidedAt
                WHERE id = $saleId AND status = $completed;
                """;
            command.Parameters.AddWithValue("$voided", (int)SaleStatus.Voided);
            command.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
            command.Parameters.AddWithValue("$reason", reason.Trim());
            command.Parameters.AddWithValue("$voidedAt", now.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$saleId", original.SaleId.Value.ToString("D"));
            command.Parameters.AddWithValue("$completed", (int)SaleStatus.Completed);
            if (command.ExecuteNonQuery() == 0)
            {
                return Result.Fail(Error.Conflict("DocumentLifecycle.SaleNotVoidable", "Sale was not found or is not voidable."));
            }
        }

        // Exact reverse of each sale stock movement.
        using SqliteCommand read = connection.CreateCommand();
        read.Transaction = tx;
        read.CommandText = """
            SELECT product_id, qty_change_x1000
            FROM stock_movements
            WHERE reference_type = 'SALE' AND reference_id = $saleId AND movement_type = $saleType;
            """;
        read.Parameters.AddWithValue("$saleId", original.SaleId.Value.ToString("D"));
        read.Parameters.AddWithValue("$saleType", (int)StockMovementType.Sale);
        List<(Guid ProductId, long Qty)> lines = new();
        using (SqliteDataReader reader = read.ExecuteReader())
        {
            while (reader.Read())
            {
                lines.Add((Guid.Parse(reader.GetString(0)), reader.GetInt64(1)));
            }
        }

        foreach ((Guid productId, long qty) in lines)
        {
            long reverseQty = Math.Abs(qty);
            if (reverseQty <= 0)
            {
                continue;
            }

            InsertStock(connection, tx, productId, StockMovementType.VoidSale, reverseQty, original.SaleId.Value,
                actorUserId, "Official void stock return", now);
            UpdateProductStock(connection, tx, productId, reverseQty);
        }

        return Result.Success();
    }

    private static Result ApplyLineStockReturns(
        SqliteConnection connection,
        SqliteTransaction tx,
        FrozenSnapshotDocument original,
        IReadOnlyList<LifecycleRefundLineResult> lines,
        Guid actorUserId,
        Guid operationId,
        DateTimeOffset now)
    {
        if (original.SaleId is null)
        {
            return Result.Fail(Error.Failure("DocumentLifecycle.SaleMissing", "Sale missing for refund."));
        }

        foreach (LifecycleRefundLineResult line in lines)
        {
            FrozenSnapshotLine? frozen = original.Lines.FirstOrDefault(l => l.SaleItemId == line.SaleItemId);
            if (frozen is null)
            {
                return Result.Fail(Error.NotFound("DocumentLifecycle.SaleItemNotOnOriginal", "Line missing on original."));
            }

            Guid productId = frozen.ProductId;
            if (productId == Guid.Empty)
            {
                productId = LookupProductId(connection, tx, line.SaleItemId);
            }

            if (productId == Guid.Empty)
            {
                return Result.Fail(Error.NotFound("DocumentLifecycle.ProductMissing", "Product missing for stock return."));
            }

            if (line.Disposition == StockReturnDisposition.Restock)
            {
                InsertStock(connection, tx, productId, StockMovementType.ReturnRestock, line.QuantityX1000,
                    operationId, actorUserId, "Official return restock", now, referenceType: "LIFECYCLE_REFUND");
                UpdateProductStock(connection, tx, productId, line.QuantityX1000);
            }
            else if (line.Disposition == StockReturnDisposition.DamagedNonRestockable)
            {
                // Evidence movement without increasing sellable stock.
                InsertStock(connection, tx, productId, StockMovementType.Damage, 0,
                    operationId, actorUserId, "Official return damaged/non-restockable qty=" + line.QuantityX1000, now,
                    referenceType: "LIFECYCLE_REFUND");
            }
            // ServiceNoStock: no stock movement.
        }

        return Result.Success();
    }

    private static Guid LookupProductId(SqliteConnection connection, SqliteTransaction tx, Guid saleItemId)
    {
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT product_id FROM sale_items WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", saleItemId.ToString("D"));
        object? o = cmd.ExecuteScalar();
        return o is string s && Guid.TryParse(s, out Guid id) ? id : Guid.Empty;
    }

    private static void InsertStock(
        SqliteConnection connection,
        SqliteTransaction tx,
        Guid productId,
        StockMovementType type,
        long qty,
        Guid referenceId,
        Guid actorUserId,
        string reason,
        DateTimeOffset now,
        string referenceType = "SALE")
    {
        using SqliteCommand insert = connection.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = """
            INSERT INTO stock_movements (
                id, product_id, movement_type, qty_change_x1000, cost_price_cents,
                reference_type, reference_id, reason, user_id, created_at
            ) VALUES (
                $id, $productId, $type, $qty, 0,
                $refType, $saleId, $reason, $userId, $createdAt
            );
            """;
        insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        insert.Parameters.AddWithValue("$productId", productId.ToString("D"));
        insert.Parameters.AddWithValue("$type", (int)type);
        insert.Parameters.AddWithValue("$qty", qty);
        insert.Parameters.AddWithValue("$refType", referenceType);
        insert.Parameters.AddWithValue("$saleId", referenceId.ToString("D"));
        insert.Parameters.AddWithValue("$reason", reason);
        insert.Parameters.AddWithValue("$userId", actorUserId.ToString("D"));
        insert.Parameters.AddWithValue("$createdAt", now.ToString("O", CultureInfo.InvariantCulture));
        insert.ExecuteNonQuery();
    }

    private static void UpdateProductStock(SqliteConnection connection, SqliteTransaction tx, Guid productId, long qty)
    {
        using SqliteCommand stock = connection.CreateCommand();
        stock.Transaction = tx;
        stock.CommandText = "UPDATE products SET stock_on_hand_x1000 = stock_on_hand_x1000 + $qty WHERE id = $productId;";
        stock.Parameters.AddWithValue("$qty", qty);
        stock.Parameters.AddWithValue("$productId", productId.ToString("D"));
        stock.ExecuteNonQuery();
    }

    private static Result InsertSettlement(
        SqliteConnection connection,
        SqliteTransaction tx,
        Guid settlementId,
        Guid operationId,
        Guid journalId,
        LifecycleRefundSettlementRequest? settlement,
        long amountCents,
        Guid? saleId,
        Guid actorUserId,
        DateTimeOffset now)
    {
        if (amountCents <= 0)
        {
            return Result.Success();
        }

        LifecycleRefundSettlementRequest s = settlement ?? new LifecycleRefundSettlementRequest(RefundSettlementMethod.Cash);
        string method = s.Method.ToString();
        string status = s.Method == RefundSettlementMethod.CardExternal && !s.ImmediateComplete
            ? "PendingExternal"
            : "Completed";

        string? sessionId = null;
        if (saleId is Guid sid)
        {
            using SqliteCommand sess = connection.CreateCommand();
            sess.Transaction = tx;
            sess.CommandText = "SELECT cashier_session_id FROM sales WHERE id = $id LIMIT 1;";
            sess.Parameters.AddWithValue("$id", sid.ToString("D"));
            object? o = sess.ExecuteScalar();
            sessionId = o as string;
        }

        if (status == "Completed" && s.Method == RefundSettlementMethod.Cash && !string.IsNullOrWhiteSpace(sessionId))
        {
            using SqliteCommand cash = connection.CreateCommand();
            cash.Transaction = tx;
            cash.CommandText = """
                UPDATE cashier_sessions
                SET expected_cash_cents = expected_cash_cents - $amount
                WHERE id = $sessionId AND status = $open;
                """;
            cash.Parameters.AddWithValue("$amount", amountCents);
            cash.Parameters.AddWithValue("$sessionId", sessionId);
            cash.Parameters.AddWithValue("$open", (int)CashierSessionStatus.Open);
            cash.ExecuteNonQuery();
        }

        using SqliteCommand insert = connection.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = """
            INSERT INTO official_document_lifecycle_settlements (
                id, operation_id, journal_entry_id, method, amount_cents, status,
                external_reference, cashier_session_id, created_at, completed_at
            ) VALUES (
                $id, $op, $journal, $method, $amount, $status,
                $ext, $session, $createdAt, $completedAt
            );
            """;
        insert.Parameters.AddWithValue("$id", settlementId.ToString("D"));
        insert.Parameters.AddWithValue("$op", operationId.ToString("D"));
        insert.Parameters.AddWithValue("$journal", journalId.ToString("D"));
        insert.Parameters.AddWithValue("$method", method);
        insert.Parameters.AddWithValue("$amount", amountCents);
        insert.Parameters.AddWithValue("$status", status);
        insert.Parameters.AddWithValue("$ext", (object?)s.ExternalReference ?? DBNull.Value);
        insert.Parameters.AddWithValue("$session", (object?)sessionId ?? DBNull.Value);
        insert.Parameters.AddWithValue("$createdAt", now.ToString("O", CultureInfo.InvariantCulture));
        insert.Parameters.AddWithValue("$completedAt",
            status == "Completed" ? now.ToString("O", CultureInfo.InvariantCulture) : DBNull.Value);
        insert.ExecuteNonQuery();
        return Result.Success();
    }

    private static void InsertRefundLine(
        SqliteConnection connection,
        SqliteTransaction tx,
        Guid operationId,
        Guid eventId,
        Guid journalId,
        LifecycleRefundLineResult line,
        DateTimeOffset now)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT INTO official_document_lifecycle_refund_lines (
                id, journal_entry_id, event_id, sale_item_id, quantity_x1000, refund_amount_cents, created_at,
                line_index, disposition, vatable_base_cents, vat_amount_cents, vat_exempt_cents,
                zero_rated_cents, sspt_cents, nonvat_exempt_cents, operation_id
            ) VALUES (
                $id, $journalId, $eventId, $saleItemId, $qty, $amount, $createdAt,
                $lineIndex, $disp, $vb, $va, $ve, $zr, $ss, $nv, $op
            );
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue("$journalId", journalId.ToString("D"));
        command.Parameters.AddWithValue("$eventId", eventId.ToString("D"));
        command.Parameters.AddWithValue("$saleItemId", line.SaleItemId.ToString("D"));
        command.Parameters.AddWithValue("$qty", line.QuantityX1000);
        command.Parameters.AddWithValue("$amount", line.RefundAmountCents);
        command.Parameters.AddWithValue("$createdAt", now.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$lineIndex", line.LineIndex);
        command.Parameters.AddWithValue("$disp", line.Disposition.ToString());
        command.Parameters.AddWithValue("$vb", line.VatableBaseCents);
        command.Parameters.AddWithValue("$va", line.VatAmountCents);
        command.Parameters.AddWithValue("$ve", line.VatExemptCents);
        command.Parameters.AddWithValue("$zr", line.ZeroRatedCents);
        command.Parameters.AddWithValue("$ss", line.SsptCents);
        command.Parameters.AddWithValue("$nv", line.NonVatExemptCents);
        command.Parameters.AddWithValue("$op", operationId.ToString("D"));
        command.ExecuteNonQuery();
    }

    private static void InsertAdjustmentDocument(
        SqliteConnection connection,
        SqliteTransaction tx,
        Guid adjustmentId,
        Guid operationId,
        FrozenSnapshotDocument original,
        string adjNumber,
        string json,
        string hash,
        LifecycleAccountingCommand command,
        DateTimeOffset now)
    {
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO official_document_adjustment_documents (
                id, journal_entry_id, operation_id, original_document_number, internal_document_number,
                document_kind, legal_series_allocated, snapshot_payload_json, snapshot_payload_hash,
                snapshot_hash_algorithm, reason, actor_user_id, created_at
            ) VALUES (
                $id, $journal, $op, $origDoc, $adjNo,
                'INTERNAL_ADJUSTMENT_DOCUMENT', 0, $json, $hash,
                'SHA-256', $reason, $actor, $createdAt
            );
            """;
        cmd.Parameters.AddWithValue("$id", adjustmentId.ToString("D"));
        cmd.Parameters.AddWithValue("$journal", original.JournalEntryId.ToString("D"));
        cmd.Parameters.AddWithValue("$op", operationId.ToString("D"));
        cmd.Parameters.AddWithValue("$origDoc", original.OfficialDocumentNumber);
        cmd.Parameters.AddWithValue("$adjNo", adjNumber);
        cmd.Parameters.AddWithValue("$json", json);
        cmd.Parameters.AddWithValue("$hash", hash);
        cmd.Parameters.AddWithValue("$reason", command.Reason.Trim());
        cmd.Parameters.AddWithValue("$actor", command.ActorUserId.ToString("D"));
        cmd.Parameters.AddWithValue("$createdAt", now.ToString("O", CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }

    private static void InsertOperation(
        SqliteConnection connection,
        SqliteTransaction tx,
        Guid operationId,
        Guid journalId,
        string action,
        string stateAfter,
        LifecycleAccountingCommand command,
        Guid eventId,
        Guid? adjustmentId,
        Guid? settlementId,
        string? taxJson,
        DateTimeOffset now)
    {
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO official_document_lifecycle_operations (
                id, journal_entry_id, action, lifecycle_state_after, reason, actor_user_id, terminal_id,
                event_id, adjustment_document_id, settlement_id, tax_reversal_json, created_at
            ) VALUES (
                $id, $journal, $action, $state, $reason, $actor, $terminal,
                $event, $adj, $settle, $tax, $createdAt
            );
            """;
        cmd.Parameters.AddWithValue("$id", operationId.ToString("D"));
        cmd.Parameters.AddWithValue("$journal", journalId.ToString("D"));
        cmd.Parameters.AddWithValue("$action", action);
        cmd.Parameters.AddWithValue("$state", stateAfter);
        cmd.Parameters.AddWithValue("$reason", command.Reason.Trim());
        cmd.Parameters.AddWithValue("$actor", command.ActorUserId.ToString("D"));
        cmd.Parameters.AddWithValue("$terminal", (object?)command.TerminalId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$event", eventId.ToString("D"));
        cmd.Parameters.AddWithValue("$adj", adjustmentId is Guid a ? a.ToString("D") : DBNull.Value);
        cmd.Parameters.AddWithValue("$settle", settlementId is Guid s ? s.ToString("D") : DBNull.Value);
        cmd.Parameters.AddWithValue("$tax", (object?)taxJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$createdAt", now.ToString("O", CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }

    private static void AppendEjEvent(
        SqliteConnection connection,
        SqliteTransaction tx,
        Guid eventId,
        Guid journalEntryId,
        string eventType,
        int voidMarker,
        int refundMarker,
        int cancelMarker,
        Guid actor,
        string reason,
        DateTimeOffset now,
        string? terminalId)
    {
        int reprintMarker = eventType == "REPRINT" ? 1 : 0;
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT INTO bir_electronic_journal_entry_events (
                id, journal_entry_id, event_type,
                original_marker, reprint_marker, void_marker, refund_marker, cancel_marker,
                cashier_user_id, reason, preservation_batch_id, created_at, metadata_json
            ) VALUES (
                $id, $journalId, $eventType,
                0, $reprint, $void, $refund, $cancel,
                $actor, $reason, NULL, $createdAt, $metadata
            );
            """;
        command.Parameters.AddWithValue("$id", eventId.ToString("D"));
        command.Parameters.AddWithValue("$journalId", journalEntryId.ToString("D"));
        command.Parameters.AddWithValue("$eventType", eventType);
        command.Parameters.AddWithValue("$reprint", reprintMarker);
        command.Parameters.AddWithValue("$void", voidMarker);
        command.Parameters.AddWithValue("$refund", refundMarker);
        command.Parameters.AddWithValue("$cancel", cancelMarker);
        command.Parameters.AddWithValue("$actor", actor.ToString("D"));
        command.Parameters.AddWithValue("$reason", reason.Trim());
        command.Parameters.AddWithValue("$createdAt", now.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$metadata", JsonSerializer.Serialize(new
        {
            terminalId,
            originalImmutable = true,
            sequenceAdvanced = false
        }));
        command.ExecuteNonQuery();
    }

    private static void StoreIdempotency(
        SqliteConnection connection,
        SqliteTransaction tx,
        LifecycleAccountingCommand command,
        string requestHash,
        Guid operationId,
        Guid eventId,
        LifecycleAccountingResult result,
        DateTimeOffset now)
    {
        LifecycleAccountingResult stored = result with { Output = null };
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO official_document_lifecycle_idempotency (
                idempotency_key, action, journal_entry_id, request_hash, result_event_id, result_json, created_at,
                output_content_sha256, operation_id
            ) VALUES (
                $key, $action, $journalId, $hash, $eventId, $json, $createdAt,
                $outHash, $op
            );
            """;
        cmd.Parameters.AddWithValue("$key", command.IdempotencyKey.Trim());
        cmd.Parameters.AddWithValue("$action", command.Action.ToString());
        cmd.Parameters.AddWithValue("$journalId", result.JournalEntryId.ToString("D"));
        cmd.Parameters.AddWithValue("$hash", requestHash);
        cmd.Parameters.AddWithValue("$eventId", eventId.ToString("D"));
        cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(stored));
        cmd.Parameters.AddWithValue("$createdAt", now.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$outHash", (object?)result.Output?.Manifest.CanonicalContentSha256 ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$op", operationId.ToString("D"));
        cmd.ExecuteNonQuery();
    }

    private static Result<InvoiceOutputPackage> RenderReprint(
        FrozenSnapshotDocument original,
        string reason,
        DateTimeOffset now)
    {
        OfficialInvoiceSnapshotIdentity identity = new(
            original.JournalEntryId,
            original.SaleId ?? Guid.Empty,
            Guid.Empty,
            original.OfficialDocumentNumber,
            "OFFICIAL_INVOICE",
            now,
            original.SnapshotPayloadJson,
            original.SnapshotPayloadHash,
            "SHA-256",
            HashVerified: false);

        Result<ParsedOfficialInvoiceSnapshot> parsed = OfficialInvoiceSnapshotParser.ParseAndVerify(identity);
        if (parsed.IsFailure)
        {
            return Result<InvoiceOutputPackage>.Fail(parsed.ErrorDetail);
        }

        InvoiceRenderModelBuilder builder = new();
        Result<InvoiceRenderModel> modelResult = builder.Build(parsed.Value, InvoiceOutputMode.InternalDemo);
        if (modelResult.IsFailure)
        {
            return Result<InvoiceOutputPackage>.Fail(modelResult.ErrorDetail);
        }

        InvoiceRenderModel model = modelResult.Value with
        {
            BoundaryNotes = modelResult.Value.BoundaryNotes
                .Concat(new[] { "REPRINT", "REPRINT reason: " + reason.Trim(), "Same official number; no sequence advance." })
                .ToArray(),
            RequiresInternalWatermark = true,
            InternalWatermark = "REPRINT — " + InvoiceOutputConstants.InternalWatermark
        };

        InvoiceHtmlRenderer html = new();
        InvoiceThermalTextRenderer thermal = new();
        Result<InvoiceOutputArtifact> htmlArt = html.Render(model);
        Result<InvoiceOutputArtifact> textArt = thermal.Render(model);
        if (htmlArt.IsFailure)
        {
            return Result<InvoiceOutputPackage>.Fail(htmlArt.ErrorDetail);
        }

        if (textArt.IsFailure)
        {
            return Result<InvoiceOutputPackage>.Fail(textArt.ErrorDetail);
        }

        if (!htmlArt.Value.Utf8Text.Contains("REPRINT", StringComparison.Ordinal) ||
            !textArt.Value.Utf8Text.Contains("REPRINT", StringComparison.Ordinal))
        {
            return Result<InvoiceOutputPackage>.Fail(Error.Failure(
                "DocumentLifecycle.ReprintMarkerMissing",
                "Rendered reprint does not contain REPRINT marker."));
        }

        InvoiceOutputManifest manifest = new(
            PackageId: "reprint-" + original.JournalEntryId.ToString("N")[..12],
            SaleId: original.SaleId ?? Guid.Empty,
            JournalEntryId: original.JournalEntryId,
            OfficialDocumentNumber: original.OfficialDocumentNumber,
            TaxpayerMode: model.TaxpayerMode,
            InvoiceVariant: model.Variant.ToString(),
            RendererName: InvoiceOutputConstants.HtmlRendererName,
            RendererVersion: InvoiceOutputConstants.RendererVersion,
            MediaFormat: htmlArt.Value.ContentType,
            CanonicalContentSha256: htmlArt.Value.ContentSha256,
            OutputMode: InvoiceOutputMode.InternalDemo,
            InternalWatermarkApplied: true,
            ProductionActivationEnabled: false,
            LiveOfficialPostingAllowed: false,
            ComplianceClaimed: false,
            MissingOptionalFields: model.MissingOptionalFields,
            ValidationNotes: model.BoundaryNotes,
            NoComplianceClaimNote: InvoiceOutputConstants.NoComplianceClaimNote);

        return Result<InvoiceOutputPackage>.Success(new InvoiceOutputPackage(
            model, manifest, new[] { htmlArt.Value, textArt.Value }));
    }

    private static Result<InvoiceOutputPackage> RenderInternalAdjustment(
        string adjJson,
        string adjHash,
        string adjNumber,
        FrozenSnapshotDocument original,
        string reason,
        DateTimeOffset now)
    {
        string html = $"""
            <html><body>
            <h1>INTERNAL ADJUSTMENT DOCUMENT</h1>
            <p>Internal document number: {adjNumber}</p>
            <p>Original official number: {original.OfficialDocumentNumber}</p>
            <p>Reason: {System.Net.WebUtility.HtmlEncode(reason)}</p>
            <p>Hash: {adjHash}</p>
            <p>INTERNAL — production activation OFF — not a legal BIR adjustment title.</p>
            <pre>{System.Net.WebUtility.HtmlEncode(adjJson)}</pre>
            </body></html>
            """;
        string thermal = "INTERNAL ADJUSTMENT DOCUMENT\n" +
                         "No: " + adjNumber + "\n" +
                         "Original: " + original.OfficialDocumentNumber + "\n" +
                         "Reason: " + reason + "\n" +
                         "Hash: " + adjHash + "\n" +
                         "INTERNAL — activation OFF\n";

        string htmlSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(html))).ToLowerInvariant();
        string textSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(thermal))).ToLowerInvariant();
        InvoiceOutputArtifact htmlArt = new(
            InvoiceOutputFormat.Html, "text/html", "adjustment.html", html, htmlSha);
        InvoiceOutputArtifact textArt = new(
            InvoiceOutputFormat.ThermalText, "text/plain", "adjustment.txt", thermal, textSha);

        // Reuse reprint model builder path for a minimal valid render model.
        OfficialInvoiceSnapshotIdentity identity = new(
            original.JournalEntryId,
            original.SaleId ?? Guid.Empty,
            Guid.Empty,
            original.OfficialDocumentNumber,
            "OFFICIAL_INVOICE",
            now,
            original.SnapshotPayloadJson,
            original.SnapshotPayloadHash,
            "SHA-256",
            HashVerified: false);
        Result<ParsedOfficialInvoiceSnapshot> parsed = OfficialInvoiceSnapshotParser.ParseAndVerify(identity);
        InvoiceRenderModel model;
        if (parsed.IsSuccess)
        {
            Result<InvoiceRenderModel> built = new InvoiceRenderModelBuilder().Build(parsed.Value, InvoiceOutputMode.InternalDemo);
            model = built.IsSuccess
                ? built.Value with
                {
                    DocumentTitle = "INTERNAL ADJUSTMENT DOCUMENT",
                    OfficialDocumentNumber = adjNumber,
                    BoundaryNotes = built.Value.BoundaryNotes
                        .Concat(new[] { "INTERNAL ADJUSTMENT DOCUMENT", "No legal series allocated.", "Original: " + original.OfficialDocumentNumber })
                        .ToArray(),
                    RequiresInternalWatermark = true,
                    InternalWatermark = InvoiceOutputConstants.InternalWatermark
                }
                : CreateFallbackAdjustmentModel(adjNumber, now, adjHash);
        }
        else
        {
            model = CreateFallbackAdjustmentModel(adjNumber, now, adjHash);
        }

        InvoiceOutputManifest manifest = new(
            PackageId: "adj-" + adjNumber,
            SaleId: original.SaleId ?? Guid.Empty,
            JournalEntryId: original.JournalEntryId,
            OfficialDocumentNumber: adjNumber,
            TaxpayerMode: "Internal",
            InvoiceVariant: "InternalAdjustment",
            RendererName: "InternalAdjustmentHtml",
            RendererVersion: "M4-RC2",
            MediaFormat: "text/html",
            CanonicalContentSha256: htmlSha,
            OutputMode: InvoiceOutputMode.InternalDemo,
            InternalWatermarkApplied: true,
            ProductionActivationEnabled: false,
            LiveOfficialPostingAllowed: false,
            ComplianceClaimed: false,
            MissingOptionalFields: Array.Empty<string>(),
            ValidationNotes: new[] { "INTERNAL ADJUSTMENT DOCUMENT" },
            NoComplianceClaimNote: InvoiceOutputConstants.NoComplianceClaimNote);

        return Result<InvoiceOutputPackage>.Success(new InvoiceOutputPackage(
            model, manifest, new[] { htmlArt, textArt }));
    }

    private static InvoiceRenderModel CreateFallbackAdjustmentModel(string adjNumber, DateTimeOffset now, string hash) =>
        new(
            InvoiceOutputConstants.RendererContractVersion,
            InvoiceDocumentVariant.VatAllVatable,
            "INTERNAL ADJUSTMENT DOCUMENT",
            "INTERNAL",
            adjNumber,
            now,
            "INTERNAL",
            null,
            "",
            "",
            "",
            null,
            null,
            null,
            null,
            "",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            Array.Empty<InvoiceRenderLine>(),
            0,
            0,
            0,
            0,
            0,
            0,
            Array.Empty<InvoiceRenderPayment>(),
            "Internal",
            Array.Empty<InvoiceRenderTaxBucket>(),
            null,
            Array.Empty<string>(),
            new[] { "INTERNAL ADJUSTMENT DOCUMENT", "No legal series allocated." },
            true,
            InvoiceOutputConstants.InternalWatermark,
            hash);

    private static string BuildInternalAdjustmentJson(
        FrozenSnapshotDocument original,
        IReadOnlyList<LifecycleRefundLineResult> lines,
        LifecycleTaxReversalTotals totals,
        string adjNumber,
        string reason,
        DateTimeOffset now)
    {
        var payload = new
        {
            schema = "SALRYN_INTERNAL_ADJUSTMENT_DOCUMENT_V1",
            documentKind = "INTERNAL_ADJUSTMENT_DOCUMENT",
            legalSeriesAllocated = false,
            internalDocumentNumber = adjNumber,
            originalOfficialDocumentNumber = original.OfficialDocumentNumber,
            originalJournalEntryId = original.JournalEntryId,
            reason,
            createdAt = now.ToString("O", CultureInfo.InvariantCulture),
            lines,
            taxReversal = totals
        };
        return JsonSerializer.Serialize(payload);
    }

    private static LifecycleAccountingResult BuildResult(
        LifecycleAccountingCommand command,
        Guid operationId,
        Guid eventId,
        FrozenSnapshotDocument original,
        DateTimeOffset now,
        string state,
        int version,
        string summary,
        LifecycleTaxReversalTotals? tax,
        IReadOnlyList<LifecycleRefundLineResult>? lines,
        Guid? settlementId,
        RefundSettlementStatus? settlementStatus,
        Guid? adjustmentId,
        string? adjNumber,
        string? adjHash,
        InvoiceOutputPackage? output) =>
        new(
            command.Action,
            operationId,
            eventId,
            original.JournalEntryId,
            original.OfficialDocumentNumber,
            command.Reason.Trim(),
            command.ActorUserId,
            now,
            SequenceAdvanced: false,
            OriginalImmutable: true,
            state,
            version,
            summary,
            tax,
            lines,
            settlementId,
            settlementStatus,
            adjustmentId,
            adjNumber,
            adjHash,
            output);

    private static string HashCommand(LifecycleAccountingCommand command)
    {
        string lines = command.RefundLines is null
            ? ""
            : string.Join(";", command.RefundLines.Select(l =>
                $"{l.SaleItemId}:{l.QuantityX1000}:{l.Disposition}:{l.ExpectedRefundAmountCents}"));
        string raw = string.Join("|",
            command.Action,
            command.JournalEntryId,
            command.OfficialDocumentNumber,
            command.SaleId,
            command.Reason,
            command.ActorUserId,
            command.TerminalId,
            command.ExpectedTotalRefundCents,
            command.Settlement?.Method,
            command.Settlement?.ExpectedAmountCents,
            command.Settlement?.ImmediateComplete,
            lines);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }
}
