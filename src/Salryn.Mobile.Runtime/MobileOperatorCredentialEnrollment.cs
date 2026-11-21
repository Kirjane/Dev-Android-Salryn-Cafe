using System.Text.Json;
using Microsoft.Data.Sqlite;
using Salryn.Core.Common;
using Salryn.Core.Security;
using Salryn.Infrastructure.Data;

namespace Salryn.Mobile.Runtime;

public sealed record MobileApprovedCredentialScope(Guid DeviceId, Guid TenantId, Guid StoreId,
    long ServerEpoch, bool IsAuthorized);

public sealed record MobileVerifiedOperatorLogin(Guid UserId, Guid DeviceId, Guid TenantId, Guid StoreId,
    long ServerEpoch, DateTimeOffset ExpiresAtUtc, bool IsAuthenticated)
{
    public Guid RoleId { get; init; }
    public IReadOnlyList<string>? Permissions { get; init; }
}

/// <summary>
/// Persistence boundary, not an online authentication verifier. The trusted connection caller must
/// obtain approved scope from its verified signed lease/current revocation guard, and verified login
/// from a successful Central operator login over authenticated HTTPS. Never construct either from
/// form fields or use this API after failed login. No server password hash or token is persisted.
/// </summary>
public sealed class MobileOperatorCredentialEnrollment(SqliteConnectionFactory factory)
{
    private const string BootstrapPassword = "!SYNC-BOOTSTRAP-NONLOGIN!";

    public Result EnrollAfterVerifiedLogin(MobileApprovedCredentialScope approved,
        MobileVerifiedOperatorLogin verified, string username, string password, DateTimeOffset nowUtc)
    {
        if (approved is null || verified is null || !approved.IsAuthorized || !verified.IsAuthenticated ||
            approved.DeviceId == Guid.Empty || approved.TenantId == Guid.Empty || approved.StoreId == Guid.Empty ||
            approved.ServerEpoch < 1 || verified.UserId == Guid.Empty || verified.ExpiresAtUtc <= nowUtc ||
            verified.RoleId == Guid.Empty || verified.Permissions is null ||
            verified.Permissions.Any(permission => string.IsNullOrWhiteSpace(permission) || permission.Length > 128) ||
            verified.Permissions.Distinct(StringComparer.Ordinal).Count() != verified.Permissions.Count ||
            approved.DeviceId != verified.DeviceId || approved.TenantId != verified.TenantId ||
            approved.StoreId != verified.StoreId || approved.ServerEpoch != verified.ServerEpoch)
            return Denied("Mobile.CredentialAuthorityMismatch", "Current approved device scope and successful Central operator authentication are required.");
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return Denied("Mobile.CredentialRequired", "The successfully authenticated operator credentials are required.");

        using SqliteConnection connection = factory.CreateConnection();
        connection.Open();
        using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        using SqliteCommand query = connection.CreateCommand();
        query.Transaction = transaction;
        query.CommandText = "SELECT COUNT(*) FROM deployment_identity WHERE store_id=$store;";
        query.Parameters.AddWithValue("$store", approved.StoreId.ToString("D"));
        if (Convert.ToInt64(query.ExecuteScalar()) != 1)
            return Denied("Mobile.CredentialStoreMismatch", "Bootstrap this approved store before enrolling operator credentials.");

        query.Parameters.Clear();
        query.CommandText = """
            SELECT u.password_hash FROM users u
            WHERE u.id=$user AND u.username=$username COLLATE NOCASE AND u.status=1 AND EXISTS(SELECT 1 FROM roles r WHERE r.id=$role);
            """;
        query.Parameters.AddWithValue("$user", verified.UserId.ToString("D"));
        query.Parameters.AddWithValue("$username", username.Trim());
        query.Parameters.AddWithValue("$role", verified.RoleId.ToString("D"));
        string? priorHash = query.ExecuteScalar() as string;
        if (priorHash is null)
            return Denied("Mobile.CredentialOperatorUnavailable", "The authenticated operator must match an active synchronized user with current local permissions.");

        string metadataKey = "salryn.cafe.operator.credential.scope." + verified.UserId.ToString("D");
        query.Parameters.Clear();
        query.CommandText = "SELECT value FROM app_metadata WHERE key=$key;";
        query.Parameters.AddWithValue("$key", metadataKey);
        string? priorJson = query.ExecuteScalar() as string;
        MobileApprovedCredentialScope? prior = null;
        if (priorJson is not null)
        {
            try { prior = JsonSerializer.Deserialize<MobileApprovedCredentialScope>(priorJson); }
            catch (JsonException) { return Denied("Mobile.CredentialScopeCorrupt", "Existing enrollment scope requires administrator reconciliation."); }
            if (prior is null || prior.DeviceId != approved.DeviceId || prior.TenantId != approved.TenantId ||
                prior.StoreId != approved.StoreId || prior.ServerEpoch > approved.ServerEpoch)
                return Denied("Mobile.CredentialScopeConflict", "Existing credential scope cannot be replaced or rolled back.");
        }
        if (prior is null && priorHash != BootstrapPassword)
            return Denied("Mobile.CredentialAlreadyLocal", "Existing local credentials are preserved; only synchronized non-login placeholders may be enrolled.");

        // Offline authority comes from the successful current Central login, not seeded role defaults.
        query.Parameters.Clear();
        query.CommandText = "DELETE FROM role_permissions WHERE role_id=$role;";
        query.Parameters.AddWithValue("$role", verified.RoleId.ToString("D"));
        query.ExecuteNonQuery();
        foreach (string permission in verified.Permissions)
        {
            query.Parameters.Clear(); query.CommandText="INSERT INTO role_permissions(id,role_id,permission,created_at) VALUES($id,$role,$permission,$at);";
            query.Parameters.AddWithValue("$role", verified.RoleId.ToString("D"));
            query.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
            query.Parameters.AddWithValue("$permission", permission);
            query.Parameters.AddWithValue("$at", nowUtc.ToUniversalTime().ToString("O")); query.ExecuteNonQuery();
        }

        Result<string> hash = SalrynPasswordHasher.HashPassword(password);
        if (hash.IsFailure) return Result.Fail(hash.ErrorDetail);
        query.Parameters.Clear();
        query.CommandText = "UPDATE users SET password_hash=$hash,role_id=$role,updated_at=$now WHERE id=$user AND status=1;";
        query.Parameters.AddWithValue("$hash", hash.Value);
        query.Parameters.AddWithValue("$role", verified.RoleId.ToString("D"));
        query.Parameters.AddWithValue("$now", nowUtc.ToUniversalTime().ToString("O"));
        query.Parameters.AddWithValue("$user", verified.UserId.ToString("D"));
        if (query.ExecuteNonQuery() != 1)
            return Denied("Mobile.CredentialOperatorUnavailable", "Operator authority changed; retain existing credentials.");
        query.Parameters.Clear();
        query.CommandText = """
            INSERT INTO app_metadata(key,value,updated_at) VALUES($key,$scope,$now)
            ON CONFLICT(key) DO UPDATE SET value=excluded.value,updated_at=excluded.updated_at;
            """;
        query.Parameters.AddWithValue("$key", metadataKey);
        query.Parameters.AddWithValue("$scope", JsonSerializer.Serialize(approved));
        query.Parameters.AddWithValue("$now", nowUtc.ToUniversalTime().ToString("O"));
        query.ExecuteNonQuery();
        query.Parameters.Clear();
        query.CommandText="INSERT INTO app_metadata(key,value,updated_at) VALUES($key,$expiry,$now) ON CONFLICT(key) DO UPDATE SET value=excluded.value,updated_at=excluded.updated_at;";
        query.Parameters.AddWithValue("$key", "salryn.cafe.operator.authority.expiry."+verified.UserId.ToString("D"));
        query.Parameters.AddWithValue("$expiry", verified.ExpiresAtUtc.ToUniversalTime().ToString("O"));
        query.Parameters.AddWithValue("$now", nowUtc.ToUniversalTime().ToString("O")); query.ExecuteNonQuery();
        transaction.Commit();
        return Result.Success();
    }

    public Result CheckOfflineAuthority(Guid userId, Guid deviceId, Guid tenantId, Guid storeId, long serverEpoch, DateTimeOffset now)
    {
        using var c=factory.CreateConnection();c.Open();using var q=c.CreateCommand();
        q.CommandText="SELECT value FROM app_metadata WHERE key=$key;";
        q.Parameters.AddWithValue("$key","salryn.cafe.operator.credential.scope."+userId.ToString("D"));
        if(q.ExecuteScalar() is not string json) return Result.Success(); // Existing local operators retain their established authority path.
        MobileApprovedCredentialScope? scope;
        try {scope=JsonSerializer.Deserialize<MobileApprovedCredentialScope>(json);} catch(JsonException){scope=null;}
        if(scope is null || scope.DeviceId!=deviceId || scope.TenantId!=tenantId || scope.StoreId!=storeId || scope.ServerEpoch!=serverEpoch)
            return Denied("Mobile.OperatorScopeExpired","Reconnect this operator to the current approved device scope.");
        q.Parameters.Clear();q.CommandText="SELECT value FROM app_metadata WHERE key=$key;";
        q.Parameters.AddWithValue("$key","salryn.cafe.operator.authority.expiry."+userId.ToString("D"));
        if(q.ExecuteScalar() is not string expiry || !DateTimeOffset.TryParse(expiry,out var deadline) || now>=deadline)
            return Denied("Mobile.OperatorAuthorityExpired","Reconnect and authenticate this operator before making offline changes.");
        return Result.Success();
    }

    private static Result Denied(string code, string message) => Result.Fail(Error.Forbidden(code, message));
}
