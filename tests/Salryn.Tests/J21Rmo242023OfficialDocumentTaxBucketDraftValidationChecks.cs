using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Services;

namespace Salryn.Tests;

public static class J21Rmo242023OfficialDocumentTaxBucketDraftValidationChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string validatorText = ReadRequiredFile(
            repoRoot,
            "src",
            "Salryn.Core",
            "Services",
            "OfficialDocumentTaxBucketDraftValidator.cs");

        string matrixText = ReadRequiredFile(
            repoRoot,
            "00-COMPLIANCE",
            "RMO_24_2023_COMPLIANCE_MATRIX.md");

        string handoffText = ReadRequiredFile(
            repoRoot,
            "00-HANDOFF",
            "J21_4D_C05_TAX_BUCKET_DRAFT_VALIDATION.md");

        string coreSmokeText = ReadRequiredFile(
            repoRoot,
            "tools",
            "Salryn.Tools.CoreSmoke",
            "Program.cs");

        RequireContains(validatorText, "OfficialDocumentTaxBucketDraftValidator", "J21-4D must define tax bucket draft validator.");
        RequireContains(validatorText, "J21-4D owns C05 tax bucket draft validation only.", "J21-4D validator must explain ownership.");
        RequireContains(validatorText, "It must not perform tax computation", "J21-4D validator must block tax computation.");
        RequireContains(validatorText, "infer legal tax treatment", "J21-4D validator must block legal tax treatment inference.");
        RequireContains(validatorText, "Known taxpayer mode is required", "J21-4D validator must require known taxpayer mode.");
        RequireContains(validatorText, "At least one tax bucket draft row is required", "J21-4D validator must require buckets.");
        RequireContains(validatorText, "Tax bucket draft kind is invalid", "J21-4D validator must reject invalid bucket kinds.");
        RequireContains(validatorText, "Tax bucket draft amounts cannot be negative", "J21-4D validator must reject negative amounts.");
        RequireContains(validatorText, "duplicate bucket kinds", "J21-4D validator must reject duplicate bucket kinds.");
        RequireContains(validatorText, "Deferred notes must preserve the no-engine boundary", "J21-4D validator must require no-engine boundary.");
        RequireContains(validatorText, "Deferred notes must preserve the no-output boundary", "J21-4D validator must require no-output boundary.");
        RequireContains(validatorText, "Non-VAT draft requires a non-VAT statement placeholder", "J21-4D validator must require non-VAT statement placeholder.");
        RequireContains(validatorText, "VAT-registered draft must not carry a non-VAT statement placeholder", "J21-4D validator must protect VAT draft shape.");
        RequireDoesNotContain(validatorText, "Microsoft.Data.Sqlite", "J21-4D validator must not depend on SQLite.");
        RequireDoesNotContain(validatorText, "SqliteConnection", "J21-4D validator must not open database connections.");
        RequireDoesNotContain(validatorText, "ReceiptNumber.Create", "J21-4D validator must not allocate sequence numbers.");
        RequireDoesNotContain(validatorText, "AllocateNextAsync", "J21-4D validator must not call sequence allocation.");
        RequireDoesNotContain(validatorText, "System.Drawing.Printing", "J21-4D validator must not depend on printing.");
        RequireDoesNotContain(validatorText, "PrintDocument", "J21-4D validator must not create print documents.");
        RequireDoesNotContain(validatorText, "receipt_snapshot", "J21-4D validator must not write saved sale receipt_snapshot fields.");
        RequireDoesNotContain(validatorText, "CompleteSale", "J21-4D validator must not wire checkout or sale completion.");
        RequireDoesNotContain(validatorText, "0.12", "J21-4D validator must not encode VAT rate rules.");
        RequireDoesNotContain(validatorText, "12%", "J21-4D validator must not encode VAT rate rules.");
        RequireContains(matrixText, "## J21-4D C05 Tax Bucket Draft Validation", "Compliance matrix must include J21-4D section.");
        RequireContains(handoffText, "Status: validation only.", "J21-4D handoff must preserve validation-only scope.");
        RequireContains(handoffText, "No tax calculator engine.", "J21-4D handoff must block tax engine work.");
        RequireContains(handoffText, "No VAT computation rules.", "J21-4D handoff must block VAT computation rules.");
        RequireContains(handoffText, "No renderer.", "J21-4D handoff must block renderer work.");
        RequireContains(handoffText, "No checkout wiring.", "J21-4D handoff must block checkout wiring.");
        RequireContains(handoffText, "No saved sale receipt_snapshot writes.", "J21-4D handoff must block saved snapshot writes.");
        RequireContains(coreSmokeText, "J21Rmo242023OfficialDocumentTaxBucketDraftValidationChecks.Run();", "Core smoke must run J21-4D checks.");

        OfficialDocumentTaxBucketDraftValidator validator = new();

        Result validVat = validator.Validate(new OfficialDocumentTaxBucketDraft(
            OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered,
            new[]
            {
                new OfficialDocumentTaxBucketDraftBucket(
                    OfficialDocumentTaxBucketDraftKind.VatableSales,
                    SalesAmountCents: 10000,
                    TaxAmountCents: 0,
                    Notes: "Shape only."),
                new OfficialDocumentTaxBucketDraftBucket(
                    OfficialDocumentTaxBucketDraftKind.VatAmount,
                    SalesAmountCents: 0,
                    TaxAmountCents: 1200,
                    Notes: "Shape only.")
            },
            NonVatStatement: null,
            new[]
            {
                "No VAT bucket engine.",
                "No official output rendering."
            }));

        RequireSuccess(validVat, "Valid VAT draft shape should pass validation.");

        Result validNonVat = validator.Validate(new OfficialDocumentTaxBucketDraft(
            OfficialDocumentTaxBucketDraftTaxpayerMode.NonVatRegistered,
            new[]
            {
                new OfficialDocumentTaxBucketDraftBucket(
                    OfficialDocumentTaxBucketDraftKind.NonVatSales,
                    SalesAmountCents: 10000,
                    TaxAmountCents: 0,
                    Notes: "Shape only.")
            },
            NonVatStatement: "NON-VAT draft statement placeholder.",
            new[]
            {
                "No VAT bucket engine.",
                "No official output rendering."
            }));

        RequireSuccess(validNonVat, "Valid non-VAT draft shape should pass validation.");

        RequireFailureCode(
            validator.Validate(new OfficialDocumentTaxBucketDraft(
                OfficialDocumentTaxBucketDraftTaxpayerMode.Unknown,
                new[]
                {
                    new OfficialDocumentTaxBucketDraftBucket(
                        OfficialDocumentTaxBucketDraftKind.NonVatSales,
                        SalesAmountCents: 10000,
                        TaxAmountCents: 0,
                        Notes: "Shape only.")
                },
                NonVatStatement: "NON-VAT draft statement placeholder.",
                new[]
                {
                    "No VAT bucket engine.",
                    "No official output rendering."
                })),
            "OfficialDocumentTaxBucketDraft.TaxpayerModeRequired",
            "Unknown taxpayer mode must fail validation.");

        RequireFailureCode(
            validator.Validate(new OfficialDocumentTaxBucketDraft(
                OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered,
                Array.Empty<OfficialDocumentTaxBucketDraftBucket>(),
                NonVatStatement: null,
                new[]
                {
                    "No VAT bucket engine.",
                    "No official output rendering."
                })),
            "OfficialDocumentTaxBucketDraft.BucketsRequired",
            "Missing buckets must fail validation.");

        RequireFailureCode(
            validator.Validate(new OfficialDocumentTaxBucketDraft(
                OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered,
                new[]
                {
                    new OfficialDocumentTaxBucketDraftBucket(
                        (OfficialDocumentTaxBucketDraftKind)999,
                        SalesAmountCents: 10000,
                        TaxAmountCents: 0,
                        Notes: "Shape only.")
                },
                NonVatStatement: null,
                new[]
                {
                    "No VAT bucket engine.",
                    "No official output rendering."
                })),
            "OfficialDocumentTaxBucketDraft.BucketKindInvalid",
            "Invalid bucket kind must fail validation.");

        RequireFailureCode(
            validator.Validate(new OfficialDocumentTaxBucketDraft(
                OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered,
                new[]
                {
                    new OfficialDocumentTaxBucketDraftBucket(
                        OfficialDocumentTaxBucketDraftKind.VatableSales,
                        SalesAmountCents: -1,
                        TaxAmountCents: 0,
                        Notes: "Shape only.")
                },
                NonVatStatement: null,
                new[]
                {
                    "No VAT bucket engine.",
                    "No official output rendering."
                })),
            "OfficialDocumentTaxBucketDraft.NegativeAmount",
            "Negative amounts must fail validation.");

        RequireFailureCode(
            validator.Validate(new OfficialDocumentTaxBucketDraft(
                OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered,
                new[]
                {
                    new OfficialDocumentTaxBucketDraftBucket(
                        OfficialDocumentTaxBucketDraftKind.VatableSales,
                        SalesAmountCents: 10000,
                        TaxAmountCents: 0,
                        Notes: "Shape only."),
                    new OfficialDocumentTaxBucketDraftBucket(
                        OfficialDocumentTaxBucketDraftKind.VatableSales,
                        SalesAmountCents: 5000,
                        TaxAmountCents: 0,
                        Notes: "Duplicate shape only.")
                },
                NonVatStatement: null,
                new[]
                {
                    "No VAT bucket engine.",
                    "No official output rendering."
                })),
            "OfficialDocumentTaxBucketDraft.DuplicateBucketKind",
            "Duplicate bucket kinds must fail validation.");

        RequireFailureCode(
            validator.Validate(new OfficialDocumentTaxBucketDraft(
                OfficialDocumentTaxBucketDraftTaxpayerMode.NonVatRegistered,
                new[]
                {
                    new OfficialDocumentTaxBucketDraftBucket(
                        OfficialDocumentTaxBucketDraftKind.NonVatSales,
                        SalesAmountCents: 10000,
                        TaxAmountCents: 0,
                        Notes: "Shape only.")
                },
                NonVatStatement: null,
                new[]
                {
                    "No VAT bucket engine.",
                    "No official output rendering."
                })),
            "OfficialDocumentTaxBucketDraft.NonVatStatementRequired",
            "Non-VAT draft must require statement placeholder.");

        RequireFailureCode(
            validator.Validate(new OfficialDocumentTaxBucketDraft(
                OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered,
                new[]
                {
                    new OfficialDocumentTaxBucketDraftBucket(
                        OfficialDocumentTaxBucketDraftKind.VatableSales,
                        SalesAmountCents: 10000,
                        TaxAmountCents: 0,
                        Notes: "Shape only.")
                },
                NonVatStatement: "NON-VAT draft statement placeholder.",
                new[]
                {
                    "No VAT bucket engine.",
                    "No official output rendering."
                })),
            "OfficialDocumentTaxBucketDraft.VatDraftNonVatStatementNotAllowed",
            "VAT draft must not carry non-VAT statement placeholder.");

        RequireFailureCode(
            validator.Validate(new OfficialDocumentTaxBucketDraft(
                OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered,
                new[]
                {
                    new OfficialDocumentTaxBucketDraftBucket(
                        OfficialDocumentTaxBucketDraftKind.VatableSales,
                        SalesAmountCents: 10000,
                        TaxAmountCents: 0,
                        Notes: "Shape only.")
                },
                NonVatStatement: null,
                new[]
                {
                    "No official output rendering."
                })),
            "OfficialDocumentTaxBucketDraft.NoEngineBoundaryRequired",
            "Missing no-engine boundary must fail validation.");

        RequireFailureCode(
            validator.Validate(new OfficialDocumentTaxBucketDraft(
                OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered,
                new[]
                {
                    new OfficialDocumentTaxBucketDraftBucket(
                        OfficialDocumentTaxBucketDraftKind.VatableSales,
                        SalesAmountCents: 10000,
                        TaxAmountCents: 0,
                        Notes: "Shape only.")
                },
                NonVatStatement: null,
                new[]
                {
                    "No VAT bucket engine."
                })),
            "OfficialDocumentTaxBucketDraft.NoOutputBoundaryRequired",
            "Missing no-output boundary must fail validation.");
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current;
            }

            current = current.Parent;
        }

        current = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate Salryn repository root.");
    }

    private static string ReadRequiredFile(DirectoryInfo repoRoot, params string[] pathParts)
    {
        string path = repoRoot.FullName;

        foreach (string pathPart in pathParts)
        {
            path = Path.Combine(path, pathPart);
        }

        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Required file was not found: {path}");
        }

        return File.ReadAllText(path);
    }

    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireDoesNotContain(string text, string value, string message)
    {
        if (text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void RequireSuccess(Result result, string message)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{message} Error: {result.Error}");
        }
    }

    private static void RequireFailureCode(Result result, string expectedCode, string message)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException(message);
        }

        if (!string.Equals(result.ErrorDetail.Code, expectedCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected error code {expectedCode}, found {result.ErrorDetail.Code}.");
        }
    }
}