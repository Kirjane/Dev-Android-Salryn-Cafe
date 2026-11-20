using System.Security.Cryptography;

namespace Salryn.Mobile.Foundation.P0;

public interface IDeviceSecretStore
{
    Task<byte[]?> ReadAsync(string key, CancellationToken cancellationToken = default);

    Task WriteAsync(string key, byte[] secret, CancellationToken cancellationToken = default);
}

public sealed record P0DeviceIdentity(Guid DeviceId, byte[] AuthenticationSecret);

public sealed class DeviceIdentityService
{
    public const string AuthenticationSecretKey = "salryn-device-auth-secret-v1";

    private readonly string _identityRoot;
    private readonly IDeviceSecretStore _secretStore;

    public DeviceIdentityService(string identityRoot, IDeviceSecretStore secretStore)
    {
        if (string.IsNullOrWhiteSpace(identityRoot))
        {
            throw new ArgumentException("An identity storage root is required.", nameof(identityRoot));
        }

        _identityRoot = Path.GetFullPath(identityRoot);
        _secretStore = secretStore ?? throw new ArgumentNullException(nameof(secretStore));
    }

    public async Task<P0DeviceIdentity> GetOrCreateAsync(
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_identityRoot);

        Guid deviceId = await GetOrCreatePublicDeviceIdAsync(cancellationToken).ConfigureAwait(false);
        byte[]? secret = await _secretStore
            .ReadAsync(AuthenticationSecretKey, cancellationToken)
            .ConfigureAwait(false);

        if (secret is null || secret.Length == 0)
        {
            secret = RandomNumberGenerator.GetBytes(32);
            await _secretStore
                .WriteAsync(AuthenticationSecretKey, secret, cancellationToken)
                .ConfigureAwait(false);
        }

        return new P0DeviceIdentity(deviceId, secret.ToArray());
    }

    private async Task<Guid> GetOrCreatePublicDeviceIdAsync(CancellationToken cancellationToken)
    {
        string path = Path.Combine(_identityRoot, "device-id.txt");

        if (File.Exists(path))
        {
            string existing = (await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false)).Trim();
            if (Guid.TryParse(existing, out Guid parsed) && parsed != Guid.Empty)
            {
                return parsed;
            }

            throw new InvalidOperationException("Stored P0 device identifier is invalid.");
        }

        Guid created = Guid.NewGuid();
        await File.WriteAllTextAsync(path, created.ToString("D"), cancellationToken).ConfigureAwait(false);
        return created;
    }
}
