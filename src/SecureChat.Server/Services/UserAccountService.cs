using SecureChat.Shared.Contracts.Auth;

namespace SecureChat.Server.Services;

public sealed class UserAccountService
{
    private readonly Dictionary<string, CurrentUserResponse> _usersByExternalId = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public CurrentUserResponse GetOrCreateMicrosoftUser(string externalId, string displayName, string email)
    {
        lock (_lock)
        {
            if (_usersByExternalId.TryGetValue(externalId, out var existing))
            {
                return existing;
            }

            var user = new CurrentUserResponse
            {
                UserId = $"ms-{Guid.NewGuid():N}",
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? email : displayName,
                Email = email,
                AuthProvider = "microsoft"
            };

            _usersByExternalId[externalId] = user;
            return user;
        }
    }
}
