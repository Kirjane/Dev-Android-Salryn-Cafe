using Salryn.Core.ValueObjects;

namespace Salryn.Core.Models;

/// <summary>
/// Append-only physical cash-drawer movement tied to one cashier session.
/// Positive deltas add cash to the drawer; negative deltas remove cash.
/// Posted movements are corrected by compensating reversal movements, never mutation.
/// </summary>
public sealed class CashMovement
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid CashierSessionId { get; init; }

    public Guid ActorUserId { get; init; }

    public CashMovementType MovementType { get; init; }

    public Money Delta { get; init; }

    public required string ReferenceType { get; init; }

    public Guid ReferenceId { get; init; }

    public Guid? ReversalOfMovementId { get; init; }

    public string? Reason { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
