namespace SecureChat.Shared.Contracts.Auth;

public sealed record UpdateUserIdRequest
{
    public string UserId { get; init; } = "";
}
