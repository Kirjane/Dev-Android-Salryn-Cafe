using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Core.Services;

// Kirch: why - journal evidence needs stable payload/hash data before any official renderer or printer is allowed.
public sealed class OfficialDocumentSnapshotEvidenceBuilder
{
    public Result<ElectronicJournalSnapshotEvidence> Build(OfficialDocumentSnapshotDraft draft)
    {
        Result validation = ValidateDraft(draft);

        if (validation.IsFailure)
        {
            return Result<ElectronicJournalSnapshotEvidence>.Fail(validation.ErrorDetail);
        }

        string payloadJson = BuildCanonicalPayloadJson(draft);
        string payloadHash = ComputeSha256Hex(payloadJson);

        return Result<ElectronicJournalSnapshotEvidence>.Success(
            new ElectronicJournalSnapshotEvidence(
                PayloadJson: payloadJson,
                PayloadHash: payloadHash,
                HashAlgorithm: "SHA-256"));
    }

    private static Result ValidateDraft(OfficialDocumentSnapshotDraft? draft)
    {
        if (draft is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentSnapshotEvidence.DraftRequired",
                "Official document snapshot draft is required."));
        }

        if (!Enum.IsDefined(typeof(OfficialDocumentSnapshotDraftKind), draft.DocumentKind))
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentSnapshotEvidence.InvalidDocumentKind",
                "Official document snapshot draft kind is invalid."));
        }

        if (draft.Header is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentSnapshotEvidence.HeaderRequired",
                "Official document snapshot header is required."));
        }

        if (string.IsNullOrWhiteSpace(draft.Header.DocumentNumber))
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentSnapshotEvidence.DocumentNumberRequired",
                "Snapshot evidence requires an allocated official document number."));
        }

        if (string.IsNullOrWhiteSpace(draft.Header.RegisteredTaxpayerName) ||
            string.IsNullOrWhiteSpace(draft.Header.TaxpayerIdentificationNumber) ||
            string.IsNullOrWhiteSpace(draft.Header.BranchCode) ||
            string.IsNullOrWhiteSpace(draft.Header.RegisteredBusinessAddress) ||
            string.IsNullOrWhiteSpace(draft.Header.CashierIdentifier))
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentSnapshotEvidence.HeaderIdentityRequired",
                "Snapshot evidence requires taxpayer, branch, address, and cashier identity."));
        }

        if (draft.Lines is null || draft.Lines.Count == 0)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentSnapshotEvidence.LinesRequired",
                "Snapshot evidence requires at least one line."));
        }

        if (draft.Payments is null || draft.Payments.Count == 0)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentSnapshotEvidence.PaymentsRequired",
                "Snapshot evidence requires at least one payment."));
        }

        foreach (OfficialDocumentSnapshotDraftLine line in draft.Lines)
        {
            if (line is null)
            {
                return Result.Fail(Error.Validation(
                    "OfficialDocumentSnapshotEvidence.LineRequired",
                    "Snapshot evidence line cannot be null."));
            }

            if (string.IsNullOrWhiteSpace(line.Description) ||
                string.IsNullOrWhiteSpace(line.UnitName) ||
                line.QuantityX1000 <= 0 ||
                line.UnitPriceCents < 0 ||
                line.DiscountCents < 0 ||
                line.LineTotalCents < 0)
            {
                return Result.Fail(Error.Validation(
                    "OfficialDocumentSnapshotEvidence.LineRequired",
                    "Snapshot evidence line description, unit, quantity, and amounts are required."));
            }
        }

        if (draft.Amounts is null ||
            draft.Amounts.SubtotalCents < 0 ||
            draft.Amounts.DiscountCents < 0 ||
            draft.Amounts.TaxCents < 0 ||
            draft.Amounts.TotalCents < 0 ||
            draft.Amounts.AmountPaidCents < 0 ||
            draft.Amounts.ChangeCents < 0)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentSnapshotEvidence.AmountsRequired",
                "Snapshot evidence requires non-negative amount fields."));
        }

        foreach (OfficialDocumentSnapshotDraftPayment payment in draft.Payments)
        {
            if (payment is null)
            {
                return Result.Fail(Error.Validation(
                    "OfficialDocumentSnapshotEvidence.PaymentRequired",
                    "Snapshot evidence payment cannot be null."));
            }

            if (!Enum.IsDefined(typeof(PaymentMethod), payment.Method))
            {
                return Result.Fail(Error.Validation(
                    "OfficialDocumentSnapshotEvidence.PaymentMethodInvalid",
                    "Snapshot evidence payment method is invalid."));
            }

            if (payment.AmountCents <= 0)
            {
                return Result.Fail(Error.Validation(
                    "OfficialDocumentSnapshotEvidence.PaymentAmountRequired",
                    "Snapshot evidence payment amount must be greater than zero."));
            }
        }

        if (draft.TaxMode is null ||
            string.IsNullOrWhiteSpace(draft.TaxMode.TaxModeCode))
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentSnapshotEvidence.TaxModeRequired",
                "Snapshot evidence requires tax mode."));
        }

        if (draft.DeferredComplianceNotes is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentSnapshotEvidence.DeferredNotesRequired",
                "Snapshot evidence requires explicit deferred compliance notes."));
        }

        return Result.Success();
    }

    private static string BuildCanonicalPayloadJson(OfficialDocumentSnapshotDraft draft)
    {
        using MemoryStream stream = new();
        using Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = false });

        writer.WriteStartObject();
        writer.WriteString("schema", "SALRYN_OFFICIAL_DOCUMENT_SNAPSHOT_V1");
        writer.WriteString("documentKind", draft.DocumentKind.ToString());

        WriteHeader(writer, draft.Header);
        WriteLines(writer, draft.Lines);
        WriteAmounts(writer, draft.Amounts);
        WritePayments(writer, draft.Payments);
        WriteTaxMode(writer, draft.TaxMode);
        WriteDeferredComplianceNotes(writer, draft.DeferredComplianceNotes);

        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteHeader(
        Utf8JsonWriter writer,
        OfficialDocumentSnapshotDraftHeader header)
    {
        writer.WritePropertyName("header");
        writer.WriteStartObject();
        writer.WriteString("registeredTaxpayerName", header.RegisteredTaxpayerName);
        writer.WriteString("businessName", header.BusinessName);
        writer.WriteString("taxpayerIdentificationNumber", header.TaxpayerIdentificationNumber);
        writer.WriteString("branchCode", header.BranchCode);
        writer.WriteString("registeredBusinessAddress", header.RegisteredBusinessAddress);
        writer.WriteString("transactionDateTime", header.TransactionDateTime.ToString("O"));
        writer.WriteString("documentNumber", header.DocumentNumber);
        writer.WriteString("machineSerialNumber", header.MachineSerialNumber);
        writer.WriteString("machineIdentificationNumber", header.MachineIdentificationNumber);
        writer.WriteString("accreditationNumber", header.AccreditationNumber);
        writer.WriteString("permitToUseNumber", header.PermitToUseNumber);
        writer.WriteString("cashierIdentifier", header.CashierIdentifier);
        writer.WriteString("terminalIdentifier", header.TerminalIdentifier);
        writer.WriteEndObject();
    }

    private static void WriteLines(
        Utf8JsonWriter writer,
        IReadOnlyList<OfficialDocumentSnapshotDraftLine> lines)
    {
        writer.WritePropertyName("lines");
        writer.WriteStartArray();

        foreach (OfficialDocumentSnapshotDraftLine line in lines)
        {
            writer.WriteStartObject();
            writer.WriteString("description", line.Description);
            writer.WriteString("barcode", line.Barcode);
            writer.WriteString("unitName", line.UnitName);
            writer.WriteNumber("quantityX1000", line.QuantityX1000);
            writer.WriteNumber("unitPriceCents", line.UnitPriceCents);
            writer.WriteNumber("discountCents", line.DiscountCents);
            writer.WriteNumber("lineTotalCents", line.LineTotalCents);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteAmounts(
        Utf8JsonWriter writer,
        OfficialDocumentSnapshotDraftAmounts amounts)
    {
        writer.WritePropertyName("amounts");
        writer.WriteStartObject();
        writer.WriteNumber("subtotalCents", amounts.SubtotalCents);
        writer.WriteNumber("discountCents", amounts.DiscountCents);
        writer.WriteNumber("taxCents", amounts.TaxCents);
        writer.WriteNumber("totalCents", amounts.TotalCents);
        writer.WriteNumber("amountPaidCents", amounts.AmountPaidCents);
        writer.WriteNumber("changeCents", amounts.ChangeCents);
        writer.WriteEndObject();
    }

    private static void WritePayments(
        Utf8JsonWriter writer,
        IReadOnlyList<OfficialDocumentSnapshotDraftPayment> payments)
    {
        writer.WritePropertyName("payments");
        writer.WriteStartArray();

        foreach (OfficialDocumentSnapshotDraftPayment payment in payments)
        {
            writer.WriteStartObject();
            writer.WriteString("method", payment.Method.ToString());
            writer.WriteNumber("amountCents", payment.AmountCents);
            writer.WriteString("paidAt", payment.PaidAt.ToString("O"));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteTaxMode(
        Utf8JsonWriter writer,
        OfficialDocumentSnapshotDraftTaxMode taxMode)
    {
        writer.WritePropertyName("taxMode");
        writer.WriteStartObject();
        writer.WriteString("taxModeCode", taxMode.TaxModeCode);
        writer.WriteBoolean("vatBucketEngineDeferred", taxMode.VatBucketEngineDeferred);
        writer.WriteString("notes", taxMode.Notes);
        writer.WriteEndObject();
    }

    private static void WriteDeferredComplianceNotes(
        Utf8JsonWriter writer,
        IReadOnlyList<string> notes)
    {
        writer.WritePropertyName("deferredComplianceNotes");
        writer.WriteStartArray();

        foreach (string note in notes)
        {
            writer.WriteStringValue(note);
        }

        writer.WriteEndArray();
    }

    private static string ComputeSha256Hex(string payloadJson)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(payloadJson);
        byte[] hash = SHA256.HashData(bytes);

        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
