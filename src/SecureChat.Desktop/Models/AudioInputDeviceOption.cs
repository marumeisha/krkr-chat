namespace SecureChat.Desktop.Models;

public enum AudioInputDeviceKind
{
    Microphone,
    SystemLoopback
}

public sealed record AudioInputDeviceOption(
    AudioInputDeviceKind Kind,
    string DisplayName,
    int? DeviceNumber = null,
    string? DeviceId = null)
{
    public bool IsMicrophone => Kind == AudioInputDeviceKind.Microphone;
    public bool IsSystemLoopback => Kind == AudioInputDeviceKind.SystemLoopback;

    public override string ToString() => DisplayName;
}