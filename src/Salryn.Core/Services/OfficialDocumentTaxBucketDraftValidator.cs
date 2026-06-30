using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Core.Services;

// Kirch strict dev guide:
// J21-4D owns C05 tax bucket draft validation only.
// This validator rejects structurally unsafe OfficialDocumentTaxBucketDraft values.
// It must not perform tax computation, infer legal tax treatment, render receipt text,
// print, persist, wire checkout, allocate sequence numbers, create journals,
// create X/Z readings, or claim official output readiness.
public sealed class OfficialDocumentTaxBucketDraftValidator
{
    public Result Validate(OfficialDocumentTaxBucketDraft draft)
    {
        if (draft is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentTaxBucketDraft.Required",
                "Tax bucket draft is required."));
        }

        if (!Enum.IsDefined(typeof(OfficialDocumentTaxBucketDraftTaxpayerMode), draft.TaxpayerMode) ||
            draft.TaxpayerMode == OfficialDocumentTaxBucketDraftTaxpayerMode.Unknown)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentTaxBucketDraft.TaxpayerModeRequired",
                "Known taxpayer mode is required for the tax bucket draft."));
        }

        if (draft.Buckets is null || draft.Buckets.Count == 0)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentTaxBucketDraft.BucketsRequired",
                "At least one tax bucket draft row is required."));
        }

        Result bucketValidation = ValidateBuckets(draft.Buckets);

        if (bucketValidation.IsFailure)
        {
            return bucketValidation;
        }

        Result boundaryValidation = ValidateDeferredBoundaryNotes(draft.DeferredComplianceNotes);

        if (boundaryValidation.IsFailure)
        {
            return boundaryValidation;
        }

        Result modeValidation = ValidateTaxpayerModeShape(draft);

        if (modeValidation.IsFailure)
        {
            return modeValidation;
        }

        return Result.Success();
    }

    public Result ValidateEngineComputed(OfficialDocumentTaxBucketDraft draft)
    {
        if (draft is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentTaxBucketDraft.Required",
                "Tax bucket draft is required."));
        }

        if (!Enum.IsDefined(typeof(OfficialDocumentTaxBucketDraftTaxpayerMode), draft.TaxpayerMode) ||
            draft.TaxpayerMode == OfficialDocumentTaxBucketDraftTaxpayerMode.Unknown)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentTaxBucketDraft.TaxpayerModeRequired",
                "Known taxpayer mode is required for the tax bucket draft."));
        }

        if (draft.Buckets is null || draft.Buckets.Count == 0)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentTaxBucketDraft.BucketsRequired",
                "At least one tax bucket draft row is required."));
        }

        Result bucketValidation = ValidateBuckets(draft.Buckets);

        if (bucketValidation.IsFailure)
        {
            return bucketValidation;
        }

        Result boundaryValidation = ValidateEngineComputedBoundaryNotes(draft.DeferredComplianceNotes);

        if (boundaryValidation.IsFailure)
        {
            return boundaryValidation;
        }

        Result modeValidation = ValidateTaxpayerModeShape(draft);

        if (modeValidation.IsFailure)
        {
            return modeValidation;
        }

        return Result.Success();
    }

    private static Result ValidateEngineComputedBoundaryNotes(IReadOnlyList<string> deferredComplianceNotes)
    {
        if (deferredComplianceNotes is null || deferredComplianceNotes.Count == 0)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentTaxBucketDraft.DeferredBoundaryRequired",
                "Deferred compliance boundary notes are required."));
        }

        if (!ContainsBoundaryNote(deferredComplianceNotes, SalrynOfficialTaxBucketPolicy.EngineBoundaryNote))
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentTaxBucketDraft.EngineBoundaryRequired",
                "Engine-computed draft must preserve the engineering engine boundary note."));
        }

        if (!ContainsBoundaryNote(deferredComplianceNotes, SalrynOfficialTaxBucketPolicy.NoLegalVatClaimNote))
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentTaxBucketDraft.NoLegalVatClaimRequired",
                "Engine-computed draft must preserve the no-legal-VAT-claim boundary."));
        }

        if (!ContainsBoundaryNote(deferredComplianceNotes, SalrynOfficialTaxBucketPolicy.NoOfficialOutputRenderingNote))
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentTaxBucketDraft.NoOutputBoundaryRequired",
                "Deferred notes must preserve the no-output boundary."));
        }

        if (ContainsForbiddenOfficialOutputClaim(deferredComplianceNotes))
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentTaxBucketDraft.OfficialOutputClaimNotAllowed",
                "Tax bucket draft notes must not claim final official output readiness."));
        }

        return Result.Success();
    }

    private static Result ValidateBuckets(IReadOnlyList<OfficialDocumentTaxBucketDraftBucket> buckets)
    {
        HashSet<OfficialDocumentTaxBucketDraftKind> seenKinds = new();

        foreach (OfficialDocumentTaxBucketDraftBucket? bucket in buckets)
        {
            if (bucket is null)
            {
                return Result.Fail(Error.Validation(
                    "OfficialDocumentTaxBucketDraft.BucketRequired",
                    "Tax bucket draft row cannot be null."));
            }

            if (!Enum.IsDefined(typeof(OfficialDocumentTaxBucketDraftKind), bucket.Kind))
            {
                return Result.Fail(Error.Validation(
                    "OfficialDocumentTaxBucketDraft.BucketKindInvalid",
                    "Tax bucket draft kind is invalid."));
            }

            if (bucket.SalesAmountCents < 0 || bucket.TaxAmountCents < 0)
            {
                return Result.Fail(Error.Validation(
                    "OfficialDocumentTaxBucketDraft.NegativeAmount",
                    "Tax bucket draft amounts cannot be negative."));
            }

            if (!seenKinds.Add(bucket.Kind))
            {
                return Result.Fail(Error.Validation(
                    "OfficialDocumentTaxBucketDraft.DuplicateBucketKind",
                    "Tax bucket draft cannot contain duplicate bucket kinds."));
            }
        }

        return Result.Success();
    }

    private static Result ValidateDeferredBoundaryNotes(IReadOnlyList<string> deferredComplianceNotes)
    {
        if (deferredComplianceNotes is null || deferredComplianceNotes.Count == 0)
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentTaxBucketDraft.DeferredBoundaryRequired",
                "Deferred compliance boundary notes are required."));
        }

        if (!ContainsBoundaryNote(deferredComplianceNotes, "No VAT bucket engine."))
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentTaxBucketDraft.NoEngineBoundaryRequired",
                "Deferred notes must preserve the no-engine boundary."));
        }

        if (!ContainsBoundaryNote(deferredComplianceNotes, "No official output rendering."))
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentTaxBucketDraft.NoOutputBoundaryRequired",
                "Deferred notes must preserve the no-output boundary."));
        }

        if (ContainsForbiddenOfficialOutputClaim(deferredComplianceNotes))
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentTaxBucketDraft.OfficialOutputClaimNotAllowed",
                "Tax bucket draft notes must not claim final official output readiness."));
        }

        return Result.Success();
    }

    private static Result ValidateTaxpayerModeShape(OfficialDocumentTaxBucketDraft draft)
    {
        if (draft.TaxpayerMode == OfficialDocumentTaxBucketDraftTaxpayerMode.NonVatRegistered)
        {
            if (string.IsNullOrWhiteSpace(draft.NonVatStatement))
            {
                return Result.Fail(Error.Validation(
                    "OfficialDocumentTaxBucketDraft.NonVatStatementRequired",
                    "Non-VAT draft requires a non-VAT statement placeholder."));
            }

            if (!ContainsBucketKind(draft.Buckets, OfficialDocumentTaxBucketDraftKind.NonVatSales))
            {
                return Result.Fail(Error.Validation(
                    "OfficialDocumentTaxBucketDraft.NonVatBucketRequired",
                    "Non-VAT draft requires a non-VAT sales bucket shape."));
            }
        }

        if (draft.TaxpayerMode == OfficialDocumentTaxBucketDraftTaxpayerMode.VatRegistered &&
            !string.IsNullOrWhiteSpace(draft.NonVatStatement))
        {
            return Result.Fail(Error.Validation(
                "OfficialDocumentTaxBucketDraft.VatDraftNonVatStatementNotAllowed",
                "VAT-registered draft must not carry a non-VAT statement placeholder."));
        }

        return Result.Success();
    }

    private static bool ContainsBucketKind(
        IReadOnlyList<OfficialDocumentTaxBucketDraftBucket> buckets,
        OfficialDocumentTaxBucketDraftKind kind)
    {
        foreach (OfficialDocumentTaxBucketDraftBucket bucket in buckets)
        {
            if (bucket.Kind == kind)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsBoundaryNote(IReadOnlyList<string> notes, string expected)
    {
        foreach (string? note in notes)
        {
            if (string.Equals(note?.Trim(), expected, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsForbiddenOfficialOutputClaim(IReadOnlyList<string> notes)
    {
        foreach (string? note in notes)
        {
            if (string.IsNullOrWhiteSpace(note))
            {
                continue;
            }

            if (note.Contains("final official output", StringComparison.OrdinalIgnoreCase) ||
                note.Contains("official output ready", StringComparison.OrdinalIgnoreCase) ||
                note.Contains("official receipt output", StringComparison.OrdinalIgnoreCase) ||
                note.Contains("official invoice output", StringComparison.OrdinalIgnoreCase) ||
                note.Contains("can render official", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}