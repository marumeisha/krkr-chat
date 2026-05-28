using SecureChat.Shared.Contracts.Calls;

namespace SecureChat.ClientCore.Calls;

public interface IVideoCallService
{
    Task<CallCapabilitiesDto> GetCapabilitiesAsync(CancellationToken cancellationToken = default);
    Task<StartCallResponse> StartCallAsync(StartCallRequest request, CancellationToken cancellationToken = default);
    Task SendSignalAsync(CallSignalRequest request, CancellationToken cancellationToken = default);
}