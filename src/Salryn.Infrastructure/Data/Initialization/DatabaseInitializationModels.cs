using Salryn.Infrastructure.Data.Bootstrap;

namespace Salryn.Infrastructure.Data.Initialization;

public sealed record DatabaseInitializationStatus(
    string DatabasePath,
    bool DatabaseFileExists,
    int ConfiguredMigrationCount,
    bool HasSeedDataFoundation,
    FirstRunStatus FirstRunStatus,
    int PendingMigrationCount = 0)
{
    public bool RequiresDatabaseUpgrade => DatabaseFileExists && PendingMigrationCount > 0;

    public bool CanRunFirstRunBootstrap =>
        DatabaseFileExists &&
        !RequiresDatabaseUpgrade &&
        FirstRunStatus.IsDatabaseInitialized &&
        FirstRunStatus.IsFirstRunRequired;
}

public sealed record DatabaseInitializationSummary(
    string DatabasePath,
    int ConfiguredMigrationCount,
    FirstRunStatus FirstRunStatus,
    DateTimeOffset InitializedAt);
