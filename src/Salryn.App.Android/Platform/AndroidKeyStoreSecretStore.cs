using System.Security.Cryptography;
using System.Text;
using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using Salryn.Mobile.Foundation.P0;
using KeyStore = Java.Security.KeyStore;

namespace Salryn.App.Android.Platform;

public sealed class AndroidKeyStoreSecretStore : IDeviceSecretStore
{
    private const string Provider = "AndroidKeyStore";
    private readonly string _ciphertextRoot;

    public AndroidKeyStoreSecretStore(string ciphertextRoot)
    {
        if (string.IsNullOrWhiteSpace(ciphertextRoot))
        {
            throw new ArgumentException("A protected-secret storage root is required.", nameof(ciphertextRoot));
        }

        _ciphertextRoot = Path.GetFullPath(ciphertextRoot);
    }

    public async Task<byte[]?> ReadAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string path = GetCiphertextPath(key);
        if (!File.Exists(path))
        {
            return null;
        }

        byte[] envelope = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        if (envelope.Length < sizeof(int))
        {
            throw new InvalidOperationException("Protected device secret envelope is invalid.");
        }

        using MemoryStream stream = new(envelope, writable: false);
        using BinaryReader reader = new(stream);

        int ivLength = reader.ReadInt32();
        if (ivLength <= 0 || ivLength > 64 || stream.Length - stream.Position <= ivLength)
        {
            throw new InvalidOperationException("Protected device secret IV is invalid.");
        }

        byte[] iv = reader.ReadBytes(ivLength);
        byte[] ciphertext = reader.ReadBytes(checked((int)(stream.Length - stream.Position)));

        using KeyStore keyStore = OpenKeyStore();
        Java.Security.IKey androidKey = GetOrCreateKey(keyStore, key);

        using Cipher cipher = Cipher.GetInstance("AES/GCM/NoPadding")
            ?? throw new InvalidOperationException("Android AES/GCM cipher is unavailable.");
        using GCMParameterSpec parameters = new(128, iv);
        cipher.Init(Javax.Crypto.CipherMode.DecryptMode, androidKey, parameters);

        return cipher.DoFinal(ciphertext);
    }

    public async Task WriteAsync(
        string key,
        byte[] secret,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(secret);
        cancellationToken.ThrowIfCancellationRequested();

        string targetPath = GetCiphertextPath(key);
        if (secret.Length == 0)
        {
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
            return;
        }

        Directory.CreateDirectory(_ciphertextRoot);

        using KeyStore keyStore = OpenKeyStore();
        Java.Security.IKey androidKey = GetOrCreateKey(keyStore, key);

        using Cipher cipher = Cipher.GetInstance("AES/GCM/NoPadding")
            ?? throw new InvalidOperationException("Android AES/GCM cipher is unavailable.");
        cipher.Init(Javax.Crypto.CipherMode.EncryptMode, androidKey);

        byte[] iv = cipher.GetIV()
            ?? throw new InvalidOperationException("Android AES/GCM cipher did not provide an IV.");
        byte[] ciphertext = cipher.DoFinal(secret)
            ?? throw new InvalidOperationException("Android AES/GCM encryption returned no ciphertext.");

        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(iv.Length);
            writer.Write(iv);
            writer.Write(ciphertext);
            writer.Flush();
        }

        string tempPath = targetPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(
                tempPath,
                stream.ToArray(),
                cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, targetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
    }

    private static KeyStore OpenKeyStore()
    {
        KeyStore keyStore = KeyStore.GetInstance(Provider)
            ?? throw new InvalidOperationException("Android Keystore provider is unavailable.");
        keyStore.Load(null);
        return keyStore;
    }

    private static Java.Security.IKey GetOrCreateKey(KeyStore keyStore, string key)
    {
        string alias = GetKeyAlias(key);

        if (!keyStore.ContainsAlias(alias))
        {
            using KeyGenerator generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, Provider)
                ?? throw new InvalidOperationException("Android Keystore AES generator is unavailable.");

            using KeyGenParameterSpec spec = new KeyGenParameterSpec.Builder(
                    alias,
                    KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
                .SetBlockModes(KeyProperties.BlockModeGcm)
                .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
                .SetKeySize(256)
                .Build();

            generator.Init(spec);
            generator.GenerateKey();
        }

        return keyStore.GetKey(alias, null)
            ?? throw new InvalidOperationException("Android Keystore key could not be loaded.");
    }

    private string GetCiphertextPath(string key)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Path.Combine(_ciphertextRoot, Convert.ToHexString(digest) + ".bin");
    }

    private static string GetKeyAlias(string key)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return "ph.kirjanelabs.salryn.device." + Convert.ToHexString(digest);
    }
}
