using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Core.Services;

/// <summary>
/// Fail-closed VAT / non-VAT combination matrix for Traditional CRM/POS.
/// Engineering policy only — not legal advice or accreditation.
/// </summary>
public static class SalrynTaxpayerTaxPolicy
{
    public const string PolicyVersion = "TRAD-M1-TAXPAYER-TAX-POLICY-2026-07-10";

    public static OfficialDocumentTaxBucketDraftTaxpayerMode ToEngineTaxpayerMode(
        TaxpayerRegistrationMode registrationMode) =>
        registrationMode switch
        {
            TaxpayerRegistrationMode.VatRegistered =>
                OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered,
            TaxpayerRegistrationMode.NonVatPercentageTax =>
                OfficialDocumentTaxBucketDraftTaxpayerMode.NonVatPercentageTax,
            TaxpayerRegistrationMode.NonVatExempt =>
                OfficialDocumentTaxBucketDraftTaxpayerMode.NonVatExempt,
            _ => OfficialDocumentTaxBucketDraftTaxpayerMode.Unknown
        };

    public static Result RequireKnownRegistrationMode(TaxpayerRegistrationMode? mode)
    {
        if (mode is null || mode == TaxpayerRegistrationMode.Unknown)
        {
            return Result.Fail(Error.Validation(
                "SalrynTaxpayerTaxPolicy.RegistrationModeRequired",
                "Known taxpayer registration mode is required for tax policy evaluation."));
        }

        return Result.Success();
    }

    public static bool IsLineClassificationAllowed(
        TaxpayerRegistrationMode registrationMode,
        ProductTaxClassification classification) =>
        registrationMode switch
        {
            TaxpayerRegistrationMode.VatRegistered =>
                classification is ProductTaxClassification.Vatable
                    or ProductTaxClassification.VatExempt
                    or ProductTaxClassification.ZeroRated,

            TaxpayerRegistrationMode.NonVatPercentageTax =>
                classification is ProductTaxClassification.NonVat
                    or ProductTaxClassification.NonVatExempt,

            TaxpayerRegistrationMode.NonVatExempt =>
                classification is ProductTaxClassification.NonVatExempt,

            _ => false
        };

    public static bool IsLineClassificationAllowed(
        OfficialDocumentTaxBucketDraftTaxpayerMode engineMode,
        OfficialDocumentLineTaxClassification classification) =>
        engineMode switch
        {
            OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered =>
                classification is OfficialDocumentLineTaxClassification.Vatable
                    or OfficialDocumentLineTaxClassification.VatExempt
                    or OfficialDocumentLineTaxClassification.ZeroRated,

            // NonVatRegistered is a legacy alias of NonVatPercentageTax (same underlying value).
            OfficialDocumentTaxBucketDraftTaxpayerMode.NonVatPercentageTax =>
                classification is OfficialDocumentLineTaxClassification.NonVat
                    or OfficialDocumentLineTaxClassification.NonVatExempt,

            OfficialDocumentTaxBucketDraftTaxpayerMode.NonVatExempt =>
                classification is OfficialDocumentLineTaxClassification.NonVatExempt,

            _ => false
        };

    public static Result ValidateLineClassificationsForMode(
        TaxpayerRegistrationMode registrationMode,
        IEnumerable<ProductTaxClassification> classifications)
    {
        Result modeOk = RequireKnownRegistrationMode(registrationMode);
        if (modeOk.IsFailure)
        {
            return modeOk;
        }

        foreach (ProductTaxClassification classification in classifications)
        {
            if (!IsLineClassificationAllowed(registrationMode, classification))
            {
                return Result.Fail(Error.Validation(
                    "SalrynTaxpayerTaxPolicy.LineClassificationNotAllowed",
                    $"Product tax classification '{ProductTaxClassificationMapper.ToDisplayText(classification)}' " +
                    $"is not allowed for taxpayer mode '{TaxpayerRegistrationModeCodes.ToDisplayText(registrationMode)}'."));
            }
        }

        return Result.Success();
    }

    public static OfficialTaxPolicySnapshot Freeze(
        TaxpayerRegistrationMode registrationMode,
        string? additionalNote = null)
    {
        string modeCode = TaxpayerRegistrationModeCodes.ToPersistedCode(registrationMode);
        return new OfficialTaxPolicySnapshot(
            PolicyVersion: PolicyVersion,
            VatRegistrationModeCode: modeCode,
            EngineTaxpayerMode: ToEngineTaxpayerMode(registrationMode).ToString(),
            PrimarySalesDocumentKind: SalrynInvoiceDocumentPolicy.PrimarySalesDocumentKind,
            InvoicePolicyVersion: SalrynInvoiceDocumentPolicy.PolicyVersion,
            EngineTaxModeCode: SalrynOfficialTaxBucketPolicy.EngineTaxModeCode,
            VatInclusiveRatePercent: SalrynOfficialTaxBucketPolicy.VatInclusiveRatePercent,
            Notes: new[]
            {
                "Immutable tax policy snapshot for official evidence freeze.",
                "Not a BIR compliance or accreditation claim.",
                additionalNote ?? string.Empty
            }.Where(n => !string.IsNullOrWhiteSpace(n)).ToArray());
    }
}

/// <summary>
/// Frozen tax/invoice policy fields for later official sale / EJ evidence.
/// Immutable value object — do not mutate after post.
/// </summary>
public sealed record OfficialTaxPolicySnapshot(
    string PolicyVersion,
    string VatRegistrationModeCode,
    string EngineTaxpayerMode,
    string PrimarySalesDocumentKind,
    string InvoicePolicyVersion,
    string EngineTaxModeCode,
    int VatInclusiveRatePercent,
    IReadOnlyList<string> Notes);
