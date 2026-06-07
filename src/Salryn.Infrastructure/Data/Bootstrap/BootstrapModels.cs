namespace Salryn.Infrastructure.Data.Bootstrap;

public sealed record FirstRunStatus(
    bool IsDatabaseInitialized,
    bool IsFirstRunRequired,
    bool HasUsers,
    bool HasStoreSettings);

public sealed record AdminBootstrapRequest(
    string StoreName,
    string? BusinessName,
    string? Address,
    string? ContactNumber,
    string? Tin,
    int ReceiptWidthMm,
    string AdminUsername,
    string AdminDisplayName,
    string AdminPassword);

public sealed record AdminBootstrapSummary(
    Guid AdminUserId,
    Guid StoreSettingsId,
    DateTimeOffset CreatedAt);
