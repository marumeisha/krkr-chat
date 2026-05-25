using SecureChat.Shared.Contracts.Messages;

namespace SecureChat.Client.Modules.OnlineUsers;

public sealed class OnlineUsersService
{
    private static readonly TimeSpan OnlineWindow = TimeSpan.FromMinutes(5);
    private readonly Dictionary<string, DateTimeOffset> _lastSeenByUserId = new(StringComparer.OrdinalIgnoreCase);

    public void RecordSeen(string? userId, DateTimeOffset? at = null)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        _lastSeenByUserId[userId.Trim()] = at ?? DateTimeOffset.UtcNow;
    }

    public void RecordInbox(IReadOnlyList<MessageDto> inbox)
    {
        foreach (var item in inbox)
        {
            if (!string.IsNullOrWhiteSpace(item.SenderUserId))
            {
                RecordSeen(item.SenderUserId, item.CreatedAt);
            }

            if (!string.IsNullOrWhiteSpace(item.RecipientUserId))
            {
                RecordSeen(item.RecipientUserId, item.CreatedAt);
            }
        }
    }

    public IReadOnlyList<OnlineUserEntry> GetEntries(DateTimeOffset? now = null)
    {
        var current = now ?? DateTimeOffset.UtcNow;
        var entries = _lastSeenByUserId
            .Select(pair => new OnlineUserEntry
            {
                UserId = pair.Key,
                LastSeenAt = pair.Value,
                IsOnline = current - pair.Value <= OnlineWindow
            })
            .OrderByDescending(item => item.IsOnline)
            .ThenByDescending(item => item.LastSeenAt)
            .ThenBy(item => item.UserId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return entries;
    }
}
