namespace Salryn.Security.Pairing;

public enum PushFenceAction
{
    AllowLiveLedger = 1,
    QuarantineRevokedOutbox = 2,
    Reject = 3
}

public sealed record PushFenceEvaluation(
    PushFenceAction Action,
    int HttpStatusCode,
    string? Code,
    string? Message,
    IReadOnlyList<Guid> QuarantinedOperationIds);

/// <summary>
/// Evaluates Central sync push/pull fencing for revoked devices (per ADR-I4 #70/#80).
/// Revoked devices cannot mutate live ledgers or pull data, but signed offline push batches
/// are directed to sync_revoked_quarantine (403 DEVICE_REVOKED_FENCED) so financial records
/// are never silently dropped.
/// </summary>
public static class DeviceRevocationFenceEvaluator
{
    public static PushFenceEvaluation EvaluatePush(
        EnrolledDeviceRecord? device,
        TenantScope scope,
        ReadOnlySpan<byte> canonicalBatchPayload,
        byte[]? signature,
        IReadOnlyList<Guid> operationIds)
    {
        PairingResult scopeCheck = ProductScopeGuard.ValidateProductAndTenant(scope);
        if (scopeCheck.IsFailure)
        {
            return new PushFenceEvaluation(
                PushFenceAction.Reject,
                scopeCheck.HttpStatusCode,
                scopeCheck.ErrorCode,
                scopeCheck.ErrorMessage,
                Array.Empty<Guid>());
        }

        if (device is null)
        {
            return new PushFenceEvaluation(
                PushFenceAction.Reject,
                404,
                PairingErrorCodes.DeviceNotFound,
                "Device is not enrolled.",
                Array.Empty<Guid>());
        }

        PairingResult match = ProductScopeGuard.ValidateExactMatch(scope, device.Scope);
        if (match.IsFailure)
        {
            return new PushFenceEvaluation(
                PushFenceAction.Reject,
                match.HttpStatusCode,
                match.ErrorCode,
                match.ErrorMessage,
                Array.Empty<Guid>());
        }

        if (!SecretCrypto.VerifyEcdsaP256(device.SpkiPublicKey, canonicalBatchPayload, signature))
        {
            return new PushFenceEvaluation(
                PushFenceAction.Reject,
                403,
                PairingErrorCodes.InvalidSignature,
                "Push batch signature verification failed.",
                Array.Empty<Guid>());
        }

        if (device.Status == DeviceStatus.Revoked)
        {
            return new PushFenceEvaluation(
                PushFenceAction.QuarantineRevokedOutbox,
                403,
                PairingErrorCodes.DeviceRevokedFenced,
                "Device is revoked; signed offline outbox fenced into sync_revoked_quarantine for admin review.",
                operationIds.ToArray());
        }

        if (device.Status != DeviceStatus.Active)
        {
            return new PushFenceEvaluation(
                PushFenceAction.Reject,
                403,
                PairingErrorCodes.DeviceDisabled,
                $"Device is in state '{device.Status}' and cannot push.",
                Array.Empty<Guid>());
        }

        return new PushFenceEvaluation(
            PushFenceAction.AllowLiveLedger,
            200,
            null,
            null,
            Array.Empty<Guid>());
    }

    public static PairingResult EvaluatePullOrToken(EnrolledDeviceRecord? device, TenantScope scope)
    {
        PairingResult scopeCheck = ProductScopeGuard.ValidateProductAndTenant(scope);
        if (scopeCheck.IsFailure)
        {
            return scopeCheck;
        }

        if (device is null)
        {
            return PairingResult.Fail(PairingErrorCodes.DeviceNotFound, "Device is not enrolled.", 404);
        }

        PairingResult match = ProductScopeGuard.ValidateExactMatch(scope, device.Scope);
        if (match.IsFailure)
        {
            return match;
        }

        if (device.Status == DeviceStatus.Revoked)
        {
            return PairingResult.Fail(
                PairingErrorCodes.DeviceRevoked,
                "Device is revoked; pull and token renewal are denied.",
                403);
        }

        if (device.Status != DeviceStatus.Active)
        {
            return PairingResult.Fail(
                PairingErrorCodes.DeviceDisabled,
                "Device is not active.",
                403);
        }

        return PairingResult.Success();
    }
}
