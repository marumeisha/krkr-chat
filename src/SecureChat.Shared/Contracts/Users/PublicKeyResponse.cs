namespace SecureChat.Shared.Contracts.Users;

public sealed record PublicKeyResponse
{
    public string UserId { get; init; } = "";
    public string PublicKeyPem { get; init; } = "";
}
