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

    Result<OfficialOutputExportPackage> TryGetPackage();

    Task<OfficialOutputTextFilePackageSupplySummary> GetAvailablePackageSummaryAsync(
        CancellationToken cancellationToken = default);

    Task<Result<OfficialOutputExportPackage>> TryGetPackageAsync(
        CancellationToken cancellationToken = default);
}

public sealed class OfficialOutputTextFilePackageSupplyBoundary : IOfficialOutputTextFilePackageSupplyBoundary
{
    private readonly OfficialOutputExportPackage? _trustedPackage;
    private readonly IOfficialOutputTextFileTrustedPackageProducer? _trustedPackageProducer;
    private readonly OfficialOutputTextFileTrustedPackageProducerRequest? _producerRequest;

    public OfficialOutputTextFilePackageSupplyBoundary(
        OfficialOutputExportPackage? trustedPackage = null,
        IOfficialOutputTextFileTrustedPackageProducer? trustedPackageProducer = null,
        OfficialOutputTextFileTrustedPackageProducerRequest? producerRequest = null)
    {
        _trustedPackage = trustedPackage;
        _trustedPackageProducer = trustedPackageProducer;
        _producerRequest = producerRequest;
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

    public OfficialOutputTextFilePackageSupplySummary GetAvailablePackageSummary()
    {
        Result<OfficialOutputExportPackage> result = TryGetPackage();

        return result.IsSuccess
            ? OfficialOutputTextFilePackageSupplySummary.Available(result.Value)
            : OfficialOutputTextFilePackageSupplySummary.Unavailable(
                result.ErrorDetail.Code,
                result.ErrorDetail.Message);
    }

    public Result<OfficialOutputExportPackage> TryGetPackage()
    {
        OfficialOutputExportPackage? package = _trustedPackage;

        if (package is null)
        {
            return Result<OfficialOutputExportPackage>.Fail(Error.Validation(
                "OfficialOutputTextFilePackageSupply.NoTrustedPackage",
                "No trusted official output text-file export package is currently supplied."));
        }

        if (!AppOfficialOutputTextFileWorkflowReadinessRules.IsTrustedExportPackage(package))
        {
            return Result<OfficialOutputExportPackage>.Fail(Error.Validation(
                "OfficialOutputTextFilePackageSupply.PackageNotTrusted",
                "Supplied official output text-file export package identity is not trusted."));
        }

        return Result<OfficialOutputExportPackage>.Success(package);
    }

    public async Task<OfficialOutputTextFilePackageSupplySummary> GetAvailablePackageSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        Result<OfficialOutputExportPackage> result =
            await TryGetPackageAsync(cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? OfficialOutputTextFilePackageSupplySummary.Available(result.Value)
            : OfficialOutputTextFilePackageSupplySummary.Unavailable(
                result.ErrorDetail.Code,
                result.ErrorDetail.Message);
    }

    public async Task<Result<OfficialOutputExportPackage>> TryGetPackageAsync(
        CancellationToken cancellationToken = default)
    {
        OfficialOutputExportPackage? package = _trustedPackage;

        if (package is not null)
        {
            return TryGetPackage();
        }

        IOfficialOutputTextFileTrustedPackageProducer? producer = _trustedPackageProducer;

        if (producer is null)
        {
            return Result<OfficialOutputExportPackage>.Fail(Error.Validation(
                "OfficialOutputTextFilePackageSupply.NoTrustedPackage",
                "No trusted official output text-file export package is currently supplied."));
        }

        OfficialOutputTextFileTrustedPackageProducerRequest? producerRequest = _producerRequest;

        if (producerRequest is null)
        {
            return Result<OfficialOutputExportPackage>.Fail(Error.Validation(
                "OfficialOutputTextFilePackageSupply.ProducerRequestRequired",
                "Official output text-file package supply requires an official output source request before producing a package."));
        }

        Result<OfficialOutputExportPackage> producedPackage =
            await producer.ProduceAsync(producerRequest, cancellationToken).ConfigureAwait(false);

        if (producedPackage.IsFailure)
        {
            return producedPackage;
        }

        if (!AppOfficialOutputTextFileWorkflowReadinessRules.IsTrustedExportPackage(producedPackage.Value))
        {
            return Result<OfficialOutputExportPackage>.Fail(Error.Validation(
                "OfficialOutputTextFilePackageSupply.PackageNotTrusted",
                "Produced official output text-file export package identity is not trusted."));
        }

        return producedPackage;
    }
}
