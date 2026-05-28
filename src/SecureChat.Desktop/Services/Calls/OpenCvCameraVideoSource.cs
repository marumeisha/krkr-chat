using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenCvSharp;
using SIPSorceryMedia.Abstractions;

namespace SecureChat.Desktop.Services.Calls;

public sealed class OpenCvCameraVideoSource : EncodedVideoSourceBase
{
    private readonly int _cameraIndex;
    private readonly int _width;
    private readonly int _height;
    private readonly int _fps;
    private VideoCapture? _capture;
    private CancellationTokenSource? _captureLoopCts;
    private Task? _captureLoopTask;
    private volatile bool _paused;

    public OpenCvCameraVideoSource(int cameraIndex = 0, int width = 1280, int height = 720, int fps = 15, bool enableEncoding = true)
        : base(enableEncoding)
    {
        _cameraIndex = cameraIndex;
        _width = width;
        _height = height;
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

        _capture = new VideoCapture(_cameraIndex, VideoCaptureAPIs.DSHOW);
        if (!_capture.IsOpened())
        {
            _capture.Open(_cameraIndex);
        }

        if (!_capture.IsOpened())
        {
            throw new InvalidOperationException("无法打开摄像头，请确认设备没有被其他程序占用。");
        }

        _capture.FrameWidth = _width;
        _capture.FrameHeight = _height;
        _capture.Fps = _fps;
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
        }

        _captureLoopTask = null;
        _captureLoopCts?.Dispose();
        _captureLoopCts = null;
        _capture?.Dispose();
        _capture = null;
    }

    public override bool IsVideoSourcePaused() => _paused;

    public override void Dispose()
    {
        _captureLoopCts?.Cancel();
        _capture?.Dispose();
        _captureLoopCts?.Dispose();
        base.Dispose();
    }

    private async Task CaptureLoopAsync(CancellationToken cancellationToken)
    {
        using var frame = new Mat();
        using var bgrFrame = new Mat();
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

            if (_capture is null || !_capture.Read(frame) || frame.Empty())
            {
                await Task.Delay(frameDelay, cancellationToken);
                continue;
            }

            var current = stopwatch.ElapsedMilliseconds;
            var durationMs = (uint)Math.Max(1, current - lastFrameAt);
            lastFrameAt = current;

            if (frame.Channels() == 3)
            {
                frame.CopyTo(bgrFrame);
            }
            else
            {
                Cv2.CvtColor(frame, bgrFrame, ColorConversionCodes.BGRA2BGR);
            }

            var byteCount = (int)(bgrFrame.Total() * bgrFrame.ElemSize());
            var buffer = new byte[byteCount];
            Marshal.Copy(bgrFrame.Data, buffer, 0, byteCount);
            PublishRawFrame(durationMs, bgrFrame.Width, bgrFrame.Height, buffer, VideoPixelFormatsEnum.Bgr);

            await Task.Delay(frameDelay, cancellationToken);
        }
    }
}
