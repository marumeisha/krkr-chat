using Microsoft.AspNetCore.Mvc;
using SecureChat.Server.Services.Live;
using SecureChat.Shared.Constants;
using SecureChat.Shared.Contracts.Calls;
using SecureChat.Shared.Contracts.Live;

namespace SecureChat.Server.Controllers;

[ApiController]
public sealed class LiveRoomsController : ControllerBase
{
    private readonly LiveRoomService _liveRoomService;
    private readonly LiveRoomSignalingService _liveRoomSignalingService;

    public LiveRoomsController(LiveRoomService liveRoomService, LiveRoomSignalingService liveRoomSignalingService)
    {
        _liveRoomService = liveRoomService;
        _liveRoomSignalingService = liveRoomSignalingService;
    }

    [HttpPost(ApiRoutes.CreateLiveRoom)]
    public ActionResult<LiveRoomDto> Create([FromBody] CreateLiveRoomRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.HostUserId))
        {
            return BadRequest("HostUserId is required.");
        }

        var room = _liveRoomService.CreateRoom(request);
        _liveRoomSignalingService.ResetRoom(room.RoomId);
        return Ok(room);
    }

    [HttpGet(ApiRoutes.GetPublicLiveRooms)]
    public ActionResult<IReadOnlyList<LiveRoomDto>> GetPublicRooms()
    {
        return Ok(_liveRoomService.GetPublicRooms());
    }

    [HttpGet(ApiRoutes.GetLiveRoom)]
    public ActionResult<LiveRoomDto> GetRoom(string roomId)
    {
        if (string.IsNullOrWhiteSpace(roomId))
        {
            return BadRequest("roomId is required.");
        }

        var room = _liveRoomService.GetRoom(roomId);
        return room is null ? NotFound() : Ok(room);
    }

    [HttpPost(ApiRoutes.JoinLiveRoom)]
    public ActionResult<LiveRoomDto> Join(string roomId, [FromBody] JoinLiveRoomRequest request)
    {
        if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(request.UserId))
        {
            return BadRequest("roomId and UserId are required.");
        }

        try
        {
            var room = _liveRoomService.JoinRoom(roomId, request);
            _liveRoomSignalingService.ResetRoom(room.RoomId);
            return Ok(room);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

    [HttpPost(ApiRoutes.LeaveLiveRoom)]
    public IActionResult Leave(string roomId, [FromBody] LeaveLiveRoomRequest request)
    {
        if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(request.UserId))
        {
            return BadRequest("roomId and UserId are required.");
        }

        _liveRoomService.LeaveRoom(roomId, request);
        _liveRoomSignalingService.ResetRoom(roomId);
        return Ok();
    }

    [HttpPost(ApiRoutes.LiveRoomSignal)]
    public async Task<IActionResult> SendSignal(string roomId, [FromBody] CallSignalRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(request.SenderUserId) || string.IsNullOrWhiteSpace(request.SignalType))
        {
            return BadRequest("roomId, SenderUserId, and SignalType are required.");
        }

        await _liveRoomSignalingService.AppendSignalAsync(roomId, request, cancellationToken);
        return Ok();
    }

    [HttpGet(ApiRoutes.GetLiveRoomSignals)]
    public ActionResult<IReadOnlyList<CallSignalMessageDto>> GetSignals(string roomId)
    {
        if (string.IsNullOrWhiteSpace(roomId))
        {
            return BadRequest("roomId is required.");
        }

        return Ok(_liveRoomSignalingService.GetSignals(roomId));
    }

    [HttpGet(ApiRoutes.GetLiveRoomDiagnostics)]
    public ActionResult<LiveRoomDiagnosticsDto> GetDiagnostics(string roomId)
    {
        if (string.IsNullOrWhiteSpace(roomId))
        {
            return BadRequest("roomId is required.");
        }

        var diagnostics = _liveRoomService.GetDiagnostics(
            roomId,
            _liveRoomSignalingService.GetSignalHistoryCount(roomId),
            _liveRoomSignalingService.GetActiveConnectionCount(roomId));

        return diagnostics is null ? NotFound() : Ok(diagnostics);
    }
}