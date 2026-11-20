using Salryn.Core.Services;
using Salryn.Sync.Contracts;

namespace Salryn.Infrastructure.Data.Sync;

public sealed record LocalSaleCommittedV1CommitRequest(
    Guid OperationId,
    Guid DeviceId,
    Guid StoreId,
    CompleteCheckoutSaleRequest SalePackage,
    DateTimeOffset OccurredAtLocal);

public sealed record LocalSaleCommittedV1CommitResult(
    Guid SaleId,
    string ReceiptNumber,
    SyncOperationEnvelopeV1 Envelope,
    bool IsReplay,
    SyncOperationEnvelopeV1? IngredientConsumptionEnvelope = null);
