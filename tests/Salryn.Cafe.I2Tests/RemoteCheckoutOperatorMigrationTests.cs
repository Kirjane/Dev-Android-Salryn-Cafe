using Microsoft.Data.Sqlite;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Migrations;
using Salryn.Infrastructure.Paths;
using Xunit;

namespace Salryn.Cafe.I2Tests;

public sealed class RemoteCheckoutOperatorMigrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OperatorBindingIsAdditiveAndImmutable(bool retainedV41)
    {
        string root = Path.Combine(Path.GetTempPath(), "cafe-binding-" + Guid.NewGuid().ToString("N"));
        DbPaths paths = DbPaths.CreateDevelopment(root);
        paths.EnsureDirectories();
        var factory = new SqliteConnectionFactory(paths);
        var migrations = CafeMigrationCatalog.CreateCanonicalCafeMigrations();
        var runner = new MigrationRunner(factory, retainedV41 ? migrations.Where(x => x.Version <= 41).ToArray() : migrations);
        runner.RunPendingMigrations();
        using var connection = factory.CreateConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        if (retainedV41)
        {
            command.CommandText = "INSERT INTO remote_pos_checkout_requests(store_id,terminal_id,client_request_id,request_hash,state,receipt_no,message,created_at_utc,updated_at_utc) VALUES('s','t','r','h','Completed','receipt','retained','now','now');";
            command.ExecuteNonQuery();
            new MigrationRunner(factory, migrations).RunPendingMigrations();
        }
        Assert.True(SchemaLineageClassifier.ColumnExists(connection, null, "remote_pos_checkout_requests", "operator_user_id"));
        if (retainedV41)
        {
            command.CommandText = "SELECT message FROM remote_pos_checkout_requests WHERE client_request_id='r' AND operator_user_id IS NULL;";
            Assert.Equal("retained", command.ExecuteScalar());
            command.CommandText = "UPDATE remote_pos_checkout_requests SET operator_user_id='inferred' WHERE client_request_id='r';";
            Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
        }
        command.CommandText = "INSERT INTO remote_pos_checkout_requests(store_id,terminal_id,client_request_id,request_hash,state,receipt_no,message,created_at_utc,updated_at_utc,operator_user_id) VALUES('s','t','bound','h','Pending','boundreceipt','bound','now','now','actorA');";
        command.ExecuteNonQuery();
        command.CommandText = "UPDATE remote_pos_checkout_requests SET operator_user_id='actorB' WHERE client_request_id='bound';";
        Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
        command.CommandText = "SELECT operator_user_id FROM remote_pos_checkout_requests WHERE client_request_id='bound';";
        Assert.Equal("actorA", command.ExecuteScalar());
    }
    [Fact]
    public void RetainedV42UpgradePreservesOutcomesAndAddsExecutionFence()
    {
        string root = Path.Combine(Path.GetTempPath(), "cafe-fence-upgrade-" + Guid.NewGuid().ToString("N"));
        var paths = DbPaths.CreateDevelopment(root);
        paths.EnsureDirectories();
        var factory = new SqliteConnectionFactory(paths);
        var migrations = CafeMigrationCatalog.CreateCanonicalCafeMigrations();
        new MigrationRunner(factory, migrations.Where(x => x.Version <= 42).ToArray()).RunPendingMigrations();
        using var connection = factory.CreateConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO cafe_mutation_idempotency VALUES('legacy','POST /synthetic','actor','hash',200,'retained-response','now');";
        command.ExecuteNonQuery();
        new MigrationRunner(factory, migrations).RunPendingMigrations();
        command.CommandText = "SELECT response_json FROM cafe_mutation_idempotency WHERE operation_id='legacy';";
        Assert.Equal("retained-response", command.ExecuteScalar());
        command.CommandText = "SELECT COUNT(*) FROM cafe_mutation_execution_fences;";
        Assert.Equal(0L, Convert.ToInt64(command.ExecuteScalar()));
        command.CommandText = "INSERT INTO cafe_mutation_execution_fences VALUES('op','endpoint','actor','hash','now');";
        command.ExecuteNonQuery();
        command.CommandText = "UPDATE cafe_mutation_execution_fences SET request_hash='changed' WHERE operation_id='op';";
        Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
        Assert.Equal(SchemaLineageKind.SalrynCafe, new MigrationRunner(factory, migrations).ClassifyDatabase().Kind);
        Assert.Equal(0, new MigrationRunner(factory, migrations).GetPendingMigrationCount());
    }

}
