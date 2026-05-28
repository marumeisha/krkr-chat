using Microsoft.AspNetCore.Mvc;
using SecureChat.Server.Services.Calls;
using SecureChat.Shared.Constants;
using SecureChat.Shared.Contracts.Calls;

namespace SecureChat.Server.Controllers;

[ApiController]
public sealed class CallsController : ControllerBase
{
    private readonly CallSignalingService _callSignalingService;

    public CallsController(CallSignalingService callSignalingService)
    {
        _callSignalingService = callSignalingService;
    }

    [HttpPost(ApiRoutes.StartCall)]
    public ActionResult<StartCallResponse> Start([FromBody] StartCallRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CallerUserId) || string.IsNullOrWhiteSpace(request.RecipientUserId))
        {
            return BadRequest("CallerUserId and RecipientUserId are required.");
        }

        return Ok(_callSignalingService.StartCall(request));
    }

    [HttpGet(ApiRoutes.GetPendingCalls)]
    public ActionResult<IReadOnlyList<PendingCallDto>> GetPendingCalls(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return BadRequest("userId is required.");
        }

        return Ok(_callSignalingService.GetPendingCalls(userId));
    }

    [HttpPost(ApiRoutes.CallSignal)]
    public async Task<IActionResult> SendSignal([FromBody] CallSignalRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CallId) || string.IsNullOrWhiteSpace(request.SenderUserId) || string.IsNullOrWhiteSpace(request.SignalType))
        {
            return BadRequest("CallId, SenderUserId, and SignalType are required.");
        }

        await _callSignalingService.AppendSignalAsync(request, cancellationToken);
        return Ok();
    }

    [HttpGet(ApiRoutes.GetCallSignals)]
    public ActionResult<IReadOnlyList<CallSignalMessageDto>> GetSignals(string callId)
    {
        if (string.IsNullOrWhiteSpace(callId))
        {
            return BadRequest("callId is required.");
        }

        return Ok(_callSignalingService.GetSignals(callId));
    }

    [HttpGet(ApiRoutes.GetCallDiagnostics)]
    public ActionResult<CallSessionDiagnosticsDto> GetDiagnostics(string callId)
    {
        if (string.IsNullOrWhiteSpace(callId))
        {
            return BadRequest("callId is required.");
        }

        var diagnostics = _callSignalingService.GetDiagnostics(callId);
        return diagnostics is null ? NotFound() : Ok(diagnostics);
    }
}
