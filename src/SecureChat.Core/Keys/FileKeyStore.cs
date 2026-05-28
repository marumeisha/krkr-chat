using System.Text;

namespace SecureChat.Core.Keys;

public sealed class FileKeyStore : IKeyStore
{
    private readonly string _baseDirectory;

    public FileKeyStore(string? baseDirectory = null)
    {
        _baseDirectory = string.IsNullOrWhiteSpace(baseDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SecureChat", "Keys")
            : Path.GetFullPath(baseDirectory);
    }

    public void SavePrivateKey(string keyId, byte[] privateKeyPkcs8)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        ArgumentNullException.ThrowIfNull(privateKeyPkcs8);

        Directory.CreateDirectory(_baseDirectory);
        File.WriteAllBytes(GetPrivateKeyPath(keyId), privateKeyPkcs8);
    }

    public byte[] LoadPrivateKey(string keyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        return File.ReadAllBytes(GetPrivateKeyPath(keyId));
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

    public void Rename(string currentKeyId, string newKeyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentKeyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newKeyId);

        if (string.Equals(currentKeyId, newKeyId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var currentPrivatePath = GetPrivateKeyPath(currentKeyId);
        var currentPublicPath = GetPublicKeyPath(currentKeyId);
        if (!File.Exists(currentPrivatePath) || !File.Exists(currentPublicPath))
        {
            return;
        }

        Directory.CreateDirectory(_baseDirectory);
        var newPrivatePath = GetPrivateKeyPath(newKeyId);
        var newPublicPath = GetPublicKeyPath(newKeyId);

        if (File.Exists(newPrivatePath))
        {
            File.Delete(newPrivatePath);
        }

        if (File.Exists(newPublicPath))
        {
            File.Delete(newPublicPath);
        }

        File.Move(currentPrivatePath, newPrivatePath);
        File.Move(currentPublicPath, newPublicPath);
    }

    private string GetPrivateKeyPath(string keyId) => Path.Combine(_baseDirectory, $"{SanitizeKeyId(keyId)}.private.pkcs8");

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