using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Salryn.Api.Local;
using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Contracts;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Security;
using Salryn.Infrastructure.Data.Seeding;

namespace Salryn.Tests;

public static class V2HostOperatorShiftApiChecks
{
    private const string SessionCookieName = LocalApiSessionService.SessionCookieName;

    public static async Task RunAsync()
    {
        string projectRoot = FindProjectRoot(Directory.GetCurrentDirectory());
        RunSourceGuards(projectRoot);
        await RunRuntimeChecksAsync(projectRoot).ConfigureAwait(false);
    }

    private static void RunSourceGuards(string projectRoot)
    {
        string apiRoot = Path.Combine(projectRoot, "src", "Salryn.Api.Local");
        string factory = ReadRequiredFile(Path.Combine(apiRoot, "LocalApiApplicationFactory.cs"));
        string shiftEndpoints = ReadRequiredFile(Path.Combine(apiRoot, "LocalHostShiftEndpoints.cs"));
        string shiftService = ReadRequiredFile(Path.Combine(apiRoot, "Services", "HostOperatorShiftApiService.cs"));
        string permissions = ReadRequiredFile(Path.Combine(apiRoot, "Auth", "LocalApiPermissionGuards.cs"));
        string coreSmoke = ReadRequiredFile(Path.Combine(projectRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs"));
        string seedFile = ReadRequiredFile(Path.Combine(
            projectRoot,
            "src",
            "Salryn.Infrastructure",
            "Data",
            "Seeding",
            "DefaultSeedData.cs"));

        AssertContains(coreSmoke, "V2HostOperatorShiftApiChecks.RunAsync()", "CoreSmoke must run V2 host shift checks.");
        AssertContains(factory, "MapLocalHostShiftEndpoints", "Factory must map host shift endpoints.");
        AssertContains(factory, "HostOperatorApiServices.Create", "Factory must compose real host operator services.");
        AssertContains(shiftEndpoints, "/api/shift", "Host shift read route must exist.");
        AssertContains(shiftEndpoints, "/api/shift/open", "Host shift open route must exist.");
        AssertContains(shiftEndpoints, "/api/shift/close", "Host shift close route must exist.");
        AssertContains(shiftEndpoints, "LocalApiPermissions.ShiftOpenClose", "Shift routes must require Shift.OpenClose.");
        AssertContains(shiftService, "ICashierSessionService", "Shift service must use real cashier session service.");
        AssertNotContains(shiftService, "IsSuccess: true", "Shift service must not fake success on failure paths.");
        AssertContains(permissions, "ShiftOpenClose = \"Shift.OpenClose\"", "Api permission catalog must include Shift.OpenClose.");
        AssertContains(seedFile, "Permissions.ShiftOpenClose", "Shift.OpenClose must remain seeded.");
    }

    private static async Task RunRuntimeChecksAsync(string projectRoot)
    {
        SalrynLocalComposition composition = SalrynLocalComposition.CreateDevelopment(projectRoot);
        await composition.InitializeDatabaseAsync().ConfigureAwait(false);

        InMemoryLocalApiSessionStore sessionStore = new();
        AuthenticatedUserReadModel cashier = await EnsureShiftOperatorAsync(composition)
            .ConfigureAwait(false);

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

            using HttpClient anonymous = CreateClient(baseAddress);
            await AssertStatusAsync(
                anonymous,
                HttpMethod.Get,
                "/api/shift",
                HttpStatusCode.Unauthorized,
                "Shift read without session must return 401.").ConfigureAwait(false);

            using HttpClient noShiftPermission = CreateClient(
                baseAddress,
                sessionStore.Create(new AuthenticatedUserReadModel(
                    Guid.NewGuid(),
                    "v2-shift-denied",
                    "V2 Shift Denied",
                    "Manager",
                    UserStatus.Active,
                    new[] { DefaultSeedData.Permissions.ProductsView })));
            await AssertStatusAsync(
                noShiftPermission,
                HttpMethod.Get,
                "/api/shift",
                HttpStatusCode.Forbidden,
                "Shift read without Shift.OpenClose must return 403.").ConfigureAwait(false);

            using HttpClient cashierClient = CreateClient(baseAddress, sessionStore.Create(cashier));

            JsonDocument noneShift = await GetShiftAsync(cashierClient).ConfigureAwait(false);
            AssertShiftState(noneShift, "None", "Fresh cashier should have no open shift.");

            JsonDocument openedShift = await PostShiftOpenAsync(cashierClient, 5000).ConfigureAwait(false);
            AssertShiftState(openedShift, "Open", "Shift open must return an open shift.");

            JsonDocument readOpenShift = await GetShiftAsync(cashierClient).ConfigureAwait(false);
            AssertShiftState(readOpenShift, "Open", "Shift read must return the open shift.");

            JsonDocument closedShift = await PostShiftCloseAsync(cashierClient).ConfigureAwait(false);
            AssertShiftState(closedShift, "Closed", "Shift close must return a closed shift.");

            await AssertStatusAsync(
                cashierClient,
                HttpMethod.Post,
                "/api/shift/close",
                HttpStatusCode.Conflict,
                "Closing with no open shift must return 409.").ConfigureAwait(false);
        }
        finally
        {
            await app.StopAsync().ConfigureAwait(false);
        }
    }

    private static async Task<AuthenticatedUserReadModel> EnsureShiftOperatorAsync(
        SalrynLocalComposition composition)
    {
        Result<IReadOnlyList<UserReadModel>> usersResult =
            await composition.UserReadRepository.GetAllAsync().ConfigureAwait(false);

        if (usersResult.IsFailure)
        {
            throw new InvalidOperationException(
                "V2 shift runtime could not read users: " + usersResult.ErrorDetail.Message);
        }

        foreach (UserReadModel user in usersResult.Value.Where(user => user.Status == UserStatus.Active))
        {
            Result<UserPermissionReadModel?> permissionsResult =
                await composition.UserPermissionReadRepository.FindByUserIdAsync(user.Id).ConfigureAwait(false);

            if (permissionsResult.IsFailure)
            {
                throw new InvalidOperationException(
                    "V2 shift runtime could not read permissions: " + permissionsResult.ErrorDetail.Message);
            }

            UserPermissionReadModel? permissions = permissionsResult.Value;
            if (permissions is not null &&
                permissions.Permissions.Contains(DefaultSeedData.Permissions.ShiftOpenClose, StringComparer.Ordinal))
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
            throw new InvalidOperationException(
                "V2 shift runtime requires an active local operator with Shift.OpenClose.");
        }

        Result<AuthenticatedUserReadModel> firstAdmin = await composition.Authentication
            .CreateFirstAdminAsync(new FirstRunAdminSetupRequest(
                "v2-shift-admin",
                "V2 Shift Admin",
                "V2ShiftAdmin!123",
                "V2ShiftAdmin!123"))
            .ConfigureAwait(false);

        if (firstAdmin.IsFailure)
        {
            throw new InvalidOperationException(
                "V2 shift runtime could not create first admin: " + firstAdmin.ErrorDetail.Message);
        }

        return firstAdmin.Value;
    }

    private static async Task<JsonDocument> GetShiftAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync("/api/shift").ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Expected GET /api/shift to return 200, got {(int)response.StatusCode}.");
        }

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    }

    private static async Task<JsonDocument> PostShiftOpenAsync(HttpClient client, long openingCashCents)
    {
        using HttpResponseMessage response = await client
            .PostAsJsonAsync("/api/shift/open", new HostOperatorShiftOpenRequest(openingCashCents))
            .ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Expected POST /api/shift/open to return 200, got {(int)response.StatusCode}.");
        }

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    }

    private static async Task<JsonDocument> PostShiftCloseAsync(HttpClient client)
    {
        using HttpResponseMessage response = await client.PostAsync("/api/shift/close", null).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Expected POST /api/shift/close to return 200, got {(int)response.StatusCode}.");
        }

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
    }

    private static void AssertShiftState(JsonDocument document, string expectedState, string message)
    {
        string? actual = document.RootElement.GetProperty("shift").GetProperty("shiftState").GetString();
        if (!string.Equals(actual, expectedState, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{message} Expected {expectedState}, got {actual ?? "<null>"}.");
        }
    }

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
        HttpMethod method,
        string path,
        HttpStatusCode expectedStatus,
        string message)
    {
        using HttpRequestMessage request = new(method, path);
        using HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);

        if (response.StatusCode != expectedStatus)
        {
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                $"{message} Expected {(int)expectedStatus}, got {(int)response.StatusCode}. Body: {body}");
        }
    }

    private static Uri ResolveStartedBaseAddress(WebApplication app)
    {
        string? address = app.Urls.FirstOrDefault(item =>
            item.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase));

        if (address is null)
        {
            throw new InvalidOperationException("Local API did not start on loopback.");
        }

        return new Uri(address);
    }

    private static string ReadRequiredFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Required file not found: " + path);
        }

        return File.ReadAllText(path);
    }

    private static string FindProjectRoot(string startPath)
    {
        DirectoryInfo? current = new(startPath);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not find Salryn.sln from " + startPath);
    }

    private static void AssertContains(string haystack, string needle, string message)
    {
        if (!haystack.Contains(needle, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void AssertNotContains(string haystack, string needle, string message)
    {
        if (haystack.Contains(needle, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }
}