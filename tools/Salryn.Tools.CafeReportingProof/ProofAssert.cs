using Salryn.Core.Common;

namespace Salryn.Tools.CafeReportingProof;

internal static class ProofAssert
{
    public static void That(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    public static T Value<T>(Result<T> result, string operation)
    {
        if (result.IsFailure)
            throw new InvalidOperationException(
                operation + " failed: " +
                result.ErrorDetail.Code + " / " +
                result.ErrorDetail.Message);

        return result.Value;
    }

    public static void Contains(
        string content,
        string expected,
        string message)
    {
        That(
            content.Contains(expected, StringComparison.Ordinal),
            message + " Missing: " + expected);
    }
}
