namespace SecureChat.Shared.Constants;

public static class ApiRoutes
{
    public const string RegisterPublicKey = "/api/users/register-key";
    public const string GetPublicKey = "/api/users/{userId}/public-key";
    public const string SendMessage = "/api/messages/send";
    public const string GetInbox = "/api/messages/inbox/{userId}";
}
