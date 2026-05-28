namespace SecureChat.Shared.Contracts.Calls;

public sealed record CallCapabilitiesDto
{
    public bool AudioEnabled { get; init; }
    public bool VideoEnabled { get; init; }
    public bool ScreenShareEnabled { get; init; }
    public string SignalTransport { get; init; } = "";
    public string MediaTransport { get; init; } = "";
    public IReadOnlyList<string> PlannedStages { get; init; } = [];
}