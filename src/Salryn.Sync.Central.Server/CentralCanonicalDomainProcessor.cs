using Microsoft.Data.Sqlite;
using Salryn.Infrastructure.Data.Repositories.Orders;
using Salryn.Infrastructure.Data.Services;
using Salryn.Infrastructure.Data.Sync;
using Salryn.Infrastructure.Data.Transactions;
using Salryn.Sync.Contracts;

namespace Salryn.Sync.Central.Server;

internal sealed record CentralDomainResult(string Status, string Code, string? ChangeType = null);

/// <summary>
/// Reconciled from Cafe Yza CentralCafeOrderV1Processor at 4241ea1e. Uses the
/// shared canonical writer within the Server inbox/feed transaction, never another ledger.
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
        if (envelope.OperationType == SaleCommittedV1Contract.OperationType)
            return ApplySale(connection, transaction, envelope);
        if (!CafeOrderOperationContract.IsSupported(envelope.OperationType)) return Review("DOMAIN_PROCESSOR_UNAVAILABLE");
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
        using var actor = connection.CreateCommand();
        actor.Transaction = transaction;
        actor.CommandText = """
            SELECT COUNT(*) FROM users u JOIN role_permissions p ON p.role_id=u.role_id
            WHERE u.id=$actor AND u.status=1 AND p.permission=$permission;
            """;
        actor.Parameters.AddWithValue("$actor", envelope.ActorUserId.ToString("D"));
        actor.Parameters.AddWithValue("$permission", permission);
        if (Convert.ToInt64(actor.ExecuteScalar()) == 0) return Reject("ERR_CANONICAL_OPERATOR_AUTHORITY");
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
