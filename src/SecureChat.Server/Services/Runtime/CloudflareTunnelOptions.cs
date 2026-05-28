namespace SecureChat.Server.Services.Runtime;

public sealed class CloudflareTunnelOptions
{
    public const string SectionName = "CloudflareTunnel";

    public string TunnelName { get; init; } = "securechat";
    public string ExecutablePath { get; init; } = @"D:\tools\cloudflared\cloudflared.exe";
    public string ConfigPath { get; init; } = "";
    public string ExpectedHostname { get; init; } = "krkr.chat";
    public string ExpectedServiceUrl { get; init; } = "http://localhost:5000";
}
