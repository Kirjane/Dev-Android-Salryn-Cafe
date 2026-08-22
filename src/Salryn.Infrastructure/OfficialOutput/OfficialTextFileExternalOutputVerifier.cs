using System.Text;
using Salryn.Core.Common;
using Salryn.Core.Services;

namespace Salryn.Infrastructure.OfficialOutput;

public sealed class OfficialTextFileExternalOutputVerifier
{
    private readonly OfficialTextFileOutputPathPolicy _pathPolicy;
    private readonly OfficialExternalOutputVerificationResultBuilder _resultBuilder;

    public OfficialTextFileExternalOutputVerifier(
        OfficialTextFileOutputPathPolicy? pathPolicy = null,
        OfficialExternalOutputVerificationResultBuilder? resultBuilder = null)
    {
        _pathPolicy = pathPolicy ?? new OfficialTextFileOutputPathPolicy();
        _resultBuilder = resultBuilder ?? new OfficialExternalOutputVerificationResultBuilder();
    }

    public Result<OfficialExternalOutputVerificationResult> Verify(
        string? outputRootPath,
        OfficialExternalOutputExecutionResult? executionResult,
        DateTimeOffset? verifiedAt = null)
    {
        if (executionResult is null)
        {
            return Result<OfficialExternalOutputVerificationResult>.Fail(Error.Validation(
                "OfficialExternalOutputVerification.ExecutionRequired",
                "Official external output verification requires an execution result."));
        }

        Result<string> rootResult = _pathPolicy.ResolveOutputRoot(outputRootPath);

        if (rootResult.IsFailure)
        {
            return Result<OfficialExternalOutputVerificationResult>.Fail(rootResult.ErrorDetail);
        }

        if (executionResult.WrittenArtifacts is null || executionResult.WrittenArtifacts.Count == 0)
        {
            return Result<OfficialExternalOutputVerificationResult>.Fail(Error.Validation(
                "OfficialExternalOutputVerification.ArtifactRequired",
                "Official external output verification requires written artifacts."));
        }

        List<OfficialExternalOutputVerificationFileResult> fileResults = new();

        foreach (OfficialExternalOutputWrittenArtifact artifact in executionResult.WrittenArtifacts)
        {
            Result<OfficialExternalOutputVerificationFileResult> checkedFile = CheckOne(rootResult.Value, artifact);

            if (checkedFile.IsFailure)
            {
                return Result<OfficialExternalOutputVerificationResult>.Fail(checkedFile.ErrorDetail);
            }

            fileResults.Add(checkedFile.Value);
        }

        return _resultBuilder.Build(executionResult, fileResults, verifiedAt);
    }

    private Result<OfficialExternalOutputVerificationFileResult> CheckOne(
        string outputRootPath,
        OfficialExternalOutputWrittenArtifact artifact)
    {
        Result artifactValidation = OfficialExternalOutputExecutionValidator.ValidateWrittenArtifact(artifact);

        if (artifactValidation.IsFailure)
        {
            return Result<OfficialExternalOutputVerificationFileResult>.Fail(artifactValidation.ErrorDetail);
        }

        Result<string> pathResult = _pathPolicy.ResolveChild(outputRootPath, artifact.RelativePath);

        if (pathResult.IsFailure)
        {
            return Result<OfficialExternalOutputVerificationFileResult>.Fail(pathResult.ErrorDetail);
        }

        FileInfo file = new(pathResult.Value);

        if (!file.Exists)
        {
            return Result<OfficialExternalOutputVerificationFileResult>.Success(new OfficialExternalOutputVerificationFileResult(
                artifact.RelativePath,
                artifact.Role,
                artifact.ContentHash,
                string.Empty,
                artifact.LineCount,
                0,
                artifact.ByteCount,
                0,
                false,
                "Missing."));
        }

        string text = System.IO.File.ReadAllText(file.FullName, Encoding.UTF8);
        string normalized = OfficialExternalOutputVerificationResultBuilder.NormalizeText(text);
        string actualHash = OfficialExternalOutputVerificationResultBuilder.ComputeContentHash(normalized);
        int actualLineCount = OfficialExternalOutputVerificationResultBuilder.CountLines(normalized);
        long actualByteCount = Encoding.UTF8.GetByteCount(normalized);
        bool passed = string.Equals(actualHash, artifact.ContentHash, StringComparison.Ordinal) &&
            actualLineCount == artifact.LineCount &&
            actualByteCount == artifact.ByteCount;

        return Result<OfficialExternalOutputVerificationFileResult>.Success(new OfficialExternalOutputVerificationFileResult(
            artifact.RelativePath,
            artifact.Role,
            artifact.ContentHash,
            actualHash,
            artifact.LineCount,
            actualLineCount,
            artifact.ByteCount,
            actualByteCount,
            passed,
            passed ? "OK" : "Mismatch."));
    }
}
