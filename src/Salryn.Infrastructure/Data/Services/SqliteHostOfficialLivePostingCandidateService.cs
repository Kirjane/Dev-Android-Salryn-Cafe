using Salryn.Core.Common;
using Salryn.Core.Official;
using Salryn.Core.Services;

namespace Salryn.Infrastructure.Data.Services;

/// <summary>
/// Host-local live posting candidate with read-only probes (setup/numbering/source sale).
/// Always blocked before mutation. Never mutates sequence, sales, or EJ.
/// </summary>
public sealed class SqliteHostOfficialLivePostingCandidateService : IHostOfficialLivePostingCandidateService
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly HostOfficialLivePostingCandidateService _planner = new();

    public SqliteHostOfficialLivePostingCandidateService(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<OfficialLivePostingCandidateResult>> EvaluateAsync(
        OfficialLivePostingCandidateRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        request ??= new OfficialLivePostingCandidateRequest(null, null, false);

        bool setupPresent = false;
        bool seriesActive = false;
        string? numberingHint = null;

        try
        {
            IHostOfficialSetupService setup = new SqliteHostOfficialSetupService(_connectionFactory);
            Result<OfficialSetupSnapshotContract> setupResult =
                await setup.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (setupResult.IsSuccess)
            {
                OfficialSetupSnapshotContract snap = setupResult.Value;
                setupPresent = snap.Business is not null && snap.Machine is not null && snap.Receipt is not null;
                seriesActive = snap.Series is not null && snap.Series.IsActive && !snap.Series.IsExhausted;
            }
        }
        catch
        {
            // Probe failure is non-mutating; planner records setup/series not verified.
        }

        try
        {
            IHostOfficialNumberingService numbering = new SqliteHostOfficialNumberingService(_connectionFactory);
            Result<OfficialNumberingPreviewContract> preview =
                await numbering.GetPreviewAsync(cancellationToken).ConfigureAwait(false);
            if (preview.IsSuccess)
            {
                numberingHint = preview.Value.FormattedNextNumber;
            }
        }
        catch
        {
            // Numbering probe failure is non-mutating.
        }

        if (request.OperationalSaleId is Guid saleId && saleId != Guid.Empty)
        {
            try
            {
                IHostOfficialSourceSaleReadOnlyAdapter adapter =
                    new SqliteHostOfficialSourceSaleReadOnlyAdapter(_connectionFactory);
                Result<OfficialSourceSaleReadOnlyResult> saleRead = await adapter.ReadAsync(
                    new OfficialSourceSaleReadOnlyRequest(saleId, request.DocumentKind),
                    cancellationToken).ConfigureAwait(false);
                if (saleRead.IsFailure)
                {
                    OfficialLivePostingCandidateResult blocked = HostOfficialLivePostingCandidateService.BuildBlockedPlan(
                        request, numberingHint, setupPresent, seriesActive);
                    List<OfficialLivePostingCandidateBlocker> blockers = blocked.Blockers.ToList();
                    blockers.Insert(0, new OfficialLivePostingCandidateBlocker(
                        "HostOfficialLivePostingCandidate.SourceSaleReadFailed",
                        saleRead.ErrorDetail.Message));
                    return Result<OfficialLivePostingCandidateResult>.Success(blocked with { Blockers = blockers });
                }
            }
            catch (Exception ex)
            {
                OfficialLivePostingCandidateResult blocked = HostOfficialLivePostingCandidateService.BuildBlockedPlan(
                    request, numberingHint, setupPresent, seriesActive);
                List<OfficialLivePostingCandidateBlocker> blockers = blocked.Blockers.ToList();
                blockers.Insert(0, new OfficialLivePostingCandidateBlocker(
                    "HostOfficialLivePostingCandidate.SourceSaleProbeFailed",
                    "Source-sale read-only probe failed: " + ex.Message));
                return Result<OfficialLivePostingCandidateResult>.Success(blocked with { Blockers = blockers });
            }
        }

        return await _planner.EvaluateEnrichedAsync(
            request,
            numberingHint,
            setupPresent,
            seriesActive,
            cancellationToken).ConfigureAwait(false);
    }

    public Task<Result<OfficialLivePostingCandidateStatusContract>> GetStatusAsync(
        CancellationToken cancellationToken = default) =>
        _planner.GetStatusAsync(cancellationToken);
}
