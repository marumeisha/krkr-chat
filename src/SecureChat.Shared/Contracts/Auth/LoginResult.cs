namespace SecureChat.Shared.Contracts.Auth;

public sealed record LoginResult
{
    public string AccessToken { get; init; } = "";
    public DateTimeOffset ExpiresAt { get; init; }
    public CurrentUserResponse User { get; init; } = new();
}
