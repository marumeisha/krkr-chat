namespace SecureChat.Server.Services.Calls;

public sealed class CallCleanupOptions
{
    public const string SectionName = "CallCleanup";

    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromHours(2);

    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(5);
}