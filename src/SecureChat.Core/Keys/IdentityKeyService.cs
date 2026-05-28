using System.Security.Cryptography;

namespace SecureChat.Core.Keys;

public sealed class IdentityKeyService
{
    private readonly IKeyStore _keyStore;

    public IdentityKeyService(IKeyStore keyStore)
    {
        _keyStore = keyStore ?? throw new ArgumentNullException(nameof(keyStore));
    }

    public void EnsureIdentityKeyPair(string keyId, int keySizeBits = 3072)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);

        if (_keyStore.Exists(keyId))
        {
            return;
        }

        GenerateAndSaveIdentityKeyPair(keyId, keySizeBits);
    }

    public void GenerateAndSaveIdentityKeyPair(string keyId, int keySizeBits = 3072)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);

        using var rsa = RSA.Create(keySizeBits);
        var privatePkcs8 = rsa.ExportPkcs8PrivateKey();
        var publicPem = ExportPublicKeyPem(rsa);

        _keyStore.SavePrivateKey(keyId, privatePkcs8);
        _keyStore.SavePublicKey(keyId, publicPem);
    }

    public RSA LoadPrivateKey(string keyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);

        var pkcs8 = _keyStore.LoadPrivateKey(keyId);
        var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(pkcs8, out _);
        return rsa;
    }

    public string LoadPublicKeyPem(string keyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        return _keyStore.LoadPublicKey(keyId);
    }

    public void RenameIdentity(string currentKeyId, string newKeyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentKeyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newKeyId);

        _keyStore.Rename(currentKeyId, newKeyId);
    }

    public RSA LoadPublicKeyFromPem(string pem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pem);

        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return rsa;
    }

    private static string ExportPublicKeyPem(RSA rsa)
    {
        var publicKeyBytes = rsa.ExportSubjectPublicKeyInfo();
        var base64 = Convert.ToBase64String(publicKeyBytes, Base64FormattingOptions.InsertLineBreaks);
        return $"-----BEGIN PUBLIC KEY-----\n{base64}\n-----END PUBLIC KEY-----\n";
    }
}
