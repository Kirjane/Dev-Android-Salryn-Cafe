namespace Salryn.Core.TerminalRegistry;

public enum TerminalRegistrationRole
{
    CashierClient = 1,
    AdminClient = 2,
    AllInOneLocalTerminal = 3,
    HostConsole = 4,
    FutureMobileDashboardReadOnly = 99
}

public enum TerminalRegistrationStatus
{
    PendingPairing = 1,
    Active = 2,
    Disabled = 3,
    Revoked = 4
}

public enum PairingInviteStatus
{
    Issued = 1,
    Consumed = 2,
    Expired = 3,
    Revoked = 4
}

public sealed record SalrynTerminalRegistration(
    Guid TerminalId,
    Guid StoreId,
    Guid HostId,
    string TerminalName,
    TerminalRegistrationRole TerminalRole,
    TerminalRegistrationStatus Status,
    DateTimeOffset RegisteredAtUtc,
    DateTimeOffset? LastSeenAtUtc,
    DateTimeOffset? DisabledAtUtc,
    DateTimeOffset? RevokedAtUtc,
    Guid CreatedByUserId,
    string? Notes);

public sealed record SalrynTerminalPairingInvite(
    Guid PairingId,
    Guid StoreId,
    Guid HostId,
    string TargetTerminalName,
    TerminalRegistrationRole TargetTerminalRole,
    PairingInviteStatus Status,
    string PairingCodeHash,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? ConsumedAtUtc,
    DateTimeOffset? RevokedAtUtc,
    Guid CreatedByUserId);

public sealed record PairingInviteCreationResult(
    SalrynTerminalPairingInvite Invite,
    string PairingCode);

public sealed record TerminalRegistryActorContext(
    Guid ActorUserId,
    IReadOnlyCollection<string> Permissions);
