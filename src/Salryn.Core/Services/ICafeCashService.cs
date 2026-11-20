using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Core.Services;

public interface ICafeCashService
{
    Result<CafeCashMutationResult> Post(CafeCashPostMutation mutation);

    Result<CafeCashMutationResult> Reverse(CafeCashReversalMutation mutation);

    Result<IReadOnlyList<CafeCashMovementSnapshot>> GetSessionMovements(
        Guid storeId,
        Guid actorUserId,
        Guid cashierSessionId);
}
