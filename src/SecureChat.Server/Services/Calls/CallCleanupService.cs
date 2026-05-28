using Microsoft.Extensions.Options;

namespace SecureChat.Server.Services.Calls;

public sealed class CallCleanupService : BackgroundService
{
    private readonly CallSignalingService _callSignalingService;
    private readonly IOptionsMonitor<CallCleanupOptions> _optionsMonitor;
    private readonly ILogger<CallCleanupService> _logger;

    public CallCleanupService(
        CallSignalingService callSignalingService,
        IOptionsMonitor<CallCleanupOptions> optionsMonitor,
        ILogger<CallCleanupService> logger)
    {
        _callSignalingService = callSignalingService;
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
                var removedCallIds = _callSignalingService.CleanupInactiveCalls(DateTimeOffset.UtcNow, options.IdleTimeout);
                if (removedCallIds.Count > 0)
                {
                    _logger.LogInformation(
                        "Removed {Count} inactive call session(s): {CallIds}",
                        removedCallIds.Count,
                        string.Join(", ", removedCallIds));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clean inactive call sessions.");
            }
        }
    }

    private static CallCleanupOptions Normalize(CallCleanupOptions options)
    {
        var sweepInterval = options.SweepInterval <= TimeSpan.Zero ? TimeSpan.FromMinutes(5) : options.SweepInterval;
        var idleTimeout = options.IdleTimeout <= TimeSpan.Zero ? TimeSpan.FromHours(2) : options.IdleTimeout;

        if (sweepInterval > idleTimeout)
        {
            sweepInterval = idleTimeout;
        }

        return new CallCleanupOptions
        {
            IdleTimeout = idleTimeout,
            SweepInterval = sweepInterval
        };
    }
}