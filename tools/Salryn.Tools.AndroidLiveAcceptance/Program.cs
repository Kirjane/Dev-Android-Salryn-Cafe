using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Salryn.Cafe.I6ATests;
using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.Security;
using Salryn.Infrastructure.Data.Repositories.Menu;
using Salryn.Infrastructure.Data.Repositories.Ingredients;
using Salryn.Sync.Central.Server;

// This executable creates a new synthetic root every run. It never accepts an existing database path.
string evidenceParent = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetTempPath();
string root = Path.Combine(evidenceParent, "salryn-android-live-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var db = C4ProofDatabase.Create(Path.Combine(root, "central"));
const string operatorPassword = "Android-Synthetic-Operator!2026";
db.Execute("UPDATE users SET password_hash=$hash WHERE id=$id;", ("$hash", SalrynPasswordHasher.HashPassword(operatorPassword).Value), ("$id", C4ProofIds.ActorUserId.ToString("D")));
// Genuine publications through runtime APIs supersede inherited proof snapshots with placeholder hashes.
var menu = new SqliteMenuCatalogStore(db.Factory);
Require(menu.SaveVariant(new(C4ProofIds.VariantId, C4ProofIds.ProductId, "Large", null, null, 650, 300, true, false, 0)), "variant");
Require(menu.PublishCurrent(), "menu publication");
var recipes = new SqliteIngredientRecipeStore(db.Factory);
Require(recipes.SaveRecipe(new("android-live-coffee", "Synthetic Coffee Recipe", 1000, true)), "recipe");
Require(recipes.SaveRecipeLine(new("android-live-coffee", C4ProofIds.ItemIngredientId, "Piece", 1000, 0)), "recipe ingredient");
RequireSimple(recipes.SetItemRecipe(C4ProofIds.ProductId, "android-live-coffee"), "item recipe binding");
Require(recipes.PublishCurrent(), "recipe publication");
db.Execute("""
    INSERT INTO customers(id,full_name,is_active,created_by_user_id,created_at,updated_at)
    VALUES($id,'Synthetic Android Customer',1,$actor,$at,$at);
    """, ("$id", Guid.NewGuid().ToString("D")), ("$actor", C4ProofIds.ActorUserId.ToString("D")), ("$at", DateTimeOffset.UtcNow.ToString("O")));
Guid tenant = Guid.NewGuid();
var options = new CentralServerOptions {
    DatabasePath = db.Paths.DatabasePath, EnableCanonicalDomainProcessing = true,
    CanonicalTenantId = tenant, CanonicalStoreId = C4ProofIds.StoreId,
    RequireHttps = true, TrustForwardedProto = false,
    BootstrapAdmin = new() { TenantId = tenant, StoreId = C4ProofIds.StoreId,
        Username = "android-qa-owner", DisplayName = "Synthetic Android QA Owner", Role = "Owner", Password = "Android-Synthetic-Owner!2026" }
};
var central = new CentralSyncDatabase(options); central.Initialize();
var service = new CentralSyncService(central, options);
var owner = service.AdminLogin(new(options.BootstrapAdmin.Username, options.BootstrapAdmin.Password));
if (owner.Value is null) throw new Exception("Synthetic administrator login failed.");
var invite = service.CreateInvite(owner.Value.Session, new("OfflineAndroidCashier"));
if (invite.Value is null) throw new Exception("Synthetic device invite failed.");
using RSA caKey = RSA.Create(2048);
var caRequest = new CertificateRequest("CN=Salryn Disposable Android QA CA", caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
caRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(caRequest.PublicKey, false));
using X509Certificate2 ca = caRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
using RSA tlsKey = RSA.Create(2048);
var tlsRequest = new CertificateRequest("CN=localhost", tlsKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
var san = new SubjectAlternativeNameBuilder(); san.AddDnsName("localhost"); san.AddIpAddress(IPAddress.Loopback); san.AddIpAddress(IPAddress.Parse("10.0.2.2"));
tlsRequest.CertificateExtensions.Add(san.Build());
tlsRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(tlsRequest.PublicKey, false));
tlsRequest.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(ca, true, false));
tlsRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
tlsRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
var usages = new OidCollection { new("1.3.6.1.5.5.7.3.1") }; tlsRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, true));
using X509Certificate2 issued = tlsRequest.Create(ca, DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow.AddHours(12), RandomNumberGenerator.GetBytes(16));
using X509Certificate2 ephemeralTls = issued.CopyWithPrivateKey(tlsKey);
// Windows Schannel needs a provider-backed key handle; normal UserKeySet import is scoped to this certificate lifetime.
using X509Certificate2 tls = X509CertificateLoader.LoadPkcs12(ephemeralTls.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.UserKeySet);
string hash = BitConverter.ToUInt32(MD5.HashData(ca.SubjectName.RawData), 0).ToString("x8");
string caPath = Path.Combine(root, hash + ".0"); await File.WriteAllTextAsync(caPath, ca.ExportCertificatePem());
var builder = WebApplication.CreateBuilder();
builder.Logging.ClearProviders(); builder.Logging.AddConsole();
builder.Logging.AddFilter("Microsoft.AspNetCore.Server.Kestrel", LogLevel.Debug);
builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 5443, l => l.UseHttps(tls)));
builder.Services.AddSingleton(options); builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(central); builder.Services.AddSingleton(service);
await using var app = builder.Build(); app.MapCentralSyncEndpoints();
// Marker is restricted to this disposable recovery directory. Never commit generated credentials or signing material.
string publicKey = db.ScalarString("SELECT lease_signing_public_key_spki FROM sync_server_state LIMIT 1;")!;
await File.WriteAllTextAsync(Path.Combine(root, "disposable-android-marker.json"), JsonSerializer.Serialize(new {
    isDisposableTest = true, root, databasePath = db.Paths.DatabasePath, processId = Environment.ProcessId,
    androidUrl = "https://10.0.2.2:5443", loopbackUrl = "https://127.0.0.1:5443", tenantId = tenant, storeId = C4ProofIds.StoreId,
    pinnedServerKey = publicKey, joinCode = invite.Value.JoinCode, caPath,
    operatorUsername = "c4-proof", operatorPassword,
    adminUsername = options.BootstrapAdmin.Username, adminPassword = options.BootstrapAdmin.Password
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("DISPOSABLE_ANDROID_MARKER=" + Path.Combine(root, "disposable-android-marker.json"));
await app.RunAsync();
static void Require<T>(Result<T> result, string label) { if (result.IsFailure) throw new Exception(label + ": " + result.ErrorDetail.Message); }
static void RequireSimple(Result result, string label) { if (result.IsFailure) throw new Exception(label + ": " + result.ErrorDetail.Message); }
