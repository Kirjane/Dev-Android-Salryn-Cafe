using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Salryn.Core.Common;

namespace Salryn.Core.Services;

public enum OfficialExternalOutputVerificationStatus
{
    Passed = 1,
    Failed = 2
}

public sealed record OfficialExternalOutputVerificationFileResult(
    string RelativePath,
    OfficialExternalOutputWrittenArtifactRole Role,
    string ExpectedHash,
    string ActualHash,
    int ExpectedLineCount,
    int ActualLineCount,
    long ExpectedByteCount,
    long ActualByteCount,
    bool Passed,
    string Message);

public sealed record OfficialExternalOutputVerificationResult(
    OfficialExternalOutputVerificationStatus VerificationStatus,
    string ExecutionReference,
    string ExecutionHash,
    string PackageReference,
    string PackageHash,
    string DispatchPlanReference,
    string DispatchPlanHash,
    string LedgerReference,
    string LedgerHash,
    IReadOnlyList<OfficialExternalOutputVerificationFileResult> Files,
    int CheckedFileCount,
    int PassedFileCount,
    int FailedFileCount,
    string VerificationHash,
    string VerificationReference,
    DateTimeOffset? VerifiedAt,
    OfficialExternalOutputVerificationTextRenderResult TextResult);

public sealed record OfficialExternalOutputVerificationTextRenderResult(
    IReadOnlyList<string> Lines,
    string Text);

public sealed class OfficialExternalOutputVerificationResultBuilder
{
    private const string VerificationReferencePrefix = "salryn://official-external-output-verification/";

    public Result<OfficialExternalOutputVerificationResult> Build(
        OfficialExternalOutputExecutionResult? executionResult,
        IReadOnlyList<OfficialExternalOutputVerificationFileResult>? files,
        DateTimeOffset? verifiedAt = null)
    {
        if (executionResult is null)
        {
            return Result<OfficialExternalOutputVerificationResult>.Fail(Error.Validation(
                "OfficialExternalOutputVerification.ExecutionRequired",
                "Official external output verification requires an execution result."));
        }

        if (files is null || files.Count == 0)
        {
            return Result<OfficialExternalOutputVerificationResult>.Fail(Error.Validation(
                "OfficialExternalOutputVerification.FileResultRequired",
                "Official external output verification requires file results."));
        }

        IReadOnlyList<OfficialExternalOutputVerificationFileResult> orderedFiles = files
            .Select(NormalizeFile)
            .OrderBy(static file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();
        int passed = orderedFiles.Count(static file => file.Passed);
        int failed = orderedFiles.Count - passed;
        OfficialExternalOutputVerificationStatus status = failed == 0
            ? OfficialExternalOutputVerificationStatus.Passed
            : OfficialExternalOutputVerificationStatus.Failed;
        OfficialExternalOutputVerificationTextRenderResult text = Render(
            executionResult,
            orderedFiles,
            status,
            verifiedAt);
        string hash = ComputeContentHash(text.Text);

        return Result<OfficialExternalOutputVerificationResult>.Success(
            new OfficialExternalOutputVerificationResult(
                status,
                executionResult.ExecutionReference,
                executionResult.ExecutionHash,
                executionResult.PackageReference,
                executionResult.PackageHash,
                executionResult.DispatchPlanReference,
                executionResult.DispatchPlanHash,
                executionResult.LedgerReference,
                executionResult.LedgerHash,
                orderedFiles,
                orderedFiles.Count,
                passed,
                failed,
                hash,
                VerificationReferencePrefix + hash,
                verifiedAt,
                text));
    }

    private static OfficialExternalOutputVerificationTextRenderResult Render(
        OfficialExternalOutputExecutionResult execution,
        IReadOnlyList<OfficialExternalOutputVerificationFileResult> files,
        OfficialExternalOutputVerificationStatus status,
        DateTimeOffset? verifiedAt)
    {
        List<string> lines = new()
        {
            "SALRYN OFFICIAL EXTERNAL OUTPUT VERIFICATION",
            "[VERIFICATION]",
            "VerificationStatus: " + status,
            "VerifiedAt: " + (verifiedAt.HasValue ? verifiedAt.Value.ToString("O", CultureInfo.InvariantCulture) : string.Empty),
            "ExecutionReference: " + execution.ExecutionReference,
            "ExecutionHash: " + execution.ExecutionHash,
            "PackageReference: " + execution.PackageReference,
            "PackageHash: " + execution.PackageHash,
            "DispatchPlanReference: " + execution.DispatchPlanReference,
            "DispatchPlanHash: " + execution.DispatchPlanHash,
            "LedgerReference: " + execution.LedgerReference,
            "LedgerHash: " + execution.LedgerHash,
            "CheckedFileCount: " + files.Count.ToString(CultureInfo.InvariantCulture),
            "PassedFileCount: " + files.Count(static file => file.Passed).ToString(CultureInfo.InvariantCulture),
            "FailedFileCount: " + files.Count(static file => !file.Passed).ToString(CultureInfo.InvariantCulture),
            "VerificationHash: COMPUTED_IN_VERIFICATION_RESULT",
            "VerificationReference: COMPUTED_IN_VERIFICATION_RESULT",
            "[FILES]"
        };

        foreach (OfficialExternalOutputVerificationFileResult file in files)
        {
            lines.Add("File: RelativePath=" + file.RelativePath +
                " | Role=" + file.Role +
                " | ExpectedHash=" + file.ExpectedHash +
                " | ActualHash=" + file.ActualHash +
                " | ExpectedLineCount=" + file.ExpectedLineCount.ToString(CultureInfo.InvariantCulture) +
                " | ActualLineCount=" + file.ActualLineCount.ToString(CultureInfo.InvariantCulture) +
                " | ExpectedByteCount=" + file.ExpectedByteCount.ToString(CultureInfo.InvariantCulture) +
                " | ActualByteCount=" + file.ActualByteCount.ToString(CultureInfo.InvariantCulture) +
                " | Passed=" + file.Passed +
                " | Message=" + file.Message);
        }

        lines.Add("[EVIDENCE]");
        lines.Add("HashAlgorithm: SHA-256");
        lines.Add("VerificationReferencePrefix: salryn://official-external-output-verification/");
        lines.Add("VerificationSideEffectMarker: EXTERNAL_OUTPUT_TEXT_FILE_VERIFICATION_READ_ONLY");

        return new OfficialExternalOutputVerificationTextRenderResult(lines, string.Join("\n", lines));
    }

    private static OfficialExternalOutputVerificationFileResult NormalizeFile(
        OfficialExternalOutputVerificationFileResult file)
    {
        return file with
        {
            RelativePath = NormalizePath(file.RelativePath),
            ExpectedHash = Normalize(file.ExpectedHash),
            ActualHash = Normalize(file.ActualHash),
            Message = Normalize(file.Message)
        };
    }

    public static string NormalizeText(string? text)
    {
        return text?.Replace("\r\n", "\n").Replace('\r', '\n').Trim() ?? string.Empty;
    }

    public static int CountLines(string text)
    {
        string normalized = NormalizeText(text);
        return normalized.Length == 0 ? 0 : normalized.Split('\n').Length;
    }

    public static string ComputeContentHash(string text)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeText(text)));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string NormalizePath(string? value)
    {
        return Normalize(value).Replace('\\', '/');
    }

    private static string Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
