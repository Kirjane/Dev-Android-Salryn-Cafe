using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Seeding;

public sealed class DatabaseSeeder
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public DatabaseSeeder(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public void SeedDefaultData()
    {
        using var connection = _connectionFactory.CreateConnection();
        connection.Open();

        SqlitePragma.Apply(connection);

        using var transaction = connection.BeginTransaction();

        SeedDefaultData(connection, transaction);

        transaction.Commit();
    }

    public static void SeedDefaultData(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");

        SeedAppMetadata(connection, transaction, now);
        SeedRoles(connection, transaction, now);
        SeedRolePermissions(connection, transaction, now);
        SeedUnits(connection, transaction);
        SeedCategories(connection, transaction);
        SeedBrands(connection, transaction);
        SeedReceiptSequence(connection, transaction, now);
    }

    private static void SeedAppMetadata(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string now)
    {
        Execute(connection, transaction, """
            INSERT OR IGNORE INTO app_metadata (key, value, updated_at)
            VALUES ($key, $value, $updatedAt);
            """,
            new Dictionary<string, object?>
            {
                ["$key"] = "seed_version",
                ["$value"] = "1",
                ["$updatedAt"] = now
            });

        Execute(connection, transaction, """
            INSERT OR IGNORE INTO app_metadata (key, value, updated_at)
            VALUES ($key, $value, $updatedAt);
            """,
            new Dictionary<string, object?>
            {
                ["$key"] = "first_run_required",
                ["$value"] = "true",
                ["$updatedAt"] = now
            });
    }

    private static void SeedRoles(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string now)
    {
        foreach (var role in DefaultSeedData.Roles)
        {
            Execute(connection, transaction, """
                INSERT OR IGNORE INTO roles (id, name, is_system_role, created_at)
                VALUES ($id, $name, $isSystemRole, $createdAt);
                """,
                new Dictionary<string, object?>
                {
                    ["$id"] = role.Id,
                    ["$name"] = role.Name,
                    ["$isSystemRole"] = role.IsSystemRole ? 1 : 0,
                    ["$createdAt"] = now
                });
        }
    }

    private static void SeedRolePermissions(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string now)
    {
        foreach (var rolePermission in DefaultSeedData.RolePermissions)
        {
            Execute(connection, transaction, """
                INSERT OR IGNORE INTO role_permissions (id, role_id, permission, created_at)
                VALUES ($id, $roleId, $permission, $createdAt);
                """,
                new Dictionary<string, object?>
                {
                    ["$id"] = Guid.NewGuid().ToString("D"),
                    ["$roleId"] = rolePermission.RoleId,
                    ["$permission"] = rolePermission.Permission,
                    ["$createdAt"] = now
                });
        }
    }

    private static void SeedUnits(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        foreach (var unit in DefaultSeedData.Units)
        {
            Execute(connection, transaction, """
                INSERT OR IGNORE INTO units (id, name, abbreviation, allows_decimal_quantity, is_active)
                VALUES ($id, $name, $abbreviation, $allowsDecimalQuantity, 1);
                """,
                new Dictionary<string, object?>
                {
                    ["$id"] = unit.Id,
                    ["$name"] = unit.Name,
                    ["$abbreviation"] = unit.Abbreviation,
                    ["$allowsDecimalQuantity"] = unit.AllowsDecimalQuantity ? 1 : 0
                });
        }
    }

    private static void SeedCategories(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        foreach (var category in DefaultSeedData.Categories)
        {
            Execute(connection, transaction, """
                INSERT OR IGNORE INTO categories (id, name, is_active)
                VALUES ($id, $name, 1);
                """,
                new Dictionary<string, object?>
                {
                    ["$id"] = category.Id,
                    ["$name"] = category.Name
                });
        }
    }

    private static void SeedBrands(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        foreach (var brand in DefaultSeedData.Brands)
        {
            Execute(connection, transaction, """
                INSERT OR IGNORE INTO brands (id, name, is_active)
                VALUES ($id, $name, 1);
                """,
                new Dictionary<string, object?>
                {
                    ["$id"] = brand.Id,
                    ["$name"] = brand.Name
                });
        }
    }

    private static void SeedReceiptSequence(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string now)
    {
        var sequence = DefaultSeedData.ReceiptSequence;

        Execute(connection, transaction, """
            INSERT OR IGNORE INTO receipt_sequences
                (id, prefix, year, current_number, padding_digits, updated_at)
            VALUES
                ($id, $prefix, $year, $currentNumber, $paddingDigits, $updatedAt);
            """,
            new Dictionary<string, object?>
            {
                ["$id"] = sequence.Id,
                ["$prefix"] = sequence.Prefix,
                ["$year"] = sequence.Year,
                ["$currentNumber"] = sequence.CurrentNumber,
                ["$paddingDigits"] = sequence.PaddingDigits,
                ["$updatedAt"] = now
            });
    }

    private static void Execute(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        IReadOnlyDictionary<string, object?> parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(
                parameter.Key,
                parameter.Value ?? DBNull.Value);
        }

        command.ExecuteNonQuery();
    }
}
