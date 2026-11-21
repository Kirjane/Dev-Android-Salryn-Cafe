using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data;
using Salryn.Infrastructure.Data.Repositories.System;

namespace Salryn.Sync.Central.Server;

public sealed record OperatorLoginRequest(string Username, string Password);
public sealed record OperatorLoginResponse(string OperatorToken, Guid UserId, DateTimeOffset ExpiresAtUtc, long ServerEpoch)
{
    public Guid RoleId { get; init; }
    public IReadOnlyList<string> Permissions { get; init; } = [];
}

public sealed partial class CentralSyncService
{
    public async Task<ServiceOutcome<OperatorLoginResponse>> LoginOperatorAsync(AuthenticatedDeviceContext device,
        OperatorLoginRequest request, CancellationToken cancellationToken = default)
    {
        if (!_options.EnableCanonicalDomainProcessing || device.TenantId != _options.CanonicalTenantId ||
            device.StoreId != _options.CanonicalStoreId || device.Status != "Active")
            return ServiceOutcome<OperatorLoginResponse>.Fail(403,"ERR_CANONICAL_AUTHORITY_SCOPE","Canonical operator authority is unavailable.");
        if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length > 128 ||
            string.IsNullOrEmpty(request.Password) || request.Password.Length > 1024)
            return ServiceOutcome<OperatorLoginResponse>.Fail(401,"ERR_OPERATOR_CREDENTIALS","Invalid operator credentials.");
        var now = _time.GetUtcNow();
        string usernameHash = HashOperatorMaterial(request.Username.Trim().ToUpperInvariant());
        using (var check = _db.OpenConnection())
        using (var limit = check.CreateCommand())
        {
            limit.CommandText = "SELECT lockout_until_utc FROM sync_operator_login_limits WHERE device_id=$device AND username_hash=$username;";
            limit.Parameters.AddWithValue("$device", device.DeviceId.ToString("D"));
            limit.Parameters.AddWithValue("$username", usernameHash);
            if (limit.ExecuteScalar() is string expiry && DateTimeOffset.Parse(expiry) > now)
                return ServiceOutcome<OperatorLoginResponse>.Fail(429,"ERR_OPERATOR_LOGIN_LOCKED","Operator login temporarily locked.");
        }
        var factory = new SqliteConnectionFactory(_db.DatabasePath);
        var authentication = new AuthenticationService(new SqliteAuthenticationRepository(factory), new SqliteUserPermissionReadRepository(factory));
        var authenticated = await authentication.LoginAsync(new(request.Username, request.Password), cancellationToken).ConfigureAwait(false);
        using var connection = _db.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred:false);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$device",device.DeviceId.ToString("D"));
        command.Parameters.AddWithValue("$username",usernameHash);
        if (authenticated.IsFailure)
        {
            command.CommandText = """
                INSERT INTO sync_operator_login_limits VALUES($device,$username,1,NULL)
                ON CONFLICT(device_id,username_hash) DO UPDATE SET
                    failed_attempts=CASE WHEN lockout_until_utc IS NOT NULL AND lockout_until_utc <= $now THEN 1 ELSE failed_attempts+1 END,
                    lockout_until_utc=CASE WHEN lockout_until_utc IS NOT NULL AND lockout_until_utc <= $now THEN NULL ELSE lockout_until_utc END;
                UPDATE sync_operator_login_limits SET lockout_until_utc=$lockout
                WHERE device_id=$device AND username_hash=$username AND failed_attempts >= 5;
                """;
            command.Parameters.AddWithValue("$now",now.ToString("O"));
            command.Parameters.AddWithValue("$lockout",now.AddMinutes(5).ToString("O"));
            command.ExecuteNonQuery();
            transaction.Commit();
            return ServiceOutcome<OperatorLoginResponse>.Fail(401,"ERR_OPERATOR_CREDENTIALS","Invalid operator credentials.");
        }
        Guid user = authenticated.Value.UserId;
        string? authority = ReadOperatorAuthority(connection, transaction, user);
        if (authority is null)
            return ServiceOutcome<OperatorLoginResponse>.Fail(403,"ERR_OPERATOR_AUTHORITY","Operator is no longer active.");
        string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        long epoch = _db.ReadServerState(connection,transaction).ServerEpoch;
        DateTimeOffset expires = now.AddMinutes(30);
        command.CommandText = "DELETE FROM sync_operator_login_limits WHERE device_id=$device AND username_hash=$username;";
        command.ExecuteNonQuery();
        command.CommandText = """
            INSERT INTO sync_operator_sessions(token_hash,tenant_id,store_id,device_id,user_id,server_epoch,authority_hash,expires_at_utc)
            VALUES($token,$tenant,$store,$device,$user,$epoch,$authority,$expires);
            """;
        command.Parameters.AddWithValue("$token",HashOperatorMaterial(token));
        command.Parameters.AddWithValue("$tenant",device.TenantId.ToString("D"));
        command.Parameters.AddWithValue("$store",device.StoreId.ToString("D"));
        command.Parameters.AddWithValue("$user",user.ToString("D"));
        command.Parameters.AddWithValue("$epoch",epoch);
        command.Parameters.AddWithValue("$authority",authority);
        command.Parameters.AddWithValue("$expires",expires.ToString("O"));
        command.ExecuteNonQuery();
        command.Parameters.Clear();
        command.CommandText = "SELECT u.role_id,p.permission FROM users u LEFT JOIN role_permissions p ON p.role_id=u.role_id WHERE u.id=$user AND u.status=1 ORDER BY p.permission;";
        command.Parameters.AddWithValue("$user",user.ToString("D"));
        Guid roleId=Guid.Empty; List<string> permissions=[];
        using(var reader=command.ExecuteReader())
            while(reader.Read()) { roleId=Guid.Parse(reader.GetString(0)); if(!reader.IsDBNull(1)) permissions.Add(reader.GetString(1)); }
        transaction.Commit();
        return ServiceOutcome<OperatorLoginResponse>.Ok(new(token,user,expires,epoch) {RoleId=roleId,Permissions=permissions});
    }

    public ServiceOutcome<bool> LogoutOperator(AuthenticatedDeviceContext device,string? token)
    {
        if (!_options.EnableCanonicalDomainProcessing || string.IsNullOrWhiteSpace(token) || token.Length > 512)
            return ServiceOutcome<bool>.Fail(401,"ERR_OPERATOR_TOKEN","Operator token required.");
        using var connection = _db.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE sync_operator_sessions SET revoked_at_utc=$now WHERE token_hash=$token AND device_id=$device AND tenant_id=$tenant AND store_id=$store;";
        command.Parameters.AddWithValue("$now",_time.GetUtcNow().ToString("O"));
        command.Parameters.AddWithValue("$token",HashOperatorMaterial(token));
        command.Parameters.AddWithValue("$device",device.DeviceId.ToString("D"));
        command.Parameters.AddWithValue("$tenant",device.TenantId.ToString("D"));
        command.Parameters.AddWithValue("$store",device.StoreId.ToString("D"));
        command.ExecuteNonQuery();
        return ServiceOutcome<bool>.Ok(true);
    }

    private string? ValidateOperatorToken(SqliteConnection connection, SqliteTransaction transaction,
        AuthenticatedDeviceContext device,string? token,long epoch,IEnumerable<Guid> actors,DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 512) return "ERR_OPERATOR_TOKEN_REQUIRED";
        if (device.TenantId != _options.CanonicalTenantId || device.StoreId != _options.CanonicalStoreId)
            return "ERR_CANONICAL_AUTHORITY_SCOPE";
        if (device.DeviceRole is not ("OfflineAndroidCashier" or "LanThinTerminal" or "OwnerAdminClient"))
            return "ERR_DEVICE_MUTATION_ROLE";
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT user_id,authority_hash,expires_at_utc FROM sync_operator_sessions
            WHERE token_hash=$token AND tenant_id=$tenant AND store_id=$store AND device_id=$device
                AND server_epoch=$epoch AND revoked_at_utc IS NULL;
            """;
        command.Parameters.AddWithValue("$token",HashOperatorMaterial(token));
        command.Parameters.AddWithValue("$tenant",device.TenantId.ToString("D"));
        command.Parameters.AddWithValue("$store",device.StoreId.ToString("D"));
        command.Parameters.AddWithValue("$device",device.DeviceId.ToString("D"));
        command.Parameters.AddWithValue("$epoch",epoch);
        Guid user;
        string authority;
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read() || DateTimeOffset.Parse(reader.GetString(2)) <= now) return "ERR_OPERATOR_TOKEN_EXPIRED_OR_REVOKED";
            user=Guid.Parse(reader.GetString(0)); authority=reader.GetString(1);
        }
        if (actors.Any(x=>x!=user)) return "ERR_OPERATOR_ACTOR_MISMATCH";
        string? current = ReadOperatorAuthority(connection,transaction,user);
        if (current is null || !CentralCrypto.FixedTimeEqualsAscii(authority,current)) return "ERR_OPERATOR_AUTHORITY_CHANGED";
        return null;
    }

    private static string? ReadOperatorAuthority(SqliteConnection connection,SqliteTransaction transaction,Guid user)
    {
        using var command=connection.CreateCommand();
        command.Transaction=transaction;
        command.CommandText="""
            SELECT u.username,u.role_id,r.name,p.permission,u.password_hash FROM users u JOIN roles r ON r.id=u.role_id
            LEFT JOIN role_permissions p ON p.role_id=u.role_id WHERE u.id=$user AND u.status=1 ORDER BY p.permission;
            """;
        command.Parameters.AddWithValue("$user",user.ToString("D"));
        using var reader=command.ExecuteReader();
        List<string> fields=[];
        while(reader.Read())
            fields.Add(System.Text.Json.JsonSerializer.Serialize(new[]{reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.IsDBNull(3)?null:reader.GetString(3),reader.GetString(4)}));
        return fields.Count==0?null:HashOperatorMaterial(string.Join("\n",fields));
    }
    private static string HashOperatorMaterial(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
