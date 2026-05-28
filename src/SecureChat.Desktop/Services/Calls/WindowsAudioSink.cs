using Concentus;
using Concentus.Structs;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using SIPSorceryMedia.Abstractions;

namespace SecureChat.Desktop.Services.Calls;

public sealed class WindowsAudioSink : IAudioSink, IDisposable
{
    private const int Channels = 1;
    private const int SampleRate = 48000;
    private const int BitsPerSample = 16;
    private const int MaxSamplesPerDecode = 5760;
    private const int OpusPayloadType = 111;

    private readonly List<AudioFormat> _audioFormats = new()
    {
        new AudioFormat(AudioCodecsEnum.OPUS, OpusPayloadType, SampleRate, SampleRate, Channels, "minptime=20;useinbandfec=1")
    };
    private readonly IOpusDecoder _decoder = OpusCodecFactory.CreateDecoder(SampleRate, Channels, null);
    private readonly BufferedWaveProvider _bufferedWaveProvider = new(new WaveFormat(SampleRate, BitsPerSample, Channels))
    {
        BufferDuration = TimeSpan.FromSeconds(2),
        DiscardOnBufferOverflow = true
    };
    private readonly IWavePlayer _playbackDevice;

    private AudioFormat _selectedFormat;
    private bool _disposed;
    private bool _isStarted;

    public WindowsAudioSink()
    {
        _selectedFormat = _audioFormats[0];
        _playbackDevice = CreatePlaybackDevice();
        _playbackDevice.Init(_bufferedWaveProvider);
    }

    public event SourceErrorDelegate OnAudioSinkError = delegate { };

    public List<AudioFormat> GetAudioSinkFormats() => [.. _audioFormats];

    public void SetAudioSinkFormat(AudioFormat audioFormat)
    {
        _selectedFormat = audioFormat;
    }

    public void GotAudioRtp(System.Net.IPEndPoint remoteEndPoint, uint ssrc, uint seqnum, uint timestamp, int payloadID, bool marker, byte[] payload)
    {
        if (payload.Length == 0)
        {
            return;
        }

        if (_selectedFormat.FormatID != payloadID)
        {
            return;
        }

        DecodeAndPlay(payload, _selectedFormat);
    }

    public void GotEncodedMediaFrame(EncodedAudioFrame encodedMediaFrame)
    {
        DecodeAndPlay(encodedMediaFrame.EncodedAudio, encodedMediaFrame.AudioFormat);
    }

    public void RestrictFormats(Func<AudioFormat, bool> filter)
    {
        _audioFormats.RemoveAll(format => !filter(format));
        if (_audioFormats.Count > 0 && !_audioFormats.Any(format => format.FormatID == _selectedFormat.FormatID))
        {
            _selectedFormat = _audioFormats[0];
        }
    }

    public Task PauseAudioSink()
    {
        _playbackDevice.Pause();
        return Task.CompletedTask;
    }

    public Task ResumeAudioSink()
    {
        if (_isStarted)
        {
            _playbackDevice.Play();
        }

        return Task.CompletedTask;
    }

    public Task StartAudioSink()
    {
        ThrowIfDisposed();
        if (_isStarted)
        {
            return Task.CompletedTask;
        }

        _playbackDevice.Play();
        _isStarted = true;
        return Task.CompletedTask;
    }

    public Task CloseAudioSink()
    {
        if (!_isStarted)
        {
            return Task.CompletedTask;
        }

        _playbackDevice.Stop();
        _bufferedWaveProvider.ClearBuffer();
        _isStarted = false;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _playbackDevice.Dispose();
        _decoder.Dispose();
    }

    private void DecodeAndPlay(byte[] encodedAudio, AudioFormat audioFormat)
    {
        try
        {
            if (audioFormat.Codec != AudioCodecsEnum.OPUS)
            {
                return;
            }

            var pcm = new short[MaxSamplesPerDecode * Channels];
            var decodedSamples = _decoder.Decode(encodedAudio, pcm, MaxSamplesPerDecode, false);
            if (decodedSamples <= 0)
            {
                return;
            }

            var bytes = new byte[decodedSamples * Channels * (BitsPerSample / 8)];
            Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
            _bufferedWaveProvider.AddSamples(bytes, 0, bytes.Length);
        }
        catch (Exception ex)
        {
            OnAudioSinkError($"音频解码失败: {ex.Message}");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static IWavePlayer CreatePlaybackDevice()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var endpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Communications);
            return new WasapiOut(endpoint, AudioClientShareMode.Shared, true, 20);
        }
        catch
        {
            return new WaveOutEvent();
        }
    }
}