using Salryn.Core.Repositories;

namespace Salryn.App.WinForms.Composition;

// Kirch: Keeps Users Admin repository construction owned by AppServices composition instead of WinForms controls.
public static class UsersAdminAppServices
{
    public static IUserWriteRepository CreateUserWriteRepository(
        this SalrynAppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.Repositories.CreateUserWriteRepository();
    }
}
