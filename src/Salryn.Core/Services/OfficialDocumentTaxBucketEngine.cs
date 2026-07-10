using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Core.Services;

public sealed record OfficialDocumentTaxBucketCalculationLine(
    long LineTotalCents,
    long LineDiscountCents,
    OfficialDocumentLineTaxClassification Classification);

public sealed record OfficialDocumentTaxBucketCalculationRequest(
    OfficialDocumentTaxBucketDraftTaxpayerMode TaxpayerMode,
    IReadOnlyList<OfficialDocumentTaxBucketCalculationLine> Lines,
    long SaleLevelDiscountCents);

// Deterministic VAT / non-VAT bucket calculator. Does not claim legal VAT correctness.
public sealed class OfficialDocumentTaxBucketEngine
{
    private readonly OfficialDocumentTaxBucketDraftValidator _validator = new();

    public Result<OfficialDocumentTaxBucketDraft> Calculate(OfficialDocumentTaxBucketCalculationRequest request)
    {
        Result validation = ValidateRequest(request);

        if (validation.IsFailure)
        {
            return Result<OfficialDocumentTaxBucketDraft>.Fail(validation.ErrorDetail);
        }

        OfficialDocumentTaxBucketDraftTaxpayerMode mode = NormalizeMode(request.TaxpayerMode);

        return mode switch
        {
            OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered =>
                CalculateVatRegistered(request),
            OfficialDocumentTaxBucketDraftTaxpayerMode.NonVatPercentageTax =>
                CalculateNonVatPercentageTax(request),
            OfficialDocumentTaxBucketDraftTaxpayerMode.NonVatExempt =>
                CalculateNonVatExempt(request),
            _ => Result<OfficialDocumentTaxBucketDraft>.Fail(Error.Validation(
                "OfficialDocumentTaxBucketEngine.TaxpayerModeRequired",
                "Known taxpayer mode is required."))
        };
    }

    private Result<OfficialDocumentTaxBucketDraft> CalculateVatRegistered(
        OfficialDocumentTaxBucketCalculationRequest request)
    {
        long vatableSalesCents = 0;
        long vatAmountCents = 0;
        long vatExemptSalesCents = 0;
        long zeroRatedSalesCents = 0;

        foreach (OfficialDocumentTaxBucketCalculationLine line in request.Lines)
        {
            if (!SalrynTaxpayerTaxPolicy.IsLineClassificationAllowed(
                    OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered,
                    line.Classification))
            {
                return Result<OfficialDocumentTaxBucketDraft>.Fail(Error.Validation(
                    "OfficialDocumentTaxBucketEngine.ClassificationNotAllowedForMode",
                    "Line tax classification is not allowed for VAT-registered taxpayer mode."));
            }

            switch (line.Classification)
            {
                case OfficialDocumentLineTaxClassification.Vatable:
                {
                    long lineVat = ComputeInclusiveVatAmountCents(line.LineTotalCents);
                    vatAmountCents = checked(vatAmountCents + lineVat);
                    vatableSalesCents = checked(vatableSalesCents + (line.LineTotalCents - lineVat));
                    break;
                }

                case OfficialDocumentLineTaxClassification.VatExempt:
                    vatExemptSalesCents = checked(vatExemptSalesCents + line.LineTotalCents);
                    break;

                case OfficialDocumentLineTaxClassification.ZeroRated:
                    zeroRatedSalesCents = checked(zeroRatedSalesCents + line.LineTotalCents);
                    break;
            }
        }

        if (request.SaleLevelDiscountCents > 0)
        {
            Result<DiscountAllocation> allocated = AllocateSaleLevelDiscountProportionally(
                request,
                includeClassifications: c => c is OfficialDocumentLineTaxClassification.Vatable
                    or OfficialDocumentLineTaxClassification.VatExempt
                    or OfficialDocumentLineTaxClassification.ZeroRated);

            if (allocated.IsFailure)
            {
                return Result<OfficialDocumentTaxBucketDraft>.Fail(allocated.ErrorDetail);
            }

            // Recompute VATable from discounted VATable gross; exempt/zero-rated take proportional share only.
            long vatableGross = SumLineGross(request.Lines, OfficialDocumentLineTaxClassification.Vatable);
            long exemptGross = SumLineGross(request.Lines, OfficialDocumentLineTaxClassification.VatExempt);
            long zeroGross = SumLineGross(request.Lines, OfficialDocumentLineTaxClassification.ZeroRated);
            long totalGross = checked(vatableGross + exemptGross + zeroGross);

            if (totalGross <= 0)
            {
                return Result<OfficialDocumentTaxBucketDraft>.Fail(Error.Validation(
                    "OfficialDocumentTaxBucketEngine.SaleDiscountWithoutGross",
                    "Sale-level discount requires positive line gross."));
            }

            long discount = request.SaleLevelDiscountCents;
            long vatableDiscount = Proportion(discount, vatableGross, totalGross);
            long exemptDiscount = Proportion(discount, exemptGross, totalGross);
            long zeroDiscount = checked(discount - vatableDiscount - exemptDiscount);

            long adjustedVatableGross = checked(vatableGross - vatableDiscount);
            vatAmountCents = ComputeInclusiveVatAmountCents(adjustedVatableGross);
            vatableSalesCents = checked(adjustedVatableGross - vatAmountCents);
            vatExemptSalesCents = checked(exemptGross - exemptDiscount);
            zeroRatedSalesCents = checked(zeroGross - zeroDiscount);
        }

        List<OfficialDocumentTaxBucketDraftBucket> buckets = new();

        if (vatableSalesCents > 0 || vatAmountCents > 0)
        {
            buckets.Add(new OfficialDocumentTaxBucketDraftBucket(
                OfficialDocumentTaxBucketDraftKind.VatableSales,
                vatableSalesCents,
                0,
                "Engineering vatable sales net of inclusive VAT."));

            buckets.Add(new OfficialDocumentTaxBucketDraftBucket(
                OfficialDocumentTaxBucketDraftKind.VatAmount,
                0,
                vatAmountCents,
                $"Inclusive VAT at {SalrynOfficialTaxBucketPolicy.VatInclusiveRatePercent}% engineering rate."));
        }

        if (vatExemptSalesCents > 0)
        {
            buckets.Add(new OfficialDocumentTaxBucketDraftBucket(
                OfficialDocumentTaxBucketDraftKind.VatExemptSales,
                vatExemptSalesCents,
                0,
                null));
        }

        if (zeroRatedSalesCents > 0)
        {
            buckets.Add(new OfficialDocumentTaxBucketDraftBucket(
                OfficialDocumentTaxBucketDraftKind.ZeroRatedSales,
                zeroRatedSalesCents,
                0,
                null));
        }

        if (buckets.Count == 0)
        {
            buckets.Add(new OfficialDocumentTaxBucketDraftBucket(
                OfficialDocumentTaxBucketDraftKind.VatableSales,
                0,
                0,
                "Zero-amount engineering bucket placeholder."));
            buckets.Add(new OfficialDocumentTaxBucketDraftBucket(
                OfficialDocumentTaxBucketDraftKind.VatAmount,
                0,
                0,
                null));
        }

        return Finalize(
            OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered,
            buckets,
            nonVatStatement: null);
    }

    private Result<OfficialDocumentTaxBucketDraft> CalculateNonVatPercentageTax(
        OfficialDocumentTaxBucketCalculationRequest request)
    {
        long ssptSalesCents = 0;
        long exemptSalesCents = 0;

        foreach (OfficialDocumentTaxBucketCalculationLine line in request.Lines)
        {
            if (!SalrynTaxpayerTaxPolicy.IsLineClassificationAllowed(
                    OfficialDocumentTaxBucketDraftTaxpayerMode.NonVatPercentageTax,
                    line.Classification))
            {
                return Result<OfficialDocumentTaxBucketDraft>.Fail(Error.Validation(
                    "OfficialDocumentTaxBucketEngine.ClassificationNotAllowedForMode",
                    "Line tax classification is not allowed for non-VAT percentage-tax taxpayer mode."));
            }

            if (line.Classification == OfficialDocumentLineTaxClassification.NonVat)
            {
                ssptSalesCents = checked(ssptSalesCents + line.LineTotalCents);
            }
            else if (line.Classification == OfficialDocumentLineTaxClassification.NonVatExempt)
            {
                exemptSalesCents = checked(exemptSalesCents + line.LineTotalCents);
            }
        }

        if (request.SaleLevelDiscountCents > 0)
        {
            long totalGross = checked(ssptSalesCents + exemptSalesCents);
            if (request.SaleLevelDiscountCents > totalGross)
            {
                return Result<OfficialDocumentTaxBucketDraft>.Fail(Error.Validation(
                    "OfficialDocumentTaxBucketEngine.SaleDiscountExceedsNonVatGross",
                    "Sale-level discount cannot exceed non-VAT gross."));
            }

            long ssptDiscount = Proportion(request.SaleLevelDiscountCents, ssptSalesCents, totalGross);
            long exemptDiscount = checked(request.SaleLevelDiscountCents - ssptDiscount);
            ssptSalesCents = checked(ssptSalesCents - ssptDiscount);
            exemptSalesCents = checked(exemptSalesCents - exemptDiscount);
        }

        List<OfficialDocumentTaxBucketDraftBucket> buckets = new();

        if (ssptSalesCents > 0)
        {
            buckets.Add(new OfficialDocumentTaxBucketDraftBucket(
                OfficialDocumentTaxBucketDraftKind.SsptSales,
                ssptSalesCents,
                0,
                "Sales subject to percentage tax (SSPT) — engineering bucket."));
        }

        if (exemptSalesCents > 0)
        {
            buckets.Add(new OfficialDocumentTaxBucketDraftBucket(
                OfficialDocumentTaxBucketDraftKind.NonVatExemptSales,
                exemptSalesCents,
                0,
                "Non-VAT exempt sales — engineering bucket."));
        }

        if (buckets.Count == 0)
        {
            buckets.Add(new OfficialDocumentTaxBucketDraftBucket(
                OfficialDocumentTaxBucketDraftKind.SsptSales,
                0,
                0,
                "Zero-amount engineering bucket placeholder."));
        }

        return Finalize(
            OfficialDocumentTaxBucketDraftTaxpayerMode.NonVatPercentageTax,
            buckets,
            nonVatStatement: SalrynOfficialTaxBucketPolicy.NonVatRegisteredStatement);
    }

    private Result<OfficialDocumentTaxBucketDraft> CalculateNonVatExempt(
        OfficialDocumentTaxBucketCalculationRequest request)
    {
        long exemptSalesCents = 0;

        foreach (OfficialDocumentTaxBucketCalculationLine line in request.Lines)
        {
            if (!SalrynTaxpayerTaxPolicy.IsLineClassificationAllowed(
                    OfficialDocumentTaxBucketDraftTaxpayerMode.NonVatExempt,
                    line.Classification))
            {
                return Result<OfficialDocumentTaxBucketDraft>.Fail(Error.Validation(
                    "OfficialDocumentTaxBucketEngine.ClassificationNotAllowedForMode",
                    "Line tax classification is not allowed for non-VAT exempt taxpayer mode."));
            }

            exemptSalesCents = checked(exemptSalesCents + line.LineTotalCents);
        }

        if (request.SaleLevelDiscountCents > 0)
        {
            if (request.SaleLevelDiscountCents > exemptSalesCents)
            {
                return Result<OfficialDocumentTaxBucketDraft>.Fail(Error.Validation(
                    "OfficialDocumentTaxBucketEngine.SaleDiscountExceedsNonVatGross",
                    "Sale-level discount cannot exceed non-VAT exempt gross."));
            }

            exemptSalesCents = checked(exemptSalesCents - request.SaleLevelDiscountCents);
        }

        List<OfficialDocumentTaxBucketDraftBucket> buckets =
        [
            new OfficialDocumentTaxBucketDraftBucket(
                OfficialDocumentTaxBucketDraftKind.NonVatExemptSales,
                exemptSalesCents,
                0,
                "Non-VAT exempt-only taxpayer path — engineering bucket.")
        ];

        return Finalize(
            OfficialDocumentTaxBucketDraftTaxpayerMode.NonVatExempt,
            buckets,
            nonVatStatement: SalrynOfficialTaxBucketPolicy.NonVatRegisteredStatement);
    }

    private Result<OfficialDocumentTaxBucketDraft> Finalize(
        OfficialDocumentTaxBucketDraftTaxpayerMode mode,
        List<OfficialDocumentTaxBucketDraftBucket> buckets,
        string? nonVatStatement)
    {
        OfficialDocumentTaxBucketDraft draft = new(
            mode,
            buckets,
            nonVatStatement,
            new[]
            {
                SalrynOfficialTaxBucketPolicy.EngineBoundaryNote,
                SalrynOfficialTaxBucketPolicy.NoLegalVatClaimNote,
                SalrynOfficialTaxBucketPolicy.NoOfficialOutputRenderingNote,
                SalrynInvoiceDocumentPolicy.InvoiceFirstBoundaryNote
            });

        Result validatorResult = _validator.ValidateEngineComputed(draft);

        if (validatorResult.IsFailure)
        {
            return Result<OfficialDocumentTaxBucketDraft>.Fail(validatorResult.ErrorDetail);
        }

        return Result<OfficialDocumentTaxBucketDraft>.Success(draft);
    }

    public static long ComputeInclusiveVatAmountCents(long taxInclusiveLineTotalCents)
    {
        if (taxInclusiveLineTotalCents <= 0)
        {
            return 0;
        }

        decimal gross = taxInclusiveLineTotalCents;
        decimal rate = SalrynOfficialTaxBucketPolicy.VatInclusiveRatePercent;
        decimal vat = gross * rate / (100m + rate);

        return RoundHalfAwayFromZero(vat);
    }

    private static OfficialDocumentTaxBucketDraftTaxpayerMode NormalizeMode(
        OfficialDocumentTaxBucketDraftTaxpayerMode mode)
    {
        // NonVatRegistered and NonVatPercentageTax share value 2.
        if (mode == OfficialDocumentTaxBucketDraftTaxpayerMode.NonVatRegistered)
        {
            return OfficialDocumentTaxBucketDraftTaxpayerMode.NonVatPercentageTax;
        }

        return mode;
    }

    private static long SumLineGross(
        IReadOnlyList<OfficialDocumentTaxBucketCalculationLine> lines,
        OfficialDocumentLineTaxClassification classification)
    {
        long sum = 0;
        foreach (OfficialDocumentTaxBucketCalculationLine line in lines)
        {
            if (line.Classification == classification)
            {
                sum = checked(sum + line.LineTotalCents);
            }
        }

        return sum;
    }

    private static long Proportion(long amount, long part, long whole)
    {
        if (whole <= 0 || part <= 0 || amount <= 0)
        {
            return 0;
        }

        return (long)decimal.Round((decimal)amount * part / whole, 0, MidpointRounding.AwayFromZero);
    }

    private static Result<DiscountAllocation> AllocateSaleLevelDiscountProportionally(
        OfficialDocumentTaxBucketCalculationRequest request,
        Func<OfficialDocumentLineTaxClassification, bool> includeClassifications)
    {
        long gross = 0;
        foreach (OfficialDocumentTaxBucketCalculationLine line in request.Lines)
        {
            if (includeClassifications(line.Classification))
            {
                gross = checked(gross + line.LineTotalCents);
            }
        }

        if (request.SaleLevelDiscountCents > gross)
        {
            return Result<DiscountAllocation>.Fail(Error.Validation(
                "OfficialDocumentTaxBucketEngine.SaleDiscountExceedsVatableGross",
                "Sale-level discount cannot exceed applicable gross for engineering bucket calculation."));
        }

        return Result<DiscountAllocation>.Success(new DiscountAllocation(gross, request.SaleLevelDiscountCents));
    }

    private static long RoundHalfAwayFromZero(decimal value)
    {
        return (long)decimal.Round(value, 0, MidpointRounding.AwayFromZero);
    }

    private static Result ValidateRequest(OfficialDocumentTaxBucketCalculationRequest? request)
    {
        if (request is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentTaxBucketEngine.RequestRequired",
                "Tax bucket calculation request is required."));
        }

        if (!Enum.IsDefined(typeof(OfficialDocumentTaxBucketDraftTaxpayerMode), request.TaxpayerMode) ||
            request.TaxpayerMode == OfficialDocumentTaxBucketDraftTaxpayerMode.Unknown)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentTaxBucketEngine.TaxpayerModeRequired",
                "Known taxpayer mode is required."));
        }

        if (request.Lines is null || request.Lines.Count == 0)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentTaxBucketEngine.LinesRequired",
                "At least one line is required."));
        }

        if (request.SaleLevelDiscountCents < 0)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentTaxBucketEngine.NegativeSaleDiscount",
                "Sale-level discount cannot be negative."));
        }

        foreach (OfficialDocumentTaxBucketCalculationLine line in request.Lines)
        {
            if (line is null)
            {
                return Result.Fail(Error.Validation(
                    "OfficialDocumentTaxBucketEngine.LineRequired",
                    "Tax bucket calculation line cannot be null."));
            }

            if (line.LineTotalCents < 0 || line.LineDiscountCents < 0)
            {
                return Result.Fail(Error.Validation(
                    "OfficialDocumentTaxBucketEngine.NegativeLineAmount",
                    "Line amounts cannot be negative."));
            }

            if (!Enum.IsDefined(typeof(OfficialDocumentLineTaxClassification), line.Classification))
            {
                return Result.Fail(Error.Validation(
                    "OfficialDocumentTaxBucketEngine.InvalidClassification",
                    "Line tax classification is invalid."));
            }
        }

        return Result.Success();
    }

    private sealed record DiscountAllocation(long GrossCents, long DiscountCents);
}
