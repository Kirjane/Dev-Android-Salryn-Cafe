namespace Salryn.Sync.Contracts;

public static class SaleCommittedV1EnvelopePolicy
{
    public static SaleCommittedV1ValidationResult ValidateIntrinsic(
        SyncOperationEnvelopeV1 envelope,
        SaleCommittedV1 payload)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(payload);

        SyncEnvelopeValidationResult envelopeValidation = SyncProtocolV1.Validate(envelope);
        if (!envelopeValidation.IsValid)
        {
            return SaleCommittedV1ValidationResult.Fail(
                envelopeValidation.ErrorCode ?? "SaleCommittedV1.EnvelopeInvalid",
                envelopeValidation.ErrorMessage ?? "Sync envelope is invalid.");
        }

        if (!string.Equals(
                envelope.OperationType,
                SaleCommittedV1Contract.OperationType,
                StringComparison.Ordinal) ||
            envelope.SchemaVersion != SaleCommittedV1Contract.SchemaVersion)
        {
            return SaleCommittedV1ValidationResult.Fail(
                "SaleCommittedV1.DispatchContractMismatch",
                "Operation type/schema does not identify SaleCommittedV1 schema version 1.");
        }

        if (envelope.AggregateId != payload.Sale.Id)
        {
            return SaleCommittedV1ValidationResult.Fail(
                "SaleCommittedV1.AggregateMismatch",
                "Envelope aggregate ID must equal the committed sale ID.");
        }

        if (envelope.ActorUserId != payload.Sale.CashierUserId)
        {
            return SaleCommittedV1ValidationResult.Fail(
                "SaleCommittedV1.ActorMismatch",
                "Envelope actor must equal the committed sale cashier.");
        }

        return SaleCommittedV1Validator.Validate(payload);
    }

    public static SaleCommittedV1ValidationResult ValidateForCentral(
        SyncOperationEnvelopeV1 envelope,
        SaleCommittedV1 payload,
        Guid expectedStoreId)
    {
        SaleCommittedV1ValidationResult intrinsic = ValidateIntrinsic(envelope, payload);
        if (!intrinsic.IsValid)
        {
            return intrinsic;
        }

        if (expectedStoreId == Guid.Empty || envelope.StoreId != expectedStoreId)
        {
            return SaleCommittedV1ValidationResult.Fail(
                "SaleCommittedV1.StoreMismatch",
                "Envelope store does not match the central deployment store.");
        }

        return SaleCommittedV1ValidationResult.Success();
    }
}
