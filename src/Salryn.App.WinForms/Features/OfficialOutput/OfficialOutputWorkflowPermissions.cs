using Salryn.App.WinForms.Features.Shell;
using Salryn.Core.Security;

namespace Salryn.App.WinForms.Features.OfficialOutput;

public static class OfficialOutputWorkflowPermissions
{
    public static bool CanRunExportWorkflow(CurrentAppSession? session)
    {
        return session is not null &&
            session.IsActive &&
            (session.Can(SalrynPermissions.OfficialOutputRun) ||
             session.Can(SalrynPermissions.OfficialEvidenceExport));
    }

    public static bool CallerCanRunExportWorkflow(IReadOnlyCollection<string>? permissions)
    {
        if (permissions is null)
        {
            return false;
        }

        return permissions.Any(static permission =>
                string.Equals(permission, SalrynPermissions.OfficialOutputRun, StringComparison.OrdinalIgnoreCase)) ||
            permissions.Any(static permission =>
                string.Equals(permission, SalrynPermissions.OfficialEvidenceExport, StringComparison.OrdinalIgnoreCase));
    }
}
