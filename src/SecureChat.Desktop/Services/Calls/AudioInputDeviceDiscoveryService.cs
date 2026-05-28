using NAudio.CoreAudioApi;
using NAudio.Wave;
using SecureChat.Desktop.Models;

namespace SecureChat.Desktop.Services.Calls;

public sealed class AudioInputDeviceDiscoveryService
{
    public IReadOnlyList<AudioInputDeviceOption> GetInputDevices()
    {
        var devices = new List<AudioInputDeviceOption>();

        for (var index = 0; index < WaveIn.DeviceCount; index++)
        {
            var capabilities = WaveIn.GetCapabilities(index);
            var displayName = string.IsNullOrWhiteSpace(capabilities.ProductName)
                ? $"输入设备 {index}"
                : capabilities.ProductName.Trim();

            devices.Add(new AudioInputDeviceOption(
                AudioInputDeviceKind.Microphone,
                $"麦克风: {displayName}",
                DeviceNumber: index));
        }

        using var enumerator = new MMDeviceEnumerator();
        var renderDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .OrderBy(device => device.FriendlyName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var seenRenderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        MMDevice? defaultMultimediaRender = null;
        MMDevice? defaultCommunicationsRender = null;

        try
        {
            defaultMultimediaRender = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
        catch
        {
        }

        try
        {
            defaultCommunicationsRender = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Communications);
        }
        catch
        {
        }

        if (defaultMultimediaRender is not null
            && defaultCommunicationsRender is not null
            && string.Equals(defaultMultimediaRender.ID, defaultCommunicationsRender.ID, StringComparison.OrdinalIgnoreCase))
        {
            devices.Add(new AudioInputDeviceOption(
                AudioInputDeviceKind.SystemLoopback,
                $"系统音频: 默认输出 ({defaultMultimediaRender.FriendlyName}, 多媒体/通信)",
                DeviceId: defaultMultimediaRender.ID));
            seenRenderIds.Add(defaultMultimediaRender.ID);
        }

        if (defaultMultimediaRender is not null && seenRenderIds.Add(defaultMultimediaRender.ID))
        {
            devices.Add(new AudioInputDeviceOption(
                AudioInputDeviceKind.SystemLoopback,
                $"系统音频: 默认多媒体输出 ({defaultMultimediaRender.FriendlyName})",
                DeviceId: defaultMultimediaRender.ID));
        }

        if (defaultCommunicationsRender is not null && seenRenderIds.Add(defaultCommunicationsRender.ID))
        {
            devices.Add(new AudioInputDeviceOption(
                AudioInputDeviceKind.SystemLoopback,
                $"系统音频: 默认通信输出 ({defaultCommunicationsRender.FriendlyName})",
                DeviceId: defaultCommunicationsRender.ID));
        }

        foreach (var device in renderDevices)
        {
            if (!seenRenderIds.Add(device.ID))
            {
                continue;
            }

            devices.Add(new AudioInputDeviceOption(
                AudioInputDeviceKind.SystemLoopback,
                $"系统音频: {device.FriendlyName}",
                DeviceId: device.ID));
        }

        return devices
            .OrderBy(device => device.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}