using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Salryn.Mobile.Foundation.Printing;
using Salryn.Mobile.Foundation.Security;
using Salryn.Mobile.Foundation.Sync;
using Salryn.Sync.Contracts;

namespace Salryn.Mobile.Runtime;

public sealed record MobileOperatorPrincipal(
    Guid OperatorId,
    string Username,
    string DisplayName,
    string Role);

public sealed record MobileMenuItemDto(
    Guid ProductId,
    string Name,
    string Category,
    long PriceCents,
    bool IsAvailable);

public sealed record MobileOrderLineInput(
    Guid ProductId,
    string ItemName,
    int Quantity,
    long UnitPriceCents,
    IReadOnlyList<string>? Modifiers = null);

public sealed record MobileOrderResult(
    bool IsSuccess,
    Guid OrderId,
    string? OrderNo,
    string? Status,
    long TotalCents,
    Guid? KitchenPrintJobId,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record MobileCheckoutRequest(
    Guid OperationId,
    Guid ShiftId,
    Guid OrderId,
    string TenderMode,
    long CashCents,
    long ArCents,
    Guid? CustomerId = null);

public sealed record MobileCheckoutResult(
    bool IsSuccess,
    bool IsIdempotentReplay,
    Guid SaleId,
    string? ReceiptNo,
    long TotalCents,
    Guid? ReceiptPrintJobId,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record MobileRefundResult(
    bool IsSuccess,
    bool IsIdempotentReplay,
    Guid RefundId,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record MobileBlindShiftCloseResult(
    bool IsSuccess,
    Guid ShiftId,
    long CountedCashCents,
    long VarianceCents,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record MobileSyncCycleResult(
    bool IsSuccess,
    bool IsRevokedFenced,
    int PushedCount,
    int QuarantinedCount,
    int PulledCount,
    long ServerEpoch,
    string? ErrorCode,
    string? ErrorMessage);

/// <summary>
/// Offline-first Salryn Cafe Android Cashier Engine:
/// - Strict Cash / AR / Partial Cash+AR tender enforcement
/// - Durable client operationId + atomic SQLite outbox writes
/// - Operator PIN RBAC strictly separated from Device Trust + Offline Lease anti-rollback guard
/// - Separate Bluetooth ESC/POS Receipt & Kitchen station queue with audited reprint policy
/// </summary>
public sealed class MobileCashierEngine
{
    public const string TenderCash = "Cash";
    public const string TenderAr = "AR";
    public const string TenderCashAndAr = "CashAndAR";

    private static readonly HashSet<string> AllowedTenders = new(StringComparer.Ordinal)
    {
        TenderCash,
        TenderAr,
        TenderCashAndAr
    };

    private static readonly HashSet<string> AllowedOrderTypes = new(StringComparer.Ordinal)
    {
        "DineIn",
        "Takeout",
        "Delivery"
    };

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly CafeMobileDatabase _db;
    private readonly MobileOfflineLeaseGuard _leaseGuard;
    private readonly IBluetoothPrinterTransport _printerTransport;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<(long ElapsedMs, int BootCount)> _clockSource;

    public MobileCashierEngine(
        CafeMobileDatabase db,
        MobileOfflineLeaseGuard leaseGuard,
        IBluetoothPrinterTransport printerTransport,
        Func<DateTimeOffset>? utcNow = null,
        Func<(long ElapsedMs, int BootCount)>? clockSource = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _leaseGuard = leaseGuard ?? throw new ArgumentNullException(nameof(leaseGuard));
        _printerTransport = printerTransport ?? throw new ArgumentNullException(nameof(printerTransport));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        // Platform callers must supply a real elapsed clock and persistent boot counter.
        _clockSource = clockSource ?? (() => (-1L, -1));
    }

    public void ConfigureDeviceBinding(
        Guid deviceId,
        Guid tenantId,
        Guid storeId,
        long serverEpoch,
        MobileSignedOfflineLease? lease,
        string status = "Active")
    {
        using SqliteConnection conn = _db.OpenConnection();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO mobile_device_state (
                singleton_id, product_id, device_id, tenant_id, store_id,
                server_epoch, pull_cursor, status, offline_lease_json, updated_at_utc)
            VALUES (1, $prod, $dev, $ten, $store, $epoch, 0, $status, $lease, $now)
            ON CONFLICT(singleton_id) DO UPDATE SET
                device_id = excluded.device_id,
                tenant_id = excluded.tenant_id,
                store_id = excluded.store_id,
                server_epoch = excluded.server_epoch,
                status = excluded.status,
                offline_lease_json = excluded.offline_lease_json,
                updated_at_utc = excluded.updated_at_utc;
            """;
        cmd.Parameters.AddWithValue("$prod", CafeMobileDatabase.CanonicalProductId);
        cmd.Parameters.AddWithValue("$dev", deviceId.ToString("D"));
        cmd.Parameters.AddWithValue("$ten", tenantId.ToString("D"));
        cmd.Parameters.AddWithValue("$store", storeId.ToString("D"));
        cmd.Parameters.AddWithValue("$epoch", serverEpoch);
        cmd.Parameters.AddWithValue("$status", status);
        cmd.Parameters.AddWithValue("$lease", lease is null ? DBNull.Value : JsonSerializer.Serialize(lease, JsonOpts));
        cmd.Parameters.AddWithValue("$now", _utcNow().ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void SeedOperator(Guid operatorId, string username, string displayName, string role, string pin)
    {
        using SqliteConnection conn = _db.OpenConnection();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO mobile_operators (operator_id, username, display_name, role, pin_hash, is_active)
            VALUES ($id, $user, $name, $role, $hash, 1);
            """;
        cmd.Parameters.AddWithValue("$id", operatorId.ToString("D"));
        cmd.Parameters.AddWithValue("$user", username.Trim().ToLowerInvariant());
        cmd.Parameters.AddWithValue("$name", displayName.Trim());
        cmd.Parameters.AddWithValue("$role", role);
        cmd.Parameters.AddWithValue("$hash", HashPin(pin));
        cmd.ExecuteNonQuery();
    }

    public MobileOperatorPrincipal? AuthenticateOperator(string username, string pin)
    {
        using SqliteConnection conn = _db.OpenConnection();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT operator_id, username, display_name, role, pin_hash, is_active
            FROM mobile_operators
            WHERE username = $user;
            """;
        cmd.Parameters.AddWithValue("$user", username.Trim().ToLowerInvariant());
        using SqliteDataReader r = cmd.ExecuteReader();
        if (!r.Read() || r.GetInt32(5) != 1)
        {
            return null;
        }

        if (!VerifyPin(pin, r.GetString(4)))
        {
            return null;
        }

        return new MobileOperatorPrincipal(Guid.Parse(r.GetString(0)), r.GetString(1), r.GetString(2), r.GetString(3));
    }

    public void SeedMenuItems(IEnumerable<MobileMenuItemDto> items)
    {
        using SqliteConnection conn = _db.OpenConnection();
        using SqliteTransaction tx = conn.BeginTransaction();
        foreach (MobileMenuItemDto item in items)
        {
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT OR REPLACE INTO mobile_menu_items (product_id, name, category, price_cents, is_available)
                VALUES ($id, $name, $cat, $price, $avail);
                """;
            cmd.Parameters.AddWithValue("$id", item.ProductId.ToString("D"));
            cmd.Parameters.AddWithValue("$name", item.Name);
            cmd.Parameters.AddWithValue("$cat", item.Category);
            cmd.Parameters.AddWithValue("$price", item.PriceCents);
            cmd.Parameters.AddWithValue("$avail", item.IsAvailable ? 1 : 0);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public IReadOnlyList<MobileMenuItemDto> GetMenuItems()
    {
        using SqliteConnection conn = _db.OpenConnection();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT product_id, name, category, price_cents, is_available FROM mobile_menu_items ORDER BY category, name;";
        List<MobileMenuItemDto> items = [];
        using SqliteDataReader r = cmd.ExecuteReader();
        while (r.Read())
        {
            items.Add(new MobileMenuItemDto(Guid.Parse(r.GetString(0)), r.GetString(1), r.GetString(2), r.GetInt64(3), r.GetInt32(4) == 1));
        }
        return items;
    }

    // ==================== PRINTER STATIONS (ISOLATED PERIPHERAL REGISTRY) ====================

    public void ConfigurePrinterStation(string stationKey, string printerTargetId, string printerName)
    {
        if (stationKey != "Receipt" && stationKey != "Kitchen")
        {
            throw new ArgumentException("StationKey must be 'Receipt' or 'Kitchen'.", nameof(stationKey));
        }

        using SqliteConnection conn = _db.OpenConnection();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO mobile_print_stations (station_key, printer_target_id, printer_name, updated_at_utc)
            VALUES ($key, $target, $name, $now);
            """;
        cmd.Parameters.AddWithValue("$key", stationKey);
        cmd.Parameters.AddWithValue("$target", printerTargetId.Trim());
        cmd.Parameters.AddWithValue("$name", printerName.Trim());
        cmd.Parameters.AddWithValue("$now", _utcNow().ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public async Task<int> FlushPrintQueueAsync(CancellationToken ct = default)
    {
        int printed = 0;
        using SqliteConnection conn = _db.OpenConnection();

        List<(string JobId, string StationKey, byte[] Payload)> pending = [];
        using (SqliteCommand q = conn.CreateCommand())
        {
            q.CommandText = "SELECT job_id, station_key, payload_base64 FROM mobile_print_jobs WHERE status IN ('Queued', 'Failed') ORDER BY created_at_utc ASC;";
            using SqliteDataReader r = q.ExecuteReader();
            while (r.Read())
            {
                pending.Add((r.GetString(0), r.GetString(1), Convert.FromBase64String(r.GetString(2))));
            }
        }

        foreach (var job in pending)
        {
            string? target = ReadStationTarget(conn, job.StationKey);
            if (string.IsNullOrWhiteSpace(target))
            {
                UpdatePrintJobStatus(conn, job.JobId, "Failed", $"No printer configured for station '{job.StationKey}'.", null);
                continue;
            }

            PrinterTransportResult res = await _printerTransport.SendAsync(target, job.Payload, ct).ConfigureAwait(false);
            if (res.Accepted)
            {
                UpdatePrintJobStatus(conn, job.JobId, "Printed", null, _utcNow());
                printed++;
            }
            else
            {
                UpdatePrintJobStatus(conn, job.JobId, "Failed", $"{res.Code}: {res.Message}", null);
            }
        }

        return printed;
    }

    // ==================== SHIFTS, ORDERS, CHECKOUT & REFUNDS ====================

    public async Task<(bool IsSuccess, Guid ShiftId, string? ErrorCode, string? ErrorMessage)> OpenShiftAsync(
        MobileOperatorPrincipal actor,
        long openingCashCents,
        CancellationToken ct = default)
    {
        LeaseValidationResult leaseCheck = await ValidateActiveLeaseAsync(ct).ConfigureAwait(false);
        if (!leaseCheck.IsAuthorized)
        {
            return (false, Guid.Empty, leaseCheck.Code, leaseCheck.Message);
        }

        if (openingCashCents < 0)
        {
            return (false, Guid.Empty, "ERR_NEGATIVE_CASH", "Opening cash cannot be negative.");
        }

        Guid shiftId = Guid.CreateVersion7();
        DateTimeOffset now = _utcNow();

        using SqliteConnection conn = _db.OpenConnection();
        using SqliteTransaction tx = conn.BeginTransaction();
        using (SqliteCommand cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO mobile_shifts (shift_id, operator_id, opened_at_utc, opening_cash_cents, status)
                VALUES ($id, $op, $now, $open, 'Open');
                """;
            cmd.Parameters.AddWithValue("$id", shiftId.ToString("D"));
            cmd.Parameters.AddWithValue("$op", actor.OperatorId.ToString("D"));
            cmd.Parameters.AddWithValue("$now", now.ToString("O"));
            cmd.Parameters.AddWithValue("$open", openingCashCents);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return (true, shiftId, null, null);
    }

    public async Task<MobileOrderResult> CreateOrderAsync(
        MobileOperatorPrincipal actor,
        string orderType,
        IReadOnlyList<MobileOrderLineInput> lines,
        long discountCents = 0,
        string? notes = null,
        CancellationToken ct = default)
    {
        LeaseValidationResult leaseCheck = await ValidateActiveLeaseAsync(ct).ConfigureAwait(false);
        if (!leaseCheck.IsAuthorized)
        {
            return new MobileOrderResult(false, Guid.Empty, null, null, 0, null, leaseCheck.Code, leaseCheck.Message);
        }

        if (!AllowedOrderTypes.Contains(orderType))
        {
            return new MobileOrderResult(false, Guid.Empty, null, null, 0, null, "ERR_INVALID_ORDER_TYPE", "OrderType must be DineIn, Takeout, or Delivery.");
        }

        if (lines is null || lines.Count == 0 || discountCents < 0)
        {
            return new MobileOrderResult(false, Guid.Empty, null, null, 0, null, "ERR_INVALID_ORDER", "Order must contain at least one line and non-negative discount.");
        }

        long subtotal = lines.Sum(l => l.UnitPriceCents * l.Quantity);
        long total = Math.Max(0, subtotal - discountCents);
        Guid orderId = Guid.CreateVersion7();
        string orderNo = $"CY-{_utcNow():HHmmss}-{orderId.ToString("N")[^6..].ToUpperInvariant()}";
        DateTimeOffset now = _utcNow();

        using SqliteConnection conn = _db.OpenConnection();
        using SqliteTransaction tx = conn.BeginTransaction();

        using (SqliteCommand cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO mobile_orders (order_id, order_no, order_type, status, lines_json, discount_cents, total_cents, notes, created_at_utc, updated_at_utc)
                VALUES ($id, $no, $type, 'Pending', $lines, $disc, $total, $notes, $now, $now);
                """;
            cmd.Parameters.AddWithValue("$id", orderId.ToString("D"));
            cmd.Parameters.AddWithValue("$no", orderNo);
            cmd.Parameters.AddWithValue("$type", orderType);
            cmd.Parameters.AddWithValue("$lines", JsonSerializer.Serialize(lines, JsonOpts));
            cmd.Parameters.AddWithValue("$disc", discountCents);
            cmd.Parameters.AddWithValue("$total", total);
            cmd.Parameters.AddWithValue("$notes", notes is null ? DBNull.Value : notes);
            cmd.Parameters.AddWithValue("$now", now.ToString("O"));
            cmd.ExecuteNonQuery();
        }

        // Queue Kitchen ESC/POS Ticket automatically on the dedicated Kitchen station
        EscPosKitchenTicketDocument ticket = new(
            orderNo,
            orderType,
            now,
            lines.Select(l => new EscPosReceiptLine(l.ItemName, l.Quantity, l.UnitPriceCents * l.Quantity, l.Modifiers)).ToArray(),
            notes);
        byte[] ticketBytes = EscPosDocumentBuilder.BuildKitchenTicket(ticket);
        Guid kitchenJobId = EnqueuePrintJob(conn, tx, "Kitchen", "KitchenTicket", orderId.ToString("D"), 0, null, ticketBytes, now);

        tx.Commit();
        return new MobileOrderResult(true, orderId, orderNo, "Pending", total, kitchenJobId, null, null);
    }

    public async Task<MobileCheckoutResult> CompleteCheckoutAsync(
        MobileOperatorPrincipal actor,
        MobileCheckoutRequest request,
        CancellationToken ct = default)
    {
        if (request.OperationId == Guid.Empty)
        {
            return new MobileCheckoutResult(false, false, Guid.Empty, null, 0, null, "ERR_OPERATION_ID_REQUIRED", "Durable client operationId is required.");
        }

        if (!AllowedTenders.Contains(request.TenderMode))
        {
            return new MobileCheckoutResult(false, false, Guid.Empty, null, 0, null, "ERR_UNSUPPORTED_TENDER", "Only Cash, AR, and CashAndAR tenders are permitted in Salryn Cafe.");
        }

        LeaseValidationResult leaseCheck = await ValidateActiveLeaseAsync(ct).ConfigureAwait(false);
        if (!leaseCheck.IsAuthorized)
        {
            return new MobileCheckoutResult(false, false, Guid.Empty, null, 0, null, leaseCheck.Code, leaseCheck.Message);
        }

        DateTimeOffset now = _utcNow();
        using SqliteConnection conn = _db.OpenConnection();
        using SqliteTransaction tx = conn.BeginTransaction();

        // Durable operationId idempotency check
        using (SqliteCommand existing = conn.CreateCommand())
        {
            existing.Transaction = tx;
            existing.CommandText = "SELECT sale_id, receipt_no, total_cents FROM mobile_sales WHERE operation_id = $opId;";
            existing.Parameters.AddWithValue("$opId", request.OperationId.ToString("D"));
            using SqliteDataReader r = existing.ExecuteReader();
            if (r.Read())
            {
                return new MobileCheckoutResult(true, true, Guid.Parse(r.GetString(0)), r.GetString(1), r.GetInt64(2), null, null, null);
            }
        }

        // Verify shift is Open
        using (SqliteCommand shiftCmd = conn.CreateCommand())
        {
            shiftCmd.Transaction = tx;
            shiftCmd.CommandText = "SELECT status FROM mobile_shifts WHERE shift_id = $sid;";
            shiftCmd.Parameters.AddWithValue("$sid", request.ShiftId.ToString("D"));
            object? shiftStatus = shiftCmd.ExecuteScalar();
            if (shiftStatus is not string s || !string.Equals(s, "Open", StringComparison.Ordinal))
            {
                return new MobileCheckoutResult(false, false, Guid.Empty, null, 0, null, "ERR_SHIFT_NOT_OPEN", "An open cashier shift is required for checkout.");
            }
        }

        // Load order
        string orderType;
        string linesJson;
        long discountCents;
        long totalCents;
        using (SqliteCommand ordCmd = conn.CreateCommand())
        {
            ordCmd.Transaction = tx;
            ordCmd.CommandText = "SELECT order_type, lines_json, discount_cents, total_cents FROM mobile_orders WHERE order_id = $oid;";
            ordCmd.Parameters.AddWithValue("$oid", request.OrderId.ToString("D"));
            using SqliteDataReader r = ordCmd.ExecuteReader();
            if (!r.Read())
            {
                return new MobileCheckoutResult(false, false, Guid.Empty, null, 0, null, "ERR_ORDER_NOT_FOUND", "Order not found.");
            }
            orderType = r.GetString(0);
            linesJson = r.GetString(1);
            discountCents = r.GetInt64(2);
            totalCents = r.GetInt64(3);
        }

        // Enforce strict Cash / AR / Partial Cash+AR tender math
        if (request.CashCents < 0 || request.ArCents < 0 || request.CashCents + request.ArCents != totalCents)
        {
            return new MobileCheckoutResult(false, false, Guid.Empty, null, 0, null, "ERR_TENDER_MISMATCH", "CashCents + ArCents must exactly equal order TotalCents.");
        }

        if ((request.TenderMode == TenderAr || request.TenderMode == TenderCashAndAr) &&
            (request.ArCents <= 0 || request.CustomerId is null || request.CustomerId == Guid.Empty))
        {
            return new MobileCheckoutResult(false, false, Guid.Empty, null, 0, null, "ERR_AR_CUSTOMER_REQUIRED", "AR and partial Cash+AR sales require a valid CustomerId and ArCents > 0.");
        }

        if (request.TenderMode == TenderCash && request.ArCents != 0)
        {
            return new MobileCheckoutResult(false, false, Guid.Empty, null, 0, null, "ERR_TENDER_MISMATCH", "Cash-only sale cannot include ArCents.");
        }

        var device = ReadDeviceState(conn, tx)!;
        Guid saleId = Guid.CreateVersion7();
        string receiptNo = $"SC-{now:yyyyMMdd}-{saleId.ToString("N")[^8..].ToUpperInvariant()}";

        using (SqliteCommand insertSale = conn.CreateCommand())
        {
            insertSale.Transaction = tx;
            insertSale.CommandText = """
                INSERT INTO mobile_sales (
                    sale_id, operation_id, shift_id, order_id, receipt_no, tender_mode,
                    cash_cents, ar_cents, customer_id, discount_cents, total_cents, status, created_at_utc)
                VALUES ($sid, $opId, $shift, $ord, $rcpt, $tender, $cash, $ar, $cust, $disc, $total, 'Completed', $now);
                """;
            insertSale.Parameters.AddWithValue("$sid", saleId.ToString("D"));
            insertSale.Parameters.AddWithValue("$opId", request.OperationId.ToString("D"));
            insertSale.Parameters.AddWithValue("$shift", request.ShiftId.ToString("D"));
            insertSale.Parameters.AddWithValue("$ord", request.OrderId.ToString("D"));
            insertSale.Parameters.AddWithValue("$rcpt", receiptNo);
            insertSale.Parameters.AddWithValue("$tender", request.TenderMode);
            insertSale.Parameters.AddWithValue("$cash", request.CashCents);
            insertSale.Parameters.AddWithValue("$ar", request.ArCents);
            insertSale.Parameters.AddWithValue("$cust", request.CustomerId.HasValue ? request.CustomerId.Value.ToString("D") : DBNull.Value);
            insertSale.Parameters.AddWithValue("$disc", discountCents);
            insertSale.Parameters.AddWithValue("$total", totalCents);
            insertSale.Parameters.AddWithValue("$now", now.ToString("O"));
            insertSale.ExecuteNonQuery();
        }

        using (SqliteCommand updateOrder = conn.CreateCommand())
        {
            updateOrder.Transaction = tx;
            updateOrder.CommandText = "UPDATE mobile_orders SET status = 'Completed', updated_at_utc = $now WHERE order_id = $oid;";
            updateOrder.Parameters.AddWithValue("$now", now.ToString("O"));
            updateOrder.Parameters.AddWithValue("$oid", request.OrderId.ToString("D"));
            updateOrder.ExecuteNonQuery();
        }

        // Atomic durable outbox write using canonical SyncProtocolV1
        string payloadJson = JsonSerializer.Serialize(new
        {
            saleId,
            receiptNo,
            orderId = request.OrderId,
            shiftId = request.ShiftId,
            tenderMode = request.TenderMode,
            cashCents = request.CashCents,
            arCents = request.ArCents,
            customerId = request.CustomerId,
            discountCents,
            totalCents
        }, JsonOpts);
        EnqueueOutboxEnvelope(conn, tx, device.DeviceId, device.StoreId, actor.OperatorId, "cafe.sale.committed.v3", saleId, payloadJson, request.OperationId, now);

        // Queue Receipt ESC/POS job on dedicated Receipt station
        List<MobileOrderLineInput> parsedLines = JsonSerializer.Deserialize<List<MobileOrderLineInput>>(linesJson, JsonOpts) ?? [];
        EscPosReceiptDocument receipt = new(
            "Cafe Yza",
            receiptNo,
            orderType,
            now,
            actor.DisplayName,
            parsedLines.Select(l => new EscPosReceiptLine(l.ItemName, l.Quantity, l.UnitPriceCents * l.Quantity, l.Modifiers)).ToArray(),
            totalCents + discountCents,
            discountCents,
            totalCents,
            request.TenderMode,
            request.CashCents,
            request.ArCents);
        Guid receiptJobId = EnqueuePrintJob(conn, tx, "Receipt", "Receipt", saleId.ToString("D"), 0, null, EscPosDocumentBuilder.BuildReceipt(receipt), now);

        tx.Commit();
        return new MobileCheckoutResult(true, false, saleId, receiptNo, totalCents, receiptJobId, null, null);
    }

    public async Task<MobileRefundResult> IssueAuditedRefundAsync(
        MobileOperatorPrincipal actor,
        Guid operationId,
        Guid saleId,
        string reason,
        CancellationToken ct = default)
    {
        if (!string.Equals(actor.Role, "Manager", StringComparison.Ordinal) &&
            !string.Equals(actor.Role, "Owner", StringComparison.Ordinal))
        {
            return new MobileRefundResult(false, false, Guid.Empty, "ERR_FORBIDDEN_ROLE", "Only Manager or Owner can issue audited refunds.");
        }

        if (operationId == Guid.Empty || string.IsNullOrWhiteSpace(reason))
        {
            return new MobileRefundResult(false, false, Guid.Empty, "ERR_INVALID_REFUND", "Durable operationId and audit reason are required.");
        }

        LeaseValidationResult leaseCheck = await ValidateActiveLeaseAsync(ct).ConfigureAwait(false);
        if (!leaseCheck.IsAuthorized)
        {
            return new MobileRefundResult(false, false, Guid.Empty, leaseCheck.Code, leaseCheck.Message);
        }

        DateTimeOffset now = _utcNow();
        using SqliteConnection conn = _db.OpenConnection();
        using SqliteTransaction tx = conn.BeginTransaction();

        using (SqliteCommand existing = conn.CreateCommand())
        {
            existing.Transaction = tx;
            existing.CommandText = "SELECT refund_id FROM mobile_refunds WHERE operation_id = $opId;";
            existing.Parameters.AddWithValue("$opId", operationId.ToString("D"));
            object? exId = existing.ExecuteScalar();
            if (exId is string s)
            {
                return new MobileRefundResult(true, true, Guid.Parse(s), null, null);
            }
        }

        long amountCents;
        using (SqliteCommand saleCmd = conn.CreateCommand())
        {
            saleCmd.Transaction = tx;
            saleCmd.CommandText = "SELECT total_cents, status FROM mobile_sales WHERE sale_id = $sid;";
            saleCmd.Parameters.AddWithValue("$sid", saleId.ToString("D"));
            using SqliteDataReader r = saleCmd.ExecuteReader();
            if (!r.Read())
            {
                return new MobileRefundResult(false, false, Guid.Empty, "ERR_SALE_NOT_FOUND", "Sale not found.");
            }
            if (string.Equals(r.GetString(1), "Refunded", StringComparison.Ordinal))
            {
                return new MobileRefundResult(false, false, Guid.Empty, "ERR_ALREADY_REFUNDED", "Sale has already been refunded.");
            }
            amountCents = r.GetInt64(0);
        }

        Guid refundId = Guid.CreateVersion7();
        using (SqliteCommand ins = conn.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText = """
                INSERT INTO mobile_refunds (refund_id, operation_id, sale_id, actor_user_id, reason, amount_cents, created_at_utc)
                VALUES ($rid, $opId, $sid, $actor, $reason, $amt, $now);
                """;
            ins.Parameters.AddWithValue("$rid", refundId.ToString("D"));
            ins.Parameters.AddWithValue("$opId", operationId.ToString("D"));
            ins.Parameters.AddWithValue("$sid", saleId.ToString("D"));
            ins.Parameters.AddWithValue("$actor", actor.OperatorId.ToString("D"));
            ins.Parameters.AddWithValue("$reason", reason.Trim());
            ins.Parameters.AddWithValue("$amt", amountCents);
            ins.Parameters.AddWithValue("$now", now.ToString("O"));
            ins.ExecuteNonQuery();
        }

        using (SqliteCommand upd = conn.CreateCommand())
        {
            upd.Transaction = tx;
            upd.CommandText = "UPDATE mobile_sales SET status = 'Refunded' WHERE sale_id = $sid;";
            upd.Parameters.AddWithValue("$sid", saleId.ToString("D"));
            upd.ExecuteNonQuery();
        }

        var device = ReadDeviceState(conn, tx)!;
        string payload = JsonSerializer.Serialize(new { refundId, saleId, amountCents, reason = reason.Trim() }, JsonOpts);
        EnqueueOutboxEnvelope(conn, tx, device.DeviceId, device.StoreId, actor.OperatorId, "cafe.refund.committed.v1", refundId, payload, operationId, now);

        tx.Commit();
        return new MobileRefundResult(true, false, refundId, null, null);
    }

    public async Task<MobileBlindShiftCloseResult> CloseShiftBlindAsync(
        MobileOperatorPrincipal actor,
        Guid operationId,
        Guid shiftId,
        long countedCashCents,
        string? note = null,
        CancellationToken ct = default)
    {
        LeaseValidationResult leaseCheck = await ValidateActiveLeaseAsync(ct).ConfigureAwait(false);
        if (!leaseCheck.IsAuthorized)
        {
            return new MobileBlindShiftCloseResult(false, shiftId, countedCashCents, 0, leaseCheck.Code, leaseCheck.Message);
        }

        if (countedCashCents < 0 || operationId == Guid.Empty)
        {
            return new MobileBlindShiftCloseResult(false, shiftId, countedCashCents, 0, "ERR_INVALID_SHIFT_CLOSE", "Counted cash must be >= 0 and operationId required.");
        }

        DateTimeOffset now = _utcNow();
        using SqliteConnection conn = _db.OpenConnection();
        using SqliteTransaction tx = conn.BeginTransaction();

        long openingCash;
        using (SqliteCommand sCmd = conn.CreateCommand())
        {
            sCmd.Transaction = tx;
            sCmd.CommandText = "SELECT opening_cash_cents, status FROM mobile_shifts WHERE shift_id = $sid;";
            sCmd.Parameters.AddWithValue("$sid", shiftId.ToString("D"));
            using SqliteDataReader r = sCmd.ExecuteReader();
            if (!r.Read() || r.GetString(1) != "Open")
            {
                return new MobileBlindShiftCloseResult(false, shiftId, countedCashCents, 0, "ERR_SHIFT_NOT_OPEN", "Shift is not open.");
            }
            openingCash = r.GetInt64(0);
        }

        long cashSales;
        using (SqliteCommand cCmd = conn.CreateCommand())
        {
            cCmd.Transaction = tx;
            cCmd.CommandText = "SELECT COALESCE(SUM(cash_cents), 0) FROM mobile_sales WHERE shift_id = $sid AND status = 'Completed';";
            cCmd.Parameters.AddWithValue("$sid", shiftId.ToString("D"));
            cashSales = Convert.ToInt64(cCmd.ExecuteScalar());
        }

        long expectedCash = openingCash + cashSales;
        long variance = countedCashCents - expectedCash;

        using (SqliteCommand upd = conn.CreateCommand())
        {
            upd.Transaction = tx;
            upd.CommandText = """
                UPDATE mobile_shifts
                SET status = 'Closed',
                    counted_cash_cents = $counted,
                    expected_cash_cents = $expected,
                    variance_cents = $variance,
                    close_note = $note,
                    closed_at_utc = $now
                WHERE shift_id = $sid;
                """;
            upd.Parameters.AddWithValue("$counted", countedCashCents);
            upd.Parameters.AddWithValue("$expected", expectedCash);
            upd.Parameters.AddWithValue("$variance", variance);
            upd.Parameters.AddWithValue("$note", note is null ? DBNull.Value : note);
            upd.Parameters.AddWithValue("$now", now.ToString("O"));
            upd.Parameters.AddWithValue("$sid", shiftId.ToString("D"));
            upd.ExecuteNonQuery();
        }

        var device = ReadDeviceState(conn, tx)!;
        string payload = JsonSerializer.Serialize(new { shiftId, countedCashCents, expectedCashCents = expectedCash, varianceCents = variance, note }, JsonOpts);
        EnqueueOutboxEnvelope(conn, tx, device.DeviceId, device.StoreId, actor.OperatorId, "cafe.shift.closed.v1", shiftId, payload, operationId, now);

        tx.Commit();
        return new MobileBlindShiftCloseResult(true, shiftId, countedCashCents, variance, null, null);
    }

    public Guid QueueAuditedReceiptReprint(MobileOperatorPrincipal actor, Guid saleId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Audited reprint requires an explicit reason.", nameof(reason));
        }

        DateTimeOffset now = _utcNow();
        using SqliteConnection conn = _db.OpenConnection();
        using SqliteTransaction tx = conn.BeginTransaction();

        string receiptNo;
        string tenderMode;
        long cashCents;
        long arCents;
        long discountCents;
        long totalCents;
        string orderType;
        string linesJson;

        using (SqliteCommand cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                SELECT s.receipt_no, s.tender_mode, s.cash_cents, s.ar_cents, s.discount_cents, s.total_cents,
                       o.order_type, o.lines_json
                FROM mobile_sales s
                INNER JOIN mobile_orders o ON o.order_id = s.order_id
                WHERE s.sale_id = $sid;
                """;
            cmd.Parameters.AddWithValue("$sid", saleId.ToString("D"));
            using SqliteDataReader r = cmd.ExecuteReader();
            if (!r.Read())
            {
                throw new InvalidOperationException("Sale not found for reprint.");
            }
            receiptNo = r.GetString(0);
            tenderMode = r.GetString(1);
            cashCents = r.GetInt64(2);
            arCents = r.GetInt64(3);
            discountCents = r.GetInt64(4);
            totalCents = r.GetInt64(5);
            orderType = r.GetString(6);
            linesJson = r.GetString(7);
        }

        int nextReprint;
        using (SqliteCommand cnt = conn.CreateCommand())
        {
            cnt.Transaction = tx;
            cnt.CommandText = "SELECT COALESCE(MAX(reprint_count), 0) + 1 FROM mobile_print_jobs WHERE reference_id = $ref AND document_type = 'Receipt';";
            cnt.Parameters.AddWithValue("$ref", saleId.ToString("D"));
            nextReprint = Convert.ToInt32(cnt.ExecuteScalar());
        }

        List<MobileOrderLineInput> parsedLines = JsonSerializer.Deserialize<List<MobileOrderLineInput>>(linesJson, JsonOpts) ?? [];
        EscPosReceiptDocument doc = new(
            "Cafe Yza",
            receiptNo,
            orderType,
            now,
            actor.DisplayName,
            parsedLines.Select(l => new EscPosReceiptLine(l.ItemName, l.Quantity, l.UnitPriceCents * l.Quantity, l.Modifiers)).ToArray(),
            totalCents + discountCents,
            discountCents,
            totalCents,
            tenderMode,
            cashCents,
            arCents,
            ReprintNumber: nextReprint,
            ReprintReason: reason.Trim());

        Guid jobId = EnqueuePrintJob(conn, tx, "Receipt", "Receipt", saleId.ToString("D"), nextReprint, reason.Trim(), EscPosDocumentBuilder.BuildReceipt(doc), now);
        tx.Commit();
        return jobId;
    }

    // ==================== SYNC COORDINATOR ====================

    public async Task<MobileSyncCycleResult> SyncNowAsync(HttpSyncOutboxTransport transport, CancellationToken ct = default)
    {
        using SqliteConnection conn = _db.OpenConnection();
        DeviceStateRow? state = ReadDeviceState(conn, null);
        if (state is null)
        {
            return new MobileSyncCycleResult(false, false, 0, 0, 0, 0, "ERR_UNPAIRED", "Device is not paired.");
        }

        List<SyncOperationEnvelopeV1> pending = [];
        using (SqliteCommand cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT envelope_json FROM mobile_sync_outbox WHERE status = 'PendingUpload' ORDER BY local_sequence ASC;";
            using SqliteDataReader r = cmd.ExecuteReader();
            while (r.Read())
            {
                pending.Add(JsonSerializer.Deserialize<SyncOperationEnvelopeV1>(r.GetString(0), JsonOpts)!);
            }
        }

        int pushed = 0;
        int quarantined = 0;
        long currentEpoch = state.ServerEpoch;

        if (pending.Count > 0)
        {
            MobilePushOutcome pushRes = await transport.PushBatchAsync(pending, ct).ConfigureAwait(false);
            if (pushRes.IsRevokedFenced)
            {
                DateTimeOffset now = _utcNow();
                var clk = _clockSource();
                await _leaseGuard.MarkRevokedFencedAsync(now, clk.ElapsedMs, clk.BootCount, ct).ConfigureAwait(false);
                MarkOutboxStatuses(conn, pushRes.QuarantinedOperationIds, "QuarantinedFenced", pushRes.ErrorCode);
                UpdateDeviceStatus(conn, "RevokedFenced", pushRes.ServerEpoch ?? currentEpoch);
                return new MobileSyncCycleResult(false, true, 0, pushRes.QuarantinedOperationIds.Count, 0, pushRes.ServerEpoch ?? currentEpoch, "DEVICE_REVOKED_FENCED", pushRes.ErrorMessage);
            }

            if (pushRes.RequiresRebootstrap)
            {
                MobileBootstrapOutcome boot = await transport.BootstrapAsync(ct).ConfigureAwait(false);
                if (!boot.IsSuccess)
                {
                    return new MobileSyncCycleResult(false, false, 0, 0, 0, currentEpoch, boot.ErrorCode, boot.ErrorMessage);
                }
                currentEpoch = boot.ServerEpoch;
                UpdateDeviceEpochAndCursor(conn, currentEpoch, boot.HeadSequence);
                return new MobileSyncCycleResult(false, false, 0, 0, 0, currentEpoch, "EPOCH_REBOOTSTRAPPED_RETRY", "Re-bootstrapped to new server epoch; retry sync.");
            }

            if (!pushRes.IsSuccess)
            {
                return new MobileSyncCycleResult(false, false, 0, 0, 0, currentEpoch, pushRes.ErrorCode, pushRes.ErrorMessage);
            }

            MarkOutboxStatuses(conn, pushRes.AppliedOperationIds, "Applied", null);
            pushed = pushRes.AppliedOperationIds.Count;
            currentEpoch = pushRes.ServerEpoch ?? currentEpoch;
        }

        MobilePullOutcome pullRes = await transport.PullPageAsync(state.PullCursor, 50, ct).ConfigureAwait(false);
        if (pullRes.IsRevoked)
        {
            var clk = _clockSource();
            await _leaseGuard.MarkRevokedFencedAsync(_utcNow(), clk.ElapsedMs, clk.BootCount, ct).ConfigureAwait(false);
            UpdateDeviceStatus(conn, "RevokedFenced", currentEpoch);
            return new MobileSyncCycleResult(false, true, pushed, quarantined, 0, currentEpoch, "DEVICE_REVOKED", pullRes.ErrorMessage);
        }

        if (!pullRes.IsSuccess || pullRes.Page is null)
        {
            return new MobileSyncCycleResult(false, false, pushed, quarantined, 0, currentEpoch, pullRes.ErrorCode, pullRes.ErrorMessage);
        }

        long nextCursor = pullRes.Page.Changes.Count > 0
            ? pullRes.Page.Changes[^1].ServerSequence
            : state.PullCursor;
        UpdateDeviceEpochAndCursor(conn, pullRes.ServerEpoch ?? currentEpoch, nextCursor);

        return new MobileSyncCycleResult(true, false, pushed, quarantined, pullRes.Page.Changes.Count, pullRes.ServerEpoch ?? currentEpoch, null, null);
    }

    public int GetPendingOutboxCount()
    {
        using SqliteConnection conn = _db.OpenConnection();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM mobile_sync_outbox WHERE status = 'PendingUpload';";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    // ==================== HELPERS ====================

    private async Task<LeaseValidationResult> ValidateActiveLeaseAsync(CancellationToken ct)
    {
        using SqliteConnection conn = _db.OpenConnection();
        DeviceStateRow? state = ReadDeviceState(conn, null);
        if (state is null)
        {
            return LeaseValidationResult.Denied("ERR_UNPAIRED", "Device must be paired before performing cashier mutations.");
        }

        if (string.Equals(state.Status, "RevokedFenced", StringComparison.Ordinal))
        {
            return LeaseValidationResult.Denied("DEVICE_REVOKED_FENCED", "Device is revoked and fenced.");
        }

        MobileSignedOfflineLease? lease = string.IsNullOrWhiteSpace(state.OfflineLeaseJson)
            ? null
            : JsonSerializer.Deserialize<MobileSignedOfflineLease>(state.OfflineLeaseJson, JsonOpts);

        var clk = _clockSource();
        return await _leaseGuard.EvaluateAndAdvanceAsync(
            lease, state.DeviceId, state.TenantId, state.StoreId,
            _utcNow(), clk.ElapsedMs, clk.BootCount, ct).ConfigureAwait(false);
    }

    private static void EnqueueOutboxEnvelope(
        SqliteConnection conn,
        SqliteTransaction tx,
        Guid deviceId,
        Guid storeId,
        Guid actorUserId,
        string opType,
        Guid aggregateId,
        string payloadJson,
        Guid operationId,
        DateTimeOffset now)
    {
        long nextSeq;
        using (SqliteCommand seqCmd = conn.CreateCommand())
        {
            seqCmd.Transaction = tx;
            seqCmd.CommandText = "SELECT COALESCE(MAX(local_sequence), 0) + 1 FROM mobile_sync_outbox;";
            nextSeq = Convert.ToInt64(seqCmd.ExecuteScalar());
        }

        SyncOperationEnvelopeV1 envelope = SyncProtocolV1.CreateEnvelope(
            deviceId, storeId, actorUserId, nextSeq, opType, 1, aggregateId, null, now, payloadJson, operationId);

        using SqliteCommand ins = conn.CreateCommand();
        ins.Transaction = tx;
        ins.CommandText = """
            INSERT INTO mobile_sync_outbox (local_sequence, operation_id, operation_type, envelope_json, status, created_at_utc, updated_at_utc)
            VALUES ($seq, $opId, $type, $json, 'PendingUpload', $now, $now);
            """;
        ins.Parameters.AddWithValue("$seq", nextSeq);
        ins.Parameters.AddWithValue("$opId", operationId.ToString("D"));
        ins.Parameters.AddWithValue("$type", opType);
        ins.Parameters.AddWithValue("$json", JsonSerializer.Serialize(envelope, JsonOpts));
        ins.Parameters.AddWithValue("$now", now.ToString("O"));
        ins.ExecuteNonQuery();
    }

    private static Guid EnqueuePrintJob(
        SqliteConnection conn,
        SqliteTransaction tx,
        string stationKey,
        string docType,
        string refId,
        int reprintCount,
        string? reprintReason,
        byte[] payload,
        DateTimeOffset now)
    {
        Guid jobId = Guid.CreateVersion7();
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO mobile_print_jobs (job_id, station_key, document_type, reference_id, reprint_count, reprint_reason, payload_base64, status, created_at_utc)
            VALUES ($id, $station, $doc, $ref, $rep, $reason, $b64, 'Queued', $now);
            """;
        cmd.Parameters.AddWithValue("$id", jobId.ToString("D"));
        cmd.Parameters.AddWithValue("$station", stationKey);
        cmd.Parameters.AddWithValue("$doc", docType);
        cmd.Parameters.AddWithValue("$ref", refId);
        cmd.Parameters.AddWithValue("$rep", reprintCount);
        cmd.Parameters.AddWithValue("$reason", reprintReason is null ? DBNull.Value : reprintReason);
        cmd.Parameters.AddWithValue("$b64", Convert.ToBase64String(payload));
        cmd.Parameters.AddWithValue("$now", now.ToString("O"));
        cmd.ExecuteNonQuery();
        return jobId;
    }

    private static string? ReadStationTarget(SqliteConnection conn, string stationKey)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT printer_target_id FROM mobile_print_stations WHERE station_key = $k;";
        cmd.Parameters.AddWithValue("$k", stationKey);
        return cmd.ExecuteScalar() as string;
    }

    private static void UpdatePrintJobStatus(SqliteConnection conn, string jobId, string status, string? err, DateTimeOffset? printedAt)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE mobile_print_jobs SET status = $st, last_error = $err, printed_at_utc = $at WHERE job_id = $id;";
        cmd.Parameters.AddWithValue("$st", status);
        cmd.Parameters.AddWithValue("$err", err is null ? DBNull.Value : err);
        cmd.Parameters.AddWithValue("$at", printedAt.HasValue ? printedAt.Value.ToString("O") : DBNull.Value);
        cmd.Parameters.AddWithValue("$id", jobId);
        cmd.ExecuteNonQuery();
    }

    private void MarkOutboxStatuses(SqliteConnection conn, IReadOnlyList<Guid> opIds, string status, string? error)
    {
        string now = _utcNow().ToString("O");
        foreach (Guid id in opIds)
        {
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE mobile_sync_outbox SET status = $st, last_error = $err, updated_at_utc = $now WHERE operation_id = $opId;";
            cmd.Parameters.AddWithValue("$st", status);
            cmd.Parameters.AddWithValue("$err", error is null ? DBNull.Value : error);
            cmd.Parameters.AddWithValue("$now", now);
            cmd.Parameters.AddWithValue("$opId", id.ToString("D"));
            cmd.ExecuteNonQuery();
        }
    }

    private void UpdateDeviceStatus(SqliteConnection conn, string status, long epoch)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE mobile_device_state SET status = $st, server_epoch = $ep, updated_at_utc = $now WHERE singleton_id = 1;";
        cmd.Parameters.AddWithValue("$st", status);
        cmd.Parameters.AddWithValue("$ep", epoch);
        cmd.Parameters.AddWithValue("$now", _utcNow().ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private void UpdateDeviceEpochAndCursor(SqliteConnection conn, long epoch, long cursor)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE mobile_device_state SET server_epoch = $ep, pull_cursor = $cur, updated_at_utc = $now WHERE singleton_id = 1;";
        cmd.Parameters.AddWithValue("$ep", epoch);
        cmd.Parameters.AddWithValue("$cur", cursor);
        cmd.Parameters.AddWithValue("$now", _utcNow().ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private static DeviceStateRow? ReadDeviceState(SqliteConnection conn, SqliteTransaction? tx)
    {
        using SqliteCommand cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT device_id, tenant_id, store_id, server_epoch, pull_cursor, status, offline_lease_json FROM mobile_device_state WHERE singleton_id = 1;";
        using SqliteDataReader r = cmd.ExecuteReader();
        if (!r.Read())
        {
            return null;
        }
        return new DeviceStateRow(
            Guid.Parse(r.GetString(0)),
            Guid.Parse(r.GetString(1)),
            Guid.Parse(r.GetString(2)),
            r.GetInt64(3),
            r.GetInt64(4),
            r.GetString(5),
            r.IsDBNull(6) ? null : r.GetString(6));
    }

    private static string HashPin(string pin)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(pin), salt, 60_000, HashAlgorithmName.SHA256, 32);
        return $"{Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPin(string pin, string stored)
    {
        string[] parts = stored.Split('$');
        if (parts.Length != 2) return false;
        byte[] salt = Convert.FromBase64String(parts[0]);
        byte[] expected = Convert.FromBase64String(parts[1]);
        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(pin), salt, 60_000, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private sealed record DeviceStateRow(
        Guid DeviceId,
        Guid TenantId,
        Guid StoreId,
        long ServerEpoch,
        long PullCursor,
        string Status,
        string? OfflineLeaseJson);
}
