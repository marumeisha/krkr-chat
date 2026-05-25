using Microsoft.AspNetCore.Mvc;
using SecureChat.Server.Services;
using SecureChat.Shared.Contracts.Messages;

namespace SecureChat.Server.Controllers;

[ApiController]
public sealed class MessagesController : ControllerBase
{
    private readonly MessageService _messageService;

    public MessagesController(MessageService messageService)
    {
        _messageService = messageService;
    }

    [HttpPost("/api/messages/send")]
    public IActionResult Send([FromBody] SendMessageRequest request)
    {
        _messageService.Add(request);
        return Ok();
    }

    [HttpGet("/api/messages/inbox/{userId}")]
    public ActionResult<IReadOnlyList<MessageDto>> Inbox(string userId)
    {
        return Ok(_messageService.GetInbox(userId));
    }
}
