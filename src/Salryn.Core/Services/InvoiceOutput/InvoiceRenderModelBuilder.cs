using Salryn.Core.Common;

namespace Salryn.Core.Services.InvoiceOutput;

/// <summary>
/// Builds a renderer model from a verified immutable snapshot only.
/// </summary>
public sealed class InvoiceRenderModelBuilder
{
    public Result<InvoiceRenderModel> Build(ParsedOfficialInvoiceSnapshot snapshot, InvoiceOutputMode mode)
    {
        if (snapshot is null)
        {
            return Result<InvoiceRenderModel>.Fail(Error.Validation(
                "InvoiceRender.SnapshotRequired",
                "Parsed snapshot is required."));
        }

        List<string> missingOptional = new();
        List<string> validationNotes = new();

        // Buyer fields are not in V1 snapshot — blank spaces only.
        missingOptional.Add("BuyerName");
        missingOptional.Add("BuyerTin");
        missingOptional.Add("BuyerAddress");

        if (string.IsNullOrWhiteSpace(snapshot.Header.AccreditationNumber))
        {
            missingOptional.Add("AccreditationNumber");
        }

        if (string.IsNullOrWhiteSpace(snapshot.Header.PermitToUseNumber))
        {
            missingOptional.Add("PermitToUseNumber");
        }

        if (string.IsNullOrWhiteSpace(snapshot.Header.MachineIdentificationNumber))
        {
            missingOptional.Add("MachineIdentificationNumber");
        }

        if (snapshot.TaxBuckets is null || snapshot.TaxBuckets.Buckets.Count == 0)
        {
            if (mode == InvoiceOutputMode.ProductionCapable)
            {
                return Result<InvoiceRenderModel>.Fail(Error.Validation(
                    "InvoiceRender.TaxBucketsRequired",
                    "Production-capable invoice output requires immutable tax buckets in the snapshot."));
            }

            validationNotes.Add("Tax buckets missing from snapshot; InternalDemo only.");
        }

        if (snapshot.Payments.Count == 0)
        {
            if (mode == InvoiceOutputMode.ProductionCapable)
            {
                return Result<InvoiceRenderModel>.Fail(Error.Validation(
                    "InvoiceRender.PaymentsRequired",
                    "Production-capable invoice output requires payment evidence in the snapshot."));
            }

            validationNotes.Add("Payments missing from snapshot.");
        }

        string taxpayerMode = snapshot.TaxBuckets?.TaxpayerMode ?? "Unknown";
        bool isVat = IsVatMode(taxpayerMode);
        bool isNonVat = IsNonVatMode(taxpayerMode);

        if (mode == InvoiceOutputMode.ProductionCapable && !isVat && !isNonVat)
        {
            return Result<InvoiceRenderModel>.Fail(Error.Validation(
                "InvoiceRender.TaxpayerModeRequired",
                "Production-capable invoice output requires a known taxpayer mode in the snapshot tax buckets."));
        }

        InvoiceDocumentVariant variant = ResolveVariant(taxpayerMode, snapshot.TaxBuckets?.Buckets);
        string documentTitle = isVat
            ? SalrynInvoiceDocumentPolicy.VatInvoiceDisplayTitle
            : SalrynInvoiceDocumentPolicy.NonVatInvoiceDisplayTitle;
        string registrationPhrase = isVat ? "VAT REG TIN" : "NON-VAT REG TIN";

        string? nonVatDisclaimer = isNonVat
            ? InvoiceOutputConstants.NonVatInputTaxDisclaimer
            : null;

        // Fail closed: non-VAT must never present VAT amount as charged VAT output buckets.
        if (isNonVat && snapshot.TaxBuckets is not null)
        {
            foreach (ParsedBucket bucket in snapshot.TaxBuckets.Buckets)
            {
                if (string.Equals(bucket.Kind, "VatAmount", StringComparison.OrdinalIgnoreCase) &&
                    bucket.TaxAmountCents > 0)
                {
                    return Result<InvoiceRenderModel>.Fail(Error.Validation(
                        "InvoiceRender.NonVatCannotCarryVatAmount",
                        "Non-VAT snapshot must not carry a positive VAT amount bucket."));
                }

                if (string.Equals(bucket.Kind, "VatableSales", StringComparison.OrdinalIgnoreCase) &&
                    bucket.SalesAmountCents > 0)
                {
                    return Result<InvoiceRenderModel>.Fail(Error.Validation(
                        "InvoiceRender.NonVatCannotCarryVatableSales",
                        "Non-VAT snapshot must not carry VATable sales buckets."));
                }
            }
        }

        bool watermark = mode == InvoiceOutputMode.InternalDemo
            || !ProductionOfficialPostingActivation.IsEnabled;

        List<string> boundary = new()
        {
            InvoiceOutputConstants.NoComplianceClaimNote,
            SalrynInvoiceDocumentPolicy.InvoiceFirstBoundaryNote,
            SalrynInvoiceDocumentPolicy.NoAccreditationClaimNote
        };
        boundary.AddRange(validationNotes);

        List<InvoiceRenderLine> lines = snapshot.Lines
            .Select(line => new InvoiceRenderLine(
                line.Description,
                line.UnitName,
                line.QuantityX1000,
                line.UnitPriceCents,
                line.DiscountCents,
                line.LineTotalCents))
            .ToList();

        List<InvoiceRenderPayment> payments = snapshot.Payments
            .Select(p => new InvoiceRenderPayment(p.Method, p.AmountCents))
            .ToList();

        List<InvoiceRenderTaxBucket> buckets = (snapshot.TaxBuckets?.Buckets ?? Array.Empty<ParsedBucket>())
            .Select(b => new InvoiceRenderTaxBucket(b.Kind, b.SalesAmountCents, b.TaxAmountCents))
            .ToList();

        return Result<InvoiceRenderModel>.Success(new InvoiceRenderModel(
            RendererContractVersion: InvoiceOutputConstants.RendererContractVersion,
            Variant: variant,
            DocumentTitle: documentTitle,
            RegistrationPhrase: registrationPhrase,
            OfficialDocumentNumber: snapshot.Header.DocumentNumber,
            TransactionDateTime: snapshot.Header.TransactionDateTime,
            SellerRegisteredName: snapshot.Header.RegisteredTaxpayerName,
            SellerBusinessName: snapshot.Header.BusinessName,
            SellerTin: snapshot.Header.TaxpayerIdentificationNumber,
            SellerBranchCode: snapshot.Header.BranchCode,
            SellerRegisteredAddress: snapshot.Header.RegisteredBusinessAddress,
            MachineSerialNumber: snapshot.Header.MachineSerialNumber,
            MachineIdentificationNumber: snapshot.Header.MachineIdentificationNumber,
            AccreditationNumber: snapshot.Header.AccreditationNumber,
            PermitToUseNumber: snapshot.Header.PermitToUseNumber,
            CashierIdentifier: snapshot.Header.CashierIdentifier,
            TerminalIdentifier: snapshot.Header.TerminalIdentifier,
            BuyerNameSpace: null,
            BuyerTinSpace: null,
            BuyerAddressSpace: null,
            Lines: lines,
            SubtotalCents: snapshot.Amounts.SubtotalCents,
            DiscountCents: snapshot.Amounts.DiscountCents,
            TaxCents: snapshot.Amounts.TaxCents,
            TotalCents: snapshot.Amounts.TotalCents,
            AmountPaidCents: snapshot.Amounts.AmountPaidCents,
            ChangeCents: snapshot.Amounts.ChangeCents,
            Payments: payments,
            TaxpayerMode: taxpayerMode,
            TaxBuckets: buckets,
            NonVatDisclaimer: nonVatDisclaimer,
            MissingOptionalFields: missingOptional,
            BoundaryNotes: boundary,
            RequiresInternalWatermark: watermark,
            InternalWatermark: InvoiceOutputConstants.InternalWatermark,
            SnapshotPayloadHash: snapshot.Identity.SnapshotPayloadHash));
    }

    private static bool IsVatMode(string mode) =>
        string.Equals(mode, "VatRegistered", StringComparison.OrdinalIgnoreCase);

    private static bool IsNonVatMode(string mode) =>
        mode.Contains("NonVat", StringComparison.OrdinalIgnoreCase);

    private static InvoiceDocumentVariant ResolveVariant(
        string taxpayerMode,
        IReadOnlyList<ParsedBucket>? buckets)
    {
        buckets ??= Array.Empty<ParsedBucket>();
        bool hasVatable = Has(buckets, "VatableSales") || Has(buckets, "VatAmount");
        bool hasExempt = Has(buckets, "VatExemptSales");
        bool hasZero = Has(buckets, "ZeroRatedSales");
        bool hasSspt = Has(buckets, "SsptSales") || Has(buckets, "NonVatSales");
        bool hasNonVatExempt = Has(buckets, "NonVatExemptSales");

        if (IsVatMode(taxpayerMode))
        {
            int kinds = (hasVatable ? 1 : 0) + (hasExempt ? 1 : 0) + (hasZero ? 1 : 0);
            if (kinds > 1)
            {
                return InvoiceDocumentVariant.VatMixed;
            }

            if (hasExempt && !hasVatable && !hasZero)
            {
                return InvoiceDocumentVariant.VatExemptOnly;
            }

            if (hasZero && !hasVatable && !hasExempt)
            {
                return InvoiceDocumentVariant.VatZeroRatedOnly;
            }

            return InvoiceDocumentVariant.VatAllVatable;
        }

        if (hasSspt && hasNonVatExempt)
        {
            return InvoiceDocumentVariant.NonVatMixed;
        }

        if (hasNonVatExempt && !hasSspt)
        {
            return InvoiceDocumentVariant.NonVatExemptOnly;
        }

        return InvoiceDocumentVariant.NonVatSsptOnly;
    }

    private static bool Has(IReadOnlyList<ParsedBucket> buckets, string kind) =>
        buckets.Any(b =>
            string.Equals(b.Kind, kind, StringComparison.OrdinalIgnoreCase)
            && (b.SalesAmountCents > 0 || b.TaxAmountCents > 0));
}
