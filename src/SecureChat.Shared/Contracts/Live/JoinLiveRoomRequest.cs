namespace SecureChat.Shared.Contracts.Live;

public sealed record JoinLiveRoomRequest
{
    public string UserId { get; init; } = "";
    public string DeviceId { get; init; } = "";
}