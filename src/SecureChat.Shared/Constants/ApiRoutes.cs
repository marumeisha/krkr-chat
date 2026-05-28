namespace SecureChat.Shared.Constants;

public static class ApiRoutes
{
    public const string RegisterPublicKey = "/api/users/register-key";
    public const string GetPublicKey = "/api/users/{userId}/public-key";
    public const string SendMessage = "/api/messages/send";
    public const string GetInbox = "/api/messages/inbox/{userId}";
    public const string MicrosoftLoginStart = "/api/auth/oauth/microsoft/start";
    public const string Me = "/api/auth/me";
    public const string UpdateMyUserId = "/api/auth/me/user-id";
    public const string OnlineHeartbeat = "/api/online/heartbeat";
    public const string OnlineStats = "/api/online/stats";
    public const string StartCall = "/api/calls/start";
    public const string GetPendingCalls = "/api/calls/pending/{userId}";
    public const string CallSignal = "/api/calls/signal";
    public const string GetCallSignals = "/api/calls/{callId}/signals";
    public const string GetCallDiagnostics = "/api/calls/{callId}/diagnostics";
    public const string CallSignalWebSocket = "/ws/calls/{callId}";
    public const string CreateLiveRoom = "/api/live-rooms";
    public const string GetPublicLiveRooms = "/api/live-rooms";
    public const string GetLiveRoom = "/api/live-rooms/{roomId}";
    public const string JoinLiveRoom = "/api/live-rooms/{roomId}/join";
    public const string LeaveLiveRoom = "/api/live-rooms/{roomId}/leave";
    public const string LiveRoomSignal = "/api/live-rooms/{roomId}/signals";
    public const string GetLiveRoomSignals = "/api/live-rooms/{roomId}/signals";
    public const string GetLiveRoomDiagnostics = "/api/live-rooms/{roomId}/diagnostics";
    public const string LiveRoomSignalWebSocket = "/ws/live-rooms/{roomId}";
}
