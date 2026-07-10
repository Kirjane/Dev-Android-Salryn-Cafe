using Salryn.Core.Common;

namespace Salryn.Core.Models;

/// <summary>
/// Installation-level BIR taxpayer registration mode for Traditional CRM/POS tax spine.
/// Engineering policy spine only — not a compliance claim.
/// </summary>
public enum TaxpayerRegistrationMode
{
    Unknown = 0,
    VatRegistered = 1,
    NonVatPercentageTax = 2,
    NonVatExempt = 3
}

public static class TaxpayerRegistrationModeCodes
{
    public const string VatRegistered = "VAT_REGISTERED";
    public const string NonVatPercentageTax = "NON_VAT_PERCENTAGE_TAX";
    public const string NonVatExempt = "NON_VAT_EXEMPT";

    public static string ToPersistedCode(TaxpayerRegistrationMode mode) =>
        mode switch
        {
            TaxpayerRegistrationMode.VatRegistered => VatRegistered,
            TaxpayerRegistrationMode.NonVatPercentageTax => NonVatPercentageTax,
            TaxpayerRegistrationMode.NonVatExempt => NonVatExempt,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown taxpayer registration mode.")
        };

    public static Result<TaxpayerRegistrationMode> Parse(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Result<TaxpayerRegistrationMode>.Fail(Error.Validation(
                "TaxpayerRegistrationMode.Required",
                "Taxpayer registration mode is required."));
        }

        string normalized = code.Trim().ToUpperInvariant();
        return normalized switch
        {
            VatRegistered => Result<TaxpayerRegistrationMode>.Success(TaxpayerRegistrationMode.VatRegistered),
            NonVatPercentageTax => Result<TaxpayerRegistrationMode>.Success(TaxpayerRegistrationMode.NonVatPercentageTax),
            NonVatExempt => Result<TaxpayerRegistrationMode>.Success(TaxpayerRegistrationMode.NonVatExempt),
            // Legacy engine alias from earlier drafts.
            "NON_VAT_REGISTERED" => Result<TaxpayerRegistrationMode>.Success(TaxpayerRegistrationMode.NonVatPercentageTax),
            _ => Result<TaxpayerRegistrationMode>.Fail(Error.Validation(
                "TaxpayerRegistrationMode.Invalid",
                "Taxpayer registration mode must be VAT_REGISTERED, NON_VAT_PERCENTAGE_TAX, or NON_VAT_EXEMPT."))
        };
    }

    public static Result<TaxpayerRegistrationMode?> ParseOptional(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Result<TaxpayerRegistrationMode?>.Success(null);
        }

        Result<TaxpayerRegistrationMode> parsed = Parse(code);
        if (parsed.IsFailure)
        {
            return Result<TaxpayerRegistrationMode?>.Fail(parsed.ErrorDetail);
        }

        return Result<TaxpayerRegistrationMode?>.Success(parsed.Value);
    }

    public static string ToDisplayText(TaxpayerRegistrationMode mode) =>
        mode switch
        {
            TaxpayerRegistrationMode.VatRegistered => "VAT-registered",
            TaxpayerRegistrationMode.NonVatPercentageTax => "Non-VAT (percentage tax / SSPT)",
            TaxpayerRegistrationMode.NonVatExempt => "Non-VAT (exempt)",
            _ => "Unset"
        };

    public static IReadOnlyList<(string Code, string DisplayText)> GetSetupOptions() =>
        new[]
        {
            (VatRegistered, ToDisplayText(TaxpayerRegistrationMode.VatRegistered)),
            (NonVatPercentageTax, ToDisplayText(TaxpayerRegistrationMode.NonVatPercentageTax)),
            (NonVatExempt, ToDisplayText(TaxpayerRegistrationMode.NonVatExempt))
        };
}
