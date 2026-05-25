using System.Security.Cryptography;
using System.Text;

namespace SecureChat.Core.Keys;

public sealed class WindowsDpapiKeyStore : IKeyStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SecureChat-WindowsDpapiKeyStore-v1");
    private readonly string _baseDirectory;

    public WindowsDpapiKeyStore(string? baseDirectory = null)
    {
        _baseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SecureChat", "Keys")
            : Path.GetFullPath(baseDirectory);
    }

    public void SavePrivateKey(string keyId, byte[] privateKeyPkcs8)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        ArgumentNullException.ThrowIfNull(privateKeyPkcs8);

        Directory.CreateDirectory(_baseDirectory);
        var protectedBytes = ProtectedData.Protect(privateKeyPkcs8, Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(GetPrivateKeyPath(keyId), protectedBytes);
    }

    public byte[] LoadPrivateKey(string keyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);

        var path = GetPrivateKeyPath(keyId);
        var protectedBytes = File.ReadAllBytes(path);
        return ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
    }

    public bool Exists(string keyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        return File.Exists(GetPrivateKeyPath(keyId)) && File.Exists(GetPublicKeyPath(keyId));
    }

    public void SavePublicKey(string keyId, string publicKeyPem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        ArgumentNullException.ThrowIfNull(publicKeyPem);

        Directory.CreateDirectory(_baseDirectory);
        File.WriteAllText(GetPublicKeyPath(keyId), publicKeyPem, Encoding.UTF8);
    }

    public string LoadPublicKey(string keyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        return File.ReadAllText(GetPublicKeyPath(keyId), Encoding.UTF8);
    }

    private string GetPrivateKeyPath(string keyId) => Path.Combine(_baseDirectory, $"{SanitizeKeyId(keyId)}.private.pkcs8.dpapi");

    private string GetPublicKeyPath(string keyId) => Path.Combine(_baseDirectory, $"{SanitizeKeyId(keyId)}.public.pem");

    private static string SanitizeKeyId(string keyId)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(keyId.Length);

        foreach (var ch in keyId)
        {
            builder.Append(invalidChars.Contains(ch) ? '_' : ch);
        }

        return builder.ToString();
    }
}
