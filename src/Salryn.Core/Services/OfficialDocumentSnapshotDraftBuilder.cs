using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Core.Services;

public sealed record OfficialDocumentSnapshotDraftMachineProfileInput(
    string RegisteredTaxpayerName,
    string? BusinessName,
    string TaxpayerIdentificationNumber,
    string BranchCode,
    string RegisteredBusinessAddress,
    string? MachineSerialNumber,
    string? MachineIdentificationNumber,
    string? AccreditationNumber,
    string? PermitToUseNumber);

public sealed record OfficialDocumentSnapshotDraftBuildRequest(
    OfficialDocumentSnapshotDraftKind DocumentKind,
    OfficialDocumentSnapshotDraftMachineProfileInput MachineProfile,
    Sale Sale,
    IReadOnlyList<SaleItem> Items,
    IReadOnlyList<Payment> Payments,
    string? AllocatedDocumentNumber,
    string CashierIdentifier,
    string? TerminalIdentifier,
    string TaxModeCode,
    string? TaxModeNotes,
    IReadOnlyList<OfficialDocumentLineTaxClassification>? LineTaxClassifications = null,
    OfficialDocumentTaxBucketDraftTaxpayerMode? TaxpayerMode = null,
    // TRAD-M3-RC2: optional buyer/statutory evidence supplied at atomic post time (frozen into EJ snapshot).
    OfficialDocumentSnapshotDraftBuyer? Buyer = null,
    OfficialDocumentSnapshotDraftStatutory? Statutory = null);

// Kirch strict dev guide:
// J21-4B owns in-memory draft building only.
// This builder maps already-provided machine profile, sale, item, payment, and optional document-number data
// into OfficialDocumentSnapshotDraft.
// It must not render, print, persist, allocate sequence numbers, wire checkout,
// write sale snapshots, open databases, create journals, create X/Z readings, or claim official output readiness.
public sealed class OfficialDocumentSnapshotDraftBuilder
{
    private readonly OfficialDocumentTaxBucketEngine _taxBucketEngine = new();

    public Result<OfficialDocumentSnapshotDraft> Build(OfficialDocumentSnapshotDraftBuildRequest request)
    {
        Result validation = ValidateRequest(request);

        if (validation.IsFailure)
        {
            return Result<OfficialDocumentSnapshotDraft>.Fail(validation.ErrorDetail);
        }

        OfficialDocumentSnapshotDraftMachineProfileInput profile = request.MachineProfile;
        Sale sale = request.Sale;

        // Kirch strict dev guide:
        // The document number is accepted as input only.
        // This builder does not allocate, reserve, increment, or validate official sequence ownership.
        OfficialDocumentSnapshotDraftHeader header = new(
            RegisteredTaxpayerName: profile.RegisteredTaxpayerName.Trim(),
            BusinessName: NormalizeOptional(profile.BusinessName),
            TaxpayerIdentificationNumber: profile.TaxpayerIdentificationNumber.Trim(),
            BranchCode: profile.BranchCode.Trim(),
            RegisteredBusinessAddress: profile.RegisteredBusinessAddress.Trim(),
            TransactionDateTime: sale.SaleDate,
            DocumentNumber: NormalizeOptional(request.AllocatedDocumentNumber),
            MachineSerialNumber: NormalizeOptional(profile.MachineSerialNumber),
            MachineIdentificationNumber: NormalizeOptional(profile.MachineIdentificationNumber),
            AccreditationNumber: NormalizeOptional(profile.AccreditationNumber),
            PermitToUseNumber: NormalizeOptional(profile.PermitToUseNumber),
            CashierIdentifier: request.CashierIdentifier.Trim(),
            TerminalIdentifier: NormalizeOptional(request.TerminalIdentifier));

        // Kirch strict dev guide:
        // Lines mirror persisted sale item evidence fields only.
        // No display formatting or official receipt wording belongs here.
        OfficialDocumentSnapshotDraftAmounts amounts = new(
            SubtotalCents: sale.Subtotal.Cents,
            DiscountCents: sale.Discount.Cents,
            TaxCents: sale.Tax.Cents,
            TotalCents: sale.Total.Cents,
            AmountPaidCents: sale.AmountPaid.Cents,
            ChangeCents: sale.Change.Cents);

        // Kirch strict dev guide:
        // Payment rows mirror payment evidence fields only.
        // This builder does not process payment or trigger output.
        List<OfficialDocumentSnapshotDraftPayment> payments = new();

        foreach (Payment payment in request.Payments)
        {
            payments.Add(new OfficialDocumentSnapshotDraftPayment(
                Method: payment.Method,
                AmountCents: payment.Amount.Cents,
                PaidAt: payment.PaidAt));
        }

        OfficialDocumentTaxBucketDraft? taxBuckets = null;
        bool vatBucketEngineDeferred = true;
        string taxModeCode = request.TaxModeCode.Trim().ToUpperInvariant();
        string? taxModeNotes = NormalizeOptional(request.TaxModeNotes);
        bool emitLineTaxEvidence = false;

        if (request.LineTaxClassifications is not null &&
            request.TaxpayerMode.HasValue)
        {
            Result taxClassValidation = ValidateLineTaxClassifications(request.Items, request.LineTaxClassifications);

            if (taxClassValidation.IsFailure)
            {
                return Result<OfficialDocumentSnapshotDraft>.Fail(taxClassValidation.ErrorDetail);
            }

            List<OfficialDocumentTaxBucketCalculationLine> calculationLines = new();

            for (int index = 0; index < request.Items.Count; index++)
            {
                SaleItem item = request.Items[index];

                calculationLines.Add(new OfficialDocumentTaxBucketCalculationLine(
                    item.LineTotal.Cents,
                    item.Discount.Cents,
                    request.LineTaxClassifications[index]));
            }

            Result<OfficialDocumentTaxBucketDraft> bucketResult = _taxBucketEngine.Calculate(
                new OfficialDocumentTaxBucketCalculationRequest(
                    request.TaxpayerMode.Value,
                    calculationLines,
                    sale.Discount.Cents));

            if (bucketResult.IsFailure)
            {
                return Result<OfficialDocumentSnapshotDraft>.Fail(bucketResult.ErrorDetail);
            }

            taxBuckets = bucketResult.Value;
            vatBucketEngineDeferred = false;
            taxModeCode = SalrynOfficialTaxBucketPolicy.EngineTaxModeCode;
            taxModeNotes = SalrynOfficialTaxBucketPolicy.EngineBoundaryNote;
            emitLineTaxEvidence = true;
        }

        // Lines mirror sale item evidence. When tax engine ran, freeze V3 line-tax components.
        List<OfficialDocumentSnapshotDraftLine> lines = new();
        for (int index = 0; index < request.Items.Count; index++)
        {
            SaleItem item = request.Items[index];
            if (emitLineTaxEvidence && request.LineTaxClassifications is not null)
            {
                OfficialDocumentLineTaxClassification classification = request.LineTaxClassifications[index];
                (long vatable, long vat, long exempt, long zero, long sspt, long nv) =
                    FreezeLineTaxComponents(item.LineTotal.Cents, classification);
                lines.Add(new OfficialDocumentSnapshotDraftLine(
                    Description: item.ProductNameSnapshot.Trim(),
                    Barcode: NormalizeOptional(item.BarcodeSnapshot),
                    UnitName: item.UnitNameSnapshot.Trim(),
                    QuantityX1000: item.Quantity.ValueX1000,
                    UnitPriceCents: item.UnitPrice.Cents,
                    DiscountCents: item.Discount.Cents,
                    LineTotalCents: item.LineTotal.Cents,
                    LineIndex: index,
                    SaleItemId: item.Id,
                    ProductId: item.ProductId,
                    TaxClassification: classification,
                    VatableBaseCents: vatable,
                    VatAmountCents: vat,
                    VatExemptCents: exempt,
                    ZeroRatedCents: zero,
                    SsptCents: sspt,
                    NonVatExemptCents: nv,
                    StockTracked: true));
            }
            else
            {
                lines.Add(new OfficialDocumentSnapshotDraftLine(
                    Description: item.ProductNameSnapshot.Trim(),
                    Barcode: NormalizeOptional(item.BarcodeSnapshot),
                    UnitName: item.UnitNameSnapshot.Trim(),
                    QuantityX1000: item.Quantity.ValueX1000,
                    UnitPriceCents: item.UnitPrice.Cents,
                    DiscountCents: item.Discount.Cents,
                    LineTotalCents: item.LineTotal.Cents));
            }
        }

        OfficialDocumentSnapshotDraftTaxMode taxMode = new(
            TaxModeCode: taxModeCode,
            VatBucketEngineDeferred: vatBucketEngineDeferred,
            Notes: taxModeNotes);

        string[] deferredNotes = vatBucketEngineDeferred
            ?
            [
                "J21-4B draft builder only.",
                "No official document rendering.",
                "No saved sale snapshot write.",
                "No checkout wiring.",
                "No VAT bucket engine."
            ]
            :
            [
                SalrynOfficialTaxBucketPolicy.EngineBoundaryNote,
                SalrynOfficialTaxBucketPolicy.NoLegalVatClaimNote,
                "No official document rendering.",
                "No saved sale snapshot write."
            ];

        OfficialDocumentSnapshotDraft draft = new(
            request.DocumentKind,
            header,
            lines,
            amounts,
            payments,
            taxMode,
            deferredNotes,
            taxBuckets,
            Buyer: NormalizeBuyer(request.Buyer),
            Statutory: NormalizeStatutory(request.Statutory));

        return Result<OfficialDocumentSnapshotDraft>.Success(draft);
    }

    /// <summary>
    /// Freeze per-line tax components for Snapshot V3. Matches TaxBucketEngine inclusive VAT split.
    /// </summary>
    private static (long Vatable, long Vat, long Exempt, long Zero, long Sspt, long NonVatExempt)
        FreezeLineTaxComponents(long lineTotalCents, OfficialDocumentLineTaxClassification classification)
    {
        return classification switch
        {
            OfficialDocumentLineTaxClassification.Vatable =>
            (
                lineTotalCents - OfficialDocumentTaxBucketEngine.ComputeInclusiveVatAmountCents(lineTotalCents),
                OfficialDocumentTaxBucketEngine.ComputeInclusiveVatAmountCents(lineTotalCents),
                0, 0, 0, 0),
            OfficialDocumentLineTaxClassification.VatExempt => (0, 0, lineTotalCents, 0, 0, 0),
            OfficialDocumentLineTaxClassification.ZeroRated => (0, 0, 0, lineTotalCents, 0, 0),
            OfficialDocumentLineTaxClassification.NonVat => (0, 0, 0, 0, lineTotalCents, 0),
            OfficialDocumentLineTaxClassification.NonVatExempt => (0, 0, 0, 0, 0, lineTotalCents),
            _ => (0, 0, 0, 0, 0, 0)
        };
    }

    private static OfficialDocumentSnapshotDraftBuyer? NormalizeBuyer(OfficialDocumentSnapshotDraftBuyer? buyer)
    {
        if (buyer is null)
        {
            return null;
        }

        string? name = NormalizeOptional(buyer.RegisteredName);
        string? tin = NormalizeOptional(buyer.Tin);
        string? address = NormalizeOptional(buyer.RegisteredAddress);
        if (name is null && tin is null && address is null)
        {
            return null;
        }

        return new OfficialDocumentSnapshotDraftBuyer(name, tin, address);
    }

    private static OfficialDocumentSnapshotDraftStatutory? NormalizeStatutory(
        OfficialDocumentSnapshotDraftStatutory? statutory)
    {
        if (statutory is null)
        {
            return null;
        }

        string? category = NormalizeOptional(statutory.DiscountCategory);
        string? govId = NormalizeOptional(statutory.GovernmentIdNumber);
        string? signature = NormalizeOptional(statutory.SignatureReference);
        if (category is null && govId is null && signature is null && !statutory.WithholdingTaxCents.HasValue)
        {
            return null;
        }

        return new OfficialDocumentSnapshotDraftStatutory(
            category,
            govId,
            signature,
            statutory.WithholdingTaxCents);
    }

    private static Result ValidateRequest(OfficialDocumentSnapshotDraftBuildRequest request)
    {
        if (request is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentDraft.RequestRequired",
                "Official document snapshot draft build request is required."));
        }

        if (!Enum.IsDefined(typeof(OfficialDocumentSnapshotDraftKind), request.DocumentKind))
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentDraft.InvalidDocumentKind",
                "Official document snapshot draft kind is invalid."));
        }

        if (request.MachineProfile is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentDraft.MachineProfileRequired",
                "Machine profile input is required."));
        }

        Result profileValidation = ValidateMachineProfile(request.MachineProfile);

        if (profileValidation.IsFailure)
        {
            return profileValidation;
        }

        if (request.Sale is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentDraft.SaleRequired",
                "Sale input is required."));
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentDraft.LinesRequired",
                "At least one sale item is required for the draft."));
        }

        if (request.Payments is null || request.Payments.Count == 0)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentDraft.PaymentsRequired",
                "At least one payment is required for the draft."));
        }

        if (string.IsNullOrWhiteSpace(request.CashierIdentifier))
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentDraft.CashierIdentifierRequired",
                "Cashier identifier is required."));
        }

        if (string.IsNullOrWhiteSpace(request.TaxModeCode))
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentDraft.TaxModeRequired",
                "Tax mode code is required."));
        }

        foreach (SaleItem item in request.Items)
        {
            if (item is null)
            {
                return Result.Fail(Error.Validation(
                    "OfficialDocumentDraft.LineRequired",
                    "Sale item cannot be null."));
            }

            if (string.IsNullOrWhiteSpace(item.ProductNameSnapshot))
            {
                return Result.Fail(Error.Validation(
                    "OfficialDocumentDraft.LineDescriptionRequired",
                    "Sale item description is required."));
            }

            if (string.IsNullOrWhiteSpace(item.UnitNameSnapshot))
            {
                return Result.Fail(Error.Validation(
                    "OfficialDocumentDraft.LineUnitRequired",
                    "Sale item unit name is required."));
            }
        }

        foreach (Payment payment in request.Payments)
        {
            if (payment is null)
            {
                return Result.Fail(Error.Validation(
                    "OfficialDocumentDraft.PaymentRequired",
                    "Payment cannot be null."));
            }

            if (!Enum.IsDefined(typeof(PaymentMethod), payment.Method))
            {
                return Result.Fail(Error.Validation(
                    "OfficialDocumentDraft.PaymentMethodInvalid",
                    "Payment method is invalid."));
            }
        }

        return Result.Success();
    }

    private static Result ValidateLineTaxClassifications(
        IReadOnlyList<SaleItem> items,
        IReadOnlyList<OfficialDocumentLineTaxClassification> classifications)
    {
        if (classifications.Count != items.Count)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentDraft.LineTaxClassificationCountMismatch",
                "Line tax classifications must match sale item count."));
        }

        foreach (OfficialDocumentLineTaxClassification classification in classifications)
        {
            if (!Enum.IsDefined(typeof(OfficialDocumentLineTaxClassification), classification))
            {
                return Result.Fail(Error.Validation(
                    "OfficialDocumentDraft.InvalidLineTaxClassification",
                    "Line tax classification is invalid."));
            }
        }

        return Result.Success();
    }

    private static Result ValidateMachineProfile(OfficialDocumentSnapshotDraftMachineProfileInput profile)
    {
        if (string.IsNullOrWhiteSpace(profile.RegisteredTaxpayerName))
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentDraft.TaxpayerNameRequired",
                "Registered taxpayer name is required."));
        }

        if (string.IsNullOrWhiteSpace(profile.TaxpayerIdentificationNumber))
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentDraft.TinRequired",
                "Taxpayer identification number is required."));
        }

        if (string.IsNullOrWhiteSpace(profile.BranchCode))
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentDraft.BranchCodeRequired",
                "Branch code is required."));
        }

        if (string.IsNullOrWhiteSpace(profile.RegisteredBusinessAddress))
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentDraft.RegisteredAddressRequired",
                "Registered business address is required."));
        }

        return Result.Success();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}