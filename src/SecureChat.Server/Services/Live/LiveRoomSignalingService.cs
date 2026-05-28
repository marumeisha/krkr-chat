using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using SecureChat.Shared.Contracts.Calls;

namespace SecureChat.Server.Services.Live;

public sealed class LiveRoomSignalingService
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<CallSignalMessageDto>> _historyByRoomId = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, WebSocketConnection>> _connectionsByRoomId = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<LiveRoomSignalingService> _logger;

    public LiveRoomSignalingService(ILogger<LiveRoomSignalingService> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<CallSignalMessageDto> GetSignals(string roomId)
    {
        var normalizedRoomId = roomId.Trim();
        if (!_historyByRoomId.TryGetValue(normalizedRoomId, out var queue))
        {
            return [];
        }

        return queue.OrderBy(item => item.CreatedAtUtc).ToList();
    }

    public void ResetRoom(string roomId)
    {
        var normalizedRoomId = roomId.Trim();
        _historyByRoomId[normalizedRoomId] = new ConcurrentQueue<CallSignalMessageDto>();
        _logger.LogInformation("Live room {RoomId} signaling history reset.", normalizedRoomId);
    }

    public bool HasActiveConnections(string roomId)
    {
        var normalizedRoomId = roomId.Trim();
        if (!_connectionsByRoomId.TryGetValue(normalizedRoomId, out var connections))
        {
            return false;
        }

        foreach (var pair in connections)
        {
            if (pair.Value.Socket.State == WebSocketState.Open)
            {
                return true;
            }

            connections.TryRemove(pair.Key, out _);
        }

        return false;
    }

    public void RemoveRoom(string roomId)
    {
        var normalizedRoomId = roomId.Trim();
        _historyByRoomId.TryRemove(normalizedRoomId, out _);
        _connectionsByRoomId.TryRemove(normalizedRoomId, out _);
        _logger.LogInformation("Live room {RoomId} signaling state removed.", normalizedRoomId);
    }

    public async Task<int> RemoveRoomAsync(string roomId, CancellationToken cancellationToken = default)
    {
        var normalizedRoomId = roomId.Trim();
        var closedConnectionCount = 0;

        if (_connectionsByRoomId.TryRemove(normalizedRoomId, out var connections))
        {
            closedConnectionCount = await CloseConnectionsAsync(connections.Values, "Live room removed by admin.", cancellationToken);
        }

        _historyByRoomId.TryRemove(normalizedRoomId, out _);
        _logger.LogInformation("Live room {RoomId} signaling state removed by admin.", normalizedRoomId);
        return closedConnectionCount;
    }

    public int GetSignalHistoryCount(string roomId)
    {
        var normalizedRoomId = roomId.Trim();
        return _historyByRoomId.TryGetValue(normalizedRoomId, out var queue) ? queue.Count : 0;
    }

    public int GetActiveConnectionCount(string roomId)
    {
        var normalizedRoomId = roomId.Trim();
        if (!_connectionsByRoomId.TryGetValue(normalizedRoomId, out var connections))
        {
            return 0;
        }

        var count = 0;
        foreach (var pair in connections)
        {
            if (pair.Value.Socket.State == WebSocketState.Open)
            {
                count++;
                continue;
            }

            connections.TryRemove(pair.Key, out _);
        }

        return count;
    }

    public async Task<CallSignalMessageDto> AppendSignalAsync(string roomId, CallSignalRequest request, CancellationToken cancellationToken = default)
    {
        var message = new CallSignalMessageDto
        {
            CallId = roomId.Trim(),
            SenderUserId = request.SenderUserId.Trim(),
            SignalType = request.SignalType.Trim(),
            PayloadJson = request.PayloadJson,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        var queue = _historyByRoomId.GetOrAdd(message.CallId, _ => new ConcurrentQueue<CallSignalMessageDto>());
        queue.Enqueue(message);
        await BroadcastAsync(message, message.SenderUserId, cancellationToken);
        return message;
    }

    public async Task AttachWebSocketAsync(string roomId, string userId, WebSocket webSocket, CancellationToken cancellationToken)
    {
        var normalizedRoomId = roomId.Trim();
        var normalizedUserId = userId.Trim();
        var connections = _connectionsByRoomId.GetOrAdd(normalizedRoomId, _ => new ConcurrentDictionary<string, WebSocketConnection>(StringComparer.OrdinalIgnoreCase));
        var connectionId = Guid.NewGuid().ToString("N");
        connections[connectionId] = new WebSocketConnection(connectionId, normalizedUserId, webSocket);

        await SendHistoryAsync(normalizedRoomId, webSocket, cancellationToken);

        var buffer = new byte[16 * 1024];
        try
        {
            while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var payload = await ReceiveTextAsync(webSocket, buffer, cancellationToken);
                if (payload is null)
                {
                    break;
                }

                var request = JsonSerializer.Deserialize<CallSignalRequest>(payload, JsonOptions);
                if (request is null)
                {
                    continue;
                }

                var effectiveRequest = request with
                {
                    CallId = normalizedRoomId,
                    SenderUserId = string.IsNullOrWhiteSpace(request.SenderUserId) ? normalizedUserId : request.SenderUserId
                };

                await AppendSignalAsync(normalizedRoomId, effectiveRequest, cancellationToken);
            }
        }
        finally
        {
            connections.TryRemove(connectionId, out _);
            if (connections.IsEmpty)
            {
                _connectionsByRoomId.TryRemove(normalizedRoomId, out _);
            }

            if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Live room socket closed.", CancellationToken.None);
            }
        }
    }

    private async Task SendHistoryAsync(string roomId, WebSocket webSocket, CancellationToken cancellationToken)
    {
        foreach (var item in GetSignals(roomId))
        {
            await SendAsync(webSocket, item, cancellationToken);
        }
    }

    private async Task BroadcastAsync(CallSignalMessageDto message, string excludeUserId, CancellationToken cancellationToken)
    {
        if (!_connectionsByRoomId.TryGetValue(message.CallId, out var connections))
        {
            return;
        }

        foreach (var pair in connections)
        {
            var connection = pair.Value;
            if (connection.Socket.State != WebSocketState.Open)
            {
                connections.TryRemove(pair.Key, out _);
                continue;
            }

            if (string.Equals(connection.UserId, excludeUserId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            await SendAsync(connection.Socket, message, cancellationToken);
        }
    }

    private static async Task SendAsync(WebSocket socket, CallSignalMessageDto message, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    private static async Task<string?> ReceiveTextAsync(WebSocket socket, byte[] buffer, CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            ms.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                break;
            }
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static async Task<int> CloseConnectionsAsync(IEnumerable<WebSocketConnection> connections, string closeReason, CancellationToken cancellationToken)
    {
        var closedConnectionCount = 0;
        foreach (var connection in connections)
        {
            if (connection.Socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
            {
                continue;
            }

            await connection.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, closeReason, cancellationToken);
            closedConnectionCount++;
        }

        return closedConnectionCount;
    }

    private sealed record WebSocketConnection(string ConnectionId, string UserId, WebSocket Socket);
}