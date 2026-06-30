namespace Salryn.Core.Services;

// Explicit engineering tax policy constants. Not legal or BIR VAT guidance.
public static class SalrynOfficialTaxBucketPolicy
{
    public const string EngineTaxModeCode = "SALRYN_ENGINE_VAT_INCLUSIVE_12PCT";

    public const int VatInclusiveRatePercent = 12;

    public const string EngineBoundaryNote = "Deterministic engineering tax bucket engine only.";

    public const string NoLegalVatClaimNote = "No legal VAT correctness claim.";

    public const string NoOfficialOutputRenderingNote = "No official output rendering.";

    public const string NonVatRegisteredStatement =
        "Non-VAT registered taxpayer placeholder; engineering classification only.";
}
