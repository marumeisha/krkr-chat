namespace SecureChat.Server.Services;

public sealed class PublicKeyDirectoryService
{
    private readonly Dictionary<string, string> _publicKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public void Set(string userId, string publicKeyPem)
    {
        lock (_lock)
        {
            _publicKeys[userId] = publicKeyPem;
        }
    }

    public string? Get(string userId)
    {
        lock (_lock)
        {
            return _publicKeys.TryGetValue(userId, out var value) ? value : null;
        }
    }
}
