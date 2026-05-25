using SecureChat.Shared.Constants;
using SecureChat.Shared.Contracts.Messages;

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
        var route = ApiRoutes.RegisterPublicKey;
        var payload = new { UserId = userId, PublicKeyPem = publicKeyPem };
        var response = await _httpClient.PostAsJsonAsync(route, payload, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
