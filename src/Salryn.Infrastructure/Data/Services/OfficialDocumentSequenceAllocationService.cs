using Salryn.Core.Common;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Data.Transactions;

namespace Salryn.Infrastructure.Data.Services;

public sealed record OfficialDocumentSequenceAllocationRequest(
    Guid BirMachineProfileId,
    string DocumentKind,
    string SequenceScopeCode,
    int Year);

public sealed record OfficialDocumentSequenceAllocation(
    Guid SequenceId,
    Guid BirMachineProfileId,
    string DocumentKind,
    string SequenceScopeCode,
    string Prefix,
    int Year,
    long Number,
    int PaddingDigits,
    string FormattedDocumentNumber);

public interface IOfficialDocumentSequenceAllocationService
{
    Task<Result<OfficialDocumentSequenceAllocation>> AllocateNextAsync(
        OfficialDocumentSequenceAllocationRequest request,
        CancellationToken cancellationToken = default);
}

// Kirch strict dev guide:
// J21-3B owns official sequence allocation only.
// It advances bir_official_document_sequences.current_number transactionally.
// It does not create sales, print receipts, render receipt/invoice text, write snapshots,
// wire checkout, resume multi-tenant, or claim accreditation readiness.
// Kirch: why - J21-5Q keeps this public service while moving allocation work behind the shared transaction boundary.
public sealed class OfficialDocumentSequenceAllocationService : IOfficialDocumentSequenceAllocationService
{
    private readonly ISqliteSharedTransactionBoundary _transactionBoundary;
    private readonly SharedSqliteOfficialDocumentSequenceAllocationParticipant _participant;

    public OfficialDocumentSequenceAllocationService(SqliteConnectionFactory connectionFactory)
    {
        if (connectionFactory is null)
        {
            throw new ArgumentNullException(nameof(connectionFactory));
        }

        _transactionBoundary = new SqliteSharedTransactionBoundary(connectionFactory);
        _participant = new SharedSqliteOfficialDocumentSequenceAllocationParticipant();
    }

    public Task<Result<OfficialDocumentSequenceAllocation>> AllocateNextAsync(
        OfficialDocumentSequenceAllocationRequest request,
        CancellationToken cancellationToken = default)
    {
        return _transactionBoundary.ExecuteAsync<OfficialDocumentSequenceAllocation>(
            (context, token) => _participant.ExecuteAsync(context, request, token),
            cancellationToken);
    }
}
