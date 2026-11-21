using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Data.Repositories.Ingredients;
using Salryn.Infrastructure.Data.Repositories.Orders;
using Salryn.Infrastructure.Data.Repositories.Sales;
using Salryn.Infrastructure.Data.Sync;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Services;

public sealed class SqliteCafeOrderCompletionCoordinator : SqliteRepositoryBase
{
    private readonly SqliteSyncOutboxStore _outbox;
    private readonly ICafeOrderCompletionFailureInjector? _failureInjector;

    public SqliteCafeOrderCompletionCoordinator(
        SqliteConnectionFactory connectionFactory,
        ICafeOrderCompletionFailureInjector? failureInjector = null)
        : base(connectionFactory)
    {
        _outbox = new SqliteSyncOutboxStore(connectionFactory);
        _failureInjector = failureInjector;
    }

    public Result<CafeOrderCompletionResult> Complete(
        CafeOrderCompletionMutation mutation,
        Action<SqliteConnection, SqliteTransaction, CafeOrderCompletionResult>? persistOutcome = null)
    {
        string? validation = ValidateMutation(mutation);
        if (validation is not null)
        {
            return Result<CafeOrderCompletionResult>.Fail(
                Error.Validation(
                    "CafeOrder.CompletionInvalid",
                    validation));
        }

        try
        {
            using SqliteConnection connection = CreateOpenConnection();
            using SqliteTransaction transaction = connection.BeginTransaction();

            Result authority = ValidateAuthority(
                connection,
                transaction,
                mutation.DeviceId,
                mutation.StoreId,
                mutation.ActorUserId);
            if (authority.IsFailure)
            {
                transaction.Rollback();
                return Result<CafeOrderCompletionResult>.Fail(authority.ErrorDetail);
            }

            if (mutation.BookingFinalization is not null &&
                !HasPermission(
                    connection,
                    transaction,
                    mutation.ActorUserId,
                    SalrynPermissions.BookingFulfill))
            {
                transaction.Rollback();
                return Result<CafeOrderCompletionResult>.Fail(
                    Error.Forbidden(
                        "CafeBooking.FulfillPermissionRequired",
                        "C9-linked Sale completion requires Booking.Fulfill authority."));
            }

            // Recheck credit authority before both posting and replaying a retained operation.
            if (mutation.Payments.Any(payment => payment.Method == PaymentMethod.AccountsReceivable && payment.AmountCents > 0)
                && !HasPermission(connection, transaction, mutation.ActorUserId, SalrynPermissions.ReceivablesCreateCreditSale))
            {
                transaction.Rollback();
                return Result<CafeOrderCompletionResult>.Fail(Error.Forbidden(
                    "CafeFinancial.CreditSalePermissionRequired", "Credit sales require Receivables.CreateCreditSale."));
            }

            Result<CafeOrderCompletionResult>? replay =
                TryReplay(connection, transaction, mutation);
            if (replay is not null)
            {
                if (replay.IsSuccess) persistOutcome?.Invoke(connection, transaction, replay.Value);
                transaction.Commit();
                return replay;
            }

            SqliteCafeOrderStore.OrderRow order =
                SqliteCafeOrderStore.ReadOrder(
                    connection,
                    transaction,
                    mutation.OrderId)
                ?? throw new OrderCompletionValidationException(
                    "CafeOrder.OrderMissing",
                    "Order was not found.");

            if (order.StoreId != mutation.StoreId ||
                order.CurrentVersion != mutation.ExpectedVersion)
            {
                throw new OrderCompletionValidationException(
                    "CafeOrder.ExpectedVersionConflict",
                    "Order version/store authority changed; last-write-wins is not allowed.");
            }

            if (!string.Equals(
                    order.State,
                    CafeOrderStates.Ready,
                    StringComparison.Ordinal))
            {
                throw new OrderCompletionValidationException(
                    "CafeOrder.NotReady",
                    "Only a Ready order may complete into Sale.");
            }

            if (!order.SubmittedRevisionId.HasValue ||
                order.SubmittedRevisionId.Value != order.CurrentRevisionId)
            {
                throw new OrderCompletionValidationException(
                    "CafeOrder.SubmittedRevisionConflict",
                    "Completion requires the exact frozen submitted revision.");
            }

            if (order.CompletedSaleId.HasValue)
            {
                throw new OrderCompletionValidationException(
                    "CafeOrder.AlreadyCompleted",
                    "Order is already linked to a completed Sale.");
            }

            Guid cashierSessionId =
                ReadOpenCashierSession(
                    connection,
                    transaction,
                    mutation.ActorUserId)
                ?? throw new OrderCompletionValidationException(
                    "CafeOrder.OpenCashierSessionRequired",
                    "Order completion requires an open cashier session.");

            CafeOrderRevisionSnapshot revision =
                SqliteCafeOrderStore.ReadRevision(
                    connection,
                    transaction,
                    order.CurrentRevisionId);

            Guid saleId = SqliteCafeOrderStore.DeterministicGuid(
                "salryn-c4-order-sale-v1",
                mutation.CompletionOperationId.ToString("D"));

            CafeBookingFinalizationContext? bookingFinalization = null;
            if (mutation.BookingFinalization is not null)
            {
                Result<CafeBookingFinalizationContext> prepared =
                    SqliteCafeBookingFinalizationParticipant.Prepare(
                        connection,
                        transaction,
                        mutation.BookingFinalization,
                        mutation.OrderId,
                        mutation.StoreId,
                        mutation.SaleCommittedOperationId);
                if (prepared.IsFailure)
                {
                    transaction.Rollback();
                    return Result<CafeOrderCompletionResult>.Fail(
                        prepared.ErrorDetail);
                }

                bookingFinalization = prepared.Value;

                long netBeforeTender = EstimateNetSaleTotal(
                    revision,
                    mutation);
                if (netBeforeTender >= 0 &&
                    bookingFinalization.BookingDepositAppliedCents > netBeforeTender)
                {
                    transaction.Rollback();
                    return Result<CafeOrderCompletionResult>.Fail(
                        Error.Conflict(
                            "CafeBooking.ExcessDepositRefundRequired",
                            "Retained Booking deposit exceeds the final Sale total. Explicitly refund the excess before fulfillment."));
                }
            }

            ReceiptAllocation receipt =
                AllocateReceipt(
                    connection,
                    transaction,
                    mutation.OccurredAtUtc);

            Result<bool> deliveryFeeAuthority =
                ValidateC8DeliveryFeeAuthority(
                    connection,
                    transaction,
                    mutation);
            if (deliveryFeeAuthority.IsFailure)
            {
                transaction.Rollback();
                return Result<CafeOrderCompletionResult>.Fail(
                    deliveryFeeAuthority.ErrorDetail);
            }

            bool allowDeliveryFee =
                deliveryFeeAuthority.Value;

            bool useC5FinancialAuthority =
                UsesC5FinancialAuthority(mutation);

            if (useC5FinancialAuthority)
            {
                Result financialAuthority =
                    ValidateC5FinancialAuthority(
                        connection,
                        transaction,
                        mutation);
                if (financialAuthority.IsFailure)
                {
                    transaction.Rollback();
                    return Result<CafeOrderCompletionResult>.Fail(
                        financialAuthority.ErrorDetail);
                }
            }

            C5FinancialSaleBuild? c5Build =
                useC5FinancialAuthority
                    ? BuildC5FinancialSale(
                        connection,
                        transaction,
                        revision,
                        saleId,
                        receipt.ReceiptNumber,
                        mutation,
                        cashierSessionId,
                        allowDeliveryFee,
                        bookingFinalization?.BookingDepositAppliedCents ?? 0)
                    : null;

            CompleteCheckoutSaleRequest salePackage =
                c5Build?.SalePackage
                ?? BuildSalePackage(
                    connection,
                    transaction,
                    revision,
                    saleId,
                    receipt.ReceiptNumber,
                    mutation.ActorUserId,
                    cashierSessionId,
                    mutation.OccurredAtUtc,
                    mutation.CompletionOperationId,
                    mutation.Payments);

            Result saleWrite = SqliteSaleWriteRepository.WriteCompletedSale(
                connection,
                transaction,
                salePackage.Sale,
                salePackage.Items,
                salePackage.Payments,
                salePackage.StockMovements,
                grossSubtotalBeforeAllocatedDiscounts:
                    useC5FinancialAuthority,
                bookingDepositAppliedCents:
                    bookingFinalization?.BookingDepositAppliedCents ?? 0);

            if (saleWrite.IsFailure)
            {
                transaction.Rollback();
                return Result<CafeOrderCompletionResult>.Fail(
                    saleWrite.ErrorDetail);
            }

            if (c5Build is not null)
            {
                Result<bool> financialWrite =
                    SqliteCafeFinancialParticipant.Persist(
                        connection,
                        transaction,
                        c5Build.Decision);
                if (financialWrite.IsFailure)
                {
                    transaction.Rollback();
                    return Result<CafeOrderCompletionResult>.Fail(
                        financialWrite.ErrorDetail);
                }
            }

            Dictionary<Guid, Guid> saleItemByOrderLine =
                revision.Lines
                .ToDictionary(
                    x => x.OrderLineId,
                    x => DeterministicSaleItemId(
                        mutation.CompletionOperationId,
                        x.OrderLineId));

            InsertCorrelation(
                connection,
                transaction,
                mutation,
                order.CurrentRevisionId,
                saleId,
                saleItemByOrderLine);

            Result<SaleIngredientConsumptionDecision> consumption =
                SqliteOrderIngredientConsumptionParticipant.ApplyOrderCompletion(
                    connection,
                    transaction,
                    mutation.OrderId,
                    saleId,
                    mutation.SaleCommittedOperationId,
                    mutation.ActorUserId,
                    mutation.OccurredAtUtc,
                    saleItemByOrderLine);

            if (consumption.IsFailure)
            {
                transaction.Rollback();
                return Result<CafeOrderCompletionResult>.Fail(
                    consumption.ErrorDetail);
            }

            string saleOperationType;
            int saleSchemaVersion;
            string saleJson;

            if (bookingFinalization is not null)
            {
                if (c5Build is null)
                    throw new OrderCompletionValidationException(
                        "CafeBooking.FinalizationFinancialAuthorityMissing",
                        "C9 finalization requires the migration-38 C5 financial decision.");

                SaleCommittedV3 salePayload =
                    SqliteCafeBookingFinalizationParticipant.BuildPayload(
                        bookingFinalization,
                        salePackage,
                        c5Build.Decision);

                string? validationV3 =
                    SaleCommittedV3Validator.Validate(salePayload);
                if (validationV3 is not null)
                    throw new OrderCompletionValidationException(
                        "SaleCommittedV3.PayloadInvalid",
                        validationV3);

                saleJson =
                    SaleCommittedV3CanonicalJson.Serialize(salePayload);
                SaleCommittedV3ParseResult parsedV3 =
                    SaleCommittedV3CanonicalJson.Parse(saleJson);
                if (!parsedV3.IsValid)
                    throw new OrderCompletionValidationException(
                        parsedV3.ErrorCode ??
                            "SaleCommittedV3.PayloadInvalid",
                        parsedV3.ErrorMessage ??
                            "SaleCommittedV3 payload is invalid.");

                string saleHash =
                    SaleCommittedV3CanonicalJson.ComputeHash(saleJson);

                Result c9Persist =
                    SqliteCafeBookingFinalizationParticipant.Persist(
                        connection,
                        transaction,
                        bookingFinalization,
                        salePayload,
                        mutation.SaleCommittedOperationId,
                        mutation.StoreId,
                        mutation.DeviceId,
                        mutation.ActorUserId,
                        mutation.OccurredAtUtc,
                        saleHash);
                if (c9Persist.IsFailure)
                {
                    transaction.Rollback();
                    return Result<CafeOrderCompletionResult>.Fail(
                        c9Persist.ErrorDetail);
                }

                _failureInjector?
                    .ThrowAfterC9FinalizationBeforeOutbox();

                saleOperationType =
                    SaleCommittedV3Contract.OperationType;
                saleSchemaVersion =
                    SaleCommittedV3Contract.SchemaVersion;
            }
            else if (c5Build is null)
            {
                SaleCommittedV1 salePayload =
                    SaleCommittedV1Mapper.FromCore(salePackage);
                saleOperationType =
                    SaleCommittedV1Contract.OperationType;
                saleSchemaVersion =
                    SaleCommittedV1Contract.SchemaVersion;
                saleJson =
                    SaleCommittedV1CanonicalJson.Serialize(
                        salePayload);
            }
            else
            {
                SaleCommittedV2 salePayload =
                    SaleCommittedV2Mapper.FromCore(
                        salePackage,
                        c5Build.Decision);
                SaleCommittedV2ValidationResult validationV2 =
                    SaleCommittedV2Validator.Validate(
                        salePayload);
                if (!validationV2.IsValid)
                {
                    throw new OrderCompletionValidationException(
                        validationV2.ErrorCode ??
                            "SaleCommittedV2.PayloadInvalid",
                        validationV2.ErrorMessage ??
                            "SaleCommittedV2 payload is invalid.");
                }

                saleOperationType =
                    SaleCommittedV2Contract.OperationType;
                saleSchemaVersion =
                    SaleCommittedV2Contract.SchemaVersion;
                saleJson =
                    SaleCommittedV2CanonicalJson.Serialize(
                        salePayload);
            }

            _outbox.Enqueue(
                connection,
                transaction,
                new SyncEnqueueRequest(
                    mutation.DeviceId,
                    mutation.StoreId,
                    mutation.ActorUserId,
                    saleOperationType,
                    saleSchemaVersion,
                    saleId,
                    null,
                    mutation.OccurredAtUtc,
                    saleJson,
                    mutation.SaleCommittedOperationId));

            if (consumption.Value.Lines.Count > 0)
            {
                IngredientConsumptionCommittedV1 ingredientPayload =
                    ToIngredientPayload(
                        consumption.Value,
                        mutation.ActorUserId,
                        mutation.OccurredAtUtc);
                string ingredientJson =
                    IngredientConsumptionCommittedV1CanonicalJson.Serialize(
                        ingredientPayload);
                Guid ingredientOperationId =
                    IngredientConsumptionCommittedV1Contract.DeriveOperationId(
                        mutation.SaleCommittedOperationId);

                _outbox.Enqueue(
                    connection,
                    transaction,
                    new SyncEnqueueRequest(
                        mutation.DeviceId,
                        mutation.StoreId,
                        mutation.ActorUserId,
                        IngredientConsumptionCommittedV1Contract.OperationType,
                        IngredientConsumptionCommittedV1Contract.SchemaVersion,
                        saleId,
                        null,
                        mutation.OccurredAtUtc,
                        ingredientJson,
                        ingredientOperationId));
            }

            int resultingVersion = checked(mutation.ExpectedVersion + 1);
            CafeOrderSaleItemLinkV1[] orderLinks =
                saleItemByOrderLine
                .Select(x => new CafeOrderSaleItemLinkV1(
                    x.Value,
                    x.Key))
                .OrderBy(x => x.OrderLineId)
                .ToArray();

            CafeOrderOperationV1 orderPayload =
                SqliteCafeOrderStore.BuildLifecyclePayload(
                    CafeOrderOperationContract.OrderCompletedV1,
                    order,
                    mutation.ExpectedVersion,
                    resultingVersion,
                    CafeOrderLifecycleActions.Completed,
                    CafeOrderStates.Ready,
                    CafeOrderStates.Completed,
                    mutation.ActorUserId,
                    mutation.OccurredAtUtc,
                    null,
                    order.SubmittedRevisionId,
                    order.SubmittedRecipeBookRevisionId,
                    saleId,
                    mutation.SaleCommittedOperationId,
                    receipt.ReceiptNumber,
                    orderLinks);

            string orderJson =
                CafeOrderOperationCanonicalJson.Serialize(orderPayload);
            string orderHash =
                CafeOrderOperationCanonicalJson.ComputeHash(orderJson);

            using (SqliteCommand update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE cafe_orders
                    SET current_version=$version,
                        state='Completed',
                        completed_sale_id=$sale,
                        updated_at_utc=$at
                    WHERE order_id=$order
                      AND current_version=$expected
                      AND state='Ready'
                      AND completed_sale_id IS NULL;
                    """;
                update.Parameters.AddWithValue("$version", resultingVersion);
                update.Parameters.AddWithValue("$sale", saleId.ToString("D"));
                update.Parameters.AddWithValue("$at", mutation.OccurredAtUtc.ToUniversalTime().ToString("O"));
                update.Parameters.AddWithValue("$order", mutation.OrderId.ToString("D"));
                update.Parameters.AddWithValue("$expected", mutation.ExpectedVersion);
                if (update.ExecuteNonQuery() != 1)
                {
                    throw new OrderCompletionValidationException(
                        "CafeOrder.CompletionConcurrentConflict",
                        "Order lost completion transition ownership.");
                }
            }

            SqliteCafeOrderStore.InsertOperationAndLifecycle(
                connection,
                transaction,
                mutation.CompletionOperationId,
                mutation.OrderId,
                CafeOrderOperationContract.OrderCompletedV1,
                orderHash,
                mutation.ExpectedVersion,
                resultingVersion,
                order.CurrentRevisionId,
                CafeOrderStates.Completed,
                saleId,
                mutation.ActorUserId,
                mutation.OccurredAtUtc,
                CafeOrderLifecycleActions.Completed,
                CafeOrderStates.Ready,
                CafeOrderStates.Completed,
                null,
                order.SubmittedRevisionId,
                order.SubmittedRecipeBookRevisionId,
                saleId);

            _outbox.Enqueue(
                connection,
                transaction,
                new SyncEnqueueRequest(
                    mutation.DeviceId,
                    mutation.StoreId,
                    mutation.ActorUserId,
                    CafeOrderOperationContract.OrderCompletedV1,
                    CafeOrderOperationContract.SchemaVersion,
                    mutation.OrderId,
                    mutation.ExpectedVersion,
                    mutation.OccurredAtUtc,
                    orderJson,
                    mutation.CompletionOperationId));

            var committed = new CafeOrderCompletionResult(
                mutation.OrderId, resultingVersion, saleId, receipt.ReceiptNumber, false);
            // Outcome, canonical ledgers, lifecycle and outbox share this commit.
            // A response persistence fault rolls the entire operation back.
            persistOutcome?.Invoke(connection, transaction, committed);
            transaction.Commit();
            return Result<CafeOrderCompletionResult>.Success(committed);
        }
        catch (OrderCompletionValidationException ex)
        {
            return Result<CafeOrderCompletionResult>.Fail(
                Error.Conflict(ex.Code, ex.Message));
        }
        catch (Exception ex) when (
            ex is SqliteException or InvalidOperationException or FormatException or OverflowException)
        {
            return Result<CafeOrderCompletionResult>.Fail(
                Error.Unexpected(
                    "CafeOrder.CompletionFailed",
                    ex.Message));
        }
    }

    private Result<CafeOrderCompletionResult>? TryReplay(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeOrderCompletionMutation mutation)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT l.sale_id,l.sale_committed_operation_id,s.receipt_no,
                   o.resulting_version,l.order_revision_id
            FROM cafe_order_sale_links l
            INNER JOIN sales s ON s.id=l.sale_id
            INNER JOIN cafe_order_operations o
              ON o.operation_id=l.completion_operation_id
            WHERE l.completion_operation_id=$operation
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "$operation",
            mutation.CompletionOperationId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            SyncOutboxItem? operation =
                _outbox.Find(
                    connection,
                    transaction,
                    mutation.CompletionOperationId);
            if (operation is null)
            {
                return null;
            }

            return Result<CafeOrderCompletionResult>.Fail(
                Error.Conflict(
                    "CafeOrder.CompletionEvidenceConflict",
                    "Completion operation exists without the required order/sale correlation."));
        }

        Guid saleId = Guid.Parse(reader.GetString(0));
        Guid saleOperationId = Guid.Parse(reader.GetString(1));
        string receipt = reader.GetString(2);
        int version = reader.GetInt32(3);
        Guid revisionId = Guid.Parse(reader.GetString(4));
        reader.Close();

        if (saleOperationId != mutation.SaleCommittedOperationId)
        {
            return Result<CafeOrderCompletionResult>.Fail(
                Error.Conflict(
                    "CafeOrder.CompletionOperationConflict",
                    "Completion OperationId is already bound to a different SaleCommitted operation."));
        }

        SyncOutboxItem? completionEvidence = _outbox.Find(connection, transaction, mutation.CompletionOperationId);
        if (completionEvidence is null || completionEvidence.Envelope.DeviceId != mutation.DeviceId ||
            completionEvidence.Envelope.StoreId != mutation.StoreId || completionEvidence.Envelope.ActorUserId != mutation.ActorUserId ||
            completionEvidence.Envelope.AggregateId != mutation.OrderId || completionEvidence.Envelope.BaseVersion != mutation.ExpectedVersion)
        {
            return Result<CafeOrderCompletionResult>.Fail(Error.Conflict(
                "CafeOrder.CompletionReplayAuthorityConflict",
                "Completion retry must match the original operator, device, store, order and version."));
        }

        SyncOutboxItem? saleOutbox =
            _outbox.Find(
                connection,
                transaction,
                mutation.SaleCommittedOperationId);
        if (saleOutbox is null)
        {
            return Result<CafeOrderCompletionResult>.Fail(
                Error.Conflict(
                    "CafeOrder.SaleOutboxEvidenceMissing",
                    "Completion replay requires retained SaleCommitted outbox evidence."));
        }

        bool requestsC5 =
            UsesC5FinancialAuthority(mutation);

        if (string.Equals(
                saleOutbox.Envelope.OperationType,
                SaleCommittedV1Contract.OperationType,
                StringComparison.Ordinal))
        {
            if (requestsC5)
            {
                return Result<CafeOrderCompletionResult>.Fail(
                    Error.Conflict(
                        "CafeOrder.CompletionFinancialVersionConflict",
                        "Completion retry requests C5 financial terms but retained Sale evidence is SaleCommittedV1."));
            }

            SaleCommittedV1CanonicalParseResult parsedV1 =
                SaleCommittedV1CanonicalJson.Parse(
                    saleOutbox.Envelope.PayloadJson);
            if (!parsedV1.IsValid ||
                parsedV1.Payload is null ||
                parsedV1.Payload.Sale.Id != saleId ||
                !PaymentsMatch(
                    parsedV1.Payload,
                    mutation.Payments))
            {
                return Result<CafeOrderCompletionResult>.Fail(
                    Error.Conflict(
                        "CafeOrder.CompletionPaymentConflict",
                        "Completion retry payment content differs from retained Sale evidence."));
            }
        }
        else if (string.Equals(
                     saleOutbox.Envelope.OperationType,
                     SaleCommittedV2Contract.OperationType,
                     StringComparison.Ordinal))
        {
            if (mutation.BookingFinalization is not null)
            {
                return Result<CafeOrderCompletionResult>.Fail(
                    Error.Conflict(
                        "CafeOrder.CompletionFinancialVersionConflict",
                        "C9-linked completion retry requires retained SaleCommittedV3 evidence, not SaleCommittedV2."));
            }

            if (!requestsC5)
            {
                return Result<CafeOrderCompletionResult>.Fail(
                    Error.Conflict(
                        "CafeOrder.CompletionFinancialVersionConflict",
                        "Completion retry omits the C5 financial terms retained in SaleCommittedV2 evidence."));
            }

            SaleCommittedV2CanonicalParseResult parsedV2 =
                SaleCommittedV2CanonicalJson.Parse(
                    saleOutbox.Envelope.PayloadJson);
            if (!parsedV2.IsValid ||
                parsedV2.Payload is null ||
                parsedV2.Payload.SaleEvidence.Sale.Id != saleId ||
                !PaymentsMatch(
                    parsedV2.Payload.SaleEvidence,
                    mutation.Payments) ||
                parsedV2.Payload.SaleEvidence.Sale.CustomerId !=
                    mutation.CustomerId ||
                parsedV2.Payload.SaleEvidence.Sale.ReceivableDueDate !=
                    mutation.ReceivableDueDate ||
                !string.Equals(
                    NormalizeOptional(
                        parsedV2.Payload.SaleEvidence.Sale.ReceivableNote),
                    NormalizeOptional(
                        mutation.ReceivableNote),
                    StringComparison.Ordinal))
            {
                return Result<CafeOrderCompletionResult>.Fail(
                    Error.Conflict(
                        "CafeOrder.CompletionFinancialEvidenceConflict",
                        "Completion retry financial/customer content differs from retained SaleCommittedV2 evidence."));
            }

            CafeOrderRevisionSnapshot revision =
                SqliteCafeOrderStore.ReadRevision(
                    connection,
                    transaction,
                    revisionId);

            Result<CafeSaleFinancialDecision> recalculated =
                CalculateC5FinancialDecision(
                    revision,
                    saleId,
                    mutation,
                    parsedV2.Payload.SaleEvidence.Sale.CashierSessionId,
                    parsedV2.Payload.Fees.Any(
                        x => string.Equals(
                            x.FeeType,
                            CafeSaleFeeTypes.Delivery,
                            StringComparison.Ordinal)));

            if (recalculated.IsFailure ||
                recalculated.Value.FinancialDecisionId !=
                    parsedV2.Payload.FinancialDecisionId ||
                !string.Equals(
                    recalculated.Value.ContentHash,
                    parsedV2.Payload.FinancialDecisionHash,
                    StringComparison.Ordinal))
            {
                return Result<CafeOrderCompletionResult>.Fail(
                    Error.Conflict(
                        "CafeOrder.CompletionFinancialDecisionConflict",
                        "Completion retry differs from retained immutable C5 financial decision evidence."));
            }
        }
        else if (string.Equals(
                     saleOutbox.Envelope.OperationType,
                     SaleCommittedV3Contract.OperationType,
                     StringComparison.Ordinal))
        {
            CafeBookingFinalizationBinding? binding =
                mutation.BookingFinalization;
            if (binding is null)
            {
                return Result<CafeOrderCompletionResult>.Fail(
                    Error.Conflict(
                        "CafeOrder.CompletionFinancialVersionConflict",
                        "Retained SaleCommittedV3 requires the exact C9 Booking finalization binding on retry."));
            }

            SaleCommittedV3ParseResult parsedV3 =
                SaleCommittedV3CanonicalJson.Parse(
                    saleOutbox.Envelope.PayloadJson);
            SaleCommittedV3? payload = parsedV3.Payload;

            if (!parsedV3.IsValid ||
                payload is null ||
                payload.SaleEvidence.Sale.Id != saleId ||
                payload.OrderId != mutation.OrderId ||
                payload.OrderRevisionId != revisionId ||
                payload.ExpectedOrderVersion != mutation.ExpectedVersion ||
                payload.BookingId != binding.BookingId ||
                payload.BookingRevisionId != binding.BookingRevisionId ||
                payload.BookingOrderLinkId != binding.BookingOrderLinkId ||
                payload.ExpectedBookingVersion != binding.ExpectedBookingVersion ||
                payload.SaleEvidence.Sale.CashierUserId != mutation.ActorUserId ||
                !PaymentsMatch(
                    payload.SaleEvidence,
                    mutation.Payments) ||
                payload.SaleEvidence.Sale.CustomerId != mutation.CustomerId ||
                payload.SaleEvidence.Sale.ReceivableDueDate != mutation.ReceivableDueDate ||
                !string.Equals(
                    NormalizeOptional(payload.SaleEvidence.Sale.ReceivableNote),
                    NormalizeOptional(mutation.ReceivableNote),
                    StringComparison.Ordinal) ||
                !string.Equals(
                    SaleCommittedV3CanonicalJson.ComputeHash(
                        saleOutbox.Envelope.PayloadJson),
                    saleOutbox.Envelope.PayloadHash,
                    StringComparison.Ordinal))
            {
                return Result<CafeOrderCompletionResult>.Fail(
                    Error.Conflict(
                        "CafeBooking.FinalizationReplayPayloadConflict",
                        "Completion retry differs from retained SaleCommittedV3 Booking/Sale evidence."));
            }

            CafeOrderRevisionSnapshot revision =
                SqliteCafeOrderStore.ReadRevision(
                    connection,
                    transaction,
                    revisionId);

            Result<CafeSaleFinancialDecision> recalculated =
                CalculateC5FinancialDecision(
                    revision,
                    saleId,
                    mutation,
                    payload.SaleEvidence.Sale.CashierSessionId,
                    payload.Fees.Any(
                        x => string.Equals(
                            x.FeeType,
                            CafeSaleFeeTypes.Delivery,
                            StringComparison.Ordinal)),
                    payload.BookingDepositAppliedCents);

            if (recalculated.IsFailure ||
                recalculated.Value.FinancialDecisionId !=
                    payload.FinancialDecisionId ||
                recalculated.Value.OperationId !=
                    payload.FinancialOperationId ||
                !string.Equals(
                    recalculated.Value.ContentHash,
                    payload.FinancialDecisionHash,
                    StringComparison.Ordinal))
            {
                return Result<CafeOrderCompletionResult>.Fail(
                    Error.Conflict(
                        "CafeBooking.FinalizationFinancialDecisionConflict",
                        "Completion retry differs from retained V3 C5 financial decision evidence."));
            }

            Result c9Replay =
                SqliteCafeBookingFinalizationParticipant.VerifyReplay(
                    connection,
                    transaction,
                    payload,
                    saleOutbox.Envelope.PayloadHash);
            if (c9Replay.IsFailure)
            {
                return Result<CafeOrderCompletionResult>.Fail(
                    c9Replay.ErrorDetail);
            }
        }
        else
        {
            return Result<CafeOrderCompletionResult>.Fail(
                Error.Conflict(
                    "CafeOrder.SaleOutboxVersionUnsupported",
                    "Completion replay retained an unsupported SaleCommitted operation version."));
        }

        return Result<CafeOrderCompletionResult>.Success(
            new CafeOrderCompletionResult(
                mutation.OrderId,
                version,
                saleId,
                receipt,
                true));
    }

    private static bool UsesC5FinancialAuthority(
        CafeOrderCompletionMutation mutation) =>
        mutation.BookingFinalization is not null ||
        mutation.FinancialDecisionOperationId != Guid.Empty ||
        mutation.FinancialDecisionId != Guid.Empty ||
        mutation.CustomerId.HasValue ||
        mutation.ReceivableDueDate.HasValue ||
        !string.IsNullOrWhiteSpace(mutation.ReceivableNote) ||
        (mutation.LineDiscounts?.Count ?? 0) > 0 ||
        mutation.OrderDiscountCents != 0 ||
        (mutation.Fees?.Count ?? 0) > 0 ||
        mutation.TaxCents != 0 ||
        mutation.Payments.Any(
            x => x.Method ==
                PaymentMethod.AccountsReceivable);

    private static Result<bool> ValidateC8DeliveryFeeAuthority(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeOrderCompletionMutation mutation)
    {
        CafeSaleFeeMutation[] deliveryFees =
            (mutation.Fees ?? Array.Empty<CafeSaleFeeMutation>())
            .Where(x => string.Equals(
                x.FeeType,
                CafeSaleFeeTypes.Delivery,
                StringComparison.Ordinal))
            .ToArray();

        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT f.status,r.delivery_fee_cents
            FROM cafe_delivery_fulfillments f
            INNER JOIN cafe_delivery_revisions r
              ON r.revision_id=f.current_revision_id
            WHERE f.order_id=$order
              AND f.store_id=$store
            LIMIT 1;
            """;
        command.Parameters.AddWithValue(
            "$order",
            mutation.OrderId.ToString("D"));
        command.Parameters.AddWithValue(
            "$store",
            mutation.StoreId.ToString("D"));

        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return deliveryFees.Length == 0
                ? Result<bool>.Success(false)
                : Result<bool>.Fail(
                    Error.Validation(
                        "CafeDelivery.FeeWithoutDelivery",
                        "Delivery fee requires an active C8 delivery fulfillment."));
        }

        string status = reader.GetString(0);
        long expectedFee = reader.GetInt64(1);

        if (string.Equals(
                status,
                CafeDeliveryStatuses.Cancelled,
                StringComparison.Ordinal))
        {
            return deliveryFees.Length == 0
                ? Result<bool>.Success(false)
                : Result<bool>.Fail(
                    Error.Validation(
                        "CafeDelivery.CancelledFeeInvalid",
                        "Cancelled delivery cannot carry a Delivery fee."));
        }

        if (!string.Equals(
                status,
                CafeDeliveryStatuses.Pending,
                StringComparison.Ordinal))
        {
            return Result<bool>.Fail(
                Error.Conflict(
                    "CafeDelivery.CompletionStateInvalid",
                    "Order completion requires its C8 delivery fulfillment to be Pending."));
        }

        if (deliveryFees.Length != 1)
        {
            return Result<bool>.Fail(
                Error.Validation(
                    "CafeDelivery.ExactFeeRequired",
                    "Pending C8 delivery requires exactly one Delivery fee at Sale completion."));
        }

        if (deliveryFees[0].AmountCents != expectedFee)
        {
            return Result<bool>.Fail(
                Error.Conflict(
                    "CafeDelivery.FeeMismatch",
                    "Delivery fee must exactly match the current immutable C8 delivery revision."));
        }

        return Result<bool>.Success(true);
    }

    private static Result ValidateC5FinancialAuthority(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeOrderCompletionMutation mutation)
    {
        bool hasDiscount =
            mutation.OrderDiscountCents > 0 ||
            (mutation.LineDiscounts?.Any(
                x => x.AmountCents > 0) ?? false);

        if (hasDiscount &&
            !HasPermission(
                connection,
                transaction,
                mutation.ActorUserId,
                SalrynPermissions.SalesDiscountApply))
        {
            return Result.Fail(
                Error.Forbidden(
                    "CafeFinancial.DiscountPermissionRequired",
                    "Applying a C5 discount requires Sales.DiscountApply."));
        }

        if ((mutation.Fees?.Count ?? 0) > 0 &&
            !HasPermission(
                connection,
                transaction,
                mutation.ActorUserId,
                SalrynPermissions.SalesFeeApply))
        {
            return Result.Fail(
                Error.Forbidden(
                    "CafeFinancial.FeePermissionRequired",
                    "Applying a C5 fee requires Sales.FeeApply."));
        }

        return Result.Success();
    }

    private static bool HasPermission(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid actorUserId,
        string permission)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(*)
            FROM users u
            INNER JOIN role_permissions rp
              ON rp.role_id=u.role_id
            WHERE u.id=$user
              AND u.status=1
              AND rp.permission=$permission;
            """;
        command.Parameters.AddWithValue(
            "$user",
            actorUserId.ToString("D"));
        command.Parameters.AddWithValue(
            "$permission",
            permission);
        return Convert.ToInt64(
            command.ExecuteScalar() ?? 0L) == 1;
    }

    private static C5FinancialSaleBuild BuildC5FinancialSale(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeOrderRevisionSnapshot revision,
        Guid saleId,
        string receiptNumber,
        CafeOrderCompletionMutation mutation,
        Guid cashierSessionId,
        bool allowDeliveryFee,
        long bookingDepositAppliedCents)
    {
        Result<CafeSaleFinancialDecision> calculated =
            CalculateC5FinancialDecision(
                revision,
                saleId,
                mutation,
                cashierSessionId,
                allowDeliveryFee,
                bookingDepositAppliedCents);
        if (calculated.IsFailure)
        {
            throw new OrderCompletionValidationException(
                calculated.ErrorDetail.Code,
                calculated.ErrorDetail.Message);
        }

        CafeSaleFinancialDecision decision =
            calculated.Value;

        if (decision.AccountsReceivableTenderCents > 0 &&
            (!mutation.CustomerId.HasValue ||
             mutation.CustomerId.Value == Guid.Empty))
        {
            throw new OrderCompletionValidationException(
                "CafeFinancial.CustomerRequiredForReceivable",
                "AccountsReceivable tender requires a customer.");
        }

        Dictionary<Guid, CafeSaleFinancialLineDecision> decisionByItem =
            decision.LineDecisions.ToDictionary(
                x => x.SaleItemId);

        List<SaleItem> items = new();
        List<StockMovement> movements = new();

        foreach (CafeOrderLineSnapshot line in revision.Lines)
        {
            ProductAuthority product =
                ReadProductAuthority(
                    connection,
                    transaction,
                    line.ProductId);

            Guid saleItemId =
                DeterministicSaleItemId(
                    mutation.CompletionOperationId,
                    line.OrderLineId);

            Result<Quantity> quantity =
                Quantity.FromX1000(
                    line.QuantityX1000);
            if (quantity.IsFailure)
            {
                throw new OrderCompletionValidationException(
                    quantity.ErrorDetail.Code,
                    quantity.ErrorDetail.Message);
            }

            CafeSaleFinancialLineDecision lineDecision =
                decisionByItem[saleItemId];

            long lineNet = checked(
                line.LineTotalCents -
                lineDecision.TotalDiscountCents);

            items.Add(new SaleItem
            {
                Id = saleItemId,
                SaleId = saleId,
                ProductId = line.ProductId,
                ProductNameSnapshot =
                    line.ProductNameSnapshot,
                BarcodeSnapshot = null,
                UnitNameSnapshot =
                    line.UnitNameSnapshot,
                Quantity = quantity.Value,
                UnitPrice = Money.FromCents(
                    line.CalculatedUnitPriceCents),
                CostPrice = null,
                Discount = Money.FromCents(
                    lineDecision.TotalDiscountCents),
                LineTotal = Money.FromCents(
                    lineNet)
            });

            if (product.TrackStock)
            {
                movements.Add(new StockMovement
                {
                    Id = SqliteCafeOrderStore.DeterministicGuid(
                        "salryn-c4-order-stock-movement-v1",
                        mutation.CompletionOperationId.ToString("D") +
                        "|" +
                        line.OrderLineId.ToString("D")),
                    ProductId = line.ProductId,
                    MovementType =
                        StockMovementType.Sale,
                    QuantityChange = quantity.Value,
                    CostPrice = null,
                    ReferenceType = "SALE",
                    ReferenceId = saleId,
                    Reason =
                        "Cafe order completion stock-out",
                    UserId = mutation.ActorUserId,
                    CreatedAt =
                        mutation.OccurredAtUtc.ToUniversalTime()
                });
            }
        }

        List<Payment> payments = new();
        for (int index = 0;
             index < mutation.Payments.Count;
             index++)
        {
            CafeOrderPaymentMutation payment =
                mutation.Payments[index];

            payments.Add(new Payment
            {
                Id = SqliteCafeOrderStore.DeterministicGuid(
                    "salryn-c4-order-payment-v1",
                    mutation.CompletionOperationId.ToString("D") +
                    "|" + index),
                SaleId = saleId,
                Method = payment.Method,
                Amount = Money.FromCents(
                    payment.AmountCents),
                PaidAt =
                    mutation.OccurredAtUtc.ToUniversalTime()
            });
        }

        Sale sale = new()
        {
            Id = saleId,
            ReceiptNumber = receiptNumber,
            SaleDate =
                mutation.OccurredAtUtc.ToUniversalTime(),
            CashierUserId =
                mutation.ActorUserId,
            CashierSessionId =
                cashierSessionId,
            Subtotal = Money.FromCents(
                decision.GrossSubtotalCents),
            Discount = Money.FromCents(
                checked(
                    decision.LineDiscountTotalCents +
                    decision.OrderDiscountTotalCents)),
            Tax = Money.FromCents(
                decision.TaxCents),
            FeeTotal = Money.FromCents(
                decision.FeeTotalCents),
            Total = Money.FromCents(
                decision.NetSaleTotalCents),
            AmountPaid = Money.FromCents(
                decision.AmountPaidCents),
            Change = Money.FromCents(
                decision.ChangeCents),
            CustomerId =
                mutation.CustomerId,
            PaymentStatus =
                decision.AccountsReceivableTenderCents == 0
                    ? SalePaymentStatus.Paid
                    : decision.CashTenderCents == 0
                        ? SalePaymentStatus.Credit
                        : SalePaymentStatus.Partial,
            BalanceDue = Money.FromCents(
                decision.AccountsReceivableTenderCents),
            ReceivableDueDate =
                mutation.ReceivableDueDate,
            ReceivableNote =
                NormalizeOptional(
                    mutation.ReceivableNote),
            CustomerFullNameSnapshot = null,
            CustomerPhoneSnapshot = null,
            Status = SaleStatus.Completed,
            VoidedByUserId = null,
            VoidReason = null,
            VoidedAt = null,
            ReceiptSnapshot = null,
            Items = items,
            Payments = payments,
            CreatedAt =
                mutation.OccurredAtUtc.ToUniversalTime()
        };

        return new C5FinancialSaleBuild(
            new CompleteCheckoutSaleRequest(
                sale,
                items,
                payments,
                movements),
            decision);
    }

    private static Result<CafeSaleFinancialDecision>
        CalculateC5FinancialDecision(
            CafeOrderRevisionSnapshot revision,
            Guid saleId,
            CafeOrderCompletionMutation mutation,
            Guid cashierSessionId,
            bool allowDeliveryFee,
            long bookingDepositAppliedCents = 0)
    {
        IReadOnlyList<CafeOrderLineDiscountMutation> discounts =
            mutation.LineDiscounts ??
            Array.Empty<CafeOrderLineDiscountMutation>();

        Dictionary<Guid, long> discountByLine =
            new();

        foreach (CafeOrderLineDiscountMutation discount in
                 discounts)
        {
            if (discount.OrderLineId == Guid.Empty ||
                discount.AmountCents < 0 ||
                !discountByLine.TryAdd(
                    discount.OrderLineId,
                    discount.AmountCents))
            {
                return Result<CafeSaleFinancialDecision>.Fail(
                    Error.Validation(
                        "CafeFinancial.InvalidLineDiscount",
                        "Line discount requires a unique OrderLineId and non-negative amount."));
            }
        }

        HashSet<Guid> revisionLineIds =
            revision.Lines
                .Select(x => x.OrderLineId)
                .ToHashSet();

        if (discountByLine.Keys.Any(
                x => !revisionLineIds.Contains(x)))
        {
            return Result<CafeSaleFinancialDecision>.Fail(
                Error.Validation(
                    "CafeFinancial.UnknownOrderLineDiscount",
                    "Line discount references an OrderLine outside the frozen order revision."));
        }

        Guid financialOperationId =
            mutation.FinancialDecisionOperationId ==
            Guid.Empty
                ? SqliteCafeOrderStore.DeterministicGuid(
                    "salryn-c5-financial-operation-v1",
                    mutation.CompletionOperationId.ToString("D"))
                : mutation.FinancialDecisionOperationId;

        Guid financialDecisionId =
            mutation.FinancialDecisionId ==
            Guid.Empty
                ? SqliteCafeOrderStore.DeterministicGuid(
                    "salryn-c5-financial-decision-v1",
                    mutation.CompletionOperationId.ToString("D"))
                : mutation.FinancialDecisionId;

        long cashTender =
            mutation.Payments
                .Where(
                    x => x.Method ==
                        PaymentMethod.Cash)
                .Sum(x => x.AmountCents);

        long arTender =
            mutation.Payments
                .Where(
                    x => x.Method ==
                        PaymentMethod.AccountsReceivable)
                .Sum(x => x.AmountCents);

        CafeSaleFinancialDecisionRequest request = new(
            financialOperationId,
            financialDecisionId,
            mutation.StoreId,
            mutation.OrderId,
            revision.RevisionId,
            mutation.ExpectedVersion,
            saleId,
            cashierSessionId,
            mutation.ActorUserId,
            mutation.OccurredAtUtc,
            revision.Lines
                .Select(line =>
                    new CafeSaleFinancialLineInput(
                        DeterministicSaleItemId(
                            mutation.CompletionOperationId,
                            line.OrderLineId),
                        line.LineTotalCents,
                        discountByLine.GetValueOrDefault(
                            line.OrderLineId),
                        line.DisplayOrder))
                .ToArray(),
            mutation.OrderDiscountCents,
            mutation.DiscountReason,
            mutation.Fees ??
                Array.Empty<CafeSaleFeeMutation>(),
            mutation.TaxCents,
            cashTender,
            arTender)
        {
            BookingDepositAppliedCents =
                bookingDepositAppliedCents
        };

        return CafeSaleFinancialCalculator.Calculate(
            request,
            allowDeliveryFee);
    }

    private static long EstimateNetSaleTotal(
        CafeOrderRevisionSnapshot revision,
        CafeOrderCompletionMutation mutation)
    {
        long gross = revision.Lines.Sum(x => x.LineTotalCents);
        long lineDiscount = (mutation.LineDiscounts ??
            Array.Empty<CafeOrderLineDiscountMutation>())
            .Sum(x => x.AmountCents);
        long fees = (mutation.Fees ??
            Array.Empty<CafeSaleFeeMutation>())
            .Sum(x => x.AmountCents);

        return checked(
            gross -
            lineDiscount -
            mutation.OrderDiscountCents +
            fees +
            mutation.TaxCents);
    }

    private static string? NormalizeOptional(
        string? value)
    {
        string normalized =
            (value ?? string.Empty).Trim();
        return normalized.Length == 0
            ? null
            : normalized;
    }

    private sealed record C5FinancialSaleBuild(
        CompleteCheckoutSaleRequest SalePackage,
        CafeSaleFinancialDecision Decision);

    private static CompleteCheckoutSaleRequest BuildSalePackage(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeOrderRevisionSnapshot revision,
        Guid saleId,
        string receiptNumber,
        Guid actorUserId,
        Guid cashierSessionId,
        DateTimeOffset occurredAtUtc,
        Guid completionOperationId,
        IReadOnlyList<CafeOrderPaymentMutation> paymentMutations)
    {
        List<SaleItem> items = new();
        List<StockMovement> movements = new();
        long subtotalCents = 0L;

        foreach (CafeOrderLineSnapshot line in revision.Lines)
        {
            ProductAuthority product =
                ReadProductAuthority(
                    connection,
                    transaction,
                    line.ProductId);

            Guid saleItemId =
                DeterministicSaleItemId(
                    completionOperationId,
                    line.OrderLineId);

            Result<Quantity> quantity =
                Quantity.FromX1000(line.QuantityX1000);
            if (quantity.IsFailure)
            {
                throw new OrderCompletionValidationException(
                    quantity.ErrorDetail.Code,
                    quantity.ErrorDetail.Message);
            }

            subtotalCents = checked(
                subtotalCents + line.LineTotalCents);

            items.Add(new SaleItem
            {
                Id = saleItemId,
                SaleId = saleId,
                ProductId = line.ProductId,
                ProductNameSnapshot = line.ProductNameSnapshot,
                BarcodeSnapshot = null,
                UnitNameSnapshot = line.UnitNameSnapshot,
                Quantity = quantity.Value,
                UnitPrice = Money.FromCents(
                    line.CalculatedUnitPriceCents),
                CostPrice = null,
                Discount = Money.Zero,
                LineTotal = Money.FromCents(
                    line.LineTotalCents)
            });

            if (product.TrackStock)
            {
                movements.Add(new StockMovement
                {
                    Id = SqliteCafeOrderStore.DeterministicGuid(
                        "salryn-c4-order-stock-movement-v1",
                        completionOperationId.ToString("D") + "|" +
                        line.OrderLineId.ToString("D")),
                    ProductId = line.ProductId,
                    MovementType = StockMovementType.Sale,
                    QuantityChange = quantity.Value,
                    CostPrice = null,
                    ReferenceType = "SALE",
                    ReferenceId = saleId,
                    Reason = "Cafe order completion stock-out",
                    UserId = actorUserId,
                    CreatedAt = occurredAtUtc.ToUniversalTime()
                });
            }
        }

        List<Payment> payments = new();
        long amountPaid = 0L;
        for (int index = 0; index < paymentMutations.Count; index++)
        {
            CafeOrderPaymentMutation payment = paymentMutations[index];
            amountPaid = checked(amountPaid + payment.AmountCents);
            payments.Add(new Payment
            {
                Id = SqliteCafeOrderStore.DeterministicGuid(
                    "salryn-c4-order-payment-v1",
                    completionOperationId.ToString("D") + "|" + index),
                SaleId = saleId,
                Method = payment.Method,
                Amount = Money.FromCents(payment.AmountCents),
                PaidAt = occurredAtUtc.ToUniversalTime()
            });
        }

        if (amountPaid < subtotalCents)
        {
            throw new OrderCompletionValidationException(
                "CafeOrder.InsufficientPayment",
                "Existing Sale completion requires tender at least equal to the immutable order total.");
        }

        long cashTender = paymentMutations
            .Where(x => x.Method == PaymentMethod.Cash)
            .Sum(x => x.AmountCents);
        long change = checked(cashTender - subtotalCents);
        if (change < 0)
        {
            throw new OrderCompletionValidationException(
                "CafeOrder.InvalidCashTender",
                "Cash tender is insufficient for this order completion.");
        }

        Sale sale = new()
        {
            Id = saleId,
            ReceiptNumber = receiptNumber,
            SaleDate = occurredAtUtc.ToUniversalTime(),
            CashierUserId = actorUserId,
            CashierSessionId = cashierSessionId,
            Subtotal = Money.FromCents(subtotalCents),
            Discount = Money.Zero,
            Tax = Money.Zero,
            Total = Money.FromCents(subtotalCents),
            AmountPaid = Money.FromCents(amountPaid),
            Change = Money.FromCents(change),
            CustomerId = null,
            PaymentStatus = SalePaymentStatus.Paid,
            BalanceDue = Money.Zero,
            ReceivableDueDate = null,
            ReceivableNote = null,
            CustomerFullNameSnapshot = null,
            CustomerPhoneSnapshot = null,
            Status = SaleStatus.Completed,
            VoidedByUserId = null,
            VoidReason = null,
            VoidedAt = null,
            ReceiptSnapshot = null,
            Items = items,
            Payments = payments,
            CreatedAt = occurredAtUtc.ToUniversalTime()
        };

        return new CompleteCheckoutSaleRequest(
            sale,
            items,
            payments,
            movements);
    }

    private static void InsertCorrelation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CafeOrderCompletionMutation mutation,
        Guid revisionId,
        Guid saleId,
        IReadOnlyDictionary<Guid, Guid> saleItemByOrderLine)
    {
        using (SqliteCommand link = connection.CreateCommand())
        {
            link.Transaction = transaction;
            link.CommandText = """
                INSERT INTO cafe_order_sale_links(
                    order_id,order_revision_id,sale_id,completion_operation_id,
                    sale_committed_operation_id,completed_at_utc)
                VALUES($order,$revision,$sale,$completion,$saleOperation,$at);
                """;
            link.Parameters.AddWithValue("$order", mutation.OrderId.ToString("D"));
            link.Parameters.AddWithValue("$revision", revisionId.ToString("D"));
            link.Parameters.AddWithValue("$sale", saleId.ToString("D"));
            link.Parameters.AddWithValue("$completion", mutation.CompletionOperationId.ToString("D"));
            link.Parameters.AddWithValue("$saleOperation", mutation.SaleCommittedOperationId.ToString("D"));
            link.Parameters.AddWithValue("$at", mutation.OccurredAtUtc.ToUniversalTime().ToString("O"));
            link.ExecuteNonQuery();
        }

        foreach ((Guid orderLineId, Guid saleItemId) in saleItemByOrderLine)
        {
            using SqliteCommand link = connection.CreateCommand();
            link.Transaction = transaction;
            link.CommandText = """
                INSERT INTO sale_item_order_links(
                    sale_item_id,order_id,order_revision_id,order_line_id)
                VALUES($saleItem,$order,$revision,$line);
                """;
            link.Parameters.AddWithValue("$saleItem", saleItemId.ToString("D"));
            link.Parameters.AddWithValue("$order", mutation.OrderId.ToString("D"));
            link.Parameters.AddWithValue("$revision", revisionId.ToString("D"));
            link.Parameters.AddWithValue("$line", orderLineId.ToString("D"));
            link.ExecuteNonQuery();
        }
    }

    private static IngredientConsumptionCommittedV1 ToIngredientPayload(
        SaleIngredientConsumptionDecision decision,
        Guid actorUserId,
        DateTimeOffset occurredAtUtc)
    {
        if (!decision.SourceSaleOperationId.HasValue ||
            string.IsNullOrWhiteSpace(decision.RecipeBookRevisionId) ||
            decision.Lines.Count == 0)
        {
            throw new InvalidOperationException(
                "Ingredient sync payload requires applied order-consumption evidence.");
        }

        return new IngredientConsumptionCommittedV1(
            decision.SourceSaleOperationId.Value,
            decision.SaleId,
            decision.RecipeBookRevisionId,
            decision.DecisionHash,
            actorUserId,
            occurredAtUtc.ToUniversalTime(),
            decision.Lines.Select(line =>
                new IngredientConsumptionLineV1(
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

    private static ReceiptAllocation AllocateReceipt(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateTimeOffset occurredAtUtc)
    {
        int year = occurredAtUtc.ToUniversalTime().Year;

        using SqliteCommand read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = """
            SELECT id,prefix,current_number,padding_digits
            FROM receipt_sequences
            WHERE year=$year
            ORDER BY prefix,id
            LIMIT 1;
            """;
        read.Parameters.AddWithValue("$year", year);

        using SqliteDataReader reader = read.ExecuteReader();
        if (!reader.Read())
        {
            throw new OrderCompletionValidationException(
                "CafeOrder.ReceiptSequenceMissing",
                "No receipt sequence exists for the completion year.");
        }

        string id = reader.GetString(0);
        string prefix = reader.GetString(1);
        long current = reader.GetInt64(2);
        int padding = reader.GetInt32(3);
        reader.Close();

        long next = checked(current + 1);
        Result<ReceiptNumber> number =
            ReceiptNumber.Create(prefix, year, next, padding);
        if (number.IsFailure)
        {
            throw new OrderCompletionValidationException(
                "CafeOrder.ReceiptSequenceInvalid",
                number.ErrorDetail.Message);
        }

        using SqliteCommand advance = connection.CreateCommand();
        advance.Transaction = transaction;
        advance.CommandText = """
            UPDATE receipt_sequences
            SET current_number=$next,
                updated_at=$at
            WHERE id=$id
              AND current_number=$current;
            """;
        advance.Parameters.AddWithValue("$next", next);
        advance.Parameters.AddWithValue("$at", occurredAtUtc.ToUniversalTime().ToString("O"));
        advance.Parameters.AddWithValue("$id", id);
        advance.Parameters.AddWithValue("$current", current);
        if (advance.ExecuteNonQuery() != 1)
        {
            throw new OrderCompletionValidationException(
                "CafeOrder.ReceiptSequenceConcurrentConflict",
                "Receipt sequence changed during order completion.");
        }

        return new ReceiptAllocation(
            id,
            number.Value.Value);
    }

    private static Guid? ReadOpenCashierSession(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid actorUserId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id
            FROM cashier_sessions
            WHERE user_id=$user AND status=1
            ORDER BY opened_at DESC,id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$user", actorUserId.ToString("D"));
        object? value = command.ExecuteScalar();
        return value is string text && Guid.TryParse(text, out Guid id)
            ? id
            : null;
    }

    private static ProductAuthority ReadProductAuthority(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid productId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT track_stock,is_active
            FROM products
            WHERE id=$product
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$product", productId.ToString("D"));
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read() || reader.GetInt32(1) != 1)
        {
            throw new OrderCompletionValidationException(
                "CafeOrder.ProductUnavailable",
                "Order completion requires each bound Product to remain available to Sale authority.");
        }

        return new ProductAuthority(
            reader.GetInt32(0) != 0);
    }

    private static Result ValidateAuthority(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid deviceId,
        Guid storeId,
        Guid actorUserId)
    {
        using (SqliteCommand store = connection.CreateCommand())
        {
            store.Transaction = transaction;
            store.CommandText = "SELECT COUNT(*) FROM deployment_identity WHERE store_id=$store;";
            store.Parameters.AddWithValue("$store", storeId.ToString("D"));
            if (Convert.ToInt64(store.ExecuteScalar() ?? 0L) != 1)
            {
                return Result.Fail(
                    Error.Conflict(
                        "CafeOrder.StoreAuthorityMismatch",
                        "Completion StoreId does not match local deployment authority."));
            }
        }

        using (SqliteCommand permission = connection.CreateCommand())
        {
            permission.Transaction = transaction;
            permission.CommandText = """
                SELECT COUNT(*)
                FROM users u
                INNER JOIN role_permissions rp ON rp.role_id=u.role_id
                WHERE u.id=$user
                  AND u.status=1
                  AND rp.permission=$permission;
                """;
            permission.Parameters.AddWithValue("$user", actorUserId.ToString("D"));
            permission.Parameters.AddWithValue("$permission", SalrynPermissions.SalesCreate);
            if (Convert.ToInt64(permission.ExecuteScalar() ?? 0L) != 1)
            {
                return Result.Fail(
                    Error.Forbidden(
                        "CafeOrder.SalesCreateRequired",
                        "Order completion requires existing Sales.Create authority."));
            }
        }

        if (deviceId == Guid.Empty)
        {
            return Result.Fail(
                Error.Validation(
                    "CafeOrder.DeviceRequired",
                    "Completion requires a stable DeviceId."));
        }

        return Result.Success();
    }

    private static string? ValidateMutation(
        CafeOrderCompletionMutation mutation)
    {
        if (mutation.CompletionOperationId == Guid.Empty ||
            mutation.SaleCommittedOperationId == Guid.Empty ||
            mutation.DeviceId == Guid.Empty ||
            mutation.OrderId == Guid.Empty ||
            mutation.StoreId == Guid.Empty ||
            mutation.ActorUserId == Guid.Empty ||
            mutation.ExpectedVersion <= 0)
        {
            return "Completion requires stable operation/device/order/store/actor identities and a positive expected version.";
        }

        if (mutation.CompletionOperationId ==
            mutation.SaleCommittedOperationId)
        {
            return "Order completion and SaleCommitted operations require distinct identities.";
        }

        if (mutation.Payments is null)
        {
            return "Sale payments collection is required.";
        }

        if (mutation.Payments.Count == 0 &&
            mutation.BookingFinalization is null)
        {
            return "At least one existing Sale payment is required outside C9 Booking deposit finalization.";
        }

        if (mutation.BookingFinalization is CafeBookingFinalizationBinding booking &&
            (booking.BookingId == Guid.Empty ||
             booking.BookingRevisionId == Guid.Empty ||
             booking.BookingOrderLinkId == Guid.Empty ||
             booking.ExpectedBookingVersion <= 0))
        {
            return "C9 Booking finalization binding requires stable identities and positive ExpectedBookingVersion.";
        }

        foreach (CafeOrderPaymentMutation payment in mutation.Payments)
        {
            if (payment.AmountCents <= 0 ||
                !Enum.IsDefined(typeof(PaymentMethod), payment.Method))
            {
                return "Payment method and positive amount are required.";
            }

            if (payment.Method != PaymentMethod.Cash &&
                payment.Method != PaymentMethod.AccountsReceivable)
            {
                return "C5 order completion supports Cash and AccountsReceivable tender only.";
            }
        }

        bool usesC5 =
            UsesC5FinancialAuthority(mutation);

        if (!usesC5 &&
            mutation.Payments.Any(
                x => x.Method != PaymentMethod.Cash))
        {
            return "Legacy C4 completion remains Cash-only; AR requires C5 financial authority.";
        }

        if (mutation.CustomerId == Guid.Empty)
        {
            return "CustomerId cannot be an empty GUID.";
        }

        if (mutation.FinancialDecisionOperationId != Guid.Empty &&
            (mutation.FinancialDecisionOperationId ==
                mutation.CompletionOperationId ||
             mutation.FinancialDecisionOperationId ==
                mutation.SaleCommittedOperationId))
        {
            return "Financial decision operation ID must be distinct from completion and SaleCommitted operation IDs.";
        }

        return null;
    }

    private static bool PaymentsMatch(
        SaleCommittedV1 payload,
        IReadOnlyList<CafeOrderPaymentMutation> request)
    {
        if (payload.Payments.Count != request.Count)
        {
            return false;
        }

        var a = payload.Payments
            .Select(x => (Method: x.Method, Amount: x.AmountCents))
            .OrderBy(x => x.Method)
            .ThenBy(x => x.Amount)
            .ToArray();
        var b = request
            .Select(x => (Method: x.Method.ToString(), Amount: x.AmountCents))
            .OrderBy(x => x.Method, StringComparer.Ordinal)
            .ThenBy(x => x.Amount)
            .ToArray();

        return a.Length == b.Length &&
            a.Zip(b).All(x =>
                string.Equals(
                    x.First.Method,
                    x.Second.Method,
                    StringComparison.OrdinalIgnoreCase) &&
                x.First.Amount == x.Second.Amount);
    }

    private static Guid DeterministicSaleItemId(
        Guid completionOperationId,
        Guid orderLineId) =>
        SqliteCafeOrderStore.DeterministicGuid(
            "salryn-c4-order-sale-item-v1",
            completionOperationId.ToString("D") + "|" +
            orderLineId.ToString("D"));

    private sealed record ReceiptAllocation(
        string SequenceId,
        string ReceiptNumber);

    private sealed record ProductAuthority(
        bool TrackStock);

    private sealed class OrderCompletionValidationException : Exception
    {
        public OrderCompletionValidationException(
            string code,
            string message)
            : base(message)
        {
            Code = code;
        }

        public string Code { get; }
    }
}
