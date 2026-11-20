using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data.Repositories.Ingredients;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Data.Transactions;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Sync;

public sealed class LocalSaleCommittedV1Coordinator
{
    private readonly ISqliteSharedTransactionBoundary _transactionBoundary;
    private readonly SharedSqliteCompletedSaleWriteParticipant _saleWriter;
    private readonly SqliteSyncOutboxStore _outbox;
    private readonly ISaleCommittedV1OutboxParticipant _outboxParticipant;

    public LocalSaleCommittedV1Coordinator(
        ISqliteSharedTransactionBoundary transactionBoundary,
        SharedSqliteCompletedSaleWriteParticipant saleWriter,
        SqliteSyncOutboxStore outbox)
        : this(transactionBoundary, saleWriter, outbox, new SaleCommittedV1OutboxParticipant(outbox))
    {
    }

    public LocalSaleCommittedV1Coordinator(
        ISqliteSharedTransactionBoundary transactionBoundary,
        SharedSqliteCompletedSaleWriteParticipant saleWriter,
        SqliteSyncOutboxStore outbox,
        ISaleCommittedV1OutboxParticipant outboxParticipant)
    {
        _transactionBoundary = transactionBoundary ?? throw new ArgumentNullException(nameof(transactionBoundary));
        _saleWriter = saleWriter ?? throw new ArgumentNullException(nameof(saleWriter));
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _outboxParticipant = outboxParticipant ?? throw new ArgumentNullException(nameof(outboxParticipant));
    }

    public async Task<Result<LocalSaleCommittedV1CommitResult>> CommitAsync(
        LocalSaleCommittedV1CommitRequest request,
        CancellationToken cancellationToken = default)
    {
        Result requestValidation = ValidateRequest(request);
        if (requestValidation.IsFailure)
            return Result<LocalSaleCommittedV1CommitResult>.Fail(requestValidation.ErrorDetail);

        SaleCommittedV1 payload;
        try
        {
            payload = SaleCommittedV1Mapper.FromCore(request.SalePackage);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return Result<LocalSaleCommittedV1CommitResult>.Fail(Error.Validation(
                "SaleCommittedV1.LocalPayloadMappingFailed", ex.Message));
        }

        SaleCommittedV1ValidationResult payloadValidation = SaleCommittedV1Validator.Validate(payload);
        if (!payloadValidation.IsValid)
        {
            return Result<LocalSaleCommittedV1CommitResult>.Fail(Error.Validation(
                payloadValidation.ErrorCode ?? "SaleCommittedV1.LocalPayloadInvalid",
                payloadValidation.ErrorMessage ?? "Committed sale payload is invalid."));
        }

        string payloadJson = SaleCommittedV1CanonicalJson.Serialize(payload);
        CompleteCheckoutSaleRequest salePackage = request.SalePackage;

        return await _transactionBoundary.ExecuteAsync(
            async (context, token) =>
            {
                token.ThrowIfCancellationRequested();

                SyncOutboxItem? existing = _outbox.Find(
                    context.Connection, context.Transaction, request.OperationId);
                if (existing is not null)
                {
                    return ReconcileExisting(
                        context.Connection,
                        context.Transaction,
                        request,
                        payload,
                        payloadJson,
                        existing);
                }

                if (ReadSaleReceipt(context.Connection, context.Transaction, payload.Sale.Id) is not null)
                {
                    return Result<LocalSaleCommittedV1CommitResult>.Fail(Error.Conflict(
                        "SaleCommittedV1.SaleWithoutOutboxConflict",
                        "Sale already exists without the expected matching SaleCommittedV1 outbox operation."));
                }

                Result<SharedSqliteCompletedSaleWriteResult> saleWrite =
                    await _saleWriter.ExecuteAsync(context, salePackage, token).ConfigureAwait(false);
                if (saleWrite.IsFailure)
                    return Result<LocalSaleCommittedV1CommitResult>.Fail(saleWrite.ErrorDetail);

                Result<SaleIngredientConsumptionDecision> consumption =
                    SqliteIngredientConsumptionParticipant.ApplySale(
                        context.Connection,
                        context.Transaction,
                        salePackage,
                        request.OperationId);
                if (consumption.IsFailure)
                    return Result<LocalSaleCommittedV1CommitResult>.Fail(consumption.ErrorDetail);

                SyncOperationEnvelopeV1 saleEnvelope = _outboxParticipant.Enqueue(
                    context,
                    new SyncEnqueueRequest(
                        request.DeviceId,
                        request.StoreId,
                        payload.Sale.CashierUserId,
                        SaleCommittedV1Contract.OperationType,
                        SaleCommittedV1Contract.SchemaVersion,
                        payload.Sale.Id,
                        BaseVersion: null,
                        request.OccurredAtLocal,
                        payloadJson,
                        request.OperationId));

                SyncOperationEnvelopeV1? ingredientEnvelope = null;
                if (consumption.Value.Lines.Count > 0)
                {
                    IngredientConsumptionCommittedV1 ingredientPayload =
                        ToIngredientPayload(consumption.Value, payload.Sale.CashierUserId, request.OccurredAtLocal);
                    string ingredientJson =
                        IngredientConsumptionCommittedV1CanonicalJson.Serialize(ingredientPayload);
                    Guid ingredientOperationId =
                        IngredientConsumptionCommittedV1Contract.DeriveOperationId(request.OperationId);

                    ingredientEnvelope = _outbox.Enqueue(
                        context.Connection,
                        context.Transaction,
                        new SyncEnqueueRequest(
                            request.DeviceId,
                            request.StoreId,
                            payload.Sale.CashierUserId,
                            IngredientConsumptionCommittedV1Contract.OperationType,
                            IngredientConsumptionCommittedV1Contract.SchemaVersion,
                            payload.Sale.Id,
                            BaseVersion: null,
                            request.OccurredAtLocal,
                            ingredientJson,
                            ingredientOperationId));
                }

                return Result<LocalSaleCommittedV1CommitResult>.Success(
                    new LocalSaleCommittedV1CommitResult(
                        payload.Sale.Id,
                        payload.Sale.ReceiptNumber,
                        saleEnvelope,
                        IsReplay: false,
                        ingredientEnvelope));
            },
            cancellationToken).ConfigureAwait(false);
    }

    private Result<LocalSaleCommittedV1CommitResult> ReconcileExisting(
        SqliteConnection connection,
        SqliteTransaction transaction,
        LocalSaleCommittedV1CommitRequest request,
        SaleCommittedV1 payload,
        string payloadJson,
        SyncOutboxItem existing)
    {
        SyncOperationEnvelopeV1 envelope = existing.Envelope;
        bool sameImmutableOperation =
            envelope.OperationId == request.OperationId &&
            envelope.DeviceId == request.DeviceId &&
            envelope.StoreId == request.StoreId &&
            envelope.ActorUserId == payload.Sale.CashierUserId &&
            envelope.AggregateId == payload.Sale.Id &&
            string.Equals(envelope.OperationType, SaleCommittedV1Contract.OperationType, StringComparison.Ordinal) &&
            envelope.SchemaVersion == SaleCommittedV1Contract.SchemaVersion &&
            string.Equals(envelope.PayloadJson, payloadJson, StringComparison.Ordinal) &&
            string.Equals(envelope.PayloadHash, SyncProtocolV1.ComputePayloadHash(payloadJson), StringComparison.Ordinal);

        if (!sameImmutableOperation)
        {
            return Result<LocalSaleCommittedV1CommitResult>.Fail(Error.Conflict(
                "SaleCommittedV1.OperationIdPayloadConflict",
                "Operation ID already exists with different immutable SaleCommittedV1 content."));
        }

        string? persistedReceipt = ReadSaleReceipt(connection, transaction, payload.Sale.Id);
        if (persistedReceipt is null ||
            !string.Equals(persistedReceipt, payload.Sale.ReceiptNumber, StringComparison.Ordinal))
        {
            return Result<LocalSaleCommittedV1CommitResult>.Fail(Error.Conflict(
                "SaleCommittedV1.LocalEvidenceMissing",
                "Matching outbox evidence exists but committed local sale evidence is missing or inconsistent."));
        }

        SaleIngredientConsumptionDecision? decision =
            SqliteIngredientConsumptionParticipant.ReadDecision(
                connection, transaction, payload.Sale.Id);
        if (decision is null || decision.SourceSaleOperationId != request.OperationId)
        {
            return Result<LocalSaleCommittedV1CommitResult>.Fail(Error.Conflict(
                "IngredientConsumption.LocalEvidenceMissing",
                "Sale replay requires the original immutable ingredient-consumption decision."));
        }

        SyncOperationEnvelopeV1? ingredientEnvelope = null;
        if (decision.Lines.Count > 0)
        {
            IngredientConsumptionCommittedV1 ingredientPayload =
                ToIngredientPayload(decision, payload.Sale.CashierUserId, request.OccurredAtLocal);
            string ingredientJson = IngredientConsumptionCommittedV1CanonicalJson.Serialize(ingredientPayload);
            Guid ingredientOperationId =
                IngredientConsumptionCommittedV1Contract.DeriveOperationId(request.OperationId);
            SyncOutboxItem? ingredientOutbox =
                _outbox.Find(connection, transaction, ingredientOperationId);

            if (ingredientOutbox is null ||
                ingredientOutbox.Envelope.DeviceId != request.DeviceId ||
                ingredientOutbox.Envelope.StoreId != request.StoreId ||
                ingredientOutbox.Envelope.AggregateId != payload.Sale.Id ||
                !string.Equals(
                    ingredientOutbox.Envelope.OperationType,
                    IngredientConsumptionCommittedV1Contract.OperationType,
                    StringComparison.Ordinal) ||
                ingredientOutbox.Envelope.SchemaVersion != IngredientConsumptionCommittedV1Contract.SchemaVersion ||
                !string.Equals(ingredientOutbox.Envelope.PayloadJson, ingredientJson, StringComparison.Ordinal))
            {
                return Result<LocalSaleCommittedV1CommitResult>.Fail(Error.Conflict(
                    "IngredientConsumption.OutboxEvidenceConflict",
                    "Sale replay ingredient-consumption outbox evidence is missing or inconsistent."));
            }

            ingredientEnvelope = ingredientOutbox.Envelope;
        }

        return Result<LocalSaleCommittedV1CommitResult>.Success(
            new LocalSaleCommittedV1CommitResult(
                payload.Sale.Id,
                persistedReceipt,
                envelope,
                IsReplay: true,
                ingredientEnvelope));
    }

    private static IngredientConsumptionCommittedV1 ToIngredientPayload(
        SaleIngredientConsumptionDecision decision,
        Guid actorUserId,
        DateTimeOffset occurredAt)
    {
        if (!decision.SourceSaleOperationId.HasValue ||
            string.IsNullOrWhiteSpace(decision.RecipeBookRevisionId) ||
            decision.Lines.Count == 0)
        {
            throw new InvalidOperationException(
                "Ingredient sync payload requires non-empty committed consumption evidence.");
        }

        return new IngredientConsumptionCommittedV1(
            decision.SourceSaleOperationId.Value,
            decision.SaleId,
            decision.RecipeBookRevisionId,
            decision.DecisionHash,
            actorUserId,
            occurredAt.ToUniversalTime(),
            decision.Lines.Select(line => new IngredientConsumptionLineV1(
                line.Id,
                line.SaleItemId,
                line.IngredientId,
                line.RecipeId,
                line.RecipeRevisionId,
                line.SourceBindingType,
                line.SourceBindingId,
                line.QuantityConsumedX1000,
                line.StockUnit)).ToArray());
    }

    private static string? ReadSaleReceipt(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid saleId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT receipt_no FROM sales WHERE id=$sale LIMIT 1;";
        command.Parameters.AddWithValue("$sale", saleId.ToString("D"));
        return command.ExecuteScalar() as string;
    }

    private static Result ValidateRequest(LocalSaleCommittedV1CommitRequest request)
    {
        if (request is null)
            return Result.Fail(Error.Validation("SaleCommittedV1.LocalRequestRequired", "Local committed-sale request is required."));

        if (request.OperationId == Guid.Empty || request.DeviceId == Guid.Empty || request.StoreId == Guid.Empty)
            return Result.Fail(Error.Validation("SaleCommittedV1.LocalIdentityRequired", "Stable operation, device and store IDs are required."));

        if (request.SalePackage is null || request.SalePackage.Sale is null)
            return Result.Fail(Error.Validation("SaleCommittedV1.LocalSaleRequired", "Committed sale package is required."));

        return Result.Success();
    }
}
