namespace SecureChat.Client.Modules.OnlineUsers;

public sealed record OnlineUserEntry
{
    public string UserId { get; init; } = "";
    public DateTimeOffset LastSeenAt { get; init; }
    public bool IsOnline { get; init; }
}
