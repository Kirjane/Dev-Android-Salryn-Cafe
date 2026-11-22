using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Salryn.Core.Services.Licensing;
using Salryn.Infrastructure.Licensing;
using Salryn.LicensingIssuer.Server;
using Xunit;

namespace Salryn.LicensingIssuer.Tests;

public sealed class LicensingIssuerSecurityAndContractTests
{
    private const string AdminUser = "kirjane-admin";
    private const string AdminPass = "Synthetic-Issuer!2026#Admin";
    private const string ReviewerUser = "kirjane-reviewer";
    private const string ReviewerPass = "Synthetic-Issuer!2026#Review";
    private const string OperatorUser = "kirjane-operator";
    private const string OperatorPass = "Synthetic-Issuer!2026#Operate";

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
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<(Microsoft.AspNetCore.Builder.WebApplication App, HttpClient Client, string DbPath)> StartHostAsync(
        string root,
        LicenseSignerOptions? signerOptions = null,
        int maxLoginAttempts = 8,
        bool allowSelfApprovalException = false)
    {
        string dbPath = Path.Combine(root, "issuer.db");
        var app = LicensingIssuerAppFactory.Create([], new LicensingIssuerHostOptions
        {
            DatabasePath = dbPath,
            ListenUrl = "http://127.0.0.1:0",
            BootstrapAdminUsername = AdminUser,
            BootstrapAdminDisplayName = "Kirjane Labs QA Admin",
            BootstrapAdminPassword = AdminPass,
            BootstrapReviewerUsername = ReviewerUser,
            BootstrapReviewerDisplayName = "Kirjane Labs QA Reviewer",
            BootstrapReviewerPassword = ReviewerPass,
            BootstrapOperatorUsername = OperatorUser,
            BootstrapOperatorDisplayName = "Kirjane Labs QA Operator",
            BootstrapOperatorPassword = OperatorPass,
            SignerOptions = signerOptions ?? new LicenseSignerOptions(),
            MaxLoginAttemptsPerWindow = maxLoginAttempts,
            AllowSelfApprovalException = allowSelfApprovalException
        });
        await app.StartAsync();
        var client = NewCookieClient(app);
        return (app, client, dbPath);
    }

    private static HttpClient NewCookieClient(Microsoft.AspNetCore.Builder.WebApplication app) =>
        new(new HttpClientHandler { UseCookies = true })
        {
            BaseAddress = new Uri(app.Urls.Single())
        };

    private static async Task<string> LoginAsync(HttpClient client, string username = AdminUser, string password = AdminPass)
    {
        using var resp = await client.PostAsJsonAsync("/api/issuer/auth/login", new { username, password });
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

    private static async Task<(IssuerCustomerRecord Customer, IssuerStoreRecord Store)> SeedCustomerAndStoreAsync(
        HttpClient client,
        string csrf,
        string productId = IssuerConstants.ProductCafe,
        string storeCode = "CAFE-YZA-01")
    {
        using var custResp = await PostJsonWithCsrfAsync(client, "/api/issuer/customers", new
        {
            customerName = "Cafe Yza Holdings",
            contactEmail = "ops@cafeyza.example",
            notes = "QA seed"
        }, csrf);
        Assert.Equal(HttpStatusCode.Created, custResp.StatusCode);
        var customer = (await custResp.Content.ReadFromJsonAsync<IssuerCustomerRecord>(JsonOpts))!;

        using var storeResp = await PostJsonWithCsrfAsync(client, "/api/issuer/stores", new
        {
            customerId = customer.Id,
            storeCode,
            storeName = $"Store {storeCode}",
            productId
        }, csrf);
        Assert.Equal(HttpStatusCode.Created, storeResp.StatusCode);
        var store = (await storeResp.Content.ReadFromJsonAsync<IssuerStoreRecord>(JsonOpts))!;
        return (customer, store);
    }

    [Fact]
    public async Task T01_AnonymousRequestsDenied_SecurityHeadersApplied_AndNoOpenCors()
    {
        string root = NewTempRoot("t01-anon");
        try
        {
            var (app, client, _) = await StartHostAsync(root);
            await using (app)
            using (client)
            {
                using var health = await client.GetAsync("/api/issuer/health");
                Assert.Equal(HttpStatusCode.OK, health.StatusCode);
                Assert.Equal("nosniff", health.Headers.GetValues("X-Content-Type-Options").Single());
                Assert.Equal("DENY", health.Headers.GetValues("X-Frame-Options").Single());
                Assert.Equal("no-referrer", health.Headers.GetValues("Referrer-Policy").Single());
                Assert.Contains("default-src 'self'", health.Headers.GetValues("Content-Security-Policy").Single(), StringComparison.Ordinal);
                Assert.False(health.Headers.Contains("Access-Control-Allow-Origin"));

                foreach (string protectedGet in new[]
                {
                    "/api/issuer/dashboard",
                    "/api/issuer/metadata",
                    "/api/issuer/customers",
                    "/api/issuer/stores",
                    "/api/issuer/requests",
                    "/api/issuer/audit",
                    "/api/issuer/audit/verify",
                    "/api/issuer/users"
                })
                {
                    using var r = await client.GetAsync(protectedGet);
                    Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
                }

                using var anonPost = await client.PostAsJsonAsync("/api/issuer/customers", new { customerName = "Anon" });
                Assert.Equal(HttpStatusCode.Unauthorized, anonPost.StatusCode);
            }
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task T02_WrongPasswordRateLimited_AndLogoutRevokesSession()
    {
        string root = NewTempRoot("t02-rate");
        try
        {
            var (app, client, _) = await StartHostAsync(root, maxLoginAttempts: 3);
            await using (app)
            using (client)
            {
                for (int i = 0; i < 3; i++)
                {
                    using var bad = await client.PostAsJsonAsync("/api/issuer/auth/login", new { username = AdminUser, password = "wrong" });
                    Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
                }
                using var limited = await client.PostAsJsonAsync("/api/issuer/auth/login", new { username = AdminUser, password = "wrong" });
                Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);

                // Separate user account is not locked out and can sign in & sign out cleanly
                string revCsrf = await LoginAsync(client, ReviewerUser, ReviewerPass);
                using var logout = await PostJsonWithCsrfAsync(client, "/api/issuer/auth/logout", new { }, revCsrf);
                Assert.Equal(HttpStatusCode.OK, logout.StatusCode);

                using var afterLogout = await client.GetAsync("/api/issuer/dashboard");
                Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
            }
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task T03_CsrfEnforcedOnAllMutatingEndpoints()
    {
        string root = NewTempRoot("t03-csrf");
        try
        {
            var (app, client, _) = await StartHostAsync(root);
            await using (app)
            using (client)
            {
                string csrf = await LoginAsync(client);

                foreach (string? invalidCsrf in new string?[] { null, "", "forged-csrf-token" })
                {
                    using var r1 = await PostJsonWithCsrfAsync(client, "/api/issuer/customers", new { customerName = "X" }, invalidCsrf);
                    Assert.Equal(HttpStatusCode.Forbidden, r1.StatusCode);

                    using var r2 = await PostJsonWithCsrfAsync(client, "/api/issuer/users", new
                    {
                        username = "u2",
                        displayName = "U2",
                        password = "StrongPassword!2026",
                        role = IssuerConstants.RoleOperator
                    }, invalidCsrf);
                    Assert.Equal(HttpStatusCode.Forbidden, r2.StatusCode);
                }

                using var valid = await PostJsonWithCsrfAsync(client, "/api/issuer/customers", new { customerName = "Valid Csrf Corp" }, csrf);
                Assert.Equal(HttpStatusCode.Created, valid.StatusCode);
            }
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task T04_RoleAuthorization_SeparatesOperatorReviewerAndAdminPermissions()
    {
        string root = NewTempRoot("t04-rbac");
        try
        {
            var (app, adminClient, _) = await StartHostAsync(root);
            await using (app)
            using (adminClient)
            using (var operatorClient = NewCookieClient(app))
            using (var reviewerClient = NewCookieClient(app))
            {
                string adminCsrf = await LoginAsync(adminClient, AdminUser, AdminPass);
                string operatorCsrf = await LoginAsync(operatorClient, OperatorUser, OperatorPass);
                string reviewerCsrf = await LoginAsync(reviewerClient, ReviewerUser, ReviewerPass);

                // Operator CAN register customer, store, and draft
                var (customer, store) = await SeedCustomerAndStoreAsync(operatorClient, operatorCsrf);
                using var draftResp = await PostJsonWithCsrfAsync(operatorClient, "/api/issuer/requests", new
                {
                    customerId = customer.Id,
                    storeId = store.Id,
                    applicationId = IssuerConstants.ProductCafe,
                    licenseType = "Host",
                    edition = "Professional",
                    machineBinding = "SALRYN-MC-RBAC-0001",
                    features = LicenseFeatures.AllList,
                    allowedLanTerminals = 1
                }, operatorCsrf);
                Assert.Equal(HttpStatusCode.Created, draftResp.StatusCode);
                using var draftDoc = JsonDocument.Parse(await draftResp.Content.ReadAsStringAsync());
                Guid reqId = draftDoc.RootElement.GetProperty("request").GetProperty("id").GetGuid();

                // Operator CANNOT approve, reject, issue, read audit, or manage users (403 Issuer.ForbiddenRole)
                using var opApprove = await PostJsonWithCsrfAsync(operatorClient, $"/api/issuer/requests/{reqId:D}/approve", new { note = "try" }, operatorCsrf);
                Assert.Equal(HttpStatusCode.Forbidden, opApprove.StatusCode);
                Assert.Contains("Issuer.ForbiddenRole", await opApprove.Content.ReadAsStringAsync(), StringComparison.Ordinal);

                using var opIssue = await PostJsonWithCsrfAsync(operatorClient, $"/api/issuer/requests/{reqId:D}/issue", new { }, operatorCsrf);
                Assert.Equal(HttpStatusCode.Forbidden, opIssue.StatusCode);

                using var opAudit = await operatorClient.GetAsync("/api/issuer/audit");
                Assert.Equal(HttpStatusCode.Forbidden, opAudit.StatusCode);

                using var opUsers = await operatorClient.GetAsync("/api/issuer/users");
                Assert.Equal(HttpStatusCode.Forbidden, opUsers.StatusCode);

                // Reviewer CANNOT create customers/stores/drafts or manage users or issue (403), but CAN read audit & approve/reject
                using var revCreateCust = await PostJsonWithCsrfAsync(reviewerClient, "/api/issuer/customers", new { customerName = "Denied" }, reviewerCsrf);
                Assert.Equal(HttpStatusCode.Forbidden, revCreateCust.StatusCode);

                using var revUsers = await reviewerClient.GetAsync("/api/issuer/users");
                Assert.Equal(HttpStatusCode.Forbidden, revUsers.StatusCode);

                using var revAudit = await reviewerClient.GetAsync("/api/issuer/audit");
                Assert.Equal(HttpStatusCode.OK, revAudit.StatusCode);

                using var revApprove = await PostJsonWithCsrfAsync(reviewerClient, $"/api/issuer/requests/{reqId:D}/approve", new { note = "Independent reviewer approval" }, reviewerCsrf);
                Assert.Equal(HttpStatusCode.OK, revApprove.StatusCode);

                using var revIssue = await PostJsonWithCsrfAsync(reviewerClient, $"/api/issuer/requests/{reqId:D}/issue", new { }, reviewerCsrf);
                Assert.Equal(HttpStatusCode.Forbidden, revIssue.StatusCode);

                // Admin CAN provision new users and list users
                using var createUser = await PostJsonWithCsrfAsync(adminClient, "/api/issuer/users", new
                {
                    username = "second-reviewer",
                    displayName = "Second Reviewer",
                    password = "Synthetic-Reviewer2!2026",
                    role = IssuerConstants.RoleReviewer
                }, adminCsrf);
                Assert.Equal(HttpStatusCode.Created, createUser.StatusCode);

                var allUsers = await adminClient.GetFromJsonAsync<IssuerAdminUser[]>("/api/issuer/users", JsonOpts);
                Assert.NotNull(allUsers);
                Assert.Equal(4, allUsers!.Length);
            }
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task T05_SeparationOfDuties_BlocksSelfApprovalByDefault_AndAuditsExplicitExceptionWhenAuthorized()
    {
        string root = NewTempRoot("t05-sod");
        try
        {
            // 1. Default strict two-person policy (AllowSelfApprovalException = false)
            var (app, adminClient, _) = await StartHostAsync(Path.Combine(root, "strict"), allowSelfApprovalException: false);
            await using (app)
            using (adminClient)
            using (var reviewerClient = NewCookieClient(app))
            {
                string adminCsrf = await LoginAsync(adminClient, AdminUser, AdminPass);
                string reviewerCsrf = await LoginAsync(reviewerClient, ReviewerUser, ReviewerPass);

                var (customer, store) = await SeedCustomerAndStoreAsync(adminClient, adminCsrf);
                using var draftResp = await PostJsonWithCsrfAsync(adminClient, "/api/issuer/requests", new
                {
                    customerId = customer.Id,
                    storeId = store.Id,
                    applicationId = IssuerConstants.ProductCafe,
                    licenseType = "Host",
                    edition = "Professional",
                    machineBinding = "SALRYN-MC-SOD-0001",
                    features = LicenseFeatures.AllList,
                    allowedLanTerminals = 2
                }, adminCsrf);
                using var draftDoc = JsonDocument.Parse(await draftResp.Content.ReadAsStringAsync());
                Guid reqId = draftDoc.RootElement.GetProperty("request").GetProperty("id").GetGuid();

                // Admin who created the draft CANNOT self-approve it (403 Issuer.SelfApprovalForbidden)
                using var selfApprove = await PostJsonWithCsrfAsync(adminClient, $"/api/issuer/requests/{reqId:D}/approve", new { note = "Self approve attempt" }, adminCsrf);
                Assert.Equal(HttpStatusCode.Forbidden, selfApprove.StatusCode);
                Assert.Contains("Issuer.SelfApprovalForbidden", await selfApprove.Content.ReadAsStringAsync(), StringComparison.Ordinal);

                // Audit log records LicenseRequest.SelfApprovalDenied
                var audits = await adminClient.GetFromJsonAsync<IssuerAuditEntry[]>("/api/issuer/audit", JsonOpts);
                Assert.Contains(audits!, a => a.Action == "LicenseRequest.SelfApprovalDenied");

                // Independent Reviewer account CAN approve the draft
                using var independentApprove = await PostJsonWithCsrfAsync(reviewerClient, $"/api/issuer/requests/{reqId:D}/approve", new { note = "Verified by separate reviewer" }, reviewerCsrf);
                Assert.Equal(HttpStatusCode.OK, independentApprove.StatusCode);
            }

            // 2. Explicit exception mode requires documented note >= 10 chars and audits selfApprovalException = true
            var (appEx, exClient, _) = await StartHostAsync(Path.Combine(root, "exception"), allowSelfApprovalException: true);
            await using (appEx)
            using (exClient)
            {
                string csrf = await LoginAsync(exClient, AdminUser, AdminPass);
                var (customer, store) = await SeedCustomerAndStoreAsync(exClient, csrf);
                using var draftResp = await PostJsonWithCsrfAsync(exClient, "/api/issuer/requests", new
                {
                    customerId = customer.Id,
                    storeId = store.Id,
                    applicationId = IssuerConstants.ProductCafe,
                    licenseType = "Host",
                    edition = "Professional",
                    machineBinding = "SALRYN-MC-SOD-0002",
                    features = LicenseFeatures.AllList,
                    allowedLanTerminals = 1
                }, csrf);
                using var draftDoc = JsonDocument.Parse(await draftResp.Content.ReadAsStringAsync());
                Guid reqId = draftDoc.RootElement.GetProperty("request").GetProperty("id").GetGuid();

                // Empty/short note rejected (409 Conflict)
                using var missingNote = await PostJsonWithCsrfAsync(exClient, $"/api/issuer/requests/{reqId:D}/approve", new { note = "short" }, csrf);
                Assert.Equal(HttpStatusCode.Conflict, missingNote.StatusCode);

                // Documented exception note succeeds and audits selfApprovalException:true
                using var documented = await PostJsonWithCsrfAsync(exClient, $"/api/issuer/requests/{reqId:D}/approve", new
                {
                    note = "Authorized single-admin emergency exception ticket #INC-2026-99"
                }, csrf);
                Assert.Equal(HttpStatusCode.OK, documented.StatusCode);

                var exAudits = await exClient.GetFromJsonAsync<IssuerAuditEntry[]>("/api/issuer/audit", JsonOpts);
                Assert.NotNull(exAudits);
                IssuerAuditEntry approvedAudit = Assert.Single(exAudits!, a => a.Action == "LicenseRequest.Approved");
                Assert.Contains("\"selfApprovalException\":true", approvedAudit.DetailJson, StringComparison.Ordinal);
            }
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task T06_UnconfiguredAndUntrustedSigners_FailClosedOnIssue_AndProductionBlocksTestSigner()
    {
        string root = NewTempRoot("t06-signers");
        try
        {
            // 1. Unconfigured signer -> 503 SIGNER_NOT_CONFIGURED
            var (app, opClient, _) = await StartHostAsync(Path.Combine(root, "unconf"), new LicenseSignerOptions());
            await using (app)
            using (opClient)
            using (var revClient = NewCookieClient(app))
            using (var admClient = NewCookieClient(app))
            {
                string opCsrf = await LoginAsync(opClient, OperatorUser, OperatorPass);
                string revCsrf = await LoginAsync(revClient, ReviewerUser, ReviewerPass);
                string admCsrf = await LoginAsync(admClient, AdminUser, AdminPass);

                var (customer, store) = await SeedCustomerAndStoreAsync(opClient, opCsrf);
                using var draftResp = await PostJsonWithCsrfAsync(opClient, "/api/issuer/requests", new
                {
                    customerId = customer.Id,
                    storeId = store.Id,
                    applicationId = IssuerConstants.ProductCafe,
                    licenseType = "Host",
                    edition = "Professional",
                    machineBinding = "SALRYN-MC-F36B-76CD-77FB-03EC",
                    features = LicenseFeatures.AllList,
                    allowedLanTerminals = 2
                }, opCsrf);
                using var draftDoc = JsonDocument.Parse(await draftResp.Content.ReadAsStringAsync());
                Guid reqId = draftDoc.RootElement.GetProperty("request").GetProperty("id").GetGuid();

                using var approve = await PostJsonWithCsrfAsync(revClient, $"/api/issuer/requests/{reqId:D}/approve", new { note = "Approved" }, revCsrf);
                Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

                using var issueResp = await PostJsonWithCsrfAsync(admClient, $"/api/issuer/requests/{reqId:D}/issue", new { }, admCsrf);
                Assert.Equal(HttpStatusCode.ServiceUnavailable, issueResp.StatusCode);
                Assert.Contains("SIGNER_NOT_CONFIGURED", await issueResp.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }

            // 2. Untrusted RSA key in Production WITH AllowUntrustedTestSigner = true -> STILL BLOCKED (503 SIGNER_TEST_KEY_FORBIDDEN_IN_PRODUCTION)
            using RSA untrustedRsa = RSA.Create(2048);
            string untrustedPem = untrustedRsa.ExportPkcs8PrivateKeyPem();

            var prodSigner = new ExternalLicenseSignerService(new LicenseSignerOptions
            {
                SigningKeyPemInline = untrustedPem,
                AllowUntrustedTestSigner = true,
                IsProductionEnvironment = true
            });
            SignerStatusSnapshot prodStatus = prodSigner.GetStatus();
            Assert.False(prodStatus.CanIssue);
            Assert.Equal("ProductionTestSignerForbidden", prodStatus.SignerMode);

            var sampleValidation = IssuerLicenseContractValidator.BuildAndValidateDraft(
                "SLRN-CAFE-PROD-01",
                "Cafe Yza",
                "CAFE-01",
                IssuerConstants.ProductCafe,
                LicenseType.Host,
                LicenseEdition.Professional,
                "SALRYN-MC-PROD-0001",
                LicenseFeatures.AllList,
                2,
                null,
                null,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddDays(30),
                null,
                null);
            LicenseSigningOutcome prodOutcome = prodSigner.SignAndVerify(sampleValidation.Document!);
            Assert.False(prodOutcome.IsSuccess);
            Assert.Equal("SIGNER_TEST_KEY_FORBIDDEN_IN_PRODUCTION", prodOutcome.ErrorCode);
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task T07_ProductIsolation_RejectsUnsupportedProduct_AndCrossProductStoreDrafts()
    {
        string root = NewTempRoot("t07-prod");
        try
        {
            var (app, client, _) = await StartHostAsync(root);
            await using (app)
            using (client)
            {
                string csrf = await LoginAsync(client, OperatorUser, OperatorPass);
                var (customer, cafeStore) = await SeedCustomerAndStoreAsync(client, csrf, IssuerConstants.ProductCafe, "CAFE-STORE-01");

                // Unsupported product on store creation -> 400
                using var badStore = await PostJsonWithCsrfAsync(client, "/api/issuer/stores", new
                {
                    customerId = customer.Id,
                    storeCode = "BAD-PROD-01",
                    storeName = "Bad Product Store",
                    productId = "Salryn.Unknown"
                }, csrf);
                Assert.Equal(HttpStatusCode.BadRequest, badStore.StatusCode);

                // Cross-product draft (Salryn.POS against a Salryn.Cafe store) -> 400
                using var crossProd = await PostJsonWithCsrfAsync(client, "/api/issuer/requests", new
                {
                    customerId = customer.Id,
                    storeId = cafeStore.Id,
                    applicationId = IssuerConstants.ProductRetail,
                    licenseType = "Host",
                    edition = "Professional",
                    machineBinding = "SALRYN-MC-CROSS-0001",
                    features = LicenseFeatures.AllList,
                    allowedLanTerminals = 1
                }, csrf);
                Assert.Equal(HttpStatusCode.BadRequest, crossProd.StatusCode);
            }
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void T08_LicenseContractValidation_RejectsMachineMismatch_Expired_InvalidHostAndTerminalCombinations()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // 1. Whitespace or too-short machine code -> invalid
        var badMachine = IssuerLicenseContractValidator.BuildAndValidateDraft(
            "LIC-1", "Cust", "STORE-1", IssuerConstants.ProductCafe, LicenseType.Host, LicenseEdition.Professional,
            "BAD MC WITH SPACES", LicenseFeatures.AllList, 1, null, null, now, now.AddDays(10), null, null);
        Assert.False(badMachine.IsValid);
        Assert.Equal("Issuer.MachineBindingInvalid", badMachine.ErrorCode);

        // 2. Expiry in the past -> invalid
        var expired = IssuerLicenseContractValidator.BuildAndValidateDraft(
            "LIC-2", "Cust", "STORE-1", IssuerConstants.ProductCafe, LicenseType.Host, LicenseEdition.Professional,
            "SALRYN-MC-VALID-01", LicenseFeatures.AllList, 1, null, null, now, now.AddMinutes(-1), null, null);
        Assert.False(expired.IsValid);
        Assert.Equal("Issuer.ExpiryInPast", expired.ErrorCode);

        // 3. Host license with TerminalId/HostLicenseId -> invalid
        var hostWithTermFields = IssuerLicenseContractValidator.BuildAndValidateDraft(
            "LIC-3", "Cust", "STORE-1", IssuerConstants.ProductCafe, LicenseType.Host, LicenseEdition.Professional,
            "SALRYN-MC-VALID-01", LicenseFeatures.AllList, 1, "HOST-1", "TERM-1", now, now.AddDays(10), null, null);
        Assert.False(hostWithTermFields.IsValid);
        Assert.Equal("Issuer.HostDisallowedTerminalFields", hostWithTermFields.ErrorCode);

        // 4. Terminal license with AllowedLanTerminals -> invalid
        var termWithLanCount = IssuerLicenseContractValidator.BuildAndValidateDraft(
            "LIC-4", "Cust", "STORE-1", IssuerConstants.ProductCafe, LicenseType.Terminal, LicenseEdition.Professional,
            "SALRYN-MC-VALID-01", [LicenseFeatures.LanTerminals], 2, "HOST-1", "TERM-1", now, now.AddDays(10), null, null);
        Assert.False(termWithLanCount.IsValid);
        Assert.Equal("Issuer.TerminalDisallowedLanCount", termWithLanCount.ErrorCode);

        // 5. Terminal license requesting host-only feature -> invalid
        var termWithHostFeat = IssuerLicenseContractValidator.BuildAndValidateDraft(
            "LIC-5", "Cust", "STORE-1", IssuerConstants.ProductCafe, LicenseType.Terminal, LicenseEdition.Professional,
            "SALRYN-MC-VALID-01", [LicenseFeatures.LanTerminals, LicenseFeatures.Backup], null, "HOST-1", "TERM-1", now, now.AddDays(10), null, null);
        Assert.False(termWithHostFeat.IsValid);
        Assert.Equal("Issuer.TerminalHostFeaturesDenied", termWithHostFeat.ErrorCode);
    }

    [Fact]
    public void T09_SignatureVerification_DetectsTamperedPayload_WrongPublicKey_AndNoProductSubstitution()
    {
        using RSA keyA = RSA.Create(2048);
        using RSA keyB = RSA.Create(2048);
        string privA = keyA.ExportPkcs8PrivateKeyPem();
        string pubA = keyA.ExportSubjectPublicKeyInfoPem();
        string pubB = keyB.ExportSubjectPublicKeyInfoPem();

        var signer = new ExternalLicenseSignerService(new LicenseSignerOptions
        {
            SigningKeyPemInline = privA,
            AllowUntrustedTestSigner = true,
            IsProductionEnvironment = false
        });

        DateTimeOffset now = DateTimeOffset.UtcNow;
        var draft = IssuerLicenseContractValidator.BuildAndValidateDraft(
            "SLRN-CAFE-SIG-01",
            "Cafe Yza",
            "CAFE-YZA-01",
            IssuerConstants.ProductCafe,
            LicenseType.Host,
            LicenseEdition.Professional,
            "SALRYN-MC-SIG-0001",
            LicenseFeatures.AllList,
            2,
            null,
            null,
            now,
            now.AddDays(60),
            null,
            null);
        Assert.True(draft.IsValid);

        LicenseSigningOutcome outcome = signer.SignAndVerify(draft.Document!);
        Assert.True(outcome.IsSuccess);
        Assert.True(outcome.VerifierPassed);
        // Honest reporting: untrusted test key must NOT claim RuntimeTrustedVerifierPassed
        Assert.False(outcome.RuntimeTrustedVerifierPassed);

        var signedDoc = JsonSerializer.Deserialize<LocalLicenseDocument>(outcome.SignedLicenseJson!, JsonOpts)!;
        Assert.Equal(IssuerConstants.ProductCafe, signedDoc.ApplicationId);
        Assert.True(ExternalLicenseSignerService.VerifyWithSpecificPublicKey(signedDoc, pubA));

        // Wrong public key MUST fail
        Assert.False(ExternalLicenseSignerService.VerifyWithSpecificPublicKey(signedDoc, pubB));
        // Production hardcoded verifier MUST reject untrusted test signature
        Assert.False(LocalLicenseSignatureVerifier.Verify(signedDoc));

        // Tampering any field (ApplicationId, MachineBinding, Features) MUST invalidate signature
        Assert.False(ExternalLicenseSignerService.VerifyWithSpecificPublicKey(signedDoc with { ApplicationId = IssuerConstants.ProductRetail }, pubA));
        Assert.False(ExternalLicenseSignerService.VerifyWithSpecificPublicKey(signedDoc with { MachineBinding = "SALRYN-MC-TAMPERED" }, pubA));
        Assert.False(ExternalLicenseSignerService.VerifyWithSpecificPublicKey(signedDoc with { AllowedLanTerminals = 99 }, pubA));
    }

    [Fact]
    public async Task T10_IdempotencyReplay_Returns200OnMatch_409OnPayloadMismatch_AndConcurrentIssuanceIsAtomic()
    {
        string root = NewTempRoot("t10-idem-conc");
        try
        {
            using RSA testKey = RSA.Create(2048);
            string keyFile = Path.Combine(root, "test-key.pem");
            await File.WriteAllTextAsync(keyFile, testKey.ExportPkcs8PrivateKeyPem());

            var (app, opClient, _) = await StartHostAsync(root, new LicenseSignerOptions
            {
                SigningKeyPemPath = keyFile,
                AllowUntrustedTestSigner = true
            });
            await using (app)
            using (opClient)
            using (var revClient = NewCookieClient(app))
            using (var admClient = NewCookieClient(app))
            {
                string opCsrf = await LoginAsync(opClient, OperatorUser, OperatorPass);
                string revCsrf = await LoginAsync(revClient, ReviewerUser, ReviewerPass);
                string admCsrf = await LoginAsync(admClient, AdminUser, AdminPass);

                var (customer, store) = await SeedCustomerAndStoreAsync(opClient, opCsrf);
                var payload = new
                {
                    customerId = customer.Id,
                    storeId = store.Id,
                    applicationId = IssuerConstants.ProductCafe,
                    licenseType = "Host",
                    edition = "Professional",
                    machineBinding = "SALRYN-MC-CONC-0001",
                    features = LicenseFeatures.AllList,
                    allowedLanTerminals = 2,
                    idempotencyKey = "idem-conc-key-1"
                };

                using var first = await PostJsonWithCsrfAsync(opClient, "/api/issuer/requests", payload, opCsrf);
                Assert.Equal(HttpStatusCode.Created, first.StatusCode);
                using var firstDoc = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
                Guid reqId = firstDoc.RootElement.GetProperty("request").GetProperty("id").GetGuid();

                // Identical replay -> 200 OK isReplay:true
                using var replay = await PostJsonWithCsrfAsync(opClient, "/api/issuer/requests", payload, opCsrf);
                Assert.Equal(HttpStatusCode.OK, replay.StatusCode);

                // Reusing same idempotencyKey with different MachineBinding -> 409 Conflict (Issuer.IdempotencyPayloadMismatch)
                using var mismatch = await PostJsonWithCsrfAsync(opClient, "/api/issuer/requests", new
                {
                    customerId = customer.Id,
                    storeId = store.Id,
                    applicationId = IssuerConstants.ProductCafe,
                    licenseType = "Host",
                    edition = "Professional",
                    machineBinding = "SALRYN-MC-DIFFERENT-9999",
                    features = LicenseFeatures.AllList,
                    allowedLanTerminals = 2,
                    idempotencyKey = "idem-conc-key-1"
                }, opCsrf);
                Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
                Assert.Contains("Issuer.IdempotencyPayloadMismatch", await mismatch.Content.ReadAsStringAsync(), StringComparison.Ordinal);

                // Independent Reviewer approves
                using var approve = await PostJsonWithCsrfAsync(revClient, $"/api/issuer/requests/{reqId:D}/approve", new { note = "Approved for concurrency test" }, revCsrf);
                Assert.Equal(HttpStatusCode.OK, approve.StatusCode);

                // 5 concurrent POST /issue requests from Admin
                Task<HttpResponseMessage>[] issueTasks = Enumerable.Range(0, 5)
                    .Select(_ => PostJsonWithCsrfAsync(admClient, $"/api/issuer/requests/{reqId:D}/issue", new { }, admCsrf))
                    .ToArray();
                HttpResponseMessage[] responses = await Task.WhenAll(issueTasks);

                var shas = new HashSet<string>(StringComparer.Ordinal);
                int freshIssues = 0;
                int replayIssues = 0;
                foreach (var r in responses)
                {
                    using (r)
                    {
                        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
                        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
                        if (doc.RootElement.GetProperty("alreadyIssued").GetBoolean()) replayIssues++;
                        else freshIssues++;
                        shas.Add(doc.RootElement.GetProperty("request").GetProperty("signedLicenseSha256").GetString()!);
                    }
                }
                Assert.Equal(1, freshIssues);
                Assert.Equal(4, replayIssues);
                Assert.Single(shas);
            }
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task T11_ReissueFlow_PreservesNote_SupersedesPriorIssuedLicense_AndBlocksRejectOnIssued()
    {
        string root = NewTempRoot("t11-reissue");
        try
        {
            using RSA testKey = RSA.Create(2048);
            string keyFile = Path.Combine(root, "test-key.pem");
            await File.WriteAllTextAsync(keyFile, testKey.ExportPkcs8PrivateKeyPem());

            var (app, opClient, _) = await StartHostAsync(root, new LicenseSignerOptions
            {
                SigningKeyPemPath = keyFile,
                AllowUntrustedTestSigner = true
            });
            await using (app)
            using (opClient)
            using (var revClient = NewCookieClient(app))
            using (var admClient = NewCookieClient(app))
            {
                string opCsrf = await LoginAsync(opClient, OperatorUser, OperatorPass);
                string revCsrf = await LoginAsync(revClient, ReviewerUser, ReviewerPass);
                string admCsrf = await LoginAsync(admClient, AdminUser, AdminPass);

                var (customer, store) = await SeedCustomerAndStoreAsync(opClient, opCsrf);
                using var draft1 = await PostJsonWithCsrfAsync(opClient, "/api/issuer/requests", new
                {
                    customerId = customer.Id,
                    storeId = store.Id,
                    applicationId = IssuerConstants.ProductCafe,
                    licenseType = "Host",
                    edition = "Professional",
                    machineBinding = "SALRYN-MC-ORIG-0001",
                    features = LicenseFeatures.AllList,
                    allowedLanTerminals = 2
                }, opCsrf);
                using var d1Doc = JsonDocument.Parse(await draft1.Content.ReadAsStringAsync());
                Guid origId = d1Doc.RootElement.GetProperty("request").GetProperty("id").GetGuid();

                await PostJsonWithCsrfAsync(revClient, $"/api/issuer/requests/{origId:D}/approve", new { note = "Initial approval" }, revCsrf);
                await PostJsonWithCsrfAsync(admClient, $"/api/issuer/requests/{origId:D}/issue", new { }, admCsrf);

                // Rejecting an already-Issued license -> 409 Conflict
                using var rejectIssued = await PostJsonWithCsrfAsync(revClient, $"/api/issuer/requests/{origId:D}/reject", new { note = "Too late" }, revCsrf);
                Assert.Equal(HttpStatusCode.Conflict, rejectIssued.StatusCode);

                // Reissue preserves the reissue reason note on the new draft
                using var reissueResp = await PostJsonWithCsrfAsync(opClient, $"/api/issuer/requests/{origId:D}/reissue", new
                {
                    machineBinding = "SALRYN-MC-REPL-0002",
                    note = "Motherboard replaced under RMA #4412"
                }, opCsrf);
                Assert.Equal(HttpStatusCode.Created, reissueResp.StatusCode);
                using var reissueDoc = JsonDocument.Parse(await reissueResp.Content.ReadAsStringAsync());
                Guid reissueId = reissueDoc.RootElement.GetProperty("request").GetProperty("id").GetGuid();
                Assert.Equal("Motherboard replaced under RMA #4412", reissueDoc.RootElement.GetProperty("request").GetProperty("reviewNote").GetString());

                await PostJsonWithCsrfAsync(revClient, $"/api/issuer/requests/{reissueId:D}/approve", new { }, revCsrf);
                await PostJsonWithCsrfAsync(admClient, $"/api/issuer/requests/{reissueId:D}/issue", new { }, admCsrf);

                var priorAfter = await admClient.GetFromJsonAsync<IssuerLicenseRequestRecord>($"/api/issuer/requests/{origId:D}", JsonOpts);
                Assert.Equal(IssuerConstants.StatusSuperseded, priorAfter!.Status);
            }
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task T12_AuditTrailHashChain_IsTamperEvident_AndZeroPrivateKeyMaterialInDbApiOrAudit()
    {
        string root = NewTempRoot("t12-audit-chain");
        try
        {
            using RSA testKey = RSA.Create(2048);
            string keyFile = Path.Combine(root, "test-key.pem");
            await File.WriteAllTextAsync(keyFile, testKey.ExportPkcs8PrivateKeyPem());

            var (app, opClient, dbPath) = await StartHostAsync(root, new LicenseSignerOptions
            {
                SigningKeyPemPath = keyFile,
                AllowUntrustedTestSigner = true
            });
            await using (app)
            using (opClient)
            using (var revClient = NewCookieClient(app))
            using (var admClient = NewCookieClient(app))
            {
                string opCsrf = await LoginAsync(opClient, OperatorUser, OperatorPass);
                string revCsrf = await LoginAsync(revClient, ReviewerUser, ReviewerPass);
                string admCsrf = await LoginAsync(admClient, AdminUser, AdminPass);

                var (customer, store) = await SeedCustomerAndStoreAsync(opClient, opCsrf);
                using var draft = await PostJsonWithCsrfAsync(opClient, "/api/issuer/requests", new
                {
                    customerId = customer.Id,
                    storeId = store.Id,
                    applicationId = IssuerConstants.ProductCafe,
                    licenseType = "Host",
                    edition = "Professional",
                    machineBinding = "SALRYN-MC-AUDIT-0001",
                    features = LicenseFeatures.AllList,
                    allowedLanTerminals = 1
                }, opCsrf);
                using var dDoc = JsonDocument.Parse(await draft.Content.ReadAsStringAsync());
                Guid reqId = dDoc.RootElement.GetProperty("request").GetProperty("id").GetGuid();

                await PostJsonWithCsrfAsync(revClient, $"/api/issuer/requests/{reqId:D}/approve", new { note = "Audit check" }, revCsrf);
                await PostJsonWithCsrfAsync(admClient, $"/api/issuer/requests/{reqId:D}/issue", new { }, admCsrf);

                // Verify intact hash chain via GET /api/issuer/audit/verify
                var chainCheck = await admClient.GetFromJsonAsync<IssuerAuditChainVerification>("/api/issuer/audit/verify", JsonOpts);
                Assert.NotNull(chainCheck);
                Assert.True(chainCheck!.IsValid);
                Assert.True(chainCheck.CheckedEntries >= 8);

                // Zero private key material in API responses
                string auditPayload = await admClient.GetStringAsync("/api/issuer/audit");
                string requestsPayload = await admClient.GetStringAsync("/api/issuer/requests");
                Assert.DoesNotContain("PRIVATE KEY", auditPayload, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("PRIVATE KEY", requestsPayload, StringComparison.OrdinalIgnoreCase);
            }

            // Verify zero private key bytes in raw SQLite DB file
            SqliteConnection.ClearAllPools();
            byte[] dbBytes = await File.ReadAllBytesAsync(dbPath);
            string dbText = Encoding.Latin1.GetString(dbBytes);
            Assert.DoesNotContain("BEGIN PRIVATE KEY", dbText, StringComparison.Ordinal);
            Assert.DoesNotContain("BEGIN RSA PRIVATE KEY", dbText, StringComparison.Ordinal);

            // Direct SQLite tampering of an audit row MUST be caught by VerifyAuditChain()
            using (var conn = new SqliteConnection($"Data Source={dbPath}"))
            {
                conn.Open();
                using var tamper = conn.CreateCommand();
                tamper.CommandText = "UPDATE issuer_audit_log SET detail_json = '{\"tampered\":true}' WHERE id = 2;";
                tamper.ExecuteNonQuery();
            }
            var directStore = new IssuerSqliteStore(dbPath);
            IssuerAuditChainVerification afterTamper = directStore.VerifyAuditChain();
            Assert.False(afterTamper.IsValid);
            Assert.Equal(2L, afterTamper.FirstBrokenId);
        }
        finally
        {
            Cleanup(root);
        }
    }
}
