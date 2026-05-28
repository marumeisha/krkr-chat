namespace SecureChat.Server.Services.Live;

public sealed class LiveRoomCleanupOptions
{
    public const string SectionName = "LiveRoomCleanup";

    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromHours(2);

    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(5);
}