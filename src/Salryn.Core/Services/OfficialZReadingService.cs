using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Core.Services;

public sealed record OfficialZReadingRequest(
    DateTimeOffset RangeStartedAt,
    DateTimeOffset RangeEndedAt,
    Guid? BirMachineProfileId,
    string? RegistrationScopeCode,
    ElectronicJournalDocumentKind DocumentKind,
    Guid? CreatedByUserId,
    string? TerminalId);

public sealed record OfficialZReadingSummary(
    Guid ZReadingId,
    DateTimeOffset RangeStartedAt,
    DateTimeOffset RangeEndedAt,
    DateTimeOffset FinalizedAt,
    Guid? BirMachineProfileId,
    string? RegistrationScopeCode,
    ElectronicJournalDocumentKind DocumentKind,
    int EntryCount,
    string? FirstOfficialDocumentNumber,
    string? LastOfficialDocumentNumber,
    long GrossSalesCents,
    long SubtotalCents,
    long DiscountTotalCents,
    long TaxTotalCents,
    long NetSalesCents,
    long TotalSalesCents,
    string EvidenceManifestHash,
    bool AllSnapshotHashesMatch,
    Guid? CreatedByUserId,
    string? TerminalId);

public sealed record OfficialZReadingResult(
    OfficialZReadingSummary Summary,
    IReadOnlyList<OfficialXReadingPaymentSummary> Payments,
    IReadOnlyList<OfficialXReadingTaxModeSummary> TaxModes);

public sealed record OfficialZReadingPersistenceRequest(
    OfficialZReadingSummary Summary,
    IReadOnlyList<OfficialXReadingPaymentSummary> Payments,
    IReadOnlyList<OfficialXReadingTaxModeSummary> TaxModes);

public sealed record OfficialZReadingPersistenceResult(Guid ZReadingId);

public interface IOfficialZReadingService
{
    Task<Result<OfficialZReadingResult>> FinalizeAsync(
        OfficialZReadingRequest request,
        CancellationToken cancellationToken = default);
}

public interface IOfficialZReadingPersistence
{
    Task<Result<OfficialZReadingPersistenceResult>> SaveAsync(
        OfficialZReadingPersistenceRequest request,
        CancellationToken cancellationToken = default);
}

// Kirch: why - Z-reading finalizes a read-only X-reading snapshot; it must not rewrite sales, stock, sequence, or journal evidence.
public sealed class OfficialZReadingService : IOfficialZReadingService
{
    private readonly IOfficialXReadingService _xReadingService;
    private readonly IOfficialZReadingPersistence _persistence;
    private readonly Func<Guid> _idFactory;
    private readonly Func<DateTimeOffset> _clock;

    public OfficialZReadingService(
        IOfficialXReadingService xReadingService,
        IOfficialZReadingPersistence persistence,
        Func<Guid>? idFactory = null,
        Func<DateTimeOffset>? clock = null)
    {
        _xReadingService = xReadingService ?? throw new ArgumentNullException(nameof(xReadingService));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _idFactory = idFactory ?? Guid.NewGuid;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<Result<OfficialZReadingResult>> FinalizeAsync(
        OfficialZReadingRequest request,
        CancellationToken cancellationToken = default)
    {
        Result validation = ValidateRequest(request);

        if (validation.IsFailure)
        {
            return Result<OfficialZReadingResult>.Fail(validation.ErrorDetail);
        }

        OfficialXReadingRequest xReadingRequest = new(
            request.RangeStartedAt,
            request.RangeEndedAt,
            request.BirMachineProfileId,
            request.RegistrationScopeCode,
            request.DocumentKind);

        Result<OfficialXReadingResult> xReadingResult =
            await _xReadingService.ReadAsync(xReadingRequest, cancellationToken);

        if (xReadingResult.IsFailure)
        {
            return Result<OfficialZReadingResult>.Fail(MapXReadingFailure(xReadingResult.ErrorDetail));
        }

        OfficialXReadingResult xReading = xReadingResult.Value;

        if (!xReading.Summary.AllSnapshotHashesMatch)
        {
            return Result<OfficialZReadingResult>.Fail(Error.Failure(
                "OfficialZReading.EvidenceHashMismatch",
                "Z-reading is blocked because official evidence snapshot hash verification failed."));
        }

        OfficialZReadingSummary summary = new(
            ZReadingId: _idFactory(),
            RangeStartedAt: xReading.Summary.RangeStartedAt,
            RangeEndedAt: xReading.Summary.RangeEndedAt,
            FinalizedAt: _clock(),
            BirMachineProfileId: xReading.Summary.BirMachineProfileId,
            RegistrationScopeCode: NormalizeOptional(xReading.Summary.RegistrationScopeCode),
            DocumentKind: xReading.Summary.DocumentKind,
            EntryCount: xReading.Summary.EntryCount,
            FirstOfficialDocumentNumber: xReading.DocumentRange?.FirstOfficialDocumentNumber,
            LastOfficialDocumentNumber: xReading.DocumentRange?.LastOfficialDocumentNumber,
            GrossSalesCents: xReading.Summary.GrossSalesCents,
            SubtotalCents: xReading.Summary.SubtotalCents,
            DiscountTotalCents: xReading.Summary.DiscountTotalCents,
            TaxTotalCents: xReading.Summary.TaxTotalCents,
            NetSalesCents: xReading.Summary.NetSalesCents,
            TotalSalesCents: xReading.Summary.TotalSalesCents,
            EvidenceManifestHash: xReading.Summary.EvidenceManifestHash,
            AllSnapshotHashesMatch: xReading.Summary.AllSnapshotHashesMatch,
            CreatedByUserId: request.CreatedByUserId,
            TerminalId: NormalizeOptional(request.TerminalId));

        OfficialZReadingPersistenceRequest persistenceRequest = new(
            summary,
            xReading.Payments,
            xReading.TaxModes);

        Result<OfficialZReadingPersistenceResult> persistenceResult =
            await _persistence.SaveAsync(persistenceRequest, cancellationToken);

        if (persistenceResult.IsFailure)
        {
            return Result<OfficialZReadingResult>.Fail(persistenceResult.ErrorDetail);
        }

        return Result<OfficialZReadingResult>.Success(
            new OfficialZReadingResult(summary, xReading.Payments, xReading.TaxModes));
    }

    public static Result ValidateRequest(OfficialZReadingRequest? request)
    {
        if (request is null)
        {
            return Result.Fail(Error.Validation(
                "OfficialZReading.RequestRequired",
                "Official Z-reading request is required."));
        }

        if (request.RangeStartedAt == default ||
            request.RangeEndedAt == default ||
            request.RangeEndedAt <= request.RangeStartedAt)
        {
            return Result.Fail(Error.Validation(
                "OfficialZReading.InvalidDateRange",
                "Official Z-reading date range is invalid."));
        }

        if (!request.BirMachineProfileId.HasValue &&
            string.IsNullOrWhiteSpace(request.RegistrationScopeCode))
        {
            return Result.Fail(Error.Validation(
                "OfficialZReading.MachineProfileOrScopeRequired",
                "Official Z-reading requires a BIR machine profile id or registration scope."));
        }

        if (request.BirMachineProfileId.HasValue &&
            request.BirMachineProfileId.Value == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "OfficialZReading.InvalidMachineProfileId",
                "Official Z-reading machine profile id is invalid."));
        }

        if (!Enum.IsDefined(typeof(ElectronicJournalDocumentKind), request.DocumentKind))
        {
            return Result.Fail(Error.Validation(
                "OfficialZReading.UnsupportedDocumentKind",
                "Official Z-reading document kind is unsupported."));
        }

        return Result.Success();
    }

    private static Error MapXReadingFailure(Error error)
    {
        if (string.Equals(error.Code, "OfficialXReading.EvidenceHashMismatch", StringComparison.Ordinal))
        {
            return Error.Failure(
                "OfficialZReading.EvidenceHashMismatch",
                "Z-reading is blocked because official evidence snapshot hash verification failed.");
        }

        return Error.Failure(
            "OfficialZReading.XReadingFailed",
            error.Message);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}
