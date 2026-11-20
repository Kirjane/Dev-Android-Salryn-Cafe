using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Core.Services;

public interface ICafeRefundService
{
    Result<CafeRefundMutationResult> Request(CafeRefundRequestMutation mutation);

    Result<CafeRefundMutationResult> Resolve(CafeRefundResolutionMutation mutation);

    Result<CafeRefundCommitResult> Commit(CafeRefundCommitMutation mutation);

    Result<CafeRefundDetail> Get(Guid refundId);
}
