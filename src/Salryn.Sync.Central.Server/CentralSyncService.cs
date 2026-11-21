using System.Text.Json;
using Microsoft.Data.Sqlite;
using Salryn.Sync.Contracts;

namespace Salryn.Sync.Central.Server;

public sealed record ServiceOutcome<T>(
    int StatusCode,
    T? Value,
    ErrorResponseDto? Error = null,
    RevokedFencedResponseDto? Fenced = null)
{
    public static ServiceOutcome<T> Ok(T value, int statusCode = 200) => new(statusCode, value);
    public static ServiceOutcome<T> Fail(int statusCode, string code, string message, long? serverEpoch = null) =>
        new(statusCode, default, new ErrorResponseDto(code, message, serverEpoch));
    public static ServiceOutcome<T> FencedRevoked(RevokedFencedResponseDto fenced) =>
        new(403, default, null, fenced);
}

public sealed record AuthenticatedDeviceContext(
    Guid DeviceId,
    Guid TenantId,
    Guid StoreId,
    string ProductId,
    string DeviceRole,
    string Status,
    string PublicKeySpki);

public sealed partial class CentralSyncService
{
    private static readonly HashSet<string> AllowedDeviceRoles = new(StringComparer.Ordinal)
    {
        "OfflineAndroidCashier",
        "LanThinTerminal",
        "OwnerAdminClient",
        "StandbyBackupNode"
    };

    private static readonly HashSet<string> AllowedQuarantineReviewStatuses = new(StringComparer.Ordinal)
    {
        "AdminReviewRequired",
        "ApprovedForManualAudit",
        "Discarded"
    };

    private readonly CentralSyncDatabase _db;
    private readonly CentralServerOptions _options;
    private readonly TimeProvider _time;

    public CentralSyncService(CentralSyncDatabase db, CentralServerOptions options, TimeProvider? time = null)
    {
        _db = db;
        _options = options;
        _time = time ?? TimeProvider.System;
    }

    public HealthResponseDto GetHealth()
    {
        using SqliteConnection conn = _db.OpenConnection();
        CentralServerStateSnapshot state = _db.ReadServerState(conn);
        return new HealthResponseDto(
            "Healthy",
            CentralServerOptions.CanonicalProductId,
            state.CentralInstanceId,
            state.ServerEpoch,
            state.HeadSequence,
            CentralServerOptions.CentralSchemaVersion,
            _time.GetUtcNow());
    }

    public void SeedAdminUser(
        Guid tenantId,
        Guid storeId,
        string username,
        string displayName,
        string role,
        string password)
    {
        using SqliteConnection conn = _db.OpenConnection();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO central_admin_users (
                admin_id, tenant_id, store_id, product_id, username, display_name, role,
                password_hash, failed_attempts, is_active, created_at_utc)
            VALUES (
                $adminId, $tenantId, $storeId, $productId, $username, $displayName, $role,
                $passwordHash, 0, 1, $now);
            """;
        cmd.Parameters.AddWithValue("$adminId", Guid.CreateVersion7().ToString("D"));
        cmd.Parameters.AddWithValue("$tenantId", tenantId.ToString("D"));
        cmd.Parameters.AddWithValue("$storeId", storeId.ToString("D"));
        cmd.Parameters.AddWithValue("$productId", CentralServerOptions.CanonicalProductId);
        cmd.Parameters.AddWithValue("$username", username.Trim().ToLowerInvariant());
        cmd.Parameters.AddWithValue("$displayName", displayName);
        cmd.Parameters.AddWithValue("$role", role);
        cmd.Parameters.AddWithValue("$passwordHash", CentralCrypto.HashPassword(password));
        cmd.Parameters.AddWithValue("$now", _time.GetUtcNow().ToString("O"));
        cmd.ExecuteNonQuery();
    }

    // ==================== ADMIN AUTH ====================

    public ServiceOutcome<AdminLoginResponse> AdminLogin(AdminLoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password))
        {
            return ServiceOutcome<AdminLoginResponse>.Fail(400, "ERR_INVALID_CREDENTIALS", "Username and password are required.");
        }

        string normalizedUser = request.Username.Trim().ToLowerInvariant();
        DateTimeOffset now = _time.GetUtcNow();

        using SqliteConnection conn = _db.OpenConnection();
        using SqliteTransaction tx = conn.BeginTransaction();

        Guid adminId;
        Guid tenantId;
        Guid storeId;
        string productId;
        string displayName;
        string role;
        string passwordHash;
        int failedAttempts;
        DateTimeOffset? lockedUntil = null;
        bool isActive;

        using (SqliteCommand cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT admin_id, tenant_id, store_id, product_id, display_name, role,
                       password_hash, failed_attempts, locked_until_utc, is_active
                FROM central_admin_users
                WHERE username = $username;
                """;
            cmd.Parameters.AddWithValue("$username", normalizedUser);
            using SqliteDataReader reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return ServiceOutcome<AdminLoginResponse>.Fail(401, "ERR_INVALID_CREDENTIALS", "Invalid username or password.");
            }

            adminId = Guid.Parse(reader.GetString(0));
            tenantId = Guid.Parse(reader.GetString(1));
            storeId = Guid.Parse(reader.GetString(2));
            productId = reader.GetString(3);
            displayName = reader.GetString(4);
            role = reader.GetString(5);
            passwordHash = reader.GetString(6);
            failedAttempts = reader.GetInt32(7);
            if (!reader.IsDBNull(8))
            {
                lockedUntil = DateTimeOffset.Parse(reader.GetString(8));
            }
            isActive = reader.GetInt32(9) == 1;
        }

        if (!isActive || !string.Equals(productId, CentralServerOptions.CanonicalProductId, StringComparison.Ordinal))
        {
            return ServiceOutcome<AdminLoginResponse>.Fail(403, "ERR_ADMIN_DISABLED", "Admin account is inactive or wrong product.");
        }

        if (lockedUntil.HasValue && lockedUntil.Value > now)
        {
            return ServiceOutcome<AdminLoginResponse>.Fail(429, "ERR_ADMIN_LOCKED_OUT", "Too many failed login attempts. Try again later.");
        }

        if (!CentralCrypto.VerifyPassword(request.Password, passwordHash))
        {
            int nextAttempts = failedAttempts + 1;
            DateTimeOffset? nextLock = nextAttempts >= _options.MaxAdminFailedAttempts
                ? now.AddMinutes(_options.AdminLockoutMinutes)
                : null;
            using SqliteCommand failCmd = conn.CreateCommand();
            failCmd.Transaction = tx;
            failCmd.CommandText = """
                UPDATE central_admin_users
                SET failed_attempts = $attempts, locked_until_utc = $lockedUntil
                WHERE admin_id = $adminId;
                """;
            failCmd.Parameters.AddWithValue("$attempts", nextAttempts);
            failCmd.Parameters.AddWithValue("$lockedUntil", nextLock.HasValue ? nextLock.Value.ToString("O") : DBNull.Value);
            failCmd.Parameters.AddWithValue("$adminId", adminId.ToString("D"));
            failCmd.ExecuteNonQuery();
            tx.Commit();

            return ServiceOutcome<AdminLoginResponse>.Fail(401, "ERR_INVALID_CREDENTIALS", "Invalid username or password.");
        }

        using (SqliteCommand resetCmd = conn.CreateCommand())
        {
            resetCmd.Transaction = tx;
            resetCmd.CommandText = "UPDATE central_admin_users SET failed_attempts = 0, locked_until_utc = NULL WHERE admin_id = $adminId;";
            resetCmd.Parameters.AddWithValue("$adminId", adminId.ToString("D"));
            resetCmd.ExecuteNonQuery();
        }

        string rawToken = CentralCrypto.GenerateOpaqueToken(32);
        string tokenHash = CentralCrypto.Sha256Hex(rawToken);
        DateTimeOffset expiresAt = now.AddMinutes(_options.AdminSessionTtlMinutes);

        using (SqliteCommand sessCmd = conn.CreateCommand())
        {
            sessCmd.Transaction = tx;
            sessCmd.CommandText = """
                INSERT INTO central_admin_sessions (
                    token_hash, admin_id, tenant_id, store_id, product_id, role, issued_at_utc, expires_at_utc)
                VALUES ($hash, $adminId, $tenantId, $storeId, $productId, $role, $now, $expires);
                """;
            sessCmd.Parameters.AddWithValue("$hash", tokenHash);
            sessCmd.Parameters.AddWithValue("$adminId", adminId.ToString("D"));
            sessCmd.Parameters.AddWithValue("$tenantId", tenantId.ToString("D"));
            sessCmd.Parameters.AddWithValue("$storeId", storeId.ToString("D"));
            sessCmd.Parameters.AddWithValue("$productId", productId);
            sessCmd.Parameters.AddWithValue("$role", role);
            sessCmd.Parameters.AddWithValue("$now", now.ToString("O"));
            sessCmd.Parameters.AddWithValue("$expires", expiresAt.ToString("O"));
            sessCmd.ExecuteNonQuery();
        }

        tx.Commit();

        AdminSessionDto session = new(adminId, tenantId, storeId, productId, normalizedUser, displayName, role, expiresAt);
        return ServiceOutcome<AdminLoginResponse>.Ok(new AdminLoginResponse(rawToken, session));
    }

    public AdminSessionDto? ValidateAdminBearerToken(string? rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return null;
        }

        string tokenHash = CentralCrypto.Sha256Hex(rawToken);
        DateTimeOffset now = _time.GetUtcNow();

        using SqliteConnection conn = _db.OpenConnection();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT s.admin_id, s.tenant_id, s.store_id, s.product_id, u.username, u.display_name,
                   s.role, s.expires_at_utc, s.revoked_at_utc, u.is_active
            FROM central_admin_sessions s
            INNER JOIN central_admin_users u ON u.admin_id = s.admin_id
            WHERE s.token_hash = $hash;
            """;
        cmd.Parameters.AddWithValue("$hash", tokenHash);
        using SqliteDataReader reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        if (!reader.IsDBNull(8) || reader.GetInt32(9) != 1)
        {
            return null;
        }

        DateTimeOffset expiresAt = DateTimeOffset.Parse(reader.GetString(7));
        if (expiresAt <= now)
        {
            return null;
        }

        string productId = reader.GetString(3);
        string role = reader.GetString(6);
        if (!string.Equals(productId, CentralServerOptions.CanonicalProductId, StringComparison.Ordinal))
        {
            return null;
        }

        if (!string.Equals(role, "Owner", StringComparison.Ordinal) &&
            !string.Equals(role, "Admin", StringComparison.Ordinal))
        {
            return null;
        }

        return new AdminSessionDto(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            Guid.Parse(reader.GetString(2)),
            productId,
            reader.GetString(4),
            reader.GetString(5),
            role,
            expiresAt);
    }

    public void AdminLogout(string rawToken)
    {
        string tokenHash = CentralCrypto.Sha256Hex(rawToken);
        using SqliteConnection conn = _db.OpenConnection();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE central_admin_sessions SET revoked_at_utc = $now WHERE token_hash = $hash;";
        cmd.Parameters.AddWithValue("$now", _time.GetUtcNow().ToString("O"));
        cmd.Parameters.AddWithValue("$hash", tokenHash);
        cmd.ExecuteNonQuery();
    }

    // ==================== INVITES & DEVICE ENROLLMENT ====================

    public ServiceOutcome<CreateInviteResponse> CreateInvite(AdminSessionDto admin, CreateInviteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceRole) || !AllowedDeviceRoles.Contains(request.DeviceRole))
        {
            return ServiceOutcome<CreateInviteResponse>.Fail(400, "ERR_INVALID_DEVICE_ROLE", "Unsupported device role.");
        }

        Guid inviteId = Guid.CreateVersion7();
        string joinCode = CentralCrypto.GenerateJoinCode();
        string joinCodeHash = CentralCrypto.Sha256Hex(joinCode.ToUpperInvariant());
        DateTimeOffset now = _time.GetUtcNow();
        DateTimeOffset expiresAt = now.AddMinutes(_options.JoinCodeTtlMinutes);

        using SqliteConnection conn = _db.OpenConnection();
        using SqliteTransaction tx = conn.BeginTransaction();
        using (SqliteCommand cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO sync_device_invites (
                    invite_id, tenant_id, store_id, product_id, device_role, join_code_hash,
                    expires_at_utc, created_by_admin_id, created_at_utc)
                VALUES ($id, $tenantId, $storeId, $productId, $role, $codeHash, $expires, $adminId, $now);
                """;
            cmd.Parameters.AddWithValue("$id", inviteId.ToString("D"));
            cmd.Parameters.AddWithValue("$tenantId", admin.TenantId.ToString("D"));
            cmd.Parameters.AddWithValue("$storeId", admin.StoreId.ToString("D"));
            cmd.Parameters.AddWithValue("$productId", CentralServerOptions.CanonicalProductId);
            cmd.Parameters.AddWithValue("$role", request.DeviceRole);
            cmd.Parameters.AddWithValue("$codeHash", joinCodeHash);
            cmd.Parameters.AddWithValue("$expires", expiresAt.ToString("O"));
            cmd.Parameters.AddWithValue("$adminId", admin.AdminId.ToString("D"));
            cmd.Parameters.AddWithValue("$now", now.ToString("O"));
            cmd.ExecuteNonQuery();
        }

        WriteAudit(conn, tx, admin.TenantId, admin.StoreId, admin.AdminId.ToString("D"), "CREATE_DEVICE_INVITE", inviteId.ToString("D"), new { request.DeviceRole, expiresAt });
        tx.Commit();

        return ServiceOutcome<CreateInviteResponse>.Ok(new CreateInviteResponse(inviteId, joinCode, request.DeviceRole, expiresAt), 201);
    }

    public ServiceOutcome<EnrollDeviceResponse> EnrollDevice(EnrollDeviceRequest request)
    {
        if (!string.Equals(request.ProductId, CentralServerOptions.CanonicalProductId, StringComparison.Ordinal))
        {
            return ServiceOutcome<EnrollDeviceResponse>.Fail(403, "ERR_WRONG_PRODUCT", "Cross-product enrollment is rejected.");
        }

        if (string.IsNullOrWhiteSpace(request.JoinCode) || string.IsNullOrWhiteSpace(request.DeviceName))
        {
            return ServiceOutcome<EnrollDeviceResponse>.Fail(400, "ERR_INVALID_ENROLLMENT", "JoinCode and DeviceName are required.");
        }

        if (!CentralCrypto.TryValidateEcdsaP256PublicKey(request.PublicKeySpkiBase64, out string thumbprint))
        {
            return ServiceOutcome<EnrollDeviceResponse>.Fail(400, "ERR_INVALID_PUBLIC_KEY", "PublicKeySpkiBase64 must be a valid ECDSA P-256 SubjectPublicKeyInfo.");
        }

        string joinCodeHash = CentralCrypto.Sha256Hex(request.JoinCode.Trim().ToUpperInvariant());
        DateTimeOffset now = _time.GetUtcNow();

        using SqliteConnection conn = _db.OpenConnection();
        using SqliteTransaction tx = conn.BeginTransaction();

        string inviteId;
        Guid inviteTenantId;
        Guid inviteStoreId;
        string inviteProductId;
        string deviceRole;
        DateTimeOffset expiresAt;
        bool alreadyConsumed;

        using (SqliteCommand cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT invite_id, tenant_id, store_id, product_id, device_role, expires_at_utc, consumed_at_utc
                FROM sync_device_invites
                WHERE join_code_hash = $hash;
                """;
            cmd.Parameters.AddWithValue("$hash", joinCodeHash);
            using SqliteDataReader reader = cmd.ExecuteReader();
            if (!reader.Read())
            {
                return ServiceOutcome<EnrollDeviceResponse>.Fail(401, "ERR_INVALID_JOIN_CODE", "Join code is invalid.");
            }

            inviteId = reader.GetString(0);
            inviteTenantId = Guid.Parse(reader.GetString(1));
            inviteStoreId = Guid.Parse(reader.GetString(2));
            inviteProductId = reader.GetString(3);
            deviceRole = reader.GetString(4);
            expiresAt = DateTimeOffset.Parse(reader.GetString(5));
            alreadyConsumed = !reader.IsDBNull(6);
        }

        if (alreadyConsumed)
        {
            return ServiceOutcome<EnrollDeviceResponse>.Fail(409, "ERR_JOIN_CODE_REPLAYED", "Join code has already been used.");
        }

        if (expiresAt <= now)
        {
            return ServiceOutcome<EnrollDeviceResponse>.Fail(401, "ERR_JOIN_CODE_EXPIRED", "Join code has expired.");
        }

        if (!string.Equals(inviteProductId, CentralServerOptions.CanonicalProductId, StringComparison.Ordinal))
        {
            return ServiceOutcome<EnrollDeviceResponse>.Fail(403, "ERR_WRONG_PRODUCT", "Invite product mismatch.");
        }

        if (inviteTenantId != request.TenantId || inviteStoreId != request.StoreId)
        {
            return ServiceOutcome<EnrollDeviceResponse>.Fail(403, "ERR_TENANT_SCOPE_VIOLATION", "Tenant or store does not match join invite.");
        }

        Guid deviceId = Guid.CreateVersion7();

        // Atomic single-use invite consumption
        using (SqliteCommand consume = conn.CreateCommand())
        {
            consume.Transaction = tx;
            consume.CommandText = """
                UPDATE sync_device_invites
                SET consumed_at_utc = $now, consumed_by_device_id = $deviceId
                WHERE invite_id = $inviteId AND consumed_at_utc IS NULL;
                """;
            consume.Parameters.AddWithValue("$now", now.ToString("O"));
            consume.Parameters.AddWithValue("$deviceId", deviceId.ToString("D"));
            consume.Parameters.AddWithValue("$inviteId", inviteId);
            if (consume.ExecuteNonQuery() != 1)
            {
                return ServiceOutcome<EnrollDeviceResponse>.Fail(409, "ERR_JOIN_CODE_REPLAYED", "Join code was concurrently consumed.");
            }
        }

        using (SqliteCommand insertDevice = conn.CreateCommand())
        {
            insertDevice.Transaction = tx;
            insertDevice.CommandText = """
                INSERT INTO sync_device_registry (
                    device_id, tenant_id, store_id, product_id, device_name, device_role,
                    public_key_spki, public_key_thumbprint, status, enrolled_at_utc)
                VALUES (
                    $deviceId, $tenantId, $storeId, $productId, $name, $role,
                    $pubKey, $thumbprint, 'Pending', $now);
                """;
            insertDevice.Parameters.AddWithValue("$deviceId", deviceId.ToString("D"));
            insertDevice.Parameters.AddWithValue("$tenantId", inviteTenantId.ToString("D"));
            insertDevice.Parameters.AddWithValue("$storeId", inviteStoreId.ToString("D"));
            insertDevice.Parameters.AddWithValue("$productId", CentralServerOptions.CanonicalProductId);
            insertDevice.Parameters.AddWithValue("$name", request.DeviceName.Trim());
            insertDevice.Parameters.AddWithValue("$role", deviceRole);
            insertDevice.Parameters.AddWithValue("$pubKey", request.PublicKeySpkiBase64);
            insertDevice.Parameters.AddWithValue("$thumbprint", thumbprint);
            insertDevice.Parameters.AddWithValue("$now", now.ToString("O"));
            insertDevice.ExecuteNonQuery();
        }

        tx.Commit();

        return ServiceOutcome<EnrollDeviceResponse>.Ok(
            new EnrollDeviceResponse(deviceId, "Pending", deviceRole, thumbprint),
            201);
    }

    public ServiceOutcome<DeviceSummaryDto> ApproveDevice(AdminSessionDto admin, Guid deviceId)
    {
        DateTimeOffset now = _time.GetUtcNow();
        using SqliteConnection conn = _db.OpenConnection();
        using SqliteTransaction tx = conn.BeginTransaction();

        using (SqliteCommand cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                UPDATE sync_device_registry
                SET status = 'Active', approved_at_utc = $now, approved_by_admin_id = $adminId
                WHERE device_id = $deviceId AND tenant_id = $tenantId AND store_id = $storeId AND status = 'Pending';
                """;
            cmd.Parameters.AddWithValue("$now", now.ToString("O"));
            cmd.Parameters.AddWithValue("$adminId", admin.AdminId.ToString("D"));
            cmd.Parameters.AddWithValue("$deviceId", deviceId.ToString("D"));
            cmd.Parameters.AddWithValue("$tenantId", admin.TenantId.ToString("D"));
            cmd.Parameters.AddWithValue("$storeId", admin.StoreId.ToString("D"));
            int updated = cmd.ExecuteNonQuery();
            if (updated != 1)
            {
                return ServiceOutcome<DeviceSummaryDto>.Fail(404, "ERR_DEVICE_NOT_PENDING", "Device not found in Pending state for this store.");
            }
        }

        WriteAudit(conn, tx, admin.TenantId, admin.StoreId, admin.AdminId.ToString("D"), "APPROVE_DEVICE", deviceId.ToString("D"), new { deviceId });
        DeviceSummaryDto summary = ReadDeviceSummary(conn, tx, deviceId)!;
        tx.Commit();

        return ServiceOutcome<DeviceSummaryDto>.Ok(summary);
    }

    public ServiceOutcome<DeviceSummaryDto> RevokeDevice(AdminSessionDto admin, Guid deviceId, string? reason)
    {
        DateTimeOffset now = _time.GetUtcNow();
        using SqliteConnection conn = _db.OpenConnection();
        using SqliteTransaction tx = conn.BeginTransaction();

        using (SqliteCommand cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                UPDATE sync_device_registry
                SET status = 'Revoked',
                    revoked_at_utc = $now,
                    revoked_by_admin_id = $adminId,
                    revoke_reason = $reason
                WHERE device_id = $deviceId AND tenant_id = $tenantId AND store_id = $storeId;
                """;
            cmd.Parameters.AddWithValue("$now", now.ToString("O"));
            cmd.Parameters.AddWithValue("$adminId", admin.AdminId.ToString("D"));
            cmd.Parameters.AddWithValue("$reason", string.IsNullOrWhiteSpace(reason) ? "Revoked by admin" : reason.Trim());
            cmd.Parameters.AddWithValue("$deviceId", deviceId.ToString("D"));
            cmd.Parameters.AddWithValue("$tenantId", admin.TenantId.ToString("D"));
            cmd.Parameters.AddWithValue("$storeId", admin.StoreId.ToString("D"));
            if (cmd.ExecuteNonQuery() != 1)
            {
                return ServiceOutcome<DeviceSummaryDto>.Fail(404, "ERR_DEVICE_NOT_FOUND", "Device not found for this store.");
            }
        }

        using (SqliteCommand revokeTokens = conn.CreateCommand())
        {
            revokeTokens.Transaction = tx;
            revokeTokens.CommandText = "UPDATE sync_device_tokens SET revoked_at_utc = $now WHERE device_id = $deviceId AND revoked_at_utc IS NULL;";
            revokeTokens.Parameters.AddWithValue("$now", now.ToString("O"));
            revokeTokens.Parameters.AddWithValue("$deviceId", deviceId.ToString("D"));
            revokeTokens.ExecuteNonQuery();
        }

        WriteAudit(conn, tx, admin.TenantId, admin.StoreId, admin.AdminId.ToString("D"), "REVOKE_DEVICE", deviceId.ToString("D"), new { deviceId, reason });
        DeviceSummaryDto summary = ReadDeviceSummary(conn, tx, deviceId)!;
        tx.Commit();

        return ServiceOutcome<DeviceSummaryDto>.Ok(summary);
    }

    public IReadOnlyList<DeviceSummaryDto> ListDevices(AdminSessionDto admin)
    {
        using SqliteConnection conn = _db.OpenConnection();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT device_id, tenant_id, store_id, product_id, device_name, device_role,
                   public_key_thumbprint, status, enrolled_at_utc, approved_at_utc,
                   revoked_at_utc, revoke_reason, last_seen_at_utc
            FROM sync_device_registry
            WHERE tenant_id = $tenantId AND store_id = $storeId
            ORDER BY enrolled_at_utc DESC;
            """;
        cmd.Parameters.AddWithValue("$tenantId", admin.TenantId.ToString("D"));
        cmd.Parameters.AddWithValue("$storeId", admin.StoreId.ToString("D"));

        List<DeviceSummaryDto> list = [];
        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(MapDeviceSummary(reader));
        }
        return list;
    }

    // ==================== DEVICE CHALLENGE & TOKEN ====================

    public ServiceOutcome<DeviceChallengeResponse> CreateChallenge(DeviceChallengeRequest request)
    {
        if (!string.Equals(request.ProductId, CentralServerOptions.CanonicalProductId, StringComparison.Ordinal))
        {
            return ServiceOutcome<DeviceChallengeResponse>.Fail(403, "ERR_WRONG_PRODUCT", "Wrong product ID.");
        }

        using SqliteConnection conn = _db.OpenConnection();
        AuthenticatedDeviceContext? device = ReadDevice(conn, null, request.DeviceId);
        if (device is null)
        {
            return ServiceOutcome<DeviceChallengeResponse>.Fail(404, "ERR_DEVICE_NOT_FOUND", "Unknown device.");
        }

        if (device.TenantId != request.TenantId || device.StoreId != request.StoreId)
        {
            return ServiceOutcome<DeviceChallengeResponse>.Fail(403, "ERR_TENANT_SCOPE_VIOLATION", "Device tenant/store mismatch.");
        }

        if (string.Equals(device.Status, "Revoked", StringComparison.Ordinal))
        {
            return ServiceOutcome<DeviceChallengeResponse>.Fail(403, "DEVICE_REVOKED", "Device has been revoked.");
        }

        if (!string.Equals(device.Status, "Active", StringComparison.Ordinal))
        {
            return ServiceOutcome<DeviceChallengeResponse>.Fail(403, "ERR_DEVICE_NOT_ACTIVE", $"Device status is '{device.Status}'.");
        }

        string challengeId = Guid.CreateVersion7().ToString("D");
        string nonce = CentralCrypto.GenerateOpaqueToken(32);
        DateTimeOffset expiresAt = _time.GetUtcNow().AddSeconds(_options.ChallengeTtlSeconds);

        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO sync_device_challenges (challenge_id, device_id, nonce_base64, expires_at_utc)
            VALUES ($id, $deviceId, $nonce, $expires);
            """;
        cmd.Parameters.AddWithValue("$id", challengeId);
        cmd.Parameters.AddWithValue("$deviceId", request.DeviceId.ToString("D"));
        cmd.Parameters.AddWithValue("$nonce", nonce);
        cmd.Parameters.AddWithValue("$expires", expiresAt.ToString("O"));
        cmd.ExecuteNonQuery();

        return ServiceOutcome<DeviceChallengeResponse>.Ok(new DeviceChallengeResponse(challengeId, nonce, expiresAt));
    }

    public ServiceOutcome<DeviceTokenResponse> ExchangeToken(DeviceTokenRequest request)
    {
        if (!string.Equals(request.ProductId, CentralServerOptions.CanonicalProductId, StringComparison.Ordinal))
        {
            return ServiceOutcome<DeviceTokenResponse>.Fail(403, "ERR_WRONG_PRODUCT", "Wrong product ID.");
        }

        DateTimeOffset now = _time.GetUtcNow();
        using SqliteConnection conn = _db.OpenConnection();
        using SqliteTransaction tx = conn.BeginTransaction();

        AuthenticatedDeviceContext? device = ReadDevice(conn, tx, request.DeviceId);
        if (device is null)
        {
            return ServiceOutcome<DeviceTokenResponse>.Fail(404, "ERR_DEVICE_NOT_FOUND", "Unknown device.");
        }

        if (device.TenantId != request.TenantId || device.StoreId != request.StoreId)
        {
            return ServiceOutcome<DeviceTokenResponse>.Fail(403, "ERR_TENANT_SCOPE_VIOLATION", "Device tenant/store mismatch.");
        }

        if (string.Equals(device.Status, "Revoked", StringComparison.Ordinal))
        {
            return ServiceOutcome<DeviceTokenResponse>.Fail(403, "DEVICE_REVOKED", "Device has been revoked.");
        }

        if (!string.Equals(device.Status, "Active", StringComparison.Ordinal))
        {
            return ServiceOutcome<DeviceTokenResponse>.Fail(403, "ERR_DEVICE_NOT_ACTIVE", "Device is not active.");
        }

        string nonce;
        DateTimeOffset challengeExpires;
        bool alreadyUsed;
        using (SqliteCommand chCmd = conn.CreateCommand())
        {
            chCmd.Transaction = tx;
            chCmd.CommandText = """
                SELECT nonce_base64, expires_at_utc, consumed_at_utc
                FROM sync_device_challenges
                WHERE challenge_id = $cid AND device_id = $deviceId;
                """;
            chCmd.Parameters.AddWithValue("$cid", request.ChallengeId);
            chCmd.Parameters.AddWithValue("$deviceId", request.DeviceId.ToString("D"));
            using SqliteDataReader reader = chCmd.ExecuteReader();
            if (!reader.Read())
            {
                return ServiceOutcome<DeviceTokenResponse>.Fail(401, "ERR_INVALID_CHALLENGE", "Challenge not found.");
            }
            nonce = reader.GetString(0);
            challengeExpires = DateTimeOffset.Parse(reader.GetString(1));
            alreadyUsed = !reader.IsDBNull(2);
        }

        if (alreadyUsed)
        {
            return ServiceOutcome<DeviceTokenResponse>.Fail(401, "ERR_CHALLENGE_REPLAYED", "Challenge nonce was already consumed.");
        }

        if (challengeExpires <= now)
        {
            return ServiceOutcome<DeviceTokenResponse>.Fail(401, "ERR_CHALLENGE_EXPIRED", "Challenge expired.");
        }

        string expectedPayload = CentralCrypto.BuildChallengeSigningPayload(
            request.ChallengeId, nonce, request.DeviceId, request.ProductId, request.TenantId, request.StoreId);

        if (!CentralCrypto.VerifyEcdsaP256Signature(device.PublicKeySpki, expectedPayload, request.SignatureBase64))
        {
            return ServiceOutcome<DeviceTokenResponse>.Fail(401, "ERR_INVALID_SIGNATURE", "ECDSA P-256 signature verification failed.");
        }

        using (SqliteCommand consume = conn.CreateCommand())
        {
            consume.Transaction = tx;
            consume.CommandText = """
                UPDATE sync_device_challenges
                SET consumed_at_utc = $now
                WHERE challenge_id = $cid AND consumed_at_utc IS NULL;
                """;
            consume.Parameters.AddWithValue("$now", now.ToString("O"));
            consume.Parameters.AddWithValue("$cid", request.ChallengeId);
            if (consume.ExecuteNonQuery() != 1)
            {
                return ServiceOutcome<DeviceTokenResponse>.Fail(401, "ERR_CHALLENGE_REPLAYED", "Challenge concurrently consumed.");
            }
        }

        CentralServerStateSnapshot state = _db.ReadServerState(conn, tx);
        string rawToken = CentralCrypto.GenerateOpaqueToken(32);
        string tokenHash = CentralCrypto.Sha256Hex(rawToken);
        DateTimeOffset tokenExpires = now.AddMinutes(_options.DeviceTokenTtlMinutes);

        using (SqliteCommand insertToken = conn.CreateCommand())
        {
            insertToken.Transaction = tx;
            insertToken.CommandText = """
                INSERT INTO sync_device_tokens (
                    token_hash, device_id, tenant_id, store_id, product_id, issued_at_utc, expires_at_utc)
                VALUES ($hash, $deviceId, $tenantId, $storeId, $productId, $now, $expires);
                """;
            insertToken.Parameters.AddWithValue("$hash", tokenHash);
            insertToken.Parameters.AddWithValue("$deviceId", device.DeviceId.ToString("D"));
            insertToken.Parameters.AddWithValue("$tenantId", device.TenantId.ToString("D"));
            insertToken.Parameters.AddWithValue("$storeId", device.StoreId.ToString("D"));
            insertToken.Parameters.AddWithValue("$productId", device.ProductId);
            insertToken.Parameters.AddWithValue("$now", now.ToString("O"));
            insertToken.Parameters.AddWithValue("$expires", tokenExpires.ToString("O"));
            insertToken.ExecuteNonQuery();
        }

        using (SqliteCommand touch = conn.CreateCommand())
        {
            touch.Transaction = tx;
            touch.CommandText = "UPDATE sync_device_registry SET last_seen_at_utc = $now WHERE device_id = $deviceId;";
            touch.Parameters.AddWithValue("$now", now.ToString("O"));
            touch.Parameters.AddWithValue("$deviceId", device.DeviceId.ToString("D"));
            touch.ExecuteNonQuery();
        }

        DateTimeOffset leaseExpires = now.AddHours(Math.Clamp(_options.OfflineLeaseHours, 1, 72));
        string leasePayload = CentralCrypto.BuildLeaseSigningPayload(
            device.DeviceId, device.TenantId, device.StoreId, device.ProductId, state.ServerEpoch, now, leaseExpires);
        string leaseSig = CentralCrypto.SignEcdsaP256WithPem(state.LeaseSigningPrivateKeyPem, leasePayload);

        SignedOfflineLeaseV1 lease = new(
            device.DeviceId,
            device.TenantId,
            device.StoreId,
            device.ProductId,
            state.ServerEpoch,
            now,
            leaseExpires,
            leaseSig,
            state.LeaseSigningPublicKeySpki);

        tx.Commit();

        return ServiceOutcome<DeviceTokenResponse>.Ok(
            new DeviceTokenResponse(rawToken, tokenExpires, state.ServerEpoch, lease));
    }

    public ServiceOutcome<AuthenticatedDeviceContext> AuthenticateDeviceToken(
        string? rawToken,
        Guid headerDeviceId,
        Guid headerTenantId,
        Guid headerStoreId,
        string headerProductId,
        bool allowRevokedForQuarantinePush)
    {
        if (!string.Equals(headerProductId, CentralServerOptions.CanonicalProductId, StringComparison.Ordinal))
        {
            return ServiceOutcome<AuthenticatedDeviceContext>.Fail(403, "ERR_WRONG_PRODUCT", "Product ID must be Salryn.Cafe.");
        }

        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return ServiceOutcome<AuthenticatedDeviceContext>.Fail(401, "ERR_DEVICE_AUTH_REQUIRED", "Device bearer token is required.");
        }

        string tokenHash = CentralCrypto.Sha256Hex(rawToken);
        DateTimeOffset now = _time.GetUtcNow();

        using SqliteConnection conn = _db.OpenConnection();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT t.device_id, t.tenant_id, t.store_id, t.product_id, t.expires_at_utc, t.revoked_at_utc,
                   d.device_role, d.status, d.public_key_spki
            FROM sync_device_tokens t
            INNER JOIN sync_device_registry d ON d.device_id = t.device_id
            WHERE t.token_hash = $hash;
            """;
        cmd.Parameters.AddWithValue("$hash", tokenHash);
        using SqliteDataReader reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            return ServiceOutcome<AuthenticatedDeviceContext>.Fail(401, "ERR_INVALID_DEVICE_TOKEN", "Invalid device token.");
        }

        Guid tokenDeviceId = Guid.Parse(reader.GetString(0));
        Guid tokenTenantId = Guid.Parse(reader.GetString(1));
        Guid tokenStoreId = Guid.Parse(reader.GetString(2));
        string tokenProductId = reader.GetString(3);
        DateTimeOffset expiresAt = DateTimeOffset.Parse(reader.GetString(4));
        bool tokenRevoked = !reader.IsDBNull(5);
        string deviceRole = reader.GetString(6);
        string deviceStatus = reader.GetString(7);
        string publicKeySpki = reader.GetString(8);

        if (!string.Equals(tokenProductId, CentralServerOptions.CanonicalProductId, StringComparison.Ordinal))
        {
            return ServiceOutcome<AuthenticatedDeviceContext>.Fail(403, "ERR_WRONG_PRODUCT", "Token belongs to another product.");
        }

        if (tokenDeviceId != headerDeviceId || tokenTenantId != headerTenantId || tokenStoreId != headerStoreId)
        {
            return ServiceOutcome<AuthenticatedDeviceContext>.Fail(403, "ERR_TENANT_SCOPE_VIOLATION", "Device/tenant/store header scope mismatch.");
        }

        if (string.Equals(deviceStatus, "Revoked", StringComparison.Ordinal))
        {
            if (allowRevokedForQuarantinePush)
            {
                // P1 #1 fix: even for revoked-device quarantine push, never accept an expired bearer token
                if (expiresAt <= now)
                {
                    return ServiceOutcome<AuthenticatedDeviceContext>.Fail(401, "ERR_DEVICE_TOKEN_EXPIRED", "Expired token cannot submit quarantine batches; provide fresh asymmetric proof or admin recovery.");
                }

                return ServiceOutcome<AuthenticatedDeviceContext>.Ok(
                    new AuthenticatedDeviceContext(tokenDeviceId, tokenTenantId, tokenStoreId, tokenProductId, deviceRole, deviceStatus, publicKeySpki));
            }
            return ServiceOutcome<AuthenticatedDeviceContext>.Fail(403, "DEVICE_REVOKED", "Device has been revoked.");
        }

        if (tokenRevoked || expiresAt <= now)
        {
            return ServiceOutcome<AuthenticatedDeviceContext>.Fail(401, "ERR_DEVICE_TOKEN_EXPIRED", "Device token expired or revoked.");
        }

        if (!string.Equals(deviceStatus, "Active", StringComparison.Ordinal))
        {
            return ServiceOutcome<AuthenticatedDeviceContext>.Fail(403, "ERR_DEVICE_NOT_ACTIVE", "Device is not active.");
        }

        return ServiceOutcome<AuthenticatedDeviceContext>.Ok(
            new AuthenticatedDeviceContext(tokenDeviceId, tokenTenantId, tokenStoreId, tokenProductId, deviceRole, deviceStatus, publicKeySpki));
    }

    // ==================== SYNC: BOOTSTRAP, PUSH, PULL ====================

    public ServiceOutcome<BootstrapResponseDto> GetBootstrap(AuthenticatedDeviceContext device, Guid requestedStoreId)
    {
        if (requestedStoreId != device.StoreId)
        {
            return ServiceOutcome<BootstrapResponseDto>.Fail(403, "ERR_TENANT_SCOPE_VIOLATION", "Cannot bootstrap another store.");
        }

        if (!_options.EnableCanonicalDomainProcessing)
        {
            return ServiceOutcome<BootstrapResponseDto>.Fail(
                503,
                "ERR_BOOTSTRAP_DOMAIN_SNAPSHOT_UNAVAILABLE",
                "Central Cafe domain projections are not implemented. Do not advance the bootstrap cursor.");
        }

        if (device.TenantId != _options.CanonicalTenantId || device.StoreId != _options.CanonicalStoreId)
        {
            return ServiceOutcome<BootstrapResponseDto>.Fail(
                403,
                "ERR_CANONICAL_AUTHORITY_SCOPE",
                "Device is outside the canonical domain authority scope.");
        }

        using SqliteConnection conn = _db.OpenConnection();
        using SqliteTransaction tx = conn.BeginTransaction();
        CentralServerStateSnapshot state = _db.ReadServerState(conn, tx);

        List<SyncBootstrapRoleV1> roles = [];
        using (SqliteCommand cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT id, name, is_system_role FROM roles ORDER BY id;";
            using SqliteDataReader r = cmd.ExecuteReader();
            while (r.Read())
            {
                roles.Add(new SyncBootstrapRoleV1(Guid.Parse(r.GetString(0)), r.GetString(1), r.GetInt32(2) != 0));
            }
        }

        List<SyncBootstrapUserV1> users = [];
        using (SqliteCommand cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT id, username, display_name, role_id, status FROM users ORDER BY id;";
            using SqliteDataReader r = cmd.ExecuteReader();
            while (r.Read())
            {
                users.Add(new SyncBootstrapUserV1(
                    Guid.Parse(r.GetString(0)), r.GetString(1), r.GetString(2), Guid.Parse(r.GetString(3)), r.GetInt32(4)));
            }
        }

        List<SyncBootstrapUnitV1> units = [];
        using (SqliteCommand cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT id, name, abbreviation, allows_decimal_quantity, is_active FROM units ORDER BY id;";
            using SqliteDataReader r = cmd.ExecuteReader();
            while (r.Read())
            {
                units.Add(new SyncBootstrapUnitV1(
                    Guid.Parse(r.GetString(0)), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetInt32(3) != 0, r.GetInt32(4) != 0));
            }
        }

        List<SyncBootstrapProductV1> products = [];
        using (SqliteCommand cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT id, sku, barcode, name, description, unit_id, cost_price_cents, selling_price_cents,
                       wholesale_price_cents, stock_on_hand_x1000, reorder_level_x1000, track_stock,
                       allow_discount, is_active, tax_classification
                FROM products ORDER BY id;
                """;
            using SqliteDataReader r = cmd.ExecuteReader();
            while (r.Read())
            {
                products.Add(new SyncBootstrapProductV1(
                    Guid.Parse(r.GetString(0)),
                    r.IsDBNull(1) ? null : r.GetString(1),
                    r.IsDBNull(2) ? null : r.GetString(2),
                    r.GetString(3),
                    r.IsDBNull(4) ? null : r.GetString(4),
                    Guid.Parse(r.GetString(5)),
                    r.IsDBNull(6) ? null : r.GetInt64(6),
                    r.GetInt64(7),
                    r.IsDBNull(8) ? null : r.GetInt64(8),
                    r.GetInt64(9),
                    r.GetInt64(10),
                    r.GetInt32(11) != 0,
                    r.GetInt32(12) != 0,
                    r.GetInt32(13) != 0,
                    r.GetInt32(14)));
            }
        }

        List<SyncBootstrapCashierSessionV1> sessions = [];
        using (SqliteCommand cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT s.id, s.user_id, s.opened_at, s.opening_cash_cents, s.expected_cash_cents, s.status, s.notes,
                       COALESCE((SELECT SUM(m.delta_cents) FROM cash_movements m WHERE m.cashier_session_id = s.id), 0)
                FROM cashier_sessions s ORDER BY s.id;
                """;
            using SqliteDataReader r = cmd.ExecuteReader();
            while (r.Read())
            {
                sessions.Add(new SyncBootstrapCashierSessionV1(
                    Guid.Parse(r.GetString(0)),
                    Guid.Parse(r.GetString(1)),
                    DateTimeOffset.Parse(r.GetString(2)),
                    r.GetInt64(3),
                    r.GetInt64(4),
                    r.GetInt32(5),
                    r.IsDBNull(6) ? null : r.GetString(6),
                    r.GetInt64(7)));
            }
        }

        List<SyncBootstrapCustomerV1> customers = [];
        using (SqliteCommand cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT c.id, c.full_name, c.phone, c.address, c.note, c.credit_limit_cents, c.is_active,
                       c.created_by_user_id, c.created_at, c.updated_at,
                       COALESCE((SELECT SUM(e.debit_cents - e.credit_cents) FROM accounts_receivable_entries e WHERE e.customer_id = c.id), 0)
                FROM customers c ORDER BY c.id;
                """;
            using SqliteDataReader r = cmd.ExecuteReader();
            while (r.Read())
            {
                customers.Add(new SyncBootstrapCustomerV1(
                    Guid.Parse(r.GetString(0)),
                    r.GetString(1),
                    r.IsDBNull(2) ? null : r.GetString(2),
                    r.IsDBNull(3) ? null : r.GetString(3),
                    r.IsDBNull(4) ? null : r.GetString(4),
                    r.IsDBNull(5) ? null : r.GetInt64(5),
                    r.GetInt32(6) != 0,
                    Guid.Parse(r.GetString(7)),
                    DateTimeOffset.Parse(r.GetString(8)),
                    DateTimeOffset.Parse(r.GetString(9)),
                    r.GetInt64(10)));
            }
        }

        SyncBootstrapSnapshotV1 snapshot = SyncBootstrapSnapshotProtocolV1.Create(
            state.CentralInstanceId,
            device.StoreId,
            state.HeadSequence,
            roles,
            users,
            units,
            products,
            sessions,
            customers);

        return ServiceOutcome<BootstrapResponseDto>.Ok(
            new BootstrapResponseDto(
                CentralServerOptions.CanonicalProductId,
                state.ServerEpoch,
                state.HeadSequence,
                snapshot));
    }

    public ServiceOutcome<PushOperationsResponse> PushOperations(
        AuthenticatedDeviceContext device,
        PushOperationsRequest request,
        string? operatorToken = null)
    {
        if (request.Operations is null || request.Operations.Count == 0)
        {
            return ServiceOutcome<PushOperationsResponse>.Fail(400, "ERR_EMPTY_BATCH", "Operations batch must not be empty.");
        }

        if (request.Operations.Count > _options.MaxPushBatchSize)
        {
            return ServiceOutcome<PushOperationsResponse>.Fail(400, "ERR_BATCH_TOO_LARGE", $"Max batch size is {_options.MaxPushBatchSize}.");
        }

        // Validate every envelope cryptographically & verify device/store scope before touching DB
        foreach (SyncOperationEnvelopeV1 env in request.Operations)
        {
            SyncEnvelopeValidationResult validation = SyncProtocolV1.Validate(env);
            if (!validation.IsValid)
            {
                return ServiceOutcome<PushOperationsResponse>.Fail(
                    400,
                    validation.ErrorCode ?? "ERR_INVALID_ENVELOPE",
                    validation.ErrorMessage ?? "Envelope validation failed.");
            }

            if (env.DeviceId != device.DeviceId || env.StoreId != device.StoreId)
            {
                return ServiceOutcome<PushOperationsResponse>.Fail(
                    403,
                    "ERR_TENANT_SCOPE_VIOLATION",
                    "Envelope DeviceId or StoreId does not match authenticated device scope.");
            }
        }

        DateTimeOffset now = _time.GetUtcNow();
        using SqliteConnection conn = _db.OpenConnection();
        // Immediate transaction ensures deterministic serialization of sequence numbers
        using SqliteTransaction transaction = conn.BeginTransaction(deferred: false);

        try
        {
            CentralServerStateSnapshot state = _db.ReadServerState(conn, transaction);

            // Revoked device fencing: strictly quarantine, NEVER mutate live ledger or change feed
            if (string.Equals(device.Status, "Revoked", StringComparison.Ordinal))
            {
                using (SqliteCommand capCmd = conn.CreateCommand())
                {
                    capCmd.Transaction = transaction;
                    capCmd.CommandText = "SELECT COUNT(*) FROM sync_revoked_quarantine WHERE device_id = $deviceId;";
                    capCmd.Parameters.AddWithValue("$deviceId", device.DeviceId.ToString("D"));
                    long existingQuarantineCount = Convert.ToInt64(capCmd.ExecuteScalar());
                    if (existingQuarantineCount + request.Operations.Count > _options.MaxQuarantinePerDevice)
                    {
                        transaction.Rollback();
                        return ServiceOutcome<PushOperationsResponse>.Fail(
                            429,
                            "ERR_QUARANTINE_CAP_REACHED",
                            $"Revoked device quarantine cap ({_options.MaxQuarantinePerDevice}) exceeded.",
                            state.ServerEpoch);
                    }
                }

                List<Guid> quarantinedIds = [];
                foreach (SyncOperationEnvelopeV1 env in request.Operations)
                {
                    using SqliteCommand qCmd = conn.CreateCommand();
                    qCmd.CommandText = """
                        INSERT OR IGNORE INTO sync_revoked_quarantine (
                            quarantine_id, tenant_id, store_id, device_id, operation_id, operation_type,
                            operation_hash, payload_hash, envelope_json, received_at_utc, review_status)
                        VALUES (
                            $qid, $tenantId, $storeId, $deviceId, $opId, $opType,
                            $opHash, $payloadHash, $envJson, $now, 'AdminReviewRequired');
                        """;
                    qCmd.Parameters.AddWithValue("$qid", Guid.CreateVersion7().ToString("D"));
                    qCmd.Parameters.AddWithValue("$tenantId", device.TenantId.ToString("D"));
                    qCmd.Parameters.AddWithValue("$storeId", device.StoreId.ToString("D"));
                    qCmd.Parameters.AddWithValue("$deviceId", device.DeviceId.ToString("D"));
                    qCmd.Parameters.AddWithValue("$opId", env.OperationId.ToString("D"));
                    qCmd.Parameters.AddWithValue("$opType", env.OperationType);
                    qCmd.Parameters.AddWithValue("$opHash", env.OperationHash);
                    qCmd.Parameters.AddWithValue("$payloadHash", env.PayloadHash);
                    qCmd.Parameters.AddWithValue("$envJson", JsonSerializer.Serialize(env));
                    qCmd.Parameters.AddWithValue("$now", now.ToString("O"));
                    qCmd.ExecuteNonQuery();
                    quarantinedIds.Add(env.OperationId);
                }

                transaction.Commit();
                return ServiceOutcome<PushOperationsResponse>.FencedRevoked(
                    new RevokedFencedResponseDto(
                        "DEVICE_REVOKED_FENCED",
                        "Device is revoked. Offline operations were quarantined for admin review and NOT applied to live ledgers.",
                        state.ServerEpoch,
                        quarantinedIds));
            }

            // Epoch check (e.g., after central DB restore)
            if (request.KnownEpoch != state.ServerEpoch)
            {
                transaction.Rollback();
                return ServiceOutcome<PushOperationsResponse>.Fail(
                    409,
                    "EPOCH_MISMATCH_REBOOTSTRAP_REQUIRED",
                    $"Client epoch {request.KnownEpoch} does not match server epoch {state.ServerEpoch}. Re-bootstrap required.",
                    state.ServerEpoch);
            }

            if (_options.EnableCanonicalDomainProcessing)
            {
                string? operatorError = ValidateOperatorToken(conn, transaction, device, operatorToken,
                    state.ServerEpoch, request.Operations.Select(x => x.ActorUserId), now);
                if (operatorError is not null)
                {
                    transaction.Rollback();
                    return ServiceOutcome<PushOperationsResponse>.Fail(403, operatorError,
                        "Current operator authorization is required for the original queued actor; retain pending operations.", state.ServerEpoch);
                }
            }

            // Explicitly recognize supported Cafe operation families, but do not report
            // financial application until a reconciled canonical domain processor exists.
            // Reject an unsupported type/version atomically before admitting any batch member.
            foreach (SyncOperationEnvelopeV1 env in request.Operations)
            {
                if (!CentralDomainAdmission.IsRecognized(env.OperationType, env.SchemaVersion))
                {
                    transaction.Rollback();
                    return ServiceOutcome<PushOperationsResponse>.Fail(
                        422,
                        "ERR_UNSUPPORTED_OPERATION_TYPE",
                        "Operation type/schema is not registered for central admission.",
                        state.ServerEpoch);
                }
            }

            long nextReceivedSeq = ReadMaxReceivedSequence(conn, transaction);
            List<PushOperationResultDto> results = [];

            foreach (SyncOperationEnvelopeV1 env in request.Operations)
            {
                // Check idempotent replay or hash conflict (scoped strictly by tenant_id + store_id + operation_id)
                using (SqliteCommand existingCmd = conn.CreateCommand())
                {
                    existingCmd.Transaction = transaction;
                    existingCmd.CommandText = """
                        SELECT operation_hash, received_sequence, received_at_utc, status,
                               result_code, result_json, resolved_at_utc
                        FROM sync_inbox_operations
                        WHERE tenant_id = $tenantId AND store_id = $storeId AND operation_id = $opId;
                        """;
                    existingCmd.Parameters.AddWithValue("$tenantId", device.TenantId.ToString("D"));
                    existingCmd.Parameters.AddWithValue("$storeId", device.StoreId.ToString("D"));
                    existingCmd.Parameters.AddWithValue("$opId", env.OperationId.ToString("D"));
                    using SqliteDataReader reader = existingCmd.ExecuteReader();
                    if (reader.Read())
                    {
                        string existingHash = reader.GetString(0);
                        if (!CentralCrypto.FixedTimeEqualsAscii(existingHash, env.OperationHash))
                        {
                            transaction.Rollback();
                            return ServiceOutcome<PushOperationsResponse>.Fail(
                                409,
                                "OPERATION_HASH_CONFLICT",
                                $"OperationId '{env.OperationId:D}' already exists with a different operation hash.",
                                state.ServerEpoch);
                        }

                        long existingReceivedSeq = reader.GetInt64(1);
                        DateTimeOffset existingReceivedAt = DateTimeOffset.Parse(reader.GetString(2));
                        string existingStatus = reader.GetString(3);
                        string existingResultCode = reader.GetString(4);
                        string existingResultJson = reader.GetString(5);
                        DateTimeOffset existingResolvedAt = DateTimeOffset.Parse(reader.GetString(6));
                        reader.Close();

                        if (existingStatus == SaleCommittedV1Contract.NeedsReview && _options.EnableCanonicalDomainProcessing)
                        {
                            CentralDomainResult retried = CentralCanonicalDomainProcessor.Apply(conn, transaction, _options, device, env);
                            if (retried.Status != SaleCommittedV1Contract.NeedsReview)
                            {
                                long? retriedFeedSeq = retried.Status == SaleCommittedV1Contract.Applied
                                    ? AppendCanonicalChange(conn, transaction, device, env, state, retried.ChangeType!, now) : null;
                                string updatedResultJson = JsonSerializer.Serialize(new
                                {
                                    applied = retried.Status == SaleCommittedV1Contract.Applied,
                                    needsReview = false,
                                    resultCode = retried.Code,
                                    operationType = env.OperationType,
                                    schemaVersion = env.SchemaVersion
                                });

                                using (SqliteCommand updateInbox = conn.CreateCommand())
                                {
                                    updateInbox.Transaction = transaction;
                                    updateInbox.CommandText = """
                                        UPDATE sync_inbox_operations
                                        SET status = $status,
                                            result_code = $resultCode,
                                            result_json = $resultJson,
                                            resolved_at_utc = $resolvedAt
                                        WHERE tenant_id = $tenantId AND store_id = $storeId AND operation_id = $opId;
                                        """;
                                    updateInbox.Parameters.AddWithValue("$status", retried.Status);
                                    updateInbox.Parameters.AddWithValue("$resultCode", retried.Code);
                                    updateInbox.Parameters.AddWithValue("$resultJson", updatedResultJson);
                                    updateInbox.Parameters.AddWithValue("$resolvedAt", now.ToString("O"));
                                    updateInbox.Parameters.AddWithValue("$tenantId", device.TenantId.ToString("D"));
                                    updateInbox.Parameters.AddWithValue("$storeId", device.StoreId.ToString("D"));
                                    updateInbox.Parameters.AddWithValue("$opId", env.OperationId.ToString("D"));
                                    updateInbox.ExecuteNonQuery();
                                }

                                SyncOperationFinalResolutionV1 resolved = new(
                                    env.OperationId,
                                    existingReceivedSeq,
                                    existingReceivedAt,
                                    retried.Status,
                                    retried.Code,
                                    updatedResultJson,
                                    now);
                                results.Add(new PushOperationResultDto(env.OperationId, false, resolved, retriedFeedSeq));
                                continue;
                            }
                        }

                        SyncOperationFinalResolutionV1 cached = new(
                            env.OperationId,
                            existingReceivedSeq,
                            existingReceivedAt,
                            existingStatus,
                            existingResultCode,
                            existingResultJson,
                            existingResolvedAt);

                        long? existingFeedSeq = ReadChangeFeedSequenceForOperation(conn, device.TenantId, device.StoreId, env.OperationId, transaction);
                        results.Add(new PushOperationResultDto(env.OperationId, true, cached, existingFeedSeq));
                        continue;
                    }
                }

                // LocalSequence belongs to the authenticated device; two different operations
                // may not reuse the same sequence even when operation IDs differ.
                using (SqliteCommand sequenceCmd = conn.CreateCommand())
                {
                    sequenceCmd.Transaction = transaction;
                    sequenceCmd.CommandText = """
                        SELECT operation_id FROM sync_inbox_operations
                        WHERE tenant_id = $tenantId AND store_id = $storeId
                          AND device_id = $deviceId AND local_sequence = $localSequence
                        LIMIT 1;
                        """;
                    sequenceCmd.Parameters.AddWithValue("$tenantId", device.TenantId.ToString("D"));
                    sequenceCmd.Parameters.AddWithValue("$storeId", device.StoreId.ToString("D"));
                    sequenceCmd.Parameters.AddWithValue("$deviceId", device.DeviceId.ToString("D"));
                    sequenceCmd.Parameters.AddWithValue("$localSequence", env.LocalSequence);
                    if (sequenceCmd.ExecuteScalar() is not null)
                    {
                        transaction.Rollback();
                        return ServiceOutcome<PushOperationsResponse>.Fail(
                            409,
                            "ERR_DEVICE_LOCAL_SEQUENCE_CONFLICT",
                            "Another operation already uses this device-local sequence.",
                            state.ServerEpoch);
                    }
                }

                // Admit financial evidence durably, but never manufacture domain application.
                // Pending evidence cannot enter the applied change-feed before domain processing.
                CentralDomainResult domain = CentralCanonicalDomainProcessor.Apply(conn, transaction, _options, device, env);
                long? appliedFeedSequence = domain.Status == SaleCommittedV1Contract.Applied
                    ? AppendCanonicalChange(conn, transaction, device, env, state, domain.ChangeType!, now) : null;
                nextReceivedSeq++;
                string resultJson = JsonSerializer.Serialize(new
                {
                    applied = domain.Status == SaleCommittedV1Contract.Applied,
                    needsReview = domain.Status == SaleCommittedV1Contract.NeedsReview,
                    operationId = env.OperationId,
                    operationType = env.OperationType,
                    serverEpoch = state.ServerEpoch,
                    reason = domain.Code
                });

                using (SqliteCommand insertInbox = conn.CreateCommand())
                {
                    insertInbox.Transaction = transaction;
                    insertInbox.CommandText = """
                        INSERT INTO sync_inbox_operations (
                            operation_id, tenant_id, store_id, device_id, actor_user_id, local_sequence,
                            operation_type, schema_version, aggregate_id, payload_hash, operation_hash,
                            envelope_json, received_sequence, status, result_code, result_json,
                            received_at_utc, resolved_at_utc)
                        VALUES (
                            $opId, $tenantId, $storeId, $deviceId, $actorId, $localSeq,
                            $opType, $schemaVer, $aggId, $payloadHash, $opHash,
                            $envJson, $recSeq, $status, $resCode, $resJson,
                            $now, $now);
                        """;
                    insertInbox.Parameters.AddWithValue("$opId", env.OperationId.ToString("D"));
                    insertInbox.Parameters.AddWithValue("$tenantId", device.TenantId.ToString("D"));
                    insertInbox.Parameters.AddWithValue("$storeId", device.StoreId.ToString("D"));
                    insertInbox.Parameters.AddWithValue("$deviceId", env.DeviceId.ToString("D"));
                    insertInbox.Parameters.AddWithValue("$actorId", env.ActorUserId.ToString("D"));
                    insertInbox.Parameters.AddWithValue("$localSeq", env.LocalSequence);
                    insertInbox.Parameters.AddWithValue("$opType", env.OperationType);
                    insertInbox.Parameters.AddWithValue("$schemaVer", env.SchemaVersion);
                    insertInbox.Parameters.AddWithValue("$aggId", env.AggregateId.ToString("D"));
                    insertInbox.Parameters.AddWithValue("$payloadHash", env.PayloadHash);
                    insertInbox.Parameters.AddWithValue("$opHash", env.OperationHash);
                    insertInbox.Parameters.AddWithValue("$envJson", JsonSerializer.Serialize(env));
                    insertInbox.Parameters.AddWithValue("$recSeq", nextReceivedSeq);
                    insertInbox.Parameters.AddWithValue("$status", domain.Status);
                    insertInbox.Parameters.AddWithValue("$resCode", domain.Code);
                    insertInbox.Parameters.AddWithValue("$resJson", resultJson);
                    insertInbox.Parameters.AddWithValue("$now", now.ToString("O"));
                    insertInbox.ExecuteNonQuery();
                }

                SyncOperationFinalResolutionV1 resolution = new(
                    env.OperationId,
                    nextReceivedSeq,
                    now,
                    domain.Status,
                    domain.Code,
                    resultJson,
                    now);

                results.Add(new PushOperationResultDto(env.OperationId, false, resolution, appliedFeedSequence));
            }

            long headSeq = ReadMaxFeedSequence(conn, transaction);
            using (SqliteCommand updateHead = conn.CreateCommand())
            {
                    updateHead.Transaction = transaction;
                updateHead.CommandText = "UPDATE sync_server_state SET head_sequence = $head, updated_at_utc = $now WHERE singleton_id = 1;";
                updateHead.Parameters.AddWithValue("$head", headSeq);
                updateHead.Parameters.AddWithValue("$now", now.ToString("O"));
                updateHead.ExecuteNonQuery();
            }

            using (SqliteCommand touch = conn.CreateCommand())
            {
                    touch.Transaction = transaction;
                touch.CommandText = "UPDATE sync_device_registry SET last_seen_at_utc = $now WHERE device_id = $deviceId;";
                touch.Parameters.AddWithValue("$now", now.ToString("O"));
                touch.Parameters.AddWithValue("$deviceId", device.DeviceId.ToString("D"));
                touch.ExecuteNonQuery();
            }

            transaction.Commit();

            return ServiceOutcome<PushOperationsResponse>.Ok(
                new PushOperationsResponse(state.CentralInstanceId, state.ServerEpoch, headSeq, results));
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public ServiceOutcome<PullChangesResponseDto> PullChanges(
        AuthenticatedDeviceContext device,
        Guid requestedStoreId,
        long afterSequence,
        int limit,
        long knownEpoch)
    {
        if (requestedStoreId != device.StoreId)
        {
            return ServiceOutcome<PullChangesResponseDto>.Fail(403, "ERR_TENANT_SCOPE_VIOLATION", "Cannot pull another store's feed.");
        }

        if (afterSequence < 0)
        {
            return ServiceOutcome<PullChangesResponseDto>.Fail(400, "ERR_INVALID_CURSOR", "afterSequence must be >= 0.");
        }

        int clampedLimit = Math.Clamp(limit <= 0 ? 50 : limit, 1, _options.MaxPullPageSize);

        using SqliteConnection conn = _db.OpenConnection();
        CentralServerStateSnapshot state = _db.ReadServerState(conn);

        if (knownEpoch != state.ServerEpoch)
        {
            return ServiceOutcome<PullChangesResponseDto>.Fail(
                409,
                "EPOCH_MISMATCH_REBOOTSTRAP_REQUIRED",
                $"Client epoch {knownEpoch} does not match server epoch {state.ServerEpoch}. Re-bootstrap required.",
                state.ServerEpoch);
        }

        long highWatermark = 0;
        using (SqliteCommand wmCmd = conn.CreateCommand())
        {
            wmCmd.CommandText = """
                SELECT COALESCE(MAX(server_sequence), 0)
                FROM sync_change_feed
                WHERE tenant_id = $tenantId AND store_id = $storeId;
                """;
            wmCmd.Parameters.AddWithValue("$tenantId", device.TenantId.ToString("D"));
            wmCmd.Parameters.AddWithValue("$storeId", device.StoreId.ToString("D"));
            highWatermark = Convert.ToInt64(wmCmd.ExecuteScalar());
        }

        List<AppliedBusinessChangeV1> changes = [];
        using (SqliteCommand feedCmd = conn.CreateCommand())
        {
            feedCmd.CommandText = """
                SELECT server_sequence, change_id, source_operation_id, origin_device_id,
                       origin_local_sequence, store_id, change_type, schema_version,
                       aggregate_id, payload_json, payload_hash, change_hash, created_at_utc
                FROM sync_change_feed
                WHERE tenant_id = $tenantId AND store_id = $storeId AND server_sequence > $after
                ORDER BY server_sequence ASC
                LIMIT $limitPlusOne;
                """;
            feedCmd.Parameters.AddWithValue("$tenantId", device.TenantId.ToString("D"));
            feedCmd.Parameters.AddWithValue("$storeId", device.StoreId.ToString("D"));
            feedCmd.Parameters.AddWithValue("$after", afterSequence);
            feedCmd.Parameters.AddWithValue("$limitPlusOne", clampedLimit + 1);

            using SqliteDataReader reader = feedCmd.ExecuteReader();
            while (reader.Read())
            {
                changes.Add(new AppliedBusinessChangeV1(
                    state.CentralInstanceId,
                    reader.GetInt64(0),
                    Guid.Parse(reader.GetString(1)),
                    Guid.Parse(reader.GetString(2)),
                    Guid.Parse(reader.GetString(3)),
                    reader.GetInt64(4),
                    Guid.Parse(reader.GetString(5)),
                    reader.GetString(6),
                    reader.GetInt32(7),
                    Guid.Parse(reader.GetString(8)),
                    reader.GetString(9),
                    reader.GetString(10),
                    reader.GetString(11),
                    DateTimeOffset.Parse(reader.GetString(12))));
            }
        }

        bool hasMore = changes.Count > clampedLimit;
        if (hasMore)
        {
            changes.RemoveAt(changes.Count - 1);
        }

        SyncChangeFeedPageV1 page = new(
            state.CentralInstanceId,
            afterSequence,
            highWatermark,
            changes,
            hasMore);

        return ServiceOutcome<PullChangesResponseDto>.Ok(
            new PullChangesResponseDto(CentralServerOptions.CanonicalProductId, state.ServerEpoch, page));
    }

    // ==================== ADMIN QUARANTINE & EPOCH ====================

    public IReadOnlyList<QuarantineItemDto> ListQuarantine(AdminSessionDto admin)
    {
        using SqliteConnection conn = _db.OpenConnection();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT quarantine_id, tenant_id, store_id, device_id, operation_id, operation_type,
                   operation_hash, payload_hash, envelope_json, received_at_utc,
                   review_status, review_note, reviewed_by_admin_id, reviewed_at_utc
            FROM sync_revoked_quarantine
            WHERE tenant_id = $tenantId AND store_id = $storeId
            ORDER BY received_at_utc DESC;
            """;
        cmd.Parameters.AddWithValue("$tenantId", admin.TenantId.ToString("D"));
        cmd.Parameters.AddWithValue("$storeId", admin.StoreId.ToString("D"));

        List<QuarantineItemDto> items = [];
        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            items.Add(MapQuarantineItem(reader));
        }
        return items;
    }

    public ServiceOutcome<QuarantineItemDto> ReviewQuarantine(
        AdminSessionDto admin,
        Guid quarantineId,
        ReviewQuarantineRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ReviewStatus) ||
            !AllowedQuarantineReviewStatuses.Contains(request.ReviewStatus))
        {
            return ServiceOutcome<QuarantineItemDto>.Fail(400, "ERR_INVALID_REVIEW_STATUS", "Invalid quarantine review status.");
        }

        DateTimeOffset now = _time.GetUtcNow();
        using SqliteConnection conn = _db.OpenConnection();
        using SqliteTransaction tx = conn.BeginTransaction();

        using (SqliteCommand cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                UPDATE sync_revoked_quarantine
                SET review_status = $status,
                    review_note = $note,
                    reviewed_by_admin_id = $adminId,
                    reviewed_at_utc = $now
                WHERE quarantine_id = $qid AND tenant_id = $tenantId AND store_id = $storeId;
                """;
            cmd.Parameters.AddWithValue("$status", request.ReviewStatus);
            cmd.Parameters.AddWithValue("$note", string.IsNullOrWhiteSpace(request.ReviewNote) ? DBNull.Value : request.ReviewNote.Trim());
            cmd.Parameters.AddWithValue("$adminId", admin.AdminId.ToString("D"));
            cmd.Parameters.AddWithValue("$now", now.ToString("O"));
            cmd.Parameters.AddWithValue("$qid", quarantineId.ToString("D"));
            cmd.Parameters.AddWithValue("$tenantId", admin.TenantId.ToString("D"));
            cmd.Parameters.AddWithValue("$storeId", admin.StoreId.ToString("D"));
            if (cmd.ExecuteNonQuery() != 1)
            {
                return ServiceOutcome<QuarantineItemDto>.Fail(404, "ERR_QUARANTINE_NOT_FOUND", "Quarantine item not found for this store.");
            }
        }

        WriteAudit(conn, tx, admin.TenantId, admin.StoreId, admin.AdminId.ToString("D"), "REVIEW_QUARANTINE", quarantineId.ToString("D"), request);

        QuarantineItemDto updated;
        using (SqliteCommand readCmd = conn.CreateCommand())
        {
            readCmd.Transaction = tx;
            readCmd.CommandText = """
                SELECT quarantine_id, tenant_id, store_id, device_id, operation_id, operation_type,
                       operation_hash, payload_hash, envelope_json, received_at_utc,
                       review_status, review_note, reviewed_by_admin_id, reviewed_at_utc
                FROM sync_revoked_quarantine
                WHERE quarantine_id = $qid;
                """;
            readCmd.Parameters.AddWithValue("$qid", quarantineId.ToString("D"));
            using SqliteDataReader reader = readCmd.ExecuteReader();
            reader.Read();
            updated = MapQuarantineItem(reader);
        }

        tx.Commit();
        return ServiceOutcome<QuarantineItemDto>.Ok(updated);
    }

    public ServiceOutcome<EpochBumpResponse> BumpEpoch(AdminSessionDto admin, EpochBumpRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return ServiceOutcome<EpochBumpResponse>.Fail(400, "ERR_REASON_REQUIRED", "Epoch bump reason is required.");
        }

        DateTimeOffset now = _time.GetUtcNow();
        using SqliteConnection conn = _db.OpenConnection();
        using SqliteTransaction tx = conn.BeginTransaction();

        CentralServerStateSnapshot before = _db.ReadServerState(conn, tx);
        long nextEpoch = before.ServerEpoch + 1;

        using (SqliteCommand cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE sync_server_state SET server_epoch = $epoch, updated_at_utc = $now WHERE singleton_id = 1;";
            cmd.Parameters.AddWithValue("$epoch", nextEpoch);
            cmd.Parameters.AddWithValue("$now", now.ToString("O"));
            cmd.ExecuteNonQuery();
        }

        WriteAudit(conn, tx, admin.TenantId, admin.StoreId, admin.AdminId.ToString("D"), "BUMP_EPOCH", nextEpoch.ToString(), new { before.ServerEpoch, nextEpoch, request.Reason });
        tx.Commit();

        return ServiceOutcome<EpochBumpResponse>.Ok(new EpochBumpResponse(before.ServerEpoch, nextEpoch, now));
    }

    // ==================== HELPERS ====================

    private static AuthenticatedDeviceContext? ReadDevice(SqliteConnection conn, SqliteTransaction? tx, Guid deviceId)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT device_id, tenant_id, store_id, product_id, device_role, status, public_key_spki
            FROM sync_device_registry
            WHERE device_id = $deviceId;
            """;
        cmd.Parameters.AddWithValue("$deviceId", deviceId.ToString("D"));
        using SqliteDataReader reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new AuthenticatedDeviceContext(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            Guid.Parse(reader.GetString(2)),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6));
    }

    private static DeviceSummaryDto? ReadDeviceSummary(SqliteConnection conn, SqliteTransaction? tx, Guid deviceId)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT device_id, tenant_id, store_id, product_id, device_name, device_role,
                   public_key_thumbprint, status, enrolled_at_utc, approved_at_utc,
                   revoked_at_utc, revoke_reason, last_seen_at_utc
            FROM sync_device_registry
            WHERE device_id = $deviceId;
            """;
        cmd.Parameters.AddWithValue("$deviceId", deviceId.ToString("D"));
        using SqliteDataReader reader = cmd.ExecuteReader();
        return reader.Read() ? MapDeviceSummary(reader) : null;
    }

    private static DeviceSummaryDto MapDeviceSummary(SqliteDataReader reader)
    {
        return new DeviceSummaryDto(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            Guid.Parse(reader.GetString(2)),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            DateTimeOffset.Parse(reader.GetString(8)),
            reader.IsDBNull(9) ? null : DateTimeOffset.Parse(reader.GetString(9)),
            reader.IsDBNull(10) ? null : DateTimeOffset.Parse(reader.GetString(10)),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.IsDBNull(12) ? null : DateTimeOffset.Parse(reader.GetString(12)));
    }

    private static QuarantineItemDto MapQuarantineItem(SqliteDataReader reader)
    {
        return new QuarantineItemDto(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            Guid.Parse(reader.GetString(2)),
            Guid.Parse(reader.GetString(3)),
            Guid.Parse(reader.GetString(4)),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            DateTimeOffset.Parse(reader.GetString(9)),
            reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.IsDBNull(12) ? null : Guid.Parse(reader.GetString(12)),
            reader.IsDBNull(13) ? null : DateTimeOffset.Parse(reader.GetString(13)));
    }

    private static long AppendCanonicalChange(SqliteConnection connection, SqliteTransaction transaction,
        AuthenticatedDeviceContext device, SyncOperationEnvelopeV1 envelope, CentralServerStateSnapshot state,
        string changeType, DateTimeOffset now)
    {
        long sequence = ReadMaxFeedSequence(connection, transaction) + 1;
        var change = AppliedBusinessChangeProtocolV1.Create(state.CentralInstanceId, sequence,
            envelope.OperationId, envelope.DeviceId, envelope.LocalSequence, envelope.StoreId,
            changeType, 1, envelope.AggregateId, envelope.PayloadJson, now);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO sync_change_feed(server_sequence,change_id,tenant_id,store_id,source_operation_id,
                origin_device_id,origin_local_sequence,change_type,schema_version,aggregate_id,payload_json,payload_hash,change_hash,created_at_utc)
            VALUES($seq,$change,$tenant,$store,$op,$device,$local,$type,$schema,$aggregate,$payload,$payloadHash,$changeHash,$at);
            """;
        foreach (var pair in new (string Name,object Value)[] {
            ("$seq",sequence),("$change",change.ChangeId.ToString("D")),("$tenant",device.TenantId.ToString("D")),
            ("$store",envelope.StoreId.ToString("D")),("$op",envelope.OperationId.ToString("D")),
            ("$device",envelope.DeviceId.ToString("D")),("$local",envelope.LocalSequence),("$type",changeType),
            ("$schema",change.SchemaVersion),("$aggregate",envelope.AggregateId.ToString("D")),("$payload",change.PayloadJson),
            ("$payloadHash",change.PayloadHash),("$changeHash",change.ChangeHash),("$at",now.ToString("O")) })
            command.Parameters.AddWithValue(pair.Name,pair.Value);
        command.ExecuteNonQuery();
        return sequence;
    }

    private static long ReadMaxReceivedSequence(SqliteConnection conn, SqliteTransaction? transaction = null)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "SELECT COALESCE(MAX(received_sequence), 0) FROM sync_inbox_operations;";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static long ReadMaxFeedSequence(SqliteConnection conn, SqliteTransaction? transaction = null)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "SELECT COALESCE(MAX(server_sequence), 0) FROM sync_change_feed;";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private static long? ReadChangeFeedSequenceForOperation(SqliteConnection conn, Guid tenantId, Guid storeId, Guid operationId, SqliteTransaction? transaction = null)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "SELECT server_sequence FROM sync_change_feed WHERE tenant_id = $tenantId AND store_id = $storeId AND source_operation_id = $opId;";
        cmd.Parameters.AddWithValue("$tenantId", tenantId.ToString("D"));
        cmd.Parameters.AddWithValue("$storeId", storeId.ToString("D"));
        cmd.Parameters.AddWithValue("$opId", operationId.ToString("D"));
        object? val = cmd.ExecuteScalar();
        return val is null or DBNull ? null : Convert.ToInt64(val);
    }

    private void WriteAudit(
        SqliteConnection conn,
        SqliteTransaction tx,
        Guid tenantId,
        Guid storeId,
        string actorId,
        string action,
        string? targetId,
        object details)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO central_admin_audit_logs (
                audit_id, tenant_id, store_id, actor_id, action, target_id, details_json, created_at_utc)
            VALUES ($id, $tenantId, $storeId, $actorId, $action, $targetId, $details, $now);
            """;
        cmd.Parameters.AddWithValue("$id", Guid.CreateVersion7().ToString("D"));
        cmd.Parameters.AddWithValue("$tenantId", tenantId.ToString("D"));
        cmd.Parameters.AddWithValue("$storeId", storeId.ToString("D"));
        cmd.Parameters.AddWithValue("$actorId", actorId);
        cmd.Parameters.AddWithValue("$action", action);
        cmd.Parameters.AddWithValue("$targetId", targetId is null ? DBNull.Value : targetId);
        cmd.Parameters.AddWithValue("$details", JsonSerializer.Serialize(details));
        cmd.Parameters.AddWithValue("$now", _time.GetUtcNow().ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private static void CommitRaw(SqliteConnection conn)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "COMMIT;";
        cmd.ExecuteNonQuery();
    }

    private static void RollbackRaw(SqliteConnection conn)
    {
        try
        {
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "ROLLBACK;";
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // Ignore rollback errors if no active transaction
        }
    }
}
