namespace SecureChat.Core.Keys;

public interface IKeyStore
{
    void SavePrivateKey(string keyId, byte[] privateKeyPkcs8);
    byte[] LoadPrivateKey(string keyId);
    bool Exists(string keyId);
    void SavePublicKey(string keyId, string publicKeyPem);
    string LoadPublicKey(string keyId);
    void Rename(string currentKeyId, string newKeyId);
}
