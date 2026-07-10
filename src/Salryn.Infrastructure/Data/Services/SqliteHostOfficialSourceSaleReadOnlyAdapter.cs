using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Official;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.Services;
using Salryn.Infrastructure.Data.Repositories.Sales;

namespace Salryn.Infrastructure.Data.Services;

/// <summary>
/// RMO-BIR-M9 source-sale read-only adapter.
/// SELECT-only via sale read repository. Never mutates sales/sequence/EJ/shift/inventory.
/// </summary>
public sealed class SqliteHostOfficialSourceSaleReadOnlyAdapter : IHostOfficialSourceSaleReadOnlyAdapter
{
    private readonly ISaleReadRepository _saleReadRepository;

    public SqliteHostOfficialSourceSaleReadOnlyAdapter(SqliteConnectionFactory connectionFactory)
        : this(new SqliteSaleReadRepository(connectionFactory))
    {
    }

    public SqliteHostOfficialSourceSaleReadOnlyAdapter(ISaleReadRepository saleReadRepository)
    {
        _saleReadRepository = saleReadRepository;
    }

    public async Task<Result<OfficialSourceSaleReadOnlyResult>> ReadAsync(
        OfficialSourceSaleReadOnlyRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        List<OfficialSourceSaleReadOnlyWarning> warnings = new()
        {
            new(
                "HostOfficialSourceSaleReadOnly.InternalOnly",
                OfficialSafetyConstants.InternalOnlyNote),
            new(
                "HostOfficialSourceSaleReadOnly.NoMutation",
                "Read-only: sale, sequence, EJ, shift, payment, and inventory are not mutated."),
            new(
                "HostOfficialSourceSaleReadOnly.NoOfficialOutput",
                "No official output is generated from this adapter.")
        };

        if (request is null || request.SaleId == Guid.Empty)
        {
            return Result<OfficialSourceSaleReadOnlyResult>.Fail(Error.Validation(
                "HostOfficialSourceSaleReadOnly.SaleIdRequired",
                "A valid operational sale id is required for the read-only adapter."));
        }

        Result<SaleDetailReadModel?> saleResult =
            await _saleReadRepository.FindSaleByIdAsync(request.SaleId, cancellationToken)
                .ConfigureAwait(false);

        if (saleResult.IsFailure)
        {
            return Result<OfficialSourceSaleReadOnlyResult>.Fail(saleResult.ErrorDetail);
        }

        SaleDetailReadModel? sale = saleResult.Value;
        if (sale is null)
        {
            return Result<OfficialSourceSaleReadOnlyResult>.Fail(Error.NotFound(
                "HostOfficialSourceSaleReadOnly.SaleNotFound",
                "Operational sale was not found for read-only dry-run."));
        }

        List<OfficialSourceSaleReadOnlyBlocker> blockers = new();
        if (sale.Status == SaleStatus.Voided)
        {
            warnings.Add(new OfficialSourceSaleReadOnlyWarning(
                "HostOfficialSourceSaleReadOnly.SaleVoided",
                "Source sale is voided; dry-run package is still non-official and non-mutating."));
        }

        List<OfficialSourceSaleReadOnlyLine> lines = sale.Items
            .Select(i => new OfficialSourceSaleReadOnlyLine(
                SkuOrCode: i.BarcodeSnapshot ?? i.ProductId.ToString("N")[..8],
                Description: i.ProductNameSnapshot,
                Quantity: i.Quantity.ToUnits(),
                UnitPrice: i.UnitPrice.ToPesos(),
                LineTotal: i.LineTotal.ToPesos(),
                TaxBucketCode: null))
            .ToList();

        if (lines.Count == 0)
        {
            blockers.Add(new OfficialSourceSaleReadOnlyBlocker(
                "HostOfficialSourceSaleReadOnly.NoLines",
                "Operational sale has no line items; dry-run plan shape may be limited."));
        }

        List<OfficialSourceSaleReadOnlyTender> tenders = sale.Payments
            .Select(p => new OfficialSourceSaleReadOnlyTender(
                Method: p.Method.ToString(),
                Amount: p.Amount.ToPesos(),
                PaidAtUtc: p.PaidAt))
            .ToList();

        OfficialSourceSaleReadOnlyTaxSummary tax = new(
            Subtotal: sale.Subtotal.ToPesos(),
            Discount: sale.Discount.ToPesos(),
            TaxTotal: sale.Tax.ToPesos(),
            GrandTotal: sale.Total.ToPesos(),
            CurrencyCode: "PHP");

        OfficialSourceSaleReadOnlySnapshot snapshot = new(
            SaleId: sale.Id,
            OperationalReceiptNo: sale.ReceiptNo,
            SaleDateUtc: sale.SaleDate,
            CashierDisplayName: sale.CashierDisplayName,
            Status: sale.Status.ToString(),
            VoidReason: sale.VoidReason,
            Lines: lines,
            Tenders: tenders,
            TaxSummary: tax);

        string documentKind = string.IsNullOrWhiteSpace(request.DocumentKindHint)
            ? "OFFICIAL_INVOICE"
            : request.DocumentKindHint.Trim();

        OfficialDryRunPostingRequest postingRequest = new(
            DocumentKind: documentKind,
            OperationalPosNumberHint: sale.ReceiptNo,
            TaxpayerNameHint: null,
            Lines: lines.Select(l => new OfficialDryRunLineItem(
                l.SkuOrCode,
                l.Description,
                l.Quantity,
                l.UnitPrice,
                l.LineTotal,
                l.TaxBucketCode)).ToList(),
            Totals: new OfficialDryRunTotals(
                Subtotal: tax.Subtotal,
                TaxTotal: tax.TaxTotal,
                GrandTotal: tax.GrandTotal,
                CurrencyCode: tax.CurrencyCode));

        OfficialSourceSaleReadOnlyResult result = new(
            ContractVersion: OfficialContractVersions.M3,
            DryRunOnly: true,
            NonOfficial: true,
            SourceSaleReadOnly: true,
            LiveOfficialPostingAllowed: false,
            OfficialCheckoutAllocationEnabled: false,
            CheckoutUsesAllocator: false,
            SequenceWillMutate: false,
            SalesWillMutate: false,
            EjWillAppendLive: false,
            OutputWillGenerate: false,
            OfficialReceiptIssued: false,
            ComplianceClaimed: false,
            Snapshot: snapshot,
            PostingRequest: postingRequest,
            Blockers: blockers,
            Warnings: warnings,
            Summary:
                "Operational sale read-only snapshot built for internal dry-run. " +
                OfficialSafetyConstants.NoOfficialNumberIssued + " " +
                "No sales/sequence/EJ mutation.");

        return Result<OfficialSourceSaleReadOnlyResult>.Success(result);
    }
}
