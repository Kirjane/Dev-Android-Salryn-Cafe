using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Infrastructure.Data.Seeding;
using Salryn.Infrastructure.Security;

namespace Salryn.Infrastructure.Data.Bootstrap;

public sealed class FirstRunBootstrapper
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly IPasswordHasher _passwordHasher;

    public FirstRunBootstrapper(
        SqliteConnectionFactory connectionFactory,
        IPasswordHasher passwordHasher)
    {
        _connectionFactory = connectionFactory;
        _passwordHasher = passwordHasher;
    }

    public Result<FirstRunStatus> CheckStatus()
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            connection.Open();

            SqlitePragma.Apply(connection);

            return Result<FirstRunStatus>.Success(ReadStatus(connection));
        }
        catch (SqliteException ex)
        {
            return Result<FirstRunStatus>.Fail(Error.Unexpected(
                "Bootstrap.StatusCheckFailed",
                $"Unable to check first-run status: {ex.Message}"));
        }
    }

    public Result<AdminBootstrapSummary> CreateFirstAdmin(AdminBootstrapRequest request)
    {
        Result validation = ValidateRequest(request);

        if (validation.IsFailure)
        {
            return Result<AdminBootstrapSummary>.Fail(validation.ErrorDetail);
        }

        try
        {
            using var connection = _connectionFactory.CreateConnection();
            connection.Open();

            SqlitePragma.Apply(connection);

            FirstRunStatus status = ReadStatus(connection);

            if (!status.IsDatabaseInitialized)
            {
                return Result<AdminBootstrapSummary>.Fail(Error.Conflict(
                    "Bootstrap.DatabaseNotInitialized",
                    "Database schema must be initialized before creating the first admin."));
            }

            if (!status.IsFirstRunRequired || status.HasUsers)
            {
                return Result<AdminBootstrapSummary>.Fail(Error.Conflict(
                    "Bootstrap.AlreadyCompleted",
                    "First-run setup has already been completed."));
            }

            using var transaction = connection.BeginTransaction();

            Guid storeSettingsId = Guid.NewGuid();
            Guid adminUserId = Guid.NewGuid();
            DateTimeOffset createdAt = DateTimeOffset.UtcNow;
            string now = createdAt.ToString("O");

            InsertStoreSettings(connection, transaction, request, storeSettingsId, now);
            InsertAdminUser(connection, transaction, request, adminUserId, now);
            MarkFirstRunComplete(connection, transaction, now);
            InsertAuditLog(connection, transaction, adminUserId, storeSettingsId, now);

            transaction.Commit();

            return Result<AdminBootstrapSummary>.Success(new AdminBootstrapSummary(
                adminUserId,
                storeSettingsId,
                createdAt));
        }
        catch (SqliteException ex)
        {
            return Result<AdminBootstrapSummary>.Fail(Error.Unexpected(
                "Bootstrap.CreateAdminFailed",
                $"Unable to complete first-run setup: {ex.Message}"));
        }
    }

    private static Result ValidateRequest(AdminBootstrapRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.StoreName))
        {
            return Result.Fail(Error.Validation(
                "Bootstrap.StoreNameRequired",
                "Store name is required."));
        }

        if (request.ReceiptWidthMm != 58 && request.ReceiptWidthMm != 80)
        {
            return Result.Fail(Error.Validation(
                "Bootstrap.InvalidReceiptWidth",
                "Receipt width must be 58 or 80 millimeters."));
        }

        if (string.IsNullOrWhiteSpace(request.AdminUsername))
        {
            return Result.Fail(Error.Validation(
                "Bootstrap.AdminUsernameRequired",
                "Admin username is required."));
        }

        if (string.IsNullOrWhiteSpace(request.AdminDisplayName))
        {
            return Result.Fail(Error.Validation(
                "Bootstrap.AdminDisplayNameRequired",
                "Admin display name is required."));
        }

        if (string.IsNullOrWhiteSpace(request.AdminPassword) || request.AdminPassword.Length < 8)
        {
            return Result.Fail(Error.Validation(
                "Bootstrap.AdminPasswordTooShort",
                "Admin password must be at least 8 characters."));
        }

        return Result.Success();
    }

    private static FirstRunStatus ReadStatus(SqliteConnection connection)
    {
        bool hasSchemaMigrations = TableExists(connection, "schema_migrations");
        bool hasUsersTable = TableExists(connection, "users");
        bool hasStoreSettingsTable = TableExists(connection, "store_settings");
        bool hasAppMetadataTable = TableExists(connection, "app_metadata");

        bool isDatabaseInitialized =
            hasSchemaMigrations &&
            hasUsersTable &&
            hasStoreSettingsTable &&
            hasAppMetadataTable;

        if (!isDatabaseInitialized)
        {
            return new FirstRunStatus(
                IsDatabaseInitialized: false,
                IsFirstRunRequired: true,
                HasUsers: false,
                HasStoreSettings: false);
        }

        bool hasUsers = RowExists(connection, "SELECT 1 FROM users LIMIT 1;");
        bool hasStoreSettings = RowExists(connection, "SELECT 1 FROM store_settings LIMIT 1;");

        string? firstRunValue = ScalarString(
            connection,
            "SELECT value FROM app_metadata WHERE key = 'first_run_required' LIMIT 1;");

        bool firstRunFlag = !string.Equals(firstRunValue, "false", StringComparison.OrdinalIgnoreCase);

        bool firstRunRequired = firstRunFlag || !hasUsers || !hasStoreSettings;

        return new FirstRunStatus(
            IsDatabaseInitialized: true,
            IsFirstRunRequired: firstRunRequired,
            HasUsers: hasUsers,
            HasStoreSettings: hasStoreSettings);
    }

    private void InsertAdminUser(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AdminBootstrapRequest request,
        Guid adminUserId,
        string now)
    {
        string passwordHash = _passwordHasher.HashPassword(request.AdminPassword);

        Execute(connection, transaction, """
            INSERT INTO users
                (id, username, display_name, password_hash, role_id, status, created_at, updated_at)
            VALUES
                ($id, $username, $displayName, $passwordHash, $roleId, 1, $createdAt, NULL);
            """,
            new Dictionary<string, object?>
            {
                ["$id"] = adminUserId.ToString("D"),
                ["$username"] = request.AdminUsername.Trim(),
                ["$displayName"] = request.AdminDisplayName.Trim(),
                ["$passwordHash"] = passwordHash,
                ["$roleId"] = DefaultSeedData.AdminRoleId,
                ["$createdAt"] = now
            });
    }

    private static void InsertStoreSettings(
        SqliteConnection connection,
        SqliteTransaction transaction,
        AdminBootstrapRequest request,
        Guid storeSettingsId,
        string now)
    {
        Execute(connection, transaction, """
            INSERT INTO store_settings
                (id, store_name, business_name, address, contact_number, tin, currency_label,
                 receipt_width_mm, receipt_header, receipt_footer, tax_mode_enabled,
                 shift_mode_enabled, allow_negative_stock, created_at, updated_at)
            VALUES
                ($id, $storeName, $businessName, $address, $contactNumber, $tin, $currencyLabel,
                 $receiptWidthMm, NULL, NULL, $taxModeEnabled,
                 $shiftModeEnabled, $allowNegativeStock, $createdAt, NULL);
            """,
            new Dictionary<string, object?>
            {
                ["$id"] = storeSettingsId.ToString("D"),
                ["$storeName"] = request.StoreName.Trim(),
                ["$businessName"] = NullIfWhiteSpace(request.BusinessName),
                ["$address"] = NullIfWhiteSpace(request.Address),
                ["$contactNumber"] = NullIfWhiteSpace(request.ContactNumber),
                ["$tin"] = NullIfWhiteSpace(request.Tin),
                ["$currencyLabel"] = DefaultSeedData.StoreDefaults.CurrencyLabel,
                ["$receiptWidthMm"] = request.ReceiptWidthMm,
                ["$taxModeEnabled"] = DefaultSeedData.StoreDefaults.TaxModeEnabled ? 1 : 0,
                ["$shiftModeEnabled"] = DefaultSeedData.StoreDefaults.ShiftModeEnabled ? 1 : 0,
                ["$allowNegativeStock"] = DefaultSeedData.StoreDefaults.AllowNegativeStock ? 1 : 0,
                ["$createdAt"] = now
            });
    }

    private static void MarkFirstRunComplete(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string now)
    {
        Execute(connection, transaction, """
            INSERT INTO app_metadata (key, value, updated_at)
            VALUES ('first_run_required', 'false', $updatedAt)
            ON CONFLICT(key) DO UPDATE SET
                value = excluded.value,
                updated_at = excluded.updated_at;
            """,
            new Dictionary<string, object?>
            {
                ["$updatedAt"] = now
            });
    }

    private static void InsertAuditLog(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid adminUserId,
        Guid storeSettingsId,
        string now)
    {
        Execute(connection, transaction, """
            INSERT INTO audit_logs
                (id, user_id, action, entity_type, entity_id, old_value_json, new_value_json, reason, created_at)
            VALUES
                ($id, $userId, 'FIRST_RUN_ADMIN_CREATED', 'store_settings', $entityId, NULL, NULL,
                 'Initial first-run bootstrap completed.', $createdAt);
            """,
            new Dictionary<string, object?>
            {
                ["$id"] = Guid.NewGuid().ToString("D"),
                ["$userId"] = adminUserId.ToString("D"),
                ["$entityId"] = storeSettingsId.ToString("D"),
                ["$createdAt"] = now
            });
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1
            FROM sqlite_master
            WHERE type = 'table' AND name = $tableName
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$tableName", tableName);

        return command.ExecuteScalar() is not null;
    }

    private static bool RowExists(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return command.ExecuteScalar() is not null;
    }

    private static string? ScalarString(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        object? value = command.ExecuteScalar();

        return value?.ToString();
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
            command.Parameters.AddWithValue(parameter.Key, parameter.Value ?? DBNull.Value);
        }

        command.ExecuteNonQuery();
    }

    private static string? NullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
