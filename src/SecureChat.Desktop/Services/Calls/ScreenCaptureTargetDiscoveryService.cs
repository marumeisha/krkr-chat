using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using SecureChat.Desktop.Models;
using Forms = System.Windows.Forms;

namespace SecureChat.Desktop.Services.Calls;

public sealed class ScreenCaptureTargetDiscoveryService
{
    public IReadOnlyList<ScreenCaptureTargetOption> GetTargets()
    {
        var targets = new List<ScreenCaptureTargetOption>();
        var desktopBounds = Forms.Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1280, 720);
        targets.Add(new ScreenCaptureTargetOption("整个桌面", "desktop", desktopBounds, IsDesktop: true));

        EnumWindows((hwnd, _) =>
        {
            if (!IsCandidateWindow(hwnd, out var title, out var bounds))
            {
                return true;
            }

            var sourcePath = $"hwnd=0x{hwnd.ToInt64():X}";
            var displayName = $"窗口: {title}";
            targets.Add(new ScreenCaptureTargetOption(displayName, sourcePath, bounds));
            return true;
        }, IntPtr.Zero);

        return targets
            .GroupBy(target => target.SourcePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(target => target.IsDesktop)
            .ThenBy(target => target.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static bool IsCandidateWindow(IntPtr hwnd, out string title, out Rectangle bounds)
    {
        title = string.Empty;
        bounds = Rectangle.Empty;

        if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd) || IsIconic(hwnd) || GetWindowTextLength(hwnd) == 0)
        {
            return false;
        }

        if (!GetWindowRect(hwnd, out var rect))
        {
            return false;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width < 64 || height < 64)
        {
            return false;
        }

        var titleBuilder = new StringBuilder(GetWindowTextLength(hwnd) + 1);
        _ = GetWindowText(hwnd, titleBuilder, titleBuilder.Capacity);
        title = titleBuilder.ToString().Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        bounds = new Rectangle(rect.Left, rect.Top, width, height);
        return true;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
}