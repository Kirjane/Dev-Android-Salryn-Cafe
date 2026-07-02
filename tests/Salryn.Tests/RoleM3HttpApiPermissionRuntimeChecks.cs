using System.Net;
using Microsoft.AspNetCore.Builder;
using Salryn.Api.Local;
using Salryn.Api.Local.Auth;
using Salryn.Core.Models;
using Salryn.Core.Security;

namespace Salryn.Tests;

public static class RoleM3HttpApiPermissionRuntimeChecks
{
    private const string ProductsView = "Products.View";
    private const string AuditView = "Audit.View";
    private const string SessionCookieName = LocalApiSessionService.SessionCookieName;

    public static async Task RunAsync()
    {
        RunSourceGuards(FindProjectRoot(Directory.GetCurrentDirectory()));

        InMemoryLocalApiSessionStore sessionStore = new();
        await using WebApplication app = await LocalApiApplicationFactory
            .CreateAsync(
                new[] { "--dev" },
                new LocalApiApplicationOptions
                {
                    Url = "http://127.0.0.1:0",
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
                "/health",
                HttpStatusCode.OK,
                "GET /health must remain public.").ConfigureAwait(false);
            await AssertStatusAsync(
                anonymous,
                HttpMethod.Get,
                "/api/session",
                HttpStatusCode.OK,
                "GET /api/session must remain reachable for auth-state discovery.").ConfigureAwait(false);
            await AssertStatusAsync(
                anonymous,
                HttpMethod.Get,
                "/api/dashboard/summary",
                HttpStatusCode.Unauthorized,
                "GET /api/dashboard/summary without a session must return 401.").ConfigureAwait(false);
            await AssertStatusAsync(
                anonymous,
                HttpMethod.Get,
                "/api/products",
                HttpStatusCode.Unauthorized,
                "GET /api/products without a session must return 401.").ConfigureAwait(false);
            await AssertStatusAsync(
                anonymous,
                HttpMethod.Get,
                "/api/remote-readonly/status",
                HttpStatusCode.Unauthorized,
                "GET /api/remote-readonly/status without a session must return 401.").ConfigureAwait(false);

            LocalApiSession roleOnlySession = CreateSession(
                sessionStore,
                roleName: "Admin",
                permissions: Array.Empty<string>());
            using HttpClient roleOnlyClient = CreateClient(baseAddress, roleOnlySession);
            await AssertStatusAsync(
                roleOnlyClient,
                HttpMethod.Get,
                "/api/products",
                HttpStatusCode.Forbidden,
                "Admin roleName without Products.View must return 403.").ConfigureAwait(false);

            LocalApiSession productsSession = CreateSession(
                sessionStore,
                roleName: "Cashier",
                permissions: new[] { ProductsView });
            using HttpClient productsClient = CreateClient(baseAddress, productsSession);
            await AssertNotUnauthorizedOrForbiddenAsync(
                productsClient,
                HttpMethod.Get,
                "/api/products",
                "Products.View permission must reach the product route regardless of roleName.")
                .ConfigureAwait(false);
            await AssertStatusAsync(
                productsClient,
                HttpMethod.Get,
                "/api/remote-readonly/audit?page=1&pageSize=25",
                HttpStatusCode.Forbidden,
                "Authenticated session missing Audit.View must return 403 for remote-readonly audit.")
                .ConfigureAwait(false);

            await AssertMutationRouteNotIntroducedAsync(anonymous, "/api/products")
                .ConfigureAwait(false);
        }
        finally
        {
            await app.StopAsync().ConfigureAwait(false);
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
            client.DefaultRequestHeaders.Add(
                "Cookie",
                $"{SessionCookieName}={session.SessionId}");
        }

        return client;
    }

    private static LocalApiSession CreateSession(
        InMemoryLocalApiSessionStore sessionStore,
        string roleName,
        IReadOnlyList<string> permissions)
    {
        AuthenticatedUserReadModel user = new(
            Guid.NewGuid(),
            "role-m3-runtime",
            "ROLE-M3 Runtime",
            roleName,
            UserStatus.Active,
            permissions)
        {
            RoleId = Guid.NewGuid()
        };

        return sessionStore.Create(user);
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
            throw new InvalidOperationException(
                $"{message} Expected {(int)expectedStatus}, got {(int)response.StatusCode}.");
        }
    }

    private static async Task AssertNotUnauthorizedOrForbiddenAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string message)
    {
        using HttpRequestMessage request = new(method, path);
        using HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException(
                $"{message} Got forbidden auth status {(int)response.StatusCode}.");
        }
    }

    private static async Task AssertMutationRouteNotIntroducedAsync(
        HttpClient client,
        string path)
    {
        foreach (HttpMethod method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete })
        {
            using HttpRequestMessage request = new(method, path);
            using HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false);

            if ((int)response.StatusCode is >= 200 and <= 299)
            {
                throw new InvalidOperationException(
                    $"ROLE-M3 must not introduce mutation route {method.Method} {path}.");
            }
        }
    }

    private static Uri ResolveStartedBaseAddress(WebApplication app)
    {
        string? address = app.Urls.FirstOrDefault(item =>
            item.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(address) || address.EndsWith(":0", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("ROLE-M3 runtime harness could not resolve started loopback address.");
        }

        return new Uri(address);
    }

    private static void RunSourceGuards(string projectRoot)
    {
        string apiRoot = Path.Combine(projectRoot, "src", "Salryn.Api.Local");
        string factory = ReadRequiredFile(Path.Combine(apiRoot, "LocalApiApplicationFactory.cs"));
        string program = ReadRequiredFile(Path.Combine(apiRoot, "Program.cs"));
        string readOnlyEndpoints = ReadRequiredFile(Path.Combine(apiRoot, "ReadOnlyEndpointExtensions.cs"));
        string remoteReadOnlyEndpoints = ReadRequiredFile(Path.Combine(apiRoot, "RemoteReadOnlyApiEndpoints.cs"));
        string coreSmokeProgram = ReadRequiredFile(Path.Combine(
            projectRoot,
            "tools",
            "Salryn.Tools.CoreSmoke",
            "Program.cs"));

        AssertContains(factory, "LocalApiApplicationFactory", "ROLE-M3 app factory seam must exist.");
        AssertContains(factory, "public static async Task<WebApplication> CreateAsync", "ROLE-M3 seam must expose WebApplication creation.");
        AssertContains(factory, "http://127.0.0.1:5299", "Production Api.Local loopback bind must remain unchanged.");
        AssertContains(program, "LocalApiApplicationFactory.CreateAsync(args)", "Program.cs must use the shared factory.");
        AssertContains(program, "app.Run();", "Production Program.cs must still run the app.");
        AssertContains(coreSmokeProgram, "RoleM3HttpApiPermissionRuntimeChecks.RunAsync()", "ROLE-M3 runtime checks must be wired explicitly into CoreSmoke.");

        AssertNoMutationRoutes(readOnlyEndpoints, "ReadOnlyEndpointExtensions.cs");
        AssertNoMutationRoutes(remoteReadOnlyEndpoints, "RemoteReadOnlyApiEndpoints.cs");
        AssertNoRuntimeExpansionMarkers(apiRoot);
    }

    private static void AssertNoMutationRoutes(string source, string label)
    {
        string[] forbiddenMarkers =
        {
            "MapPost",
            "MapPut",
            "MapPatch",
            "MapDelete",
            "CompleteSale",
            "CreateProduct",
            "UpdateProduct",
            "DeactivateProduct",
            "StockAdjustment",
            "Backup",
            "OfficialOutput",
            "OfficialDocument"
        };

        foreach (string marker in forbiddenMarkers)
        {
            AssertNotContains(source, marker, label + " must not introduce mutation marker: " + marker);
        }
    }

    private static void AssertNoRuntimeExpansionMarkers(string apiRoot)
    {
        foreach (string file in Directory.EnumerateFiles(apiRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string text = File.ReadAllText(file);
            string relative = Path.GetRelativePath(apiRoot, file);
            AssertNotContains(text, "0.0.0.0", "Api.Local must not bind to all interfaces: " + relative);
            AssertNotContains(text, "QRCode", "Api.Local must not add QR behavior: " + relative);
            AssertNotContains(text, "network discovery", "Api.Local must not add discovery behavior: " + relative);
            AssertNotContains(text, "mobile/cloud", "Api.Local must not add mobile/cloud behavior: " + relative);
            AssertNotContains(text, "Salryn.App.WinForms", "Api.Local must not depend on WinForms operator UI: " + relative);
        }
    }

    private static string ReadRequiredFile(string path)
    {
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

        throw new InvalidOperationException("Could not find Salryn.sln from current directory.");
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
}
