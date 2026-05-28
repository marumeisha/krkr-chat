using SecureChat.Shared.Contracts.Calls;

namespace SecureChat.Shared.Contracts.Live;

public sealed record LiveRoomDto
{
    public string RoomId { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string HostUserId { get; init; } = "";
    public bool IsPublic { get; init; }
    public int ViewerCount { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
    public IReadOnlyList<IceServerDto> IceServers { get; init; } = [];
}