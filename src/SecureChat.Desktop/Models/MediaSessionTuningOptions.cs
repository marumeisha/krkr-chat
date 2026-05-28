namespace SecureChat.Desktop.Models;

public sealed record MediaSessionTuningOptions
{
    public int AudioBitrate { get; init; } = 24_000;
    public int CameraVideoBitrate { get; init; } = 1_500_000;
    public int CameraFrameRate { get; init; } = 15;
    public int ScreenShareBitrate { get; init; } = 1_500_000;
    public int ScreenShareFrameRate { get; init; } = 8;
}