namespace SecureChat.Server.Services.Runtime;

public sealed record CloudflareTunnelStatus
{
    public bool IsInstalled { get; init; }
    public bool ConfigExists { get; init; }
    public bool ConfigMatches { get; init; }
    public bool IsRunning { get; init; }
    public bool IsManagedByServer { get; init; }
    public string TunnelName { get; init; } = "";
    public string ExecutablePath { get; init; } = "";
    public string ConfigPath { get; init; } = "";
    public string Hostname { get; init; } = "";
    public string ServiceUrl { get; init; } = "";
    public string Message { get; init; } = "";
    public IReadOnlyList<string> RecentLogs { get; init; } = Array.Empty<string>();
}
