using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Salryn.Core.Services.Licensing;
using Salryn.Infrastructure.Licensing;

namespace Salryn.Tests;

public static class PreICanonicalLicenseActivationProofChecks
{
    private const string Gate = "pre-i-canonical-license-activation-proof";
    private const string CanonicalFingerprint = "30153dcb1de6cbfa775316292f93ba50eab0dd89e8eae84d785cbfd0168008f4";
    private const string ManifestEnvironmentVariable = "SALRYN_CANONICAL_LICENSE_PROOF_MANIFEST";
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
        string manifestPath = ResolveManifestPath();
        string manifestRaw = File.ReadAllText(manifestPath);
        CanonicalActivationProofManifest manifest = JsonSerializer.Deserialize<CanonicalActivationProofManifest>(manifestRaw, JsonOptions)
            ?? throw new InvalidOperationException(Gate + ": proof manifest did not deserialize");
        Require(manifest.SchemaVersion == 1, "manifest schema version");
        Require(manifest.CanonicalPublicSpkiSha256 == CanonicalFingerprint, "manifest canonical fingerprint");
        Require(Path.IsPathFullyQualified(manifest.HostLicensePath) && Path.IsPathFullyQualified(manifest.TerminalLicensePath), "proof license paths must be absolute");
        Require(File.Exists(manifest.HostLicensePath) && File.Exists(manifest.TerminalLicensePath), "proof license files must exist");
        Require(File.Exists(manifest.RetiredOldKeyHostLicensePath), "retired old-key fixture must exist");
        Require(!Path.GetFullPath(manifest.HostLicensePath).Equals(Path.GetFullPath(manifest.RetiredOldKeyHostLicensePath), StringComparison.OrdinalIgnoreCase), "old-key fixture must not be current Host proof");

        string hostJson = File.ReadAllText(manifest.HostLicensePath);
        string terminalJson = File.ReadAllText(manifest.TerminalLicensePath);
        string oldKeyJson = File.ReadAllText(manifest.RetiredOldKeyHostLicensePath);
        LocalLicenseDocument host = ReadFlat(hostJson, LicenseType.Host, "Host");
        LocalLicenseDocument terminal = ReadFlat(terminalJson, LicenseType.Terminal, "Terminal");
        LocalLicenseDocument oldKeyHost = ReadFlat(oldKeyJson, LicenseType.Host, "retired Host");

        Require(LocalLicenseSignatureVerifier.TrustedPublicKeySpkiSha256 == CanonicalFingerprint, "embedded fingerprint constant");
        Require(LocalLicenseSignatureVerifier.GetTrustedPublicKeySpkiSha256() == CanonicalFingerprint, "embedded public key fingerprint");
        Require(host.MachineBinding == manifest.HostMachineCode, "Host machine manifest linkage");
        Require(terminal.MachineBinding == manifest.TerminalMachineCode, "Terminal machine manifest linkage");
        Require(terminal.HostLicenseId == host.LicenseId, "Terminal-from-Host linkage");
        Require(LocalLicenseSignatureVerifier.Verify(host), "fresh Host signature");
        Require(LocalLicenseSignatureVerifier.Verify(terminal), "fresh Terminal signature");
        Require(!LocalLicenseSignatureVerifier.Verify(oldKeyHost), "historical old-key license must fail closed");

        string isolated = Path.Combine(Path.GetTempPath(), "salryn-cross-repo-activation-" + Guid.NewGuid().ToString("N"));
        try
        {
            LocalLicenseService hostService = Service(isolated, manifest.HostMachineCode);
            LocalLicenseInstallResponse hostInstall = await hostService.InstallAsync(new(hostJson));
            Require(hostInstall.InstallStatus == "Installed" && hostInstall.ValidationStatus == LicenseStatus.Licensed, "fresh Host import/activation");

            string installedBeforeReject = File.ReadAllText(Path.Combine(isolated, LocalLicenseFileStore.DefaultFileName));
            LocalLicenseInstallResponse wrapper = await hostService.InstallAsync(new("{\"license\":" + hostJson + ",\"metadata\":{}}"));
            Require(wrapper.InstallStatus == "Rejected", "wrapper JSON rejection");
            Require(File.ReadAllText(Path.Combine(isolated, LocalLicenseFileStore.DefaultFileName)) == installedBeforeReject, "wrapper rejection must preserve installed Host");

            string tamperedJson = hostJson.Replace(host.CustomerName!, host.CustomerName + " Tampered", StringComparison.Ordinal);
            Require(!LocalLicenseSignatureVerifier.Verify(JsonSerializer.Deserialize<LocalLicenseDocument>(tamperedJson, JsonOptions)!), "tampered Host signature rejection");
            Require((await Service(Path.Combine(isolated, "tampered"), manifest.HostMachineCode).InstallAsync(new(tamperedJson))).InstallStatus == "Rejected", "tampered Host import rejection");
            Require((await Service(Path.Combine(isolated, "wrong-host-machine"), "SALRYN-MC-DEAD-BEEF-0000-0001").InstallAsync(new(hostJson))).InstallStatus == "Rejected", "wrong Host machine rejection");
            Require((await Service(Path.Combine(isolated, "old-key"), oldKeyHost.MachineBinding!).InstallAsync(new(oldKeyJson))).InstallStatus == "Rejected", "old-key Host import rejection");

            LocalLanTerminalEntitlementService terminalPath = new(hostService, signatureVerifier: LocalLicenseSignatureVerifier.Verify);
            LocalLanTerminalEntitlementStatus hostEntitlement = await terminalPath.GetHostEntitlementAsync();
            Require(hostEntitlement.HostOperationalLanAllowed, "fresh Host entitlement");
            Require(terminalPath.ValidateTerminalLicenseProof(terminalJson, manifest.TerminalMachineCode, hostEntitlement).IsSuccess, "fresh Terminal activation proof");
            Require(terminalPath.ValidateTerminalLicenseProof(terminalJson, "SALRYN-MC-DEAD-BEEF-0000-0002", hostEntitlement).IsFailure, "wrong Terminal machine rejection");
            Require(terminalPath.ValidateTerminalLicenseProof(hostJson, manifest.HostMachineCode, hostEntitlement).IsFailure, "Host-as-Terminal rejection");

            LocalLicenseService terminalService = Service(Path.Combine(isolated, "terminal"), manifest.TerminalMachineCode);
            Require((await terminalService.InstallAsync(new(terminalJson))).InstallStatus == "Installed", "fresh Terminal local validation");
            LocalLanTerminalEntitlementStatus terminalAsHost = await new LocalLanTerminalEntitlementService(terminalService).GetHostEntitlementAsync();
            Require(!terminalAsHost.HostOperationalLanAllowed, "Terminal-as-Host entitlement rejection");
            LicenseEnforcementDecision hostPos = await new LocalLicenseEnforcementService(terminalService).RequireFeatureAsync(LicenseFeatures.Pos);
            Require(!hostPos.Allowed && hostPos.ReasonCode == LicenseEnforcementReason.WrongLicenseType, "Terminal-as-Host POS rejection");
        }
        finally
        {
            if (Directory.Exists(isolated)) Directory.Delete(isolated, recursive: true);
        }

        AssertSecretSafety(repo, manifestRaw);
        Console.WriteLine("PROOF_MANIFEST=" + manifestPath);
        Console.WriteLine($"PASS {Gate}");
    }

    private static LocalLicenseService Service(string root, string machine) => new(
        new LocalLicenseFileStore(new LocalLicenseFileStoreOptions(root, null)),
        new LocalLicenseValidator(new LocalLicenseValidatorOptions
        {
            MachineFingerprint = machine,
            SignatureVerifier = LocalLicenseSignatureVerifier.Verify
        }, () => DateTimeOffset.UtcNow),
        () => DateTimeOffset.UtcNow);

    private static LocalLicenseDocument ReadFlat(string raw, LicenseType expectedType, string label)
    {
        using JsonDocument parsed = JsonDocument.Parse(raw);
        Require(parsed.RootElement.ValueKind == JsonValueKind.Object, label + " root object");
        foreach (string wrapper in new[] { "document", "license", "metadata", "outputPath" })
            Require(!parsed.RootElement.TryGetProperty(wrapper, out _), label + " wrapper field " + wrapper);
        LocalLicenseDocument document = JsonSerializer.Deserialize<LocalLicenseDocument>(raw, JsonOptions)
            ?? throw new InvalidOperationException(Gate + ": " + label + " did not deserialize");
        Require(document.LicenseType == expectedType, label + " LicenseType");
        return document;
    }

    private static string ResolveManifestPath()
    {
        string? explicitPath = Environment.GetEnvironmentVariable(ManifestEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            string full = Path.GetFullPath(explicitPath.Trim());
            Require(File.Exists(full), "explicit proof manifest not found: " + full);
            return full;
        }

        DirectoryInfo temp = new(Path.GetTempPath());
        FileInfo? latest = temp.EnumerateDirectories("salryn-canonical-license-activation-proof-*")
            .Select(d => new FileInfo(Path.Combine(d.FullName, "manifest.json")))
            .Where(f => f.Exists)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();
        return latest?.FullName ?? throw new InvalidOperationException(
            Gate + ": no proof manifest found. Run `npm run proof:canonical-activation` in Salryn.LicensingOps or set " + ManifestEnvironmentVariable + ".");
    }

    private static void AssertSecretSafety(string repo, string manifestRaw)
    {
        ProcessStartInfo psi = new("git", "ls-files") { WorkingDirectory = repo, RedirectStandardOutput = true, UseShellExecute = false };
        using Process process = Process.Start(psi) ?? throw new InvalidOperationException("could not inspect tracked files");
        string[] tracked = process.StandardOutput.ReadToEnd().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        process.WaitForExit();
        Require(process.ExitCode == 0, "git ls-files");
        Require(!tracked.Any(p => p.StartsWith(".secrets/", StringComparison.OrdinalIgnoreCase)), ".secrets must not be tracked");
        Require(!tracked.Any(p => Path.GetFileName(p).Contains("private", StringComparison.OrdinalIgnoreCase)
            && new[] { ".pem", ".key", ".pfx", ".p12" }.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase)), "private-key file must not be tracked");
        string privateArmor = "-----BEGIN " + "PRIVATE KEY-----";
        string rsaPrivateArmor = "-----BEGIN RSA " + "PRIVATE KEY-----";
        Require(!manifestRaw.Contains(privateArmor, StringComparison.Ordinal) && !manifestRaw.Contains(rsaPrivateArmor, StringComparison.Ordinal), "manifest private-key PEM armor");
        Require(!manifestRaw.Contains("beta-signing-private.pem", StringComparison.OrdinalIgnoreCase), "manifest private-key path");
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

    private sealed record CanonicalActivationProofManifest(
        int SchemaVersion,
        string CanonicalPublicSpkiSha256,
        string HostLicensePath,
        string TerminalLicensePath,
        string HostMachineCode,
        string TerminalMachineCode,
        string RetiredOldKeyHostLicensePath,
        DateTimeOffset CreatedAtUtc);
}
