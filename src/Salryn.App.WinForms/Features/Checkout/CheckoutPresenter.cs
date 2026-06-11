using System.Globalization;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Services;

namespace Salryn.App.WinForms.Features.Checkout;

public sealed class CheckoutPresenter
{
    private readonly ICheckoutProductSearchBoundary? _productSearchBoundary;
    private readonly CheckoutSessionContext? _sessionContext;
    private readonly ICheckoutReceiptNumberBoundary _receiptNumberBoundary;
    private readonly ICheckoutCompletionBoundary _completionBoundary;
    private readonly PosCheckoutProductPickerBoundary _productPickerBoundary = new();

    private PosCheckoutDraftTransactionBuilder _builder = new();

    private Guid? _cashierUserId;
    private string _receiptNumber = string.Empty;
    private string _productInputText = string.Empty;
    private string _productResultText = "Waiting for product input.";
    private string _cashAmountText = "PHP 0.00";
    private long _cashAmountCents;
    private Guid? _selectedLineId;
    private PosCheckoutDraftLineInput? _preparedProductLineInput;
    private CheckoutPreparedProductViewModel? _preparedProduct;

    public CheckoutPresenter()
        : this(
            productSearchBoundary: null,
            sessionContext: null,
            receiptNumberBoundary: null,
            completionBoundary: null)
    {
    }

    public CheckoutPresenter(ICheckoutProductSearchBoundary? productSearchBoundary)
        : this(
            productSearchBoundary,
            sessionContext: null,
            receiptNumberBoundary: null,
            completionBoundary: null)
    {
    }

    public CheckoutPresenter(
        ICheckoutProductSearchBoundary? productSearchBoundary,
        CheckoutSessionContext? sessionContext)
        : this(
            productSearchBoundary,
            sessionContext,
            receiptNumberBoundary: null,
            completionBoundary: null)
    {
    }

    public CheckoutPresenter(
        ICheckoutProductSearchBoundary? productSearchBoundary,
        CheckoutSessionContext? sessionContext,
        ICheckoutReceiptNumberBoundary? receiptNumberBoundary)
        : this(
            productSearchBoundary,
            sessionContext,
            receiptNumberBoundary,
            completionBoundary: null)
    {
    }

    public CheckoutPresenter(
        ICheckoutProductSearchBoundary? productSearchBoundary,
        CheckoutSessionContext? sessionContext,
        ICheckoutReceiptNumberBoundary? receiptNumberBoundary,
        ICheckoutCompletionBoundary? completionBoundary)
    {
        _productSearchBoundary = productSearchBoundary;
        _sessionContext = sessionContext;
        _receiptNumberBoundary = receiptNumberBoundary ?? new SystemCheckoutReceiptNumberBoundary();
        _completionBoundary = completionBoundary ?? new CheckoutCompletionBoundary();
        State = CheckoutViewModel.Empty();
        _ = RefreshState("Checkout presenter ready.");
    }

    public CheckoutViewModel State { get; private set; }

    public CheckoutPresenterOperationResult Initialize()
    {
        _builder = new PosCheckoutDraftTransactionBuilder();
        _cashierUserId = null;
        _receiptNumber = string.Empty;
        _productInputText = string.Empty;
        _productResultText = "Waiting for product input.";
        _cashAmountText = "PHP 0.00";
        _cashAmountCents = 0;
        _selectedLineId = null;
        _preparedProductLineInput = null;
        _preparedProduct = null;

        Result receiptResult = AssignNewReceiptNumber();

        if (receiptResult.IsFailure)
        {
            return FailFrom(receiptResult);
        }

        if (_sessionContext is not null)
        {
            Result cashierResult = _builder.SetCashierUserId(_sessionContext.CashierUserId);

            if (cashierResult.IsFailure)
            {
                return FailFrom(cashierResult);
            }

            Result cashierSessionResult = _builder.SetCashierSessionId(_sessionContext.CashierSessionId);

            if (cashierSessionResult.IsFailure)
            {
                return FailFrom(cashierSessionResult);
            }

            _cashierUserId = _sessionContext.CashierUserId;

            return RefreshState(
                $"Checkout bound to cashier shift {_sessionContext.ShortCashierSessionId}.");
        }

        return RefreshState("Checkout presenter initialized without cashier shift context.");
    }

    public CheckoutPresenterOperationResult SetCashierUserId(Guid cashierUserId)
    {
        Result result = _builder.SetCashierUserId(cashierUserId);

        if (result.IsFailure)
        {
            return FailFrom(result);
        }

        _cashierUserId = cashierUserId;

        return RefreshState("Cashier placeholder set.");
    }

    private Result AssignNewReceiptNumber()
    {
        Result<string> receiptResult =
            _receiptNumberBoundary.CreateReceiptNumber(_sessionContext);

        if (receiptResult.IsFailure)
        {
            return Result.Fail(receiptResult.ErrorDetail);
        }

        Result builderResult = _builder.SetReceiptNumber(receiptResult.Value);

        if (builderResult.IsFailure)
        {
            return builderResult;
        }

        _receiptNumber = receiptResult.Value;

        return Result.Success();
    }

    public CheckoutPresenterOperationResult SetReceiptNumber(string? receiptNumber)
    {
        string trimmed = receiptNumber?.Trim() ?? string.Empty;

        Result result = _builder.SetReceiptNumber(trimmed);

        if (result.IsFailure)
        {
            return FailFrom(result);
        }

        _receiptNumber = trimmed;

        return RefreshState("Receipt number set.");
    }

    public CheckoutPresenterOperationResult SetProductInputText(string? productInputText)
    {
        _productInputText = productInputText?.Trim() ?? string.Empty;
        _preparedProductLineInput = null;
        _preparedProduct = null;

        if (string.IsNullOrWhiteSpace(_productInputText))
        {
            _productResultText = "Waiting for product input.";
        }
        else if (_productSearchBoundary is null)
        {
            _productResultText = "Product search boundary is not wired yet.";
        }
        else
        {
            _productResultText = "Ready to search product.";
        }

        return RefreshState("Product input updated.");
    }

    public async Task<CheckoutPresenterOperationResult> SearchProductAsync(
        string? productInputText,
        CancellationToken cancellationToken = default)
    {
        SetProductInputText(productInputText);

        if (string.IsNullOrWhiteSpace(_productInputText))
        {
            return Fail(
                "CheckoutUi.ProductSearchTextRequired",
                "Enter or scan a product before searching.");
        }

        if (_productSearchBoundary is null)
        {
            return Fail(
                "CheckoutUi.ProductSearchBoundaryMissing",
                "Product search is not wired yet.");
        }

        Result<ProductLookupReadModel?> searchResult =
            await _productSearchBoundary.FindProductAsync(
                _productInputText,
                cancellationToken).ConfigureAwait(false);

        if (searchResult.IsFailure)
        {
            return FailFrom(searchResult);
        }

        ProductLookupReadModel? product = searchResult.Value;

        if (product is null)
        {
            _preparedProductLineInput = null;
            _preparedProduct = null;
            _productResultText = $"No product found for '{_productInputText}'.";

            return RefreshState("Product not found.");
        }

        Result<PosCheckoutDraftLineInput> lineInputResult =
            _productPickerBoundary.CreateLineInput(product);

        if (lineInputResult.IsFailure)
        {
            _preparedProductLineInput = null;
            _preparedProduct = null;

            return FailFrom(lineInputResult);
        }

        _preparedProductLineInput = lineInputResult.Value;
        _preparedProduct = MapPreparedProduct(product);
        _productResultText = $"{product.Name} is ready to add.";

        return RefreshState("Product found and prepared.");
    }

    public CheckoutPresenterOperationResult ClearProductSearch()
    {
        _productInputText = string.Empty;
        _productResultText = "Waiting for product input.";
        _preparedProductLineInput = null;
        _preparedProduct = null;

        return RefreshState("Product search cleared.");
    }

    public CheckoutPresenterOperationResult AddPreparedProductLine()
    {
        if (_preparedProductLineInput is null)
        {
            return Fail(
                "CheckoutUi.PreparedProductRequired",
                "Search and prepare a product before adding it to the draft.");
        }

        Result<PosCheckoutDraftLine> result = _builder.AddLine(_preparedProductLineInput);

        if (result.IsFailure)
        {
            return FailFrom(result);
        }

        _selectedLineId = result.Value.Id;
        _preparedProductLineInput = null;
        _preparedProduct = null;
        _productInputText = string.Empty;
        _productResultText = "Waiting for product input.";

        return RefreshState("Prepared product added to draft.");
    }

    public CheckoutPresenterOperationResult AddPreparedLine(PosCheckoutDraftLineInput? lineInput)
    {
        if (lineInput is null)
        {
            return Fail(
                "CheckoutUi.PreparedLineRequired",
                "A prepared checkout line is required before adding an item.");
        }

        Result<PosCheckoutDraftLine> result = _builder.AddLine(lineInput);

        if (result.IsFailure)
        {
            return FailFrom(result);
        }

        _selectedLineId = result.Value.Id;

        return RefreshState("Prepared line added to draft.");
    }

    public CheckoutPresenterOperationResult SelectLine(Guid? lineId)
    {
        _selectedLineId = lineId;

        return RefreshState(lineId.HasValue
            ? "Draft line selected."
            : "Draft line selection cleared.");
    }

    public CheckoutPresenterOperationResult RemoveLine(Guid lineId)
    {
        Result result = _builder.RemoveLine(lineId);

        if (result.IsFailure)
        {
            return FailFrom(result);
        }

        if (_selectedLineId == lineId)
        {
            _selectedLineId = null;
        }

        return RefreshState("Draft line removed.");
    }

    public CheckoutPresenterOperationResult SetCashAmountCents(long amountCents)
    {
        if (amountCents < 0)
        {
            return Fail(
                "CheckoutUi.InvalidCashAmount",
                "Cash amount cannot be negative.");
        }

        _cashAmountCents = amountCents;
        _cashAmountText = FormatMoney(amountCents);

        return RefreshState("Cash amount updated.");
    }

    public CheckoutPresenterOperationResult SetCashAmountText(string? amountText)
    {
        string normalized = (amountText ?? string.Empty)
            .Trim()
            .Replace("PHP", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(",", string.Empty)
            .Trim();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            _cashAmountCents = 0;
            _cashAmountText = "PHP 0.00";

            return RefreshState("Cash amount cleared.");
        }

        if (!decimal.TryParse(
                normalized,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out decimal parsed))
        {
            return Fail(
                "CheckoutUi.InvalidCashAmount",
                "Cash amount must be a valid money value.");
        }

        if (parsed < 0)
        {
            return Fail(
                "CheckoutUi.InvalidCashAmount",
                "Cash amount cannot be negative.");
        }

        long cents = checked((long)Math.Round(
            parsed * 100m,
            0,
            MidpointRounding.AwayFromZero));

        return SetCashAmountCents(cents);
    }

    public CheckoutPresenterOperationResult AddCashPayment()
    {
        if (_cashAmountCents <= 0)
        {
            return Fail(
                "CheckoutUi.CashAmountRequired",
                "Cash amount must be greater than zero before adding payment.");
        }

        Result<PosCheckoutDraftPayment> result = _builder.AddPayment(PaymentMethod.Cash, _cashAmountCents);

        if (result.IsFailure)
        {
            return FailFrom(result);
        }

        return RefreshState("Cash payment added to draft.");
    }

    public CheckoutPresenterOperationResult ClearPayments()
    {
        Result result = _builder.ClearPayments();

        if (result.IsFailure)
        {
            return FailFrom(result);
        }

        return RefreshState("Draft payments cleared.");
    }

    public CheckoutPresenterOperationResult ClearDraft()
    {
        return Initialize();
    }

    public CheckoutPresenterOperationResult RefreshState()
    {
        return RefreshState("Checkout state refreshed.");
    }

    private CheckoutPresenterOperationResult RefreshState(string statusMessage)
    {
        Result<PosCheckoutDraftTransaction> transactionResult = _builder.GetTransaction();

        if (transactionResult.IsFailure)
        {
            return FailFrom(transactionResult);
        }

        PosCheckoutDraftTransaction transaction = transactionResult.Value;

        IReadOnlyList<CheckoutLineViewModel> lines = transaction.Lines
            .Select(MapLine)
            .ToList();

        bool hasLines = lines.Count > 0;
        bool hasPayments = transaction.Payments.Count > 0;
        bool hasProductInput = !string.IsNullOrWhiteSpace(_productInputText);

        Result<CheckoutCompletionReadiness> completionReadinessResult =
            _completionBoundary.Evaluate(transaction);

        if (completionReadinessResult.IsFailure)
        {
            return FailFrom(completionReadinessResult);
        }

        CheckoutCompletionReadiness completionReadiness = completionReadinessResult.Value;

        State = new CheckoutViewModel(
            CashierUserId: _cashierUserId,
            CashierSessionId: _sessionContext?.CashierSessionId,
            CashierDisplayName: _sessionContext?.CashierDisplayName ?? string.Empty,
            CashierRoleName: _sessionContext?.CashierRoleName ?? string.Empty,
            CashierSessionText: _sessionContext?.ShiftLine ?? "No cashier shift context bound.",
            CheckoutModeText: _sessionContext?.ModeLine ?? "Repository-backed product search · Sale completion locked",
            ReceiptNumber: _receiptNumber,
            ProductInputText: _productInputText,
            ProductResultText: _productResultText,
            PreparedProduct: _preparedProduct,
            Lines: lines,
            Totals: MapTotals(transaction.Totals),
            PaymentCount: transaction.Payments.Count,
            CashAmountText: _cashAmountText,
            CashAmountCents: _cashAmountCents,
            CanSearchProduct: _productSearchBoundary is not null && hasProductInput,
            CanAddPreparedLine: hasProductInput || _preparedProductLineInput is not null,
            CanAddPreparedProductLine: _preparedProductLineInput is not null,
            CanRemoveSelectedLine: hasLines,
            CanAddCashPayment: hasLines && _cashAmountCents > 0,
            CanClearPayments: hasPayments,
            CanCompleteSale: false,
            CompletionReadiness: MapCompletionReadiness(completionReadiness),
            StatusMessage: statusMessage,
            HasError: false,
            ErrorCode: null,
            ErrorMessage: null);

        return new CheckoutPresenterOperationResult(
            IsSuccess: true,
            ErrorCode: null,
            ErrorMessage: null,
            State: State);
    }

    private CheckoutPresenterOperationResult FailFrom(Result result)
    {
        return Fail(result.ErrorDetail.Code, result.Error);
    }

    private CheckoutPresenterOperationResult FailFrom<T>(Result<T> result)
    {
        return Fail(result.ErrorDetail.Code, result.Error);
    }

    private CheckoutPresenterOperationResult Fail(string code, string message)
    {
        State = State with
        {
            HasError = true,
            ErrorCode = code,
            ErrorMessage = message,
            StatusMessage = message
        };

        return new CheckoutPresenterOperationResult(
            IsSuccess: false,
            ErrorCode: code,
            ErrorMessage: message,
            State: State);
    }

    private static CheckoutCompletionReadinessViewModel MapCompletionReadiness(
        CheckoutCompletionReadiness readiness)
    {
        return new CheckoutCompletionReadinessViewModel(
            HasCashierUser: readiness.HasCashierUser,
            HasCashierSession: readiness.HasCashierSession,
            HasReceiptNumber: readiness.HasReceiptNumber,
            HasLines: readiness.HasLines,
            HasPayments: readiness.HasPayments,
            IsPaidInFull: readiness.IsPaidInFull,
            CanBuildCompletionRequest: readiness.CanBuildCompletionRequest,
            CanSubmitSale: readiness.CanSubmitSale,
            SummaryText: readiness.SummaryText,
            DetailText: readiness.DetailText);
    }

    private static CheckoutLineViewModel MapLine(PosCheckoutDraftLine line)
    {
        return new CheckoutLineViewModel(
            LineId: line.Id,
            ProductId: line.ProductId,
            ProductName: line.ProductNameSnapshot,
            Barcode: line.BarcodeSnapshot,
            UnitName: line.UnitNameSnapshot,
            QuantityX1000: line.QuantityX1000,
            QuantityText: FormatQuantity(line.QuantityX1000),
            UnitPriceCents: line.UnitPriceCents,
            UnitPriceText: FormatMoney(line.UnitPriceCents),
            CostPriceCents: line.CostPriceCents,
            DiscountCents: line.DiscountCents,
            DiscountText: FormatMoney(line.DiscountCents),
            GrossCents: line.GrossCents,
            GrossText: FormatMoney(line.GrossCents),
            LineTotalCents: line.LineTotalCents,
            LineTotalText: FormatMoney(line.LineTotalCents),
            TrackStock: line.TrackStock);
    }

    private static CheckoutPreparedProductViewModel MapPreparedProduct(ProductLookupReadModel product)
    {
        return new CheckoutPreparedProductViewModel(
            ProductId: product.Id,
            ProductName: product.Name,
            Sku: product.Sku,
            Barcode: product.Barcode,
            UnitName: product.UnitName,
            UnitPriceCents: product.SellingPrice.Cents,
            UnitPriceText: FormatMoney(product.SellingPrice.Cents),
            CostPriceCents: product.CostPrice?.Cents,
            CostPriceText: product.CostPrice is null
                ? "PHP 0.00"
                : FormatMoney(product.CostPrice.Value.Cents),
            StockOnHandX1000: product.StockOnHand.ValueX1000,
            StockOnHandText: FormatQuantity(product.StockOnHand.ValueX1000),
            TrackStock: product.TrackStock,
            IsActive: product.IsActive);
    }

    private static CheckoutTotalsViewModel MapTotals(PosCheckoutDraftTotals totals)
    {
        return new CheckoutTotalsViewModel(
            SubtotalCents: totals.SubtotalCents,
            SubtotalText: FormatMoney(totals.SubtotalCents),
            SaleDiscountCents: totals.SaleDiscountCents,
            SaleDiscountText: FormatMoney(totals.SaleDiscountCents),
            TaxCents: totals.TaxCents,
            TaxText: FormatMoney(totals.TaxCents),
            TotalCents: totals.TotalCents,
            TotalText: FormatMoney(totals.TotalCents),
            AmountPaidCents: totals.AmountPaidCents,
            AmountPaidText: FormatMoney(totals.AmountPaidCents),
            ChangeCents: totals.ChangeCents,
            ChangeText: FormatMoney(totals.ChangeCents),
            BalanceDueCents: totals.BalanceDueCents,
            BalanceDueText: FormatMoney(totals.BalanceDueCents));
    }

    private static string FormatMoney(long cents)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "PHP {0:N2}",
            cents / 100m);
    }

    private static string FormatQuantity(long quantityX1000)
    {
        return (quantityX1000 / 1000m).ToString("0.###", CultureInfo.InvariantCulture);
    }
}





