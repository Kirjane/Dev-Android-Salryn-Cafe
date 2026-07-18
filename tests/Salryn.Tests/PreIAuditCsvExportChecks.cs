using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Salryn.Api.Local;
using Salryn.Api.Local.Auth;
using Salryn.Api.Local.Services;
using Salryn.Composition.Local;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.Security;
using Salryn.Infrastructure.Data.Repositories;
using Salryn.Infrastructure.Data.Repositories.System;

namespace Salryn.Tests;

/// <summary>
/// PRE-I: complete filtered audit CSV export (server-side, not visible page only).
/// </summary>
public static class PreIAuditCsvExportChecks
{
    private const string GateName = "pre-i-audit-csv-export";
    private const string SessionCookieName = LocalApiSessionService.SessionCookieName;
    private const string AuditView = "Audit.View";

    public static async Task RunAsync()
    {
        Console.WriteLine($"=== {GateName} ===");
        string root = LocateRepoRoot();
        AssertSourceContracts(root);
        AssertCsvEscaping();
        AssertSecretSanitization();
        await AssertApiExportBehaviorAsync().ConfigureAwait(false);
        Console.WriteLine($"PASS {GateName}");
    }

    private static void AssertSourceContracts(string root)
    {
        string endpoint = File.ReadAllText(Path.Combine(root, "src", "Salryn.Api.Local", "ReadOnlyEndpointExtensions.cs"));
        string repoIface = File.ReadAllText(Path.Combine(root, "src", "Salryn.Core", "Repositories", "ReadRepositoryInterfaces.cs"));
        string formatter = File.ReadAllText(Path.Combine(root, "src", "Salryn.Api.Local", "Services", "AuditCsvFormatter.cs"));
        string sqlite = File.ReadAllText(Path.Combine(root, "src", "Salryn.Infrastructure", "Data", "Repositories", "System", "SqliteLogReadRepositories.cs"));
        string auditPage = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "pages", "AuditPage.tsx"));
        string auditApi = File.ReadAllText(Path.Combine(root, "src", "Salryn.App.Web", "src", "api", "audit.ts"));
        string coreSmoke = File.ReadAllText(Path.Combine(root, "tools", "Salryn.Tools.CoreSmoke", "Program.cs"));

        RequireContains(endpoint, "MapGet(\"/api/audit/logs/export\"", "Export endpoint must be registered.");
        RequireContains(endpoint, "LocalApiPermissions.AuditView", "Export must require Audit.View.");
        RequireContains(endpoint, "fileDownloadName", "Export must set download file name.");
        RequireContains(endpoint, "X-Salryn-Audit-Export-Truncated", "Export must disclose truncation.");
        RequireContains(endpoint, "X-Salryn-Audit-Export-Max-Rows", "Export must disclose max rows.");
        RequireContains(endpoint, "X-Salryn-Audit-Export-Count", "Export must disclose exported count.");
        RequireContains(endpoint, "X-Salryn-Audit-Export-Total-Matching", "Export must disclose total matching.");

        RequireContains(repoIface, "ExportAuditLogsAsync", "Repository must expose export method.");
        RequireContains(formatter, "\"Date/Time\"", "Date/Time header required.");
        RequireContains(formatter, "\"User\"", "User header required.");
        RequireContains(formatter, "\"Action\"", "Action header required.");
        RequireContains(formatter, "\"Entity Type\"", "Entity Type header required.");
        RequireContains(formatter, "\"Entity ID\"", "Entity ID header required.");
        RequireContains(formatter, "\"Reason\"", "Reason header required.");
        RequireContains(formatter, "\"Details\"", "Details header required.");
        RequireContains(formatter, "public static string Escape", "CSV escaping helper required.");
        RequireContains(formatter, "ExportMaxRows", "Export max rows constant required.");
        RequireContains(formatter, "salryn-audit-", "Safe filename prefix required.");
        RequireContains(sqlite, "SanitizeAuditDetails", "Export must sanitize details.");
        RequireContains(sqlite, "ORDER BY a.created_at DESC, a.id DESC", "Export ordering must be newest-first stable.");
        RequireContains(sqlite, "DefaultExportMaxRows = 25000", "Default export row cap required.");

        RequireContains(auditPage, "Download audit CSV", "Audit page must have export button.");
        RequireContains(auditPage, "not only the visible page", "UI must disclose filtered-trail export.");
        RequireContains(auditPage, "25000", "UI must disclose export row cap.");
        RequireContains(auditPage, "Audit CSV downloaded", "Success notification required.");
        RequireContains(auditPage, "Audit export failed", "Failure notification required.");
        RequireContains(auditPage, "downloadAuditCsv", "Page must call downloadAuditCsv.");
        RequireContains(auditPage, "revokeObjectURL", "Blob object URLs must be revoked.");
        RequireContains(auditApi, "downloadAuditCsv", "Frontend API must expose downloadAuditCsv.");
        RequireContains(auditApi, "/api/audit/logs/export", "Frontend must hit export route.");

        RequireContains(coreSmoke, "PreIAuditCsvExportChecks.RunAsync", "CoreSmoke must wire CSV export gate.");
        RequireContains(coreSmoke, "pre-i-audit-csv-export", "CoreSmoke --only gate name.");

        Require(!endpoint.Contains("/api/error-logs/export", StringComparison.Ordinal)
            && !endpoint.Contains("/api/audit/errors/export", StringComparison.Ordinal),
            "Error-log CSV export is out of scope for this gate.");
    }

    private static void AssertCsvEscaping()
    {
        var rows = new List<AuditLogExportRow>
        {
            new(
                new DateTimeOffset(2026, 7, 18, 10, 30, 0, TimeSpan.Zero),
                "alice,admin",
                "SALE_COMPLETED",
                "Sale",
                "sale-1",
                "reason \"quoted\"",
                "line1\nline2"),
            new(
                new DateTimeOffset(2026, 7, 18, 9, 0, 0, TimeSpan.Zero),
                null,
                "PRODUCT_CREATED",
                "Product",
                null,
                null,
                null),
        };

        byte[] bytes = AuditCsvFormatter.BuildCsv(rows);
        string text = Encoding.UTF8.GetString(bytes);
        Require(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
            "CSV must start with UTF-8 BOM for Excel.");
        Require(text.Contains("Date/Time,User,Action,Entity Type,Entity ID,Reason,Details", StringComparison.Ordinal),
            "Header row required.");
        Require(text.Contains("\"alice,admin\"", StringComparison.Ordinal), "Commas in fields must be quoted.");
        Require(text.Contains("\"reason \"\"quoted\"\"\"", StringComparison.Ordinal), "Quotes must be doubled.");
        Require(text.Contains("\"line1\nline2\"", StringComparison.Ordinal), "Newlines must be enclosed in quotes.");
        Require(text.Contains("PRODUCT_CREATED", StringComparison.Ordinal), "Empty optional fields must still produce a row.");

        string fileName = AuditCsvFormatter.BuildFileName(new DateTimeOffset(2026, 7, 18, 14, 5, 9, TimeSpan.FromHours(8)));
        Require(fileName.StartsWith("salryn-audit-", StringComparison.Ordinal), "Filename prefix required.");
        Require(fileName.EndsWith(".csv", StringComparison.Ordinal), "Filename extension required.");
        Require(!fileName.Contains(' ') && !fileName.Contains('/') && !fileName.Contains('\\'),
            "Filename must be path-safe.");
    }

    private static void AssertSecretSanitization()
    {
        string? passwordy = SqliteAuditReadRepository.SanitizeAuditDetails(
            """{"passwordHash":"abc","ok":true}""");
        Require(passwordy is null, "passwordHash payloads must be blanked.");

        string? license = SqliteAuditReadRepository.SanitizeAuditDetails(
            """{"signedLicenseJson":"{\"sig\":\"SECRET_LICENSE\"}","action":"activate"}""");
        Require(license is null, "Signed license JSON must not export.");

        string? key = SqliteAuditReadRepository.SanitizeAuditDetails(
            "privateKey=-----BEGIN PRIVATE KEY-----\nMII\n-----END PRIVATE KEY-----");
        Require(key is null, "Private keys must not export.");

        string? stack = SqliteAuditReadRepository.SanitizeAuditDetails(
            "at Salryn.Foo.Bar() in C:\\src\\Foo.cs:line 12\nException: boom");
        Require(stack is null, "Stack traces must not export in audit CSV details.");

        string? backup = SqliteAuditReadRepository.SanitizeAuditDetails(
            """{"backupBytes":"AAAA","backupContents":"db-blob"}""");
        Require(backup is null, "Backup contents must not export raw.");

        string? safe = SqliteAuditReadRepository.SanitizeAuditDetails("""{"productName":"Soap","qty":2}""");
        Require(safe is not null && safe.Contains("Soap", StringComparison.Ordinal), "Safe metadata may export.");
    }

    private static async Task AssertApiExportBehaviorAsync()
    {
        string isolatedRoot = Path.Combine(Path.GetTempPath(), "salryn-audit-csv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(isolatedRoot);

        SalrynLocalComposition composition = SalrynLocalComposition.CreateDevelopment(isolatedRoot);
        await composition.InitializeDatabaseAsync().ConfigureAwait(false);

        IAuditWriteRepository auditWrite = new SqliteRepositoryFactory(composition.ConnectionFactory)
            .CreateAuditWriteRepository();

        DateTimeOffset now = DateTimeOffset.UtcNow;

        for (int i = 0; i < 60; i++)
        {
            Result append = await auditWrite.AddAsync(new AuditLog
            {
                Id = Guid.NewGuid(),
                UserId = null,
                Action = i % 2 == 0 ? "SALE_COMPLETED" : "PRODUCT_CREATED",
                EntityType = "Entity",
                EntityId = $"entity-{i}",
                Reason = i % 3 == 0 ? "match-export-token" : "other-reason",
                NewValueJson = """{"note":"safe-meta"}""",
                CreatedAt = now.AddMinutes(-i),
            }).ConfigureAwait(false);
            Require(append.IsSuccess,
                "Seed audit row failed: " + (append.ErrorDetail?.Message ?? append.ErrorDetail?.Code ?? "unknown"));
        }

        Result secretRow = await auditWrite.AddAsync(new AuditLog
        {
            Id = Guid.NewGuid(),
            UserId = null,
            Action = "USER_LOGIN",
            EntityType = "User",
            EntityId = "login-probe",
            Reason = "login probe",
            NewValueJson = """{"passwordHash":"super-secret-hash","token":"session-token-xyz"}""",
            CreatedAt = now.AddMinutes(-100),
        }).ConfigureAwait(false);
        Require(secretRow.IsSuccess,
            "Seed secret audit row failed: " + (secretRow.ErrorDetail?.Message ?? secretRow.ErrorDetail?.Code ?? "unknown"));

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
                    WriteDevelopmentDatabasePath = false,
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
                "/api/audit/logs/export",
                HttpStatusCode.Unauthorized,
                "Unauthenticated export must return 401.").ConfigureAwait(false);

            LocalApiSession noPerm = CreateSession(sessionStore, "Cashier", Array.Empty<string>());
            using HttpClient forbidden = CreateClient(baseAddress, noPerm);
            await AssertStatusAsync(
                forbidden,
                HttpMethod.Get,
                "/api/audit/logs/export",
                HttpStatusCode.Forbidden,
                "Session without Audit.View must return 403.").ConfigureAwait(false);

            LocalApiSession admin = CreateSession(sessionStore, "Admin", new[] { AuditView });
            using HttpClient client = CreateClient(baseAddress, admin);

            using HttpResponseMessage full = await client.GetAsync("/api/audit/logs/export").ConfigureAwait(false);
            Require(full.IsSuccessStatusCode, $"Authorized export failed: {(int)full.StatusCode}");
            string fullCsv = await full.Content.ReadAsStringAsync().ConfigureAwait(false);
            Require(fullCsv.Contains("Date/Time,User,Action", StringComparison.Ordinal), "Headers missing.");
            Require(fullCsv.Contains("SALE_COMPLETED", StringComparison.Ordinal), "Expected sale audit rows.");
            int saleCount = CountOccurrences(fullCsv, "SALE_COMPLETED");
            Require(saleCount >= 25, "Export must include more than one visible page of matching rows.");
            Require(!fullCsv.Contains("super-secret-hash", StringComparison.Ordinal), "passwordHash must not appear.");
            Require(!fullCsv.Contains("session-token-xyz", StringComparison.Ordinal), "session token must not appear.");

            string? disposition = full.Content.Headers.ContentDisposition?.FileName
                ?? full.Content.Headers.ContentDisposition?.FileNameStar;
            if (string.IsNullOrWhiteSpace(disposition)
                && full.Headers.TryGetValues("Content-Disposition", out IEnumerable<string>? values))
            {
                disposition = values.FirstOrDefault();
            }

            Require(
                disposition is not null && disposition.Contains("salryn-audit-", StringComparison.OrdinalIgnoreCase),
                "Content-Disposition filename must be salryn-audit-*.csv");

            Require(full.Headers.Contains("X-Salryn-Audit-Export-Max-Rows"), "Max-rows header required.");
            Require(full.Headers.Contains("X-Salryn-Audit-Export-Count"), "Count header required.");
            Require(full.Headers.Contains("X-Salryn-Audit-Export-Total-Matching"), "Total-matching header required.");
            Require(full.Headers.Contains("X-Salryn-Audit-Export-Truncated"), "Truncated header required.");

            using HttpResponseMessage filtered = await client
                .GetAsync("/api/audit/logs/export?search=match-export-token")
                .ConfigureAwait(false);
            Require(filtered.IsSuccessStatusCode, "Filtered export failed.");
            string filteredCsv = await filtered.Content.ReadAsStringAsync().ConfigureAwait(false);
            Require(filteredCsv.Contains("match-export-token", StringComparison.Ordinal), "Search filter must include matches.");
            int matchCount = CountOccurrences(filteredCsv, "match-export-token");
            Require(matchCount >= 15, "Search filter should return multiple matching reason rows.");
            Require(!filteredCsv.Contains("other-reason", StringComparison.Ordinal),
                "Search filter must not include non-matching reasons.");

            DateTimeOffset from = now.AddMinutes(-5);
            string fromQ = Uri.EscapeDataString(from.ToString("o"));
            using HttpResponseMessage dated = await client
                .GetAsync($"/api/audit/logs/export?from={fromQ}")
                .ConfigureAwait(false);
            Require(dated.IsSuccessStatusCode, "Date-filtered export failed.");
            string datedCsv = await dated.Content.ReadAsStringAsync().ConfigureAwait(false);
            int datedDataRows = datedCsv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length - 1;
            Require(datedDataRows > 0 && datedDataRows < 60, "Date filter must reduce export set.");

            // Service-level completeness vs paged list (page size 25).
            OperatorReadService reads = new(composition);
            Result<AuditCsvExportResult> exportResult = await reads
                .ExportAuditLogsCsvAsync(null, null, null)
                .ConfigureAwait(false);
            Require(exportResult.IsSuccess, "Service export failed.");
            Require(exportResult.Value.ExportedRowCount > 25,
                "Service export must exceed one default UI page.");
            Require(exportResult.Value.ExportedRowCount == exportResult.Value.TotalMatchingCount
                || exportResult.Value.Truncated,
                "Truncation must be explicit when counts differ.");
        }
        finally
        {
            await app.StopAsync().ConfigureAwait(false);
        }
    }

    private static LocalApiSession CreateSession(
        InMemoryLocalApiSessionStore sessionStore,
        string roleName,
        IReadOnlyList<string> permissions)
    {
        AuthenticatedUserReadModel user = new(
            Guid.NewGuid(),
            "audit-csv-export",
            "Audit CSV Export",
            roleName,
            UserStatus.Active,
            permissions)
        {
            RoleId = Guid.NewGuid(),
        };

        return sessionStore.Create(user);
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
                $"{GateName}: {message} Expected {(int)expectedStatus}, got {(int)response.StatusCode}.");
        }
    }

    private static Uri ResolveStartedBaseAddress(WebApplication app)
    {
        string? address = app.Urls.FirstOrDefault(item =>
            item.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(address))
        {
            address = app.Urls.FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(address))
        {
            throw new InvalidOperationException($"{GateName}: Could not resolve started base address.");
        }

        return new Uri(address.TrimEnd('/') + "/");
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int count = 0;
        int idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += needle.Length;
        }

        return count;
    }

    private static string LocateRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Salryn.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate Salryn.sln from test base directory.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"{GateName}: {message}");
        }
    }

    private static void RequireContains(string haystack, string needle, string message)
    {
        if (!haystack.Contains(needle, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{GateName}: {message} Missing: {needle}");
        }
    }
}
