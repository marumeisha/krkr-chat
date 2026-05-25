using System.Net.Http.Headers;
using System.Net.Http.Json;
using SecureChat.Shared.Constants;
using SecureChat.Shared.Contracts.Auth;
using SecureChat.Shared.Contracts.Messages;
using SecureChat.Shared.Contracts.Users;

namespace SecureChat.Client.Services;

public sealed class ApiClient
{
    private readonly HttpClient _httpClient;

    public ApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public void SetBearerToken(string? accessToken)
    {
        _httpClient.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(accessToken)
            ? null
            : new AuthenticationHeaderValue("Bearer", accessToken);
    }

    public string BuildMicrosoftLoginUrl(string? redirectUri = null)
    {
        var url = ApiRoutes.MicrosoftLoginStart;
        if (!string.IsNullOrWhiteSpace(redirectUri))
        {
            url += $"?redirectUri={Uri.EscapeDataString(redirectUri)}";
        }

        return new Uri(_httpClient.BaseAddress!, url).ToString();
    }

    public async Task<CurrentUserResponse?> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(ApiRoutes.Me, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CurrentUserResponse>(cancellationToken: cancellationToken);
    }

    public async Task SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(ApiRoutes.SendMessage, request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<MessageDto>> GetInboxAsync(string userId, CancellationToken cancellationToken = default)
    {
        var route = ApiRoutes.GetInbox.Replace("{userId}", Uri.EscapeDataString(userId));
        var messages = await _httpClient.GetFromJsonAsync<List<MessageDto>>(route, cancellationToken);
        return messages ?? [];
    }

    public async Task RegisterPublicKeyAsync(string userId, string publicKeyPem, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(ApiRoutes.RegisterPublicKey, new RegisterPublicKeyRequest
        {
            UserId = userId,
            PublicKeyPem = publicKeyPem
        }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<string?> GetPublicKeyAsync(string userId, CancellationToken cancellationToken = default)
    {
        var route = ApiRoutes.GetPublicKey.Replace("{userId}", Uri.EscapeDataString(userId));
        var response = await _httpClient.GetAsync(route, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<PublicKeyResponse>(cancellationToken: cancellationToken);
        return payload?.PublicKeyPem;
    }
}
