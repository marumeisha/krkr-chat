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

    public void RenameUserId(string currentUserId, string newUserId)
    {
        if (string.Equals(currentUserId, newUserId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        lock (_lock)
        {
            for (var index = 0; index < _messages.Count; index++)
            {
                var message = _messages[index];
                var senderUserId = string.Equals(message.SenderUserId, currentUserId, StringComparison.OrdinalIgnoreCase)
                    ? newUserId
                    : message.SenderUserId;
                var recipientUserId = string.Equals(message.RecipientUserId, currentUserId, StringComparison.OrdinalIgnoreCase)
                    ? newUserId
                    : message.RecipientUserId;

                if (senderUserId == message.SenderUserId && recipientUserId == message.RecipientUserId)
                {
                    continue;
                }

                _messages[index] = message with
                {
                    SenderUserId = senderUserId,
                    RecipientUserId = recipientUserId
                };
            }
        }
    }
}
