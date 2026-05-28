namespace SecureChat.Shared.Contracts.Live;

public sealed record CreateLiveRoomRequest
{
    public string HostUserId { get; init; } = "";
    public string HostDeviceId { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public bool IsPublic { get; init; } = true;
}