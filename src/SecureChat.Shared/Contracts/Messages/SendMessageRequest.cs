namespace SecureChat.Shared.Contracts.Messages;

public sealed record SendMessageRequest
{
    public string RecipientUserId { get; init; } = "";
    public string EnvelopeJson { get; init; } = "";
}
