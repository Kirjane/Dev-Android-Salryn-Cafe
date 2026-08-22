using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Core.Services;

public sealed record OfficialOutputSourceReadRequest(
    Guid? JournalEntryId,
    string? OfficialDocumentNumber,
    ElectronicJournalDocumentKind? DocumentKind,
    Guid? BirMachineProfileId,
    string? RegistrationScopeCode);

public sealed record OfficialOutputDocumentHeader(
    Guid JournalEntryId,
    Guid SaleId,
    Guid BirMachineProfileId,
    Guid OfficialDocumentSequenceId,
    ElectronicJournalDocumentKind DocumentKind,
    string OfficialDocumentNumber,
    DateTimeOffset TransactionTimestamp,
    Guid CashierUserId,
    string CashierIdentifier,
    string? TerminalId,
    string? SnapshotTerminalIdentifier);

public sealed record OfficialOutputLineItem(
    string Description,
    string? Barcode,
    string UnitName,
    long QuantityX1000,
    long UnitPriceCents,
    long DiscountCents,
    long LineTotalCents);

public sealed record OfficialOutputPayment(
    PaymentMethod Method,
    long AmountCents,
    DateTimeOffset PaidAt);

public sealed record OfficialOutputTotals(
    long SubtotalCents,
    long DiscountCents,
    long TaxCents,
    long TotalCents,
    long AmountPaidCents,
    long ChangeCents,
    string TaxModeCode,
    bool VatBucketEngineDeferred,
    string? TaxModeNotes,
    IReadOnlyList<string> DeferredComplianceNotes);

public sealed record OfficialOutputMachineProfile(
    Guid BirMachineProfileId,
    string? RegistrationScopeCode,
    string RegisteredTaxpayerName,
    string? BusinessName,
    string RegisteredAddress,
    string? BranchAddress,
    string TaxpayerIdentificationNumber,
    string BranchCode,
    string? MachineIdentificationNumber,
    string? MachineSerialNumber,
    string? PosTerminalNumber,
    string? AccreditationNumber,
    string? PermitToUseNumber,
    string? SoftwareName,
    string? SoftwareVersion,
    string? SoftwareBuild,
    string? ComplianceMode);

public sealed record OfficialOutputEvidence(
    Guid OriginalEventId,
    string SnapshotPayloadJson,
    string SnapshotPayloadHash,
    string RecomputedSnapshotPayloadHash,
    string SnapshotHashAlgorithm,
    bool SnapshotHashMatches);

public sealed record OfficialOutputLifecycle(
    OfficialDocumentLifecycleState State,
    int CorrectionCount,
    IReadOnlyList<string> CorrectionNotes,
    bool IsCoveredByFinalizedZReading,
    Guid? CoveringZReadingId,
    IReadOnlyList<OfficialDocumentLifecycleIssue> Issues,
    IReadOnlyList<OfficialDocumentLifecycleEventReadModel> Events);

public sealed record OfficialOutputSourceEvidence(
    Guid JournalEntryId,
    Guid SaleId,
    Guid BirMachineProfileId,
    Guid OfficialDocumentSequenceId,
    ElectronicJournalDocumentKind DocumentKind,
    string OfficialDocumentNumber,
    DateTimeOffset TransactionTimestamp,
    Guid CashierUserId,
    string? TerminalId,
    string SnapshotPayloadJson,
    string SnapshotPayloadHash,
    string SnapshotHashAlgorithm,
    Guid OriginalEventId,
    OfficialOutputMachineProfile MachineProfile);

public sealed record OfficialOutputSourceResult(
    OfficialOutputDocumentHeader Header,
    IReadOnlyList<OfficialOutputLineItem> LineItems,
    IReadOnlyList<OfficialOutputPayment> Payments,
    OfficialOutputTotals Totals,
    OfficialOutputMachineProfile MachineProfile,
    OfficialOutputEvidence Evidence,
    OfficialOutputLifecycle Lifecycle);

public interface IOfficialOutputSourceReadService
{
    Task<Result<OfficialOutputSourceResult>> ReadAsync(
        OfficialOutputSourceReadRequest request,
        CancellationToken cancellationToken = default);
}

// Kirch: why - official renderers must later consume this controlled evidence source, not UI preview strings or report-template logic.
public static class OfficialOutputSourceReadService
{
    public static Result ValidateRequest(OfficialOutputSourceReadRequest? request)
    {
        if (request is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputSource.RequestRequired",
                "Official output source read request is required."));
        }

        bool hasJournalEntryId = request.JournalEntryId.HasValue;
        bool hasOfficialDocumentNumber = !string.IsNullOrWhiteSpace(request.OfficialDocumentNumber);

        if (!hasJournalEntryId && !hasOfficialDocumentNumber)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputSource.DocumentReferenceRequired",
                "Official output source read requires a journal entry id or official document number."));
        }

        if (hasJournalEntryId && request.JournalEntryId!.Value == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputSource.InvalidJournalEntryId",
                "Official output source journal entry id is invalid."));
        }

        if (hasOfficialDocumentNumber)
        {
            if (!request.DocumentKind.HasValue)
            {
                return Result.Fail(Error.Validation(
                    "OfficialOutputSource.DocumentKindRequired",
                    "Official output source read requires document kind when using an official document number."));
            }

            if (!request.BirMachineProfileId.HasValue &&
                string.IsNullOrWhiteSpace(request.RegistrationScopeCode))
            {
                return Result.Fail(Error.Validation(
                    "OfficialOutputSource.MachineProfileOrScopeRequired",
                    "Official output source read requires a machine profile id or registration scope when using an official document number."));
            }
        }

        if (request.DocumentKind.HasValue &&
            !Enum.IsDefined(typeof(ElectronicJournalDocumentKind), request.DocumentKind.Value))
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputSource.UnsupportedDocumentKind",
                "Official output source document kind is unsupported."));
        }

        if (request.BirMachineProfileId.HasValue &&
            request.BirMachineProfileId.Value == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "OfficialOutputSource.InvalidMachineProfileId",
                "Official output source machine profile id is invalid."));
        }

        return Result.Success();
    }

    public static OfficialOutputSourceReadRequest NormalizeRequest(
        OfficialOutputSourceReadRequest request)
    {
        return request with
        {
            OfficialDocumentNumber = NormalizeOptional(request.OfficialDocumentNumber),
            RegistrationScopeCode = NormalizeOptional(request.RegistrationScopeCode)
        };
    }

    public static Result<OfficialOutputSourceResult> BuildResult(
        OfficialOutputSourceEvidence? source,
        OfficialDocumentLifecycleResult? lifecycle)
    {
        if (source is null)
        {
            return Result<OfficialOutputSourceResult>.Fail(Error.Validation(
                "OfficialOutputSource.SourceRequired",
                "Official output source evidence is required."));
        }

        if (lifecycle is null)
        {
            return Result<OfficialOutputSourceResult>.Fail(Error.Validation(
                "OfficialOutputSource.LifecycleRequired",
                "Official output source requires lifecycle state."));
        }

        if (string.IsNullOrWhiteSpace(source.SnapshotPayloadJson) ||
            string.IsNullOrWhiteSpace(source.SnapshotPayloadHash) ||
            string.IsNullOrWhiteSpace(source.SnapshotHashAlgorithm))
        {
            return Result<OfficialOutputSourceResult>.Fail(Error.Failure(
                "OfficialOutputSource.SnapshotEvidenceMissing",
                "Official journal entry is missing snapshot evidence."));
        }

        string recomputedHash = ComputeSha256Hex(source.SnapshotPayloadJson);

        if (!string.Equals(source.SnapshotHashAlgorithm, "SHA-256", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(source.SnapshotPayloadHash, recomputedHash, StringComparison.OrdinalIgnoreCase))
        {
            return Result<OfficialOutputSourceResult>.Fail(Error.Failure(
                "OfficialOutputSource.SnapshotHashMismatch",
                "Official journal snapshot payload hash does not match stored evidence."));
        }

        Result<ParsedSnapshot> parsedSnapshotResult = ParseSnapshotPayload(source.SnapshotPayloadJson);

        if (parsedSnapshotResult.IsFailure)
        {
            return Result<OfficialOutputSourceResult>.Fail(parsedSnapshotResult.ErrorDetail);
        }

        ParsedSnapshot snapshot = parsedSnapshotResult.Value;

        Result identityValidation = ValidateSnapshotIdentity(source, snapshot);

        if (identityValidation.IsFailure)
        {
            return Result<OfficialOutputSourceResult>.Fail(identityValidation.ErrorDetail);
        }

        OfficialOutputDocumentHeader header = new(
            JournalEntryId: source.JournalEntryId,
            SaleId: source.SaleId,
            BirMachineProfileId: source.BirMachineProfileId,
            OfficialDocumentSequenceId: source.OfficialDocumentSequenceId,
            DocumentKind: source.DocumentKind,
            OfficialDocumentNumber: source.OfficialDocumentNumber,
            TransactionTimestamp: source.TransactionTimestamp,
            CashierUserId: source.CashierUserId,
            CashierIdentifier: snapshot.CashierIdentifier,
            TerminalId: source.TerminalId,
            SnapshotTerminalIdentifier: snapshot.SnapshotTerminalIdentifier);

        OfficialOutputEvidence evidence = new(
            OriginalEventId: source.OriginalEventId,
            SnapshotPayloadJson: source.SnapshotPayloadJson,
            SnapshotPayloadHash: source.SnapshotPayloadHash,
            RecomputedSnapshotPayloadHash: recomputedHash,
            SnapshotHashAlgorithm: source.SnapshotHashAlgorithm,
            SnapshotHashMatches: true);

        OfficialOutputLifecycle outputLifecycle = new(
            State: lifecycle.Summary.State,
            CorrectionCount: lifecycle.Summary.CorrectionCount,
            CorrectionNotes: lifecycle.Corrections
                .Select(static correction => NormalizeOptional(correction.Reason))
                .Where(static reason => reason is not null)
                .Select(static reason => reason!)
                .ToArray(),
            IsCoveredByFinalizedZReading: lifecycle.Summary.IsCoveredByFinalizedZReading,
            CoveringZReadingId: lifecycle.Summary.CoveringZReadingId,
            Issues: lifecycle.Issues,
            Events: lifecycle.Events);

        return Result<OfficialOutputSourceResult>.Success(new OfficialOutputSourceResult(
            header,
            snapshot.LineItems,
            snapshot.Payments,
            snapshot.Totals,
            source.MachineProfile,
            evidence,
            outputLifecycle));
    }

    private static Result ValidateSnapshotIdentity(
        OfficialOutputSourceEvidence source,
        ParsedSnapshot snapshot)
    {
        if (source.DocumentKind != snapshot.DocumentKind)
        {
            return Result.Fail(Error.Failure(
                "OfficialOutputSource.SnapshotDocumentKindMismatch",
                "Official journal snapshot document kind does not match the journal entry."));
        }

        if (!string.Equals(source.OfficialDocumentNumber, snapshot.DocumentNumber, StringComparison.Ordinal))
        {
            return Result.Fail(Error.Failure(
                "OfficialOutputSource.SnapshotDocumentNumberMismatch",
                "Official journal snapshot document number does not match the journal entry."));
        }

        return Result.Success();
    }

    private static Result<ParsedSnapshot> ParseSnapshotPayload(string payloadJson)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payloadJson);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                !TryReadString(root, "schema", out string? schema) ||
                !string.Equals(schema, "SALRYN_OFFICIAL_DOCUMENT_SNAPSHOT_V1", StringComparison.Ordinal))
            {
                return Result<ParsedSnapshot>.Fail(Error.Failure(
                    "OfficialOutputSource.UnsupportedSnapshotSchema",
                    "Official output source snapshot schema is unsupported."));
            }

            ElectronicJournalDocumentKind documentKind = ReadDocumentKind(root);
            JsonElement header = ReadRequiredObject(root, "header");
            IReadOnlyList<OfficialOutputLineItem> lineItems = ReadLineItems(root);
            OfficialOutputTotals totals = ReadTotals(root);
            IReadOnlyList<OfficialOutputPayment> payments = ReadPayments(root);
            JsonElement taxMode = ReadRequiredObject(root, "taxMode");

            string taxModeCode = ReadRequiredString(taxMode, "taxModeCode");
            bool vatBucketEngineDeferred = ReadRequiredBoolean(taxMode, "vatBucketEngineDeferred");
            string? taxModeNotes = ReadOptionalString(taxMode, "notes");
            IReadOnlyList<string> deferredNotes = ReadDeferredNotes(root);

            totals = totals with
            {
                TaxModeCode = taxModeCode,
                VatBucketEngineDeferred = vatBucketEngineDeferred,
                TaxModeNotes = taxModeNotes,
                DeferredComplianceNotes = deferredNotes
            };

            return Result<ParsedSnapshot>.Success(new ParsedSnapshot(
                DocumentKind: documentKind,
                DocumentNumber: ReadRequiredString(header, "documentNumber"),
                CashierIdentifier: ReadRequiredString(header, "cashierIdentifier"),
                SnapshotTerminalIdentifier: ReadOptionalString(header, "terminalIdentifier"),
                LineItems: lineItems,
                Payments: payments,
                Totals: totals));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return Result<ParsedSnapshot>.Fail(Error.Failure(
                "OfficialOutputSource.InvalidSnapshotPayload",
                ex.Message));
        }
    }

    private static ElectronicJournalDocumentKind ReadDocumentKind(JsonElement root)
    {
        string value = ReadRequiredString(root, "documentKind");

        return value switch
        {
            "OfficialReceipt" => ElectronicJournalDocumentKind.OfficialReceipt,
            "OfficialInvoice" => ElectronicJournalDocumentKind.OfficialInvoice,
            _ => throw new InvalidOperationException("Official output source snapshot document kind is unsupported.")
        };
    }

    private static IReadOnlyList<OfficialOutputLineItem> ReadLineItems(JsonElement root)
    {
        JsonElement lines = ReadRequiredArray(root, "lines");
        List<OfficialOutputLineItem> lineItems = new();

        foreach (JsonElement line in lines.EnumerateArray())
        {
            lineItems.Add(new OfficialOutputLineItem(
                Description: ReadRequiredString(line, "description"),
                Barcode: ReadOptionalString(line, "barcode"),
                UnitName: ReadRequiredString(line, "unitName"),
                QuantityX1000: ReadRequiredInt64(line, "quantityX1000"),
                UnitPriceCents: ReadRequiredInt64(line, "unitPriceCents"),
                DiscountCents: ReadRequiredInt64(line, "discountCents"),
                LineTotalCents: ReadRequiredInt64(line, "lineTotalCents")));
        }

        return lineItems;
    }

    private static OfficialOutputTotals ReadTotals(JsonElement root)
    {
        JsonElement amounts = ReadRequiredObject(root, "amounts");

        return new OfficialOutputTotals(
            SubtotalCents: ReadRequiredInt64(amounts, "subtotalCents"),
            DiscountCents: ReadRequiredInt64(amounts, "discountCents"),
            TaxCents: ReadRequiredInt64(amounts, "taxCents"),
            TotalCents: ReadRequiredInt64(amounts, "totalCents"),
            AmountPaidCents: ReadRequiredInt64(amounts, "amountPaidCents"),
            ChangeCents: ReadRequiredInt64(amounts, "changeCents"),
            TaxModeCode: string.Empty,
            VatBucketEngineDeferred: false,
            TaxModeNotes: null,
            DeferredComplianceNotes: Array.Empty<string>());
    }

    private static IReadOnlyList<OfficialOutputPayment> ReadPayments(JsonElement root)
    {
        JsonElement payments = ReadRequiredArray(root, "payments");
        List<OfficialOutputPayment> outputPayments = new();

        foreach (JsonElement payment in payments.EnumerateArray())
        {
            string methodText = ReadRequiredString(payment, "method");

            if (!Enum.TryParse(methodText, ignoreCase: false, out PaymentMethod method) ||
                !Enum.IsDefined(typeof(PaymentMethod), method))
            {
                throw new InvalidOperationException("Official output source snapshot payment method is unsupported.");
            }

            outputPayments.Add(new OfficialOutputPayment(
                Method: method,
                AmountCents: ReadRequiredInt64(payment, "amountCents"),
                PaidAt: DateTimeOffset.Parse(
                    ReadRequiredString(payment, "paidAt"),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind)));
        }

        return outputPayments;
    }

    private static IReadOnlyList<string> ReadDeferredNotes(JsonElement root)
    {
        JsonElement notes = ReadRequiredArray(root, "deferredComplianceNotes");
        List<string> output = new();

        foreach (JsonElement note in notes.EnumerateArray())
        {
            if (note.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException("Official output source deferred note must be text.");
            }

            string? value = NormalizeOptional(note.GetString());

            if (value is not null)
            {
                output.Add(value);
            }
        }

        return output;
    }

    private static JsonElement ReadRequiredObject(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value) ||
            value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"Official output source snapshot object '{propertyName}' is required.");
        }

        return value;
    }

    private static JsonElement ReadRequiredArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Official output source snapshot array '{propertyName}' is required.");
        }

        return value;
    }

    private static string ReadRequiredString(JsonElement element, string propertyName)
    {
        if (!TryReadString(element, propertyName, out string? value) ||
            string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Official output source snapshot text '{propertyName}' is required.");
        }

        return value.Trim();
    }

    private static string? ReadOptionalString(JsonElement element, string propertyName)
    {
        return TryReadString(element, propertyName, out string? value)
            ? NormalizeOptional(value)
            : null;
    }

    private static long ReadRequiredInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt64(out long number))
        {
            throw new InvalidOperationException($"Official output source snapshot number '{propertyName}' is required.");
        }

        return number;
    }

    private static bool ReadRequiredBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value) ||
            value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidOperationException($"Official output source snapshot boolean '{propertyName}' is required.");
        }

        return value.GetBoolean();
    }

    private static bool TryReadString(
        JsonElement element,
        string propertyName,
        out string? value)
    {
        value = null;

        if (!element.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();

        return true;
    }

    private static string ComputeSha256Hex(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        byte[] hash = SHA256.HashData(bytes);

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private sealed record ParsedSnapshot(
        ElectronicJournalDocumentKind DocumentKind,
        string DocumentNumber,
        string CashierIdentifier,
        string? SnapshotTerminalIdentifier,
        IReadOnlyList<OfficialOutputLineItem> LineItems,
        IReadOnlyList<OfficialOutputPayment> Payments,
        OfficialOutputTotals Totals);
}
