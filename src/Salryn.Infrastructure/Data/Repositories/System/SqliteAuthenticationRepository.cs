using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Repositories;
using Salryn.Core.Security;
using Salryn.Infrastructure.Data.Seeding;

namespace Salryn.Infrastructure.Data.Repositories.System;

public sealed class SqliteAuthenticationRepository :
    SqliteRepositoryBase,
    IAuthenticationRepository
{
    public SqliteAuthenticationRepository(SqliteConnectionFactory connectionFactory)
        : base(connectionFactory)
    {
    }

    // Kirch: Checks whether the local database has at least one active user for login routing.
    public Task<Result<bool>> HasAnyActiveUserAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var connection = CreateOpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText = """
                SELECT COUNT(1)
                FROM users
                WHERE status = $activeStatus;
                """;

            command.Parameters.AddWithValue("$activeStatus", (int)UserStatus.Active);

            long count = (long)command.ExecuteScalar()!;

            return Task.FromResult(Result<bool>.Success(count > 0));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException)
        {
            return Task.FromResult(Unexpected<bool>(
                "Auth.HasAnyActiveUserFailed",
                ex));
        }
    }

    // Kirch: Reads login credential data by username without verifying the password in infrastructure.
    public Task<Result<LoginUserCredentialReadModel?>> FindCredentialByUsernameAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return Task.FromResult(Result<LoginUserCredentialReadModel?>.Fail(Error.Validation(
                "Auth.UsernameRequired",
                "Username is required.")));
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var connection = CreateOpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText = """
                SELECT
                    u.id,
                    u.username,
                    u.display_name,
                    r.name AS role_name,
                    u.status,
                    u.password_hash
                FROM users u
                INNER JOIN roles r ON r.id = u.role_id
                WHERE lower(u.username) = lower($username)
                LIMIT 1;
                """;

            command.Parameters.AddWithValue("$username", username.Trim());

            using SqliteDataReader reader = command.ExecuteReader();

            if (!reader.Read())
            {
                return Task.FromResult(Result<LoginUserCredentialReadModel?>.Success(null));
            }

            LoginUserCredentialReadModel credential = new(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                (UserStatus)reader.GetInt32(4),
                reader.GetString(5));

            return Task.FromResult(Result<LoginUserCredentialReadModel?>.Success(credential));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException)
        {
            return Task.FromResult(Unexpected<LoginUserCredentialReadModel?>(
                "Auth.FindCredentialFailed",
                ex));
        }
    }

    // Kirch: Creates the first active Admin account and flips first-run metadata off in one transaction.
    public Task<Result<AuthenticatedUserReadModel>> CreateFirstAdminAsync(
        FirstRunAdminSetupRequest request,
        string passwordHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            return Task.FromResult(Result<AuthenticatedUserReadModel>.Fail(Error.Validation(
                "Auth.PasswordHashRequired",
                "Password hash is required.")));
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var connection = CreateOpenConnection();
            using var transaction = connection.BeginTransaction();

            if (HasAnyActiveUser(connection, transaction))
            {
                transaction.Rollback();

                return Task.FromResult(Result<AuthenticatedUserReadModel>.Fail(Error.Conflict(
                    "Auth.FirstAdminAlreadyExists",
                    "First-run admin setup is no longer available because an active user already exists.")));
            }

            Guid userId = Guid.NewGuid();
            string now = DateTimeOffset.UtcNow.ToString("O");
            Guid storeSettingsId = EnsureDefaultStoreSettings(connection, transaction, now);

            using (var insertUser = connection.CreateCommand())
            {
                insertUser.Transaction = transaction;
                insertUser.CommandText = """
                    INSERT INTO users
                        (id, username, display_name, password_hash, role_id, status, created_at, updated_at)
                    VALUES
                        ($id, $username, $displayName, $passwordHash, $roleId, $status, $createdAt, NULL);
                    """;

                insertUser.Parameters.AddWithValue("$id", userId.ToString("D"));
                insertUser.Parameters.AddWithValue("$username", request.Username.Trim());
                insertUser.Parameters.AddWithValue("$displayName", request.DisplayName.Trim());
                insertUser.Parameters.AddWithValue("$passwordHash", passwordHash);
                insertUser.Parameters.AddWithValue("$roleId", DefaultSeedData.AdminRoleId);
                insertUser.Parameters.AddWithValue("$status", (int)UserStatus.Active);
                insertUser.Parameters.AddWithValue("$createdAt", now);

                insertUser.ExecuteNonQuery();
            }

            using (var updateMetadata = connection.CreateCommand())
            {
                updateMetadata.Transaction = transaction;
                updateMetadata.CommandText = """
                    INSERT INTO app_metadata (key, value, updated_at)
                    VALUES ('first_run_required', 'false', $updatedAt)
                    ON CONFLICT(key) DO UPDATE SET
                        value = excluded.value,
                        updated_at = excluded.updated_at;
                    """;

                updateMetadata.Parameters.AddWithValue("$updatedAt", now);
                updateMetadata.ExecuteNonQuery();
            }

            InsertFirstRunAuditLog(
                connection,
                transaction,
                userId,
                storeSettingsId,
                now);

            IReadOnlyList<string> permissions = ReadRolePermissions(
                connection,
                transaction,
                DefaultSeedData.AdminRoleId);

            transaction.Commit();

            AuthenticatedUserReadModel admin = new(
                userId,
                request.Username.Trim(),
                request.DisplayName.Trim(),
                "Admin",
                UserStatus.Active,
                permissions)
            {
                RoleId = Guid.Parse(DefaultSeedData.AdminRoleId)
            };

            return Task.FromResult(Result<AuthenticatedUserReadModel>.Success(admin));
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException)
        {
            return Task.FromResult(Unexpected<AuthenticatedUserReadModel>(
                "Auth.CreateFirstAdminFailed",
                ex));
        }
    }

    private static Guid EnsureDefaultStoreSettings(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string now)
    {
        Guid? existingStoreSettingsId = TryReadFirstStoreSettingsId(connection, transaction);

        if (existingStoreSettingsId.HasValue)
        {
            return existingStoreSettingsId.Value;
        }

        Guid storeSettingsId = Guid.NewGuid();

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO store_settings
                (id, store_name, business_name, address, contact_number, tin, currency_label,
                 receipt_width_mm, receipt_header, receipt_footer, tax_mode_enabled,
                 shift_mode_enabled, allow_negative_stock, created_at, updated_at)
            VALUES
                ($id, $storeName, $businessName, NULL, NULL, NULL, $currencyLabel,
                 $receiptWidthMm, NULL, NULL, $taxModeEnabled,
                 $shiftModeEnabled, $allowNegativeStock, $createdAt, NULL);
            """;

        command.Parameters.AddWithValue("$id", storeSettingsId.ToString("D"));
        command.Parameters.AddWithValue("$storeName", "SALRYN STORE");
        command.Parameters.AddWithValue("$businessName", "Local POS");
        command.Parameters.AddWithValue("$currencyLabel", DefaultSeedData.StoreDefaults.CurrencyLabel);
        command.Parameters.AddWithValue("$receiptWidthMm", DefaultSeedData.StoreDefaults.ReceiptWidthMm);
        command.Parameters.AddWithValue("$taxModeEnabled", DefaultSeedData.StoreDefaults.TaxModeEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$shiftModeEnabled", DefaultSeedData.StoreDefaults.ShiftModeEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$allowNegativeStock", DefaultSeedData.StoreDefaults.AllowNegativeStock ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", now);

        command.ExecuteNonQuery();

        return storeSettingsId;
    }

    private static Guid? TryReadFirstStoreSettingsId(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id
            FROM store_settings
            ORDER BY created_at
            LIMIT 1;
            """;

        object? value = command.ExecuteScalar();

        if (value is null || value == DBNull.Value)
        {
            return null;
        }

        string? rawValue = value.ToString();

        if (Guid.TryParse(rawValue, out Guid parsed))
        {
            return parsed;
        }

        throw new FormatException("Existing store_settings id is not a valid GUID.");
    }

    private static void InsertFirstRunAuditLog(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid adminUserId,
        Guid storeSettingsId,
        string now)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO audit_logs
                (id, user_id, action, entity_type, entity_id, old_value_json, new_value_json, reason, created_at)
            VALUES
                ($id, $userId, 'FIRST_RUN_ADMIN_CREATED', 'store_settings', $entityId, NULL, NULL,
                 'Initial first-run authentication setup completed.', $createdAt);
            """;

        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue("$userId", adminUserId.ToString("D"));
        command.Parameters.AddWithValue("$entityId", storeSettingsId.ToString("D"));
        command.Parameters.AddWithValue("$createdAt", now);

        command.ExecuteNonQuery();
    }
    private static bool HasAnyActiveUser(
        SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COUNT(1)
            FROM users
            WHERE status = $activeStatus;
            """;

        command.Parameters.AddWithValue("$activeStatus", (int)UserStatus.Active);

        long count = (long)command.ExecuteScalar()!;

        return count > 0;
    }

    private static IReadOnlyList<string> ReadRolePermissions(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string roleId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT permission
            FROM role_permissions
            WHERE role_id = $roleId
            ORDER BY permission;
            """;

        command.Parameters.AddWithValue("$roleId", roleId);

        using SqliteDataReader reader = command.ExecuteReader();

        List<string> permissions = new();

        while (reader.Read())
        {
            string permission = reader.GetString(0).Trim();

            if (!string.IsNullOrWhiteSpace(permission))
            {
                permissions.Add(permission);
            }
        }

        return permissions
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item)
            .ToArray();
    }
}
