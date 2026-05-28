using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using SecureChat.Shared.Constants;
using SecureChat.Shared.Contracts.Auth;
using SecureChat.Shared.Contracts.Calls;
using SecureChat.Shared.Contracts.Live;
using SecureChat.Shared.Contracts.Messages;
using SecureChat.Shared.Contracts.Online;
using SecureChat.Shared.Contracts.Users;

namespace SecureChat.ClientCore.Services;

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

    public async Task<CurrentUserResponse> UpdateMyUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PutAsJsonAsync(ApiRoutes.UpdateMyUserId, new UpdateUserIdRequest
        {
            UserId = userId
        }, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(errorText))
            {
                throw new HttpRequestException(errorText.Trim(), null, response.StatusCode);
            }

            response.EnsureSuccessStatusCode();
        }

        return await response.Content.ReadFromJsonAsync<CurrentUserResponse>(cancellationToken: cancellationToken)
               ?? new CurrentUserResponse { UserId = userId };
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

    public async Task SendHeartbeatAsync(string userId, string deviceId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(ApiRoutes.OnlineHeartbeat, new OnlineHeartbeatRequest
        {
            UserId = userId,
            DeviceId = deviceId
        }, cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    public async Task<OnlineStatsResponse?> GetOnlineStatsAsync(CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<OnlineStatsResponse>(ApiRoutes.OnlineStats, cancellationToken);
    }

    public async Task<StartCallResponse?> StartCallAsync(StartCallRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(ApiRoutes.StartCall, request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<StartCallResponse>(cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<PendingCallDto>> GetPendingCallsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var route = ApiRoutes.GetPendingCalls.Replace("{userId}", Uri.EscapeDataString(userId));
        var calls = await _httpClient.GetFromJsonAsync<List<PendingCallDto>>(route, cancellationToken);
        return calls ?? [];
    }

    public async Task SendCallSignalAsync(CallSignalRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(ApiRoutes.CallSignal, request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<CallSignalMessageDto>> GetCallSignalsAsync(string callId, CancellationToken cancellationToken = default)
    {
        var route = ApiRoutes.GetCallSignals.Replace("{callId}", Uri.EscapeDataString(callId));
        var signals = await _httpClient.GetFromJsonAsync<List<CallSignalMessageDto>>(route, cancellationToken);
        return signals ?? [];
    }

    public async Task<LiveRoomDto> CreateLiveRoomAsync(CreateLiveRoomRequest request, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(ApiRoutes.CreateLiveRoom, request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LiveRoomDto>(cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException("服务端未返回有效的直播房间。 ");
    }

    public async Task<IReadOnlyList<LiveRoomDto>> GetPublicLiveRoomsAsync(CancellationToken cancellationToken = default)
    {
        var rooms = await _httpClient.GetFromJsonAsync<List<LiveRoomDto>>(ApiRoutes.GetPublicLiveRooms, cancellationToken);
        return rooms ?? [];
    }

    public async Task<LiveRoomDto?> GetLiveRoomAsync(string roomId, CancellationToken cancellationToken = default)
    {
        var route = ApiRoutes.GetLiveRoom.Replace("{roomId}", Uri.EscapeDataString(roomId));
        var response = await _httpClient.GetAsync(route, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LiveRoomDto>(cancellationToken: cancellationToken);
    }

    public async Task<LiveRoomDto> JoinLiveRoomAsync(string roomId, JoinLiveRoomRequest request, CancellationToken cancellationToken = default)
    {
        var route = ApiRoutes.JoinLiveRoom.Replace("{roomId}", Uri.EscapeDataString(roomId));
        var response = await _httpClient.PostAsJsonAsync(route, request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LiveRoomDto>(cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException("服务端未返回有效的直播房间。 ");
    }

    public async Task LeaveLiveRoomAsync(string roomId, LeaveLiveRoomRequest request, CancellationToken cancellationToken = default)
    {
        var route = ApiRoutes.LeaveLiveRoom.Replace("{roomId}", Uri.EscapeDataString(roomId));
        var response = await _httpClient.PostAsJsonAsync(route, request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task SendLiveRoomSignalAsync(string roomId, CallSignalRequest request, CancellationToken cancellationToken = default)
    {
        var route = ApiRoutes.LiveRoomSignal.Replace("{roomId}", Uri.EscapeDataString(roomId));
        var response = await _httpClient.PostAsJsonAsync(route, request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<CallSignalMessageDto>> GetLiveRoomSignalsAsync(string roomId, CancellationToken cancellationToken = default)
    {
        var route = ApiRoutes.GetLiveRoomSignals.Replace("{roomId}", Uri.EscapeDataString(roomId));
        var signals = await _httpClient.GetFromJsonAsync<List<CallSignalMessageDto>>(route, cancellationToken);
        return signals ?? [];
    }

    public Uri BuildLiveRoomSignalWebSocketUri(string roomId, string userId)
    {
        var route = ApiRoutes.LiveRoomSignalWebSocket.Replace("{roomId}", Uri.EscapeDataString(roomId));
        var wsBase = new Uri(_httpClient.BaseAddress!, route);
        var builder = new UriBuilder(wsBase)
        {
            Scheme = string.Equals(wsBase.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? Uri.UriSchemeWss : Uri.UriSchemeWs,
            Port = wsBase.IsDefaultPort ? -1 : wsBase.Port,
            Query = $"userId={Uri.EscapeDataString(userId)}"
        };

        return builder.Uri;
    }

    public async Task<ClientWebSocket> ConnectLiveRoomSignalWebSocketAsync(string roomId, string userId, CancellationToken cancellationToken = default)
    {
        var socket = new ClientWebSocket();
        socket.Options.Proxy = null;
        if (_httpClient.DefaultRequestHeaders.Authorization is { } authorization)
        {
            socket.Options.SetRequestHeader("Authorization", authorization.ToString());
        }

        await socket.ConnectAsync(BuildLiveRoomSignalWebSocketUri(roomId, userId), cancellationToken);
        return socket;
    }

    public Uri BuildCallSignalWebSocketUri(string callId, string userId)
    {
        var route = ApiRoutes.CallSignalWebSocket.Replace("{callId}", Uri.EscapeDataString(callId));
        var wsBase = new Uri(_httpClient.BaseAddress!, route);
        var builder = new UriBuilder(wsBase)
        {
            Scheme = string.Equals(wsBase.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? Uri.UriSchemeWss : Uri.UriSchemeWs,
            Port = wsBase.IsDefaultPort ? -1 : wsBase.Port,
            Query = $"userId={Uri.EscapeDataString(userId)}"
        };

        return builder.Uri;
    }

    public async Task<ClientWebSocket> ConnectCallSignalWebSocketAsync(string callId, string userId, CancellationToken cancellationToken = default)
    {
        var socket = new ClientWebSocket();
        socket.Options.Proxy = null;
        if (_httpClient.DefaultRequestHeaders.Authorization is { } authorization)
        {
            socket.Options.SetRequestHeader("Authorization", authorization.ToString());
        }

        await socket.ConnectAsync(BuildCallSignalWebSocketUri(callId, userId), cancellationToken);
        return socket;
    }
}