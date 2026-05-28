namespace SecureChat.Shared.Contracts.Calls;

public sealed record CallSignalRequest
{
    public string CallId { get; init; } = "";
    public string SenderUserId { get; init; } = "";
    public string SignalType { get; init; } = "";
    public string PayloadJson { get; init; } = "";
}