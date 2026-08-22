using Salryn.Core.Services;
using Salryn.Infrastructure.Data.Services;

namespace Salryn.App.WinForms.Composition;

// Kirch: Keeps backup recovery service construction owned by AppServices instead of WinForms controls.
public static class BackupRecoveryAppServices
{
    public static IBackupRecoveryService CreateBackupRecoveryService(
        this SalrynAppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return new SqliteBackupRecoveryService(services.DbPaths);
    }
}
