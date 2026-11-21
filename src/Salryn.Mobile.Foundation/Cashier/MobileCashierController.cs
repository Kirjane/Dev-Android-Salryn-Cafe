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

    public Task<Result<CashierSessionReadModel>> OpenShiftAsync(long openingCashCents, CancellationToken ct = default) =>
        OpenShiftAsync(new MobileShiftOpenIntent(Guid.NewGuid(), openingCashCents, DateTimeOffset.UtcNow), ct);

    public Task<Result<CashierSessionReadModel>> OpenShiftAsync(MobileShiftOpenIntent intent, CancellationToken ct = default) =>
        ExecuteAsync(SalrynPermissions.ShiftOpenClose, async (_, actor, token) =>
        {
            if (intent.OperationId == Guid.Empty) return Fail<CashierSessionReadModel>("Mobile.OperationIdRequired", "Retain a client operation ID before opening a shift.");
            if (intent.OpeningCashCents < 0) return Fail<CashierSessionReadModel>("Mobile.InvalidCash", "Opening cash cannot be negative.");
            // A lost acknowledgement is recovered from the canonical open session,
            // never by creating a second drawer or exposing expected closing cash.
            var existing = await runtime.CashierSessions.FindOpenSessionByUserIdAsync(actor.UserId, token).ConfigureAwait(false);
            if (existing.IsFailure) return Result<CashierSessionReadModel>.Fail(existing.ErrorDetail);
            if (existing.Value is { } opened) return Result<CashierSessionReadModel>.Success(opened);
            return await runtime.CashierSessions.OpenAsync(
                new(actor.UserId, Money.FromCents(intent.OpeningCashCents), $"[op:{intent.OperationId:D}]"), token).ConfigureAwait(false);
        }, ct);

    public Task<Result<CashierSessionReadModel>> CloseShiftAsync(Guid sessionId, long countedCashCents, CancellationToken ct = default) =>
        CloseShiftAsync(new MobileShiftCloseIntent(Guid.NewGuid(), sessionId, countedCashCents, DateTimeOffset.UtcNow), ct);

    public Task<Result<CashierSessionReadModel>> CloseShiftAsync(MobileShiftCloseIntent intent, CancellationToken ct = default) =>
        ExecuteAsync(SalrynPermissions.ShiftOpenClose, async (_, actor, token) =>
        {
            if (intent.OperationId == Guid.Empty) return Fail<CashierSessionReadModel>("Mobile.OperationIdRequired", "Retain a client operation ID before closing a shift.");
            if (intent.CountedCashCents < 0) return Fail<CashierSessionReadModel>("Mobile.InvalidCash", "Counted cash cannot be negative.");
            var session = await runtime.CashierSessions.FindByIdAsync(intent.SessionId, token).ConfigureAwait(false);
            if (session.IsFailure) return Result<CashierSessionReadModel>.Fail(session.ErrorDetail);
            if (session.Value is not { } own || own.UserId != actor.UserId)
                return Fail<CashierSessionReadModel>("Mobile.ShiftUnavailable", "This drawer is unavailable for the current operator.");
            string opMarker = $"[op:{intent.OperationId:D}]";
            if (own.Status == CashierSessionStatus.Closed && own.Notes is not null && own.Notes.Contains(opMarker, StringComparison.Ordinal))
                return Result<CashierSessionReadModel>.Success(own);
            return await runtime.CashierSessions.CloseAsync(
                new(intent.SessionId, actor.UserId, Money.FromCents(intent.CountedCashCents), opMarker), token).ConfigureAwait(false);
        }, ct);

    public Task<Result<CafeOrderMutationResult>> SaveOrderAsync(MobileOrderDraft draft, CancellationToken ct = default) =>
        ExecuteAsync(draft.ExpectedVersion == 0 ? SalrynPermissions.OrdersCreate : SalrynPermissions.OrdersManage,
            (binding, actor, _) => Task.FromResult(runtime.CafeOrders.SaveRevision(new(
                draft.OperationId, binding.DeviceId, draft.OrderId, binding.StoreId, draft.ExpectedVersion,
                actor.UserId, draft.OccurredAtUtc, draft.ServiceMode, draft.ServiceReference, draft.Lines))), ct);

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

    private async Task<Result<T>> ExecuteAsync<T>(string permission,
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
            if (!actor.Permissions.Contains(permission, StringComparer.Ordinal))
                return Fail<T>("Mobile.PermissionDenied", "Your current operator account cannot perform this action.");
            var device = await deviceGuard.AuthorizeAsync(ct).ConfigureAwait(false);
            if (device.IsFailure) return Result<T>.Fail(device.ErrorDetail);
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

