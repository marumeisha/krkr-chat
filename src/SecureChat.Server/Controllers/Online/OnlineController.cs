using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureChat.Server.Services.Online;
using SecureChat.Shared.Contracts.Online;

namespace SecureChat.Server.Controllers.Online;

[ApiController]
[Authorize]
public sealed class OnlineController : ControllerBase
{
    private static readonly TimeSpan PresenceTtl = TimeSpan.FromMinutes(2);
    private readonly OnlinePresenceService _onlinePresenceService;

    public OnlineController(OnlinePresenceService onlinePresenceService)
    {
        _onlinePresenceService = onlinePresenceService;
    }

    [HttpPost("/api/online/heartbeat")]
    public IActionResult Heartbeat([FromBody] OnlineHeartbeatRequest request)
    {
        var tokenUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(tokenUserId))
        {
            return Unauthorized();
        }

        var requestedUserId = string.IsNullOrWhiteSpace(request.UserId) ? tokenUserId : request.UserId.Trim();
        if (!string.Equals(tokenUserId, requestedUserId, StringComparison.OrdinalIgnoreCase))
        {
            return Forbid();
        }

        var deviceId = string.IsNullOrWhiteSpace(request.DeviceId) ? "unknown-device" : request.DeviceId.Trim();
        _onlinePresenceService.Heartbeat(tokenUserId, deviceId, DateTimeOffset.UtcNow);
        return Ok();
    }

    [HttpGet("/api/online/stats")]
    public ActionResult<OnlineStatsResponse> Stats()
    {
        var snapshot = _onlinePresenceService.GetSnapshot(PresenceTtl, DateTimeOffset.UtcNow);
        return Ok(snapshot);
    }
}
