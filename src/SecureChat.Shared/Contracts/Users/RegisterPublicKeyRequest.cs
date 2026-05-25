namespace SecureChat.Shared.Contracts.Users;

public sealed record RegisterPublicKeyRequest
{
    public string UserId { get; init; } = "";
    public string PublicKeyPem { get; init; } = "";
}
