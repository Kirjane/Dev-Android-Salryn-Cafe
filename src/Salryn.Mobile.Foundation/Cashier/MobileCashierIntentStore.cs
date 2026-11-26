using System.Text.Json;
using Salryn.Mobile.Foundation.P0;

namespace Salryn.Mobile.Foundation.Cashier;

public sealed record MobilePendingCashierIntent(string Kind, MobileOrderDraft? Draft = null,
    MobileOrderAction? Action = null, string? TargetState = null, MobileCheckoutIntent? Checkout = null,
    Guid ActorUserId = default, MobileShiftOpenIntent? ShiftOpen = null, MobileShiftCloseIntent? ShiftClose = null,
    MobileRefundRequestIntent? RefundRequest = null, MobileRefundResolutionIntent? RefundResolution = null,
    MobileRefundCommitIntent? RefundCommit = null,
    string? LastErrorCode = null, string? LastErrorMessage = null, int AttemptCount = 0,
    MobileOrderCancelAction? CancelOrder = null, MobileDeliveryAdvanceIntent? DeliveryAdvance = null)
{
    public Guid OperationId => Draft?.OperationId ?? Action?.OperationId ?? Checkout?.OperationId
        ?? ShiftOpen?.OperationId ?? ShiftClose?.OperationId ?? RefundRequest?.OperationId
        ?? RefundResolution?.OperationId ?? RefundCommit?.OperationId
        ?? CancelOrder?.OperationId ?? DeliveryAdvance?.OperationId ?? Guid.Empty;

    internal MobilePendingCashierIntent WithoutDiagnostics() =>
        this with { LastErrorCode = null, LastErrorMessage = null, AttemptCount = 0 };
}

public sealed record MobilePendingIntentResolutionRecord(
    Guid OperationId,
    string Kind,
    Guid OriginalActorUserId,
    Guid ResolvedByUserId,
    string ResolutionStatus,
    string Reason,
    DateTimeOffset ResolvedAtUtc);

/// <summary>Recoverable UI intent, not a second business/outbox ledger. Persist before
/// invoking canonical domain; retain on lost response, restart, or denied reconciliation.</summary>
public sealed class MobileCashierIntentStore(IDeviceSecretStore secrets)
{
    public const string SecretKey = "salryn.cafe.pending_cashier_intent.v1";
    public const string ResolutionHistorySecretKey = "salryn.cafe.pending_cashier_intent_resolutions.v1";
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<MobilePendingCashierIntent?> ReadAsync(CancellationToken ct = default)
    {
        byte[]? bytes = await secrets.ReadAsync(SecretKey, ct).ConfigureAwait(false);
        return bytes is null || bytes.Length == 0 ? null : JsonSerializer.Deserialize<MobilePendingCashierIntent>(bytes)
            ?? throw new InvalidOperationException("Pending cashier intent is unreadable; preserve it for recovery.");
    }

    public async Task SaveBeforeDispatchAsync(MobilePendingCashierIntent intent, CancellationToken ct = default)
    {
        if (intent.OperationId == Guid.Empty || intent.ActorUserId == Guid.Empty)
            throw new ArgumentException("A durable client operation ID and original operator are required.", nameof(intent));
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var pending = await ReadAsync(ct).ConfigureAwait(false);
            if (pending is not null)
            {
                if (JsonSerializer.Serialize(pending.WithoutDiagnostics()) != JsonSerializer.Serialize(intent.WithoutDiagnostics()))
                    throw new InvalidOperationException("An earlier cashier intent is unresolved. Reconcile it before creating a new action.");
                return;
            }
            await secrets.WriteAsync(SecretKey, JsonSerializer.SerializeToUtf8Bytes(intent), ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<MobilePendingCashierIntent> RecordAttemptFailureAsync(
        Guid operationId,
        string errorCode,
        string errorMessage,
        CancellationToken ct = default)
    {
        if (operationId == Guid.Empty)
            throw new ArgumentException("Operation ID is required.", nameof(operationId));
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var pending = await ReadAsync(ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("No pending cashier intent exists to record failure against.");
            if (pending.OperationId != operationId)
                throw new InvalidOperationException("Cannot record failure for another cashier intent.");
            var updated = pending with
            {
                LastErrorCode = string.IsNullOrWhiteSpace(errorCode) ? "Mobile.UnknownError" : errorCode.Trim(),
                LastErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? "Action failed." : errorMessage.Trim(),
                AttemptCount = pending.AttemptCount + 1
            };
            await secrets.WriteAsync(SecretKey, JsonSerializer.SerializeToUtf8Bytes(updated), ct).ConfigureAwait(false);
            return updated;
        }
        finally { _gate.Release(); }
    }

    public async Task<MobilePendingIntentResolutionRecord> ResolveRejectedAsync(
        Guid operationId,
        Guid resolvedByUserId,
        string reason,
        DateTimeOffset resolvedAtUtc,
        CancellationToken ct = default)
    {
        if (operationId == Guid.Empty || resolvedByUserId == Guid.Empty)
            throw new ArgumentException("Operation ID and resolving operator ID are required.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A resolution reason is required to dismiss a rejected pending intent.", nameof(reason));
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var pending = await ReadAsync(ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("No pending cashier intent exists to resolve.");
            if (pending.OperationId != operationId)
                throw new InvalidOperationException("Cannot resolve another cashier intent.");

            var record = new MobilePendingIntentResolutionRecord(
                pending.OperationId,
                pending.Kind,
                pending.ActorUserId,
                resolvedByUserId,
                "DismissedRejected",
                reason.Trim(),
                resolvedAtUtc);

            var history = (await ReadResolutionHistoryCoreAsync(ct).ConfigureAwait(false)).ToList();
            history.Add(record);
            await secrets.WriteAsync(ResolutionHistorySecretKey, JsonSerializer.SerializeToUtf8Bytes(history), ct).ConfigureAwait(false);
            await secrets.WriteAsync(SecretKey, [], ct).ConfigureAwait(false);
            return record;
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<MobilePendingIntentResolutionRecord>> ReadResolutionHistoryAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ReadResolutionHistoryCoreAsync(ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task ClearConfirmedAsync(Guid operationId, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var pending = await ReadAsync(ct).ConfigureAwait(false);
            if (pending is not null && pending.OperationId != operationId)
                throw new InvalidOperationException("Cannot clear another cashier intent.");
            await secrets.WriteAsync(SecretKey, [], ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task<IReadOnlyList<MobilePendingIntentResolutionRecord>> ReadResolutionHistoryCoreAsync(CancellationToken ct)
    {
        byte[]? bytes = await secrets.ReadAsync(ResolutionHistorySecretKey, ct).ConfigureAwait(false);
        if (bytes is null || bytes.Length == 0) return [];
        return JsonSerializer.Deserialize<List<MobilePendingIntentResolutionRecord>>(bytes) ?? [];
    }
}
