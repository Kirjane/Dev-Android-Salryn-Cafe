using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Salryn.Api.Local;
using Salryn.Api.Local.Auth;
using Salryn.Core.Models;
using Salryn.Core.Security;
using Salryn.Core.Services.Licensing;

namespace Salryn.Tests;

public static class RoleAuthorizationBlockerFixChecks
{
    private const string SessionCookieName = LocalApiSessionService.SessionCookieName;

    public static async Task RunAsync()
    {
        VerifyLicenseBootstrapPolicy();

        InMemoryLocalApiSessionStore sessionStore = new();
        FakeLicenseService licenseService = new(LicenseStatus.Licensed);
        await using WebApplication app = await LocalApiApplicationFactory
            .CreateAsync(
                new[] { "--dev" },
                new LocalApiApplicationOptions
                {
                    Url = "http://127.0.0.1:0",
                    SessionStore = sessionStore,
                    LicenseService = licenseService,
                    EnableStaticWebAssets = false,
                    WriteDevelopmentDatabasePath = false
                })
            .ConfigureAwait(false);

        await app.StartAsync().ConfigureAwait(false);

        try
        {
            Uri baseAddress = ResolveStartedBaseAddress(app);
            using HttpClient anonymous = CreateClient(baseAddress);
            using HttpClient cashier = CreateClient(
                baseAddress,
                CreateSession(sessionStore, "Cashier", Array.Empty<string>()));
            using HttpClient manager = CreateClient(
                baseAddress,
                CreateSession(
                    sessionStore,
                    "Manager",
                    new[]
                    {
                        SalrynPermissions.ReportsView,
                        SalrynPermissions.OfficialReadingView,
                        SalrynPermissions.OfficialEvidenceExport
                    }));
            using HttpClient admin = CreateClient(
                baseAddress,
                CreateSession(
                    sessionStore,
                    "Admin",
                    new[]
                    {
                        SalrynPermissions.SettingsManage,
                        SalrynPermissions.ReportsView,
                        SalrynPermissions.OfficialReadingView,
                        SalrynPermissions.OfficialReadingFinalize,
                        SalrynPermissions.OfficialEvidenceExport,
                        SalrynPermissions.OfficialOutputRun,
                        SalrynPermissions.OfficialDocumentReprint,
                        SalrynPermissions.OfficialDocumentVoid,
                        SalrynPermissions.OfficialDocumentCancel,
                        SalrynPermissions.OfficialDocumentCorrection,
                        SalrynPermissions.OfficialDocumentRefund
                    }));

            await VerifyOfficialLifecycleAsync(anonymous, cashier, admin).ConfigureAwait(false);
            await VerifyLicenseBoundaryAsync(anonymous, cashier, admin, licenseService).ConfigureAwait(false);
            await VerifyTraditionalReportingAsync(cashier, manager, admin).ConfigureAwait(false);
            await VerifyDashboardAsync(cashier, manager, admin).ConfigureAwait(false);
            await VerifyOtherOfficialEndpointsAsync(cashier, manager, admin).ConfigureAwait(false);
            await VerifyTerminalHostApiDenialsAsync(baseAddress, admin).ConfigureAwait(false);
        }
        finally
        {
            await app.StopAsync().ConfigureAwait(false);
        }
    }

    private static void VerifyLicenseBootstrapPolicy()
    {
        foreach (LicenseStatus status in new[]
        {
            LicenseStatus.Unlicensed,
            LicenseStatus.Expired,
            LicenseStatus.Invalid
        })
        {
            Require(
                !LocalLicenseEndpoints.RequiresPrivilegedInstallAuthorization(status),
                $"{status} must retain the signed first-install/recovery path.");
        }

        foreach (LicenseStatus status in new[]
        {
            LicenseStatus.Licensed,
            LicenseStatus.Grace,
            LicenseStatus.Development
        })
        {
            Require(
                LocalLicenseEndpoints.RequiresPrivilegedInstallAuthorization(status),
                $"{status} must require Settings.Manage for license replacement.");
        }
    }

    private static async Task VerifyOfficialLifecycleAsync(
        HttpClient anonymous,
        HttpClient cashier,
        HttpClient admin)
    {
        object request = new
        {
            action = "Refund",
            reason = "authorization-smoke",
            idempotencyKey = "role-auth-refund"
        };

        await AssertStatusAsync(
            anonymous,
            HttpMethod.Post,
            "/api/official/document-lifecycle/action",
            request,
            HttpStatusCode.Unauthorized,
            "Unauthenticated lifecycle mutation must return 401.").ConfigureAwait(false);
        await AssertStatusAsync(
            cashier,
            HttpMethod.Post,
            "/api/official/document-lifecycle/action",
            request,
            HttpStatusCode.Forbidden,
            "Cashier lifecycle mutation must return 403.").ConfigureAwait(false);
        await AssertNotAuthDeniedAsync(
            admin,
            HttpMethod.Post,
            "/api/official/document-lifecycle/action",
            request,
            "Authorized lifecycle mutation must reach business validation.").ConfigureAwait(false);
    }

    private static async Task VerifyLicenseBoundaryAsync(
        HttpClient anonymous,
        HttpClient cashier,
        HttpClient admin,
        FakeLicenseService licenseService)
    {
        object request = new { licenseJson = "{}" };

        await AssertStatusAsync(
            anonymous,
            HttpMethod.Post,
            "/api/license/install",
            request,
            HttpStatusCode.Unauthorized,
            "Normal post-bootstrap anonymous license install must return 401.").ConfigureAwait(false);
        await AssertStatusAsync(
            cashier,
            HttpMethod.Post,
            "/api/license/install",
            request,
            HttpStatusCode.Forbidden,
            "Cashier license replacement must return 403.").ConfigureAwait(false);

        int beforeAdmin = licenseService.InstallCalls;
        await AssertNotAuthDeniedAsync(
            admin,
            HttpMethod.Post,
            "/api/license/install",
            request,
            "Settings.Manage license replacement must reach signed-payload validation.").ConfigureAwait(false);
        Require(licenseService.InstallCalls == beforeAdmin + 1, "Authorized license install must invoke the license service.");

        licenseService.Status = LicenseStatus.Unlicensed;
        int beforeBootstrap = licenseService.InstallCalls;
        await AssertNotAuthDeniedAsync(
            anonymous,
            HttpMethod.Post,
            "/api/license/install",
            request,
            "Unlicensed loopback first install must reach signed-payload validation.").ConfigureAwait(false);
        Require(licenseService.InstallCalls == beforeBootstrap + 1, "First-install exception must invoke signed license validation.");
        licenseService.Status = LicenseStatus.Licensed;
    }

    private static async Task VerifyTraditionalReportingAsync(
        HttpClient cashier,
        HttpClient manager,
        HttpClient admin)
    {
        object invalidRange = new { rangeStart = "", rangeEnd = "" };

        await AssertStatusAsync(
            cashier,
            HttpMethod.Post,
            "/api/official/traditional-reporting/z-reading/finalize",
            invalidRange,
            HttpStatusCode.Forbidden,
            "Cashier Z-reading finalization must return 403.").ConfigureAwait(false);
        await AssertNotAuthDeniedAsync(
            admin,
            HttpMethod.Post,
            "/api/official/traditional-reporting/z-reading/finalize",
            invalidRange,
            "OfficialReading.Finalize must reach business validation.").ConfigureAwait(false);
        await AssertStatusAsync(
            cashier,
            HttpMethod.Post,
            "/api/official/traditional-reporting/ej-export",
            invalidRange,
            HttpStatusCode.Forbidden,
            "Cashier evidence export must return 403.").ConfigureAwait(false);
        await AssertNotAuthDeniedAsync(
            manager,
            HttpMethod.Get,
            "/api/official/traditional-reporting/status",
            body: null,
            "OfficialReading.View must reach the reporting status route.").ConfigureAwait(false);
    }

    private static async Task VerifyDashboardAsync(
        HttpClient cashier,
        HttpClient manager,
        HttpClient admin)
    {
        await AssertStatusAsync(
            cashier,
            HttpMethod.Get,
            "/api/dashboard/summary",
            body: null,
            HttpStatusCode.Forbidden,
            "Cashier dashboard summary must return 403.").ConfigureAwait(false);
        await AssertStatusAsync(
            manager,
            HttpMethod.Get,
            "/api/dashboard/summary",
            body: null,
            HttpStatusCode.OK,
            "Manager dashboard summary must return 200.").ConfigureAwait(false);
        await AssertStatusAsync(
            admin,
            HttpMethod.Get,
            "/api/dashboard/summary",
            body: null,
            HttpStatusCode.OK,
            "Admin dashboard summary must return 200.").ConfigureAwait(false);
    }

    private static async Task VerifyOtherOfficialEndpointsAsync(
        HttpClient cashier,
        HttpClient manager,
        HttpClient admin)
    {
        await AssertStatusAsync(
            cashier,
            HttpMethod.Get,
            "/api/official/readiness",
            body: null,
            HttpStatusCode.Forbidden,
            "Cashier official readiness read must return 403.").ConfigureAwait(false);
        await AssertNotAuthDeniedAsync(
            manager,
            HttpMethod.Get,
            "/api/official/readiness",
            body: null,
            "OfficialReading.View must reach readiness.").ConfigureAwait(false);
        await AssertStatusAsync(
            cashier,
            HttpMethod.Post,
            "/api/official/invoice-output/generate",
            new { },
            HttpStatusCode.Forbidden,
            "Cashier invoice output generation must return 403.").ConfigureAwait(false);
        await AssertNotAuthDeniedAsync(
            admin,
            HttpMethod.Post,
            "/api/official/invoice-output/generate",
            new { },
            "OfficialOutput.Run must reach invoice output validation.").ConfigureAwait(false);
        await AssertStatusAsync(
            cashier,
            HttpMethod.Post,
            "/api/official/dry-run/packages",
            new { },
            HttpStatusCode.Forbidden,
            "Cashier internal package creation must return 403.").ConfigureAwait(false);
    }

    private static async Task VerifyTerminalHostApiDenialsAsync(Uri baseAddress, HttpClient admin)
    {
        using HttpClient terminal = CreateClient(baseAddress);
        terminal.DefaultRequestHeaders.Add("Cookie", admin.DefaultRequestHeaders.GetValues("Cookie").Single());
        terminal.DefaultRequestHeaders.Add("X-Salryn-Terminal-Id", Guid.NewGuid().ToString("D"));
        terminal.DefaultRequestHeaders.Add("X-Salryn-Handshake-Session-Id", Guid.NewGuid().ToString("D"));

        await AssertStatusAsync(
            terminal,
            HttpMethod.Get,
            "/api/dashboard/summary",
            body: null,
            HttpStatusCode.Forbidden,
            "Terminal must not call host dashboard API.").ConfigureAwait(false);
        await AssertStatusAsync(
            terminal,
            HttpMethod.Post,
            "/api/license/install",
            new { licenseJson = "{}" },
            HttpStatusCode.Forbidden,
            "Terminal must not call host license API.").ConfigureAwait(false);
        await AssertStatusAsync(
            terminal,
            HttpMethod.Post,
            "/api/official/document-lifecycle/action",
            new { action = "Refund", idempotencyKey = "terminal-denied" },
            HttpStatusCode.Forbidden,
            "Terminal must not call host official lifecycle API.").ConfigureAwait(false);
    }

    private static HttpClient CreateClient(Uri baseAddress, LocalApiSession? session = null)
    {
        HttpClient client = new() { BaseAddress = baseAddress };
        if (session is not null)
        {
            client.DefaultRequestHeaders.Add("Cookie", $"{SessionCookieName}={session.SessionId}");
        }

        return client;
    }

    private static LocalApiSession CreateSession(
        InMemoryLocalApiSessionStore store,
        string roleName,
        IReadOnlyList<string> permissions)
    {
        AuthenticatedUserReadModel user = new(
            Guid.NewGuid(),
            $"role-auth-{roleName.ToLowerInvariant()}",
            $"Role Auth {roleName}",
            roleName,
            UserStatus.Active,
            permissions)
        {
            RoleId = Guid.NewGuid()
        };
        return store.Create(user);
    }

    private static async Task AssertStatusAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        object? body,
        HttpStatusCode expected,
        string message)
    {
        using HttpResponseMessage response = await SendAsync(client, method, path, body).ConfigureAwait(false);
        if (response.StatusCode != expected)
        {
            string content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                $"{message} Expected {(int)expected}, got {(int)response.StatusCode}: {content}");
        }
    }

    private static async Task AssertNotAuthDeniedAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        object? body,
        string message)
    {
        using HttpResponseMessage response = await SendAsync(client, method, path, body).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            string content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new InvalidOperationException($"{message} Got {(int)response.StatusCode}: {content}");
        }
    }

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        object? body)
    {
        HttpRequestMessage request = new(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return client.SendAsync(request);
    }

    private static Uri ResolveStartedBaseAddress(WebApplication app)
    {
        string? address = app.Urls.FirstOrDefault(item =>
            item.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(address) || address.EndsWith(":0", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Authorization runtime harness could not resolve loopback address.");
        }

        return new Uri(address);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class FakeLicenseService : ILocalLicenseService
    {
        public FakeLicenseService(LicenseStatus status)
        {
            Status = status;
        }

        public LicenseStatus Status { get; set; }

        public int InstallCalls { get; private set; }

        public Task<LocalLicenseValidationResult> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new LocalLicenseValidationResult(
                Status,
                LicenseEdition.Local,
                LicenseFeatures.AllList,
                ExpiresAt: null,
                DaysRemaining: null,
                MachineMatched: true,
                Message: "Authorization smoke license state.",
                SourcePath: "authorization-smoke",
                ValidationTimestamp: DateTimeOffset.UtcNow,
                LicenseType: LicenseType.Host));
        }

        public Task<LocalLicenseInstallResponse> InstallAsync(
            LocalLicenseInstallRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InstallCalls++;
            return Task.FromResult(new LocalLicenseInstallResponse(
                "Rejected",
                LicenseStatus.Invalid,
                "authorization-smoke",
                BackupPath: null,
                "License.Install.ValidationRejected",
                "Authorization smoke reached signed-payload validation.",
                DateTimeOffset.UtcNow));
        }

        public Task<bool> HasFeatureAsync(string feature, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
