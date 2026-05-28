using SecureChat.Shared.Contracts.Auth;

namespace SecureChat.Server.Services;

public sealed class UserAccountService
{
    private const int MinUserIdLength = 3;
    private const int MaxUserIdLength = 32;
    private readonly Dictionary<string, UserAccount> _usersByExternalId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _externalIdsByUserId = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public CurrentUserResponse GetOrCreateMicrosoftUser(string externalId, string displayName, string email)
    {
        lock (_lock)
        {
            if (_usersByExternalId.TryGetValue(externalId, out var existing))
            {
                var updated = existing with
                {
                    DisplayName = string.IsNullOrWhiteSpace(displayName) ? email : displayName,
                    Email = email
                };

                _usersByExternalId[externalId] = updated;
                return updated.ToResponse();
            }

            var user = new UserAccount(
                ExternalId: externalId,
                UserId: GenerateDefaultUserId(),
                DisplayName: string.IsNullOrWhiteSpace(displayName) ? email : displayName,
                Email: email,
                AuthProvider: "microsoft");

            _usersByExternalId[externalId] = user;
            _externalIdsByUserId[user.UserId] = externalId;
            return user.ToResponse();
        }
    }

    public CurrentUserResponse? GetByExternalId(string externalId)
    {
        lock (_lock)
        {
            return _usersByExternalId.TryGetValue(externalId, out var user)
                ? user.ToResponse()
                : null;
        }
    }

    public bool TryUpdateUserId(
        string externalId,
        string requestedUserId,
        out CurrentUserResponse user,
        out string previousUserId,
        out string error)
    {
        lock (_lock)
        {
            user = new CurrentUserResponse();
            previousUserId = string.Empty;
            error = string.Empty;

            if (!_usersByExternalId.TryGetValue(externalId, out var existing))
            {
                error = "当前登录用户不存在。";
                return false;
            }

            var normalizedUserId = requestedUserId.Trim();
            if (!TryValidateUserId(normalizedUserId, out error))
            {
                return false;
            }

            if (_externalIdsByUserId.TryGetValue(normalizedUserId, out var ownerExternalId) &&
                !string.Equals(ownerExternalId, externalId, StringComparison.OrdinalIgnoreCase))
            {
                error = "该 ID 已被其他用户占用。";
                return false;
            }

            previousUserId = existing.UserId;
            var updated = existing with { UserId = normalizedUserId };
            _usersByExternalId[externalId] = updated;
            _externalIdsByUserId.Remove(previousUserId);
            _externalIdsByUserId[normalizedUserId] = externalId;
            user = updated.ToResponse();
            return true;
        }
    }

    private string GenerateDefaultUserId()
    {
        while (true)
        {
            var candidate = $"ms-{Guid.NewGuid():N}";
            if (!_externalIdsByUserId.ContainsKey(candidate))
            {
                return candidate;
            }
        }
    }

    private static bool TryValidateUserId(string userId, out string error)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            error = "用户 ID 不能为空。";
            return false;
        }

        if (userId.Length < MinUserIdLength || userId.Length > MaxUserIdLength)
        {
            error = $"用户 ID 长度需要在 {MinUserIdLength} 到 {MaxUserIdLength} 个字符之间。";
            return false;
        }

        if (!char.IsLetterOrDigit(userId[0]))
        {
            error = "用户 ID 必须以字母或数字开头。";
            return false;
        }

        foreach (var ch in userId)
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')
            {
                continue;
            }

            error = "用户 ID 只能包含字母、数字、点、下划线或短横线。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private sealed record UserAccount(string ExternalId, string UserId, string DisplayName, string Email, string AuthProvider)
    {
        public CurrentUserResponse ToResponse()
        {
            return new CurrentUserResponse
            {
                UserId = UserId,
                DisplayName = DisplayName,
                Email = Email,
                AuthProvider = AuthProvider
            };
        }
    }
}
