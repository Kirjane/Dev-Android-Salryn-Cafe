using System.Security;
using System.Text;
using Salryn.Core.Common;
using Salryn.Core.Services;

namespace Salryn.Infrastructure.OfficialOutput;

public sealed class OfficialTextFileExternalOutputAdapter : IOfficialExternalOutputExecutionAdapter
{
    private const string AdapterName = nameof(OfficialTextFileExternalOutputAdapter);

    private readonly OfficialTextFileOutputPathPolicy _pathPolicy;
    private readonly OfficialExternalOutputExecutionReceiptBuilder _receiptBuilder;

    public OfficialTextFileExternalOutputAdapter(
        OfficialTextFileOutputPathPolicy? pathPolicy = null,
        OfficialExternalOutputExecutionReceiptBuilder? receiptBuilder = null)
    {
        _pathPolicy = pathPolicy ?? new OfficialTextFileOutputPathPolicy();
        _receiptBuilder = receiptBuilder ?? new OfficialExternalOutputExecutionReceiptBuilder();
    }

    public Task<Result<OfficialExternalOutputExecutionResult>> ExecuteAsync(
        OfficialExternalOutputExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Result validation = OfficialExternalOutputExecutionValidator.ValidateExecutionRequest(request);

        if (validation.IsFailure)
        {
            return Task.FromResult(Result<OfficialExternalOutputExecutionResult>.Fail(validation.ErrorDetail));
        }

        Result<string> rootResult = _pathPolicy.ValidateWritableOutputRoot(request.OutputRootPath);

        if (rootResult.IsFailure)
        {
            return Task.FromResult(Result<OfficialExternalOutputExecutionResult>.Fail(rootResult.ErrorDetail));
        }

        OfficialOutputExportPackage package = request.ExportPackage!;
        Result<string> outputDirectoryResult = _pathPolicy.ResolveOfficialOutputDirectory(
            rootResult.Value,
            package.PackageHash);

        if (outputDirectoryResult.IsFailure)
        {
            return Task.FromResult(Result<OfficialExternalOutputExecutionResult>.Fail(outputDirectoryResult.ErrorDetail));
        }

        Result outputDirectoryCreate = TryCreateDirectory(outputDirectoryResult.Value);

        if (outputDirectoryCreate.IsFailure)
        {
            return Task.FromResult(Result<OfficialExternalOutputExecutionResult>.Fail(outputDirectoryCreate.ErrorDetail));
        }

        List<OfficialExternalOutputWrittenArtifact> artifacts = new();

        Result writeResult = WriteArtifact(
            rootResult.Value,
            OfficialExternalOutputWrittenArtifactRole.Manifest,
            _pathPolicy.BuildRelativePath(package.PackageHash, "manifest.txt"),
            package.ManifestPackage.TextResult.Text,
            package.ManifestPackage.ManifestReference,
            "OFFICIAL_OUTPUT_MANIFEST",
            artifacts);

        if (writeResult.IsFailure)
        {
            return Task.FromResult(Result<OfficialExternalOutputExecutionResult>.Fail(writeResult.ErrorDetail));
        }

        writeResult = WriteArtifact(
            rootResult.Value,
            OfficialExternalOutputWrittenArtifactRole.Package,
            _pathPolicy.BuildRelativePath(package.PackageHash, "package.txt"),
            package.TextResult.Text,
            package.PackageReference,
            "OFFICIAL_OUTPUT_EXPORT_PACKAGE",
            artifacts);

        if (writeResult.IsFailure)
        {
            return Task.FromResult(Result<OfficialExternalOutputExecutionResult>.Fail(writeResult.ErrorDetail));
        }

        writeResult = WriteArtifact(
            rootResult.Value,
            OfficialExternalOutputWrittenArtifactRole.Ledger,
            _pathPolicy.BuildRelativePath(package.PackageHash, "ledger.txt"),
            request.DeliveryLedgerPackage!.TextResult.Text,
            request.DeliveryLedgerPackage.LedgerReference,
            "OFFICIAL_OUTPUT_DELIVERY_LEDGER",
            artifacts);

        if (writeResult.IsFailure)
        {
            return Task.FromResult(Result<OfficialExternalOutputExecutionResult>.Fail(writeResult.ErrorDetail));
        }

        writeResult = WriteArtifact(
            rootResult.Value,
            OfficialExternalOutputWrittenArtifactRole.DispatchPlan,
            _pathPolicy.BuildRelativePath(package.PackageHash, "dispatch-plan.txt"),
            request.DispatchPlan!.TextResult.Text,
            request.DispatchPlan.DispatchPlanReference,
            "OFFICIAL_EXTERNAL_OUTPUT_DISPATCH_PLAN",
            artifacts);

        if (writeResult.IsFailure)
        {
            return Task.FromResult(Result<OfficialExternalOutputExecutionResult>.Fail(writeResult.ErrorDetail));
        }

        int payloadIndex = 1;
        foreach (OfficialOutputExportPackageItem item in package.Items.OrderBy(static item => item.ManifestEntry.ArtifactReference, StringComparer.Ordinal))
        {
            string fileName = _pathPolicy.BuildPayloadFileName(
                payloadIndex,
                item.Payload.ArtifactFamily.ToString(),
                item.Payload.ContentHash);
            writeResult = WriteArtifact(
                rootResult.Value,
                OfficialExternalOutputWrittenArtifactRole.Payload,
                _pathPolicy.BuildRelativePath(package.PackageHash, fileName),
                item.Payload.Text,
                item.Payload.ArtifactReference,
                item.Payload.ArtifactLabel,
                artifacts,
                new Dictionary<string, string> { ["artifactFamily"] = item.Payload.ArtifactFamily.ToString() });

            if (writeResult.IsFailure)
            {
                return Task.FromResult(Result<OfficialExternalOutputExecutionResult>.Fail(writeResult.ErrorDetail));
            }

            payloadIndex++;
        }

        Result<OfficialExternalOutputExecutionResult> executionResult = _receiptBuilder.Build(
            request,
            artifacts,
            AdapterName,
            request.RequestedAt);

        if (executionResult.IsFailure)
        {
            return Task.FromResult(executionResult);
        }

        string receiptRelativePath = _pathPolicy.BuildRelativePath(
            package.PackageHash,
            OfficialExternalOutputExecutionValidator.ExecutionReceiptRelativeFileName);
        string receiptText = executionResult.Value.TextResult.Text;
        Result receiptWrite = WriteText(rootResult.Value, receiptRelativePath, receiptText);

        if (receiptWrite.IsFailure)
        {
            return Task.FromResult(Result<OfficialExternalOutputExecutionResult>.Fail(receiptWrite.ErrorDetail));
        }

        OfficialExternalOutputWrittenArtifact receiptArtifact = OfficialExternalOutputExecutionValidator.BuildArtifact(
            OfficialExternalOutputWrittenArtifactRole.ExecutionReceipt,
            receiptRelativePath,
            receiptText,
            executionResult.Value.ExecutionReference,
            "OFFICIAL_EXTERNAL_OUTPUT_EXECUTION_RECEIPT");

        OfficialExternalOutputExecutionResult finalResult = executionResult.Value with
        {
            WrittenArtifacts = executionResult.Value.WrittenArtifacts.Concat(new[] { receiptArtifact }).ToArray()
        };

        return Task.FromResult(Result<OfficialExternalOutputExecutionResult>.Success(finalResult));
    }

    private Result WriteArtifact(
        string outputRootPath,
        OfficialExternalOutputWrittenArtifactRole role,
        string relativePath,
        string content,
        string? artifactReference,
        string? artifactLabel,
        List<OfficialExternalOutputWrittenArtifact> artifacts,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        Result writeResult = WriteText(outputRootPath, relativePath, content);

        if (writeResult.IsFailure)
        {
            return writeResult;
        }

        artifacts.Add(OfficialExternalOutputExecutionValidator.BuildArtifact(
            role,
            relativePath,
            content,
            artifactReference,
            artifactLabel,
            metadata));

        return Result.Success();
    }

    private Result WriteText(
        string outputRootPath,
        string relativePath,
        string content)
    {
        Result<string> pathResult = _pathPolicy.ResolveChild(outputRootPath, relativePath);

        if (pathResult.IsFailure)
        {
            return Result.Fail(pathResult.ErrorDetail);
        }

        string? directory = Path.GetDirectoryName(pathResult.Value);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Result directoryCreate = TryCreateDirectory(directory);

            if (directoryCreate.IsFailure)
            {
                return directoryCreate;
            }
        }

        try
        {
            File.WriteAllText(
                pathResult.Value,
                OfficialExternalOutputExecutionValidator.NormalizeText(content),
                Encoding.UTF8);

            return Result.Success();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException or PathTooLongException or SecurityException or ArgumentException or NotSupportedException)
        {
            return OfficialTextFileOutputPathPolicy.MapWriteException(ex);
        }
    }

    private static Result TryCreateDirectory(string directoryPath)
    {
        try
        {
            Directory.CreateDirectory(directoryPath);
            return Result.Success();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException or PathTooLongException or SecurityException or ArgumentException or NotSupportedException)
        {
            return OfficialTextFileOutputPathPolicy.MapWriteException(ex);
        }
    }
}
