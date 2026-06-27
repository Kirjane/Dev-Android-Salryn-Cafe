using Salryn.Core.Common;

namespace Salryn.Core.Services;

public interface IOfficialOutputTextFileWorkflowExecutionPort
{
    Task<Result<OfficialExternalOutputWorkflowResult>> RunAsync(
        OfficialExternalOutputWorkflowRequest request,
        CancellationToken cancellationToken = default);
}
