namespace SecureChat.Shared.Contracts.Live;

public sealed record LiveRoomDiagnosticsDto
{
    public string RoomId { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string HostUserId { get; init; } = "";
    public bool IsPublic { get; init; }
    public int ViewerCount { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset LastActivityAtUtc { get; init; }
    public int SignalHistoryCount { get; init; }
    public int ActiveConnectionCount { get; init; }
}