using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SecureChat.Shared.Contracts.Calls;

namespace SecureChat.Server.Services.Calls;

public sealed class CallSignalingService
{
    private readonly ILogger<CallSignalingService> _logger;
    private readonly IReadOnlyList<IceServerDto> _iceServers;
    private readonly ConcurrentDictionary<string, ConcurrentQueue<CallSignalMessageDto>> _historyByCallId = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, WebSocketConnection>> _connectionsByCallId = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, PendingCallDto> _pendingCallsById = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CallSessionState> _sessionsByCallId = new(StringComparer.OrdinalIgnoreCase);

    public CallSignalingService(ILogger<CallSignalingService> logger, IOptions<CallMediaOptions> callMediaOptions)
    {
        _logger = logger;
        _iceServers = (callMediaOptions.Value.IceServers ?? [])
            .Where(server => server.Urls.Count > 0)
            .Select(server => new IceServerDto
            {
                Urls = server.Urls.Where(url => !string.IsNullOrWhiteSpace(url)).ToArray(),
                Username = server.Username,
                Credential = server.Credential
            })
            .Where(server => server.Urls.Count > 0)
            .ToArray();
    }

    public StartCallResponse StartCall(StartCallRequest request)
    {
        var callId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        _historyByCallId.TryAdd(callId, new ConcurrentQueue<CallSignalMessageDto>());
        _connectionsByCallId.TryAdd(callId, new ConcurrentDictionary<string, WebSocketConnection>(StringComparer.OrdinalIgnoreCase));
        _pendingCallsById[callId] = new PendingCallDto
        {
            CallId = callId,
            CallerUserId = request.CallerUserId.Trim(),
            RecipientUserId = request.RecipientUserId.Trim(),
            CallerDeviceId = request.CallerDeviceId.Trim(),
            AudioEnabled = request.AudioEnabled,
            VideoEnabled = request.VideoEnabled,
            CreatedAtUtc = now
        };
        _sessionsByCallId[callId] = new CallSessionState(now, now);

        _logger.LogInformation("Call {CallId} started from {CallerUserId} to {RecipientUserId}.", callId, request.CallerUserId, request.RecipientUserId);
        if (_iceServers.Count == 0)
        {
            _logger.LogWarning("Call {CallId} started without any ICE servers configured. Cross-network device connections may fail.", callId);
        }

        return new StartCallResponse
        {
            CallId = callId,
            SignalTransport = "WebSocket signaling API",
            MediaTransport = "Reserved WebRTC media pipeline",
            RequiresServerSupport = true,
            IceServers = _iceServers
        };
    }

    public IReadOnlyList<PendingCallDto> GetPendingCalls(string userId)
    {
        var normalizedUserId = userId.Trim();
        return _pendingCallsById.Values
            .Where(call => string.Equals(call.RecipientUserId, normalizedUserId, StringComparison.OrdinalIgnoreCase))
            .Where(call => call.RecipientJoinedAtUtc is null)
            .OrderByDescending(call => call.CreatedAtUtc)
            .ToList();
    }

    public IReadOnlyList<CallSignalMessageDto> GetSignals(string callId)
    {
        var normalizedCallId = callId.Trim();
        if (!_historyByCallId.TryGetValue(normalizedCallId, out var queue))
        {
            return [];
        }

        return queue.OrderBy(item => item.CreatedAtUtc).ToList();
    }

    public async Task<CallSignalMessageDto> AppendSignalAsync(CallSignalRequest request, CancellationToken cancellationToken = default)
    {
        var message = new CallSignalMessageDto
        {
            CallId = request.CallId.Trim(),
            SenderUserId = request.SenderUserId.Trim(),
            SignalType = request.SignalType.Trim(),
            PayloadJson = request.PayloadJson,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        var queue = _historyByCallId.GetOrAdd(message.CallId, _ => new ConcurrentQueue<CallSignalMessageDto>());
        queue.Enqueue(message);
        TouchCall(message.CallId, message.CreatedAtUtc);

        _logger.LogInformation("Call {CallId} signal appended: {SignalType} from {SenderUserId}.", message.CallId, message.SignalType, message.SenderUserId);

        await BroadcastAsync(message, excludeUserId: message.SenderUserId, cancellationToken);
        return message;
    }

    public async Task AttachWebSocketAsync(string callId, string userId, WebSocket webSocket, CancellationToken cancellationToken)
    {
        var normalizedCallId = callId.Trim();
        var normalizedUserId = userId.Trim();
        TouchCall(normalizedCallId);
        MarkParticipantConnected(normalizedCallId, normalizedUserId);
        var connections = _connectionsByCallId.GetOrAdd(normalizedCallId, _ => new ConcurrentDictionary<string, WebSocketConnection>(StringComparer.OrdinalIgnoreCase));
        var connectionId = Guid.NewGuid().ToString("N");
        connections[connectionId] = new WebSocketConnection(connectionId, normalizedUserId, webSocket);

        _logger.LogInformation("Call {CallId} websocket attached: {UserId} ({ConnectionId}).", normalizedCallId, normalizedUserId, connectionId);

        await SendHistoryAsync(normalizedCallId, webSocket, cancellationToken);

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
                    CallId = string.IsNullOrWhiteSpace(request.CallId) ? normalizedCallId : request.CallId,
                    SenderUserId = string.IsNullOrWhiteSpace(request.SenderUserId) ? normalizedUserId : request.SenderUserId
                };

                await AppendSignalAsync(effectiveRequest, cancellationToken);
            }
        }
        finally
        {
            connections.TryRemove(connectionId, out _);
            TouchCall(normalizedCallId);

            if (connections.IsEmpty)
            {
                _connectionsByCallId.TryRemove(normalizedCallId, out _);
            }

            _logger.LogInformation("Call {CallId} websocket detached: {UserId} ({ConnectionId}).", normalizedCallId, normalizedUserId, connectionId);

            if (webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Call socket closed.", CancellationToken.None);
            }
        }
    }

    private void MarkParticipantConnected(string callId, string userId)
    {
        if (!_pendingCallsById.TryGetValue(callId, out var pendingCall))
        {
            return;
        }

        if (!string.Equals(pendingCall.RecipientUserId, userId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _pendingCallsById[callId] = pendingCall with
        {
            RecipientJoinedAtUtc = DateTimeOffset.UtcNow
        };
        TouchCall(callId);
    }

    public CallSessionDiagnosticsDto? GetDiagnostics(string callId)
    {
        var normalizedCallId = callId.Trim();
        if (!_sessionsByCallId.TryGetValue(normalizedCallId, out var session))
        {
            return null;
        }

        var activeConnectionCount = GetActiveConnectionCount(normalizedCallId);
        var signalHistoryCount = _historyByCallId.TryGetValue(normalizedCallId, out var queue) ? queue.Count : 0;
        var hasPendingInvitation = _pendingCallsById.TryGetValue(normalizedCallId, out var pendingCall);

        return new CallSessionDiagnosticsDto
        {
            CallId = normalizedCallId,
            CreatedAtUtc = session.CreatedAtUtc,
            LastActivityAtUtc = session.LastActivityAtUtc,
            HasPendingInvitation = hasPendingInvitation,
            RecipientJoined = pendingCall?.RecipientJoinedAtUtc is not null,
            SignalHistoryCount = signalHistoryCount,
            ActiveConnectionCount = activeConnectionCount
        };
    }

    public IReadOnlyList<string> GetCallIds()
    {
        return _sessionsByCallId.Keys
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<string> CleanupInactiveCalls(DateTimeOffset utcNow, TimeSpan idleTimeout)
    {
        if (idleTimeout <= TimeSpan.Zero)
        {
            return [];
        }

        var removedCallIds = new List<string>();
        foreach (var pair in _sessionsByCallId)
        {
            var callId = pair.Key;
            var session = pair.Value;
            if (utcNow - session.LastActivityAtUtc < idleTimeout)
            {
                continue;
            }

            if (HasActiveConnections(callId))
            {
                continue;
            }

            if (_sessionsByCallId.TryRemove(callId, out _))
            {
                _historyByCallId.TryRemove(callId, out _);
                _connectionsByCallId.TryRemove(callId, out _);
                _pendingCallsById.TryRemove(callId, out _);
                removedCallIds.Add(callId);
            }
        }

        return removedCallIds;
    }

    public async Task<int> RemoveCallAsync(string callId, CancellationToken cancellationToken = default)
    {
        var normalizedCallId = callId.Trim();
        var closedConnectionCount = 0;

        if (_connectionsByCallId.TryRemove(normalizedCallId, out var connections))
        {
            closedConnectionCount = await CloseConnectionsAsync(connections.Values, "Call session removed by admin.", cancellationToken);
        }

        _historyByCallId.TryRemove(normalizedCallId, out _);
        _pendingCallsById.TryRemove(normalizedCallId, out _);
        _sessionsByCallId.TryRemove(normalizedCallId, out _);
        return closedConnectionCount;
    }

    private void TouchCall(string callId, DateTimeOffset? utcNow = null)
    {
        var normalizedCallId = callId.Trim();
        var now = utcNow ?? DateTimeOffset.UtcNow;

        _sessionsByCallId.AddOrUpdate(
            normalizedCallId,
            _ => new CallSessionState(now, now),
            (_, existing) => existing with { LastActivityAtUtc = now });
    }

    private bool HasActiveConnections(string callId)
    {
        return GetActiveConnectionCount(callId) > 0;
    }

    private int GetActiveConnectionCount(string callId)
    {
        var normalizedCallId = callId.Trim();
        if (!_connectionsByCallId.TryGetValue(normalizedCallId, out var connections))
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

    private async Task SendHistoryAsync(string callId, WebSocket webSocket, CancellationToken cancellationToken)
    {
        var history = GetSignals(callId);
        foreach (var item in history)
        {
            await SendAsync(webSocket, item, cancellationToken);
        }
    }

    private async Task BroadcastAsync(CallSignalMessageDto message, string excludeUserId, CancellationToken cancellationToken)
    {
        if (!_connectionsByCallId.TryGetValue(message.CallId, out var connections))
        {
            _logger.LogInformation("Call {CallId} signal {SignalType} has no active websocket connections.", message.CallId, message.SignalType);
            return;
        }

        var sentCount = 0;
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
            sentCount++;
        }

        _logger.LogInformation("Call {CallId} signal {SignalType} broadcast to {SentCount} connection(s), excluding {SenderUserId}.", message.CallId, message.SignalType, sentCount, excludeUserId);
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
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }
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

    private sealed record CallSessionState(DateTimeOffset CreatedAtUtc, DateTimeOffset LastActivityAtUtc);

    private sealed record WebSocketConnection(string ConnectionId, string UserId, WebSocket Socket);
}
