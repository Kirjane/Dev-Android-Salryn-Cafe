using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Repositories;
using Salryn.Core.Services;

namespace Salryn.App.WinForms.Features.OfficialOutput;

public sealed class OfficialOutputTextFileWorkflowAuditRecorder : IOfficialOutputTextFileWorkflowAuditRecorder
{
    private readonly IAuditWriteRepository _auditWriteRepository;

    public OfficialOutputTextFileWorkflowAuditRecorder(IAuditWriteRepository auditWriteRepository)
    {
        _auditWriteRepository = auditWriteRepository ?? throw new ArgumentNullException(nameof(auditWriteRepository));
    }

    public Task<Result> RecordRunAttemptAsync(
        OfficialOutputTextFileWorkflowAuditRecordRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string action = OfficialOutputTextFileWorkflowAuditActions.ResolveAction(
            request.Succeeded,
            request.ReadinessBlocked,
            request.ErrorCode);

        Result<string> newValueJson = OfficialOutputTextFileWorkflowAuditDetailBuilder.BuildNewValueJson(
            request,
            action);

        if (newValueJson.IsFailure)
        {
            return Task.FromResult(Result.Fail(newValueJson.ErrorDetail));
        }

        AuditLog auditLog = new()
        {
            UserId = request.UserId,
            Action = action,
            EntityType = OfficialOutputTextFileWorkflowAuditActions.EntityType,
            EntityId = OfficialOutputTextFileWorkflowAuditDetailBuilder.ResolveEntityId(
                request.PackageHash,
                request.Metadata),
            Reason = OfficialOutputTextFileWorkflowAuditDetailBuilder.ResolveReason(
                action,
                request.ErrorCode),
            NewValueJson = newValueJson.Value,
            CreatedAt = request.RecordedAt ?? DateTimeOffset.UtcNow
        };

        return _auditWriteRepository.AddAsync(auditLog, cancellationToken);
    }
}
