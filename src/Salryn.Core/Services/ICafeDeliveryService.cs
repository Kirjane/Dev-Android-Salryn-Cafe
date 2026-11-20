using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Core.Services;

public interface ICafeDeliveryService
{
    Result<CafeDeliveryMutationResult> Create(CafeDeliveryCreateMutation mutation);

    Result<CafeDeliveryMutationResult> Revise(CafeDeliveryReviseMutation mutation);

    Result<CafeDeliveryMutationResult> Advance(CafeDeliveryAdvanceMutation mutation);

    Result<IReadOnlyList<CafeDeliverySummary>> List(
        Guid storeId,
        Guid actorUserId,
        string? status = null);

    Result<CafeDeliveryDetail?> Get(
        Guid storeId,
        Guid actorUserId,
        Guid deliveryId);

    Result<CafeDeliveryDetail?> GetByOrder(
        Guid storeId,
        Guid actorUserId,
        Guid orderId);
}
