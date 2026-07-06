using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Salryn.Api.Local;
using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Contracts;
using Salryn.Api.Local.RemotePos;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Security;
using Salryn.Infrastructure.Data.Seeding;

namespace Salryn.Tests;

public static class UserM1HostOnlyUserCreationChecks
{
    private const string SessionCookieName = LocalApiSessionService.SessionCookieName;

    public static async Task RunAsync()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        RunSourceGuards(projectRoot);
        await RunRuntimeChecksAsync(projectRoot).ConfigureAwait(false);
    }

    private static async Task RunRuntimeChecksAsync(string projectRoot)
    {
        SalrynLocalComposition composition = SalrynLocalComposition.CreateDevelopment(projectRoot);
        await composition.InitializeDatabaseAsync().ConfigureAwait(false);

        AuthenticatedUserReadModel adminUser = await EnsureUsersManageOperatorAsync(composition)
            .ConfigureAwait(false);
        InMemoryLocalApiSessionStore sessionStore = new();

        await using WebApplication app = await LocalApiApplicationFactory
            .CreateAsync(
                new[] { "--dev" },
                new LocalApiApplicationOptions
                {
                    Url = "http://127.0.0.1:0",
                    Composition = composition,
                    InitializeDatabase = false,
                    SessionStore = sessionStore,
                    EnableStaticWebAssets = false,
                    WriteDevelopmentDatabasePath = false
                })
            .ConfigureAwait(false);

        await app.StartAsync().ConfigureAwait(false);

        try
        {
            Uri baseAddress = ResolveStartedBaseAddress(app);
            RoleIds roles = await ResolveRoleIdsAsync(composition).ConfigureAwait(false);

            using HttpClient anonymous = CreateClient(baseAddress);
            await AssertStatusAsync(
                anonymous,
                BuildCreateRequest("user-m1-anon", roles.CashierRoleId, "AnonUserM1!"),
                HttpStatusCode.Unauthorized,
                "Anonymous user creation must return 401.").ConfigureAwait(false);

            using HttpClient noUsersManageClient = CreateClient(
                baseAddress,
                sessionStore.Create(new AuthenticatedUserReadModel(
                    Guid.NewGuid(),
                    "user-m1-no-manage",
                    "USER-M1 No Manage",
                    "Manager",
                    UserStatus.Active,
                    Array.Empty<string>())
                {
                    RoleId = roles.ManagerRoleId
                }));
            await AssertStatusAsync(
                noUsersManageClient,
                BuildCreateRequest("user-m1-forbidden", roles.CashierRoleId, "NoManageM1!"),
                HttpStatusCode.Forbidden,
                "User creation without Users.Manage must return 403.").ConfigureAwait(false);

            LocalApiSession adminSession = sessionStore.Create(adminUser);
            using HttpClient admin = CreateClient(baseAddress, adminSession);
            await AssertBridgeHeaderRejectedAsync(admin, roles.CashierRoleId).ConfigureAwait(false);

            string unique = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            string cashierUsername = "m1-cashier-" + unique;
            string cashierPassword = "CashierM1!" + unique;
            UserListItemResponse cashier = await AssertCreateUserAsync(
                admin,
                BuildCreateRequest(cashierUsername, roles.CashierRoleId, cashierPassword),
                HttpStatusCode.Created,
                cashierPassword,
                "Cashier").ConfigureAwait(false);

            await AssertCreatedUserListedAsync(admin, cashierUsername).ConfigureAwait(false);
            await AssertCreatedUserAuthenticatesAsync(
                composition,
                cashierUsername,
                cashierPassword,
                "Cashier").ConfigureAwait(false);

            string managerUsername = "m1-manager-" + unique;
            string managerPassword = "ManagerM1!" + unique;
            await AssertCreateUserAsync(
                admin,
                BuildCreateRequest(managerUsername, roles.ManagerRoleId, managerPassword),
                HttpStatusCode.Created,
                managerPassword,
                "Manager").ConfigureAwait(false);
            await AssertCreatedUserAuthenticatesAsync(
                composition,
                managerUsername,
                managerPassword,
                "Manager").ConfigureAwait(false);

            await AssertStatusAsync(
                admin,
                BuildCreateRequest(cashierUsername, roles.CashierRoleId, "DuplicateM1!"),
                HttpStatusCode.Conflict,
                "Duplicate username must return 409.").ConfigureAwait(false);

            await AssertStatusAsync(
                admin,
                BuildCreateRequest("m1-weak-" + unique, roles.CashierRoleId, "short"),
                HttpStatusCode.BadRequest,
                "Weak password must return 400.").ConfigureAwait(false);

            await AssertStatusAsync(
                admin,
                BuildCreateRequest("m1-admin-" + unique, roles.AdminRoleId, "AdminRoleM1!"),
                HttpStatusCode.Forbidden,
                "USER-M1 endpoint must reject Admin role creation.").ConfigureAwait(false);

            if (!RemotePosOperatorSessionService.IsRemotePosLoginRoleAllowed(cashier.RoleName) ||
                !RemotePosOperatorSessionService.IsRemotePosLoginRoleAllowed("Manager") ||
                RemotePosOperatorSessionService.IsRemotePosLoginRoleAllowed("Admin"))
            {
                throw new InvalidOperationException("Remote POS role boundary must allow Cashier/Manager and reject Admin.");
            }
        }
        finally
        {
            await app.StopAsync().ConfigureAwait(false);
        }
    }

    private static async Task<AuthenticatedUserReadModel> EnsureUsersManageOperatorAsync(
        SalrynLocalComposition composition)
    {
        Result<IReadOnlyList<UserReadModel>> usersResult =
            await composition.UserReadRepository.GetAllAsync().ConfigureAwait(false);
        RequireSuccess(usersResult, "USER-M1.GetUsers");

        foreach (UserReadModel user in usersResult.Value.Where(user => user.Status == UserStatus.Active))
        {
            Result<UserPermissionReadModel?> permissionsResult =
                await composition.UserPermissionReadRepository.FindByUserIdAsync(user.Id).ConfigureAwait(false);
            RequireSuccess(permissionsResult, "USER-M1.GetUserPermissions");

            UserPermissionReadModel? permissions = permissionsResult.Value;
            if (permissions is not null &&
                permissions.Permissions.Contains(DefaultSeedData.Permissions.UsersManage, StringComparer.Ordinal))
            {
                return new AuthenticatedUserReadModel(
                    permissions.UserId,
                    permissions.Username,
                    permissions.DisplayName,
                    permissions.RoleName,
                    permissions.Status,
                    permissions.Permissions)
                {
                    RoleId = permissions.RoleId
                };
            }
        }

        if (usersResult.Value.Any(user => user.Status == UserStatus.Active))
        {
            throw new InvalidOperationException("USER-M1 runtime requires an active local operator with Users.Manage.");
        }

        Result<AuthenticatedUserReadModel> firstAdmin = await composition.Authentication
            .CreateFirstAdminAsync(new FirstRunAdminSetupRequest(
                "user-m1-admin",
                "USER-M1 Admin",
                "UserM1Admin!123",
                "UserM1Admin!123"))
            .ConfigureAwait(false);
        RequireSuccess(firstAdmin, "USER-M1.CreateFirstAdmin");

        return firstAdmin.Value;
    }

    private static async Task<RoleIds> ResolveRoleIdsAsync(SalrynLocalComposition composition)
    {
        Result<IReadOnlyList<UserRoleReadModel>> rolesResult =
            await composition.UserReadRepository.GetRolesAsync().ConfigureAwait(false);
        RequireSuccess(rolesResult, "USER-M1.GetRoles");

        Guid admin = RoleId(rolesResult.Value, "Admin");
        Guid cashier = RoleId(rolesResult.Value, "Cashier");
        Guid manager = RoleId(rolesResult.Value, "Manager");
        return new RoleIds(admin, cashier, manager);
    }

    private static Guid RoleId(IReadOnlyList<UserRoleReadModel> roles, string roleName)
    {
        UserRoleReadModel? role = roles.FirstOrDefault(
            item => item.IsActive && string.Equals(item.RoleName, roleName, StringComparison.OrdinalIgnoreCase));

        return role?.RoleId ?? throw new InvalidOperationException("USER-M1 expected active role: " + roleName);
    }

    private static CreateUserApiRequest BuildCreateRequest(
        string username,
        Guid roleId,
        string password) =>
        new(
            username,
            "USER-M1 " + username,
            password,
            password,
            roleId,
            IsActive: true);

    private static HttpClient CreateClient(Uri baseAddress, LocalApiSession? session = null)
    {
        HttpClient client = new()
        {
            BaseAddress = baseAddress
        };

        if (session is not null)
        {
            client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookieName}={session.SessionId}");
        }

        return client;
    }

    private static async Task AssertStatusAsync(
        HttpClient client,
        CreateUserApiRequest request,
        HttpStatusCode expectedStatus,
        string message)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/users", request)
            .ConfigureAwait(false);

        if (response.StatusCode != expectedStatus)
        {
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                $"{message} Expected {(int)expectedStatus}, got {(int)response.StatusCode}: {body}");
        }
    }

    private static async Task<UserListItemResponse> AssertCreateUserAsync(
        HttpClient client,
        CreateUserApiRequest request,
        HttpStatusCode expectedStatus,
        string plainPassword,
        string expectedRole)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/users", request)
            .ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (response.StatusCode != expectedStatus)
        {
            throw new InvalidOperationException(
                $"Create user expected {(int)expectedStatus}, got {(int)response.StatusCode}: {body}");
        }

        if (body.Contains(plainPassword, StringComparison.Ordinal) ||
            body.Contains("passwordHash", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("temporaryPassword", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Create user response must not expose password material.");
        }

        UserListItemResponse? created = JsonSerializer.Deserialize<UserListItemResponse>(
            body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (created is null ||
            !string.Equals(created.Username, request.Username, StringComparison.Ordinal) ||
            !string.Equals(created.RoleName, expectedRole, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(created.Status, "Active", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Create user response did not contain the expected safe user row.");
        }

        return created;
    }

    private static async Task AssertCreatedUserListedAsync(HttpClient client, string username)
    {
        using HttpResponseMessage response = await client.GetAsync("/api/users").ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.OK || !body.Contains(username, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Created user did not appear in GET /api/users.");
        }
    }

    private static async Task AssertCreatedUserAuthenticatesAsync(
        SalrynLocalComposition composition,
        string username,
        string password,
        string expectedRole)
    {
        Result<AuthenticatedUserReadModel> login = await composition.Authentication
            .LoginAsync(new LoginRequest(username, password))
            .ConfigureAwait(false);
        RequireSuccess(login, "USER-M1.LoginCreatedUser");

        if (!string.Equals(login.Value.RoleName, expectedRole, StringComparison.OrdinalIgnoreCase) ||
            !RemotePosOperatorSessionService.IsRemotePosLoginRoleAllowed(login.Value.RoleName))
        {
            throw new InvalidOperationException("Created user did not satisfy Remote POS Cashier/Manager role boundary.");
        }
    }

    private static async Task AssertBridgeHeaderRejectedAsync(HttpClient client, Guid cashierRoleId)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/users");
        request.Headers.Add("X-Salryn-Terminal-Id", Guid.NewGuid().ToString("D"));
        request.Content = JsonContent.Create(BuildCreateRequest(
            "m1-bridge-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            cashierRoleId,
            "BridgeM1!123"));

        using HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException("Create user with remote bridge headers must be rejected as non-local.");
        }
    }

    private static Uri ResolveStartedBaseAddress(WebApplication app)
    {
        string? address = app.Urls.FirstOrDefault(item =>
            item.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(address) || address.EndsWith(":0", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("USER-M1 runtime harness could not resolve started loopback address.");
        }

        return new Uri(address);
    }

    private static void RunSourceGuards(string projectRoot)
    {
        string webRoot = Path.Combine(projectRoot, "src", "Salryn.App.Web", "src");
        string apiRoot = Path.Combine(projectRoot, "src", "Salryn.Api.Local");
        string usersPage = ReadRequiredFile(webRoot, "pages", "UsersPage.tsx");
        string usersApi = ReadRequiredFile(webRoot, "api", "users.ts");
        string permissions = ReadRequiredFile(webRoot, "security", "permissions.ts");
        string app = ReadRequiredFile(webRoot, "App.tsx");
        string endpoints = ReadRequiredFile(apiRoot, "LocalUserAdminEndpoints.cs");
        string service = ReadRequiredFile(apiRoot, "Services", "UserAdminApiService.cs");
        string writeRepository = ReadRequiredFile(
            projectRoot,
            "src",
            "Salryn.Infrastructure",
            "Data",
            "Repositories",
            "System",
            "SqliteUserWriteRepository.cs");
        string seedData = ReadRequiredFile(
            projectRoot,
            "src",
            "Salryn.Infrastructure",
            "Data",
            "Seeding",
            "DefaultSeedData.cs");
        string coreSmoke = ReadRequiredFile(projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");

        AssertContains(usersPage, "Create user", "Users page must expose a Create user form.");
        AssertContains(usersPage, "Display name", "Create user form must include display name.");
        AssertContains(usersPage, "Username", "Create user form must include username.");
        AssertContains(usersPage, "Temporary password", "Create user form must include temporary password.");
        AssertContains(usersPage, "Confirm temporary password", "Create user form must include password confirmation.");
        AssertContains(usersPage, "Active/enabled", "Create user form must include active/enabled checkbox.");
        AssertContains(usersPage, "\"Cashier\"", "Users page must offer Cashier role.");
        AssertContains(usersPage, "\"Manager\"", "Users page must offer Manager role.");
        AssertContains(usersPage, "setCreateForm(emptyCreateForm)", "Users page must clear password fields after successful create.");
        AssertContains(usersPage, "Temporary password cleared", "Users page success message must not display the password.");
        AssertContains(usersApi, "method: \"POST\"", "Users API client must call POST for create user.");
        AssertContains(usersApi, "temporaryPassword", "Users API client must send temporary password only in the request body.");
        AssertNotContains(usersApi, "passwordHash", "Users API client must not expose password hashes.");
        AssertContains(permissions, "users: [Permissions.UsersManage]", "Users page must require Users.Manage permission.");

        string remoteClientApp = ExtractBlock(app, "function RemoteClientApp()", "function ConsoleApp()");
        AssertNotContains(remoteClientApp, "UsersPage", "Remote client app must not render Users page.");
        AssertNotContains(remoteClientApp, "Create user", "Remote client app must not expose create-user form.");

        AssertContains(endpoints, "app.MapPost(\"/api/users\"", "Backend must map minimal POST /api/users.");
        AssertContains(endpoints, "LocalApiPermissionGuards.RequirePermission", "Create user endpoint must use permission guard.");
        AssertContains(endpoints, "LocalApiPermissions.UsersManage", "Create user endpoint must require Users.Manage.");
        AssertContains(service, "SalrynPasswordHasher.HashPassword", "Create user service must use existing password hashing.");
        AssertContains(service, "\"Cashier\"", "Create user service must allow Cashier.");
        AssertContains(service, "\"Manager\"", "Create user service must allow Manager.");
        AssertContains(service, "Users.RoleNotAllowedForMvp", "Create user service must reject roles outside USER-M1 scope.");
        AssertNotContains(service, "Console.WriteLine", "Create user service must not log password-bearing requests.");
        AssertNotContains(writeRepository, "TemporaryPassword", "Repository must not persist plaintext temporary passwords.");
        AssertNotContains(writeRepository, "ConfirmTemporaryPassword", "Repository must not persist password confirmation.");
        AssertNotContains(seedData, "cashier_dev", "USER-M1 must not add seeded cashier credentials.");
        AssertContains(coreSmoke, "UserM1HostOnlyUserCreationChecks.RunAsync()", "USER-M1 checks must be wired into CoreSmoke.");
    }

    private static string ReadRequiredFile(params string[] pathParts)
    {
        string path = Path.Combine(pathParts);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("Required source file missing: " + path);
        }

        return File.ReadAllText(path);
    }

    private static string FindProjectRoot(string startPath)
    {
        DirectoryInfo? directory = new(startPath);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Salryn.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find Salryn.sln.");
    }

    private static void RequireSuccess(Result result, string area)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{area} failed: {result.Error}");
        }
    }

    private static void RequireSuccess<T>(Result<T> result, string area)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"{area} failed: {result.Error}");
        }
    }

    private static void AssertContains(string source, string expected, string message)
    {
        if (!source.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertNotContains(string source, string forbidden, string message)
    {
        if (source.Contains(forbidden, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static string ExtractBlock(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException("Expected block start missing: " + startMarker);
        }

        int end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        if (end < 0)
        {
            throw new InvalidOperationException("Expected block end missing: " + endMarker);
        }

        return source[start..end];
    }

    private sealed record RoleIds(Guid AdminRoleId, Guid CashierRoleId, Guid ManagerRoleId);
}
