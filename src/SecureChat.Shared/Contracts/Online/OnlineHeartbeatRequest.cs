namespace SecureChat.Shared.Contracts.Online;

public sealed record OnlineHeartbeatRequest
{
    public string UserId { get; init; } = "";
    public string DeviceId { get; init; } = "";
}
