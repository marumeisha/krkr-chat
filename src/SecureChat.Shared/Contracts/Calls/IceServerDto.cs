namespace SecureChat.Shared.Contracts.Calls;

public sealed record IceServerDto
{
    public IReadOnlyList<string> Urls { get; init; } = [];
    public string Username { get; init; } = "";
    public string Credential { get; init; } = "";
}