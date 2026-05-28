using System.Drawing;

namespace SecureChat.Desktop.Models;

public sealed record ScreenCaptureTargetOption(string DisplayName, string SourcePath, Rectangle Bounds, bool IsDesktop = false)
{
    public override string ToString() => DisplayName;
}