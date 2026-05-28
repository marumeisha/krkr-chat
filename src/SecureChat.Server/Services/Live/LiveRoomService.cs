using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using SecureChat.Server.Services.Calls;
using SecureChat.Shared.Contracts.Calls;
using SecureChat.Shared.Contracts.Live;

namespace SecureChat.Server.Services.Live;

public sealed class LiveRoomService
{
    private readonly IReadOnlyList<IceServerDto> _iceServers;
    private readonly ConcurrentDictionary<string, LiveRoomState> _rooms = new(StringComparer.OrdinalIgnoreCase);

    public LiveRoomService(IOptions<CallMediaOptions> callMediaOptions)
    {
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

    public LiveRoomDto CreateRoom(CreateLiveRoomRequest request)
    {
        var roomId = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var now = DateTimeOffset.UtcNow;
        var room = new LiveRoomState(
            roomId,
            string.IsNullOrWhiteSpace(request.DisplayName) ? $"{request.HostUserId.Trim()} 的直播间" : request.DisplayName.Trim(),
            request.HostUserId.Trim(),
            string.IsNullOrWhiteSpace(request.HostDeviceId) ? "unknown-device" : request.HostDeviceId.Trim(),
            request.IsPublic,
            now,
            now);

        _rooms[roomId] = room;
        return room.ToDto(_iceServers);
    }

    public void TouchRoom(string roomId)
    {
        var normalizedRoomId = roomId.Trim();
        if (_rooms.TryGetValue(normalizedRoomId, out var room))
        {
            room.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    public IReadOnlyList<LiveRoomDto> GetPublicRooms()
    {
        return _rooms.Values
            .Where(room => room.IsPublic)
            .OrderByDescending(room => room.UpdatedAtUtc)
            .Select(room => room.ToDto(_iceServers))
            .ToList();
    }

    public LiveRoomDto? GetRoom(string roomId)
    {
        return _rooms.TryGetValue(roomId.Trim(), out var room) ? room.ToDto(_iceServers) : null;
    }

    public IReadOnlyList<string> GetRoomIds()
    {
        return _rooms.Keys
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public LiveRoomDiagnosticsDto? GetDiagnostics(string roomId, int signalHistoryCount, int activeConnectionCount)
    {
        var normalizedRoomId = roomId.Trim();
        if (!_rooms.TryGetValue(normalizedRoomId, out var room))
        {
            return null;
        }

        return new LiveRoomDiagnosticsDto
        {
            RoomId = room.RoomId,
            DisplayName = room.DisplayName,
            HostUserId = room.HostUserId,
            IsPublic = room.IsPublic,
            ViewerCount = room.Viewers.Count,
            CreatedAtUtc = room.CreatedAtUtc,
            LastActivityAtUtc = room.UpdatedAtUtc,
            SignalHistoryCount = signalHistoryCount,
            ActiveConnectionCount = activeConnectionCount
        };
    }

    public LiveRoomDto JoinRoom(string roomId, JoinLiveRoomRequest request)
    {
        var normalizedRoomId = roomId.Trim();
        if (!_rooms.TryGetValue(normalizedRoomId, out var room))
        {
            throw new KeyNotFoundException($"直播房间不存在: {normalizedRoomId}");
        }

        var normalizedUserId = request.UserId.Trim();
        var normalizedDeviceId = string.IsNullOrWhiteSpace(request.DeviceId) ? "unknown-device" : request.DeviceId.Trim();

        if (!string.Equals(room.HostUserId, normalizedUserId, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(room.ActiveViewerUserId)
                && !string.Equals(room.ActiveViewerUserId, normalizedUserId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("当前版本的直播房间仅支持 1 个观众同时观看。请等待当前观众离开后再加入。 ");
            }

            room.Viewers[$"{normalizedUserId}@{normalizedDeviceId}"] = DateTimeOffset.UtcNow;
            room.ActiveViewerUserId = normalizedUserId;
        }

        room.UpdatedAtUtc = DateTimeOffset.UtcNow;
        return room.ToDto(_iceServers);
    }

    public void LeaveRoom(string roomId, LeaveLiveRoomRequest request)
    {
        var normalizedRoomId = roomId.Trim();
        if (!_rooms.TryGetValue(normalizedRoomId, out var room))
        {
            return;
        }

        var normalizedUserId = request.UserId.Trim();
        var normalizedDeviceId = string.IsNullOrWhiteSpace(request.DeviceId) ? "unknown-device" : request.DeviceId.Trim();
        if (string.Equals(room.HostUserId, normalizedUserId, StringComparison.OrdinalIgnoreCase))
        {
            _rooms.TryRemove(normalizedRoomId, out _);
            return;
        }

        room.Viewers.TryRemove($"{normalizedUserId}@{normalizedDeviceId}", out _);
        if (string.Equals(room.ActiveViewerUserId, normalizedUserId, StringComparison.OrdinalIgnoreCase))
        {
            room.ActiveViewerUserId = null;
        }
        room.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public IReadOnlyList<string> CleanupInactiveRooms(DateTimeOffset utcNow, TimeSpan idleTimeout, Func<string, bool>? canRemoveRoom = null)
    {
        if (idleTimeout <= TimeSpan.Zero)
        {
            return [];
        }

        var removedRoomIds = new List<string>();
        foreach (var pair in _rooms)
        {
            var room = pair.Value;
            if (utcNow - room.UpdatedAtUtc < idleTimeout)
            {
                continue;
            }

            if (canRemoveRoom is not null && !canRemoveRoom(room.RoomId))
            {
                continue;
            }

            if (_rooms.TryRemove(pair.Key, out _))
            {
                removedRoomIds.Add(room.RoomId);
            }
        }

        return removedRoomIds;
    }

    public bool RemoveRoom(string roomId)
    {
        return _rooms.TryRemove(roomId.Trim(), out _);
    }

    private sealed class LiveRoomState
    {
        public LiveRoomState(string roomId, string displayName, string hostUserId, string hostDeviceId, bool isPublic, DateTimeOffset createdAtUtc, DateTimeOffset updatedAtUtc)
        {
            RoomId = roomId;
            DisplayName = displayName;
            HostUserId = hostUserId;
            HostDeviceId = hostDeviceId;
            IsPublic = isPublic;
            CreatedAtUtc = createdAtUtc;
            UpdatedAtUtc = updatedAtUtc;
        }

        public string RoomId { get; }
        public string DisplayName { get; }
        public string HostUserId { get; }
        public string HostDeviceId { get; }
        public bool IsPublic { get; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset UpdatedAtUtc { get; set; }
        public string? ActiveViewerUserId { get; set; }
        public ConcurrentDictionary<string, DateTimeOffset> Viewers { get; } = new(StringComparer.OrdinalIgnoreCase);

        public LiveRoomDto ToDto(IReadOnlyList<IceServerDto> iceServers)
        {
            return new LiveRoomDto
            {
                RoomId = RoomId,
                DisplayName = DisplayName,
                HostUserId = HostUserId,
                IsPublic = IsPublic,
                ViewerCount = Viewers.Count,
                CreatedAtUtc = CreatedAtUtc,
                UpdatedAtUtc = UpdatedAtUtc,
                IceServers = iceServers
            };
        }
    }
}