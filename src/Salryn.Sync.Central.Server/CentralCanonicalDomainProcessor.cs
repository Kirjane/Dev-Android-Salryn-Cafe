using Microsoft.Data.Sqlite;
using Salryn.Infrastructure.Data.Repositories.Ingredients;
using Salryn.Infrastructure.Data.Repositories.Inventory;
using Salryn.Infrastructure.Data.Repositories.Orders;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Data.Sync;
using Salryn.Infrastructure.Data.Transactions;
using Salryn.Sync.Contracts;

namespace Salryn.Sync.Central.Server;

internal sealed record CentralDomainResult(string Status, string Code, string? ChangeType = null);

/// <summary>
/// Reconciled canonical domain processor for Central.Server. Uses shared
/// canonical writers inside the Server inbox/feed transaction, never a parallel ledger.
/// Unsupported operation families remain explicitly NeedsReview.
/// </summary>
internal static class CentralCanonicalDomainProcessor
{
    internal static CentralDomainResult Apply(SqliteConnection connection, SqliteTransaction transaction,
        CentralServerOptions options, AuthenticatedDeviceContext device, SyncOperationEnvelopeV1 envelope)
    {
        CentralDomainResult Review(string code) => new(SaleCommittedV1Contract.NeedsReview, code);
        CentralDomainResult Reject(string code) => new(SaleCommittedV1Contract.Rejected, code);
        if (!options.EnableCanonicalDomainProcessing) return Review("DOMAIN_PROCESSOR_UNAVAILABLE");
        if (device.TenantId != options.CanonicalTenantId || device.StoreId != options.CanonicalStoreId)
            return Reject("ERR_CANONICAL_AUTHORITY_SCOPE");
        using var authority = connection.CreateCommand();
        authority.Transaction = transaction;
        authority.CommandText = "SELECT COUNT(*) FROM sync_canonical_domain_scope WHERE singleton_id=1 AND tenant_id=$tenant AND store_id=$store;";
        authority.Parameters.AddWithValue("$tenant", device.TenantId.ToString("D"));
        authority.Parameters.AddWithValue("$store", device.StoreId.ToString("D"));
        if (Convert.ToInt64(authority.ExecuteScalar()) != 1) return Reject("ERR_CANONICAL_AUTHORITY_SCOPE");

        if (envelope.OperationType == SaleCommittedV2Contract.OperationType &&
            envelope.SchemaVersion == SaleCommittedV2Contract.SchemaVersion)
            return ApplySaleV2(connection, transaction, envelope);

        if (envelope.OperationType == SaleCommittedV1Contract.OperationType &&
            envelope.SchemaVersion == SaleCommittedV1Contract.SchemaVersion)
            return ApplySale(connection, transaction, envelope);

        if (envelope.SchemaVersion == 1 && CafeOrderOperationContract.IsSupported(envelope.OperationType))
            return ApplyCafeOrder(connection, transaction, envelope);

        if (envelope.SchemaVersion == 1 && envelope.OperationType == CafeCashMovementSyncContract.OperationType)
            return ApplyCashMovement(connection, transaction, envelope);

        if (envelope.SchemaVersion == 1 && CafeRefundSyncContract.IsSupported(envelope.OperationType))
            return ApplyRefund(connection, transaction, envelope);

        if (envelope.SchemaVersion == 1 && CafeInventoryOperationContract.IsSupported(envelope.OperationType))
            return ApplyInventory(connection, transaction, envelope);

        if (envelope.SchemaVersion == 1 && envelope.OperationType == IngredientConsumptionCommittedV1Contract.OperationType)
            return ApplyIngredientConsumption(connection, transaction, envelope);

        return Review("DOMAIN_PROCESSOR_UNAVAILABLE");
    }

    private static CentralDomainResult ApplyCafeOrder(SqliteConnection connection, SqliteTransaction transaction,
        SyncOperationEnvelopeV1 envelope)
    {
        CentralDomainResult Review(string code) => new(SaleCommittedV1Contract.NeedsReview, code);
        CentralDomainResult Reject(string code) => new(SaleCommittedV1Contract.Rejected, code);
        var parsed = CafeOrderOperationCanonicalJson.Parse(envelope.OperationType, envelope.PayloadJson);
        if (!parsed.IsValid || parsed.Payload is null) return Reject(parsed.ErrorCode ?? "CafeOrder.PayloadInvalid");
        var payload = parsed.Payload;
        if (payload.StoreId != envelope.StoreId || payload.ActorUserId != envelope.ActorUserId ||
            payload.OrderId != envelope.AggregateId || payload.ExpectedVersion != envelope.BaseVersion)
            return Reject("CafeOrder.EnvelopeConflict");
        string permission = envelope.OperationType switch
        {
            CafeOrderOperationContract.OrderRevisionCommittedV1 => payload.ExpectedVersion == 0 ? "Orders.Create" : "Orders.Manage",
            CafeOrderOperationContract.OrderCancelledV1 => "Orders.Cancel",
            CafeOrderOperationContract.OrderCompletedV1 => "Orders.Complete",
            CafeOrderOperationContract.OrderPreparationStatusChangedV1 => "Preparation.Manage",
            _ => "Orders.Manage"
        };
        if (!HasOperatorPermission(connection, transaction, envelope.ActorUserId, permission))
            return Reject("ERR_CANONICAL_OPERATOR_AUTHORITY");
        using var savepoint = connection.CreateCommand();
        savepoint.Transaction = transaction;
        savepoint.CommandText = "SAVEPOINT central_canonical_apply;";
        savepoint.ExecuteNonQuery();
        var result = SqliteCafeOrderStore.ApplyCanonical(connection, transaction, envelope.OperationId, payload);
        if (result.IsFailure)
        {
            savepoint.CommandText = "ROLLBACK TO central_canonical_apply; RELEASE central_canonical_apply;";
            savepoint.ExecuteNonQuery();
            return Review(result.ErrorDetail.Code);
        }
        savepoint.CommandText = "RELEASE central_canonical_apply;";
        savepoint.ExecuteNonQuery();
        return new(SaleCommittedV1Contract.Applied, "CafeOrder.Applied", ChangeTypeForOrder(envelope.OperationType));
    }

    private static CentralDomainResult ApplySaleV2(SqliteConnection connection, SqliteTransaction transaction,
        SyncOperationEnvelopeV1 envelope)
    {
        CentralDomainResult Review(string code) => new(SaleCommittedV1Contract.NeedsReview, code);
        CentralDomainResult Reject(string code) => new(SaleCommittedV1Contract.Rejected, code);
        var parsed = SaleCommittedV2CanonicalJson.Parse(envelope.PayloadJson);
        if (!parsed.IsValid || parsed.Payload is null) return Reject(parsed.ErrorCode ?? "SaleCommittedV2.PayloadInvalid");
        var policy = SaleCommittedV2EnvelopePolicy.ValidateForCentral(envelope, parsed.Payload, envelope.StoreId);
        if (!policy.IsValid) return Reject(policy.ErrorCode ?? "SaleCommittedV2.EnvelopeConflict");
        if (!HasOperatorPermission(connection, transaction, envelope.ActorUserId, "Sales.Create"))
            return Reject("ERR_CANONICAL_OPERATOR_AUTHORITY");
        var prerequisites = CentralSaleCommittedPrerequisites.Validate(connection, transaction, parsed.Payload.SaleEvidence);
        if (!prerequisites.CanApply) return Review(prerequisites.ConflictCode ?? "SaleCommittedV2.PrerequisiteMissing");
        var mapping = SaleCommittedV2Mapper.ToCore(parsed.Payload);
        if (mapping.IsFailure) return Reject(mapping.ErrorDetail.Code);
        var financial = SaleCommittedV2Mapper.ToFinancialDecision(parsed.Payload, envelope.StoreId);
        if (financial.IsFailure) return Reject(financial.ErrorDetail.Code);
        using var savepoint = connection.CreateCommand();
        savepoint.Transaction = transaction;
        savepoint.CommandText = "SAVEPOINT central_sale_v2_apply;";
        savepoint.ExecuteNonQuery();
        var txContext = new SqliteSharedTransactionContext(connection, transaction);
        var written = new SharedSqliteCompletedSaleWriteParticipant().ExecuteC5Async(
            txContext, mapping.Value).GetAwaiter().GetResult();
        if (written.IsFailure)
        {
            savepoint.CommandText = "ROLLBACK TO central_sale_v2_apply; RELEASE central_sale_v2_apply;";
            savepoint.ExecuteNonQuery();
            return Review(written.ErrorDetail.Code);
        }
        var financialWritten = new SharedSqliteCafeFinancialParticipant().ExecuteAsync(
            txContext, financial.Value).GetAwaiter().GetResult();
        if (financialWritten.IsFailure)
        {
            savepoint.CommandText = "ROLLBACK TO central_sale_v2_apply; RELEASE central_sale_v2_apply;";
            savepoint.ExecuteNonQuery();
            return Review(financialWritten.ErrorDetail.Code);
        }
        savepoint.CommandText = "RELEASE central_sale_v2_apply;";
        savepoint.ExecuteNonQuery();
        return new(SaleCommittedV1Contract.Applied, "SaleCommittedV2.Applied", AppliedBusinessChangeContract.SaleCommittedV2AppliedV1);
    }

    private static CentralDomainResult ApplyCashMovement(SqliteConnection connection, SqliteTransaction transaction,
        SyncOperationEnvelopeV1 envelope)
    {
        CentralDomainResult Review(string code) => new(SaleCommittedV1Contract.NeedsReview, code);
        CentralDomainResult Reject(string code) => new(SaleCommittedV1Contract.Rejected, code);
        var parsed = CafeCashMovementPostedV1CanonicalJson.Parse(envelope.OperationType, envelope.PayloadJson);
        if (!parsed.IsValid || parsed.Payload is null) return Reject(parsed.ErrorCode ?? "CafeCashSync.PayloadInvalid");
        using var savepoint = connection.CreateCommand();
        savepoint.Transaction = transaction;
        savepoint.CommandText = "SAVEPOINT central_cash_apply;";
        savepoint.ExecuteNonQuery();
        var result = new SharedSqliteCafeCashParticipant().ApplyCanonical(
            new SqliteSharedTransactionContext(connection, transaction), envelope, parsed.Payload);
        if (result.IsFailure)
        {
            savepoint.CommandText = "ROLLBACK TO central_cash_apply; RELEASE central_cash_apply;";
            savepoint.ExecuteNonQuery();
            return Review(result.ErrorDetail.Code);
        }
        savepoint.CommandText = "RELEASE central_cash_apply;";
        savepoint.ExecuteNonQuery();
        return new(SaleCommittedV1Contract.Applied, "CafeCashMovement.Applied", AppliedBusinessChangeContract.CashMovementPostedV1AppliedV1);
    }

    private static CentralDomainResult ApplyRefund(SqliteConnection connection, SqliteTransaction transaction,
        SyncOperationEnvelopeV1 envelope)
    {
        CentralDomainResult Review(string code) => new(SaleCommittedV1Contract.NeedsReview, code);
        CentralDomainResult Reject(string code) => new(SaleCommittedV1Contract.Rejected, code);
        var parsed = CafeRefundOperationV1CanonicalJson.Parse(envelope.OperationType, envelope.PayloadJson);
        if (!parsed.IsValid || parsed.Payload is null) return Reject(parsed.ErrorCode ?? "CafeRefundSync.PayloadInvalid");
        using var savepoint = connection.CreateCommand();
        savepoint.Transaction = transaction;
        savepoint.CommandText = "SAVEPOINT central_refund_apply;";
        savepoint.ExecuteNonQuery();
        var result = new SharedSqliteCafeRefundParticipant().ApplyCanonical(
            new SqliteSharedTransactionContext(connection, transaction), envelope, parsed.Payload);
        if (result.IsFailure)
        {
            savepoint.CommandText = "ROLLBACK TO central_refund_apply; RELEASE central_refund_apply;";
            savepoint.ExecuteNonQuery();
            return Review(result.ErrorDetail.Code);
        }
        savepoint.CommandText = "RELEASE central_refund_apply;";
        savepoint.ExecuteNonQuery();
        string changeType = envelope.OperationType switch
        {
            CafeRefundSyncContract.Requested => AppliedBusinessChangeContract.SaleRefundRequestedV1AppliedV1,
            CafeRefundSyncContract.Resolved => AppliedBusinessChangeContract.SaleRefundResolvedV1AppliedV1,
            CafeRefundSyncContract.Committed => AppliedBusinessChangeContract.SaleRefundCommittedV1AppliedV1,
            _ => throw new InvalidOperationException("Unsupported canonical refund change.")
        };
        return new(SaleCommittedV1Contract.Applied, "CafeRefund.Applied", changeType);
    }

    private static CentralDomainResult ApplyInventory(SqliteConnection connection, SqliteTransaction transaction,
        SyncOperationEnvelopeV1 envelope)
    {
        CentralDomainResult Review(string code) => new(SaleCommittedV1Contract.NeedsReview, code);
        CentralDomainResult Reject(string code) => new(SaleCommittedV1Contract.Rejected, code);
        if (!HasOperatorPermission(connection, transaction, envelope.ActorUserId, "Inventory.Manage"))
            return Reject("ERR_CANONICAL_OPERATOR_AUTHORITY");
        using var savepoint = connection.CreateCommand();
        savepoint.Transaction = transaction;
        savepoint.CommandText = "SAVEPOINT central_inventory_apply;";
        savepoint.ExecuteNonQuery();
        Salryn.Core.Common.Result result;
        string changeType;
        switch (envelope.OperationType)
        {
            case CafeInventoryOperationContract.PurchaseReceiptCommittedV1:
                var purchase = CafeInventoryOperationCanonicalJson.ParsePurchase(envelope.PayloadJson);
                if (!purchase.IsValid || purchase.Payload is null)
                {
                    savepoint.CommandText = "RELEASE central_inventory_apply;";
                    savepoint.ExecuteNonQuery();
                    return Reject(purchase.ErrorCode ?? "CafeInventory.PayloadInvalid");
                }
                result = SqliteCafeInventoryStore.ApplyCanonicalPurchase(connection, transaction, envelope.StoreId, envelope.OperationId, purchase.Payload);
                changeType = AppliedBusinessChangeContract.PurchaseReceiptCommittedV1AppliedV1;
                break;
            case CafeInventoryOperationContract.InventoryWasteCommittedV1:
                var waste = CafeInventoryOperationCanonicalJson.ParseWaste(envelope.PayloadJson);
                if (!waste.IsValid || waste.Payload is null)
                {
                    savepoint.CommandText = "RELEASE central_inventory_apply;";
                    savepoint.ExecuteNonQuery();
                    return Reject(waste.ErrorCode ?? "CafeInventory.PayloadInvalid");
                }
                result = SqliteCafeInventoryStore.ApplyCanonicalWaste(connection, transaction, envelope.StoreId, envelope.OperationId, waste.Payload);
                changeType = AppliedBusinessChangeContract.InventoryWasteCommittedV1AppliedV1;
                break;
            case CafeInventoryOperationContract.InventoryAdjustmentCommittedV1:
                var adjustment = CafeInventoryOperationCanonicalJson.ParseAdjustment(envelope.PayloadJson);
                if (!adjustment.IsValid || adjustment.Payload is null)
                {
                    savepoint.CommandText = "RELEASE central_inventory_apply;";
                    savepoint.ExecuteNonQuery();
                    return Reject(adjustment.ErrorCode ?? "CafeInventory.PayloadInvalid");
                }
                result = SqliteCafeInventoryStore.ApplyCanonicalAdjustment(connection, transaction, envelope.StoreId, envelope.OperationId, adjustment.Payload);
                changeType = AppliedBusinessChangeContract.InventoryAdjustmentCommittedV1AppliedV1;
                break;
            case CafeInventoryOperationContract.InventoryCountFinalizedV1:
                var count = CafeInventoryOperationCanonicalJson.ParseCount(envelope.PayloadJson);
                if (!count.IsValid || count.Payload is null)
                {
                    savepoint.CommandText = "RELEASE central_inventory_apply;";
                    savepoint.ExecuteNonQuery();
                    return Reject(count.ErrorCode ?? "CafeInventory.PayloadInvalid");
                }
                result = SqliteCafeInventoryStore.ApplyCanonicalCount(connection, transaction, envelope.StoreId, envelope.OperationId, count.Payload);
                changeType = AppliedBusinessChangeContract.InventoryCountFinalizedV1AppliedV1;
                break;
            default:
                savepoint.CommandText = "RELEASE central_inventory_apply;";
                savepoint.ExecuteNonQuery();
                return Review("DOMAIN_PROCESSOR_UNAVAILABLE");
        }
        if (result.IsFailure)
        {
            savepoint.CommandText = "ROLLBACK TO central_inventory_apply; RELEASE central_inventory_apply;";
            savepoint.ExecuteNonQuery();
            return Review(result.ErrorDetail.Code);
        }
        savepoint.CommandText = "RELEASE central_inventory_apply;";
        savepoint.ExecuteNonQuery();
        return new(SaleCommittedV1Contract.Applied, "CafeInventory.Applied", changeType);
    }

    private static CentralDomainResult ApplyIngredientConsumption(SqliteConnection connection, SqliteTransaction transaction,
        SyncOperationEnvelopeV1 envelope)
    {
        CentralDomainResult Review(string code) => new(SaleCommittedV1Contract.NeedsReview, code);
        CentralDomainResult Reject(string code) => new(SaleCommittedV1Contract.Rejected, code);
        var parsed = IngredientConsumptionCommittedV1CanonicalJson.Parse(envelope.PayloadJson);
        if (!parsed.IsValid || parsed.Payload is null) return Reject(parsed.ErrorCode ?? "IngredientConsumption.PayloadInvalid");
        if (!HasOperatorPermission(connection, transaction, envelope.ActorUserId, "Sales.Create"))
            return Reject("ERR_CANONICAL_OPERATOR_AUTHORITY");
        using var savepoint = connection.CreateCommand();
        savepoint.Transaction = transaction;
        savepoint.CommandText = "SAVEPOINT central_ingredient_apply;";
        savepoint.ExecuteNonQuery();
        var result = SqliteIngredientConsumptionParticipant.ApplyCanonical(connection, transaction, parsed.Payload);
        if (result.IsFailure)
        {
            savepoint.CommandText = "ROLLBACK TO central_ingredient_apply; RELEASE central_ingredient_apply;";
            savepoint.ExecuteNonQuery();
            return Review(result.ErrorDetail.Code);
        }
        savepoint.CommandText = "RELEASE central_ingredient_apply;";
        savepoint.ExecuteNonQuery();
        return new(SaleCommittedV1Contract.Applied, "IngredientConsumption.Applied", AppliedBusinessChangeContract.IngredientConsumptionCommittedV1AppliedV1);
    }

    private static bool HasOperatorPermission(SqliteConnection connection, SqliteTransaction transaction,
        Guid actorUserId, string permission)
    {
        using var actor = connection.CreateCommand();
        actor.Transaction = transaction;
        actor.CommandText = """
            SELECT COUNT(*) FROM users u JOIN role_permissions p ON p.role_id=u.role_id
            WHERE u.id=$actor AND u.status=1 AND p.permission=$permission;
            """;
        actor.Parameters.AddWithValue("$actor", actorUserId.ToString("D"));
        actor.Parameters.AddWithValue("$permission", permission);
        return Convert.ToInt64(actor.ExecuteScalar()) > 0;
    }

    private static CentralDomainResult ApplySale(SqliteConnection connection, SqliteTransaction transaction,
        SyncOperationEnvelopeV1 envelope)
    {
        CentralDomainResult Review(string code) => new(SaleCommittedV1Contract.NeedsReview, code);
        CentralDomainResult Reject(string code) => new(SaleCommittedV1Contract.Rejected, code);
        var parsed = SaleCommittedV1CanonicalJson.Parse(envelope.PayloadJson);
        if (!parsed.IsValid || parsed.Payload is null) return Reject(parsed.ErrorCode ?? "SaleCommittedV1.PayloadInvalid");
        var policy = SaleCommittedV1EnvelopePolicy.ValidateForCentral(envelope, parsed.Payload, envelope.StoreId);
        if (!policy.IsValid) return Reject(policy.ErrorCode ?? "SaleCommittedV1.EnvelopeConflict");
        using var actor = connection.CreateCommand();
        actor.Transaction = transaction;
        actor.CommandText = "SELECT COUNT(*) FROM users u JOIN role_permissions p ON p.role_id=u.role_id WHERE u.id=$actor AND u.status=1 AND p.permission='Sales.Create';";
        actor.Parameters.AddWithValue("$actor",envelope.ActorUserId.ToString("D"));
        if (Convert.ToInt64(actor.ExecuteScalar()) == 0) return Reject("ERR_CANONICAL_OPERATOR_AUTHORITY");
        var prerequisites = CentralSaleCommittedPrerequisites.Validate(connection, transaction, parsed.Payload);
        if (!prerequisites.CanApply) return Review(prerequisites.ConflictCode ?? "SaleCommittedV1.PrerequisiteMissing");
        var mapping = SaleCommittedV1Mapper.ToCore(parsed.Payload);
        if (mapping.IsFailure) return Reject(mapping.ErrorDetail.Code);
        using var savepoint = connection.CreateCommand();
        savepoint.Transaction = transaction;
        savepoint.CommandText = "SAVEPOINT central_sale_apply;";
        savepoint.ExecuteNonQuery();
        var written = new SharedSqliteCompletedSaleWriteParticipant().ExecuteAsync(
            new SqliteSharedTransactionContext(connection, transaction), mapping.Value).GetAwaiter().GetResult();
        if (written.IsFailure)
        {
            savepoint.CommandText = "ROLLBACK TO central_sale_apply; RELEASE central_sale_apply;";
            savepoint.ExecuteNonQuery();
            return Review(written.ErrorDetail.Code);
        }
        savepoint.CommandText = "RELEASE central_sale_apply;";
        savepoint.ExecuteNonQuery();
        return new(SaleCommittedV1Contract.Applied, "SaleCommittedV1.Applied", AppliedBusinessChangeContract.SaleCommittedV1AppliedV1);
    }

    private static string ChangeTypeForOrder(string operationType) => operationType switch
    {
        CafeOrderOperationContract.OrderRevisionCommittedV1 => AppliedBusinessChangeContract.OrderRevisionCommittedV1AppliedV1,
        CafeOrderOperationContract.OrderParkedV1 => AppliedBusinessChangeContract.OrderParkedV1AppliedV1,
        CafeOrderOperationContract.OrderResumedV1 => AppliedBusinessChangeContract.OrderResumedV1AppliedV1,
        CafeOrderOperationContract.OrderPreparationSubmittedV1 => AppliedBusinessChangeContract.OrderPreparationSubmittedV1AppliedV1,
        CafeOrderOperationContract.OrderPreparationStatusChangedV1 => AppliedBusinessChangeContract.OrderPreparationStatusChangedV1AppliedV1,
        CafeOrderOperationContract.OrderCancelledV1 => AppliedBusinessChangeContract.OrderCancelledV1AppliedV1,
        CafeOrderOperationContract.OrderCompletedV1 => AppliedBusinessChangeContract.OrderCompletedV1AppliedV1,
        _ => throw new InvalidOperationException("Unsupported canonical order change.")
    };
}
