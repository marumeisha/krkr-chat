using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SecureChat.Server.Services.Calls;
using SecureChat.Server.Services.Live;
using SecureChat.Server.Services.Online;
using SecureChat.Server.Services.Runtime;

namespace SecureChat.Server.Controllers;

[ApiController]
public sealed class AdminController : ControllerBase
{
    private static readonly TimeSpan PresenceTtl = TimeSpan.FromMinutes(2);
    private readonly CallSignalingService _callSignalingService;
    private readonly LiveRoomService _liveRoomService;
    private readonly LiveRoomSignalingService _liveRoomSignalingService;
    private readonly IOptionsMonitor<CallCleanupOptions> _callCleanupOptions;
    private readonly IOptionsMonitor<LiveRoomCleanupOptions> _liveRoomCleanupOptions;
    private readonly OnlinePresenceService _onlinePresenceService;
    private readonly CloudflareTunnelManager _cloudflareTunnelManager;

    public AdminController(
        OnlinePresenceService onlinePresenceService,
        CloudflareTunnelManager cloudflareTunnelManager,
        CallSignalingService callSignalingService,
        LiveRoomService liveRoomService,
        LiveRoomSignalingService liveRoomSignalingService,
        IOptionsMonitor<CallCleanupOptions> callCleanupOptions,
        IOptionsMonitor<LiveRoomCleanupOptions> liveRoomCleanupOptions)
    {
        _onlinePresenceService = onlinePresenceService;
        _cloudflareTunnelManager = cloudflareTunnelManager;
        _callSignalingService = callSignalingService;
        _liveRoomService = liveRoomService;
        _liveRoomSignalingService = liveRoomSignalingService;
        _callCleanupOptions = callCleanupOptions;
        _liveRoomCleanupOptions = liveRoomCleanupOptions;
    }

    [HttpGet("/api/admin/runtime")]
    public IActionResult Runtime()
    {
        if (!IsLoopbackRequest())
        {
            return StatusCode(StatusCodes.Status403Forbidden, "Admin UI is only available from localhost.");
        }

        var onlineStats = _onlinePresenceService.GetSnapshot(PresenceTtl, DateTimeOffset.UtcNow);
        var oauthClientId = Environment.GetEnvironmentVariable("Authentication__Microsoft__ClientId") ?? string.Empty;
        var jwtSigningKey = Environment.GetEnvironmentVariable("Authentication__Jwt__SigningKey") ?? string.Empty;
        var callSessions = _callSignalingService.GetCallIds()
            .Select(callId => _callSignalingService.GetDiagnostics(callId))
            .Where(diagnostics => diagnostics is not null)
            .Select(diagnostics => new
            {
                diagnostics!.CallId,
                diagnostics.LastActivityAtUtc,
                diagnostics.ActiveConnectionCount,
                diagnostics.HasPendingInvitation,
                diagnostics.RecipientJoined
            })
            .ToList();
        var liveRooms = _liveRoomService.GetRoomIds()
            .Select(roomId => _liveRoomService.GetDiagnostics(
                roomId,
                _liveRoomSignalingService.GetSignalHistoryCount(roomId),
                _liveRoomSignalingService.GetActiveConnectionCount(roomId)))
            .Where(diagnostics => diagnostics is not null)
            .Select(diagnostics => new
            {
                diagnostics!.RoomId,
                diagnostics.DisplayName,
                diagnostics.LastActivityAtUtc,
                diagnostics.ActiveConnectionCount,
                diagnostics.ViewerCount
            })
            .ToList();

        return Ok(new
        {
            name = "SecureChat Server Console",
            status = "ok",
            machine = Environment.MachineName,
            nowUtc = DateTimeOffset.UtcNow,
            urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://0.0.0.0:5000",
            oauthConfigured = !string.IsNullOrWhiteSpace(oauthClientId),
            jwtConfigured = !string.IsNullOrWhiteSpace(jwtSigningKey),
            online = onlineStats,
            cloudflare = _cloudflareTunnelManager.GetStatus(),
            callSessions,
            liveRooms
        });
    }

    [HttpPost("/api/admin/cloudflare/start")]
    public IActionResult StartCloudflare()
    {
        if (!IsLoopbackRequest())
        {
            return StatusCode(StatusCodes.Status403Forbidden, "Cloudflare management is only available from localhost.");
        }

        return Ok(_cloudflareTunnelManager.Start());
    }

    [HttpPost("/api/admin/cleanup/calls/{callId}")]
    public async Task<IActionResult> CleanupCall(string callId, CancellationToken cancellationToken)
    {
        if (!IsLoopbackRequest())
        {
            return StatusCode(StatusCodes.Status403Forbidden, "Call cleanup is only available from localhost.");
        }

        if (string.IsNullOrWhiteSpace(callId))
        {
            return BadRequest("callId is required.");
        }

        var diagnostics = _callSignalingService.GetDiagnostics(callId);
        if (diagnostics is null)
        {
            return NotFound();
        }

        var closedConnectionCount = await _callSignalingService.RemoveCallAsync(callId, cancellationToken);
        return Ok(new
        {
            removed = true,
            type = "call",
            id = diagnostics.CallId,
            closedConnectionCount,
            diagnostics
        });
    }

    [HttpPost("/api/admin/cleanup/live-rooms/{roomId}")]
    public async Task<IActionResult> CleanupLiveRoom(string roomId, CancellationToken cancellationToken)
    {
        if (!IsLoopbackRequest())
        {
            return StatusCode(StatusCodes.Status403Forbidden, "Live room cleanup is only available from localhost.");
        }

        if (string.IsNullOrWhiteSpace(roomId))
        {
            return BadRequest("roomId is required.");
        }

        var diagnostics = _liveRoomService.GetDiagnostics(
            roomId,
            _liveRoomSignalingService.GetSignalHistoryCount(roomId),
            _liveRoomSignalingService.GetActiveConnectionCount(roomId));
        if (diagnostics is null)
        {
            return NotFound();
        }

        var closedConnectionCount = await _liveRoomSignalingService.RemoveRoomAsync(roomId, cancellationToken);
        _liveRoomService.RemoveRoom(roomId);

        return Ok(new
        {
            removed = true,
            type = "live-room",
            id = diagnostics.RoomId,
            closedConnectionCount,
            diagnostics
        });
    }

    [HttpGet("/api/admin/live-rooms/{roomId}")]
    public IActionResult GetLiveRoomDetails(string roomId)
    {
        if (!IsLoopbackRequest())
        {
            return StatusCode(StatusCodes.Status403Forbidden, "Live room lookup is only available from localhost.");
        }

        if (string.IsNullOrWhiteSpace(roomId))
        {
            return BadRequest("roomId is required.");
        }

        var diagnostics = _liveRoomService.GetDiagnostics(
            roomId,
            _liveRoomSignalingService.GetSignalHistoryCount(roomId),
            _liveRoomSignalingService.GetActiveConnectionCount(roomId));

        return Ok(new
        {
            roomId = roomId.Trim().ToUpperInvariant(),
            exists = diagnostics is not null,
            diagnostics,
            message = diagnostics is null
                ? "当前服务器上不存在这个 RoomId。若主播已开播，请确认观众连接的是同一台服务端。"
                : $"房间 {diagnostics.RoomId} 当前仍存在于本机服务端。"
        });
    }

    [HttpPost("/api/admin/cleanup/inactive")]
    public IActionResult CleanupInactive()
    {
        if (!IsLoopbackRequest())
        {
            return StatusCode(StatusCodes.Status403Forbidden, "Inactive cleanup is only available from localhost.");
        }

        var utcNow = DateTimeOffset.UtcNow;
        var callOptions = NormalizeCallCleanupOptions(_callCleanupOptions.CurrentValue);
        var liveRoomOptions = NormalizeLiveRoomCleanupOptions(_liveRoomCleanupOptions.CurrentValue);

        var removedCallIds = _callSignalingService.CleanupInactiveCalls(utcNow, callOptions.IdleTimeout);
        var removedRoomIds = _liveRoomService.CleanupInactiveRooms(
            utcNow,
            liveRoomOptions.IdleTimeout,
            roomId => !_liveRoomSignalingService.HasActiveConnections(roomId));

        foreach (var roomId in removedRoomIds)
        {
            _liveRoomSignalingService.RemoveRoom(roomId);
        }

        return Ok(new
        {
            nowUtc = utcNow,
            calls = new
            {
                idleTimeout = callOptions.IdleTimeout,
                removedCount = removedCallIds.Count,
                removedIds = removedCallIds
            },
            liveRooms = new
            {
                idleTimeout = liveRoomOptions.IdleTimeout,
                removedCount = removedRoomIds.Count,
                removedIds = removedRoomIds
            }
        });
    }

    private static CallCleanupOptions NormalizeCallCleanupOptions(CallCleanupOptions options)
    {
        return new CallCleanupOptions
        {
            IdleTimeout = options.IdleTimeout <= TimeSpan.Zero ? TimeSpan.FromHours(2) : options.IdleTimeout,
            SweepInterval = options.SweepInterval <= TimeSpan.Zero ? TimeSpan.FromMinutes(5) : options.SweepInterval
        };
    }

    private static LiveRoomCleanupOptions NormalizeLiveRoomCleanupOptions(LiveRoomCleanupOptions options)
    {
        return new LiveRoomCleanupOptions
        {
            IdleTimeout = options.IdleTimeout <= TimeSpan.Zero ? TimeSpan.FromHours(2) : options.IdleTimeout,
            SweepInterval = options.SweepInterval <= TimeSpan.Zero ? TimeSpan.FromMinutes(5) : options.SweepInterval
        };
    }

    private bool IsLoopbackRequest()
    {
        var remoteIp = HttpContext.Connection.RemoteIpAddress;
        return remoteIp is not null && IPAddress.IsLoopback(remoteIp);
    }
}
