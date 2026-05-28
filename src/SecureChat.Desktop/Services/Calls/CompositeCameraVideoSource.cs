using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;

namespace SecureChat.Desktop.Services.Calls;

public sealed class CompositeCameraVideoSource : IVideoSource, IDisposable
{
    private readonly FFmpegCameraSource _encoderSource;
    private readonly OpenCvCameraVideoSource _previewSource;

    public CompositeCameraVideoSource(string cameraDevicePath, int cameraIndex, int width = 1280, int height = 720, int fps = 15)
    {
        _encoderSource = new FFmpegCameraSource(cameraDevicePath);
        _encoderSource.RestrictFormats(format => format.Codec == VideoCodecsEnum.VP8);
        if (!_encoderSource.SetEncoderForCodec(VideoCodecsEnum.VP8, FfmpegBootstrap.Vp8EncoderName, new Dictionary<string, string>()))
        {
            _encoderSource.Dispose();
            throw new InvalidOperationException("无法初始化摄像头 VP8 编码器。请确认 FFmpeg 共享库可用。");
        }

        _encoderSource.SetVideoEncoderBitrate(1_500_000, fps, null, null);
        _previewSource = new OpenCvCameraVideoSource(cameraIndex, width, height, fps, enableEncoding: false);

        _encoderSource.OnVideoSourceEncodedSample += HandleEncodedSample;
        _encoderSource.OnVideoSourceError += HandleSourceError;
        _previewSource.OnVideoSourceRawSample += HandleRawSample;
        _previewSource.OnVideoSourceError += HandlePreviewError;
    }

    public event EncodedSampleDelegate OnVideoSourceEncodedSample = delegate { };
    public event RawVideoSampleDelegate OnVideoSourceRawSample = delegate { };
    public event RawVideoSampleFasterDelegate OnVideoSourceRawSampleFaster = delegate { };
    public event SourceErrorDelegate OnVideoSourceError = delegate { };

    public Task PauseVideo()
    {
        return Task.WhenAll(_encoderSource.PauseVideo(), _previewSource.PauseVideo());
    }

    public Task ResumeVideo()
    {
        return Task.WhenAll(_encoderSource.ResumeVideo(), _previewSource.ResumeVideo());
    }

    public async Task StartVideo()
    {
        await _encoderSource.StartVideo().ConfigureAwait(false);

        try
        {
            await _previewSource.StartVideo().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            OnVideoSourceError($"本地摄像头预览启动失败，但编码发送仍会继续: {ex.Message}");
        }
    }

    public async Task CloseVideo()
    {
        Exception? encoderCloseError = null;

        try
        {
            await _previewSource.CloseVideo().ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await _encoderSource.CloseVideo().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                encoderCloseError = ex;
            }
        }

        if (encoderCloseError is not null)
        {
            throw encoderCloseError;
        }
    }

    public List<VideoFormat> GetVideoSourceFormats() => _encoderSource.GetVideoSourceFormats();

    public void SetVideoSourceFormat(VideoFormat videoFormat)
    {
        _encoderSource.SetVideoSourceFormat(videoFormat);
    }

    public void RestrictFormats(Func<VideoFormat, bool> filter)
    {
        _encoderSource.RestrictFormats(filter);
    }

    public void ExternalVideoSourceRawSample(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat)
    {
        throw new NotSupportedException("Composite camera source does not accept external raw samples.");
    }

    public void ExternalVideoSourceRawSampleFaster(uint durationMilliseconds, RawImage rawImage)
    {
        throw new NotSupportedException("Composite camera source does not accept external raw samples.");
    }

    public void ForceKeyFrame()
    {
        _encoderSource.ForceKeyFrame();
    }

    public bool HasEncodedVideoSubscribers() => _encoderSource.HasEncodedVideoSubscribers();

    public bool IsVideoSourcePaused() => _encoderSource.IsVideoSourcePaused();

    public void Dispose()
    {
        _encoderSource.OnVideoSourceEncodedSample -= HandleEncodedSample;
        _encoderSource.OnVideoSourceError -= HandleSourceError;
        _previewSource.OnVideoSourceRawSample -= HandleRawSample;
        _previewSource.OnVideoSourceError -= HandlePreviewError;
        _previewSource.Dispose();
        _encoderSource.Dispose();
    }

    private void HandleEncodedSample(uint durationRtpUnits, byte[] sample)
    {
        OnVideoSourceEncodedSample(durationRtpUnits, sample);
    }

    private void HandleRawSample(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat)
    {
        OnVideoSourceRawSample(durationMilliseconds, width, height, sample, pixelFormat);
    }

    private void HandleSourceError(string message)
    {
        OnVideoSourceError(message);
    }

    private void HandlePreviewError(string message)
    {
        OnVideoSourceError($"本地预览: {message}");
    }
}