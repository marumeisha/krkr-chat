namespace SecureChat.Shared.Contracts.Calls;

public sealed record CallSessionDiagnosticsDto
{
    public string CallId { get; init; } = "";
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset LastActivityAtUtc { get; init; }
    public bool HasPendingInvitation { get; init; }
    public bool RecipientJoined { get; init; }
    public int SignalHistoryCount { get; init; }
    public int ActiveConnectionCount { get; init; }
}