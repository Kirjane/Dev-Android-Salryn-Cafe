using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Salryn.Api.Local;
using Salryn.Composition.Local;
using Salryn.Core.Services.Licensing;
using Salryn.Infrastructure.Licensing;
using Salryn.Infrastructure.Paths;
using Salryn.LicensingIssuer.Server;
using Xunit;

namespace Salryn.LicensingIssuer.Tests;

public sealed class CombinedIssuerAndCafeHostEndToEndTests
{
    private const string AdminUser = "kirjane-admin";
    private const string AdminPass = "Synthetic-Issuer!2026#Admin";
    private const string ReviewerUser = "kirjane-reviewer";
    private const string ReviewerPass = "Synthetic-Issuer!2026#Review";
    private const string OperatorUser = "kirjane-operator";
    private const string OperatorPass = "Synthetic-Issuer!2026#Operate";
    private const string CafeAdminPassword = "Synthetic-CafeHost!2026#Admin";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task CombinedSystem_UnlicensedCafeLocked_IssuerDualControlSignsUnchangedCafeLicense_CafeHostVerifiesAndUnlocksFirstAdmin_WhileInvalidWrongProductWrongMachineTerminalAndExpiredStayBlocked()
    {
        string root = Path.Combine(Path.GetTempPath(), $"salryn-combined-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            const string workstationMachineCode = "SALRYN-MC-COMBINED-HOST-01";
            using RSA testKeyPair = RSA.Create(2048);
            string privateKeyPem = testKeyPair.ExportPkcs8PrivateKeyPem();
            string publicKeyPem = testKeyPair.ExportSubjectPublicKeyInfoPem();
            string keyFile = Path.Combine(root, "issuer-test-key.pem");
            await File.WriteAllTextAsync(keyFile, privateKeyPem);

            // -------------------------------------------------------------------------
            // 1. Start real Salryn Cafe Local Host API (Issue #38 contract: Salryn.Cafe)
            // -------------------------------------------------------------------------
            string cafeRoot = Path.Combine(root, "cafe-host");
            Directory.CreateDirectory(cafeRoot);
            var cafeDbPaths = DbPaths.CreateAtRoot(cafeRoot);
            var cafeComposition = SalrynLocalComposition.Create(cafeDbPaths);
            await cafeComposition.InitializeDatabaseAsync();

            var cafeLicenseStore = new LocalLicenseFileStore(
                new LocalLicenseFileStoreOptions(Path.Combine(cafeRoot, "licensing"), "license.json"));
            var cafeValidator = new LocalLicenseValidator(new LocalLicenseValidatorOptions
            {
                ExpectedApplicationId = SalrynProductIdentity.ProductId, // "Salryn.Cafe"
                MachineFingerprint = workstationMachineCode,
                SignatureVerifier = doc => LocalLicenseSignatureVerifier.VerifyWithPublicKeyPem(doc, publicKeyPem)
            });
            var cafeLicenseService = new LocalLicenseService(cafeLicenseStore, cafeValidator);

            await using var cafeHost = await LocalApiApplicationFactory.CreateAsync([], new()
            {
                Composition = SalrynLocalComposition.Create(cafeDbPaths),
                LicenseService = cafeLicenseService,
                Url = "http://127.0.0.1:0",
                EnableStaticWebAssets = false,
                WriteDevelopmentDatabasePath = false
            });
            await cafeHost.StartAsync();
            using var cafeClient = new HttpClient(new HttpClientHandler { UseCookies = true })
            {
                BaseAddress = new Uri(cafeHost.Urls.Single())
            };

            // Verify unlicensed Cafe Host reports Unlicensed + MachineCode and blocks first-admin (403)
            using var initialStatusResp = await cafeClient.GetAsync("/api/license/status");
            Assert.Equal(HttpStatusCode.OK, initialStatusResp.StatusCode);
            using var initialStatusDoc = JsonDocument.Parse(await initialStatusResp.Content.ReadAsStringAsync());
            Assert.Equal((int)LicenseStatus.Unlicensed, initialStatusDoc.RootElement.GetProperty("status").GetInt32());
            string observedMachineCode = initialStatusDoc.RootElement.GetProperty("machineCode").GetString()!;
            Assert.Equal(workstationMachineCode, observedMachineCode);

            using var unlicensedBootstrap = await cafeClient.PostAsJsonAsync("/api/bootstrap/first-admin", new
            {
                storeName = "Cafe Yza Unlicensed Attempt",
                receiptWidthMm = 58,
                adminUsername = "cafe-admin",
                adminDisplayName = "Cafe Admin",
                adminPassword = CafeAdminPassword
            });
            Assert.Equal(HttpStatusCode.Forbidden, unlicensedBootstrap.StatusCode);
            Assert.Contains("Bootstrap.HostLicenseRequired", await unlicensedBootstrap.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            // -------------------------------------------------------------------------
            // 2. Start real Standalone Licensing Issuer Server (Issue #39)
            // -------------------------------------------------------------------------
            await using var issuerHost = LicensingIssuerAppFactory.Create([], new LicensingIssuerHostOptions
            {
                DatabasePath = Path.Combine(root, "issuer", "issuer.db"),
                ListenUrl = "http://127.0.0.1:0",
                BootstrapAdminUsername = AdminUser,
                BootstrapAdminPassword = AdminPass,
                BootstrapReviewerUsername = ReviewerUser,
                BootstrapReviewerPassword = ReviewerPass,
                BootstrapOperatorUsername = OperatorUser,
                BootstrapOperatorPassword = OperatorPass,
                AllowSelfApprovalException = false,
                SignerOptions = new LicenseSignerOptions
                {
                    SigningKeyPemPath = keyFile,
                    AllowUntrustedTestSigner = true,
                    IsProductionEnvironment = false
                }
            });
            await issuerHost.StartAsync();

            using var operatorClient = new HttpClient(new HttpClientHandler { UseCookies = true }) { BaseAddress = new Uri(issuerHost.Urls.Single()) };
            using var reviewerClient = new HttpClient(new HttpClientHandler { UseCookies = true }) { BaseAddress = new Uri(issuerHost.Urls.Single()) };
            using var adminClient = new HttpClient(new HttpClientHandler { UseCookies = true }) { BaseAddress = new Uri(issuerHost.Urls.Single()) };

            static async Task<string> LoginIssuerAsync(HttpClient c, string u, string p)
            {
                using var r = await c.PostAsJsonAsync("/api/issuer/auth/login", new { username = u, password = p });
                Assert.Equal(HttpStatusCode.OK, r.StatusCode);
                var s = await r.Content.ReadFromJsonAsync<IssuerSessionInfo>(JsonOpts);
                return s!.CsrfToken!;
            }

            static async Task<HttpResponseMessage> PostIssuerAsync(HttpClient c, string path, object body, string csrf)
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
                req.Headers.Add(IssuerConstants.CsrfHeaderName, csrf);
                return await c.SendAsync(req);
            }

            string opCsrf = await LoginIssuerAsync(operatorClient, OperatorUser, OperatorPass);
            string revCsrf = await LoginIssuerAsync(reviewerClient, ReviewerUser, ReviewerPass);
            string admCsrf = await LoginIssuerAsync(adminClient, AdminUser, AdminPass);

            // Operator registers customer + Salryn.Cafe store + Salryn.POS store
            using var custResp = await PostIssuerAsync(operatorClient, "/api/issuer/customers", new
            {
                customerName = "Cafe Yza F&B Group",
                contactEmail = "it@cafeyza.example"
            }, opCsrf);
            var customer = (await custResp.Content.ReadFromJsonAsync<IssuerCustomerRecord>(JsonOpts))!;

            using var cafeStoreResp = await PostIssuerAsync(operatorClient, "/api/issuer/stores", new
            {
                customerId = customer.Id,
                storeCode = "CAFE-YZA-MAIN",
                storeName = "Cafe Yza Main Branch",
                productId = IssuerConstants.ProductCafe
            }, opCsrf);
            var cafeStore = (await cafeStoreResp.Content.ReadFromJsonAsync<IssuerStoreRecord>(JsonOpts))!;

            using var retailStoreResp = await PostIssuerAsync(operatorClient, "/api/issuer/stores", new
            {
                customerId = customer.Id,
                storeCode = "RETAIL-POS-01",
                storeName = "Retail Branch",
                productId = IssuerConstants.ProductRetail
            }, opCsrf);
            var retailStore = (await retailStoreResp.Content.ReadFromJsonAsync<IssuerStoreRecord>(JsonOpts))!;

            async Task<string> DraftApproveIssueAndDownloadAsync(
                Guid storeId,
                string appId,
                string licenseType,
                string machineBinding,
                IReadOnlyList<string> features,
                int? allowedLan,
                string? hostLicId = null,
                string? termId = null)
            {
                using var dResp = await PostIssuerAsync(operatorClient, "/api/issuer/requests", new
                {
                    customerId = customer.Id,
                    storeId,
                    applicationId = appId,
                    licenseType,
                    edition = "Professional",
                    machineBinding,
                    features,
                    allowedLanTerminals = allowedLan,
                    hostLicenseId = hostLicId,
                    terminalId = termId,
                    expiresAtUtc = DateTimeOffset.UtcNow.AddDays(180)
                }, opCsrf);
                Assert.Equal(HttpStatusCode.Created, dResp.StatusCode);
                using var dDoc = JsonDocument.Parse(await dResp.Content.ReadAsStringAsync());
                Guid reqId = dDoc.RootElement.GetProperty("request").GetProperty("id").GetGuid();

                using var aResp = await PostIssuerAsync(reviewerClient, $"/api/issuer/requests/{reqId:D}/approve", new
                {
                    note = "Independent Reviewer approval"
                }, revCsrf);
                Assert.Equal(HttpStatusCode.OK, aResp.StatusCode);

                using var iResp = await PostIssuerAsync(adminClient, $"/api/issuer/requests/{reqId:D}/issue", new { }, admCsrf);
                Assert.Equal(HttpStatusCode.OK, iResp.StatusCode);

                using var dlResp = await adminClient.GetAsync($"/api/issuer/requests/{reqId:D}/download");
                Assert.Equal(HttpStatusCode.OK, dlResp.StatusCode);
                return await dlResp.Content.ReadAsStringAsync();
            }

            // -------------------------------------------------------------------------
            // 3. Issue valid Salryn.Cafe Host license + negative-case licenses from Issuer
            // -------------------------------------------------------------------------
            string validCafeHostLicenseJson = await DraftApproveIssueAndDownloadAsync(
                cafeStore.Id,
                IssuerConstants.ProductCafe,
                "Host",
                observedMachineCode,
                LicenseFeatures.AllList,
                allowedLan: 3);

            string wrongProductRetailLicenseJson = await DraftApproveIssueAndDownloadAsync(
                retailStore.Id,
                IssuerConstants.ProductRetail,
                "Host",
                observedMachineCode,
                LicenseFeatures.AllList,
                allowedLan: 2);

            string wrongMachineCafeLicenseJson = await DraftApproveIssueAndDownloadAsync(
                cafeStore.Id,
                IssuerConstants.ProductCafe,
                "Host",
                "SALRYN-MC-OTHER-WORKSTATION-99",
                LicenseFeatures.AllList,
                allowedLan: 2);

            string terminalCafeLicenseJson = await DraftApproveIssueAndDownloadAsync(
                cafeStore.Id,
                IssuerConstants.ProductCafe,
                "Terminal",
                observedMachineCode,
                [LicenseFeatures.LanTerminals],
                allowedLan: null,
                hostLicId: "SLRN-CAFE-HOST-PARENT",
                termId: "TERM-01");

            // 3b. Verify default unmodified production LocalLicenseService rejects test-signed license (honest BLOCKED_EXTERNAL proof)
            var productionDefaultService = new LocalLicenseService(
                new LocalLicenseFileStore(new LocalLicenseFileStoreOptions(Path.Combine(root, "prod-default"), "license.json")));
            LocalLicenseInstallResponse prodDefaultCheck =
                await productionDefaultService.InstallAsync(new LocalLicenseInstallRequest(validCafeHostLicenseJson));
            Assert.Equal("Rejected", prodDefaultCheck.InstallStatus);

            // -------------------------------------------------------------------------
            // 4. Verify Cafe Host blocks wrong-product, wrong-machine, Terminal, tampered, and expired licenses
            // -------------------------------------------------------------------------
            // 4a. Wrong-product (Salryn.POS) -> 400 ERR_WRONG_PRODUCT_LICENSE
            using var installRetail = await cafeClient.PostAsJsonAsync("/api/license/install", new { licenseJson = wrongProductRetailLicenseJson });
            Assert.Equal(HttpStatusCode.BadRequest, installRetail.StatusCode);
            Assert.Contains(SalrynProductGuard.ErrorWrongProductLicense, await installRetail.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            // 4b. Wrong-machine -> 400
            using var installWrongMachine = await cafeClient.PostAsJsonAsync("/api/license/install", new { licenseJson = wrongMachineCafeLicenseJson });
            Assert.Equal(HttpStatusCode.BadRequest, installWrongMachine.StatusCode);

            // 4c. Terminal license on Host -> 400 License.Install.HostLicenseRequired
            using var installTerminal = await cafeClient.PostAsJsonAsync("/api/license/install", new { licenseJson = terminalCafeLicenseJson });
            Assert.Equal(HttpStatusCode.BadRequest, installTerminal.StatusCode);
            Assert.Contains("License.Install.HostLicenseRequired", await installTerminal.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            // 4d. Tampered signature / payload -> 400
            string tamperedJson = validCafeHostLicenseJson.Replace("\"allowedLanTerminals\": 3", "\"allowedLanTerminals\": 99", StringComparison.Ordinal);
            using var installTampered = await cafeClient.PostAsJsonAsync("/api/license/install", new { licenseJson = tamperedJson });
            Assert.Equal(HttpStatusCode.BadRequest, installTampered.StatusCode);

            // 4e. Expired Salryn.Cafe Host license -> 400 Expired
            var parsedValid = JsonSerializer.Deserialize<LocalLicenseDocument>(validCafeHostLicenseJson, JsonOpts)!;
            var expiredUnsigned = parsedValid with
            {
                LicenseId = "SLRN-CAFE-EXPIRED-01",
                IssuedAt = DateTimeOffset.UtcNow.AddDays(-60),
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
                Signature = null
            };
            byte[] expPayload = Encoding.UTF8.GetBytes(LocalLicenseSignatureVerifier.BuildSigningPayload(expiredUnsigned));
            string expSig = Convert.ToBase64String(testKeyPair.SignData(expPayload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            string expiredSignedJson = JsonSerializer.Serialize(expiredUnsigned with { Signature = expSig }, IssuerConstants.CanonicalJsonOptions);
            using var installExpired = await cafeClient.PostAsJsonAsync("/api/license/install", new { licenseJson = expiredSignedJson });
            Assert.Equal(HttpStatusCode.BadRequest, installExpired.StatusCode);

            // Confirm first-admin is STILL blocked (403) after all negative attempts
            using var stillBlocked = await cafeClient.PostAsJsonAsync("/api/bootstrap/first-admin", new
            {
                storeName = "Still Blocked Cafe",
                receiptWidthMm = 58,
                adminUsername = "cafe-admin",
                adminDisplayName = "Cafe Admin",
                adminPassword = CafeAdminPassword
            });
            Assert.Equal(HttpStatusCode.Forbidden, stillBlocked.StatusCode);

            // -------------------------------------------------------------------------
            // 5. Install original, unchanged Issuer-signed Salryn.Cafe Host license onto Cafe Host -> 200 OK
            // -------------------------------------------------------------------------
            using var installValid = await cafeClient.PostAsJsonAsync("/api/license/install", new
            {
                licenseJson = validCafeHostLicenseJson
            });
            Assert.Equal(HttpStatusCode.OK, installValid.StatusCode);

            using var licensedStatusResp = await cafeClient.GetAsync("/api/license/status");
            Assert.Equal(HttpStatusCode.OK, licensedStatusResp.StatusCode);
            using var licensedDoc = JsonDocument.Parse(await licensedStatusResp.Content.ReadAsStringAsync());
            Assert.Equal((int)LicenseStatus.Licensed, licensedDoc.RootElement.GetProperty("status").GetInt32());
            Assert.Equal(IssuerConstants.ProductCafe, licensedDoc.RootElement.GetProperty("applicationId").GetString());
            Assert.Equal((int)LicenseType.Host, licensedDoc.RootElement.GetProperty("licenseType").GetInt32());
            Assert.True(licensedDoc.RootElement.GetProperty("machineMatched").GetBoolean());
            Assert.Equal(3, licensedDoc.RootElement.GetProperty("allowedLanTerminals").GetInt32());

            // -------------------------------------------------------------------------
            // 6. First-Administrator registration NOW succeeds (201 Created) & login works (200 OK)
            // -------------------------------------------------------------------------
            using var bootstrapOk = await cafeClient.PostAsJsonAsync("/api/bootstrap/first-admin", new
            {
                storeName = "Cafe Yza Main Branch",
                receiptWidthMm = 58,
                adminUsername = "cafe-admin",
                adminDisplayName = "Cafe Yza Administrator",
                adminPassword = CafeAdminPassword
            });
            Assert.Equal(HttpStatusCode.Created, bootstrapOk.StatusCode);

            using var cafeLogin = await cafeClient.PostAsJsonAsync("/api/auth/login", new
            {
                username = "cafe-admin",
                password = CafeAdminPassword
            });
            Assert.Equal(HttpStatusCode.OK, cafeLogin.StatusCode);

            // Verify Issuer audit chain remains 100% intact across all operations
            var auditVerify = await adminClient.GetFromJsonAsync<IssuerAuditChainVerification>("/api/issuer/audit/verify", JsonOpts);
            Assert.NotNull(auditVerify);
            Assert.True(auditVerify!.IsValid);

            await cafeHost.StopAsync();
            await issuerHost.StopAsync();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void InstallerPackagingBoundary_ExcludesStandaloneLicensingIssuerFromCustomerSetup()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Salryn.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        string repoRoot = dir!.FullName;

        string buildScript = File.ReadAllText(Path.Combine(repoRoot, "installer", "Build-SalrynCafeInstaller.ps1"));
        string issScript = File.ReadAllText(Path.Combine(repoRoot, "installer", "SalrynCafeSetup.iss"));
        string ciWorkflow = File.ReadAllText(Path.Combine(repoRoot, ".github", "workflows", "salryn-cafe-installer-qa.yml"));

        foreach (string packagingFile in new[] { buildScript, issScript, ciWorkflow })
        {
            Assert.DoesNotContain("services/licensing-issuer", packagingFile, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("services\\licensing-issuer", packagingFile, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Salryn.LicensingIssuer.Server", packagingFile, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("apps/licensing-web", packagingFile, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("apps\\licensing-web", packagingFile, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ProductionTrustedSigner_WhenCanonicalLicensingOpsKeyPresent_VerifiesWithUnmodifiedReleaseLocalLicenseService()
    {
        const string canonicalOpsKeyPath = @"C:\Salryn-LicensingOps-I3\.secrets\beta-signing-private.pem";
        if (!File.Exists(canonicalOpsKeyPath))
        {
            // On GitHub CI runners without external LicensingOps secret custody, verify fail-closed SIGNER_NOT_CONFIGURED.
            var unconfiguredSigner = new ExternalLicenseSignerService(new LicenseSignerOptions
            {
                SigningKeyPemPath = canonicalOpsKeyPath,
                AllowUntrustedTestSigner = false,
                IsProductionEnvironment = true
            });
            SignerStatusSnapshot ciStatus = unconfiguredSigner.GetStatus();
            Assert.False(ciStatus.CanIssue);
            return;
        }

        // On authorized Kirjane Labs workstation with external LicensingOps key custody:
        // verify ProductionTrustedSigner (AllowUntrustedTestSigner = false, IsProductionEnvironment = true)
        // issues an unchanged Salryn.Cafe Host license accepted by UNMODIFIED default LocalLicenseService!
        string root = Path.Combine(Path.GetTempPath(), $"salryn-prod-trusted-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var prodSigner = new ExternalLicenseSignerService(new LicenseSignerOptions
            {
                SigningKeyPemPath = canonicalOpsKeyPath,
                AllowUntrustedTestSigner = false,
                IsProductionEnvironment = true
            });
            SignerStatusSnapshot status = prodSigner.GetStatus();
            Assert.True(status.IsConfigured);
            Assert.True(status.CanIssue);
            Assert.True(status.MatchesTrustedRuntimeKey);
            Assert.Equal("ProductionTrustedSigner", status.SignerMode);
            Assert.Equal(LocalLicenseSignatureVerifier.TrustedPublicKeySpkiSha256, status.PublicKeySpkiSha256);

            // Unmodified default LocalLicenseService (uses real machine code + hardcoded production PublicKeyPem)
            var cafeStore = new LocalLicenseFileStore(new LocalLicenseFileStoreOptions(Path.Combine(root, "cafe-licensing"), "license.json"));
            var defaultReleaseLicenseService = new LocalLicenseService(cafeStore);
            LocalLicenseValidationResult unlicensed = await defaultReleaseLicenseService.GetStatusAsync();
            string actualMachineCode = unlicensed.MachineCode ?? unlicensed.MachineFingerprint!;
            Assert.False(string.IsNullOrWhiteSpace(actualMachineCode));

            DraftValidationResult draft = IssuerLicenseContractValidator.BuildAndValidateDraft(
                "SLRN-CAFE-TRUSTED-001",
                "Kirjane Labs Cafe QA",
                "CAFE-YZA-TRUSTED-01",
                IssuerConstants.ProductCafe,
                LicenseType.Host,
                LicenseEdition.Professional,
                actualMachineCode,
                LicenseFeatures.AllList,
                allowedLanTerminals: 2,
                hostLicenseId: null,
                terminalId: null,
                issuedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1),
                expiresAtUtc: DateTimeOffset.UtcNow.AddDays(30),
                supportExpiresAtUtc: null,
                updatesUntilUtc: null);
            Assert.True(draft.IsValid);

            LicenseSigningOutcome signed = prodSigner.SignAndVerify(draft.Document!);
            Assert.True(signed.IsSuccess);
            Assert.True(signed.VerifierPassed);
            Assert.True(signed.RuntimeTrustedVerifierPassed);
            Assert.Equal("ProductionTrustedSigner", signed.SignerMode);

            LocalLicenseInstallResponse installed = await defaultReleaseLicenseService.InstallAsync(
                new LocalLicenseInstallRequest(signed.SignedLicenseJson));
            Assert.Equal("Installed", installed.InstallStatus);
            Assert.Equal(LicenseStatus.Licensed, installed.ValidationStatus);

            LocalLicenseValidationResult verified = await defaultReleaseLicenseService.GetStatusAsync();
            Assert.Equal(LicenseStatus.Licensed, verified.Status);
            Assert.Equal(IssuerConstants.ProductCafe, verified.ApplicationId);
            Assert.Equal(LicenseType.Host, verified.LicenseType);
            Assert.True(verified.MachineMatched);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
