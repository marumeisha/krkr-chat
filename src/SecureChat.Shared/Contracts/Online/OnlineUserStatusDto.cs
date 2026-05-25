namespace SecureChat.Shared.Contracts.Online;

public sealed record OnlineUserStatusDto
{
    public string UserId { get; init; } = "";
    public int DeviceCount { get; init; }
    public DateTimeOffset LastSeenUtc { get; init; }
}
