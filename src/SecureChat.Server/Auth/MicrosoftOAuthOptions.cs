namespace SecureChat.Server.Auth;

public sealed class MicrosoftOAuthOptions
{
    public const string SectionName = "Authentication:Microsoft";

    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string Tenant { get; set; } = "common";
    public string CallbackPath { get; set; } = "/api/auth/oauth/microsoft/callback";
}
