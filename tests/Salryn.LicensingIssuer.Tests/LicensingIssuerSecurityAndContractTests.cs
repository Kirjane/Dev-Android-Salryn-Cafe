using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Salryn.Core.Services.Licensing;
using Salryn.Infrastructure.Licensing;
using Salryn.LicensingIssuer.Server;
using Xunit;

namespace Salryn.LicensingIssuer.Tests;

public sealed class LicensingIssuerSecurityAndContractTests
{
    private const string AdminUser = "kirjane-admin";
    private const string AdminPass = "Synthetic-Issuer!2026#QA";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static string NewTempRoot(string tag)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"salryn-issuer-{tag}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void Cleanup(string root)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<(Microsoft.AspNetCore.Builder.WebApplication App, HttpClient Client, string DbPath)> StartHostAsync(
        string root,
        LicenseSignerOptions? signerOptions = null,
        int maxLoginAttempts = 8)
    {
        string dbPath = Path.Combine(root, "issuer.db");
        var app = LicensingIssuerAppFactory.Create([], new LicensingIssuerHostOptions
        {
            DatabasePath = dbPath,
            ListenUrl = "http://127.0.0.1:0",
            BootstrapAdminUsername = AdminUser,
            BootstrapAdminDisplayName = "Kirjane Labs QA Admin",
            BootstrapAdminPassword = AdminPass,
            SignerOptions = signerOptions ?? new LicenseSignerOptions(),
            MaxLoginAttemptsPerWindow = maxLoginAttempts
        });
        await app.StartAsync();
        var client = new HttpClient(new HttpClientHandler { UseCookies = true })
        {
            BaseAddress = new Uri(app.Urls.Single())
        };
        return (app, client, dbPath);
    }

    private static async Task<string> LoginAsync(HttpClient client)
    {
        using var resp = await client.PostAsJsonAsync("/api/issuer/auth/login", new { username = AdminUser, password = AdminPass });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var info = await resp.Content.ReadFromJsonAsync<IssuerSessionInfo>(JsonOpts);
        Assert.NotNull(info);
        Assert.True(info!.IsAuthenticated);
        Assert.False(string.IsNullOrWhiteSpace(info.CsrfToken));
        return info.CsrfToken!;
    }

    private static async Task<HttpResponseMessage> PostJsonWithCsrfAsync(HttpClient client, string path, object body, string? csrf)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        if (csrf is not null)
        {
            req.Headers.Add(IssuerConstants.CsrfHeaderName, csrf);
        }
        return await client.SendAsync(req);
    }

    [Fact]
    public async Task AnonymousRequestsDenied_WrongPasswordRateLimited_AndCsrfEnforced()
    {
        string root = NewTempRoot("auth-csrf");
        try
        {
            var (app, client, _) = await StartHostAsync(root, maxLoginAttempts: 3);
            await using (app)
            using (client)
            {
                // Public health works & carries security headers
                using var health = await client.GetAsync("/api/issuer/health");
                Assert.Equal(HttpStatusCode.OK, health.StatusCode);
                Assert.Equal("nosniff", health.Headers.GetValues("X-Content-Type-Options").Single());
                Assert.Equal("DENY", health.Headers.GetValues("X-Frame-Options").Single());
                Assert.False(health.Headers.Contains("Access-Control-Allow-Origin"));

                // Anonymous calls fail closed (401)
                foreach (string protectedPath in new[] { "/api/issuer/dashboard", "/api/issuer/customers", "/api/issuer/stores", "/api/issuer/requests", "/api/issuer/audit" })
                {
                    using var r = await client.GetAsync(protectedPath);
                    Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
                }

                // Wrong password triggers 401 then 429 rate limit
                for (int i = 0; i < 3; i++)
                {
                    using var bad = await client.PostAsJsonAsync("/api/issuer/auth/login", new { username = AdminUser, password = "wrong-password" });
                    Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
                }
                using var limited = await client.PostAsJsonAsync("/api/issuer/auth/login", new { username = AdminUser, password = "wrong-password" });
                Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
            }

            // Fresh host to test CSRF & logout
            var (app2, client2, _) = await StartHostAsync(Path.Combine(root, "csrf2"));
            await using (app2)
            using (client2)
            {
                string csrf = await LoginAsync(client2);

                // Missing CSRF header -> 403
                using var noCsrf = await PostJsonWithCsrfAsync(client2, "/api/issuer/customers", new { customerName = "Blocked Corp" }, csrf: null);
                Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);

                // Wrong CSRF header -> 403
                using var badCsrf = await PostJsonWithCsrfAsync(client2, "/api/issuer/customers", new { customerName = "Blocked Corp" }, csrf: "deadbeef");
                Assert.Equal(HttpStatusCode.Forbidden, badCsrf.StatusCode);

                // Valid CSRF header -> 201
                using var ok = await PostJsonWithCsrfAsync(client2, "/api/issuer/customers", new { customerName = "Cafe Yza Holdings" }, csrf);
                Assert.Equal(HttpStatusCode.Created, ok.StatusCode);

                // Logout invalidates session
                using var logout = await PostJsonWithCsrfAsync(client2, "/api/issuer/auth/logout", new { }, csrf);
                Assert.Equal(HttpStatusCode.OK, logout.StatusCode);

                using var afterLogout = await client2.GetAsync("/api/issuer/dashboard");
                Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
            }
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task UnconfiguredAndUntrustedSigners_FailClosedOnIssue_WhileDraftAndApprovalPersist()
    {
        string root = NewTempRoot("fail-closed");
        try
        {
            // 1. No key configured -> SIGNER_NOT_CONFIGURED (503) on issue
            var (app, client, _) = await StartHostAsync(root, new LicenseSignerOptions());
            await using (app)
            using (client)
            {
                string csrf = await LoginAsync(client);

                using var custResp = await PostJsonWithCsrfAsync(client, "/api/issuer/customers", new { customerName = "Cafe Yza" }, csrf);
                var customer = await custResp.Content.ReadFromJsonAsync<IssuerCustomerRecord>(JsonOpts);
                Assert.NotNull(customer);

                using var storeResp = await PostJsonWithCsrfAsync(client, "/api/issuer/stores", new
                {
                    customerId = customer!.Id,
                    storeCode = "YZA-MAIN-01",
                    storeName = "Cafe Yza Main Branch",
                    productId = IssuerConstants.ProductCafe
                }, csrf);
                var store = await storeResp.Content.ReadFromJsonAsync<IssuerStoreRecord>(JsonOpts);
                Assert.NotNull(store);

                using var draftResp = await PostJsonWithCsrfAsync(client, "/api/issuer/requests", new
                {
                    customerId = customer.Id,
                    storeId = store!.Id,
                    applicationId = IssuerConstants.ProductCafe,
                    licenseType = "Host",
                    edition = "Professional",
                    machineBinding = "SALRYN-MC-F36B-76CD-77FB-03EC",
                    features = LicenseFeatures.AllList,
                    allowedLanTerminals = 2,
                    expiresAtUtc = DateTimeOffset.UtcNow.AddDays(90),
                    idempotencyKey = "draft-failclosed-1"
                }, csrf);
                Assert.Equal(HttpStatusCode.Created, draftResp.StatusCode);
                using var draftDoc = JsonDocument.Parse(await draftResp.Content.ReadAsStringAsync());
                Guid reqId = draftDoc.RootElement.GetProperty("request").GetProperty("id").GetGuid();

                using var approveResp = await PostJsonWithCsrfAsync(client, $"/api/issuer/requests/{reqId:D}/approve", new { note = "Approved for signing test" }, csrf);
                Assert.Equal(HttpStatusCode.OK, approveResp.StatusCode);

                // Issuing without configured external key MUST fail closed (503 SIGNER_NOT_CONFIGURED)
                using var issueResp = await PostJsonWithCsrfAsync(client, $"/api/issuer/requests/{reqId:D}/issue", new { }, csrf);
                Assert.Equal(HttpStatusCode.ServiceUnavailable, issueResp.StatusCode);
                string issueBody = await issueResp.Content.ReadAsStringAsync();
                Assert.Contains("SIGNER_NOT_CONFIGURED", issueBody, StringComparison.Ordinal);

                // Download before issuance stays 409 Conflict
                using var dlResp = await client.GetAsync($"/api/issuer/requests/{reqId:D}/download");
                Assert.Equal(HttpStatusCode.Conflict, dlResp.StatusCode);

                // Audit log captured LicenseRequest.IssueBlocked
                var audits = await client.GetFromJsonAsync<IssuerAuditEntry[]>("/api/issuer/audit", JsonOpts);
                Assert.NotNull(audits);
                Assert.Contains(audits!, a => a.Action == "LicenseRequest.IssueBlocked");
            }

            // 2. Random RSA key WITHOUT AllowUntrustedTestSigner -> 503 SIGNER_KEY_UNTRUSTED
            using RSA untrustedRsa = RSA.Create(2048);
            string untrustedPem = untrustedRsa.ExportPkcs8PrivateKeyPem();
            var (app2, client2, _) = await StartHostAsync(Path.Combine(root, "untrusted-key"), new LicenseSignerOptions
            {
                SigningKeyPemInline = untrustedPem,
                AllowUntrustedTestSigner = false
            });
            await using (app2)
            using (client2)
            {
                string csrf = await LoginAsync(client2);
                var dash = await client2.GetFromJsonAsync<IssuerDashboardSummary>("/api/issuer/dashboard", JsonOpts);
                Assert.NotNull(dash);
                Assert.True(dash!.Signer.IsConfigured);
                Assert.False(dash.Signer.CanIssue);
                Assert.Equal("UntrustedKeyBlocked", dash.Signer.SignerMode);
            }
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task EndToEndIssuance_Reissue_OfflineVerification_ProductIsolation_AndZeroKeyLeakage()
    {
        string root = NewTempRoot("e2e-issue");
        try
        {
            using RSA testKey = RSA.Create(2048);
            string privateKeyPem = testKey.ExportPkcs8PrivateKeyPem();
            string publicKeyPem = testKey.ExportSubjectPublicKeyInfoPem();
            string keyFile = Path.Combine(root, "ephemeral-test-key.pem");
            await File.WriteAllTextAsync(keyFile, privateKeyPem);

            var (app, client, dbPath) = await StartHostAsync(root, new LicenseSignerOptions
            {
                SigningKeyPemPath = keyFile,
                AllowUntrustedTestSigner = true
            });
            await using (app)
            using (client)
            {
                string csrf = await LoginAsync(client);

                // Verify signer status honestly reports TestUntrustedKey
                var dash = await client.GetFromJsonAsync<IssuerDashboardSummary>("/api/issuer/dashboard", JsonOpts);
                Assert.NotNull(dash);
                Assert.True(dash!.Signer.CanIssue);
                Assert.Equal("TestUntrustedKey", dash.Signer.SignerMode);
                Assert.False(dash.Signer.MatchesTrustedRuntimeKey);

                // Register customer & Salryn.Cafe store
                using var custResp = await PostJsonWithCsrfAsync(client, "/api/issuer/customers", new
                {
                    customerName = "Cafe Yza Co.",
                    contactEmail = "ops@cafeyza.example",
                    notes = "First F&B Cafe deployment"
                }, csrf);
                var customer = await custResp.Content.ReadFromJsonAsync<IssuerCustomerRecord>(JsonOpts);

                using var storeResp = await PostJsonWithCsrfAsync(client, "/api/issuer/stores", new
                {
                    customerId = customer!.Id,
                    storeCode = "CAFE-YZA-01",
                    storeName = "Cafe Yza Flagship",
                    productId = IssuerConstants.ProductCafe
                }, csrf);
                var store = await storeResp.Content.ReadFromJsonAsync<IssuerStoreRecord>(JsonOpts);

                // Cross-product draft (Salryn.POS against a Salryn.Cafe store) MUST fail 400
                using var crossProd = await PostJsonWithCsrfAsync(client, "/api/issuer/requests", new
                {
                    customerId = customer.Id,
                    storeId = store!.Id,
                    applicationId = IssuerConstants.ProductRetail,
                    licenseType = "Host",
                    edition = "Professional",
                    machineBinding = "SALRYN-MC-1111-2222-3333-4444",
                    features = LicenseFeatures.AllList,
                    allowedLanTerminals = 2
                }, csrf);
                Assert.Equal(HttpStatusCode.BadRequest, crossProd.StatusCode);

                // Terminal draft requesting host-only feature MUST fail 400
                using var badTerm = await PostJsonWithCsrfAsync(client, "/api/issuer/requests", new
                {
                    customerId = customer.Id,
                    storeId = store.Id,
                    applicationId = IssuerConstants.ProductCafe,
                    licenseType = "Terminal",
                    edition = "Professional",
                    machineBinding = "SALRYN-MC-TERM-0001",
                    features = new[] { LicenseFeatures.LanTerminals, LicenseFeatures.Backup },
                    hostLicenseId = "SLRN-CAFE-HOST-1",
                    terminalId = "TERM-01"
                }, csrf);
                Assert.Equal(HttpStatusCode.BadRequest, badTerm.StatusCode);

                // Valid Host draft + idempotent replay
                string machineCode = "SALRYN-MC-CAFE-HOST-01";
                var draftPayload = new
                {
                    customerId = customer.Id,
                    storeId = store.Id,
                    applicationId = IssuerConstants.ProductCafe,
                    licenseType = "Host",
                    edition = "Professional",
                    machineBinding = machineCode,
                    features = LicenseFeatures.AllList,
                    allowedLanTerminals = 3,
                    expiresAtUtc = DateTimeOffset.UtcNow.AddDays(365),
                    idempotencyKey = "idem-host-001"
                };

                using var draft1 = await PostJsonWithCsrfAsync(client, "/api/issuer/requests", draftPayload, csrf);
                Assert.Equal(HttpStatusCode.Created, draft1.StatusCode);
                using var d1Json = JsonDocument.Parse(await draft1.Content.ReadAsStringAsync());
                Guid reqId = d1Json.RootElement.GetProperty("request").GetProperty("id").GetGuid();

                using var draftReplay = await PostJsonWithCsrfAsync(client, "/api/issuer/requests", draftPayload, csrf);
                Assert.Equal(HttpStatusCode.OK, draftReplay.StatusCode);
                using var d2Json = JsonDocument.Parse(await draftReplay.Content.ReadAsStringAsync());
                Assert.Equal(reqId, d2Json.RootElement.GetProperty("request").GetProperty("id").GetGuid());
                Assert.True(d2Json.RootElement.GetProperty("isReplay").GetBoolean());

                // Issuing before approval -> 409 Conflict
                using var issueBeforeApprove = await PostJsonWithCsrfAsync(client, $"/api/issuer/requests/{reqId:D}/issue", new { }, csrf);
                Assert.Equal(HttpStatusCode.Conflict, issueBeforeApprove.StatusCode);

                // Approve -> Issue -> Idempotent Re-Issue
                using var approve = await PostJsonWithCsrfAsync(client, $"/api/issuer/requests/{reqId:D}/approve", new { note = "Verified customer machine code" }, csrf);
                Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

                using var issue1 = await PostJsonWithCsrfAsync(client, $"/api/issuer/requests/{reqId:D}/issue", new { }, csrf);
                Assert.Equal(HttpStatusCode.OK, issue1.StatusCode);
                using var issue1Doc = JsonDocument.Parse(await issue1.Content.ReadAsStringAsync());
                Assert.False(issue1Doc.RootElement.GetProperty("alreadyIssued").GetBoolean());

                using var issue2 = await PostJsonWithCsrfAsync(client, $"/api/issuer/requests/{reqId:D}/issue", new { }, csrf);
                Assert.Equal(HttpStatusCode.OK, issue2.StatusCode);
                using var issue2Doc = JsonDocument.Parse(await issue2.Content.ReadAsStringAsync());
                Assert.True(issue2Doc.RootElement.GetProperty("alreadyIssued").GetBoolean());

                // Download signed offline license JSON and verify signature + fields independently
                using var dl = await client.GetAsync($"/api/issuer/requests/{reqId:D}/download");
                Assert.Equal(HttpStatusCode.OK, dl.StatusCode);
                string downloadedJson = await dl.Content.ReadAsStringAsync();
                string downloadedSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(downloadedJson))).ToLowerInvariant();
                Assert.Equal(downloadedSha, dl.Headers.GetValues("X-License-Sha256").Single());

                LocalLicenseDocument? parsedLic = JsonSerializer.Deserialize<LocalLicenseDocument>(downloadedJson, JsonOpts);
                Assert.NotNull(parsedLic);
                Assert.Equal(IssuerConstants.ProductCafe, parsedLic!.ApplicationId);
                Assert.Equal(LicenseType.Host, parsedLic.LicenseType);
                Assert.Equal(machineCode, parsedLic.MachineBinding);
                Assert.Equal(3, parsedLic.AllowedLanTerminals);
                Assert.Null(parsedLic.HostLicenseId);
                Assert.Null(parsedLic.TerminalId);
                Assert.True(ExternalLicenseSignerService.VerifyWithSpecificPublicKey(parsedLic, publicKeyPem));

                // Reissue for replacement machine -> approve & issue -> prior becomes Superseded
                using var reissueDraft = await PostJsonWithCsrfAsync(client, $"/api/issuer/requests/{reqId:D}/reissue", new
                {
                    machineBinding = "SALRYN-MC-CAFE-HOST-02",
                    note = "Hardware replacement"
                }, csrf);
                Assert.Equal(HttpStatusCode.Created, reissueDraft.StatusCode);
                using var reissueDoc = JsonDocument.Parse(await reissueDraft.Content.ReadAsStringAsync());
                Guid reissueId = reissueDoc.RootElement.GetProperty("request").GetProperty("id").GetGuid();

                using var approve2 = await PostJsonWithCsrfAsync(client, $"/api/issuer/requests/{reissueId:D}/approve", new { note = "Approved replacement" }, csrf);
                Assert.Equal(HttpStatusCode.OK, approve2.StatusCode);

                using var issueReplacement = await PostJsonWithCsrfAsync(client, $"/api/issuer/requests/{reissueId:D}/issue", new { }, csrf);
                Assert.Equal(HttpStatusCode.OK, issueReplacement.StatusCode);

                var priorAfter = await client.GetFromJsonAsync<IssuerLicenseRequestRecord>($"/api/issuer/requests/{reqId:D}", JsonOpts);
                Assert.Equal(IssuerConstants.StatusSuperseded, priorAfter!.Status);

                // Secret hygiene check: private key material MUST NEVER appear in API responses, audit log, or SQLite DB
                string auditPayload = await client.GetStringAsync("/api/issuer/audit");
                string requestsPayload = await client.GetStringAsync("/api/issuer/requests");
                Assert.DoesNotContain("PRIVATE KEY", auditPayload, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("PRIVATE KEY", requestsPayload, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("PRIVATE KEY", downloadedJson, StringComparison.OrdinalIgnoreCase);
            }

            // Read raw SQLite DB bytes after host stops and confirm zero private key bytes persisted
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            byte[] dbBytes = await File.ReadAllBytesAsync(dbPath);
            string dbText = Encoding.Latin1.GetString(dbBytes);
            Assert.DoesNotContain("BEGIN PRIVATE KEY", dbText, StringComparison.Ordinal);
            Assert.DoesNotContain("BEGIN RSA PRIVATE KEY", dbText, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(root);
        }
    }
}
