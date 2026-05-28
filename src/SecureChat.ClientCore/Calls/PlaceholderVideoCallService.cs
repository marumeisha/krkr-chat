using SecureChat.Shared.Contracts.Calls;

namespace SecureChat.ClientCore.Calls;

public sealed class PlaceholderVideoCallService : IVideoCallService
{
    public Task<CallCapabilitiesDto> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new CallCapabilitiesDto
        {
            AudioEnabled = true,
            VideoEnabled = true,
            ScreenShareEnabled = true,
            SignalTransport = "WebSocket signaling API",
            MediaTransport = "Reserved WebRTC media pipeline",
            PlannedStages =
            [
                "Client-side device selection",
                "SDP offer/answer exchange",
                "ICE candidate relay",
                "Encrypted media via DTLS-SRTP"
            ]
        });
    }

    public Task<StartCallResponse> StartCallAsync(StartCallRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new StartCallResponse
        {
            CallId = Guid.NewGuid().ToString("N"),
            SignalTransport = "WebSocket signaling API",
            MediaTransport = "Reserved WebRTC media pipeline",
            RequiresServerSupport = true,
            IceServers = []
        });
    }

    public Task SendSignalAsync(CallSignalRequest request, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}