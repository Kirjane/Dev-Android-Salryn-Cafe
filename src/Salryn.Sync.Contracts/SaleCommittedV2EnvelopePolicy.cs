namespace Salryn.Sync.Contracts;

public static class SaleCommittedV2EnvelopePolicy
{
    public static SaleCommittedV2ValidationResult ValidateIntrinsic(
        SyncOperationEnvelopeV1 envelope,
        SaleCommittedV2 payload)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(payload);

        SyncEnvelopeValidationResult envelopeValidation =
            SyncProtocolV1.Validate(envelope);
        if (!envelopeValidation.IsValid)
        {
            return SaleCommittedV2ValidationResult.Fail(
                envelopeValidation.ErrorCode ?? "SaleCommittedV2.EnvelopeInvalid",
                envelopeValidation.ErrorMessage ?? "Sync envelope is invalid.");
        }

        if (!string.Equals(
                envelope.OperationType,
                SaleCommittedV2Contract.OperationType,
                StringComparison.Ordinal) ||
            envelope.SchemaVersion != SaleCommittedV2Contract.SchemaVersion)
        {
            return SaleCommittedV2ValidationResult.Fail(
                "SaleCommittedV2.DispatchContractMismatch",
                "Operation type/schema does not identify SaleCommittedV2 schema version 2.");
        }

        if (envelope.AggregateId != payload.SaleEvidence.Sale.Id)
        {
            return SaleCommittedV2ValidationResult.Fail(
                "SaleCommittedV2.AggregateMismatch",
                "Envelope aggregate ID must equal the committed sale ID.");
        }

        if (envelope.ActorUserId != payload.SaleEvidence.Sale.CashierUserId)
        {
            return SaleCommittedV2ValidationResult.Fail(
                "SaleCommittedV2.ActorMismatch",
                "Envelope actor must equal the committed sale cashier.");
        }

        return SaleCommittedV2Validator.Validate(payload);
    }

    public static SaleCommittedV2ValidationResult ValidateForCentral(
        SyncOperationEnvelopeV1 envelope,
        SaleCommittedV2 payload,
        Guid expectedStoreId)
    {
        SaleCommittedV2ValidationResult intrinsic =
            ValidateIntrinsic(envelope, payload);
        if (!intrinsic.IsValid)
            return intrinsic;

        if (expectedStoreId == Guid.Empty ||
            envelope.StoreId != expectedStoreId)
        {
            return SaleCommittedV2ValidationResult.Fail(
                "SaleCommittedV2.StoreMismatch",
                "Envelope store does not match the central deployment store.");
        }

        return SaleCommittedV2ValidationResult.Success();
    }
}
