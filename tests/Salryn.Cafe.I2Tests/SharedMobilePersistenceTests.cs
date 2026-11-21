using Microsoft.Data.Sqlite;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Paths;
using Salryn.Mobile.Runtime;
using Xunit;

namespace Salryn.Cafe.I2Tests;

public sealed class SharedMobilePersistenceTests
{
    [Fact]
    public async Task MobileCompositionUsesCanonicalCafeSchemaAndOnePortablePersistenceAssembly()
    {
        string root = Path.Combine(Path.GetTempPath(), "cafe-shared-mobile-" + Guid.NewGuid().ToString("N"));
        try
        {
            var composition = MobileProductionComposition.Create(root);
            Assert.Equal(Path.Combine(root, "data", "salryn-cafe.db"), composition.ConnectionFactory.DatabasePath);
            await composition.InitializeDatabaseAsync();
            var readiness = composition.InspectReadiness();
            Assert.True(readiness.IsReady, "Canonical Cafe schema must satisfy mobile readiness.");
            Assert.Equal(Salryn.Infrastructure.Data.Migrations.CafeMigrationCatalog.CurrentCafeSchemaVersion, readiness.MaxMigrationVersion);
            Assert.Equal("Salryn.Persistence.Sqlite", typeof(SqliteConnectionFactory).Assembly.GetName().Name);
            Assert.Equal(typeof(SqliteConnectionFactory).Assembly, typeof(DbPaths).Assembly);
            Assert.DoesNotContain(readiness.Tables, t => t.StartsWith("mobile_"));
            Assert.NotNull(composition.CafeOrders);
            Assert.NotNull(composition.AccountsReceivable);
            Assert.NotNull(composition.SyncProjection);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}

