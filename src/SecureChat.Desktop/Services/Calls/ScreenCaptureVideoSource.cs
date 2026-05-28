using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;
using SIPSorceryMedia.Abstractions;

namespace SecureChat.Desktop.Services.Calls;

public sealed class ScreenCaptureVideoSource : EncodedVideoSourceBase
{
    private readonly int _targetWidth;
    private readonly int _targetHeight;
    private readonly int _fps;
    private CancellationTokenSource? _captureLoopCts;
    private Task? _captureLoopTask;
    private volatile bool _paused;

    public ScreenCaptureVideoSource(int targetWidth = 1280, int targetHeight = 720, int fps = 8)
    {
        _targetWidth = targetWidth;
        _targetHeight = targetHeight;
        _fps = fps;
    }

    public override Task PauseVideo()
    {
        _paused = true;
        return Task.CompletedTask;
    }

    public override Task ResumeVideo()
    {
        _paused = false;
        return Task.CompletedTask;
    }

    public override Task StartVideo()
    {
        if (_captureLoopTask is not null)
        {
            return Task.CompletedTask;
        }

        _captureLoopCts = new CancellationTokenSource();
        _captureLoopTask = Task.Run(() => CaptureLoopAsync(_captureLoopCts.Token));
        return Task.CompletedTask;
    }

    public override async Task CloseVideo()
    {
        if (_captureLoopCts is not null)
        {
            _captureLoopCts.Cancel();
        }

        if (_captureLoopTask is not null)
        {
            try
            {
                await _captureLoopTask;
            }
            catch (OperationCanceledException)
            {
                // Ignore cancellation during shutdown.
            }
            catch (Exception ex)
            {
                ReportSourceError($"屏幕采集关闭时发生异常: {ex.Message}");
            }
        }

        _captureLoopTask = null;
        _captureLoopCts?.Dispose();
        _captureLoopCts = null;
    }

    public override bool IsVideoSourcePaused() => _paused;

    public override void Dispose()
    {
        _captureLoopCts?.Cancel();
        _captureLoopCts?.Dispose();
        base.Dispose();
    }

    private async Task CaptureLoopAsync(CancellationToken cancellationToken)
    {
        var screenBounds = Forms.Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, _targetWidth, _targetHeight);
        var scaledSize = CalculateScaledSize(screenBounds.Size, _targetWidth, _targetHeight);
        using var screenBitmap = new Bitmap(screenBounds.Width, screenBounds.Height, PixelFormat.Format24bppRgb);
        using var scaledBitmap = new Bitmap(scaledSize.Width, scaledSize.Height, PixelFormat.Format24bppRgb);
        using var screenGraphics = Graphics.FromImage(screenBitmap);
        using var scaledGraphics = Graphics.FromImage(scaledBitmap);
        scaledGraphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        scaledGraphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
        scaledGraphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;

        var stopwatch = Stopwatch.StartNew();
        var lastFrameAt = stopwatch.ElapsedMilliseconds;
        var frameDelay = TimeSpan.FromMilliseconds(Math.Max(1, 1000 / Math.Max(1, _fps)));

        while (!cancellationToken.IsCancellationRequested)
        {
            if (_paused)
            {
                await Task.Delay(100, cancellationToken);
                continue;
            }

            screenGraphics.CopyFromScreen(screenBounds.Location, Point.Empty, screenBounds.Size);
            scaledGraphics.DrawImage(screenBitmap, new Rectangle(Point.Empty, scaledSize));

            var now = stopwatch.ElapsedMilliseconds;
            var durationMs = (uint)Math.Max(1, now - lastFrameAt);
            lastFrameAt = now;

            var buffer = CopyBitmapToBgrBuffer(scaledBitmap);
            try
            {
                PublishRawFrame(durationMs, scaledBitmap.Width, scaledBitmap.Height, buffer, VideoPixelFormatsEnum.Bgr);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ReportSourceError($"屏幕采集推帧失败: {ex.Message}");
                return;
            }

            await Task.Delay(frameDelay, cancellationToken);
        }
    }

    private static byte[] CopyBitmapToBgrBuffer(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var bitmapData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

        try
        {
            var rowLength = bitmap.Width * 3;
            var buffer = new byte[rowLength * bitmap.Height];
            for (var row = 0; row < bitmap.Height; row++)
            {
                var sourcePtr = IntPtr.Add(bitmapData.Scan0, row * bitmapData.Stride);
                Marshal.Copy(sourcePtr, buffer, row * rowLength, rowLength);
            }

            return buffer;
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }
    }

    private static Size CalculateScaledSize(Size original, int maxWidth, int maxHeight)
    {
        if (original.Width <= maxWidth && original.Height <= maxHeight)
        {
            return original;
        }

        var widthRatio = (double)maxWidth / original.Width;
        var heightRatio = (double)maxHeight / original.Height;
        var ratio = Math.Min(widthRatio, heightRatio);
        return new Size(Math.Max(1, (int)(original.Width * ratio)), Math.Max(1, (int)(original.Height * ratio)));
    }
}
