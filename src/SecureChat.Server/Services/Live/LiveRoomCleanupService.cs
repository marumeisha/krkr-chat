using Microsoft.Extensions.Options;

namespace SecureChat.Server.Services.Live;

public sealed class LiveRoomCleanupService : BackgroundService
{
    private readonly LiveRoomService _liveRoomService;
    private readonly LiveRoomSignalingService _liveRoomSignalingService;
    private readonly IOptionsMonitor<LiveRoomCleanupOptions> _optionsMonitor;
    private readonly ILogger<LiveRoomCleanupService> _logger;

    public LiveRoomCleanupService(
        LiveRoomService liveRoomService,
        LiveRoomSignalingService liveRoomSignalingService,
        IOptionsMonitor<LiveRoomCleanupOptions> optionsMonitor,
        ILogger<LiveRoomCleanupService> logger)
    {
        _liveRoomService = liveRoomService;
        _liveRoomSignalingService = liveRoomSignalingService;
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var options = Normalize(_optionsMonitor.CurrentValue);

            try
            {
                await Task.Delay(options.SweepInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                var removedRoomIds = _liveRoomService.CleanupInactiveRooms(
                    DateTimeOffset.UtcNow,
                    options.IdleTimeout,
                    roomId => !_liveRoomSignalingService.HasActiveConnections(roomId));

                foreach (var roomId in removedRoomIds)
                {
                    _liveRoomSignalingService.RemoveRoom(roomId);
                }

                if (removedRoomIds.Count > 0)
                {
                    _logger.LogInformation(
                        "Removed {Count} inactive live room(s): {RoomIds}",
                        removedRoomIds.Count,
                        string.Join(", ", removedRoomIds));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clean inactive live rooms.");
            }
        }
    }

    private static LiveRoomCleanupOptions Normalize(LiveRoomCleanupOptions options)
    {
        var sweepInterval = options.SweepInterval <= TimeSpan.Zero ? TimeSpan.FromMinutes(5) : options.SweepInterval;
        var idleTimeout = options.IdleTimeout <= TimeSpan.Zero ? TimeSpan.FromHours(2) : options.IdleTimeout;

        if (sweepInterval > idleTimeout)
        {
            sweepInterval = idleTimeout;
        }

        return new LiveRoomCleanupOptions
        {
            IdleTimeout = idleTimeout,
            SweepInterval = sweepInterval
        };
    }
}