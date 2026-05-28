namespace SecureChat.Desktop.Models;

public sealed record VideoCaptureDeviceOption(string DisplayName, string DevicePath)
{
    public override string ToString() => DisplayName;
}