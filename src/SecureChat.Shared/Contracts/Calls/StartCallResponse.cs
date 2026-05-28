namespace SecureChat.Shared.Contracts.Calls;

public sealed record StartCallResponse
{
    public string CallId { get; init; } = "";
    public string SignalTransport { get; init; } = "";
    public string MediaTransport { get; init; } = "";
    public bool RequiresServerSupport { get; init; }
    public IReadOnlyList<IceServerDto> IceServers { get; init; } = [];
}