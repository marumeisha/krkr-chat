using SecureChat.Desktop.Models;
using SIPSorceryMedia.FFmpeg;

namespace SecureChat.Desktop.Services.Calls;

public sealed class VideoDeviceDiscoveryService
{
    public IReadOnlyList<VideoCaptureDeviceOption> GetCameraDevices()
    {
        FfmpegBootstrap.EnsureInitialized();

        return (FFmpegCameraManager
            .GetCameraDevices() ?? [])
            .Where(device => !string.IsNullOrWhiteSpace(device.Path))
            .GroupBy(device => device.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(device => new VideoCaptureDeviceOption(
                string.IsNullOrWhiteSpace(device.Name) ? device.Path : device.Name,
                device.Path))
            .OrderBy(device => device.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}