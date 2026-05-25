using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SecureChat.Server.Auth;
using SecureChat.Server.Services;
using SecureChat.Shared.Contracts.Auth;

namespace SecureChat.Server.Controllers;

[ApiController]
public sealed class AuthController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<MicrosoftOAuthOptions> _microsoftOptions;
    private readonly JwtTokenService _jwtTokenService;
    private readonly UserAccountService _userAccountService;

    public AuthController(
        IHttpClientFactory httpClientFactory,
        IOptions<MicrosoftOAuthOptions> microsoftOptions,
        JwtTokenService jwtTokenService,
        UserAccountService userAccountService)
    {
        _httpClientFactory = httpClientFactory;
        _microsoftOptions = microsoftOptions;
        _jwtTokenService = jwtTokenService;
        _userAccountService = userAccountService;
    }

    [HttpGet("/api/auth/oauth/microsoft/start")]
    public IActionResult Start([FromQuery] string? redirectUri = null)
    {
        var options = _microsoftOptions.Value;
        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            return Problem("Microsoft OAuth is not configured.");
        }

        var callback = BuildAbsoluteCallbackUri(options.CallbackPath);
        var state = string.IsNullOrWhiteSpace(redirectUri) ? string.Empty : Uri.EscapeDataString(redirectUri);
        var authorizeUrl = $"https://login.microsoftonline.com/{options.Tenant}/oauth2/v2.0/authorize" +
                           $"?client_id={Uri.EscapeDataString(options.ClientId)}" +
                           $"&response_type=code" +
                           $"&redirect_uri={Uri.EscapeDataString(callback)}" +
                           $"&response_mode=query" +
                           $"&scope={Uri.EscapeDataString("openid profile email offline_access User.Read")}" +
                           $"&state={state}";

        return Redirect(authorizeUrl);
    }

    [HttpGet("/api/auth/oauth/microsoft/callback")]
    public async Task<IActionResult> Callback([FromQuery] string code, [FromQuery] string? state = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return BadRequest("code is required.");
        }

        var options = _microsoftOptions.Value;
        var callback = BuildAbsoluteCallbackUri(options.CallbackPath);
        var client = _httpClientFactory.CreateClient("MicrosoftOAuth");

        MicrosoftTokenResponse tokenResponse;
        try
        {
            tokenResponse = await ExchangeCodeForTokenAsync(client, options, code, callback, cancellationToken);
        }
        catch (OAuthTokenExchangeException ex)
        {
            return BadRequest($"Microsoft OAuth token exchange failed ({ex.StatusCode}): {ex.Message}");
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway,
                "Unable to reach Microsoft OAuth token endpoint. Please try again in a few seconds.");
        }

        if (string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
        {
            return Problem("Failed to retrieve Microsoft access token.");
        }

        var profile = await GetMicrosoftProfileAsync(client, tokenResponse.AccessToken, cancellationToken);
        var externalId = profile.Id ?? profile.UserPrincipalName ?? profile.Mail;
        var email = profile.Mail ?? profile.UserPrincipalName ?? string.Empty;
        var displayName = profile.DisplayName ?? email;

        if (string.IsNullOrWhiteSpace(externalId) || string.IsNullOrWhiteSpace(email))
        {
            return Problem("Microsoft profile did not include a usable identity.");
        }

        var user = _userAccountService.GetOrCreateMicrosoftUser(externalId, displayName, email);
        var login = _jwtTokenService.CreateToken(user);

        if (!string.IsNullOrWhiteSpace(state))
        {
            var decodedState = Uri.UnescapeDataString(state);
            if (Uri.TryCreate(decodedState, UriKind.Absolute, out var redirectUri))
            {
                var separator = string.IsNullOrEmpty(redirectUri.Query) ? "?" : "&";
                var finalUrl = $"{redirectUri}{separator}token={Uri.EscapeDataString(login.AccessToken)}&userId={Uri.EscapeDataString(user.UserId)}";
                return Redirect(finalUrl);
            }
        }

        return Ok(login);
    }

    [Authorize]
    [HttpGet("/api/auth/me")]
    public ActionResult<CurrentUserResponse> Me()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? "";
        var displayName = User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue("unique_name") ?? "";
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email") ?? "";
        var provider = User.FindFirstValue("provider") ?? "microsoft";

        return Ok(new CurrentUserResponse
        {
            UserId = userId,
            DisplayName = displayName,
            Email = email,
            AuthProvider = provider
        });
    }

    private string BuildAbsoluteCallbackUri(string callbackPath)
    {
        return $"{Request.Scheme}://{Request.Host}{callbackPath}";
    }

    private static async Task<MicrosoftTokenResponse> ExchangeCodeForTokenAsync(
        HttpClient client,
        MicrosoftOAuthOptions options,
        string code,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        var endpoint = $"https://login.microsoftonline.com/{options.Tenant}/oauth2/v2.0/token";
        var payload = new Dictionary<string, string>
        {
            ["client_id"] = options.ClientId,
            ["client_secret"] = options.ClientSecret,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["scope"] = "openid profile email offline_access User.Read"
        };

        // Prefer HTTP/1.1 and retry once to survive transient TLS/proxy handshake failures.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new FormUrlEncodedContent(payload),
                    Version = HttpVersion.Version11,
                    VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
                };

                using var response = await client.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    throw new OAuthTokenExchangeException((int)response.StatusCode, errorBody);
                }

                return await response.Content.ReadFromJsonAsync<MicrosoftTokenResponse>(cancellationToken: cancellationToken)
                       ?? new MicrosoftTokenResponse();
            }
            catch (HttpRequestException) when (attempt == 0)
            {
                await Task.Delay(500, cancellationToken);
            }
        }

        throw new HttpRequestException("Failed to exchange OAuth code for token after retry.");
    }

    private static async Task<MicrosoftProfileResponse> GetMicrosoftProfileAsync(HttpClient client, string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MicrosoftProfileResponse>(cancellationToken: cancellationToken)
               ?? new MicrosoftProfileResponse();
    }

    private sealed record MicrosoftTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = "";
    }

    private sealed record MicrosoftProfileResponse
    {
        public string? Id { get; init; }
        public string? DisplayName { get; init; }
        public string? Mail { get; init; }
        public string? UserPrincipalName { get; init; }
    }

    private sealed class OAuthTokenExchangeException : Exception
    {
        public int StatusCode { get; }

        public OAuthTokenExchangeException(int statusCode, string message) : base(message)
        {
            StatusCode = statusCode;
        }
    }
}
