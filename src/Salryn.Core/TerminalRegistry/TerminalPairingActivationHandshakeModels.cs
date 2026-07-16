using System.Security.Cryptography;
using System.Text;

namespace Salryn.Core.TerminalRegistry;

public enum RemoteClientHandshakeStatus
{
    Pending = 1,
    Succeeded = 2,
    Failed = 3,
    Expired = 4,
    Revoked = 5
}

public enum RemoteClientAllowedCategory
{
    StatusRead = 1,
    ProductRead = 2,
    DashboardRead = 3,
    TerminalStatusRead = 4
}

public sealed record TerminalPairingActivationRequest(
    Guid PairingId,
    Guid StoreId,
    Guid HostId,
    string RequestedTerminalName,
    TerminalRegistrationRole RequestedTerminalRole,
    string RawPairingCode,
    string? ClientMachineName,
    string? ClientFingerprint,
    DateTimeOffset RequestedAtUtc,
    string OriginAddress,
    string? TerminalLicenseId = null,
    string? LicenseTerminalId = null,
    string? LicenseMachineCode = null,
    DateTimeOffset? TerminalLicenseExpiresAt = null);

public static class TerminalLicenseTrustMetadata
{
    private const string Prefix = "[TerminalLicenseProof:v1;LicenseId=";
    private const string TerminalIdMarker = ";TerminalId=";
    private const string MachineCodeMarker = ";MachineCode=";
    private const string ExpiresAtMarker = ";ExpiresAt=";
    private const string Suffix = "]";

    public static string Build(string licenseId, string terminalId, string machineCode, DateTimeOffset? expiresAt) =>
        Prefix + Uri.EscapeDataString(licenseId.Trim())
        + TerminalIdMarker + Uri.EscapeDataString(terminalId.Trim())
        + MachineCodeMarker + Uri.EscapeDataString(machineCode.Trim())
        + ExpiresAtMarker + Uri.EscapeDataString(expiresAt?.ToString("O") ?? "none")
        + Suffix;

    public static bool TryRead(
        string? notes,
        out string licenseId,
        out string terminalId,
        out string machineCode,
        out DateTimeOffset? expiresAt)
    {
        licenseId = string.Empty;
        terminalId = string.Empty;
        machineCode = string.Empty;
        expiresAt = null;
        if (string.IsNullOrWhiteSpace(notes))
        {
            return false;
        }

        int start = notes.IndexOf(Prefix, StringComparison.Ordinal);
        int terminalMarker = start < 0 ? -1 : notes.IndexOf(TerminalIdMarker, start + Prefix.Length, StringComparison.Ordinal);
        int machineMarker = terminalMarker < 0 ? -1 : notes.IndexOf(MachineCodeMarker, terminalMarker + TerminalIdMarker.Length, StringComparison.Ordinal);
        int expiryMarker = machineMarker < 0 ? -1 : notes.IndexOf(ExpiresAtMarker, machineMarker + MachineCodeMarker.Length, StringComparison.Ordinal);
        int end = expiryMarker < 0 ? -1 : notes.IndexOf(Suffix, expiryMarker + ExpiresAtMarker.Length, StringComparison.Ordinal);
        if (start < 0 || terminalMarker < 0 || machineMarker < 0 || expiryMarker < 0 || end < 0)
        {
            return false;
        }

        licenseId = Uri.UnescapeDataString(notes[(start + Prefix.Length)..terminalMarker]);
        terminalId = Uri.UnescapeDataString(notes[(terminalMarker + TerminalIdMarker.Length)..machineMarker]);
        machineCode = Uri.UnescapeDataString(notes[(machineMarker + MachineCodeMarker.Length)..expiryMarker]);
        string expiryText = Uri.UnescapeDataString(notes[(expiryMarker + ExpiresAtMarker.Length)..end]);
        if (!string.Equals(expiryText, "none", StringComparison.Ordinal))
        {
            if (!DateTimeOffset.TryParse(expiryText, out DateTimeOffset parsedExpiry))
            {
                return false;
            }

            expiresAt = parsedExpiry;
        }
        return !string.IsNullOrWhiteSpace(licenseId)
            && !string.IsNullOrWhiteSpace(terminalId)
            && !string.IsNullOrWhiteSpace(machineCode);
    }
}

public sealed record TerminalPairingActivationResult(
    bool Succeeded,
    Guid? TerminalId,
    string? TerminalName,
    TerminalRegistrationRole? TerminalRole,
    TerminalRegistrationStatus? TerminalStatus,
    string? OneTimeTerminalCredential,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static TerminalPairingActivationResult Success(
        SalrynTerminalRegistration terminal,
        string oneTimeTerminalCredential) =>
        new(
            true,
            terminal.TerminalId,
            terminal.TerminalName,
            terminal.TerminalRole,
            terminal.Status,
            oneTimeTerminalCredential,
            null,
            null);

    public static TerminalPairingActivationResult Failure(string errorCode, string errorMessage) =>
        new(false, null, null, null, null, null, errorCode, errorMessage);
}

public sealed record RemoteClientHandshakeRequest(
    Guid TerminalId,
    Guid StoreId,
    Guid HostId,
    string RawTerminalCredential,
    string OriginAddress,
    DateTimeOffset RequestedAtUtc,
    string? ClientMachineName,
    string? ClientFingerprint);

public sealed record RemoteClientHandshakeResult(
    bool Succeeded,
    Guid TerminalId,
    TerminalRegistrationStatus? TerminalStatus,
    Guid? HandshakeSessionId,
    DateTimeOffset? SessionExpiresAtUtc,
    IReadOnlyList<RemoteClientAllowedCategory> AllowedRemoteCategories,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static RemoteClientHandshakeResult Success(
        Guid terminalId,
        TerminalRegistrationStatus terminalStatus,
        Guid handshakeSessionId,
        DateTimeOffset sessionExpiresAtUtc,
        IReadOnlyList<RemoteClientAllowedCategory> allowedRemoteCategories) =>
        new(
            true,
            terminalId,
            terminalStatus,
            handshakeSessionId,
            sessionExpiresAtUtc,
            allowedRemoteCategories,
            null,
            null);

    public static RemoteClientHandshakeResult Failure(
        Guid terminalId,
        TerminalRegistrationStatus? terminalStatus,
        string errorCode,
        string errorMessage) =>
        new(false, terminalId, terminalStatus, null, null, Array.Empty<RemoteClientAllowedCategory>(), errorCode, errorMessage);
}

public sealed record RemoteTerminalCredential(
    Guid TerminalId,
    Guid StoreId,
    Guid HostId,
    string CredentialHash,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset? LastUsedAtUtc,
    DateTimeOffset? RevokedAtUtc,
    Guid? CreatedFromPairingId,
    Guid? CreatedByUserId);

public sealed record RemoteClientHandshakeSession(
    Guid HandshakeId,
    Guid TerminalId,
    Guid StoreId,
    Guid HostId,
    string OriginAddress,
    string? ClientMachineName,
    string? ClientFingerprint,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    RemoteClientHandshakeStatus Status);

public static class TerminalCredentialSecretHasher
{
    public static string GenerateTerminalCredential()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static string HashTerminalCredential(string credential)
    {
        if (string.IsNullOrWhiteSpace(credential))
        {
            throw new ArgumentException("Terminal credential is required.", nameof(credential));
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes("salryn-terminal-credential-v1:" + credential.Trim()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
