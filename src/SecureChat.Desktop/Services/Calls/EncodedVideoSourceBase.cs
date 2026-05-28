using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.FFmpeg;

namespace SecureChat.Desktop.Services.Calls;

public abstract class EncodedVideoSourceBase : IVideoSource, IDisposable
{
    private readonly FFmpegVideoSource _encoder;
    private readonly bool _enableEncoding;
    private bool _rawFrameEncodingUnsupported;

    protected EncodedVideoSourceBase(bool enableEncoding = true)
    {
        _enableEncoding = enableEncoding;
        FfmpegBootstrap.EnsureInitialized();
        _encoder = new FFmpegVideoSource();
        if (_enableEncoding && !_encoder.SetEncoderForCodec(VideoCodecsEnum.VP8, FfmpegBootstrap.Vp8EncoderName, new Dictionary<string, string>()))
        {
            throw new InvalidOperationException("无法初始化 VP8 编码器。请确认 FFmpeg 共享库可用。");
        }

        if (_enableEncoding)
        {
            _encoder.RestrictFormats(format => format.Codec == VideoCodecsEnum.VP8);
            _encoder.OnVideoSourceEncodedSample += (duration, sample) => OnVideoSourceEncodedSample(duration, sample);
            _encoder.OnVideoSourceError += message => OnVideoSourceError(message);
        }
    }

    public event EncodedSampleDelegate OnVideoSourceEncodedSample = delegate { };
    public event RawVideoSampleDelegate OnVideoSourceRawSample = delegate { };
    public event RawVideoSampleFasterDelegate OnVideoSourceRawSampleFaster = delegate { };
    public event SourceErrorDelegate OnVideoSourceError = delegate { };

    protected void PublishRawFrame(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat)
    {
        OnVideoSourceRawSample(durationMilliseconds, width, height, sample, pixelFormat);

        if (!_enableEncoding || _rawFrameEncodingUnsupported)
        {
            return;
        }

        try
        {
            var encoderSample = sample;
            var encoderPixelFormat = pixelFormat;

            if (TryConvertToEncoderFormat(width, height, sample, pixelFormat, out var convertedSample, out var convertedPixelFormat))
            {
                encoderSample = convertedSample;
                encoderPixelFormat = convertedPixelFormat;
            }

            _encoder.ExternalVideoSourceRawSample(durationMilliseconds, width, height, encoderSample, encoderPixelFormat);
        }
        catch (NotImplementedException)
        {
            _rawFrameEncodingUnsupported = true;
            OnVideoSourceError($"当前 FFmpeg 编码后端不支持原始视频帧直送编码，像素格式={pixelFormat}，已停止发送当前视频媒体流。");
        }
    }

    private static bool TryConvertToEncoderFormat(
        int width,
        int height,
        byte[] sample,
        VideoPixelFormatsEnum pixelFormat,
        out byte[] convertedSample,
        out VideoPixelFormatsEnum convertedPixelFormat)
    {
        convertedSample = sample;
        convertedPixelFormat = pixelFormat;

        switch (pixelFormat)
        {
            case VideoPixelFormatsEnum.Bgr:
            case VideoPixelFormatsEnum.Rgb:
            case VideoPixelFormatsEnum.Bgra:
            case VideoPixelFormatsEnum.Rgba:
                convertedSample = ConvertPackedToI420(width, height, sample, pixelFormat);
                convertedPixelFormat = VideoPixelFormatsEnum.I420;
                return true;
            default:
                return false;
        }
    }

    private static byte[] ConvertPackedToI420(int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat)
    {
        var yPlaneLength = width * height;
        var chromaWidth = (width + 1) / 2;
        var chromaHeight = (height + 1) / 2;
        var uvPlaneLength = chromaWidth * chromaHeight;
        var converted = new byte[yPlaneLength + (uvPlaneLength * 2)];
        var uPlaneOffset = yPlaneLength;
        var vPlaneOffset = yPlaneLength + uvPlaneLength;

        for (var y = 0; y < height; y += 2)
        {
            for (var x = 0; x < width; x += 2)
            {
                var uAccumulator = 0;
                var vAccumulator = 0;
                var pixelCount = 0;

                for (var rowOffset = 0; rowOffset < 2 && y + rowOffset < height; rowOffset++)
                {
                    for (var columnOffset = 0; columnOffset < 2 && x + columnOffset < width; columnOffset++)
                    {
                        var pixelX = x + columnOffset;
                        var pixelY = y + rowOffset;
                        var sampleIndex = GetPackedPixelOffset(width, pixelX, pixelY, pixelFormat);
                        var (r, g, b) = ReadPackedPixel(sample, sampleIndex, pixelFormat);

                        converted[(pixelY * width) + pixelX] = ClampToByte(((66 * r) + (129 * g) + (25 * b) + 128 >> 8) + 16);
                        uAccumulator += ((-38 * r) - (74 * g) + (112 * b) + 128 >> 8) + 128;
                        vAccumulator += ((112 * r) - (94 * g) - (18 * b) + 128 >> 8) + 128;
                        pixelCount++;
                    }
                }

                var chromaIndex = ((y / 2) * chromaWidth) + (x / 2);
                converted[uPlaneOffset + chromaIndex] = ClampToByte(uAccumulator / Math.Max(1, pixelCount));
                converted[vPlaneOffset + chromaIndex] = ClampToByte(vAccumulator / Math.Max(1, pixelCount));
            }
        }

        return converted;
    }

    private static int GetPackedPixelOffset(int width, int x, int y, VideoPixelFormatsEnum pixelFormat)
    {
        var bytesPerPixel = pixelFormat switch
        {
            VideoPixelFormatsEnum.Bgr or VideoPixelFormatsEnum.Rgb => 3,
            VideoPixelFormatsEnum.Bgra or VideoPixelFormatsEnum.Rgba => 4,
            _ => throw new NotSupportedException($"不支持的打包像素格式: {pixelFormat}")
        };

        return ((y * width) + x) * bytesPerPixel;
    }

    private static (int R, int G, int B) ReadPackedPixel(byte[] sample, int offset, VideoPixelFormatsEnum pixelFormat)
    {
        return pixelFormat switch
        {
            VideoPixelFormatsEnum.Bgr => (sample[offset + 2], sample[offset + 1], sample[offset]),
            VideoPixelFormatsEnum.Rgb => (sample[offset], sample[offset + 1], sample[offset + 2]),
            VideoPixelFormatsEnum.Bgra => (sample[offset + 2], sample[offset + 1], sample[offset]),
            VideoPixelFormatsEnum.Rgba => (sample[offset], sample[offset + 1], sample[offset + 2]),
            _ => throw new NotSupportedException($"不支持的打包像素格式: {pixelFormat}")
        };
    }

    private static byte ClampToByte(int value)
    {
        if (value < 0)
        {
            return 0;
        }

        if (value > 255)
        {
            return 255;
        }

        return (byte)value;
    }

    protected void ReportSourceError(string message)
    {
        OnVideoSourceError(message);
    }

    public abstract Task PauseVideo();
    public abstract Task ResumeVideo();
    public abstract Task StartVideo();
    public abstract Task CloseVideo();

    public List<VideoFormat> GetVideoSourceFormats() => _encoder.GetVideoSourceFormats();

    public void SetVideoSourceFormat(VideoFormat videoFormat)
    {
        _encoder.SetVideoSourceFormat(videoFormat);
    }

    public void RestrictFormats(Func<VideoFormat, bool> filter)
    {
        _encoder.RestrictFormats(filter);
    }

    public void ExternalVideoSourceRawSample(uint durationMilliseconds, int width, int height, byte[] sample, VideoPixelFormatsEnum pixelFormat)
    {
        PublishRawFrame(durationMilliseconds, width, height, sample, pixelFormat);
    }

    public virtual void ExternalVideoSourceRawSampleFaster(uint durationMilliseconds, RawImage rawImage)
    {
        throw new NotSupportedException("RawImage path is not used by this source.");
    }

    public void ForceKeyFrame()
    {
        _encoder.ForceKeyFrame();
    }

    public bool HasEncodedVideoSubscribers() => _encoder.HasEncodedVideoSubscribers();

    public abstract bool IsVideoSourcePaused();

    public virtual void Dispose()
    {
        _encoder.Dispose();
    }
}
