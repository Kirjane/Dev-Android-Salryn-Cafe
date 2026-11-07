using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Salryn.Core.Services.Licensing;
using Salryn.Infrastructure.Licensing;

namespace Salryn.Tests;

public static class PreILicenseKeyRotationCanonicalBetaChecks
{
    private const string Gate = "pre-i-license-key-rotation-canonical-beta";
    private const string CanonicalFingerprint = "97bd9399360ec1bb95e52abf7b02acf9c195852afa8dd837ffa454d735d36a59";
    private const string RetiredFingerprint = "10149b43f8dcb6c82e4c8fdd538572faaa78fd126af2caaa29cad997e67bc284";
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 18, 16, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void Run() => RunAsync().GetAwaiter().GetResult();

    private static async Task RunAsync()
    {
        Console.WriteLine($"=== {Gate} ===");
        string repo = FindRoot();
        string fixtureRoot = Path.Combine(repo, "tests", "Salryn.Tests", "Fixtures", "CanonicalBeta");
        string hostJson = File.ReadAllText(Path.Combine(fixtureRoot, "host-license.json"));
        string terminalJson = File.ReadAllText(Path.Combine(fixtureRoot, "terminal-license.json"));
        LocalLicenseDocument host = DeserializeFlat(hostJson, LicenseType.Host);
        LocalLicenseDocument terminal = DeserializeFlat(terminalJson, LicenseType.Terminal);

        Require(LocalLicenseSignatureVerifier.TrustedPublicKeySpkiSha256 == CanonicalFingerprint, "embedded fingerprint constant");
        Require(LocalLicenseSignatureVerifier.GetTrustedPublicKeySpkiSha256() == CanonicalFingerprint, "embedded public key SPKI fingerprint");
        Require(LocalLicenseSignatureVerifier.Verify(host), "fresh canonical Host signature");
        Require(LocalLicenseSignatureVerifier.Verify(terminal), "fresh canonical Terminal signature");
        Require(terminal.HostLicenseId == host.LicenseId, "Terminal HostLicenseId must match canonical Host fixture");

        string root = Path.Combine(Path.GetTempPath(), "salryn-canonical-beta-" + Guid.NewGuid().ToString("N"));
        try
        {
            LocalLicenseService hostService = Service(root, host.MachineBinding!);
            LocalLicenseInstallResponse installed = await hostService.InstallAsync(new(hostJson));
            Require(installed.InstallStatus == "Installed" && installed.ValidationStatus == LicenseStatus.Licensed, "canonical Host install");

            string installedBeforeReject = File.ReadAllText(Path.Combine(root, LocalLicenseFileStore.DefaultFileName));
            LocalLicenseInstallResponse wrapper = await hostService.InstallAsync(new("{\"document\":" + hostJson + "}"));
            Require(wrapper.InstallStatus == "Rejected" && wrapper.ErrorCode == "License.Install.UnsupportedField", "wrapper JSON must fail closed");
            Require(File.ReadAllText(Path.Combine(root, LocalLicenseFileStore.DefaultFileName)) == installedBeforeReject, "rejected wrapper must not replace installed license");

            LocalLicenseInstallResponse wrongMachine = await Service(Path.Combine(root, "wrong"), "SALRYN-MC-CB01-0000-0000-9999").InstallAsync(new(hostJson));
            Require(wrongMachine.InstallStatus == "Rejected", "wrong machine binding must fail");

            string tamperedJson = hostJson.Replace("Canonical Beta Fixture", "Tampered Fixture", StringComparison.Ordinal);
            LocalLicenseDocument tampered = JsonSerializer.Deserialize<LocalLicenseDocument>(tamperedJson, JsonOptions)!;
            Require(!LocalLicenseSignatureVerifier.Verify(tampered), "tampered signed fields must fail signature verification");
            Require((await Service(Path.Combine(root, "tampered"), host.MachineBinding!).InstallAsync(new(tamperedJson))).InstallStatus == "Rejected", "tampered Host install must fail");

            LocalLanTerminalEntitlementService entitlements = new(
                hostService,
                signatureVerifier: LocalLicenseSignatureVerifier.Verify,
                utcNow: () => FixedNow);
            LocalLanTerminalEntitlementStatus hostEntitlement = await entitlements.GetHostEntitlementAsync();
            Require(hostEntitlement.HostOperationalLanAllowed, "canonical Host must unlock Host Terminal Management");
            Require(entitlements.ValidateTerminalLicenseProof(terminalJson, terminal.MachineBinding, hostEntitlement).IsSuccess, "canonical Terminal proof");
            Require(entitlements.ValidateTerminalLicenseProof(terminalJson, "SALRYN-MC-CB01-0000-0000-9999", hostEntitlement).IsFailure, "Terminal wrong-machine rejection");
            Require(entitlements.ValidateTerminalLicenseProof(hostJson, host.MachineBinding, hostEntitlement).IsFailure, "Host-as-Terminal rejection");

            LocalLicenseService terminalService = Service(Path.Combine(root, "terminal"), terminal.MachineBinding!);
            Require((await terminalService.InstallAsync(new(terminalJson))).InstallStatus == "Installed", "canonical Terminal local validation");
            LicenseEnforcementDecision hostFeature = await new LocalLicenseEnforcementService(terminalService).RequireFeatureAsync(LicenseFeatures.Pos);
            Require(!hostFeature.Allowed && hostFeature.ReasonCode == LicenseEnforcementReason.WrongLicenseType, "Terminal must not unlock Host POS feature");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }

        string verifierSource = File.ReadAllText(Path.Combine(repo, "src", "Salryn.Infrastructure", "Licensing", "LocalLicenseSignatureVerifier.cs"));
        Require(!verifierSource.Contains(RetiredFingerprint, StringComparison.Ordinal), "retired key fingerprint must not remain in verifier");
        Require(!verifierSource.Contains("fallback", StringComparison.OrdinalIgnoreCase), "no fallback verification key");
        AssertNoCommittedPrivateKey(repo);
        Console.WriteLine($"PASS {Gate}");
    }

    private static LocalLicenseDocument DeserializeFlat(string raw, LicenseType expectedType)
    {
        using JsonDocument parsed = JsonDocument.Parse(raw);
        Require(parsed.RootElement.ValueKind == JsonValueKind.Object, "fixture root must be object");
        foreach (string wrapper in new[] { "document", "license", "metadata", "outputPath" })
            Require(!parsed.RootElement.TryGetProperty(wrapper, out _), "fixture must not contain wrapper field " + wrapper);
        LocalLicenseDocument document = JsonSerializer.Deserialize<LocalLicenseDocument>(raw, JsonOptions)
            ?? throw new InvalidOperationException("fixture did not deserialize");
        Require(document.LicenseType == expectedType, "fixture LicenseType " + expectedType);
        return document;
    }

    private static LocalLicenseService Service(string root, string machine) => new(
        new LocalLicenseFileStore(new LocalLicenseFileStoreOptions(root, null)),
        new LocalLicenseValidator(new LocalLicenseValidatorOptions
        {
            MachineFingerprint = machine,
            SignatureVerifier = LocalLicenseSignatureVerifier.Verify
        }, () => FixedNow),
        () => FixedNow);

    private static void AssertNoCommittedPrivateKey(string repo)
    {
        ProcessStartInfo psi = new("git", "ls-files") { WorkingDirectory = repo, RedirectStandardOutput = true, UseShellExecute = false };
        using Process process = Process.Start(psi) ?? throw new InvalidOperationException("could not inspect tracked files");
        string[] tracked = process.StandardOutput.ReadToEnd().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        process.WaitForExit();
        Require(process.ExitCode == 0, "git ls-files");
        Require(!tracked.Any(p => p.StartsWith(".secrets/", StringComparison.OrdinalIgnoreCase)), ".secrets must not be committed");
        Require(!tracked.Any(p => Path.GetFileName(p).Contains("private", StringComparison.OrdinalIgnoreCase)
            && new[] { ".pem", ".key", ".pfx", ".p12" }.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase)), "private-key file must not be tracked");
        string privateArmor = "-----BEGIN " + "PRIVATE KEY-----";
        string rsaPrivateArmor = "-----BEGIN RSA " + "PRIVATE KEY-----";
        foreach (string relative in tracked.Where(p => new[] { ".pem", ".key" }.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase)))
        {
            string text = File.ReadAllText(Path.Combine(repo, relative));
            Require(!text.Contains(privateArmor, StringComparison.Ordinal) && !text.Contains(rsaPrivateArmor, StringComparison.Ordinal), "private-key PEM armor in tracked file " + relative);
        }
    }

    private static string FindRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Salryn.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Salryn repo root not found");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(Gate + ": " + message);
    }
}
