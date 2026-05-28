using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SecureChat.Server.Services;
using SecureChat.Server.Services.Online;
using SecureChat.Shared.Contracts.Online;

namespace SecureChat.Server.Controllers.Online;

[ApiController]
[Authorize]
public sealed class OnlineController : ControllerBase
{
    private static readonly TimeSpan PresenceTtl = TimeSpan.FromMinutes(2);
    private readonly OnlinePresenceService _onlinePresenceService;
    private readonly UserAccountService _userAccountService;

    public OnlineController(OnlinePresenceService onlinePresenceService, UserAccountService userAccountService)
    {
        _onlinePresenceService = onlinePresenceService;
        _userAccountService = userAccountService;
    }

    [HttpPost("/api/online/heartbeat")]
    public IActionResult Heartbeat([FromBody] OnlineHeartbeatRequest request)
    {
        var externalId = User.FindFirstValue("external_id")
                         ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                         ?? User.FindFirstValue("sub")
                         ?? string.Empty;
        if (string.IsNullOrWhiteSpace(externalId))
        {
            return Unauthorized();
        }

        var currentUser = _userAccountService.GetByExternalId(externalId);
        if (currentUser is null)
        {
            return Unauthorized();
        }

        var requestedUserId = string.IsNullOrWhiteSpace(request.UserId) ? currentUser.UserId : request.UserId.Trim();
        if (!string.Equals(currentUser.UserId, requestedUserId, StringComparison.OrdinalIgnoreCase))
        {
            return Forbid();
        }

        var deviceId = string.IsNullOrWhiteSpace(request.DeviceId) ? "unknown-device" : request.DeviceId.Trim();
        _onlinePresenceService.Heartbeat(currentUser.UserId, deviceId, DateTimeOffset.UtcNow);
        return Ok();
    }

    [HttpGet("/api/online/stats")]
    public ActionResult<OnlineStatsResponse> Stats()
    {
        var snapshot = _onlinePresenceService.GetSnapshot(PresenceTtl, DateTimeOffset.UtcNow);
        return Ok(snapshot);
    }
}
