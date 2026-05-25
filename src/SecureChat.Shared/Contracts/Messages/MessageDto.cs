namespace SecureChat.Shared.Contracts.Messages;

public sealed record MessageDto
{
    public string MessageId { get; init; } = "";
    public string SenderUserId { get; init; } = "";
    public string RecipientUserId { get; init; } = "";
    public string EnvelopeJson { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
}
