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

// Deterministic VAT bucket calculator. Does not claim legal VAT correctness.
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

        long vatableSalesCents = 0;
        long vatAmountCents = 0;
        long vatExemptSalesCents = 0;
        long zeroRatedSalesCents = 0;
        long nonVatSalesCents = 0;

        if (request.TaxpayerMode == OfficialDocumentTaxBucketDraftTaxpayerMode.NonVatRegistered)
        {
            foreach (OfficialDocumentTaxBucketCalculationLine line in request.Lines)
            {
                nonVatSalesCents = checked(nonVatSalesCents + line.LineTotalCents);
            }
        }
        else
        {
            foreach (OfficialDocumentTaxBucketCalculationLine line in request.Lines)
            {
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

                    case OfficialDocumentLineTaxClassification.NonVat:
                        nonVatSalesCents = checked(nonVatSalesCents + line.LineTotalCents);
                        break;

                    default:
                        return Result<OfficialDocumentTaxBucketDraft>.Fail(Error.Validation(
                            "OfficialDocumentTaxBucketEngine.UnsupportedClassification",
                            "Official document line tax classification is unsupported."));
                }
            }
        }

        if (request.SaleLevelDiscountCents > 0 && request.TaxpayerMode == OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered)
        {
            Result<OfficialDocumentTaxBucketDraft> adjusted =
                ApplySaleLevelDiscountToVatableBuckets(
                    vatableSalesCents,
                    vatAmountCents,
                    request.SaleLevelDiscountCents);

            if (adjusted.IsFailure)
            {
                return adjusted;
            }

            vatableSalesCents = ReadBucketAmount(adjusted.Value, OfficialDocumentTaxBucketDraftKind.VatableSales);
            vatAmountCents = ReadBucketAmount(adjusted.Value, OfficialDocumentTaxBucketDraftKind.VatAmount);
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

        if (nonVatSalesCents > 0)
        {
            buckets.Add(new OfficialDocumentTaxBucketDraftBucket(
                OfficialDocumentTaxBucketDraftKind.NonVatSales,
                nonVatSalesCents,
                0,
                null));
        }

        if (buckets.Count == 0)
        {
            buckets.Add(new OfficialDocumentTaxBucketDraftBucket(
                OfficialDocumentTaxBucketDraftKind.NonVatSales,
                0,
                0,
                "Zero-amount engineering bucket placeholder."));
        }

        OfficialDocumentTaxBucketDraft draft = new(
            request.TaxpayerMode,
            buckets,
            request.TaxpayerMode == OfficialDocumentTaxBucketDraftTaxpayerMode.NonVatRegistered
                ? SalrynOfficialTaxBucketPolicy.NonVatRegisteredStatement
                : null,
            new[]
            {
                SalrynOfficialTaxBucketPolicy.EngineBoundaryNote,
                SalrynOfficialTaxBucketPolicy.NoLegalVatClaimNote,
                SalrynOfficialTaxBucketPolicy.NoOfficialOutputRenderingNote
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

    private static Result<OfficialDocumentTaxBucketDraft> ApplySaleLevelDiscountToVatableBuckets(
        long vatableSalesCents,
        long vatAmountCents,
        long saleLevelDiscountCents)
    {
        long vatableGross = checked(vatableSalesCents + vatAmountCents);

        if (saleLevelDiscountCents > vatableGross)
        {
            return Result<OfficialDocumentTaxBucketDraft>.Fail(Error.Validation(
                "OfficialDocumentTaxBucketEngine.SaleDiscountExceedsVatableGross",
                "Sale-level discount cannot exceed vatable gross for engineering bucket calculation."));
        }

        long adjustedGross = checked(vatableGross - saleLevelDiscountCents);
        long adjustedVat = ComputeInclusiveVatAmountCents(adjustedGross);
        long adjustedNet = checked(adjustedGross - adjustedVat);

        return Result<OfficialDocumentTaxBucketDraft>.Success(new OfficialDocumentTaxBucketDraft(
            OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered,
            new[]
            {
                new OfficialDocumentTaxBucketDraftBucket(
                    OfficialDocumentTaxBucketDraftKind.VatableSales,
                    adjustedNet,
                    0,
                    null),
                new OfficialDocumentTaxBucketDraftBucket(
                    OfficialDocumentTaxBucketDraftKind.VatAmount,
                    0,
                    adjustedVat,
                    null)
            },
            null,
            Array.Empty<string>()));
    }

    private static long ReadBucketAmount(
        OfficialDocumentTaxBucketDraft draft,
        OfficialDocumentTaxBucketDraftKind kind)
    {
        foreach (OfficialDocumentTaxBucketDraftBucket bucket in draft.Buckets)
        {
            if (bucket.Kind == kind)
            {
                return kind == OfficialDocumentTaxBucketDraftKind.VatAmount
                    ? bucket.TaxAmountCents
                    : bucket.SalesAmountCents;
            }
        }

        return 0;
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
}
