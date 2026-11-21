using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Migrations;
using Xunit;

namespace Salryn.Cafe.I2Tests;

public sealed class ExplicitDatabasePathTests
{
    [Fact]
    public void ExplicitCentralPathUsesCanonicalCafeMigrationsAndRejectsUnclassifiedTransport()
    {
        string root = Path.Combine(Path.GetTempPath(), "cafe-central-path-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "central.db");
        var factory = new SqliteConnectionFactory(path);
        var runner = new MigrationRunner(factory, CafeMigrationCatalog.CreateCanonicalCafeMigrations());
        runner.RunPendingMigrations();
        Assert.Equal(path, factory.DatabasePath);
        Assert.Equal(SchemaLineageKind.SalrynCafe, runner.ClassifyDatabase().Kind);
        Assert.Equal(0, runner.GetPendingMigrationCount());
        var transport = new SqliteConnectionFactory(Path.Combine(root, "transport-only.db"));
        using var connection = transport.CreateConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE sync_server_state(id INTEGER); INSERT INTO sync_server_state VALUES(123);";
        command.ExecuteNonQuery();
        Assert.Throws<UnknownSchemaLineageException>(() => new MigrationRunner(transport, CafeMigrationCatalog.CreateCanonicalCafeMigrations()).RunPendingMigrations());
        command.CommandText = "SELECT id FROM sync_server_state;";
        Assert.Equal(123L, Convert.ToInt64(command.ExecuteScalar()));
    }
}
