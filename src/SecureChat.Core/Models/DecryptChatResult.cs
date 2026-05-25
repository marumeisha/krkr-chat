namespace SecureChat.Core.Models;

public sealed record DecryptChatResult(
    bool Success,
    string Plaintext,
    bool SignatureValid,
    string Error
);
