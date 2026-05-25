using System.Net.Http.Json;
using SecureChat.Shared.Constants;
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
