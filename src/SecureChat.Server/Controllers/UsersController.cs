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
        _publicKeyDirectoryService.Set(request.UserId, request.PublicKeyPem);
        return Ok();
    }

    [HttpGet("/api/users/{userId}/public-key")]
    public ActionResult<PublicKeyResponse> GetPublicKey(string userId)
    {
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
