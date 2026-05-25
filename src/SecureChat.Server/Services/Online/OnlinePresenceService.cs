using System.Collections.Concurrent;
using SecureChat.Shared.Contracts.Online;

namespace SecureChat.Server.Services.Online;

public sealed class OnlinePresenceService
{
    private readonly ConcurrentDictionary<string, PresenceRecord> _records = new(StringComparer.OrdinalIgnoreCase);

    public void Heartbeat(string userId, string deviceId, DateTimeOffset now)
    {
        var normalizedUser = userId.Trim();
        var normalizedDevice = string.IsNullOrWhiteSpace(deviceId) ? "unknown-device" : deviceId.Trim();
        var key = BuildKey(normalizedUser, normalizedDevice);

        _records.AddOrUpdate(
            key,
            _ => new PresenceRecord
            {
                UserId = normalizedUser,
                DeviceId = normalizedDevice,
                LastSeenUtc = now
            },
            (_, existing) => existing with { LastSeenUtc = now });
    }

    public OnlineStatsResponse GetSnapshot(TimeSpan ttl, DateTimeOffset now)
    {
        var cutoff = now - ttl;
        foreach (var pair in _records)
        {
            if (pair.Value.LastSeenUtc < cutoff)
            {
                _records.TryRemove(pair.Key, out _);
            }
        }

        var active = _records.Values
            .Where(x => x.LastSeenUtc >= cutoff)
            .ToList();

        var users = active
            .GroupBy(x => x.UserId, StringComparer.OrdinalIgnoreCase)
            .Select(group => new OnlineUserStatusDto
            {
                UserId = group.Key,
                DeviceCount = group.Select(x => x.DeviceId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                LastSeenUtc = group.Max(x => x.LastSeenUtc)
            })
            .OrderByDescending(x => x.LastSeenUtc)
            .ThenBy(x => x.UserId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new OnlineStatsResponse
        {
            OnlineUserCount = users.Count,
            ActiveDeviceCount = active.Select(x => BuildKey(x.UserId, x.DeviceId)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            Users = users
        };
    }

    private static string BuildKey(string userId, string deviceId) => $"{userId}::{deviceId}";

    private sealed record PresenceRecord
    {
        public string UserId { get; init; } = "";
        public string DeviceId { get; init; } = "";
        public DateTimeOffset LastSeenUtc { get; init; }
    }
}
