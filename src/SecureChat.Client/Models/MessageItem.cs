namespace SecureChat.Client.Models;

public sealed record MessageItem(
    string MessageId,
    string SenderUserId,
    string Plaintext,
    DateTimeOffset CreatedAt
);
