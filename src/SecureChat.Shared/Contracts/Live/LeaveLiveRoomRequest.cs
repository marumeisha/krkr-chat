namespace SecureChat.Shared.Contracts.Live;

public sealed record LeaveLiveRoomRequest
{
    public string UserId { get; init; } = "";
    public string DeviceId { get; init; } = "";
}