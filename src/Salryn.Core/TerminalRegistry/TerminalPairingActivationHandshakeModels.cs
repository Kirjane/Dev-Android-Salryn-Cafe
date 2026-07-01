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
    ProductRead = 2
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
    string OriginAddress);

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
