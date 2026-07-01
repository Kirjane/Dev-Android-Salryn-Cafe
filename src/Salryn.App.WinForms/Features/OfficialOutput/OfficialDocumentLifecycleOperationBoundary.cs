using Salryn.Core.Common;
using Salryn.Core.Security;
using Salryn.Core.Services;

namespace Salryn.App.WinForms.Features.OfficialOutput;

public sealed record OfficialDocumentLifecycleOperationRequest(
    OfficialDocumentAuditEventRequest AuditRequest,
    Func<IReadOnlyCollection<string>> PermissionProvider);

public interface IOfficialDocumentLifecycleOperationBoundary
{
    Task<Result<OfficialDocumentAuditEventResult>> AppendLifecycleEventAsync(
        OfficialDocumentLifecycleOperationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class OfficialDocumentLifecycleOperationBoundary : IOfficialDocumentLifecycleOperationBoundary
{
    private readonly IOfficialDocumentAuditEventService _auditEvents;

    public OfficialDocumentLifecycleOperationBoundary(IOfficialDocumentAuditEventService auditEvents)
    {
        _auditEvents = auditEvents ?? throw new ArgumentNullException(nameof(auditEvents));
    }

    public Task<Result<OfficialDocumentAuditEventResult>> AppendLifecycleEventAsync(
        OfficialDocumentLifecycleOperationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return Task.FromResult(Result<OfficialDocumentAuditEventResult>.Fail(Error.Validation(
                "OfficialDocumentLifecycleOperation.RequestRequired",
                "Official document lifecycle operation request is required.")));
        }

        if (request.AuditRequest is null)
        {
            return Task.FromResult(Result<OfficialDocumentAuditEventResult>.Fail(Error.Validation(
                "OfficialDocumentLifecycleOperation.AuditRequestRequired",
                "Official document lifecycle operation audit request is required.")));
        }

        OfficialDocumentAuditEventKind submittedEventKind = request.AuditRequest.EventKind;
        string? requiredPermission = ResolvePermission(submittedEventKind);

        if (requiredPermission is null)
        {
            return Task.FromResult(Result<OfficialDocumentAuditEventResult>.Fail(Error.Validation(
                "OfficialDocumentLifecycleOperation.UnsupportedEventKind",
                "Official document lifecycle operation event kind is unsupported.")));
        }

        IReadOnlyCollection<string> permissions = request.PermissionProvider?.Invoke() ?? Array.Empty<string>();

        if (!permissions.Contains(requiredPermission, StringComparer.Ordinal))
        {
            return Task.FromResult(Result<OfficialDocumentAuditEventResult>.Fail(Error.Forbidden(
                "OfficialDocumentLifecycleOperation.PermissionDenied",
                "Official document lifecycle operation is not permitted for the current actor.")));
        }

        return _auditEvents.AppendAsync(request.AuditRequest, cancellationToken);
    }

    private static string? ResolvePermission(OfficialDocumentAuditEventKind eventKind)
    {
        return eventKind switch
        {
            OfficialDocumentAuditEventKind.Void => SalrynPermissions.OfficialDocumentVoid,
            OfficialDocumentAuditEventKind.Cancel => SalrynPermissions.OfficialDocumentCancel,
            OfficialDocumentAuditEventKind.Correction => SalrynPermissions.OfficialDocumentCorrection,
            OfficialDocumentAuditEventKind.Refund => SalrynPermissions.OfficialDocumentRefund,
            _ => null
        };
    }
}
