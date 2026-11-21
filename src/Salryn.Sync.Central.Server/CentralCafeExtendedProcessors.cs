using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Infrastructure.Data.Sync;
using Salryn.Infrastructure.Data.Transactions;
using Salryn.Sync.Contracts;

namespace Salryn.Sync.Central.Server;

internal static partial class CentralCanonicalDomainProcessor
{
    private static CentralDomainResult? ApplyExtended(SqliteConnection connection, SqliteTransaction transaction,
        SyncOperationEnvelopeV1 envelope)
    {
        CentralDomainResult Reject(string code) => new(SaleCommittedV1Contract.Rejected, code);
        bool Allowed(params string[] permissions) => permissions.All(permission =>
            HasOperatorPermission(connection, transaction, envelope.ActorUserId, permission));
        if (envelope.SchemaVersion == 1 && CafeDeliverySyncContract.IsSupported(envelope.OperationType))
        {
            var parsed = CafeDeliveryOperationCanonicalJson.Parse(envelope.OperationType, envelope.PayloadJson);
            if (!parsed.IsValid || parsed.Payload is null) return Reject(parsed.ErrorCode ?? "CafeDeliverySync.PayloadInvalid");
            string permission = envelope.OperationType is CafeDeliverySyncContract.DeliveryDispatchedV1 or CafeDeliverySyncContract.DeliveryResolvedV1
                ? "Delivery.Advance" : "Delivery.Manage";
            if (!Allowed(permission)) return Reject("ERR_CANONICAL_OPERATOR_AUTHORITY");
            string changeType = envelope.OperationType switch
            {
                CafeDeliverySyncContract.DeliveryConfiguredV1 => AppliedBusinessChangeContract.DeliveryConfiguredV1AppliedV1,
                CafeDeliverySyncContract.DeliveryDispatchedV1 => AppliedBusinessChangeContract.DeliveryDispatchedV1AppliedV1,
                CafeDeliverySyncContract.DeliveryResolvedV1 => AppliedBusinessChangeContract.DeliveryResolvedV1AppliedV1,
                _ => AppliedBusinessChangeContract.DeliveryCancelledV1AppliedV1
            };
            return ApplyExtendedAtomic(connection, transaction, () =>
                SqliteCafeDeliverySyncParticipant.ApplyCanonical(connection, transaction, envelope, parsed.Payload), changeType);
        }
        if (envelope.SchemaVersion == 1 && SqliteCafeBookingSyncParticipant.IsSupportedSourceOperation(envelope.OperationType))
        {
            string[] permissions = envelope.OperationType switch
            {
                CafeBookingOperationContract.BookingCancelledV1 => ["Booking.Cancel"],
                CafeBookingOperationContract.BookingDepositReceivedV1 => ["Booking.DepositReceive", "Cash.In"],
                CafeBookingOperationContract.BookingDepositRefundedV1 => ["Booking.DepositRefund", "Cash.Out"],
                BookingFulfillmentOrderBoundV1Contract.OperationType => ["Booking.Fulfill", "Orders.Create"],
                _ => ["Booking.Manage"]
            };
            if (!Allowed(permissions)) return Reject("ERR_CANONICAL_OPERATOR_AUTHORITY");
            return ApplyExtendedAtomic(connection, transaction, () =>
                SqliteCafeBookingSyncParticipant.ApplyCanonical(connection, transaction, envelope),
                SqliteCafeBookingSyncParticipant.AppliedChangeTypeForSource(envelope.OperationType));
        }
        if (envelope.SchemaVersion == 1 && CafeAttendanceIncidentSyncContract.IsSupported(envelope.OperationType))
        {
            string permission = envelope.OperationType switch
            {
                CafeAttendanceIncidentSyncContract.AttendanceEventRecordedV1 => "Attendance.Record",
                CafeAttendanceIncidentSyncContract.AttendanceEventCorrectedV1 => "Attendance.Manage",
                CafeAttendanceIncidentSyncContract.IncidentRevisionCommittedV1 => "Incident.Record",
                _ => "Incident.Manage"
            };
            // Revisions after initial creation require management authority, as the Host endpoint does.
            if (envelope.OperationType == CafeAttendanceIncidentSyncContract.IncidentRevisionCommittedV1 && envelope.BaseVersion > 0)
                permission = "Incident.Manage";
            if (!Allowed(permission)) return Reject("ERR_CANONICAL_OPERATOR_AUTHORITY");
            return ApplyExtendedAtomic(connection, transaction, () =>
                SqliteCafeAttendanceIncidentSyncParticipant.ApplyCanonical(connection, transaction, envelope),
                SqliteCafeAttendanceIncidentSyncParticipant.AppliedChangeTypeForSource(envelope.OperationType));
        }
        if (envelope.OperationType == SaleCommittedV3Contract.OperationType && envelope.SchemaVersion == SaleCommittedV3Contract.SchemaVersion)
        {
            var parsed = SaleCommittedV3CanonicalJson.Parse(envelope.PayloadJson);
            if (!parsed.IsValid || parsed.Payload is null) return Reject(parsed.ErrorCode ?? "SaleCommittedV3.PayloadInvalid");
            var payload = parsed.Payload;
            SaleCommittedV1Sale sale = payload.SaleEvidence.Sale;
            if (envelope.OperationId == Guid.Empty ||
                envelope.AggregateId != sale.Id ||
                envelope.StoreId == Guid.Empty ||
                envelope.ActorUserId != sale.CashierUserId ||
                envelope.BaseVersion.HasValue)
                return Reject("SaleCommittedV3.EnvelopeAuthorityMismatch");
            if (!Allowed("Sales.Create", "Orders.Complete", "Booking.Fulfill") ||
                (RequiresCreditSalePermission(payload.SaleEvidence) && !Allowed("Receivables.CreateCreditSale")) ||
                (payload.SaleEvidence.Sale.DiscountCents > 0 && !Allowed("Sales.DiscountApply")) ||
                (payload.Fees.Count > 0 && !Allowed("Sales.FeeApply")))
                return Reject("ERR_CANONICAL_OPERATOR_AUTHORITY");
            var origin=CanonicalOriginSaleVerifier.VerifyIfRetained(connection,transaction,envelope);
            if(origin.IsFailure) return new(SaleCommittedV1Contract.NeedsReview,origin.ErrorDetail.Code);
            if(origin.Value) return new(SaleCommittedV1Contract.Applied,AppliedBusinessChangeContract.SaleCommittedV3AppliedV1,AppliedBusinessChangeContract.SaleCommittedV3AppliedV1);
            var prerequisites = CentralSaleCommittedPrerequisites.Validate(connection, transaction, payload.SaleEvidence);
            if (!prerequisites.CanApply) return new(SaleCommittedV1Contract.NeedsReview,
                prerequisites.ConflictCode ?? "SaleCommittedV3.PrerequisiteMissing");
            return ApplyExtendedAtomic(connection, transaction, () => SharedSqliteCafeBookingV3Participant.ApplyCanonicalAsync(
                new SqliteSharedTransactionContext(connection, transaction), envelope, payload).GetAwaiter().GetResult(),
                AppliedBusinessChangeContract.SaleCommittedV3AppliedV1);
        }
        return null;
    }

    private static CentralDomainResult ApplyExtendedAtomic(SqliteConnection connection, SqliteTransaction transaction,
        Func<Result> apply, string changeType)
    {
        using var savepoint = connection.CreateCommand();
        savepoint.Transaction = transaction;
        savepoint.CommandText = "SAVEPOINT central_extended_apply;";
        savepoint.ExecuteNonQuery();
        Result result = apply();
        if (result.IsFailure)
        {
            savepoint.CommandText = "ROLLBACK TO central_extended_apply; RELEASE central_extended_apply;";
            savepoint.ExecuteNonQuery();
            return new(SaleCommittedV1Contract.NeedsReview, result.ErrorDetail.Code);
        }
        savepoint.CommandText = "RELEASE central_extended_apply;";
        savepoint.ExecuteNonQuery();
        return new(SaleCommittedV1Contract.Applied, changeType, changeType);
    }
}
