using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Core.Services;

// Kirch: why - keeps the future journal append inside one ordered transaction boundary.
public static class ElectronicJournalAppendAtomicBoundaryRules
{
    public static Result Validate(ElectronicJournalAppendContract contract)
    {
        IReadOnlyList<ElectronicJournalAtomicBoundaryStep> steps = contract.AtomicBoundarySteps;

        Result duplicateValidation = ValidateKnownUniqueSteps(steps);
        if (duplicateValidation.IsFailure)
        {
            return duplicateValidation;
        }

        if (steps[0] != ElectronicJournalAtomicBoundaryStep.OfficialSequenceAllocation)
        {
            return Result.Fail(Error.Validation(
                "ElectronicJournalAppend.SequenceBoundaryMustStart",
                "Official sequence allocation must be the first atomic append boundary step."));
        }

        Result requiredValidation = RequireStep(
            steps,
            ElectronicJournalAtomicBoundaryStep.JournalAnchorInsert,
            "ElectronicJournalAppend.AnchorBoundaryRequired",
            "Journal anchor insert boundary is required.");

        if (requiredValidation.IsFailure)
        {
            return requiredValidation;
        }

        requiredValidation = RequireStep(
            steps,
            ElectronicJournalAtomicBoundaryStep.OriginalEventInsert,
            "ElectronicJournalAppend.OriginalEventBoundaryRequired",
            "ORIGINAL event insert boundary is required.");

        if (requiredValidation.IsFailure)
        {
            return requiredValidation;
        }

        if (contract.SnapshotEvidence is not null)
        {
            requiredValidation = RequireStep(
                steps,
                ElectronicJournalAtomicBoundaryStep.SnapshotPayloadHashPersistence,
                "ElectronicJournalAppend.SnapshotBoundaryRequired",
                "Snapshot payload/hash persistence boundary is required when snapshot evidence is supplied.");

            if (requiredValidation.IsFailure)
            {
                return requiredValidation;
            }
        }

        Result orderValidation = RequireBefore(
            steps,
            ElectronicJournalAtomicBoundaryStep.OfficialSequenceAllocation,
            ElectronicJournalAtomicBoundaryStep.JournalAnchorInsert,
            "ElectronicJournalAppend.AnchorBoundaryOrderInvalid",
            "Journal anchor insert must happen after official sequence allocation.");

        if (orderValidation.IsFailure)
        {
            return orderValidation;
        }

        orderValidation = RequireBefore(
            steps,
            ElectronicJournalAtomicBoundaryStep.JournalAnchorInsert,
            ElectronicJournalAtomicBoundaryStep.OriginalEventInsert,
            "ElectronicJournalAppend.OriginalEventBoundaryOrderInvalid",
            "ORIGINAL event insert must happen after journal anchor insert.");

        if (orderValidation.IsFailure)
        {
            return orderValidation;
        }

        if (steps.Contains(ElectronicJournalAtomicBoundaryStep.SnapshotPayloadHashPersistence))
        {
            orderValidation = RequireBefore(
                steps,
                ElectronicJournalAtomicBoundaryStep.OfficialSequenceAllocation,
                ElectronicJournalAtomicBoundaryStep.SnapshotPayloadHashPersistence,
                "ElectronicJournalAppend.SnapshotBoundaryOrderInvalid",
                "Snapshot payload/hash persistence must happen after official sequence allocation.");

            if (orderValidation.IsFailure)
            {
                return orderValidation;
            }

            orderValidation = RequireBefore(
                steps,
                ElectronicJournalAtomicBoundaryStep.SnapshotPayloadHashPersistence,
                ElectronicJournalAtomicBoundaryStep.JournalAnchorInsert,
                "ElectronicJournalAppend.SnapshotBoundaryOrderInvalid",
                "Snapshot payload/hash persistence must happen before journal anchor insert.");

            if (orderValidation.IsFailure)
            {
                return orderValidation;
            }
        }

        if (steps.Contains(ElectronicJournalAtomicBoundaryStep.SaleOfficialSnapshotLink))
        {
            return RequireBefore(
                steps,
                ElectronicJournalAtomicBoundaryStep.OriginalEventInsert,
                ElectronicJournalAtomicBoundaryStep.SaleOfficialSnapshotLink,
                "ElectronicJournalAppend.SaleLinkBoundaryOrderInvalid",
                "Sale official snapshot link must happen after ORIGINAL event insert.");
        }

        return Result.Success();
    }

    private static Result ValidateKnownUniqueSteps(IReadOnlyList<ElectronicJournalAtomicBoundaryStep> steps)
    {
        HashSet<ElectronicJournalAtomicBoundaryStep> seen = new();

        foreach (ElectronicJournalAtomicBoundaryStep step in steps)
        {
            if (!Enum.IsDefined(typeof(ElectronicJournalAtomicBoundaryStep), step))
            {
                return Result.Fail(Error.Validation(
                    "ElectronicJournalAppend.UnknownBoundaryStep",
                    "Atomic append boundary step is not recognized."));
            }

            if (!seen.Add(step))
            {
                return Result.Fail(Error.Validation(
                    "ElectronicJournalAppend.DuplicateBoundaryStep",
                    "Atomic append boundary steps must not contain duplicates."));
            }
        }

        return Result.Success();
    }

    private static Result RequireStep(
        IReadOnlyList<ElectronicJournalAtomicBoundaryStep> steps,
        ElectronicJournalAtomicBoundaryStep requiredStep,
        string code,
        string message)
    {
        return steps.Contains(requiredStep)
            ? Result.Success()
            : Result.Fail(Error.Validation(code, message));
    }

    private static Result RequireBefore(
        IReadOnlyList<ElectronicJournalAtomicBoundaryStep> steps,
        ElectronicJournalAtomicBoundaryStep first,
        ElectronicJournalAtomicBoundaryStep second,
        string code,
        string message)
    {
        int firstIndex = IndexOf(steps, first);
        int secondIndex = IndexOf(steps, second);

        if (firstIndex >= 0 && secondIndex >= 0 && firstIndex > secondIndex)
        {
            return Result.Fail(Error.Validation(code, message));
        }

        return Result.Success();
    }

    private static int IndexOf(
        IReadOnlyList<ElectronicJournalAtomicBoundaryStep> steps,
        ElectronicJournalAtomicBoundaryStep step)
    {
        for (int index = 0; index < steps.Count; index++)
        {
            if (steps[index] == step)
            {
                return index;
            }
        }

        return -1;
    }
}
