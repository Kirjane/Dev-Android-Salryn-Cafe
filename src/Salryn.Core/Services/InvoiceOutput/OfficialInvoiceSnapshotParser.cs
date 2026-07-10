using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Salryn.Core.Common;

namespace Salryn.Core.Services.InvoiceOutput;

/// <summary>
/// Parses and re-hashes SALRYN_OFFICIAL_DOCUMENT_SNAPSHOT_V1 payloads.
/// Never consults mutable setup tables.
/// </summary>
public static class OfficialInvoiceSnapshotParser
{
    public static Result<ParsedOfficialInvoiceSnapshot> ParseAndVerify(
        OfficialInvoiceSnapshotIdentity identity)
    {
        if (identity is null)
        {
            return Result<ParsedOfficialInvoiceSnapshot>.Fail(Error.Validation(
                "InvoiceSnapshot.IdentityRequired",
                "Official invoice snapshot identity is required."));
        }

        if (string.IsNullOrWhiteSpace(identity.SnapshotPayloadJson))
        {
            return Result<ParsedOfficialInvoiceSnapshot>.Fail(Error.Validation(
                "InvoiceSnapshot.PayloadMissing",
                "Committed snapshot payload is missing."));
        }

        if (string.IsNullOrWhiteSpace(identity.SnapshotPayloadHash))
        {
            return Result<ParsedOfficialInvoiceSnapshot>.Fail(Error.Validation(
                "InvoiceSnapshot.HashMissing",
                "Committed snapshot payload hash is missing."));
        }

        string recomputed = ComputeSha256Hex(identity.SnapshotPayloadJson);
        if (!string.Equals(recomputed, identity.SnapshotPayloadHash.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return Result<ParsedOfficialInvoiceSnapshot>.Fail(Error.Validation(
                "InvoiceSnapshot.HashMismatch",
                "Snapshot payload hash does not match stored hash."));
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(identity.SnapshotPayloadJson);
            JsonElement root = document.RootElement;

            if (!root.TryGetProperty("schema", out JsonElement schemaEl) ||
                !string.Equals(schemaEl.GetString(), InvoiceOutputConstants.SnapshotSchema, StringComparison.Ordinal))
            {
                return Result<ParsedOfficialInvoiceSnapshot>.Fail(Error.Validation(
                    "InvoiceSnapshot.UnsupportedSchema",
                    "Snapshot schema is missing or unsupported for invoice output."));
            }

            string documentKind = ReadString(root, "documentKind") ?? string.Empty;
            if (!root.TryGetProperty("header", out JsonElement header))
            {
                return Result<ParsedOfficialInvoiceSnapshot>.Fail(Error.Validation(
                    "InvoiceSnapshot.HeaderRequired",
                    "Snapshot header is required."));
            }

            ParsedHeader parsedHeader = new(
                RegisteredTaxpayerName: RequireString(header, "registeredTaxpayerName"),
                BusinessName: ReadString(header, "businessName"),
                TaxpayerIdentificationNumber: RequireString(header, "taxpayerIdentificationNumber"),
                BranchCode: RequireString(header, "branchCode"),
                RegisteredBusinessAddress: RequireString(header, "registeredBusinessAddress"),
                TransactionDateTime: ParseDate(RequireString(header, "transactionDateTime")),
                DocumentNumber: RequireString(header, "documentNumber"),
                MachineSerialNumber: ReadString(header, "machineSerialNumber"),
                MachineIdentificationNumber: ReadString(header, "machineIdentificationNumber"),
                AccreditationNumber: ReadString(header, "accreditationNumber"),
                PermitToUseNumber: ReadString(header, "permitToUseNumber"),
                CashierIdentifier: RequireString(header, "cashierIdentifier"),
                TerminalIdentifier: ReadString(header, "terminalIdentifier"));

            if (!root.TryGetProperty("lines", out JsonElement linesEl) || linesEl.ValueKind != JsonValueKind.Array ||
                linesEl.GetArrayLength() == 0)
            {
                return Result<ParsedOfficialInvoiceSnapshot>.Fail(Error.Validation(
                    "InvoiceSnapshot.LinesRequired",
                    "Snapshot lines are required."));
            }

            List<ParsedLine> lines = new();
            foreach (JsonElement line in linesEl.EnumerateArray())
            {
                lines.Add(new ParsedLine(
                    Description: RequireString(line, "description"),
                    UnitName: ReadString(line, "unitName") ?? "pc",
                    QuantityX1000: RequireLong(line, "quantityX1000"),
                    UnitPriceCents: RequireLong(line, "unitPriceCents"),
                    DiscountCents: RequireLong(line, "discountCents"),
                    LineTotalCents: RequireLong(line, "lineTotalCents")));
            }

            if (!root.TryGetProperty("amounts", out JsonElement amountsEl))
            {
                return Result<ParsedOfficialInvoiceSnapshot>.Fail(Error.Validation(
                    "InvoiceSnapshot.AmountsRequired",
                    "Snapshot amounts are required."));
            }

            ParsedAmounts amounts = new(
                SubtotalCents: RequireLong(amountsEl, "subtotalCents"),
                DiscountCents: RequireLong(amountsEl, "discountCents"),
                TaxCents: RequireLong(amountsEl, "taxCents"),
                TotalCents: RequireLong(amountsEl, "totalCents"),
                AmountPaidCents: RequireLong(amountsEl, "amountPaidCents"),
                ChangeCents: RequireLong(amountsEl, "changeCents"));

            List<ParsedPayment> payments = new();
            if (root.TryGetProperty("payments", out JsonElement paymentsEl) && paymentsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement payment in paymentsEl.EnumerateArray())
                {
                    payments.Add(new ParsedPayment(
                        Method: ReadString(payment, "method") ?? "Unknown",
                        AmountCents: RequireLong(payment, "amountCents")));
                }
            }

            ParsedTaxBuckets? taxBuckets = null;
            if (root.TryGetProperty("taxBuckets", out JsonElement taxBucketsEl) &&
                taxBucketsEl.ValueKind == JsonValueKind.Object)
            {
                string taxpayerMode = ReadString(taxBucketsEl, "taxpayerMode") ?? "Unknown";
                string? nonVatStatement = ReadString(taxBucketsEl, "nonVatStatement");
                List<ParsedBucket> buckets = new();
                if (taxBucketsEl.TryGetProperty("buckets", out JsonElement bucketsEl) &&
                    bucketsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement bucket in bucketsEl.EnumerateArray())
                    {
                        buckets.Add(new ParsedBucket(
                            Kind: ReadString(bucket, "kind") ?? "Unknown",
                            SalesAmountCents: RequireLong(bucket, "salesAmountCents"),
                            TaxAmountCents: RequireLong(bucket, "taxAmountCents")));
                    }
                }

                taxBuckets = new ParsedTaxBuckets(taxpayerMode, nonVatStatement, buckets);
            }

            return Result<ParsedOfficialInvoiceSnapshot>.Success(new ParsedOfficialInvoiceSnapshot(
                Identity: identity with { HashVerified = true },
                DocumentKind: documentKind,
                Header: parsedHeader,
                Lines: lines,
                Amounts: amounts,
                Payments: payments,
                TaxBuckets: taxBuckets));
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or KeyNotFoundException)
        {
            return Result<ParsedOfficialInvoiceSnapshot>.Fail(Error.Validation(
                "InvoiceSnapshot.ParseFailed",
                "Committed snapshot payload could not be parsed for invoice output."));
        }
    }

    public static string ComputeSha256Hex(string utf8Text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(utf8Text);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string RequireString(JsonElement element, string name)
    {
        string? value = ReadString(element, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Missing required snapshot field: " + name);
        }

        return value.Trim();
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.GetString();
    }

    private static long RequireLong(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Number)
        {
            throw new InvalidOperationException("Missing required numeric snapshot field: " + name);
        }

        return value.GetInt64();
    }

    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}

public sealed record ParsedOfficialInvoiceSnapshot(
    OfficialInvoiceSnapshotIdentity Identity,
    string DocumentKind,
    ParsedHeader Header,
    IReadOnlyList<ParsedLine> Lines,
    ParsedAmounts Amounts,
    IReadOnlyList<ParsedPayment> Payments,
    ParsedTaxBuckets? TaxBuckets);

public sealed record ParsedHeader(
    string RegisteredTaxpayerName,
    string? BusinessName,
    string TaxpayerIdentificationNumber,
    string BranchCode,
    string RegisteredBusinessAddress,
    DateTimeOffset TransactionDateTime,
    string DocumentNumber,
    string? MachineSerialNumber,
    string? MachineIdentificationNumber,
    string? AccreditationNumber,
    string? PermitToUseNumber,
    string CashierIdentifier,
    string? TerminalIdentifier);

public sealed record ParsedLine(
    string Description,
    string UnitName,
    long QuantityX1000,
    long UnitPriceCents,
    long DiscountCents,
    long LineTotalCents);

public sealed record ParsedAmounts(
    long SubtotalCents,
    long DiscountCents,
    long TaxCents,
    long TotalCents,
    long AmountPaidCents,
    long ChangeCents);

public sealed record ParsedPayment(string Method, long AmountCents);

public sealed record ParsedBucket(string Kind, long SalesAmountCents, long TaxAmountCents);

public sealed record ParsedTaxBuckets(
    string TaxpayerMode,
    string? NonVatStatement,
    IReadOnlyList<ParsedBucket> Buckets);
