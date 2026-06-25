using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Repositories;

namespace Salryn.Core.Services;

public sealed record PosCheckoutLineInput(
    Guid ProductId,
    string ProductNameSnapshot,
    string? BarcodeSnapshot,
    string UnitNameSnapshot,
    long QuantityX1000,
    long UnitPriceCents,
    long? CostPriceCents,
    long DiscountCents,
    bool TrackStock);

public sealed record PosCheckoutPaymentInput(
    PaymentMethod Method,
    long AmountCents);

public sealed record PosCheckoutApplicationRequest(
    Guid CashierUserId,
    Guid CashierSessionId,
    string ReceiptNumber,
    IReadOnlyList<PosCheckoutLineInput> Lines,
    IReadOnlyList<PosCheckoutPaymentInput> Payments,
    long SaleDiscountCents,
    long TaxCents,
    string? ReceiptSnapshot,
    DateTimeOffset? SaleDate = null);

public interface IPosCheckoutApplicationService
{
    Task<Result> CompleteSaleAsync(
        PosCheckoutApplicationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class PosCheckoutApplicationService : IPosCheckoutApplicationService
{
    private readonly IPosCheckoutService _checkoutService;
    private readonly ITrustedPosCheckoutSaleBuilder _trustedSaleBuilder;

    public PosCheckoutApplicationService(IPosCheckoutService checkoutService)
        : this(checkoutService, new TrustedPosCheckoutSaleBuilder())
    {
    }

    public PosCheckoutApplicationService(
        IPosCheckoutService checkoutService,
        IProductReadRepository productReadRepository)
        : this(checkoutService, new TrustedPosCheckoutSaleBuilder(productReadRepository))
    {
    }

    public PosCheckoutApplicationService(
        IPosCheckoutService checkoutService,
        ITrustedPosCheckoutSaleBuilder trustedSaleBuilder)
    {
        _checkoutService = checkoutService ?? throw new ArgumentNullException(nameof(checkoutService));
        _trustedSaleBuilder = trustedSaleBuilder ?? throw new ArgumentNullException(nameof(trustedSaleBuilder));
    }

    public async Task<Result> CompleteSaleAsync(
        PosCheckoutApplicationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Result<CompleteCheckoutSaleRequest> trustedSaleResult =
            await _trustedSaleBuilder.BuildAsync(request, cancellationToken).ConfigureAwait(false);

        if (trustedSaleResult.IsFailure)
        {
            return Result.Fail(trustedSaleResult.ErrorDetail);
        }

        return await _checkoutService.CompleteSaleAsync(
            trustedSaleResult.Value,
            cancellationToken).ConfigureAwait(false);
    }
}

