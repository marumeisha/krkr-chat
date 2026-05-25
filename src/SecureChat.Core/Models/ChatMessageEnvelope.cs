namespace SecureChat.Core.Models;

public sealed record ChatMessageEnvelope
{
    public string Version { get; init; } = "1";
    public ChatMessageMetadata Metadata { get; init; } = new();
    public string ContentEncryption { get; init; } = "AES-256-GCM";
    public string KeyEncryption { get; init; } = "RSA-OAEP-SHA256";
    public string SignatureAlgorithm { get; init; } = "RSA-PSS-SHA256";
    public string EncryptedAesKeyB64 { get; init; } = "";
    public string NonceB64 { get; init; } = "";
    public string TagB64 { get; init; } = "";
    public string CiphertextB64 { get; init; } = "";
    public string SignatureB64 { get; init; } = "";
    public string SenderPublicKeyPem { get; init; } = "";
}
