using System.Text.Json;
using Microsoft.Data.Sqlite;
using Salryn.Core.Security;
using Salryn.Core.Services.Licensing;

namespace Salryn.LicensingIssuer.Server;

public sealed class IssuerSqliteStore
{
    private readonly string _connectionString;

    public IssuerSqliteStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        string full = Path.GetFullPath(databasePath);
        string? dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = full,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        InitializeSchema();
    }

    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    private void InitializeSchema()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS issuer_admins (
                id TEXT PRIMARY KEY,
                username TEXT NOT NULL UNIQUE COLLATE NOCASE,
                display_name TEXT NOT NULL,
                role TEXT NOT NULL,
                password_hash TEXT NOT NULL,
                is_active INTEGER NOT NULL DEFAULT 1,
                created_at_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS issuer_customers (
                id TEXT PRIMARY KEY,
                customer_name TEXT NOT NULL,
                contact_email TEXT NULL,
                notes TEXT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS issuer_stores (
                id TEXT PRIMARY KEY,
                customer_id TEXT NOT NULL REFERENCES issuer_customers(id),
                store_code TEXT NOT NULL UNIQUE COLLATE NOCASE,
                store_name TEXT NOT NULL,
                product_id TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS issuer_license_requests (
                id TEXT PRIMARY KEY,
                license_id TEXT NOT NULL UNIQUE,
                idempotency_key TEXT NULL UNIQUE,
                status TEXT NOT NULL,
                customer_id TEXT NOT NULL REFERENCES issuer_customers(id),
                store_id TEXT NOT NULL REFERENCES issuer_stores(id),
                application_id TEXT NOT NULL,
                license_type TEXT NOT NULL,
                edition TEXT NOT NULL,
                machine_binding TEXT NOT NULL,
                features_json TEXT NOT NULL,
                allowed_lan_terminals INTEGER NULL,
                host_license_id TEXT NULL,
                terminal_id TEXT NULL,
                issued_at_utc TEXT NOT NULL,
                expires_at_utc TEXT NULL,
                support_expires_at_utc TEXT NULL,
                updates_until_utc TEXT NULL,
                supersedes_request_id TEXT NULL REFERENCES issuer_license_requests(id),
                canonical_signing_payload TEXT NOT NULL,
                signed_license_json TEXT NULL,
                signed_license_sha256 TEXT NULL,
                signer_key_spki_sha256 TEXT NULL,
                signer_mode TEXT NULL,
                verifier_passed INTEGER NOT NULL DEFAULT 0,
                review_note TEXT NULL,
                created_by TEXT NOT NULL,
                approved_by TEXT NULL,
                issued_by TEXT NULL,
                created_at_utc TEXT NOT NULL,
                approved_at_utc TEXT NULL,
                issued_at_server_utc TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS issuer_audit_log (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                actor_username TEXT NOT NULL,
                actor_role TEXT NOT NULL,
                action TEXT NOT NULL,
                entity_type TEXT NOT NULL,
                entity_id TEXT NOT NULL,
                detail_json TEXT NOT NULL,
                created_at_utc TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    public void EnsureBootstrapAdmin(string username, string displayName, string password, string role = IssuerConstants.RoleAdmin)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        using var conn = OpenConnection();
        using var tx = conn.BeginTransaction(deferred: false);
        using (var exists = conn.CreateCommand())
        {
            exists.Transaction = tx;
            exists.CommandText = "SELECT COUNT(*) FROM issuer_admins WHERE username = $u;";
            exists.Parameters.AddWithValue("$u", username.Trim());
            if (Convert.ToInt64(exists.ExecuteScalar()) > 0)
            {
                tx.Commit();
                return;
            }
        }

        var hashRes = SalrynPasswordHasher.HashPassword(password);
        if (hashRes.IsFailure)
        {
            throw new InvalidOperationException("Failed to hash bootstrap admin password: " + hashRes.ErrorDetail.Message);
        }

        Guid id = Guid.NewGuid();
        string now = DateTimeOffset.UtcNow.ToString("O");
        using (var ins = conn.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText = """
                INSERT INTO issuer_admins(id, username, display_name, role, password_hash, is_active, created_at_utc)
                VALUES($id, $u, $d, $r, $h, 1, $at);
                """;
            ins.Parameters.AddWithValue("$id", id.ToString("D"));
            ins.Parameters.AddWithValue("$u", username.Trim());
            ins.Parameters.AddWithValue("$d", string.IsNullOrWhiteSpace(displayName) ? username.Trim() : displayName.Trim());
            ins.Parameters.AddWithValue("$r", role);
            ins.Parameters.AddWithValue("$h", hashRes.Value);
            ins.Parameters.AddWithValue("$at", now);
            ins.ExecuteNonQuery();
        }

        AppendAuditInTx(conn, tx, "system", "System", "Admin.Bootstrapped", "IssuerAdmin", id.ToString("D"), new { username = username.Trim(), role });
        tx.Commit();
    }

    public IssuerAdminUser? Authenticate(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, username, display_name, role, password_hash, is_active, created_at_utc
            FROM issuer_admins
            WHERE username = $u
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$u", username.Trim());
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        bool isActive = reader.GetInt64(5) == 1;
        string hash = reader.GetString(4);
        if (!isActive || !SalrynPasswordHasher.VerifyPassword(password, hash))
        {
            return null;
        }

        return new IssuerAdminUser(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            isActive,
            DateTimeOffset.Parse(reader.GetString(6)));
    }

    public IssuerCustomerRecord CreateCustomer(string customerName, string? contactEmail, string? notes, IssuerAdminUser actor)
    {
        using var conn = OpenConnection();
        using var tx = conn.BeginTransaction(deferred: false);
        Guid id = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO issuer_customers(id, customer_name, contact_email, notes, created_at_utc, updated_at_utc)
                VALUES($id, $name, $email, $notes, $at, $at);
                """;
            cmd.Parameters.AddWithValue("$id", id.ToString("D"));
            cmd.Parameters.AddWithValue("$name", customerName.Trim());
            cmd.Parameters.AddWithValue("$email", (object?)Clean(contactEmail) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$notes", (object?)Clean(notes) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$at", now.ToString("O"));
            cmd.ExecuteNonQuery();
        }
        AppendAuditInTx(conn, tx, actor.Username, actor.Role, "Customer.Created", "Customer", id.ToString("D"), new { customerName = customerName.Trim() });
        tx.Commit();
        return new IssuerCustomerRecord(id, customerName.Trim(), Clean(contactEmail), Clean(notes), now, now);
    }

    public IReadOnlyList<IssuerCustomerRecord> ListCustomers()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, customer_name, contact_email, notes, created_at_utc, updated_at_utc FROM issuer_customers ORDER BY created_at_utc DESC;";
        using var r = cmd.ExecuteReader();
        List<IssuerCustomerRecord> rows = [];
        while (r.Read())
        {
            rows.Add(new IssuerCustomerRecord(
                Guid.Parse(r.GetString(0)),
                r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                DateTimeOffset.Parse(r.GetString(4)),
                DateTimeOffset.Parse(r.GetString(5))));
        }
        return rows;
    }

    public IssuerStoreRecord CreateStore(Guid customerId, string storeCode, string storeName, string productId, IssuerAdminUser actor)
    {
        using var conn = OpenConnection();
        using var tx = conn.BeginTransaction(deferred: false);
        using (var check = conn.CreateCommand())
        {
            check.Transaction = tx;
            check.CommandText = "SELECT COUNT(*) FROM issuer_customers WHERE id = $cid;";
            check.Parameters.AddWithValue("$cid", customerId.ToString("D"));
            if (Convert.ToInt64(check.ExecuteScalar()) == 0)
            {
                throw new KeyNotFoundException("Customer was not found.");
            }
        }

        Guid id = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO issuer_stores(id, customer_id, store_code, store_name, product_id, created_at_utc, updated_at_utc)
                VALUES($id, $cid, $code, $name, $prod, $at, $at);
                """;
            cmd.Parameters.AddWithValue("$id", id.ToString("D"));
            cmd.Parameters.AddWithValue("$cid", customerId.ToString("D"));
            cmd.Parameters.AddWithValue("$code", storeCode.Trim());
            cmd.Parameters.AddWithValue("$name", storeName.Trim());
            cmd.Parameters.AddWithValue("$prod", productId.Trim());
            cmd.Parameters.AddWithValue("$at", now.ToString("O"));
            cmd.ExecuteNonQuery();
        }
        AppendAuditInTx(conn, tx, actor.Username, actor.Role, "Store.Created", "Store", id.ToString("D"), new { customerId, storeCode = storeCode.Trim(), productId = productId.Trim() });
        tx.Commit();
        return new IssuerStoreRecord(id, customerId, storeCode.Trim(), storeName.Trim(), productId.Trim(), now, now);
    }

    public IReadOnlyList<IssuerStoreRecord> ListStores()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, customer_id, store_code, store_name, product_id, created_at_utc, updated_at_utc FROM issuer_stores ORDER BY created_at_utc DESC;";
        using var r = cmd.ExecuteReader();
        List<IssuerStoreRecord> rows = [];
        while (r.Read())
        {
            rows.Add(new IssuerStoreRecord(
                Guid.Parse(r.GetString(0)),
                Guid.Parse(r.GetString(1)),
                r.GetString(2),
                r.GetString(3),
                r.GetString(4),
                DateTimeOffset.Parse(r.GetString(5)),
                DateTimeOffset.Parse(r.GetString(6))));
        }
        return rows;
    }

    public (IssuerLicenseRequestRecord Record, IReadOnlyList<string> Warnings, bool IsReplay) CreateDraft(
        CreateLicenseDraftRequestDto req,
        IssuerAdminUser actor)
    {
        using var conn = OpenConnection();
        using var tx = conn.BeginTransaction(deferred: false);

        string? idemKey = Clean(req.IdempotencyKey);
        if (idemKey is not null)
        {
            IssuerLicenseRequestRecord? existing = FindRequestByIdempotencyKeyInTx(conn, tx, idemKey);
            if (existing is not null)
            {
                tx.Commit();
                return (existing, Array.Empty<string>(), true);
            }
        }

        string customerName;
        using (var c = conn.CreateCommand())
        {
            c.Transaction = tx;
            c.CommandText = "SELECT customer_name FROM issuer_customers WHERE id = $id;";
            c.Parameters.AddWithValue("$id", req.CustomerId.ToString("D"));
            customerName = c.ExecuteScalar() as string ?? throw new KeyNotFoundException("Customer was not found.");
        }

        string storeCode;
        string storeProductId;
        using (var s = conn.CreateCommand())
        {
            s.Transaction = tx;
            s.CommandText = "SELECT store_code, product_id FROM issuer_stores WHERE id = $id AND customer_id = $cid;";
            s.Parameters.AddWithValue("$id", req.StoreId.ToString("D"));
            s.Parameters.AddWithValue("$cid", req.CustomerId.ToString("D"));
            using var sr = s.ExecuteReader();
            if (!sr.Read())
            {
                throw new KeyNotFoundException("Store was not found for this customer.");
            }
            storeCode = sr.GetString(0);
            storeProductId = sr.GetString(1);
        }

        if (!string.Equals(storeProductId, req.ApplicationId.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Store '{storeCode}' is registered for product '{storeProductId}', not '{req.ApplicationId.Trim()}'.");
        }

        if (!Enum.TryParse<LicenseType>(req.LicenseType, ignoreCase: true, out LicenseType licenseType))
        {
            throw new InvalidOperationException("LicenseType must be Host or Terminal.");
        }
        if (!Enum.TryParse<LicenseEdition>(req.Edition, ignoreCase: true, out LicenseEdition edition))
        {
            throw new InvalidOperationException("Edition must be Trial, Local, Professional, or Enterprise.");
        }

        Guid id = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string productTag = string.Equals(req.ApplicationId.Trim(), IssuerConstants.ProductCafe, StringComparison.Ordinal) ? "CAFE" : "POS";
        string licenseId = $"SLRN-{productTag}-{now:yyyyMMdd}-{id.ToString("N")[..8].ToUpperInvariant()}";

        DraftValidationResult validation = IssuerLicenseContractValidator.BuildAndValidateDraft(
            licenseId,
            customerName,
            storeCode,
            req.ApplicationId,
            licenseType,
            edition,
            req.MachineBinding,
            req.Features,
            req.AllowedLanTerminals,
            req.HostLicenseId,
            req.TerminalId,
            now,
            req.ExpiresAtUtc,
            req.SupportExpiresAtUtc,
            req.UpdatesUntilUtc);

        if (!validation.IsValid || validation.Document is null || validation.CanonicalSigningPayload is null)
        {
            throw new InvalidOperationException($"{validation.ErrorCode}: {validation.ErrorMessage}");
        }

        LocalLicenseDocument doc = validation.Document;
        using (var ins = conn.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText = """
                INSERT INTO issuer_license_requests(
                    id, license_id, idempotency_key, status, customer_id, store_id, application_id,
                    license_type, edition, machine_binding, features_json, allowed_lan_terminals,
                    host_license_id, terminal_id, issued_at_utc, expires_at_utc, support_expires_at_utc,
                    updates_until_utc, supersedes_request_id, canonical_signing_payload,
                    created_by, created_at_utc)
                VALUES(
                    $id, $licId, $idem, $status, $cid, $sid, $appId,
                    $licType, $edition, $machine, $features, $lanCount,
                    $hostLic, $termId, $issuedAt, $expAt, $supAt,
                    $updAt, $supersedes, $canonical,
                    $actor, $createdAt);
                """;
            ins.Parameters.AddWithValue("$id", id.ToString("D"));
            ins.Parameters.AddWithValue("$licId", licenseId);
            ins.Parameters.AddWithValue("$idem", (object?)idemKey ?? DBNull.Value);
            ins.Parameters.AddWithValue("$status", IssuerConstants.StatusAwaitingApproval);
            ins.Parameters.AddWithValue("$cid", req.CustomerId.ToString("D"));
            ins.Parameters.AddWithValue("$sid", req.StoreId.ToString("D"));
            ins.Parameters.AddWithValue("$appId", doc.ApplicationId!);
            ins.Parameters.AddWithValue("$licType", doc.LicenseType!.Value.ToString());
            ins.Parameters.AddWithValue("$edition", doc.Edition.ToString());
            ins.Parameters.AddWithValue("$machine", doc.MachineBinding!);
            ins.Parameters.AddWithValue("$features", JsonSerializer.Serialize(doc.Features));
            ins.Parameters.AddWithValue("$lanCount", (object?)doc.AllowedLanTerminals ?? DBNull.Value);
            ins.Parameters.AddWithValue("$hostLic", (object?)doc.HostLicenseId ?? DBNull.Value);
            ins.Parameters.AddWithValue("$termId", (object?)doc.TerminalId ?? DBNull.Value);
            ins.Parameters.AddWithValue("$issuedAt", doc.IssuedAt.ToString("O"));
            ins.Parameters.AddWithValue("$expAt", (object?)doc.ExpiresAt?.ToString("O") ?? DBNull.Value);
            ins.Parameters.AddWithValue("$supAt", (object?)doc.SupportExpiresAt?.ToString("O") ?? DBNull.Value);
            ins.Parameters.AddWithValue("$updAt", (object?)doc.UpdatesUntil?.ToString("O") ?? DBNull.Value);
            ins.Parameters.AddWithValue("$supersedes", (object?)req.SupersedesRequestId?.ToString("D") ?? DBNull.Value);
            ins.Parameters.AddWithValue("$canonical", validation.CanonicalSigningPayload);
            ins.Parameters.AddWithValue("$actor", actor.Username);
            ins.Parameters.AddWithValue("$createdAt", now.ToString("O"));
            ins.ExecuteNonQuery();
        }

        AppendAuditInTx(conn, tx, actor.Username, actor.Role, "LicenseDraft.Created", "LicenseRequest", id.ToString("D"), new
        {
            licenseId,
            applicationId = doc.ApplicationId,
            licenseType = doc.LicenseType.ToString(),
            machineBinding = doc.MachineBinding,
            supersedesRequestId = req.SupersedesRequestId
        });

        IssuerLicenseRequestRecord created = FindRequestByIdInTx(conn, tx, id)!;
        tx.Commit();
        return (created, validation.Warnings, false);
    }

    public IssuerLicenseRequestRecord ApproveRequest(Guid id, string? note, IssuerAdminUser actor)
    {
        using var conn = OpenConnection();
        using var tx = conn.BeginTransaction(deferred: false);
        IssuerLicenseRequestRecord? current = FindRequestByIdInTx(conn, tx, id)
            ?? throw new KeyNotFoundException("License request was not found.");

        if (current.Status == IssuerConstants.StatusApproved)
        {
            tx.Commit();
            return current;
        }
        if (current.Status is not (IssuerConstants.StatusDraft or IssuerConstants.StatusAwaitingApproval))
        {
            throw new InvalidOperationException($"Cannot approve license request in status '{current.Status}'.");
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                UPDATE issuer_license_requests
                SET status = $status, approved_by = $actor, approved_at_utc = $at, review_note = $note
                WHERE id = $id;
                """;
            cmd.Parameters.AddWithValue("$status", IssuerConstants.StatusApproved);
            cmd.Parameters.AddWithValue("$actor", actor.Username);
            cmd.Parameters.AddWithValue("$at", now.ToString("O"));
            cmd.Parameters.AddWithValue("$note", (object?)Clean(note) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$id", id.ToString("D"));
            cmd.ExecuteNonQuery();
        }

        AppendAuditInTx(conn, tx, actor.Username, actor.Role, "LicenseRequest.Approved", "LicenseRequest", id.ToString("D"), new { current.LicenseId, note = Clean(note) });
        IssuerLicenseRequestRecord updated = FindRequestByIdInTx(conn, tx, id)!;
        tx.Commit();
        return updated;
    }

    public IssuerLicenseRequestRecord RejectRequest(Guid id, string? note, IssuerAdminUser actor)
    {
        using var conn = OpenConnection();
        using var tx = conn.BeginTransaction(deferred: false);
        IssuerLicenseRequestRecord? current = FindRequestByIdInTx(conn, tx, id)
            ?? throw new KeyNotFoundException("License request was not found.");

        if (current.Status is IssuerConstants.StatusIssued or IssuerConstants.StatusSuperseded)
        {
            throw new InvalidOperationException($"Cannot reject already-issued license request '{current.LicenseId}'.");
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE issuer_license_requests SET status = $status, review_note = $note WHERE id = $id;";
            cmd.Parameters.AddWithValue("$status", IssuerConstants.StatusRejected);
            cmd.Parameters.AddWithValue("$note", (object?)Clean(note) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$id", id.ToString("D"));
            cmd.ExecuteNonQuery();
        }

        AppendAuditInTx(conn, tx, actor.Username, actor.Role, "LicenseRequest.Rejected", "LicenseRequest", id.ToString("D"), new { current.LicenseId, note = Clean(note) });
        IssuerLicenseRequestRecord updated = FindRequestByIdInTx(conn, tx, id)!;
        tx.Commit();
        return updated;
    }

    public (IssuerLicenseRequestRecord? Record, LicenseSigningOutcome Outcome, bool AlreadyIssued) IssueRequest(
        Guid id,
        ILicenseSignerService signer,
        IssuerAdminUser actor)
    {
        using var conn = OpenConnection();
        using var tx = conn.BeginTransaction(deferred: false);
        IssuerLicenseRequestRecord? current = FindRequestByIdInTx(conn, tx, id)
            ?? throw new KeyNotFoundException("License request was not found.");

        // Idempotent replay when already issued
        if (current.Status == IssuerConstants.StatusIssued && !string.IsNullOrWhiteSpace(current.SignedLicenseJson))
        {
            tx.Commit();
            return (current, new LicenseSigningOutcome(true, null, null, current.SignedLicenseJson, current.SignedLicenseSha256, current.SignerKeySpkiSha256, current.SignerMode, current.VerifierPassed), true);
        }

        if (current.Status != IssuerConstants.StatusApproved)
        {
            throw new InvalidOperationException($"License request must be Approved before issuance (current status: '{current.Status}').");
        }

        LocalLicenseDocument unsignedDoc = new(
            LicenseId: current.LicenseId,
            CustomerName: current.CustomerName,
            Edition: current.Edition,
            IssuedAt: current.IssuedAtUtc,
            ExpiresAt: current.ExpiresAtUtc,
            MachineBinding: current.MachineBinding,
            Features: current.Features,
            Signature: null,
            SchemaVersion: LocalLicenseContractPolicy.BetaLicenseSchemaVersion,
            ApplicationId: current.ApplicationId,
            StoreId: current.StoreCode,
            AllowedLanTerminals: current.AllowedLanTerminals,
            SupportExpiresAt: current.SupportExpiresAtUtc,
            UpdatesUntil: current.UpdatesUntilUtc,
            LicenseType: current.LicenseType,
            HostLicenseId: current.HostLicenseId,
            TerminalId: current.TerminalId);

        LicenseSigningOutcome signed = signer.SignAndVerify(unsignedDoc);
        if (!signed.IsSuccess)
        {
            AppendAuditInTx(conn, tx, actor.Username, actor.Role, "LicenseRequest.IssueBlocked", "LicenseRequest", id.ToString("D"), new { current.LicenseId, signed.ErrorCode, signed.ErrorMessage });
            tx.Commit();
            return (null, signed, false);
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        using (var upd = conn.CreateCommand())
        {
            upd.Transaction = tx;
            upd.CommandText = """
                UPDATE issuer_license_requests
                SET status = $status,
                    signed_license_json = $json,
                    signed_license_sha256 = $sha,
                    signer_key_spki_sha256 = $spki,
                    signer_mode = $mode,
                    verifier_passed = $ver,
                    issued_by = $actor,
                    issued_at_server_utc = $at
                WHERE id = $id;
                """;
            upd.Parameters.AddWithValue("$status", IssuerConstants.StatusIssued);
            upd.Parameters.AddWithValue("$json", signed.SignedLicenseJson!);
            upd.Parameters.AddWithValue("$sha", signed.SignedLicenseSha256!);
            upd.Parameters.AddWithValue("$spki", signed.SignerKeySpkiSha256!);
            upd.Parameters.AddWithValue("$mode", signed.SignerMode!);
            upd.Parameters.AddWithValue("$ver", signed.VerifierPassed ? 1 : 0);
            upd.Parameters.AddWithValue("$actor", actor.Username);
            upd.Parameters.AddWithValue("$at", now.ToString("O"));
            upd.Parameters.AddWithValue("$id", id.ToString("D"));
            upd.ExecuteNonQuery();
        }

        if (current.SupersedesRequestId.HasValue)
        {
            using var sup = conn.CreateCommand();
            sup.Transaction = tx;
            sup.CommandText = "UPDATE issuer_license_requests SET status = $sup WHERE id = $oldId AND status = $issued;";
            sup.Parameters.AddWithValue("$sup", IssuerConstants.StatusSuperseded);
            sup.Parameters.AddWithValue("$oldId", current.SupersedesRequestId.Value.ToString("D"));
            sup.Parameters.AddWithValue("$issued", IssuerConstants.StatusIssued);
            sup.ExecuteNonQuery();
        }

        AppendAuditInTx(conn, tx, actor.Username, actor.Role, "LicenseRequest.Issued", "LicenseRequest", id.ToString("D"), new
        {
            current.LicenseId,
            signed.SignedLicenseSha256,
            signed.SignerKeySpkiSha256,
            signed.SignerMode,
            supersedes = current.SupersedesRequestId
        });

        IssuerLicenseRequestRecord issuedRecord = FindRequestByIdInTx(conn, tx, id)!;
        tx.Commit();
        return (issuedRecord, signed, false);
    }

    public void RecordAudit(IssuerAdminUser actor, string action, string entityType, string entityId, object detail)
    {
        using var conn = OpenConnection();
        using var tx = conn.BeginTransaction(deferred: false);
        AppendAuditInTx(conn, tx, actor.Username, actor.Role, action, entityType, entityId, detail);
        tx.Commit();
    }

    public IssuerLicenseRequestRecord? FindRequest(Guid id)
    {
        using var conn = OpenConnection();
        using var tx = conn.BeginTransaction();
        return FindRequestByIdInTx(conn, tx, id);
    }

    public IReadOnlyList<IssuerLicenseRequestRecord> ListRequests()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = SelectRequestsSql + " ORDER BY r.created_at_utc DESC;";
        using var reader = cmd.ExecuteReader();
        List<IssuerLicenseRequestRecord> rows = [];
        while (reader.Read()) rows.Add(ReadRequest(reader));
        return rows;
    }

    public IReadOnlyList<IssuerAuditEntry> ListAuditLog(int limit = 100)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, actor_username, actor_role, action, entity_type, entity_id, detail_json, created_at_utc FROM issuer_audit_log ORDER BY id DESC LIMIT $limit;";
        cmd.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 500));
        using var r = cmd.ExecuteReader();
        List<IssuerAuditEntry> rows = [];
        while (r.Read())
        {
            rows.Add(new IssuerAuditEntry(
                r.GetInt64(0),
                r.GetString(1),
                r.GetString(2),
                r.GetString(3),
                r.GetString(4),
                r.GetString(5),
                r.GetString(6),
                DateTimeOffset.Parse(r.GetString(7))));
        }
        return rows;
    }

    public IssuerDashboardSummary GetDashboard(SignerStatusSnapshot signer)
    {
        var requests = ListRequests();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new IssuerDashboardSummary(
            DraftCount: requests.Count(x => x.Status == IssuerConstants.StatusDraft),
            AwaitingApprovalCount: requests.Count(x => x.Status == IssuerConstants.StatusAwaitingApproval),
            ApprovedCount: requests.Count(x => x.Status == IssuerConstants.StatusApproved),
            IssuedCount: requests.Count(x => x.Status == IssuerConstants.StatusIssued),
            ExpiredIssuedCount: requests.Count(x => x.Status == IssuerConstants.StatusIssued && x.ExpiresAtUtc.HasValue && x.ExpiresAtUtc.Value <= now),
            SupersededCount: requests.Count(x => x.Status == IssuerConstants.StatusSuperseded),
            RejectedCount: requests.Count(x => x.Status == IssuerConstants.StatusRejected),
            CustomerCount: ListCustomers().Count,
            StoreCount: ListStores().Count,
            Signer: signer);
    }

    private const string SelectRequestsSql = """
        SELECT r.id, r.license_id, r.idempotency_key, r.status, r.customer_id, c.customer_name,
               r.store_id, s.store_code, s.store_name, r.application_id, r.license_type, r.edition,
               r.machine_binding, r.features_json, r.allowed_lan_terminals, r.host_license_id,
               r.terminal_id, r.issued_at_utc, r.expires_at_utc, r.support_expires_at_utc,
               r.updates_until_utc, r.supersedes_request_id, r.canonical_signing_payload,
               r.signed_license_json, r.signed_license_sha256, r.signer_key_spki_sha256,
               r.signer_mode, r.verifier_passed, r.review_note, r.created_by, r.approved_by,
               r.issued_by, r.created_at_utc, r.approved_at_utc, r.issued_at_server_utc
        FROM issuer_license_requests r
        INNER JOIN issuer_customers c ON c.id = r.customer_id
        INNER JOIN issuer_stores s ON s.id = r.store_id
        """;

    private static IssuerLicenseRequestRecord? FindRequestByIdInTx(SqliteConnection conn, SqliteTransaction tx, Guid id)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = SelectRequestsSql + " WHERE r.id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", id.ToString("D"));
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadRequest(r) : null;
    }

    private static IssuerLicenseRequestRecord? FindRequestByIdempotencyKeyInTx(SqliteConnection conn, SqliteTransaction tx, string key)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = SelectRequestsSql + " WHERE r.idempotency_key = $k LIMIT 1;";
        cmd.Parameters.AddWithValue("$k", key);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadRequest(r) : null;
    }

    private static IssuerLicenseRequestRecord ReadRequest(SqliteDataReader r) => new(
        Id: Guid.Parse(r.GetString(0)),
        LicenseId: r.GetString(1),
        IdempotencyKey: r.IsDBNull(2) ? null : r.GetString(2),
        Status: r.GetString(3),
        CustomerId: Guid.Parse(r.GetString(4)),
        CustomerName: r.GetString(5),
        StoreId: Guid.Parse(r.GetString(6)),
        StoreCode: r.GetString(7),
        StoreName: r.GetString(8),
        ApplicationId: r.GetString(9),
        LicenseType: Enum.Parse<LicenseType>(r.GetString(10), ignoreCase: true),
        Edition: Enum.Parse<LicenseEdition>(r.GetString(11), ignoreCase: true),
        MachineBinding: r.GetString(12),
        Features: JsonSerializer.Deserialize<string[]>(r.GetString(13)) ?? Array.Empty<string>(),
        AllowedLanTerminals: r.IsDBNull(14) ? null : r.GetInt32(14),
        HostLicenseId: r.IsDBNull(15) ? null : r.GetString(15),
        TerminalId: r.IsDBNull(16) ? null : r.GetString(16),
        IssuedAtUtc: DateTimeOffset.Parse(r.GetString(17)),
        ExpiresAtUtc: r.IsDBNull(18) ? null : DateTimeOffset.Parse(r.GetString(18)),
        SupportExpiresAtUtc: r.IsDBNull(19) ? null : DateTimeOffset.Parse(r.GetString(19)),
        UpdatesUntilUtc: r.IsDBNull(20) ? null : DateTimeOffset.Parse(r.GetString(20)),
        SupersedesRequestId: r.IsDBNull(21) ? null : Guid.Parse(r.GetString(21)),
        CanonicalSigningPayload: r.GetString(22),
        SignedLicenseJson: r.IsDBNull(23) ? null : r.GetString(23),
        SignedLicenseSha256: r.IsDBNull(24) ? null : r.GetString(24),
        SignerKeySpkiSha256: r.IsDBNull(25) ? null : r.GetString(25),
        SignerMode: r.IsDBNull(26) ? null : r.GetString(26),
        VerifierPassed: r.GetInt64(27) == 1,
        ReviewNote: r.IsDBNull(28) ? null : r.GetString(28),
        CreatedBy: r.GetString(29),
        ApprovedBy: r.IsDBNull(30) ? null : r.GetString(30),
        IssuedBy: r.IsDBNull(31) ? null : r.GetString(31),
        CreatedAtUtc: DateTimeOffset.Parse(r.GetString(32)),
        ApprovedAtUtc: r.IsDBNull(33) ? null : DateTimeOffset.Parse(r.GetString(33)),
        IssuedAtServerUtc: r.IsDBNull(34) ? null : DateTimeOffset.Parse(r.GetString(34)));

    private static void AppendAuditInTx(SqliteConnection conn, SqliteTransaction tx, string actor, string role, string action, string entityType, string entityId, object detail)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO issuer_audit_log(actor_username, actor_role, action, entity_type, entity_id, detail_json, created_at_utc)
            VALUES($actor, $role, $action, $etype, $eid, $detail, $at);
            """;
        cmd.Parameters.AddWithValue("$actor", actor);
        cmd.Parameters.AddWithValue("$role", role);
        cmd.Parameters.AddWithValue("$action", action);
        cmd.Parameters.AddWithValue("$etype", entityType);
        cmd.Parameters.AddWithValue("$eid", entityId);
        cmd.Parameters.AddWithValue("$detail", JsonSerializer.Serialize(detail));
        cmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
