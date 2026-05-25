namespace SecureChat.Core.Models;

public sealed record ChatMessageMetadata
{
    public string MessageId { get; init; } = "";
    public string ConversationId { get; init; } = "";
    public string SenderUserId { get; init; } = "";
    public string SenderDeviceId { get; init; } = "";
    public string RecipientUserId { get; init; } = "";
    public long TimestampUnixMs { get; init; }
    public string MessageType { get; init; } = "text";
}
