namespace SecureChat.Server.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Authentication:Jwt";

    public string Issuer { get; set; } = "SecureChat.Server";
    public string Audience { get; set; } = "SecureChat.Client";
    public string SigningKey { get; set; } = "replace-this-with-a-long-random-development-key";
    public int ExpirationMinutes { get; set; } = 480;
}
