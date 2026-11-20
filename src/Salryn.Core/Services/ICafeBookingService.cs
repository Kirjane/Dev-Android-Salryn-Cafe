using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Core.Services;

public interface ICafeBookingService
{
    Result<CafeBookingMutationResult> Create(CafeBookingCreateMutation mutation);
    Result<CafeBookingMutationResult> Revise(CafeBookingReviseMutation mutation);
    Result<CafeBookingMutationResult> AdvanceStatus(CafeBookingStatusMutation mutation);
    Result<CafeBookingMutationResult> Cancel(CafeBookingCancelMutation mutation);

    Result<CafeBookingOrderBindResult> BindOrder(
        CafeBookingOrderBindMutation mutation);

    Result<CafeBookingDepositMutationResult> ReceiveDeposit(
        CafeBookingDepositReceiveMutation mutation);

    Result<CafeBookingDepositMutationResult> RefundDeposit(
        CafeBookingDepositRefundMutation mutation);

    Result<CafeBookingDetail?> Get(
        Guid storeId,
        Guid actorUserId,
        Guid bookingId);

    Result<IReadOnlyList<CafeBookingSummary>> List(
        Guid storeId,
        Guid actorUserId,
        string? status = null,
        string? bookingMode = null,
        DateTimeOffset? eventStartUtc = null,
        DateTimeOffset? eventEndUtc = null,
        Guid? customerId = null);
}
