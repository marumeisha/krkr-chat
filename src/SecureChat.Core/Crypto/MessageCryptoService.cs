using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecureChat.Core.Models;

namespace SecureChat.Core.Crypto;

public sealed class MessageCryptoService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public ChatMessageEnvelope EncryptText(
        string plaintext,
        ChatMessageMetadata metadata,
        RSA recipientPublicKey,
        RSA senderPrivateKey,
        string senderPublicKeyPem)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(recipientPublicKey);
        ArgumentNullException.ThrowIfNull(senderPrivateKey);
        ArgumentNullException.ThrowIfNull(senderPublicKeyPem);

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var signaturePayload = BuildSignaturePayload(metadata, plaintextBytes);

        var signature = senderPrivateKey.SignData(
            signaturePayload,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pss);

        var aesKey = RandomNumberGenerator.GetBytes(32);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[16];

        using (var aes = new AesGcm(aesKey, 16))
        {
            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);
        }

        var encryptedAesKey = recipientPublicKey.Encrypt(aesKey, RSAEncryptionPadding.OaepSHA256);

        return new ChatMessageEnvelope
        {
            Version = "1",
            Metadata = metadata,
            ContentEncryption = "AES-256-GCM",
            KeyEncryption = "RSA-OAEP-SHA256",
            SignatureAlgorithm = "RSA-PSS-SHA256",
            EncryptedAesKeyB64 = Convert.ToBase64String(encryptedAesKey),
            NonceB64 = Convert.ToBase64String(nonce),
            TagB64 = Convert.ToBase64String(tag),
            CiphertextB64 = Convert.ToBase64String(ciphertext),
            SignatureB64 = Convert.ToBase64String(signature),
            SenderPublicKeyPem = senderPublicKeyPem
        };
    }

    public DecryptChatResult DecryptText(ChatMessageEnvelope envelope, RSA recipientPrivateKey)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(recipientPrivateKey);

        try
        {
            var encryptedAesKey = Convert.FromBase64String(envelope.EncryptedAesKeyB64);
            var nonce = Convert.FromBase64String(envelope.NonceB64);
            var tag = Convert.FromBase64String(envelope.TagB64);
            var ciphertext = Convert.FromBase64String(envelope.CiphertextB64);
            var signature = Convert.FromBase64String(envelope.SignatureB64);

            var aesKey = recipientPrivateKey.Decrypt(encryptedAesKey, RSAEncryptionPadding.OaepSHA256);

            var plaintextBytes = new byte[ciphertext.Length];
            using (var aes = new AesGcm(aesKey, 16))
            {
                aes.Decrypt(nonce, ciphertext, tag, plaintextBytes);
            }

            var plaintext = Encoding.UTF8.GetString(plaintextBytes);

            var signatureValid = false;
            if (!string.IsNullOrWhiteSpace(envelope.SenderPublicKeyPem))
            {
                using var senderRsa = RSA.Create();
                senderRsa.ImportFromPem(envelope.SenderPublicKeyPem);

                var signaturePayload = BuildSignaturePayload(envelope.Metadata, plaintextBytes);
                signatureValid = senderRsa.VerifyData(
                    signaturePayload,
                    signature,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pss);
            }

            return new DecryptChatResult(true, plaintext, signatureValid, "");
        }
        catch (CryptographicException ex)
        {
            return new DecryptChatResult(false, "", false, $"加解密失败: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new DecryptChatResult(false, "", false, $"处理失败: {ex.Message}");
        }
    }

    public string SerializeEnvelope(ChatMessageEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return JsonSerializer.Serialize(envelope, JsonOptions);
    }

    public ChatMessageEnvelope DeserializeEnvelope(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var envelope = JsonSerializer.Deserialize<ChatMessageEnvelope>(json, JsonOptions);
        if (envelope is null)
        {
            throw new InvalidOperationException("无法解析聊天消息信封(JSON)。");
        }

        return envelope;
    }

    private static byte[] BuildSignaturePayload(ChatMessageMetadata metadata, byte[] plaintextBytes)
    {
        using var ms = new MemoryStream();

        WriteString(ms, metadata.MessageId);
        WriteString(ms, metadata.ConversationId);
        WriteString(ms, metadata.SenderUserId);
        WriteString(ms, metadata.SenderDeviceId);
        WriteString(ms, metadata.RecipientUserId);
        WriteString(ms, metadata.MessageType);

        var timestampBytes = BitConverter.GetBytes(metadata.TimestampUnixMs);
        ms.Write(timestampBytes, 0, timestampBytes.Length);
        ms.Write(plaintextBytes, 0, plaintextBytes.Length);

        return ms.ToArray();
    }

    private static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? "");
        stream.Write(bytes, 0, bytes.Length);
        stream.WriteByte(0);
    }
}
