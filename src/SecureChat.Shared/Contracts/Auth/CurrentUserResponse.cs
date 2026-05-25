namespace SecureChat.Shared.Contracts.Auth;

public sealed record CurrentUserResponse
{
    public string UserId { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Email { get; init; } = "";
    public string AuthProvider { get; init; } = "";
}
