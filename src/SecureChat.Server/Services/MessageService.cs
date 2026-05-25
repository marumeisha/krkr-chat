using SecureChat.Shared.Contracts.Messages;

namespace SecureChat.Server.Services;

public sealed class MessageService
{
    private readonly List<MessageDto> _messages = [];
    private readonly object _lock = new();

    public void Add(SendMessageRequest request)
    {
        lock (_lock)
        {
            _messages.Add(new MessageDto
            {
                MessageId = Guid.NewGuid().ToString("N"),
                SenderUserId = request.SenderUserId,
                RecipientUserId = request.RecipientUserId,
                EnvelopeJson = request.EnvelopeJson,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
    }

    public IReadOnlyList<MessageDto> GetInbox(string userId)
    {
        lock (_lock)
        {
            return _messages
                .Where(x => string.Equals(x.RecipientUserId, userId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.CreatedAt)
                .ToList();
        }
    }
}
