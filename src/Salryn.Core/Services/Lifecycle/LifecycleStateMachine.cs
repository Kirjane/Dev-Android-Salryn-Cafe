using Salryn.Core.Common;
using Salryn.Core.Services;

namespace Salryn.Core.Services.Lifecycle;

public static class LifecycleStateMachine
{
    public static Result Guard(LifecycleDocumentState current, OfficialDocumentLifecycleAction action)
    {
        return action switch
        {
            OfficialDocumentLifecycleAction.Reprint =>
                current is LifecycleDocumentState.Voided or LifecycleDocumentState.CancelledBeforeCommit
                    ? Fail("DocumentLifecycle.ReprintNotAllowed", "Reprint is not allowed from current state.")
                    : Result.Success(),

            OfficialDocumentLifecycleAction.Void =>
                current == LifecycleDocumentState.Committed
                    ? Result.Success()
                    : Fail("DocumentLifecycle.VoidNotAllowed", "Void is only allowed from a non-refunded committed state."),

            OfficialDocumentLifecycleAction.Refund =>
                current is LifecycleDocumentState.Committed
                    or LifecycleDocumentState.PartiallyRefunded
                    or LifecycleDocumentState.RefundPendingSettlement
                    ? Result.Success()
                    : Fail("DocumentLifecycle.RefundNotAllowed", "Refund is not allowed from current lifecycle state."),

            OfficialDocumentLifecycleAction.Cancel =>
                Fail("DocumentLifecycle.PostCommitCancelForbidden",
                    "Cancel after official commit is forbidden. Use void/refund/adjustment."),

            OfficialDocumentLifecycleAction.Adjust =>
                current is LifecycleDocumentState.Voided or LifecycleDocumentState.CancelledBeforeCommit
                    ? Fail("DocumentLifecycle.AdjustNotAllowed", "Adjustment is not allowed from current state.")
                    : Result.Success(),

            _ => Fail("DocumentLifecycle.UnknownAction", "Unknown lifecycle action.")
        };
    }

    public static LifecycleDocumentState Parse(string code) => code switch
    {
        "Committed" => LifecycleDocumentState.Committed,
        "PartiallyRefunded" => LifecycleDocumentState.PartiallyRefunded,
        "FullyRefunded" => LifecycleDocumentState.FullyRefunded,
        "Voided" => LifecycleDocumentState.Voided,
        "RefundPendingSettlement" => LifecycleDocumentState.RefundPendingSettlement,
        "Adjusted" => LifecycleDocumentState.Adjusted,
        "CancelledBeforeCommit" => LifecycleDocumentState.CancelledBeforeCommit,
        _ => LifecycleDocumentState.Committed
    };

    public static string ToCode(LifecycleDocumentState state) => state.ToString();

    private static Result Fail(string code, string message) =>
        Result.Fail(Error.Conflict(code, message));
}
