namespace SecureChat.Shared.Contracts.Calls;

public sealed record PendingCallDto
{
    public string CallId { get; init; } = "";
    public string CallerUserId { get; init; } = "";
    public string RecipientUserId { get; init; } = "";
    public string CallerDeviceId { get; init; } = "";
    public bool AudioEnabled { get; init; } = true;
    public bool VideoEnabled { get; init; } = true;
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? RecipientJoinedAtUtc { get; init; }
}