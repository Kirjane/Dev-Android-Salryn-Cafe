using Microsoft.Data.Sqlite;

namespace Salryn.Infrastructure.Data.Migrations;

public enum SchemaLineageKind
{
    Fresh = 0,
    SalrynCafe = 1,
    YzaV39Legacy = 2,
    RetailKernel = 3,
    UnknownOrCorrupt = 4
}

public sealed record SchemaLineageClassification(
    SchemaLineageKind Kind,
    string? ProductId,
    int MaxAppliedVersion,
    string Reason);

public sealed class WrongProductDatabaseException : InvalidOperationException
{
    public const string ErrorCode = "ERR_WRONG_PRODUCT_DATABASE";

    public WrongProductDatabaseException(string message)
        : base($"{ErrorCode}: {message}")
    {
    }
}

public sealed class UnknownSchemaLineageException : InvalidOperationException
{
    public const string ErrorCode = "ERR_UNKNOWN_SCHEMA_LINEAGE";

    public UnknownSchemaLineageException(string message)
        : base($"{ErrorCode}: {message}")
    {
    }
}

/// <summary>
/// Pre-migration database lineage classifier (ADR-I2 / Issue #4).
/// Fails closed on Retail/General Merchandise or unknown/corrupt databases without mutating data.
/// </summary>
public static class SchemaLineageClassifier
{
    public const string SalrynCafeProductId = "Salryn.Cafe";
    public const int MaxSupportedCafeSchemaVersion = 41;

    private static readonly string[] RetailOnlyTables =
    {
        "purchase_orders",
        "purchase_order_lines",
        "goods_receipts",
        "supplier_invoices",
        "supplier_payments",
        "customer_quotations",
        "customer_orders"
    };

    private static readonly string[] YzaCafeSignatureTables =
    {
        "menu_variants",
        "ingredients",
        "cafe_orders",
        "cafe_attendance_events",
        "cash_movements",
        "sync_outbox",
        "cashier_session_reports"
    };

    public static SchemaLineageClassification Classify(SqliteConnection connection)
    {
        HashSet<string> tables = ReadUserTables(connection);
        if (tables.Count == 0)
        {
            return new SchemaLineageClassification(
                SchemaLineageKind.Fresh,
                null,
                0,
                "Database contains no user tables.");
        }

        bool hasSchemaMigrations = tables.Contains("schema_migrations");
        Dictionary<int, string> appliedMigrations = hasSchemaMigrations
            ? ReadAppliedMigrations(connection)
            : new Dictionary<int, string>();
        int maxVersion = appliedMigrations.Count == 0 ? 0 : appliedMigrations.Keys.Max();

        // 1. Explicit lineage marker takes highest precedence.
        if (tables.Contains("salryn_schema_lineage"))
        {
            string? productId = ReadLineageProductId(connection);
            if (string.Equals(productId, SalrynCafeProductId, StringComparison.Ordinal))
            {
                if (!hasSchemaMigrations || maxVersion < 40 || maxVersion > MaxSupportedCafeSchemaVersion)
                {
                    return new SchemaLineageClassification(
                        SchemaLineageKind.UnknownOrCorrupt,
                        productId,
                        maxVersion,
                        $"Salryn.Cafe lineage marker present, but schema_migrations max version ({maxVersion}) is out of supported bounds [40..{MaxSupportedCafeSchemaVersion}].");
                }

                return new SchemaLineageClassification(
                    SchemaLineageKind.SalrynCafe,
                    productId,
                    maxVersion,
                    $"Verified Salryn.Cafe lineage at v{maxVersion}.");
            }

            if (!string.IsNullOrWhiteSpace(productId))
            {
                return new SchemaLineageClassification(
                    SchemaLineageKind.RetailKernel,
                    productId,
                    maxVersion,
                    $"Database lineage marker belongs to product '{productId}', not '{SalrynCafeProductId}'.");
            }

            return new SchemaLineageClassification(
                SchemaLineageKind.UnknownOrCorrupt,
                null,
                maxVersion,
                "salryn_schema_lineage table exists but contains no valid product_id row.");
        }

        // 2. Non-empty database without schema_migrations is unknown/corrupt (never silently reset).
        if (!hasSchemaMigrations)
        {
            return new SchemaLineageClassification(
                SchemaLineageKind.UnknownOrCorrupt,
                null,
                0,
                $"Non-empty database ({tables.Count} tables) is missing schema_migrations.");
        }

        if (appliedMigrations.Count == 0)
        {
            // Only schema_migrations exists with 0 rows and no other domain tables -> treat as Fresh.
            if (tables.Count == 1)
            {
                return new SchemaLineageClassification(
                    SchemaLineageKind.Fresh,
                    null,
                    0,
                    "Only empty schema_migrations table exists.");
            }

            return new SchemaLineageClassification(
                SchemaLineageKind.UnknownOrCorrupt,
                null,
                0,
                $"schema_migrations is empty while {tables.Count - 1} other tables exist.");
        }

        bool hasRetailTables = RetailOnlyTables.Any(tables.Contains);
        bool hasCafeTables = YzaCafeSignatureTables.Any(tables.Contains);

        bool hasRetailMigrationSignature = appliedMigrations.Any(kvp =>
            kvp.Value.Contains("Procure-to-Pay", StringComparison.OrdinalIgnoreCase) ||
            kvp.Value.Contains("ProcureToPay", StringComparison.OrdinalIgnoreCase) ||
            kvp.Value.Contains("Customer orders", StringComparison.OrdinalIgnoreCase) ||
            kvp.Value.Contains("Supplier payment", StringComparison.OrdinalIgnoreCase) ||
            (kvp.Key == 25 && kvp.Value.Contains("cash drawer", StringComparison.OrdinalIgnoreCase)) ||
            (kvp.Key == 26 && kvp.Value.Contains("cash drawer", StringComparison.OrdinalIgnoreCase)));

        if (hasRetailTables && hasCafeTables)
        {
            return new SchemaLineageClassification(
                SchemaLineageKind.UnknownOrCorrupt,
                null,
                maxVersion,
                "Database contains a mixed combination of retail-only and Cafe Yza tables.");
        }

        if (hasRetailTables || (hasRetailMigrationSignature && !hasCafeTables))
        {
            return new SchemaLineageClassification(
                SchemaLineageKind.RetailKernel,
                "Salryn.Retail",
                maxVersion,
                $"Detected Retail/General Merchandise Kernel schema (max v{maxVersion}) without Salryn Cafe lineage.");
        }

        // 3. Yza v39 (or legacy Yza v25..v39) lineage adoption path.
        if (hasCafeTables && maxVersion >= 25 && maxVersion <= 39)
        {
            bool hasV23 = appliedMigrations.ContainsKey(23);
            if (hasV23 && !tables.Contains("host_checkout_requests"))
            {
                return new SchemaLineageClassification(
                    SchemaLineageKind.UnknownOrCorrupt,
                    null,
                    maxVersion,
                    "schema_migrations claims v23 is applied, but host_checkout_requests is missing.");
            }

            return new SchemaLineageClassification(
                SchemaLineageKind.YzaV39Legacy,
                null,
                maxVersion,
                $"Detected legacy Cafe Yza lineage at v{maxVersion}; eligible for zero-data-loss adoption at v40.");
        }

        // 4. Shared pre-divergence base (v1..v24) without retail-only tables is also fail-closed unless explicitly Fresh/Yza/Cafe.
        return new SchemaLineageClassification(
            SchemaLineageKind.UnknownOrCorrupt,
            null,
            maxVersion,
            $"Unrecognized schema lineage (max v{maxVersion}, tables={tables.Count}, hasCafeTables={hasCafeTables}).");
    }

    public static SchemaLineageClassification EnsureAllowedForCafeMigration(SqliteConnection connection)
    {
        SchemaLineageClassification result = Classify(connection);
        return result.Kind switch
        {
            SchemaLineageKind.Fresh => result,
            SchemaLineageKind.SalrynCafe => result,
            SchemaLineageKind.YzaV39Legacy => result,
            SchemaLineageKind.RetailKernel => throw new WrongProductDatabaseException(result.Reason),
            _ => throw new UnknownSchemaLineageException(result.Reason)
        };
    }

    public static bool ColumnExists(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string tableName,
        string columnName)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info({tableName});";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool TableExists(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string tableName)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1
            FROM sqlite_master
            WHERE type = 'table' AND name = $name
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$name", tableName);
        return command.ExecuteScalar() is not null;
    }

    private static HashSet<string> ReadUserTables(SqliteConnection connection)
    {
        HashSet<string> tables = new(StringComparer.OrdinalIgnoreCase);
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT name
            FROM sqlite_master
            WHERE type = 'table'
              AND name NOT LIKE 'sqlite_%';
            """;
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private static Dictionary<int, string> ReadAppliedMigrations(SqliteConnection connection)
    {
        Dictionary<int, string> applied = new();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT version, name FROM schema_migrations;";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            applied[reader.GetInt32(0)] = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
        }

        return applied;
    }

    private static string? ReadLineageProductId(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT product_id FROM salryn_schema_lineage WHERE id = 1 LIMIT 1;";
        object? scalar = command.ExecuteScalar();
        return scalar is string s ? s : null;
    }
}
