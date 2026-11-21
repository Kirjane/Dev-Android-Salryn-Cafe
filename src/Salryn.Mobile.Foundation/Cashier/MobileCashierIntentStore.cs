using System.Text.Json;
using Salryn.Mobile.Foundation.P0;

namespace Salryn.Mobile.Foundation.Cashier;

public sealed record MobilePendingCashierIntent(string Kind, MobileOrderDraft? Draft = null,
    MobileOrderAction? Action = null, string? TargetState = null, MobileCheckoutIntent? Checkout = null,
    Guid ActorUserId = default)
{
    public Guid OperationId => Draft?.OperationId ?? Action?.OperationId ?? Checkout?.OperationId ?? Guid.Empty;
}

/// <summary>Recoverable UI intent, not a second business/outbox ledger. Persist before
/// invoking canonical domain; retain on lost response, restart, or denied reconciliation.</summary>
public sealed class MobileCashierIntentStore(IDeviceSecretStore secrets)
{
    public const string SecretKey = "salryn.cafe.pending_cashier_intent.v1";
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
                if (JsonSerializer.Serialize(pending) != JsonSerializer.Serialize(intent))
                    throw new InvalidOperationException("An earlier cashier intent is unresolved. Reconcile it before creating a new action.");
                return;
            }
            await secrets.WriteAsync(SecretKey, JsonSerializer.SerializeToUtf8Bytes(intent), ct).ConfigureAwait(false);
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
}
