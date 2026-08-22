using Salryn.App.WinForms.Composition;
using Salryn.Core.Common;
using Salryn.Core.Services;

namespace Salryn.App.WinForms.Features.OfficialOutput;

public sealed record OfficialOutputTextFilePackageSupplySummary(
    bool HasTrustedPackage,
    string StatusCode,
    string StatusMessage,
    string? PackageReference,
    string? PackageHash,
    OfficialOutputExportPackageKind? PackageKind,
    int? EntryCount,
    int? PayloadCount,
    int? TotalLineCount)
{
    public static OfficialOutputTextFilePackageSupplySummary Unavailable(
        string code,
        string message)
    {
        return new OfficialOutputTextFilePackageSupplySummary(
            HasTrustedPackage: false,
            StatusCode: code,
            StatusMessage: message,
            PackageReference: null,
            PackageHash: null,
            PackageKind: null,
            EntryCount: null,
            PayloadCount: null,
            TotalLineCount: null);
    }

    public static OfficialOutputTextFilePackageSupplySummary Available(
        OfficialOutputExportPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        return new OfficialOutputTextFilePackageSupplySummary(
            HasTrustedPackage: true,
            StatusCode: "OfficialOutputTextFilePackageSupply.Available",
            StatusMessage: "Trusted official output text-file export package is available.",
            PackageReference: package.PackageReference,
            PackageHash: package.PackageHash,
            PackageKind: package.PackageKind,
            EntryCount: package.EntryCount,
            PayloadCount: package.PayloadCount,
            TotalLineCount: package.TotalLineCount);
    }
}

public interface IOfficialOutputTextFilePackageSupplyBoundary
{
    OfficialOutputTextFilePackageSupplySummary GetAvailablePackageSummary();

    OfficialOutputTextFilePackageSupplySummary GetAvailablePackageSummary(
        OfficialOutputTextFileSourceSelectionRequest? selectedSourceRequest);

    Result<OfficialOutputExportPackage> TryGetPackage();

    Result<OfficialOutputExportPackage> TryGetPackage(
        OfficialOutputTextFileSourceSelectionRequest? selectedSourceRequest);

    Task<OfficialOutputTextFilePackageSupplySummary> GetAvailablePackageSummaryAsync(
        CancellationToken cancellationToken = default);

    Task<OfficialOutputTextFilePackageSupplySummary> GetAvailablePackageSummaryAsync(
        OfficialOutputTextFileSourceSelectionRequest? selectedSourceRequest,
        CancellationToken cancellationToken = default);

    Task<Result<OfficialOutputExportPackage>> TryGetPackageAsync(
        CancellationToken cancellationToken = default);

    Task<Result<OfficialOutputExportPackage>> TryGetPackageAsync(
        OfficialOutputTextFileSourceSelectionRequest? selectedSourceRequest,
        CancellationToken cancellationToken = default);
}

public sealed class OfficialOutputTextFilePackageSupplyBoundary : IOfficialOutputTextFilePackageSupplyBoundary
{
    private readonly OfficialOutputExportPackage? _trustedPackage;
    private readonly IOfficialOutputTextFileTrustedPackageProducer? _trustedPackageProducer;
    private readonly OfficialOutputTextFileTrustedPackageProducerRequest? _producerRequest;
    private readonly IOfficialOutputTextFileSourceSelectionBoundary? _sourceSelectionBoundary;

    public OfficialOutputTextFilePackageSupplyBoundary(
        OfficialOutputExportPackage? trustedPackage = null,
        IOfficialOutputTextFileTrustedPackageProducer? trustedPackageProducer = null,
        OfficialOutputTextFileTrustedPackageProducerRequest? producerRequest = null,
        IOfficialOutputTextFileSourceSelectionBoundary? sourceSelectionBoundary = null)
    {
        _trustedPackage = trustedPackage;
        _trustedPackageProducer = trustedPackageProducer;
        _producerRequest = producerRequest;
        _sourceSelectionBoundary = sourceSelectionBoundary;
    }

    public static OfficialOutputTextFilePackageSupplyBoundary FromTrustedPackage(
        OfficialOutputExportPackage trustedPackage)
    {
        ArgumentNullException.ThrowIfNull(trustedPackage);

        return new OfficialOutputTextFilePackageSupplyBoundary(trustedPackage);
    }

    public static OfficialOutputTextFilePackageSupplyBoundary FromTrustedPackageProducer(
        IOfficialOutputTextFileTrustedPackageProducer trustedPackageProducer,
        OfficialOutputTextFileTrustedPackageProducerRequest producerRequest)
    {
        ArgumentNullException.ThrowIfNull(trustedPackageProducer);
        ArgumentNullException.ThrowIfNull(producerRequest);

        return new OfficialOutputTextFilePackageSupplyBoundary(
            trustedPackage: null,
            trustedPackageProducer,
            producerRequest);
    }

    public static OfficialOutputTextFilePackageSupplyBoundary FromTrustedPackageProducer(
        IOfficialOutputTextFileTrustedPackageProducer trustedPackageProducer,
        IOfficialOutputTextFileSourceSelectionBoundary sourceSelectionBoundary)
    {
        ArgumentNullException.ThrowIfNull(trustedPackageProducer);
        ArgumentNullException.ThrowIfNull(sourceSelectionBoundary);

        return new OfficialOutputTextFilePackageSupplyBoundary(
            trustedPackage: null,
            trustedPackageProducer,
            producerRequest: null,
            sourceSelectionBoundary);
    }

    public OfficialOutputTextFilePackageSupplySummary GetAvailablePackageSummary()
    {
        return GetAvailablePackageSummary(selectedSourceRequest: null);
    }

    public OfficialOutputTextFilePackageSupplySummary GetAvailablePackageSummary(
        OfficialOutputTextFileSourceSelectionRequest? selectedSourceRequest)
    {
        Result<OfficialOutputExportPackage> result = TryGetPackage(selectedSourceRequest);

        return result.IsSuccess
            ? OfficialOutputTextFilePackageSupplySummary.Available(result.Value)
            : OfficialOutputTextFilePackageSupplySummary.Unavailable(
                result.ErrorDetail.Code,
                result.ErrorDetail.Message);
    }

    public Result<OfficialOutputExportPackage> TryGetPackage()
    {
        return TryGetPackage(selectedSourceRequest: null);
    }

    public Result<OfficialOutputExportPackage> TryGetPackage(
        OfficialOutputTextFileSourceSelectionRequest? selectedSourceRequest)
    {
        OfficialOutputExportPackage? package = _trustedPackage;

        if (package is not null)
        {
            return ValidateTrustedPackage(package, "Supplied");
        }

        if (selectedSourceRequest is not null)
        {
            return Result<OfficialOutputExportPackage>.Fail(Error.Validation(
                "OfficialOutputTextFilePackageSupply.AsyncProductionRequired",
                "Selected official output source requires asynchronous trusted package production."));
        }

        return Result<OfficialOutputExportPackage>.Fail(Error.Validation(
            "OfficialOutputTextFilePackageSupply.NoTrustedPackage",
            "No trusted official output text-file export package is currently supplied."));
    }

    public async Task<OfficialOutputTextFilePackageSupplySummary> GetAvailablePackageSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        return await GetAvailablePackageSummaryAsync(
                selectedSourceRequest: null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OfficialOutputTextFilePackageSupplySummary> GetAvailablePackageSummaryAsync(
        OfficialOutputTextFileSourceSelectionRequest? selectedSourceRequest,
        CancellationToken cancellationToken = default)
    {
        Result<OfficialOutputExportPackage> result =
            await TryGetPackageAsync(selectedSourceRequest, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? OfficialOutputTextFilePackageSupplySummary.Available(result.Value)
            : OfficialOutputTextFilePackageSupplySummary.Unavailable(
                result.ErrorDetail.Code,
                result.ErrorDetail.Message);
    }

    public async Task<Result<OfficialOutputExportPackage>> TryGetPackageAsync(
        CancellationToken cancellationToken = default)
    {
        return await TryGetPackageAsync(
                selectedSourceRequest: null,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<OfficialOutputExportPackage>> TryGetPackageAsync(
        OfficialOutputTextFileSourceSelectionRequest? selectedSourceRequest,
        CancellationToken cancellationToken = default)
    {
        OfficialOutputExportPackage? package = _trustedPackage;

        if (package is not null)
        {
            return ValidateTrustedPackage(package, "Supplied");
        }

        IOfficialOutputTextFileTrustedPackageProducer? producer = _trustedPackageProducer;

        if (producer is null)
        {
            return Result<OfficialOutputExportPackage>.Fail(Error.Validation(
                "OfficialOutputTextFilePackageSupply.NoTrustedPackage",
                "No trusted official output text-file export package is currently supplied."));
        }

        Result<OfficialOutputTextFileTrustedPackageProducerRequest> producerRequest =
            TryBuildProducerRequest(selectedSourceRequest);

        if (producerRequest.IsFailure)
        {
            return Result<OfficialOutputExportPackage>.Fail(producerRequest.ErrorDetail);
        }

        Result<OfficialOutputExportPackage> producedPackage =
            await producer.ProduceAsync(producerRequest.Value, cancellationToken).ConfigureAwait(false);

        if (producedPackage.IsFailure)
        {
            return producedPackage;
        }

        return ValidateTrustedPackage(producedPackage.Value, "Produced");
    }

    private Result<OfficialOutputTextFileTrustedPackageProducerRequest> TryBuildProducerRequest(
        OfficialOutputTextFileSourceSelectionRequest? selectedSourceRequest)
    {
        if (selectedSourceRequest is null)
        {
            OfficialOutputTextFileTrustedPackageProducerRequest? producerRequest = _producerRequest;

            if (producerRequest is null)
            {
                return Result<OfficialOutputTextFileTrustedPackageProducerRequest>.Fail(Error.Validation(
                    "OfficialOutputTextFilePackageSupply.ProducerRequestRequired",
                    "Official output text-file package supply requires an official output source request before producing a package."));
            }

            return Result<OfficialOutputTextFileTrustedPackageProducerRequest>.Success(producerRequest);
        }

        IOfficialOutputTextFileSourceSelectionBoundary sourceSelectionBoundary =
            _sourceSelectionBoundary ?? new OfficialOutputTextFileSourceSelectionBoundary();

        OfficialOutputTextFileSourceSelectionResult selectedSource =
            sourceSelectionBoundary.SelectSource(selectedSourceRequest);

        if (!selectedSource.Succeeded || selectedSource.ProducerRequest is null)
        {
            return Result<OfficialOutputTextFileTrustedPackageProducerRequest>.Fail(Error.Validation(
                selectedSource.ErrorCode ?? "OfficialOutputTextFilePackageSupply.SourceSelectionInvalid",
                selectedSource.ErrorMessage ?? selectedSource.StatusMessage));
        }

        return Result<OfficialOutputTextFileTrustedPackageProducerRequest>.Success(
            selectedSource.ProducerRequest);
    }

    private static Result<OfficialOutputExportPackage> ValidateTrustedPackage(
        OfficialOutputExportPackage package,
        string sourceLabel)
    {
        if (!AppOfficialOutputTextFileWorkflowReadinessRules.IsTrustedExportPackage(package))
        {
            return Result<OfficialOutputExportPackage>.Fail(Error.Validation(
                "OfficialOutputTextFilePackageSupply.PackageNotTrusted",
                sourceLabel + " official output text-file export package identity is not trusted."));
        }

        return Result<OfficialOutputExportPackage>.Success(package);
    }
}
