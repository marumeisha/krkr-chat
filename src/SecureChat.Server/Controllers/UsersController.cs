using Microsoft.AspNetCore.Mvc;
using SecureChat.Server.Services;
using SecureChat.Shared.Contracts.Users;

namespace SecureChat.Server.Controllers;

[ApiController]
public sealed class UsersController : ControllerBase
{
    private readonly PublicKeyDirectoryService _publicKeyDirectoryService;

    public UsersController(PublicKeyDirectoryService publicKeyDirectoryService)
    {
        _publicKeyDirectoryService = publicKeyDirectoryService;
    }

    [HttpPost("/api/users/register-key")]
    public IActionResult RegisterKey([FromBody] RegisterPublicKeyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.PublicKeyPem))
        {
            return BadRequest("UserId and PublicKeyPem are required.");
        }

        _publicKeyDirectoryService.Set(request.UserId, request.PublicKeyPem);
        return Ok();
    }

    [HttpGet("/api/users/{userId}/public-key")]
    public ActionResult<PublicKeyResponse> GetPublicKey(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return BadRequest("userId is required.");
        }

        var publicKeyPem = _publicKeyDirectoryService.Get(userId);
        if (publicKeyPem is null)
        {
            return NotFound();
        }

        return Ok(new PublicKeyResponse
        {
            UserId = userId,
            PublicKeyPem = publicKeyPem
        });
    }
}
