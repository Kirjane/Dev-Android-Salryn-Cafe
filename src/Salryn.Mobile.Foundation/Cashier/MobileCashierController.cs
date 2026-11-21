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
        ExecuteAsync(SalrynPermissions.ShiftOpenClose, async (_, actor, token) =>
        {
            if (openingCashCents < 0) return Fail<CashierSessionReadModel>("Mobile.InvalidCash", "Opening cash cannot be negative.");
            // A lost acknowledgement is recovered from the canonical open session,
            // never by creating a second drawer or exposing expected closing cash.
            var existing = await runtime.CashierSessions.FindOpenSessionByUserIdAsync(actor.UserId, token).ConfigureAwait(false);
            if (existing.IsFailure) return Result<CashierSessionReadModel>.Fail(existing.ErrorDetail);
            if (existing.Value is { } opened) return Result<CashierSessionReadModel>.Success(opened);
            return await runtime.CashierSessions.OpenAsync(new(actor.UserId, Money.FromCents(openingCashCents), null), token).ConfigureAwait(false);
        }, ct);

    public Task<Result<CashierSessionReadModel>> CloseShiftAsync(Guid sessionId, long countedCashCents, CancellationToken ct = default) =>
        ExecuteAsync(SalrynPermissions.ShiftOpenClose, async (_, actor, token) =>
        {
            if (countedCashCents < 0) return Fail<CashierSessionReadModel>("Mobile.InvalidCash", "Counted cash cannot be negative.");
            var session = await runtime.CashierSessions.FindByIdAsync(sessionId, token).ConfigureAwait(false);
            if (session.IsFailure) return Result<CashierSessionReadModel>.Fail(session.ErrorDetail);
            if (session.Value is not { } own || own.UserId != actor.UserId)
                return Fail<CashierSessionReadModel>("Mobile.ShiftUnavailable", "This drawer is unavailable for the current operator.");
            return await runtime.CashierSessions.CloseAsync(new(sessionId, actor.UserId, Money.FromCents(countedCashCents), null), token).ConfigureAwait(false);
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
            return Task.FromResult(runtime.CafeOrderCompletion.Complete(new(intent.OperationId,
                DeriveId(intent.OperationId, "sale"), binding.DeviceId, intent.OrderId, binding.StoreId,
                intent.ExpectedVersion, actor.UserId, intent.OccurredAtUtc, payments)
            {
                CustomerId = intent.CustomerId,
                FinancialDecisionOperationId = DeriveId(intent.OperationId, "financial-operation"),
                FinancialDecisionId = DeriveId(intent.OperationId, "financial-decision")
            }));
        }, ct);

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

