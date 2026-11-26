using System.Security.Cryptography;
using System.Text;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Security;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;
using Salryn.Mobile.Runtime;

namespace Salryn.Mobile.Foundation.Cashier;

public sealed record MobileOrderDraft(Guid OperationId, Guid OrderId, int ExpectedVersion,
    DateTimeOffset OccurredAtUtc, string ServiceMode, string? ServiceReference,
    IReadOnlyList<CafeOrderLineMutation> Lines);
public sealed record MobileOrderAction(Guid OperationId, Guid OrderId, int ExpectedVersion, DateTimeOffset OccurredAtUtc);
public sealed record MobileOrderCancelAction(Guid OperationId, Guid OrderId, int ExpectedVersion, DateTimeOffset OccurredAtUtc, string Reason);
public sealed record MobileCustomerOption(Guid CustomerId, string Name);
public sealed record MobileCheckoutIntent(Guid OperationId, Guid OrderId, int ExpectedVersion,
    DateTimeOffset OccurredAtUtc, Guid ActorUserId, Guid DeviceId, Guid StoreId,
    long CashCents, long AccountsReceivableCents, Guid? CustomerId);
public sealed record MobileShiftOpenIntent(Guid OperationId, long OpeningCashCents, DateTimeOffset OccurredAtUtc);
public sealed record MobileShiftCloseIntent(Guid OperationId, Guid SessionId, long CountedCashCents, DateTimeOffset OccurredAtUtc);
public sealed record MobileRefundRequestIntent(Guid OperationId, Guid RefundId, Guid SaleId, int ExpectedVersion,
    DateTimeOffset OccurredAtUtc, string Reason, IReadOnlyList<CafeRefundLineMutation> Lines);
public sealed record MobileRefundResolutionIntent(Guid OperationId, Guid RefundId, Guid SaleId, int ExpectedVersion,
    DateTimeOffset OccurredAtUtc, bool Approve, string? Reason);
public sealed record MobileRefundCommitIntent(Guid OperationId, Guid RefundId, Guid SaleId, int ExpectedVersion,
    Guid CashierSessionId, DateTimeOffset OccurredAtUtc, IReadOnlyList<CafeRefundSettlementMutation> Settlements);
public sealed record MobileReceiptPrintIntent(Guid OperationId, Guid JobId, Guid StationId, Guid SaleId,
    CafePrintCopyKind CopyKind, Guid? ParentJobId, DateTimeOffset OccurredAtUtc);
public sealed record MobileKitchenPrintIntent(Guid OperationId, Guid JobId, Guid StationId, Guid OrderId,
    Guid OrderRevisionId, CafePrintCopyKind CopyKind, Guid? ParentJobId, DateTimeOffset OccurredAtUtc);
public sealed record MobileDeliveryCreateIntent(Guid OperationId, Guid DeliveryId, Guid OrderId,
    DateTimeOffset OccurredAtUtc, string RecipientName, string? ContactPhone, string DeliveryAddress,
    string? Landmark, string? Instructions, long DeliveryFeeCents, string? CourierReference);
public sealed record MobileDeliveryAdvanceIntent(Guid OperationId, Guid DeliveryId, int ExpectedVersion,
    DateTimeOffset OccurredAtUtc, string TargetStatus, string? Reason);
public sealed record MobileRefundableSaleItem(Guid SaleItemId, string ProductName, long RemainingQuantityX1000, long UnitPriceCents, bool TrackStock);
public sealed record MobileRefundableSaleSummary(Guid SaleId, string ReceiptNumber, long TotalCents, long CashNetCents, long ArCents,
    IReadOnlyList<MobileRefundableSaleItem> Items);
public sealed record MobileRefundSummary(Guid RefundId, Guid SaleId, string? ReceiptNumber, int Version, string State, string Reason, long? TotalRefundCents);

/// <summary>Native presentation boundary over the single canonical Cafe authority.
/// No financial tables, prices, stock calculations, or operator substitutions live here.
/// Callers retain each intent/operation ID until its outcome is confirmed.</summary>
public sealed class MobileCashierController(MobileProductionComposition runtime, MobileCashierDeviceGuard deviceGuard)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AuthenticatedUserReadModel? _operator;
    public AuthenticatedUserReadModel? CurrentOperator => _operator;

    public async Task<Result<AuthenticatedUserReadModel>> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        if (!await _gate.WaitAsync(0, ct).ConfigureAwait(false)) return Busy<AuthenticatedUserReadModel>();
        try
        {
            _operator = null;
            var result = await runtime.Authentication.LoginAsync(new LoginRequest(username, password), ct).ConfigureAwait(false);
            if (result.IsSuccess) _operator = result.Value;
            return result;
        }
        finally { _gate.Release(); }
    }

    public void SignOut() => _operator = null;

    public Task<Result<MenuPublicationSnapshot>> MenuAsync(CancellationToken ct = default) =>
        ExecuteAsync(SalrynPermissions.ProductsView, (binding, _, _) =>
        {
            var result = runtime.MenuCatalog.GetCurrentPublication();
            if (result.IsFailure) return Task.FromResult(Result<MenuPublicationSnapshot>.Fail(result.ErrorDetail));
            if (result.Value is not { } menu || menu.StoreId != binding.StoreId)
                return Task.FromResult(Fail<MenuPublicationSnapshot>("Mobile.MenuUnavailable", "No published menu is available for this store. Complete synchronization first."));
            return Task.FromResult(Result<MenuPublicationSnapshot>.Success(menu));
        }, ct);

    public Task<Result<IReadOnlyList<CafeOrderSummary>>> OrdersAsync(CancellationToken ct = default) =>
        ExecuteAsync(SalrynPermissions.OrdersView, (binding, _, _) =>
        {
            var result = runtime.CafeOrders.List();
            return Task.FromResult(result.IsFailure ? result : Result<IReadOnlyList<CafeOrderSummary>>.Success(
                result.Value.Where(x => x.StoreId == binding.StoreId).ToArray()));
        }, ct);

    public Task<Result<CafeOrderDetail>> FindOrderAsync(Guid orderId, CancellationToken ct = default) =>
        ExecuteAsync(SalrynPermissions.OrdersView, (binding, _, _) =>
            Task.FromResult(FindScopedOrder(binding, orderId)), ct);

    public Task<Result<IReadOnlyList<MobileCustomerOption>>> CustomersAsync(string? search, CancellationToken ct = default) =>
        ExecuteAsync(SalrynPermissions.ReceivablesCreateCreditSale, async (_, _, token) =>
        {
            var result = await runtime.AccountsReceivable.SearchCustomersPageAsync(search, 0, 50, token).ConfigureAwait(false);
            return result.IsFailure ? Result<IReadOnlyList<MobileCustomerOption>>.Fail(result.ErrorDetail)
                : Result<IReadOnlyList<MobileCustomerOption>>.Success(result.Value.Items
                    .Where(x => x.Customer.IsActive).Select(x => new MobileCustomerOption(x.Customer.Id, x.Customer.FullName)).ToArray());
        }, ct);

    public Task<Result<CashierSessionReadModel>> OpenShiftAsync(MobileShiftOpenIntent intent, CancellationToken ct = default) =>
        ExecuteAsync(SalrynPermissions.ShiftOpenClose, async (binding, actor, token) =>
        {
            if (intent.OperationId == Guid.Empty) return Fail<CashierSessionReadModel>("Mobile.OperationIdRequired", "Retain a client operation ID before opening a shift.");
            if (intent.OpeningCashCents < 0) return Fail<CashierSessionReadModel>("Mobile.InvalidCash", "Opening cash cannot be negative.");
            var result = runtime.CafeShifts.Open(intent.OperationId, binding.DeviceId, binding.StoreId, actor.UserId,
                intent.OpeningCashCents, intent.OccurredAtUtc);
            if (result.IsFailure) return Result<CashierSessionReadModel>.Fail(result.ErrorDetail);
            var session = await runtime.CashierSessions.FindByIdAsync(result.Value, token).ConfigureAwait(false);
            return session.IsSuccess && session.Value is not null ? Result<CashierSessionReadModel>.Success(session.Value)
                : Fail<CashierSessionReadModel>("Mobile.ShiftReadBackMissing", "Shift result was committed; retain the original operation to recover it.");
        }, ct);

    public Task<Result<CashierSessionReadModel>> CloseShiftAsync(MobileShiftCloseIntent intent, CancellationToken ct = default) =>
        ExecuteAsync(SalrynPermissions.ShiftOpenClose, async (binding, actor, token) =>
        {
            if (intent.OperationId == Guid.Empty) return Fail<CashierSessionReadModel>("Mobile.OperationIdRequired", "Retain a client operation ID before closing a shift.");
            if (intent.CountedCashCents < 0) return Fail<CashierSessionReadModel>("Mobile.InvalidCash", "Counted cash cannot be negative.");
            var session = await runtime.CashierSessions.FindByIdAsync(intent.SessionId, token).ConfigureAwait(false);
            if (session.IsFailure) return Result<CashierSessionReadModel>.Fail(session.ErrorDetail);
            if (session.Value is not { } own || own.UserId != actor.UserId)
                return Fail<CashierSessionReadModel>("Mobile.ShiftUnavailable", "This drawer is unavailable for the current operator.");
            var result = runtime.CafeShifts.Close(intent.OperationId, binding.DeviceId, binding.StoreId, actor.UserId,
                intent.SessionId, intent.CountedCashCents, intent.OccurredAtUtc);
            if (result.IsFailure) return Result<CashierSessionReadModel>.Fail(result.ErrorDetail);
            var closed = await runtime.CashierSessions.FindByIdAsync(result.Value, token).ConfigureAwait(false);
            return closed.IsSuccess && closed.Value is not null ? Result<CashierSessionReadModel>.Success(closed.Value)
                : Fail<CashierSessionReadModel>("Mobile.ShiftReadBackMissing", "Shift result was committed; retain the original operation to recover it.");
        }, ct);

    public Task<Result<CafeOrderMutationResult>> SaveOrderAsync(MobileOrderDraft draft, CancellationToken ct = default) =>
        ExecuteAsync(draft.ExpectedVersion == 0 ? SalrynPermissions.OrdersCreate : SalrynPermissions.OrdersManage,
            (binding, actor, _) => Task.FromResult(runtime.CafeOrders.SaveRevision(new(
                draft.OperationId, binding.DeviceId, draft.OrderId, binding.StoreId, draft.ExpectedVersion,
                actor.UserId, draft.OccurredAtUtc, draft.ServiceMode, draft.ServiceReference, draft.Lines))), ct);

    public Task<Result<CafeOrderMutationResult>> ParkOrderAsync(MobileOrderAction intent, CancellationToken ct = default) =>
        ExecuteAsync(SalrynPermissions.OrdersManage, (binding, actor, _) =>
            intent.OperationId == Guid.Empty
                ? Task.FromResult(Fail<CafeOrderMutationResult>("Mobile.OperationIdRequired", "Retain a client operation ID before parking an order."))
                : Task.FromResult(runtime.CafeOrders.Park(new(
                    intent.OperationId, binding.DeviceId, intent.OrderId, binding.StoreId, intent.ExpectedVersion,
                    actor.UserId, intent.OccurredAtUtc))), ct);

    public Task<Result<CafeOrderMutationResult>> ResumeOrderAsync(MobileOrderAction intent, CancellationToken ct = default) =>
        ExecuteAsync(SalrynPermissions.OrdersManage, (binding, actor, _) =>
            intent.OperationId == Guid.Empty
                ? Task.FromResult(Fail<CafeOrderMutationResult>("Mobile.OperationIdRequired", "Retain a client operation ID before resuming a parked order."))
                : Task.FromResult(runtime.CafeOrders.Resume(new(
                    intent.OperationId, binding.DeviceId, intent.OrderId, binding.StoreId, intent.ExpectedVersion,
                    actor.UserId, intent.OccurredAtUtc))), ct);

    public Task<Result<CafeOrderMutationResult>> CancelOrderAsync(MobileOrderCancelAction intent, CancellationToken ct = default) =>
        ExecuteAsync(SalrynPermissions.OrdersCancel, (binding, actor, _) =>
            intent.OperationId == Guid.Empty
                ? Task.FromResult(Fail<CafeOrderMutationResult>("Mobile.OperationIdRequired", "Retain a client operation ID before cancelling an order."))
                : Task.FromResult(runtime.CafeOrders.Cancel(new(
                    intent.OperationId, binding.DeviceId, intent.OrderId, binding.StoreId, intent.ExpectedVersion,
                    actor.UserId, intent.OccurredAtUtc, intent.Reason))), ct);

    public Task<Result<CafeOrderPreparationSubmissionResult>> SubmitPreparationAsync(MobileOrderAction intent, CancellationToken ct = default) =>
        ExecuteAsync(SalrynPermissions.OrdersManage, (binding, actor, _) =>
            Task.FromResult(runtime.CafeOrders.SubmitPreparation(new(intent.OperationId,
                binding.DeviceId, intent.OrderId, binding.StoreId, intent.ExpectedVersion,
                actor.UserId, intent.OccurredAtUtc))), ct);

    public Task<Result<CafeOrderMutationResult>> SetPreparationStatusAsync(MobileOrderAction intent, string targetState, CancellationToken ct = default) =>
        ExecuteAsync(SalrynPermissions.PreparationManage, (binding, actor, _) =>
            Task.FromResult(runtime.CafeOrders.SetPreparationStatus(new(intent.OperationId,
                binding.DeviceId, intent.OrderId, binding.StoreId, intent.ExpectedVersion,
                actor.UserId, intent.OccurredAtUtc, targetState))), ct);

    public Task<Result<MobileCheckoutIntent>> CreateCheckoutIntentAsync(Guid operationId, Guid orderId, int expectedVersion,
        DateTimeOffset occurredAtUtc, long cashCents, long arCents, Guid? customerId, CancellationToken ct = default) =>
        ExecuteAsync(SalrynPermissions.OrdersComplete, (binding, actor, _) => Task.FromResult(
            operationId == Guid.Empty ? Fail<MobileCheckoutIntent>("Mobile.OperationIdRequired", "Retain a client operation ID before submitting checkout.") :
            Result<MobileCheckoutIntent>.Success(new(operationId, orderId, expectedVersion, occurredAtUtc,
                actor.UserId, binding.DeviceId, binding.StoreId, cashCents, arCents, customerId))), ct);

    public Task<Result<CafeOrderCompletionResult>> CompleteAsync(MobileCheckoutIntent intent, CancellationToken ct = default) =>
        ExecuteAsync(SalrynPermissions.OrdersComplete, (binding, actor, _) =>
        {
            if (intent.ActorUserId != actor.UserId || intent.DeviceId != binding.DeviceId || intent.StoreId != binding.StoreId)
                return Task.FromResult(Fail<CafeOrderCompletionResult>("Mobile.CheckoutOwnerMismatch", "Saved checkout belongs to another operator or device. Preserve it for reconciliation."));
            if (intent.OperationId == Guid.Empty || intent.CashCents < 0 || intent.AccountsReceivableCents < 0)
                return Task.FromResult(Fail<CafeOrderCompletionResult>("Mobile.InvalidTender", "Only nonnegative Cash and Accounts Receivable amounts are supported."));
            if (intent.AccountsReceivableCents > 0 && !actor.Permissions.Contains(SalrynPermissions.ReceivablesCreateCreditSale, StringComparer.Ordinal))
                return Task.FromResult(Fail<CafeOrderCompletionResult>("Mobile.CreditSalePermissionRequired", "Your current account cannot create a credit sale."));
            List<CafeOrderPaymentMutation> payments = [];
            if (intent.CashCents > 0) payments.Add(new(PaymentMethod.Cash, intent.CashCents));
            if (intent.AccountsReceivableCents > 0) payments.Add(new(PaymentMethod.AccountsReceivable, intent.AccountsReceivableCents));
            IReadOnlyList<CafeSaleFeeMutation> fees = [];
            var delivery = runtime.CafeDelivery.GetByOrder(binding.StoreId, actor.UserId, intent.OrderId);
            if (delivery.IsSuccess && delivery.Value is { Delivery.Status: CafeDeliveryStatuses.Pending } pendingDelivery)
            {
                fees = [new(DeriveId(intent.OperationId, "delivery-fee"), CafeSaleFeeTypes.Delivery,
                    "Delivery Fee", pendingDelivery.CurrentRevision.DeliveryFeeCents, null)];
            }
            return Task.FromResult(runtime.CafeOrderCompletion.Complete(new(intent.OperationId,
                DeriveId(intent.OperationId, "sale"), binding.DeviceId, intent.OrderId, binding.StoreId,
                intent.ExpectedVersion, actor.UserId, intent.OccurredAtUtc, payments)
            {
                CustomerId = intent.CustomerId,
                FinancialDecisionOperationId = DeriveId(intent.OperationId, "financial-operation"),
                FinancialDecisionId = DeriveId(intent.OperationId, "financial-decision"),
                Fees = fees
            }));
        }, ct);

    public Task<Result<CafeRefundMutationResult>> RequestRefundAsync(MobileRefundRequestIntent intent, CancellationToken ct = default) =>
        ExecuteAsync(SalrynPermissions.SalesRefundRequest, (binding, actor, _) =>
            intent.OperationId == Guid.Empty
                ? Task.FromResult(Fail<CafeRefundMutationResult>("Mobile.OperationIdRequired", "Retain a client operation ID before requesting a refund."))
                : Task.FromResult(runtime.CafeRefunds.Request(new(
                    intent.OperationId, binding.DeviceId, intent.RefundId, intent.SaleId, binding.StoreId,
                    intent.ExpectedVersion, actor.UserId, intent.OccurredAtUtc, intent.Reason, intent.Lines))), ct);

    public Task<Result<CafeRefundMutationResult>> ResolveRefundAsync(MobileRefundResolutionIntent intent, CancellationToken ct = default) =>
        ExecuteAsync(SalrynPermissions.SalesRefundApprove, (binding, actor, _) =>
            intent.OperationId == Guid.Empty
                ? Task.FromResult(Fail<CafeRefundMutationResult>("Mobile.OperationIdRequired", "Retain a client operation ID before resolving a refund."))
                : Task.FromResult(runtime.CafeRefunds.Resolve(new(
                    intent.OperationId, binding.DeviceId, intent.RefundId, intent.SaleId, binding.StoreId,
                    intent.ExpectedVersion, actor.UserId, intent.OccurredAtUtc, intent.Approve, intent.Reason))), ct);

    public Task<Result<CafeRefundCommitResult>> CommitRefundAsync(MobileRefundCommitIntent intent, CancellationToken ct = default) =>
        ExecuteAsync(SalrynPermissions.SalesRefundApprove, (binding, actor, _) =>
            intent.OperationId == Guid.Empty
                ? Task.FromResult(Fail<CafeRefundCommitResult>("Mobile.OperationIdRequired", "Retain a client operation ID before committing a refund."))
                : Task.FromResult(runtime.CafeRefunds.Commit(new(
                    intent.OperationId, binding.DeviceId, intent.RefundId, intent.SaleId, binding.StoreId,
                    intent.ExpectedVersion, actor.UserId, intent.CashierSessionId, intent.OccurredAtUtc, intent.Settlements))), ct);

    public Task<Result<IReadOnlyList<MobileRefundableSaleSummary>>> ListRefundableSalesAsync(CancellationToken ct = default) =>
        ExecuteAnyAsync([SalrynPermissions.SalesRefundRequest, SalrynPermissions.SalesRefundApprove], (binding, _, _) =>
        {
            using var connection = runtime.ConnectionFactory.CreateConnection();
            connection.Open();
            using var salesCmd = connection.CreateCommand();
            salesCmd.CommandText = """
                SELECT s.id, s.receipt_no, s.total_cents,
                       COALESCE((SELECT SUM(p.amount_cents) FROM payments p WHERE p.sale_id = s.id AND p.method = $cash), 0),
                       COALESCE((SELECT SUM(p.amount_cents) FROM payments p WHERE p.sale_id = s.id AND p.method = $ar), 0),
                       s.change_cents
                FROM sales s
                WHERE s.status = $completed
                  AND EXISTS (SELECT 1 FROM deployment_identity di WHERE di.store_id = $store)
                  AND NOT EXISTS (
                      SELECT 1
                      FROM cafe_order_sale_links osl
                      INNER JOIN cafe_orders co ON co.order_id = osl.order_id
                      WHERE osl.sale_id = s.id AND co.store_id != $store
                  )
                ORDER BY s.created_at DESC
                LIMIT 25;
                """;
            salesCmd.Parameters.AddWithValue("$cash", Convert.ToInt32(PaymentMethod.Cash));
            salesCmd.Parameters.AddWithValue("$ar", Convert.ToInt32(PaymentMethod.AccountsReceivable));
            salesCmd.Parameters.AddWithValue("$completed", Convert.ToInt32(SaleStatus.Completed));
            salesCmd.Parameters.AddWithValue("$store", binding.StoreId.ToString("D"));
            List<MobileRefundableSaleSummary> result = [];
            List<(Guid SaleId, string Receipt, long Total, long CashNet, long Ar)> sales = [];
            using (var r = salesCmd.ExecuteReader())
            {
                while (r.Read())
                {
                    long cashNet = Math.Max(0, r.GetInt64(3) - r.GetInt64(5));
                    sales.Add((Guid.Parse(r.GetString(0)), r.GetString(1), r.GetInt64(2), cashNet, r.GetInt64(4)));
                }
            }
            foreach (var s in sales)
            {
                using var lineCmd = connection.CreateCommand();
                lineCmd.CommandText = """
                    SELECT si.id, si.product_name_snapshot, si.qty_x1000, si.unit_price_cents, COALESCE(p.track_stock, 0),
                           COALESCE((SELECT SUM(rl.refund_quantity_x1000)
                                     FROM cafe_sale_refund_lines rl
                                     INNER JOIN cafe_sale_refunds r ON r.refund_id = rl.refund_id
                                     WHERE rl.sale_item_id = si.id AND r.state != 'Rejected'), 0)
                    FROM sale_items si
                    INNER JOIN products p ON p.id = si.product_id
                    WHERE si.sale_id = $sale;
                    """;
                lineCmd.Parameters.AddWithValue("$sale", s.SaleId.ToString("D"));
                List<MobileRefundableSaleItem> items = [];
                using var lr = lineCmd.ExecuteReader();
                while (lr.Read())
                {
                    long remaining = Math.Max(0, lr.GetInt64(2) - lr.GetInt64(5));
                    if (remaining > 0)
                        items.Add(new(Guid.Parse(lr.GetString(0)), lr.GetString(1), remaining, lr.GetInt64(3), lr.GetInt64(4) == 1));
                }
                if (items.Count > 0)
                    result.Add(new(s.SaleId, s.Receipt, s.Total, s.CashNet, s.Ar, items));
            }
            return Task.FromResult(Result<IReadOnlyList<MobileRefundableSaleSummary>>.Success(result));
        }, ct);

    public Task<Result<IReadOnlyList<MobileRefundSummary>>> ListRefundsAsync(CancellationToken ct = default) =>
        ExecuteAnyAsync([SalrynPermissions.SalesRefundRequest, SalrynPermissions.SalesRefundApprove], (binding, _, _) =>
        {
            using var connection = runtime.ConnectionFactory.CreateConnection();
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT r.refund_id, r.sale_id, s.receipt_no, r.current_version, r.state, r.reason,
                       COALESCE(r.total_refund_cents, (SELECT SUM(rl.refund_amount_cents) FROM cafe_sale_refund_lines rl WHERE rl.refund_id = r.refund_id))
                FROM cafe_sale_refunds r
                LEFT JOIN sales s ON s.id = r.sale_id
                WHERE r.store_id = $store
                ORDER BY r.requested_at_utc DESC
                LIMIT 50;
                """;
            cmd.Parameters.AddWithValue("$store", binding.StoreId.ToString("D"));
            List<MobileRefundSummary> rows = [];
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new(
                    Guid.Parse(reader.GetString(0)),
                    Guid.Parse(reader.GetString(1)),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetInt32(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetInt64(6)));
            }
            return Task.FromResult(Result<IReadOnlyList<MobileRefundSummary>>.Success(rows));
        }, ct);

    public Task<Result<CafeRefundDetail>> FindRefundAsync(Guid refundId, CancellationToken ct = default) =>
        ExecuteAsync(SalrynPermissions.SalesViewHistory, (binding, _, _) =>
        {
            var result = runtime.CafeRefunds.Get(refundId);
            if (result.IsFailure) return Task.FromResult(result);
            return Task.FromResult(result.Value.Refund.StoreId == binding.StoreId
                ? result
                : Fail<CafeRefundDetail>("Mobile.RefundUnavailable", "Refund was not found in this store."));
        }, ct);

    public Task<Result<IReadOnlyList<CafePrinterStation>>> ListPrinterStationsAsync(CancellationToken ct = default) =>
        ExecuteAsync(SalrynPermissions.PrintingView, (binding, actor, _) =>
            Task.FromResult(runtime.CafePrint.ListStations(binding.StoreId, actor.UserId)), ct);

    public Task<Result<CafePrintJobMutationResult>> CreateReceiptPrintJobAsync(MobileReceiptPrintIntent intent, CancellationToken ct = default) =>
        ExecuteAsync(intent.CopyKind == CafePrintCopyKind.Original ? SalrynPermissions.PrintingPrint : SalrynPermissions.PrintingReprint,
            (binding, actor, token) =>
                intent.OperationId == Guid.Empty
                    ? Task.FromResult(Fail<CafePrintJobMutationResult>("Mobile.OperationIdRequired", "Retain a client operation ID before creating a receipt print job."))
                    : runtime.CafePrint.CreateReceiptJobAsync(new(
                        intent.OperationId, intent.JobId, binding.DeviceId, binding.StoreId, actor.UserId,
                        intent.StationId, intent.SaleId, intent.CopyKind, intent.ParentJobId, intent.OccurredAtUtc), token), ct);

    public Task<Result<CafePrintJobMutationResult>> CreateKitchenPrintJobAsync(MobileKitchenPrintIntent intent, CancellationToken ct = default) =>
        ExecuteAsync(intent.CopyKind == CafePrintCopyKind.Original ? SalrynPermissions.PrintingPrint : SalrynPermissions.PrintingReprint,
            (binding, actor, _) =>
                intent.OperationId == Guid.Empty
                    ? Task.FromResult(Fail<CafePrintJobMutationResult>("Mobile.OperationIdRequired", "Retain a client operation ID before creating a kitchen print job."))
                    : Task.FromResult(runtime.CafePrint.CreateKitchenTicketJob(new(
                        intent.OperationId, intent.JobId, binding.DeviceId, binding.StoreId, actor.UserId,
                        intent.StationId, intent.OrderId, intent.OrderRevisionId, intent.CopyKind, intent.ParentJobId, intent.OccurredAtUtc))), ct);

    public Task<Result<CafePrintAttemptResult>> ExecutePrintAttemptAsync(Guid jobId, IReceiptPrinterAdapter printerAdapter, CancellationToken ct = default) =>
        ExecuteAsync(SalrynPermissions.PrintingPrint, (binding, actor, token) =>
            runtime.CafePrint.ExecuteAttemptAsync(jobId, binding.DeviceId, actor.UserId, printerAdapter, token), ct);

    public Task<Result<CafePrintJob?>> FindPrintJobAsync(Guid jobId, CancellationToken ct = default) =>
        ExecuteAsync(SalrynPermissions.PrintingView, (binding, actor, _) =>
        {
            var result = runtime.CafePrint.FindJob(jobId, actor.UserId);
            if (result.IsFailure || result.Value is null) return Task.FromResult(result);
            return Task.FromResult(result.Value.StoreId == binding.StoreId
                ? result
                : Fail<CafePrintJob?>("Mobile.PrintJobUnavailable", "Print job was not found in this store."));
        }, ct);

    public Task<Result<IReadOnlyList<CafeDeliverySummary>>> ListDeliveriesAsync(string? status = null, CancellationToken ct = default) =>
        ExecuteAsync(SalrynPermissions.DeliveryView, (binding, actor, _) =>
            Task.FromResult(runtime.CafeDelivery.List(binding.StoreId, actor.UserId, status)), ct);

    public Task<Result<CafeDeliveryDetail?>> FindDeliveryByOrderAsync(Guid orderId, CancellationToken ct = default) =>
        ExecuteAsync(SalrynPermissions.DeliveryView, (binding, actor, _) =>
            Task.FromResult(runtime.CafeDelivery.GetByOrder(binding.StoreId, actor.UserId, orderId)), ct);

    public Task<Result<CafeDeliveryMutationResult>> CreateDeliveryAsync(MobileDeliveryCreateIntent intent, CancellationToken ct = default) =>
        ExecuteAsync(SalrynPermissions.DeliveryManage, (binding, actor, _) =>
            intent.OperationId == Guid.Empty
                ? Task.FromResult(Fail<CafeDeliveryMutationResult>("Mobile.OperationIdRequired", "Retain a client operation ID before creating a delivery."))
                : Task.FromResult(runtime.CafeDelivery.Create(new(
                    intent.OperationId, binding.DeviceId, intent.DeliveryId, intent.OrderId, binding.StoreId,
                    actor.UserId, intent.OccurredAtUtc, intent.RecipientName, intent.ContactPhone,
                    intent.DeliveryAddress, intent.Landmark, intent.Instructions, intent.DeliveryFeeCents, intent.CourierReference))), ct);

    public Task<Result<CafeDeliveryMutationResult>> AdvanceDeliveryAsync(MobileDeliveryAdvanceIntent intent, CancellationToken ct = default) =>
        ExecuteAsync(SalrynPermissions.DeliveryAdvance, (binding, actor, _) =>
            intent.OperationId == Guid.Empty
                ? Task.FromResult(Fail<CafeDeliveryMutationResult>("Mobile.OperationIdRequired", "Retain a client operation ID before advancing delivery status."))
                : Task.FromResult(runtime.CafeDelivery.Advance(new(
                    intent.OperationId, binding.DeviceId, intent.DeliveryId, binding.StoreId,
                    intent.ExpectedVersion, actor.UserId, intent.OccurredAtUtc, intent.TargetStatus, intent.Reason))), ct);

    private Task<Result<T>> ExecuteAsync<T>(string permission,
        Func<MobileEnrollmentBinding, UserPermissionReadModel, CancellationToken, Task<Result<T>>> action, CancellationToken ct) =>
        ExecuteAnyAsync([permission], action, ct);

    private async Task<Result<T>> ExecuteAnyAsync<T>(IReadOnlyList<string> anyPermissions,
        Func<MobileEnrollmentBinding, UserPermissionReadModel, CancellationToken, Task<Result<T>>> action, CancellationToken ct)
    {
        // Acquisition is immediate: two taps cannot queue two intents before UI disable.
        if (!await _gate.WaitAsync(0, ct).ConfigureAwait(false)) return Busy<T>();
        try
        {
            AuthenticatedUserReadModel? current = _operator;
            if (current is null) return Fail<T>("Mobile.OperatorLoginRequired", "Sign in with your operator account.");
            var profile = await runtime.UserPermissionReadRepository.FindByUserIdAsync(current.UserId, ct).ConfigureAwait(false);
            if (profile.IsFailure) return Result<T>.Fail(profile.ErrorDetail);
            if (profile.Value is not { Status: UserStatus.Active } actor || actor.RoleId != current.RoleId)
            {
                _operator = null;
                return Fail<T>("Mobile.OperatorRevoked", "Operator authorization changed. Sign in again.");
            }
            if (!anyPermissions.Any(p => actor.Permissions.Contains(p, StringComparer.Ordinal)))
                return Fail<T>("Mobile.PermissionDenied", "Your current operator account cannot perform this action.");
            var device = await deviceGuard.AuthorizeAsync(ct).ConfigureAwait(false);
            if (device.IsFailure) return Result<T>.Fail(device.ErrorDetail);
            var offlineAuthority=runtime.OperatorCredentials.CheckOfflineAuthority(current.UserId,device.Value.DeviceId,
                device.Value.TenantId,device.Value.StoreId,device.Value.ServerEpoch,deviceGuard.CurrentUtcNow);
            if(offlineAuthority.IsFailure) return Result<T>.Fail(offlineAuthority.ErrorDetail);
            return await action(device.Value, actor, ct).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private Result<CafeOrderDetail> FindScopedOrder(MobileEnrollmentBinding binding, Guid id)
    {
        var result = runtime.CafeOrders.Find(id);
        if (result.IsFailure) return Result<CafeOrderDetail>.Fail(result.ErrorDetail);
        return result.Value is { } order && order.Order.StoreId == binding.StoreId
            ? Result<CafeOrderDetail>.Success(order)
            : Fail<CafeOrderDetail>("Mobile.OrderUnavailable", "Order was not found in this store.");
    }

    private static Guid DeriveId(Guid operationId, string purpose) => new(
        SHA256.HashData(Encoding.UTF8.GetBytes($"salryn.cafe.mobile.checkout.v1|{operationId:D}|{purpose}"))[..16]);
    private static Result<T> Busy<T>() => Fail<T>("Mobile.Busy", "Wait for the current action to finish before trying again.");
    private static Result<T> Fail<T>(string code, string message) => Result<T>.Fail(Error.Forbidden(code, message));
}

