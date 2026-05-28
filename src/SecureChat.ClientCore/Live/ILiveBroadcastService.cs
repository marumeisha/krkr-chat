using SecureChat.Shared.Contracts.Live;

namespace SecureChat.ClientCore.Live;

public interface ILiveBroadcastService
{
    Task<LiveRoomDto> CreateRoomAsync(CreateLiveRoomRequest request, CancellationToken cancellationToken = default);
    Task<LiveRoomDto?> GetRoomAsync(string roomId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LiveRoomDto>> GetPublicRoomsAsync(CancellationToken cancellationToken = default);
    Task<LiveRoomDto> JoinRoomAsync(string roomId, JoinLiveRoomRequest request, CancellationToken cancellationToken = default);
    Task LeaveRoomAsync(string roomId, LeaveLiveRoomRequest request, CancellationToken cancellationToken = default);
}