using SIPSorceryMedia.Abstractions;

namespace SecureChat.Desktop.Services.Calls;

public sealed class SilentAudioSource : IAudioSource, IDisposable
{
    private const int SampleRate = 48000;
    private const int Channels = 1;
    private const int OpusPayloadType = 111;

    private readonly List<AudioFormat> _audioFormats =
    [
        new AudioFormat(AudioCodecsEnum.OPUS, OpusPayloadType, SampleRate, SampleRate, Channels, "minptime=20;useinbandfec=1")
    ];

    private AudioFormat _selectedFormat;
    private bool _paused;

    public SilentAudioSource()
    {
        _selectedFormat = _audioFormats[0];
    }

    public event EncodedSampleDelegate OnAudioSourceEncodedSample = delegate { };
    public event Action<EncodedAudioFrame> OnAudioSourceEncodedFrameReady = delegate { };
    public event RawAudioSampleDelegate OnAudioSourceRawSample = delegate { };
    public event SourceErrorDelegate OnAudioSourceError = delegate { };

    public Task PauseAudio()
    {
        _paused = true;
        return Task.CompletedTask;
    }

    public Task ResumeAudio()
    {
        _paused = false;
        return Task.CompletedTask;
    }

    public Task StartAudio() => Task.CompletedTask;

    public Task CloseAudio() => Task.CompletedTask;

    public List<AudioFormat> GetAudioSourceFormats() => [.. _audioFormats];

    public void SetAudioSourceFormat(AudioFormat audioFormat)
    {
        _selectedFormat = audioFormat;
    }

    public void RestrictFormats(Func<AudioFormat, bool> filter)
    {
        _audioFormats.RemoveAll(format => !filter(format));
        if (_audioFormats.Count > 0 && !_audioFormats.Any(format => format.FormatID == _selectedFormat.FormatID))
        {
            _selectedFormat = _audioFormats[0];
        }
    }

    public void ExternalAudioSourceRawSample(AudioSamplingRatesEnum samplingRate, uint durationMilliseconds, short[] sample)
    {
    }

    public bool HasEncodedAudioSubscribers() => !_paused;

    public bool IsAudioSourcePaused() => _paused;

    public void Dispose()
    {
    }
}