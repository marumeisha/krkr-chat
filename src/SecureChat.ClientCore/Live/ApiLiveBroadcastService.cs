using SecureChat.ClientCore.Services;
using SecureChat.Shared.Contracts.Live;

namespace SecureChat.ClientCore.Live;

public sealed class ApiLiveBroadcastService : ILiveBroadcastService
{
    private readonly ApiClient _apiClient;

    public ApiLiveBroadcastService(ApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public Task<LiveRoomDto> CreateRoomAsync(CreateLiveRoomRequest request, CancellationToken cancellationToken = default)
        => _apiClient.CreateLiveRoomAsync(request, cancellationToken);

    public Task<LiveRoomDto?> GetRoomAsync(string roomId, CancellationToken cancellationToken = default)
        => _apiClient.GetLiveRoomAsync(roomId, cancellationToken);

    public Task<IReadOnlyList<LiveRoomDto>> GetPublicRoomsAsync(CancellationToken cancellationToken = default)
        => _apiClient.GetPublicLiveRoomsAsync(cancellationToken);

    public Task<LiveRoomDto> JoinRoomAsync(string roomId, JoinLiveRoomRequest request, CancellationToken cancellationToken = default)
        => _apiClient.JoinLiveRoomAsync(roomId, request, cancellationToken);

    public Task LeaveRoomAsync(string roomId, LeaveLiveRoomRequest request, CancellationToken cancellationToken = default)
        => _apiClient.LeaveLiveRoomAsync(roomId, request, cancellationToken);
}