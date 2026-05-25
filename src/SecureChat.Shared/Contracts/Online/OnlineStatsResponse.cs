namespace SecureChat.Shared.Contracts.Online;

public sealed record OnlineStatsResponse
{
    public int OnlineUserCount { get; init; }
    public int ActiveDeviceCount { get; init; }
    public IReadOnlyList<OnlineUserStatusDto> Users { get; init; } = [];
}
